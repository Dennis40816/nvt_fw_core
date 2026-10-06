// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files;
using Nvt.Core.IO;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Persistence;

/// <summary>Physical atomic-byte write failures, replacement identity and writer lifetime.</summary>
public sealed class StatePublicationTests
{
    private static readonly byte[] Prior = SyntheticStateCodec.Encode("original", false);
    private static readonly byte[] Replacement = SyntheticStateCodec.Encode("replacement", true);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Physical partial writes and failed flushes retain the prior complete document.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task PhysicalWriteFailurePreservesPriorStateAndCleansTemporaryFile(int failurePosition)
    {
        var scenario = (WriteFailure)failurePosition;
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor();
        var state = new VersionManagerStateFile(path, 1_048_576);
        using VersionManagerWriteLeaseResult lease = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        await state.WriteAsync(Prior, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var failure = new IOException("Synthetic physical state-write failure.");
        FaultingFileStream? stream = null;
        Exception? error = await Record.ExceptionAsync(() => AtomicOutput.WriteBytesAsync(path, Replacement,
            temporary => stream = new FaultingFileStream(temporary, scenario, cancellation, failure), cancellation.Token));
        Assert.NotNull(stream);
        Assert.True(stream.WrittenBytes > 0);
        if (scenario == WriteFailure.CancelAfterDiskFlush)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Equal(1, stream.DiskFlushCalls);
        }
        else Assert.Same(failure, error);
        if (scenario == WriteFailure.PartialWrite) Assert.Equal(Replacement.Length / 2, stream.WrittenBytes);
        if (scenario is WriteFailure.AsyncFlush or WriteFailure.DiskFlush) Assert.Equal(1, stream.AsyncFlushCalls);
        if (scenario == WriteFailure.DiskFlush) Assert.Equal(1, stream.DiskFlushCalls);
        Assert.Equal(Prior, await state.ReadAsync(Token));
        Assert.Equal(("original", false), SyntheticStateCodec.Decode(await state.ReadAsync(Token)));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
        Assert.True(lease.HoldsStatePath(path));
    }

