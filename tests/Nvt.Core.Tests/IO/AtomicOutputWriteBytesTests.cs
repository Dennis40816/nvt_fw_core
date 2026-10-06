// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.
#pragma warning disable CA1707 // Keep descriptive test names.
#pragma warning disable xUnit1051 // Characterize default and explicitly controlled cancellation tokens.

using System.Text.RegularExpressions;
using Nvt.Core.IO;
using Xunit;

namespace Nvt.Core.Tests.IO;

public sealed class AtomicOutputWriteBytesTests : IDisposable
{
    private static readonly byte[] PriorBytes = [0x21, 0x43, 0x65];
    private static readonly byte[] AbandonedBytes = [0x87, 0xa9, 0xcb];
    private const string AbandonedName = ".state.bin.00112233445566778899aabbccddeeff.tmp";
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"nvt-atomic-bytes-{Guid.NewGuid():N}");

    public AtomicOutputWriteBytesTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64 * 1024 + 1)]
    [InlineData(1024 * 1024)]
    public async Task Payload_bytes_match_the_frozen_NFC_loop(int length)
    {
        var bytes = CreatePayload(length);

        var result = await CompareAsync(Scenario.NewFile, bytes);

        Assert.Null(result.Error);
        Assert.Equal(bytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task An_existing_destination_is_replaced()
    {
        var bytes = CreatePayload(64 * 1024 + 1);

        var result = await CompareAsync(Scenario.Replace, bytes);

        Assert.Null(result.Error);
        Assert.Equal(bytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task Missing_parent_folders_are_created()
    {
        var bytes = CreatePayload(1);

        var result = await CompareAsync(Scenario.MissingParents, bytes);

        Assert.Null(result.Error);
        Assert.True(result.ParentExists);
        Assert.Equal(bytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task A_token_cancelled_before_the_call_preserves_the_destination()
    {
        var result = await CompareAsync(Scenario.CancelBeforeCall, CreatePayload(1));

        Assert.IsAssignableFrom<OperationCanceledException>(result.Error);
        Assert.Equal(PriorBytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task Cancellation_after_disk_flush_prevents_the_move()
    {
        var result = await CompareAsync(Scenario.CancelAfterDiskFlush, CreatePayload(1));

        Assert.IsType<OperationCanceledException>(result.Error);
        Assert.Equal(1, result.DiskFlushCalls);
        Assert.Equal(PriorBytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task FlushAsync_failure_preserves_the_destination_and_removes_the_temporary_file()
    {
        var result = await CompareAsync(Scenario.FlushAsyncFailure, CreatePayload(1));

        Assert.Same(result.InjectedFailure, result.Error);
        Assert.Equal(PriorBytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task Cancellation_between_write_and_flush_still_reaches_FlushAsync()
    {
        var result = await CompareAsync(Scenario.CancelAfterWrite, CreatePayload(1));

        Assert.Same(result.InjectedFailure, result.Error);
        Assert.Equal(1, result.AsyncFlushCalls);
        Assert.Equal(PriorBytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task Disk_flush_failure_preserves_the_destination_and_removes_the_temporary_file()
    {
        var result = await CompareAsync(Scenario.DiskFlushFailure, CreatePayload(1));

        Assert.Same(result.InjectedFailure, result.Error);
        Assert.Equal(1, result.DiskFlushCalls);
        Assert.Equal(PriorBytes, result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task An_existing_directory_causes_a_real_move_failure()
    {
        var result = await CompareAsync(Scenario.MoveFailure, CreatePayload(1));

        Assert.True(result.Error is IOException or UnauthorizedAccessException);
        Assert.True(result.DestinationIsDirectory);
        Assert.Equal(PriorBytes, result.DirectoryBytes);
        Assert.Null(result.DestinationBytes);
        Assert.Equal(0, result.TemporaryCount);
    }

    [Fact]
    public async Task Cleanup_failure_preserves_the_original_exception_and_leaves_the_temporary_file()
    {
        var result = await CompareAsync(Scenario.CleanupFailure, CreatePayload(1));

        Assert.Same(result.InjectedFailure, result.Error);
        Assert.Equal(PriorBytes, result.DestinationBytes);
        Assert.Equal(1, result.TemporaryCount);
        Assert.True(result.TemporaryIsReadOnly);
    }

    [Fact]
    public async Task A_new_write_replaces_the_destination_without_touching_an_abandoned_temporary_file()
    {
        var bytes = CreatePayload(64 * 1024 + 1);

        var result = await CompareAsync(Scenario.AbandonedTemporary, bytes);

        Assert.Null(result.Error);
        Assert.Equal(bytes, result.DestinationBytes);
        Assert.Equal(1, result.TemporaryCount);
        Assert.Equal(AbandonedBytes, result.AbandonedBytes);
    }

    [Fact]
    public async Task WriteAsync_cleanup_failure_still_replaces_the_original_exception()
    {
        var folder = CreateFolder();
        var path = Path.Combine(folder, "state.bin");
        await File.WriteAllBytesAsync(path, PriorBytes);
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("Synthetic flush failure.");

        var error = await Record.ExceptionAsync(() => AtomicOutput.WriteAsync(
            path,
            (stream, token) => stream.WriteAsync(CreatePayload(1), token).AsTask(),
            temporary => new FaultingFileStream(temporary, Scenario.CleanupFailure, cancellation, failure)));

        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.NotSame(failure, error);
        Assert.Equal(PriorBytes, await File.ReadAllBytesAsync(path));
        var temporary = Assert.Single(Directory.GetFiles(folder, "*.tmp"));
        Assert.True(File.GetAttributes(temporary).HasFlag(FileAttributes.ReadOnly));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task Invalid_paths_use_the_same_validation_as_WriteAsync(string? path)
    {
        var expected = await Record.ExceptionAsync(() => AtomicOutput.WriteAsync(path!, null!));
        var actual = await Record.ExceptionAsync(() => AtomicOutput.WriteBytesAsync(path!, ReadOnlyMemory<byte>.Empty));

        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Message, actual.Message);
        Assert.Empty(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_root_path_uses_the_existing_parent_directory_exception()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicOutput.WriteBytesAsync(Path.GetPathRoot(directory)!, ReadOnlyMemory<byte>.Empty));

        Assert.Equal("Output path has no parent directory.", error.Message);
    }

    [Fact]
    public async Task The_path_is_normalized_before_parent_directories_are_created()
    {
        var path = Path.Combine(directory, "unused", "..", "nested", "state.bin");
        var bytes = CreatePayload(1);

        await AtomicOutput.WriteBytesAsync(path, bytes);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.GetFullPath(path)));
        Assert.False(Directory.Exists(Path.Combine(directory, "unused")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_WriteAsync_resumes_on_the_captured_context(bool writeBytes)
    {
        var path = Path.Combine(CreateFolder(), "state.bin");
        var bytes = CreatePayload(1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task operation;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            operation = writeBytes
                ? AtomicOutput.WriteBytesAsync(path, bytes, temporary => new GatedFileStream(temporary, release.Task))
                : AtomicOutput.WriteAsync(path, (stream, token) => stream.WriteAsync(bytes, token).AsTask(),
                    temporary => new GatedFileStream(temporary, release.Task));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            release.SetResult();
        }

        await operation;

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        if (writeBytes) Assert.Equal(0, context.PostCount);
        else Assert.True(context.PostCount > 0);
    }

    private async Task<Outcome> CompareAsync(Scenario scenario, byte[] bytes)
    {
        var expected = await RunAsync(scenario, bytes, oracle: true);
        var actual = await RunAsync(scenario, bytes, oracle: false);

        Assert.Equal(expected.Error?.GetType(), actual.Error?.GetType());
        Assert.Equal(expected.ErrorMessage, actual.ErrorMessage);
        Assert.Equal(expected.DestinationBytes, actual.DestinationBytes);
        Assert.Equal(expected.TemporaryCount, actual.TemporaryCount);
        Assert.Equal(expected.ParentExists, actual.ParentExists);
        Assert.Equal(expected.DestinationIsDirectory, actual.DestinationIsDirectory);
        Assert.Equal(expected.DirectoryBytes, actual.DirectoryBytes);
        Assert.Equal(expected.AbandonedBytes, actual.AbandonedBytes);
        Assert.Equal(expected.TemporaryIsReadOnly, actual.TemporaryIsReadOnly);
        Assert.Equal(expected.AsyncFlushCalls, actual.AsyncFlushCalls);
        Assert.Equal(expected.DiskFlushCalls, actual.DiskFlushCalls);
        return actual;
    }

    private async Task<Outcome> RunAsync(Scenario scenario, byte[] bytes, bool oracle)
    {
        var folder = CreateFolder();
        var path = scenario == Scenario.MissingParents
            ? Path.Combine(folder, "nested", "child", "state.bin")
            : Path.Combine(folder, "state.bin");
        if (scenario == Scenario.MoveFailure)
        {
            Directory.CreateDirectory(path);
            await File.WriteAllBytesAsync(Path.Combine(path, "prior.bin"), PriorBytes);
        }
        else if (scenario is not (Scenario.NewFile or Scenario.MissingParents))
        {
            await File.WriteAllBytesAsync(path, PriorBytes);
        }
        if (scenario == Scenario.AbandonedTemporary)
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, AbandonedName), AbandonedBytes);
        }

        using var cancellation = new CancellationTokenSource();
        if (scenario == Scenario.CancelBeforeCall) cancellation.Cancel();
        var failure = new IOException("Synthetic flush failure.");
        FaultingFileStream? injectedStream = null;
        Func<string, FileStream>? createStream = scenario is Scenario.CancelAfterDiskFlush
            or Scenario.FlushAsyncFailure or Scenario.CancelAfterWrite or Scenario.DiskFlushFailure or Scenario.CleanupFailure
            ? temporary => injectedStream = new FaultingFileStream(temporary, scenario, cancellation, failure)
            : null;

        var error = await Record.ExceptionAsync(() => oracle
            ? FrozenNfcWriteAtomicallyAsync(path, bytes, cancellation.Token, createStream)
            : createStream is null
                ? AtomicOutput.WriteBytesAsync(path, bytes, cancellation.Token)
                : AtomicOutput.WriteBytesAsync(path, bytes, createStream, cancellation.Token));

        var temporaryFiles = Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories);
        var abandoned = Path.Combine(folder, AbandonedName);
        // Compare messages after replacing only the fresh folder and generated temporary-file identifier.
        var message = error is null ? null : Regex.Replace(
            error.Message.Replace(folder, "<folder>", StringComparison.Ordinal),
            @"(?<=\.)[0-9a-f]{32}(?=\.tmp)", "<temporary>");
        return new Outcome(
            error, message,
            File.Exists(path) ? await File.ReadAllBytesAsync(path) : null,
            temporaryFiles.Length,
            Directory.Exists(Path.GetDirectoryName(path)),
            Directory.Exists(path),
            Directory.Exists(path) ? await File.ReadAllBytesAsync(Path.Combine(path, "prior.bin")) : null,
            File.Exists(abandoned) ? await File.ReadAllBytesAsync(abandoned) : null,
            temporaryFiles.Any(file => File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)),
            injectedStream?.AsyncFlushCalls ?? 0,
            injectedStream?.DiskFlushCalls ?? 0,
            failure);
    }

    // Frozen NFC source: Dennis40816/nvt_fw_combiner, origin/1.2.x,
    // commit 60e3f28e9c9f9926097e642e22e59d2a92ebc00e, identical WriteAtomicallyAsync copies in:
    // src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonVersionManagerStateStore.cs
    // src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonLauncherBootstrapStateStore.cs
    // The optional factory replaces only construction of the stream for deterministic fault injection.
    // NFC's adapter keeps its own parent-directory check and message; this copy uses Core's message.
    private static async Task FrozenNfcWriteAtomicallyAsync(
        string destination,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken,
        Func<string, FileStream>? createStream = null)
    {
        var directory = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Output path has no parent directory.");
        _ = Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = createStream is null
                ? new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)
                : createStream(temporary))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private string CreateFolder()
    {
        var folder = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static byte[] CreatePayload(int length)
    {
        var bytes = new byte[length];
        new Random(0x704).NextBytes(bytes);
        return bytes;
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }

    private enum Scenario
    {
        NewFile,
        Replace,
        MissingParents,
        CancelBeforeCall,
        CancelAfterDiskFlush,
        FlushAsyncFailure,
        CancelAfterWrite,
        DiskFlushFailure,
        MoveFailure,
        CleanupFailure,
        AbandonedTemporary
    }

    private sealed record Outcome(
        Exception? Error,
        string? ErrorMessage,
        byte[]? DestinationBytes,
        int TemporaryCount,
        bool ParentExists,
        bool DestinationIsDirectory,
        byte[]? DirectoryBytes,
        byte[]? AbandonedBytes,
        bool TemporaryIsReadOnly,
        int AsyncFlushCalls,
        int DiskFlushCalls,
        IOException InjectedFailure);

    private sealed class FaultingFileStream(
        string path,
        Scenario scenario,
        CancellationTokenSource cancellation,
        IOException failure)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)
    {
        public int AsyncFlushCalls { get; private set; }
        public int DiskFlushCalls { get; private set; }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (scenario == Scenario.CancelAfterWrite) cancellation.Cancel();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            AsyncFlushCalls++;
            if (scenario == Scenario.CleanupFailure)
            {
                File.SetAttributes(Name, File.GetAttributes(Name) | FileAttributes.ReadOnly);
            }
            return scenario is Scenario.FlushAsyncFailure or Scenario.CancelAfterWrite or Scenario.CleanupFailure
                ? Task.FromException(failure)
                : base.FlushAsync(cancellationToken);
        }

        public override void Flush(bool flushToDisk)
        {
            base.Flush(flushToDisk);
            if (!flushToDisk) return;
            DiskFlushCalls++;
            if (scenario == Scenario.CancelAfterDiskFlush) cancellation.Cancel();
            if (scenario == Scenario.DiskFlushFailure) throw failure;
        }
    }

    private sealed class GatedFileStream(string path, Task release)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await release.ConfigureAwait(false);
        }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int postCount;
        public int PostCount => Volatile.Read(ref postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref postCount);
            ThreadPool.QueueUserWorkItem(work =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { d(work); }
                finally { SetSynchronizationContext(previous); }
            }, state);
        }
    }
}
