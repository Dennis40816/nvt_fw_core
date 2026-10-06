// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Nvt.Core.Files;
using Xunit;

namespace Nvt.Core.Tests.Files;

/// <summary>Tests rooted file admission, caller ceilings, complete hashes, and exception order.</summary>
public sealed class BoundedFileReaderPathTests
{
    /// <summary>The file reader returns authoritative length, hash, and captured bytes.</summary>
    [Fact]
    public async Task ReadFileAsyncReturnsLengthHashAndBytes()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.Write("input.bin", [1, 2, 3, 4]);
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);

        BoundedReadResult result = await BoundedFileReader.ReadFileAsync(
            path, [root], 4, FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Length);
        Assert.Equal(SHA256.HashData([1, 2, 3, 4]), result.Sha256);
        Assert.Equal([1, 2, 3, 4], result.Bytes);
    }

    /// <summary>Both modes reject a measured length above the caller-resolved ceiling.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncRejectsFileAboveResolvedMaximum(FileCaptureMode mode)
    {
        using var workspace = new TestWorkspace();
        string path = workspace.Write("oversized.bin", [1, 2, 3, 4]);
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);

        FileSizeLimitExceededException exception = await Assert.ThrowsAsync<FileSizeLimitExceededException>(() =>
            BoundedFileReader.ReadFileAsync(path, [root], 3, mode, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(4, exception.ObservedBytes);
        Assert.Equal(3, exception.MaximumBytes);
        Assert.False(exception.IsCaptureStorageLimit);
        Assert.Equal("File length 4 exceeds the resolved maximum 3 bytes.", exception.Message);
    }

    /// <summary>The 100,000,001-byte sparse file is rejected before capture materialization.</summary>
    [Fact]
    public async Task ReadFileAsyncRejectsHundredMegabyteOverflowBeforeMaterialization()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.PathFor("oversized-sparse.bin");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(100_000_001);
        }
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);

        FileSizeLimitExceededException exception = await Assert.ThrowsAsync<FileSizeLimitExceededException>(() =>
            BoundedFileReader.ReadFileAsync(path, [root], 100_000_000,
                FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(100_000_001, exception.ObservedBytes);
        Assert.Equal(100_000_000, exception.MaximumBytes);
    }

    /// <summary>Same-size mutation between complete reads changes the hash in both modes.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncDetectsSameSizeMutation(FileCaptureMode mode)
    {
        using var workspace = new TestWorkspace();
        string path = workspace.Write("input.bin", [1, 2, 3, 4]);
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);
        BoundedReadResult first = await BoundedFileReader.ReadFileAsync(
            path, [root], int.MaxValue, mode, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, [1, 2, 9, 4], TestContext.Current.CancellationToken);

        BoundedReadResult second = await BoundedFileReader.ReadFileAsync(
            path, [root], int.MaxValue, mode, TestContext.Current.CancellationToken);

        Assert.Equal(first.Length, second.Length);
        Assert.NotEqual(first.Sha256, second.Sha256);
        Assert.Equal(4, second.Length);
        Assert.Equal(SHA256.HashData([1, 2, 9, 4]), second.Sha256);
        if (mode == FileCaptureMode.CaptureBytes)
        {
            Assert.Equal([1, 2, 3, 4], first.Bytes);
            Assert.Equal([1, 2, 9, 4], second.Bytes);
        }
        else
        {
            Assert.Null(first.Bytes);
            Assert.Null(second.Bytes);
        }
    }

    /// <summary>Both modes reject a selected file outside the configured roots.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncRejectsPathOutsideAllowedRoot(FileCaptureMode mode)
    {
        using var workspace = new TestWorkspace();
        string allowed = RootedPathGuard.ResolveRoot(workspace.PathFor("allowed"));
        string outside = Directory.CreateDirectory(workspace.PathFor("outside")).FullName;
        string path = Path.Combine(outside, "input.bin");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            BoundedFileReader.ReadFileAsync(path, [allowed], int.MaxValue,
                mode, TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>Both modes hash the complete empty or multi-buffer file and only capture retains bytes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(131089)]
    public async Task ReadFileAsyncHashesCompleteFileWithExplicitPayloadMode(int length)
    {
        using var workspace = new TestWorkspace();
        byte[] bytes = [.. Enumerable.Range(0, length).Select(static index => (byte)(index % 251))];
        string path = workspace.Write("boundary.bin", bytes);
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);
        byte[] expected = SHA256.HashData(bytes);

        BoundedReadResult identity = await BoundedFileReader.ReadFileAsync(
            path, [root], long.MaxValue, FileCaptureMode.IdentityOnly, TestContext.Current.CancellationToken);
        BoundedReadResult capture = await BoundedFileReader.ReadFileAsync(
            path, [root], long.MaxValue, FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken);

        Assert.Equal(length, identity.Length);
        Assert.Equal(expected, identity.Sha256);
        Assert.Null(identity.Bytes);
        Assert.Equal(length, capture.Length);
        Assert.Equal(expected, capture.Sha256);
        Assert.Equal(bytes, capture.Bytes);
    }

    /// <summary>Undefined modes fail before missing-path access, matching the frozen path test.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task ReadFileAsyncRejectsInvalidModeBeforeAccessingPath(int modeValue)
    {
        using var workspace = new TestWorkspace();
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedFileReader.ReadFileAsync(workspace.PathFor("missing.bin"), [root], long.MaxValue,
                (FileCaptureMode)modeValue, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("mode", exception.ParamName);
    }

    /// <summary>Pre-cancelled admission fails before missing-path access in either mode.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncPropagatesCancellationBeforeAccessingPath(FileCaptureMode mode)
    {
        using var workspace = new TestWorkspace();
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadFileAsync(workspace.PathFor("missing.bin"), [root], long.MaxValue,
                mode, cancellation.Token).AsTask());
    }

    /// <summary>Zero and negative ceilings precede undefined mode, cancellation, and invalid path checks.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public async Task ReadFileAsyncRejectsNonPositiveMaximumFirst(long maximumBytes)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedFileReader.ReadFileAsync(null!, [], maximumBytes, (FileCaptureMode)(-1), cancellation.Token).AsTask());
        Assert.Equal("maximumBytes", exception.ParamName);
    }

    /// <summary>Mode precedes cancellation, cancellation precedes invalid path, and path precedes roots.</summary>
    [Fact]
    public async Task ReadFileAsyncPreservesValidationOrder()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        ArgumentOutOfRangeException mode = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedFileReader.ReadFileAsync(null!, [], 1, (FileCaptureMode)2, cancellation.Token).AsTask());
        Assert.Equal("mode", mode.ParamName);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadFileAsync(null!, [], 1, FileCaptureMode.IdentityOnly, cancellation.Token).AsTask());
        ArgumentNullException path = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BoundedFileReader.ReadFileAsync(null!, [], 1, FileCaptureMode.IdentityOnly,
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("path", path.ParamName);
    }

    /// <summary>Null roots use the existing parent directory and admit exact size in both modes.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncUsesExistingParentForNullRoots(FileCaptureMode mode)
    {
        using var workspace = new TestWorkspace();
        string path = workspace.Write("nested/input.bin", [1, 2, 3, 4]);

        BoundedReadResult result = await BoundedFileReader.ReadFileAsync(
            path, null, 4, mode, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Length);
        Assert.Equal(SHA256.HashData([1, 2, 3, 4]), result.Sha256);
        if (mode == FileCaptureMode.CaptureBytes)
        {
            Assert.Equal([1, 2, 3, 4], result.Bytes);
        }
        else
        {
            Assert.Null(result.Bytes);
        }
    }

    /// <summary>The smallest positive ceiling admits empty and exact content and rejects one extra byte.</summary>
    [Theory]
    [InlineData(0, FileCaptureMode.IdentityOnly)]
    [InlineData(1, FileCaptureMode.IdentityOnly)]
    [InlineData(2, FileCaptureMode.IdentityOnly)]
    [InlineData(0, FileCaptureMode.CaptureBytes)]
    [InlineData(1, FileCaptureMode.CaptureBytes)]
    [InlineData(2, FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncEnforcesSmallestPositiveMaximum(int length, FileCaptureMode mode)
    {
        using var workspace = new TestWorkspace();
        byte[] bytes = new byte[length];
        string path = workspace.Write("input.bin", bytes);
        if (length > 1)
        {
            FileSizeLimitExceededException exception = await Assert.ThrowsAsync<FileSizeLimitExceededException>(() =>
                BoundedFileReader.ReadFileAsync(path, null, 1, mode, TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(2, exception.ObservedBytes);
            Assert.Equal(1, exception.MaximumBytes);
            Assert.False(exception.IsCaptureStorageLimit);
        }
        else
        {
            BoundedReadResult result = await BoundedFileReader.ReadFileAsync(
                path, null, 1, mode, TestContext.Current.CancellationToken);
            Assert.Equal(length, result.Length);
            Assert.Equal(SHA256.HashData(bytes), result.Sha256);
            Assert.Equal(mode == FileCaptureMode.CaptureBytes ? bytes : null, result.Bytes);
        }
    }

    /// <summary>The fixed host ceiling remains an explicit caller value, tested below, at, and above its boundary.</summary>
    [Theory]
    [InlineData(99_999_999, FileCaptureMode.IdentityOnly)]
    [InlineData(100_000_000, FileCaptureMode.IdentityOnly)]
    [InlineData(100_000_001, FileCaptureMode.IdentityOnly)]
    [InlineData(99_999_999, FileCaptureMode.CaptureBytes)]
    [InlineData(100_000_000, FileCaptureMode.CaptureBytes)]
    [InlineData(100_000_001, FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncEnforcesHundredMegabyteBoundary(int length, FileCaptureMode mode)
    {
        const long maximum = 100_000_000;
        using var workspace = new TestWorkspace();
        string path = workspace.PathFor("sparse.bin");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(length);
        }

        if (length > maximum)
        {
            FileSizeLimitExceededException exception = await Assert.ThrowsAsync<FileSizeLimitExceededException>(() =>
                BoundedFileReader.ReadFileAsync(path, null, maximum, mode, TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(length, exception.ObservedBytes);
            Assert.Equal(maximum, exception.MaximumBytes);
            Assert.False(exception.IsCaptureStorageLimit);
        }
        else
        {
            BoundedReadResult result = await BoundedFileReader.ReadFileAsync(
                path, null, maximum, mode, TestContext.Current.CancellationToken);
            Assert.Equal(length, result.Length);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] zeros = new byte[64 * 1024];
            for (long offset = 0; offset < length; offset += zeros.Length)
            {
                hash.AppendData(zeros.AsSpan(0, (int)Math.Min(zeros.Length, length - offset)));
            }
            Assert.Equal(hash.GetHashAndReset(), result.Sha256);
            if (mode == FileCaptureMode.CaptureBytes)
            {
                Assert.NotNull(result.Bytes);
                Assert.Equal(length, result.Bytes.Length);
                Assert.DoesNotContain(result.Bytes, static value => value != 0);
            }
            else
            {
                Assert.Null(result.Bytes);
            }
        }
    }

    /// <summary>Files below, at, and above the fixed 64 KiB buffer are read completely in both modes.</summary>
    [Theory]
    [InlineData(65535, FileCaptureMode.IdentityOnly)]
    [InlineData(65536, FileCaptureMode.IdentityOnly)]
    [InlineData(65537, FileCaptureMode.IdentityOnly)]
    [InlineData(65535, FileCaptureMode.CaptureBytes)]
    [InlineData(65536, FileCaptureMode.CaptureBytes)]
    [InlineData(65537, FileCaptureMode.CaptureBytes)]
    public async Task ReadFileAsyncHashesBufferBoundaries(int length, FileCaptureMode mode)
    {
        using var workspace = new TestWorkspace();
        byte[] bytes = [.. Enumerable.Range(0, length).Select(static index => (byte)(index % 251))];
        string path = workspace.Write("boundary.bin", bytes);

        BoundedReadResult result = await BoundedFileReader.ReadFileAsync(
            path, null, length, mode, TestContext.Current.CancellationToken);

        Assert.Equal(length, result.Length);
        Assert.Equal(SHA256.HashData(bytes), result.Sha256);
        Assert.Equal(mode == FileCaptureMode.CaptureBytes ? bytes : null, result.Bytes);
    }

    /// <summary>A supplied list is not resolved again, even if an unused root no longer exists.</summary>
    [Fact]
    public async Task ReadFileAsyncUsesNonNullRootsAsSupplied()
    {
        using var workspace = new TestWorkspace();
        string unused = RootedPathGuard.ResolveRoot(workspace.PathFor("unused"));
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);
        Directory.Delete(unused);
        string path = workspace.Write("input.bin", [1]);

        BoundedReadResult result = await BoundedFileReader.ReadFileAsync(
            path, [unused, root], 1, FileCaptureMode.IdentityOnly, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Length);
    }

    /// <summary>Empty roots and null-root missing parents retain different admission errors.</summary>
    [Fact]
    public async Task ReadFileAsyncRejectsEmptyRootsAndMissingParents()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.PathFor("missing/input.bin");
        InvalidOperationException roots = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedFileReader.ReadFileAsync(path, [], 1, FileCaptureMode.IdentityOnly,
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("At least one allowed root is required.", roots.Message);
        _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            BoundedFileReader.ReadFileAsync(path, null, 1, FileCaptureMode.IdentityOnly,
                TestContext.Current.CancellationToken).AsTask());
        _ = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            BoundedFileReader.ReadFileAsync(workspace.PathFor("missing.bin"), null, 1,
                FileCaptureMode.IdentityOnly, TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>The owned file stream is closed on success and on a caller-limit rejection.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReadFileAsyncDisposesStream(int maximumBytes)
    {
        using var workspace = new TestWorkspace();
        string path = workspace.Write("input.bin", [1, 2]);
        if (maximumBytes == 1)
        {
            _ = await Assert.ThrowsAsync<FileSizeLimitExceededException>(() =>
                BoundedFileReader.ReadFileAsync(path, null, maximumBytes, FileCaptureMode.CaptureBytes,
                    TestContext.Current.CancellationToken).AsTask());
        }
        else
        {
            _ = await BoundedFileReader.ReadFileAsync(path, null, maximumBytes,
                FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken);
        }

        await using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(2, exclusive.Length);
    }
}