    /// <summary>Native move failure maps to launcher Unavailable and retains the prior complete document.</summary>
    [Fact]
    public async Task PhysicalReplacementFailureRetainsPriorState()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows destination sharing is required.");
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor();
        var app = new VersionManagerStateFile(path, 1_048_576);
        var launcher = new LauncherBootstrapStateFile(path, ".launcher.bin", 65_536);
        using VersionManagerWriteLeaseResult lease = await app.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        await app.WriteAsync(Prior, Token);
        Assert.True((await launcher.TryWriteAsync(Prior, Token)).IsSuccess);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Exception? error = await Record.ExceptionAsync(async () => await app.WriteAsync(Replacement, Token));
            Assert.NotNull(error);
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Contains(error.HResult & 0xFFFF, NativeMoveFailures);
            Assert.Equal(Prior, await File.ReadAllBytesAsync(path, Token));
        }
        using (var held = new FileStream(launcher.StatePathIdentity, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            LauncherBootstrapStateSaveResult result = await launcher.TryWriteAsync(Replacement, Token);
            Assert.Equal(LauncherBootstrapStateSaveIssue.Unavailable, result.Issue);
            Assert.Equal(Prior, await File.ReadAllBytesAsync(launcher.StatePathIdentity, Token));
        }
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
        Assert.True(lease.HoldsStatePath(path));
    }

    /// <summary>A later failed physical save preserves the most recently replaced complete document.</summary>
    [Fact]
    public async Task FailureAfterReplacementRetainsNewCompleteDocument()
    {
        using var workspace = new StateFileWorkspace();
        var state = new VersionManagerStateFile(workspace.PathFor(), 1_048_576);
        using VersionManagerWriteLeaseResult lease = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        await state.WriteAsync(Prior, Token);
        await state.WriteAsync(Replacement, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await state.WriteAsync(Prior, cancellation.Token));
        Assert.Equal(Replacement, await state.ReadAsync(Token));
        Assert.Equal(("replacement", true), SyntheticStateCodec.Decode(await state.ReadAsync(Token)));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
    }

    /// <summary>Byte-identical replacement changes the file object without changing canonical writer custody.</summary>
    [Fact]
    public async Task ByteIdenticalReplacementChangesFileIdentity()
    {
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor();
        var state = new VersionManagerStateFile(path, 1_048_576);
        using VersionManagerWriteLeaseResult lease = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        await state.WriteAsync(Prior, Token);
        string key = FileSystemVersionManagerWriteLease.GetLockPath(path);
        (ulong Device, ulong File) priorIdentity = ReadFileIdentity(path);
        await state.WriteAsync(Prior, Token);
        Assert.NotEqual(priorIdentity, ReadFileIdentity(path));
        Assert.Equal(Prior, await state.ReadAsync(Token));
        Assert.Equal(key, FileSystemVersionManagerWriteLease.GetLockPath(path));
        Assert.True(lease.HoldsStatePath(path));
    }

    /// <summary>The writer remains exclusive while physical publication is gated and until caller disposal.</summary>
    [Fact]
    public async Task WriterCustodySpansCompletePhysicalPublication()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native writer contention is required.");
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor();
        var state = new VersionManagerStateFile(path, 1_048_576);
        using VersionManagerWriteLeaseResult lease = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        await state.WriteAsync(Prior, Token);
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task write = AtomicOutput.WriteBytesAsync(path, Replacement,
            temporary => new GatedFileStream(temporary, flushed, release.Task), Token);
        try
        {
            await flushed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.False(write.IsCompleted);
            Assert.Equal(Prior, await state.ReadAsync(Token));
            using VersionManagerWriteLeaseResult during = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
            Assert.Equal(VersionManagerWriteLeaseIssue.Busy, during.Issue);
            Assert.True(lease.HoldsStatePath(path));
            release.SetResult();
            await write.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.Equal(Replacement, await state.ReadAsync(Token));
            using VersionManagerWriteLeaseResult after = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
            Assert.Equal(VersionManagerWriteLeaseIssue.Busy, after.Issue);
        }
        finally
        {
            release.TrySetResult();
            await write.WaitAsync(TimeSpan.FromSeconds(5), Token);
        }
        lease.Dispose();
        using VersionManagerWriteLeaseResult reacquired = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        Assert.True(reacquired.HoldsStatePath(path));
    }

    private static readonly int[] NativeMoveFailures = [5, 32, 33];

    private static (ulong Device, ulong File) ReadFileIdentity(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            (long device, long inode) = RegularFileGuard.ReadUnixIdentity(path)!.Value;
            return (unchecked((ulong)device), unchecked((ulong)inode));
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out FileInformation information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return (information.VolumeSerialNumber, ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    // Read-only identity evidence in tests; this introduces no runtime filesystem custody owner.
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    private enum WriteFailure
    {
        /// <summary>Fail after physically writing a strict prefix.</summary>
        PartialWrite,
        /// <summary>Fail asynchronous flush after writing the entire temporary file.</summary>
        AsyncFlush,
        /// <summary>Fail after the actual disk flush.</summary>
        DiskFlush,
        /// <summary>Cancel after actual disk flush and before move.</summary>
        CancelAfterDiskFlush,
    }

    private sealed class FaultingFileStream(string path, WriteFailure scenario,
        CancellationTokenSource cancellation, IOException failure)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)
    {
        internal int WrittenBytes { get; private set; }
        internal int AsyncFlushCalls { get; private set; }
        internal int DiskFlushCalls { get; private set; }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int length = scenario == WriteFailure.PartialWrite ? buffer.Length / 2 : buffer.Length;
            await base.WriteAsync(buffer[..length], cancellationToken).ConfigureAwait(false);
            WrittenBytes = length;
            if (scenario == WriteFailure.PartialWrite) throw failure;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            AsyncFlushCalls++;
            return scenario == WriteFailure.AsyncFlush ? Task.FromException(failure) : base.FlushAsync(cancellationToken);
        }

        public override void Flush(bool flushToDisk)
        {
            base.Flush(flushToDisk);
            if (flushToDisk)
            {
                DiskFlushCalls++;
                if (scenario == WriteFailure.CancelAfterDiskFlush) cancellation.Cancel();
                if (scenario == WriteFailure.DiskFlush) throw failure;
            }
        }
    }

    private sealed class GatedFileStream(string path, TaskCompletionSource flushed, Task release)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)
    {
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await base.FlushAsync(cancellationToken).ConfigureAwait(false);
            flushed.SetResult();
            await release.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
