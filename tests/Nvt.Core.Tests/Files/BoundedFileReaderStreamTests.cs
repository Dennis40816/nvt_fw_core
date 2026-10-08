// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Nvt.Core.Files;
using Xunit;

namespace Nvt.Core.Tests.Files;

/// <summary>Tests complete stream hashing, optional capture, and bounded change detection.</summary>
public sealed class BoundedFileReaderStreamTests
{
    /// <summary>The default result has no hash or content and advertises the nullable hash contract.</summary>
    [Fact]
    public void DefaultResultHasNoIdentity()
    {
        BoundedReadResult result = default;
        Assert.Equal(0, result.Length);
        Assert.Null(result.Sha256);
        Assert.Null(result.Bytes);
        var property = typeof(BoundedReadResult).GetProperty(nameof(BoundedReadResult.Sha256));
        Assert.NotNull(property);
        Assert.Equal(System.Reflection.NullabilityState.Nullable,
            new System.Reflection.NullabilityInfoContext().Create(property).ReadState);
    }

    /// <summary>Successful empty reads supply the complete hash in both capture modes.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task SuccessfulEmptyReadHasIdentity(FileCaptureMode mode)
    {
        using var stream = new MemoryStream();
        BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
            stream, 0, mode, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Sha256);
        Assert.Equal(SHA256.HashData(Array.Empty<byte>()), result.Sha256);
        if (mode == FileCaptureMode.CaptureBytes)
        {
            Assert.NotNull(result.Bytes);
            Assert.Empty(result.Bytes);
        }
        else
        {
            Assert.Null(result.Bytes);
        }
    }

    /// <summary>Growth is rejected after reading only one byte beyond the measured length.</summary>
    [Fact]
    public async Task ReadAndHashAsyncRejectsGrowthWithOneByteProbe()
    {
        await using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        FileChangedDuringReadException exception = await Assert.ThrowsAsync<FileChangedDuringReadException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, observedLength: 4, FileCaptureMode.IdentityOnly,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FileChangeKind.Growth, exception.ChangeKind);
        Assert.Equal(5, stream.Position);
    }

    /// <summary>Early end of stream is a typed content change.</summary>
    [Fact]
    public async Task ReadAndHashAsyncRejectsShortReadAsContentChange()
    {
        await using var stream = new MemoryStream([1, 2, 3]);

        FileChangedDuringReadException exception = await Assert.ThrowsAsync<FileChangedDuringReadException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, observedLength: 4, FileCaptureMode.IdentityOnly,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FileChangeKind.ShortRead, exception.ChangeKind);
    }

    /// <summary>A cancelled token propagates cancellation rather than a content change.</summary>
    [Fact]
    public async Task ReadAndHashAsyncPropagatesCancellationForCancelledToken()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await using var stream = new MemoryStream([1, 2, 3, 4]);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, observedLength: 4, FileCaptureMode.IdentityOnly,
                cancellation.Token).AsTask());
    }

    /// <summary>Partial positive reads continue across the buffer boundary until all content is hashed.</summary>
    [Fact]
    public async Task ReadAndHashAsyncAccumulatesPartialReadsAcrossBufferBoundary()
    {
        const int length = 65553;
        await using var stream = new GeneratedReadStream(length, maximumReadSize: 17);
        byte[] expectedBytes = new byte[length];
        Array.Fill(expectedBytes, (byte)0xA5);

        BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
            stream, length, FileCaptureMode.IdentityOnly, TestContext.Current.CancellationToken);

        Assert.Equal(length, result.Length);
        Assert.Equal(SHA256.HashData(expectedBytes), result.Sha256);
        Assert.Null(result.Bytes);
        Assert.Equal(length, stream.Position);
        Assert.Equal(length, stream.TotalBytesRead);
        Assert.True(stream.MaximumRequested <= 65536);
    }

    /// <summary>Identity reads hash more than 2 and 4 GiB against fixed SHA-256 values without keeping content.</summary>
    [Theory]
    [InlineData(2147483665L, "f650a6f46439a9d3cf3d83254cff0321a139e94bc9dbdf280da93df545f0a196")]
    [InlineData(4294967313L, "f8378e7969a7ef05ad12276fed64adf6cbcf7e31ec74249a7b6f0b84d4caf32e")]
    public async Task ReadAndHashAsyncUsesLongCountersWithoutPayload(long length, string expectedSha256)
    {
        await using var stream = new GeneratedReadStream(length);

        BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
            stream, length, FileCaptureMode.IdentityOnly, TestContext.Current.CancellationToken);

        Assert.Equal(length, result.Length);
        Assert.Null(result.Bytes);
        Assert.NotNull(result.Sha256);
        Assert.Equal(expectedSha256, Convert.ToHexString(result.Sha256).ToLowerInvariant());
        Assert.Equal(length, stream.Position);
        Assert.Equal(length, stream.TotalBytesRead);
        Assert.Equal(((length + 65535) / 65536) + 1, stream.ReadCalls);
        Assert.True(stream.MaximumRequested <= 65536);
    }

    /// <summary>An oversized capture request is rejected before any content read.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAndHashAsyncRejectsUnrepresentableCaptureBeforeReading(bool useArrayBoundary)
    {
        long length = useArrayBoundary ? (long)Array.MaxLength + 1 : (long)int.MaxValue + 1;
        await using var stream = new GeneratedReadStream(length);

        FileSizeLimitExceededException exception = await Assert.ThrowsAsync<FileSizeLimitExceededException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, length, FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(length, exception.ObservedBytes);
        Assert.Equal(Array.MaxLength, exception.MaximumBytes);
        Assert.True(exception.IsCaptureStorageLimit);
        Assert.Equal(0, stream.ReadCalls);
    }

    /// <summary>Capture keeps and hashes every byte when each read returns at most 17 bytes.</summary>
    [Fact]
    public async Task ReadAndHashAsyncCapturesPartialReads()
    {
        const int length = 53;
        await using var stream = new GeneratedReadStream(length, maximumReadSize: 17);
        byte[] expectedBytes = new byte[length];
        Array.Fill(expectedBytes, (byte)0xA5);

        BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
            stream, length, FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken);

        Assert.Equal(length, result.Length);
        Assert.Equal(expectedBytes, result.Bytes);
        Assert.Equal(SHA256.HashData(expectedBytes), result.Sha256);
        Assert.Equal(length, stream.TotalBytesRead);
        Assert.Equal(5, stream.ReadCalls);
        Assert.Equal(length, stream.Position);
    }

    /// <summary>A changed final position rejects both modes without returning a result.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncRejectsFinalPositionChange(FileCaptureMode mode)
    {
        const long length = 53;
        await using var stream = new GeneratedReadStream(length);
        stream.AfterRead = (source, read) =>
        {
            if (read == 0)
            {
                source.Position = length - 1;
            }
        };

        FileChangedDuringReadException exception = await Assert.ThrowsAsync<FileChangedDuringReadException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, length, mode, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FileChangeKind.PositionChanged, exception.ChangeKind);
        Assert.Equal(length, stream.TotalBytesRead);
        Assert.Equal(2, stream.ReadCalls);
    }

    /// <summary>Non-seekable streams are read without accessing their unsupported length or position.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncSupportsNonSeekableStream(FileCaptureMode mode)
    {
        const int length = 53;
        await using var stream = new GeneratedReadStream(length, canSeek: false);
        byte[] expectedBytes = new byte[length];
        Array.Fill(expectedBytes, (byte)0xA5);

        BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
            stream, length, mode, TestContext.Current.CancellationToken);

        Assert.False(stream.CanSeek);
        Assert.Equal(length, result.Length);
        Assert.Equal(SHA256.HashData(expectedBytes), result.Sha256);
        Assert.Equal(length, stream.TotalBytesRead);
        Assert.Equal(2, stream.ReadCalls);
        if (mode == FileCaptureMode.CaptureBytes)
        {
            Assert.Equal(expectedBytes, result.Bytes);
        }
        else
        {
            Assert.Null(result.Bytes);
        }
    }

    /// <summary>Growth after a buffer read rejects both modes with only a one-byte trailing probe.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncRejectsGrowthDuringRead(FileCaptureMode mode)
    {
        const long length = 65553;
        await using var stream = new GeneratedReadStream(length);
        stream.AfterRead = (source, _) =>
        {
            source.ReadableLength = length + 3;
            source.ReportedLength = length + 3;
        };

        FileChangedDuringReadException exception = await Assert.ThrowsAsync<FileChangedDuringReadException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, length, mode, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FileChangeKind.Growth, exception.ChangeKind);
        Assert.Equal(length + 1, stream.Position);
    }

    /// <summary>Truncation after the first buffer produces a typed short read.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncRejectsShortReadDuringRead(FileCaptureMode mode)
    {
        const long length = 65553;
        await using var stream = new GeneratedReadStream(length);
        stream.AfterRead = (source, _) =>
        {
            source.ReadableLength = 65536;
            source.ReportedLength = 65536;
        };

        FileChangedDuringReadException exception = await Assert.ThrowsAsync<FileChangedDuringReadException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, length, mode, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FileChangeKind.ShortRead, exception.ChangeKind);
        Assert.Equal(65536, stream.Position);
    }

    /// <summary>Final length changes are rejected even when the trailing probe finds no bytes.</summary>
    [Theory]
    [InlineData(-1, FileChangeKind.Shrinkage, FileCaptureMode.IdentityOnly)]
    [InlineData(1, FileChangeKind.Growth, FileCaptureMode.IdentityOnly)]
    [InlineData(-1, FileChangeKind.Shrinkage, FileCaptureMode.CaptureBytes)]
    [InlineData(1, FileChangeKind.Growth, FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncRejectsFinalLengthChange(
        int delta, FileChangeKind kind, FileCaptureMode mode)
    {
        const long length = 65553;
        await using var stream = new GeneratedReadStream(length);
        stream.AfterRead = (source, read) =>
        {
            if (read == 0)
            {
                source.ReportedLength = length + delta;
            }
        };

        FileChangedDuringReadException exception = await Assert.ThrowsAsync<FileChangedDuringReadException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, length, mode, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(kind, exception.ChangeKind);
    }

    /// <summary>Undefined capture modes are rejected before any stream read.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task ReadAndHashAsyncRejectsInvalidModeBeforeReading(int modeValue)
    {
        var mode = (FileCaptureMode)modeValue;
        await using var stream = new GeneratedReadStream(53);

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, 53, mode, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("mode", exception.ParamName);
        Assert.Equal(0, stream.ReadCalls);
    }

    /// <summary>Cancellation before reading takes precedence over the capture storage limit.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncPropagatesCancellationBeforeReading(FileCaptureMode mode)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        long length = (long)Array.MaxLength + 1;
        await using var stream = new GeneratedReadStream(length);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadAndHashAsync(stream, length, mode, cancellation.Token).AsTask());

        Assert.Equal(0, stream.ReadCalls);
    }

    /// <summary>Cancellation during a successful read propagates before any further read.</summary>
    [Theory]
    [InlineData(FileCaptureMode.IdentityOnly)]
    [InlineData(FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncPropagatesCancellationMidRead(FileCaptureMode mode)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var stream = new GeneratedReadStream(65553);
        stream.AfterRead = (_, _) => cancellation.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadAndHashAsync(stream, stream.Length, mode, cancellation.Token).AsTask());

        Assert.Equal(1, stream.ReadCalls);
        Assert.Equal(65536, stream.Position);
    }

    /// <summary>Both modes hash empty content and a partial final buffer, while only capture keeps bytes.</summary>
    [Theory]
    [InlineData(0, FileCaptureMode.IdentityOnly)]
    [InlineData(0, FileCaptureMode.CaptureBytes)]
    [InlineData(131089, FileCaptureMode.IdentityOnly)]
    [InlineData(131089, FileCaptureMode.CaptureBytes)]
    public async Task ReadAndHashAsyncHashesCompleteStreamWithExplicitPayloadMode(int length, FileCaptureMode mode)
    {
        byte[] bytes = [.. Enumerable.Range(0, length).Select(static index => (byte)(index % 251))];
        await using var stream = new MemoryStream(bytes);

        BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
            stream, length, mode, TestContext.Current.CancellationToken);

        Assert.Equal(length, result.Length);
        Assert.Equal(SHA256.HashData(bytes), result.Sha256);
        Assert.Equal(length, stream.Position);
        if (mode == FileCaptureMode.CaptureBytes)
        {
            Assert.Equal(bytes, result.Bytes);
        }
        else
        {
            Assert.Null(result.Bytes);
        }
    }

    /// <summary>A null stream is rejected before length, mode, and cancellation checks.</summary>
    [Fact]
    public async Task ReadAndHashAsyncRejectsNullStream()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                null!, -1, (FileCaptureMode)(-1), cancellation.Token).AsTask());

        Assert.Equal("stream", exception.ParamName);
    }

    /// <summary>A negative measured length is rejected before any stream read.</summary>
    [Fact]
    public async Task ReadAndHashAsyncRejectsNegativeObservedLength()
    {
        await using var stream = new GeneratedReadStream(0);

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, -1, FileCaptureMode.IdentityOnly, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("observedLength", exception.ParamName);
        Assert.Equal(0, stream.ReadCalls);
    }

    /// <summary>Length validation precedes mode validation, and both precede cancellation.</summary>
    [Theory]
    [InlineData(-1L, -1, "observedLength")]
    [InlineData(0L, -1, "mode")]
    public async Task ReadAndHashAsyncValidatesArgumentsBeforeCancellation(
        long length, int modeValue, string expectedParameter)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await using var stream = new GeneratedReadStream(0);

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedFileReader.ReadAndHashAsync(
                stream, length, (FileCaptureMode)modeValue, cancellation.Token).AsTask());

        Assert.Equal(expectedParameter, exception.ParamName);
        Assert.Equal(0, stream.ReadCalls);
    }

    /// <summary>Size exception constructors require a nonnegative observed length and a positive maximum.</summary>
    [Theory]
    [InlineData(-1L, 1L, "observedBytes")]
    [InlineData(0L, 0L, "maximumBytes")]
    [InlineData(0L, -1L, "maximumBytes")]
    [InlineData(-1L, 0L, "observedBytes")]
    public void FileSizeLimitExceededExceptionRejectsInvalidLengths(
        long observedBytes, long maximumBytes, string expectedParameter)
    {
        ArgumentOutOfRangeException callerException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileSizeLimitExceededException(observedBytes, maximumBytes));
        ArgumentOutOfRangeException explicitCallerException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileSizeLimitExceededException(observedBytes, maximumBytes, isCaptureStorageLimit: false));
        ArgumentOutOfRangeException storageException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileSizeLimitExceededException(observedBytes, maximumBytes, isCaptureStorageLimit: true));

        Assert.Equal(expectedParameter, callerException.ParamName);
        Assert.Equal(expectedParameter, explicitCallerException.ParamName);
        Assert.Equal(expectedParameter, storageException.ParamName);
    }

    /// <summary>The two-argument size exception reports the caller limit and its exact message.</summary>
    [Fact]
    public void FileSizeLimitExceededExceptionReportsCallerLimit()
    {
        var exception = new FileSizeLimitExceededException(12, 10);

        Assert.Equal(12, exception.ObservedBytes);
        Assert.Equal(10, exception.MaximumBytes);
        Assert.False(exception.IsCaptureStorageLimit);
        Assert.Equal("File length 12 exceeds the resolved maximum 10 bytes.", exception.Message);
    }

    /// <summary>The explicit size exception flag selects the exact caller or capture storage message.</summary>
    [Theory]
    [InlineData(false, "File length 12 exceeds the resolved maximum 10 bytes.")]
    [InlineData(true, "File length 12 exceeds the capture storage limit 10 bytes.")]
    public void FileSizeLimitExceededExceptionReportsExplicitLimit(bool isCaptureStorageLimit, string expectedMessage)
    {
        var exception = new FileSizeLimitExceededException(12, 10, isCaptureStorageLimit);

        Assert.Equal(12, exception.ObservedBytes);
        Assert.Equal(10, exception.MaximumBytes);
        Assert.Equal(isCaptureStorageLimit, exception.IsCaptureStorageLimit);
        Assert.Equal(expectedMessage, exception.Message);
    }

    /// <summary>The read change exception defaults to an unspecified change and its exact message.</summary>
    [Fact]
    public void FileChangedDuringReadExceptionDefaultsToUnspecified()
    {
        var exception = new FileChangedDuringReadException();

        Assert.Equal(FileChangeKind.Unspecified, exception.ChangeKind);
        Assert.Equal("File length changed during complete-content read.", exception.Message);
    }

    /// <summary>The read change exception exposes the supplied change kind.</summary>
    [Theory]
    [InlineData(FileChangeKind.ShortRead)]
    [InlineData(FileChangeKind.Growth)]
    [InlineData(FileChangeKind.Shrinkage)]
    [InlineData(FileChangeKind.PositionChanged)]
    public void FileChangedDuringReadExceptionExposesChangeKind(FileChangeKind changeKind)
    {
        var exception = new FileChangedDuringReadException(changeKind);

        Assert.Equal(changeKind, exception.ChangeKind);
        Assert.Equal("File length changed during complete-content read.", exception.Message);
    }
}
