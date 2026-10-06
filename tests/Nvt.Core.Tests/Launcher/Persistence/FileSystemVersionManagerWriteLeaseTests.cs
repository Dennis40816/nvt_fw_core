// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Tests.TestProbe;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Persistence;

/// <summary>Exact live writer custody, frozen contention timing and Windows restart convergence.</summary>
public sealed class FileSystemVersionManagerWriteLeaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Only live production custody authorizes the exact canonical state path.</summary>
    [Fact]
    public async Task RecoveryCapabilityIsLiveExactAndNotForgeable()
    {
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor("state/document.bin");
        var store = new VersionManagerStateFile(path, 1_048_576);
        using VersionManagerWriteLeaseResult lease = await store.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        Assert.True(lease.HoldsStatePath(path));
        Assert.True(lease.HoldsStatePath(workspace.PathFor("state/unused/../document.bin")));
        Assert.False(lease.HoldsStatePath(workspace.PathFor("state/other.bin")));
        using var fake = new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.None, new DisposableStub());
        Assert.True(fake.IsAcquired);
        Assert.False(fake.HoldsStatePath(path));
        lease.Dispose();
        Assert.True(lease.IsAcquired);
        Assert.False(lease.HoldsStatePath(path));
        lease.Dispose();
    }

    /// <summary>Equivalent root spellings share the canonical writer key and exact-path proof.</summary>
    [Fact]
    public async Task CanonicalWriterKeysAreSharedAcrossRootSpellings()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows sharing violations and case folding are required.");
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor("state/document.bin");
        string equivalent = workspace.PathFor("state/unused/../document.bin");
        string differentlyCased = equivalent.ToUpperInvariant();
        Assert.Equal(FileSystemVersionManagerWriteLease.GetLockPath(path),
            FileSystemVersionManagerWriteLease.GetLockPath(equivalent));
        Assert.Equal(FileSystemVersionManagerWriteLease.GetLockPath(path),
            FileSystemVersionManagerWriteLease.GetLockPath(differentlyCased), ignoreCase: true);
        using VersionManagerWriteLeaseResult first = await FileSystemVersionManagerWriteLease.TryAcquireAsync(path, TimeSpan.Zero, Token);
        using VersionManagerWriteLeaseResult second = await FileSystemVersionManagerWriteLease.TryAcquireAsync(differentlyCased, TimeSpan.Zero, Token);
        Assert.True(first.HoldsStatePath(differentlyCased));
        Assert.Equal(VersionManagerWriteLeaseIssue.Busy, second.Issue);
        Assert.False(second.HoldsStatePath(path));
    }

    /// <summary>Different state paths have independent writers; disposed writers can be reacquired.</summary>
    [Fact]
    public async Task DifferentStatePathsDoNotContendAndDisposedWriterCanBeAcquiredAgain()
    {
        using var workspace = new StateFileWorkspace();
        string firstPath = workspace.PathFor("first/state.bin");
        string secondPath = workspace.PathFor("second/state.bin");
        using VersionManagerWriteLeaseResult first = await FileSystemVersionManagerWriteLease.TryAcquireAsync(firstPath, TimeSpan.Zero, Token);
        using VersionManagerWriteLeaseResult second = await FileSystemVersionManagerWriteLease.TryAcquireAsync(secondPath, TimeSpan.Zero, Token);
        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        first.Dispose();
        using VersionManagerWriteLeaseResult again = await FileSystemVersionManagerWriteLease.TryAcquireAsync(firstPath, TimeSpan.Zero, Token);
        Assert.True(again.HoldsStatePath(firstPath));
        Assert.False(again.HoldsStatePath(secondPath));
    }

    /// <summary>The key preserves the frozen filename and first 24 lowercase SHA-256 characters.</summary>
    [Fact]
    public void LockFilenamePreservesFrozenIdentityDerivation()
    {
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor("state.bin");
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        Assert.Equal(Path.Combine(workspace.Root, $".state.bin.{hash[..24]}.writer.lock"),
            FileSystemVersionManagerWriteLease.GetLockPath(path));
        Assert.Matches(@"^\.state\.bin\.[0-9a-f]{24}\.writer\.lock$",
            Path.GetFileName(FileSystemVersionManagerWriteLease.GetLockPath(path)));
    }

    /// <summary>The production lock is physically exclusive and held until result disposal.</summary>
    [Fact]
    public async Task WriterOwnsOnePhysicalExclusiveHandle()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native file sharing is required.");
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor();
        using VersionManagerWriteLeaseResult lease = await FileSystemVersionManagerWriteLease.TryAcquireAsync(path, TimeSpan.Zero, Token);
        string lockPath = FileSystemVersionManagerWriteLease.GetLockPath(path);
        Assert.ThrowsAny<IOException>(() =>
        {
            using var contender = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
        lease.Dispose();
        using var reopened = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(reopened.CanWrite);
    }

    /// <summary>A terminated holder releases writer custody while abandoned residue grants no authority.</summary>
    [Fact]
    public async Task WindowsAbandonedProcessReleasesWriterForRestartConvergence()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Abandoned-process writer custody requires Windows.");
        await using var workspace = new ProbeWorkspace();
        string path = workspace.PathFor("state/document.bin");
        string lockPath = FileSystemVersionManagerWriteLease.GetLockPath(path);
        string ready = workspace.PathFor("lock-ready.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var state = new VersionManagerStateFile(path, 1_048_576);
        byte[] prior = SyntheticStateCodec.Encode("original", false);
        await state.WriteAsync(prior, Token);
        Process process = workspace.Start(["--mode", "hold-lock", "--lock-path", lockPath, "--lock-ready", ready]);
        Assert.Equal("STARTED", await process.StandardOutput.ReadLineAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(60), Token));
        Assert.Equal("LOCK_HELD", await process.StandardOutput.ReadLineAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token));
        Assert.Equal("ready", await File.ReadAllTextAsync(ready, Token));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        string residue = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        byte[] partial = [0xA7, 0];
        await File.WriteAllBytesAsync(residue, partial, Token);
        using VersionManagerWriteLeaseResult contended = await state.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        Assert.Equal(VersionManagerWriteLeaseIssue.Busy, contended.Issue);
        Assert.False(contended.HoldsStatePath(path));
        using var fake = new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.None, new DisposableStub());
        Assert.False(fake.HoldsStatePath(path));
        await ProbeWorkspace.KillAsync(process);
        Assert.True(process.HasExited);
        using VersionManagerWriteLeaseResult recovered = await state.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(2), Token);
        Assert.True(recovered.IsAcquired);
        Assert.True(recovered.HoldsStatePath(path));
        Assert.Equal(prior, await state.ReadAsync(Token));
        byte[] replacement = SyntheticStateCodec.Encode("replacement", true);
        await state.WriteAsync(replacement, Token);
        Assert.Equal(replacement, await state.ReadAsync(Token));
        Assert.Equal(partial, await File.ReadAllBytesAsync(residue, Token));
        recovered.Dispose();
        Assert.False(recovered.HoldsStatePath(path));
        Assert.False(fake.HoldsStatePath(path));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    /// <summary>Zero wait attempts once; 49/50/51 ms waits preserve exact delay and retry boundaries.</summary>
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(49, 2, 49)]
    [InlineData(50, 2, 50)]
    [InlineData(51, 3, 51)]
    public async Task RetryIntervalIsBoundedByRemainingTimeout(int milliseconds, int expectedAttempts, int expectedDelay)
    {
        using var workspace = new StateFileWorkspace();
        long now = 100;
        int attempts = 0;
        var delays = new List<TimeSpan>();
        var operations = new WriteLeaseOperations(_ =>
        {
            attempts++;
            throw NativeIo(32);
        }, () => now, (delay, token) =>
        {
            Assert.Equal(Token, token);
            delays.Add(delay);
            now += (long)delay.TotalMilliseconds;
            return Task.CompletedTask;
        });
        using VersionManagerWriteLeaseResult result = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            workspace.PathFor(), TimeSpan.FromMilliseconds(milliseconds), operations, Token);
        Assert.Equal(VersionManagerWriteLeaseIssue.Busy, result.Issue);
        Assert.Equal(expectedAttempts, attempts);
        Assert.Equal(expectedDelay, delays.Sum(delay => delay.TotalMilliseconds));
        Assert.All(delays, delay => Assert.InRange(delay.TotalMilliseconds, 1, 50));
        if (milliseconds == 51)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(50), delays[0]);
            Assert.Equal(TimeSpan.FromMilliseconds(1), delays[1]);
        }
    }

    /// <summary>The source performs the final acquisition attempt before classifying timeout contention.</summary>
    [Fact]
    public async Task AcquisitionAtExactRetryDeadlineSucceeds()
    {
        using var workspace = new StateFileWorkspace();
        long now = 0;
        int attempts = 0;
        var operations = new WriteLeaseOperations(path =>
        {
            if (++attempts == 1) throw NativeIo(33);
            return WriteLeaseOperations.Default.Open(path);
        }, () => now, (delay, token) =>
        {
            Assert.Equal(Token, token);
            now += (long)delay.TotalMilliseconds;
            return Task.CompletedTask;
        });
        string path = workspace.PathFor();
        using VersionManagerWriteLeaseResult result = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            path, TimeSpan.FromMilliseconds(50), operations, Token);
        Assert.Equal(2, attempts);
        Assert.Equal(50, now);
        Assert.True(result.HoldsStatePath(path));
    }

    /// <summary>The smallest positive wait keeps its sub-millisecond remaining delay.</summary>
    [Fact]
    public async Task SmallestPositiveWaitIsNotRoundedUp()
    {
        using var workspace = new StateFileWorkspace();
        long now = 0;
        int attempts = 0;
        var operations = new WriteLeaseOperations(_ =>
        {
            attempts++;
            throw NativeIo(32);
        }, () => now, (delay, token) =>
        {
            Assert.Equal(TimeSpan.FromTicks(1), delay);
            Assert.Equal(Token, token);
            now = 1;
            return Task.CompletedTask;
        });
        using VersionManagerWriteLeaseResult result = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            workspace.PathFor(), TimeSpan.FromTicks(1), operations, Token);
        Assert.Equal(VersionManagerWriteLeaseIssue.Busy, result.Issue);
        Assert.Equal(2, attempts);
    }

    /// <summary>Closing the physical handle removes authority even before result disposal.</summary>
    [Fact]
    public async Task ClosedHandleCannotGrantWriterAuthority()
    {
        using var workspace = new StateFileWorkspace();
        FileStream? opened = null;
        var operations = WriteLeaseOperations.Default with { Open = path => opened = WriteLeaseOperations.Default.Open(path) };
        string path = workspace.PathFor();
        using VersionManagerWriteLeaseResult result = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            path, TimeSpan.Zero, operations, Token);
        Assert.True(result.HoldsStatePath(path));
        Assert.NotNull(opened);
        opened.Dispose();
        Assert.False(result.HoldsStatePath(path));
    }

    /// <summary>Only native sharing violations 32 and 33 produce Busy.</summary>
    [Theory]
    [InlineData(31, VersionManagerWriteLeaseIssue.Unavailable)]
    [InlineData(32, VersionManagerWriteLeaseIssue.Busy)]
    [InlineData(33, VersionManagerWriteLeaseIssue.Busy)]
    [InlineData(34, VersionManagerWriteLeaseIssue.Unavailable)]
    public async Task NativeFailureMappingPreservesSharingViolationBoundary(int nativeCode, VersionManagerWriteLeaseIssue expected)
    {
        using var workspace = new StateFileWorkspace();
        var operations = WriteLeaseOperations.Default with { Open = _ => throw NativeIo(nativeCode) };
        using VersionManagerWriteLeaseResult result = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            workspace.PathFor(), TimeSpan.Zero, operations, Token);
        Assert.Equal(expected, result.Issue);
        Assert.False(result.HoldsStatePath(workspace.PathFor()));
    }

    /// <summary>Original access failures retain typed Unavailable outcomes.</summary>
    [Fact]
    public async Task InaccessibleLockAndInvalidParentAreUnavailable()
    {
        using var workspace = new StateFileWorkspace();
        var operations = WriteLeaseOperations.Default with { Open = _ => throw new UnauthorizedAccessException() };
        using VersionManagerWriteLeaseResult denied = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            workspace.PathFor(), TimeSpan.Zero, operations, Token);
        Assert.Equal(VersionManagerWriteLeaseIssue.Unavailable, denied.Issue);
        string parentFile = workspace.PathFor("parent-file");
        await File.WriteAllTextAsync(parentFile, "synthetic", Token);
        using VersionManagerWriteLeaseResult blocked = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            Path.Combine(parentFile, "state.bin"), TimeSpan.Zero, Token);
        Assert.Equal(VersionManagerWriteLeaseIssue.Unavailable, blocked.Issue);
    }

    /// <summary>Negative waits are rejected at the smallest negative tick and extreme value.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public async Task NegativeWaitIsRejected(long ticks)
    {
        using var workspace = new StateFileWorkspace();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>("waitTimeout", async () =>
            await FileSystemVersionManagerWriteLease.TryAcquireAsync(workspace.PathFor(), TimeSpan.FromTicks(ticks), Token));
    }

    /// <summary>Blank state identity is rejected before a negative wait or cancellation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankPathIsRejectedBeforeWait(string? path)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await FileSystemVersionManagerWriteLease.TryAcquireAsync(path!, TimeSpan.FromTicks(-1), Token));
    }

    /// <summary>Cancellation interrupts contention without granting custody.</summary>
    [Fact]
    public async Task CancellationDuringRetryPropagates()
    {
        using var workspace = new StateFileWorkspace();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        int attempts = 0;
        var operations = new WriteLeaseOperations(_ =>
        {
            attempts++;
            throw NativeIo(32);
        }, static () => 0, (delay, token) =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(50), delay);
            cancellation.Cancel();
            return Task.FromCanceled(token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await FileSystemVersionManagerWriteLease.TryAcquireAsync(workspace.PathFor(), TimeSpan.FromSeconds(1), operations, cancellation.Token));
        Assert.Equal(1, attempts);
    }

    /// <summary>Cancellation precedes the first open after canonical lock parent setup.</summary>
    [Fact]
    public async Task CancelledAcquisitionCreatesParentButNeverOpensLock()
    {
        using var workspace = new StateFileWorkspace();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.Cancel();
        string path = workspace.PathFor("nested/state.bin");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await FileSystemVersionManagerWriteLease.TryAcquireAsync(path, TimeSpan.Zero, cancellation.Token));
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.False(File.Exists(FileSystemVersionManagerWriteLease.GetLockPath(path)));
    }

    /// <summary>Result construction preserves the exact successful-handle invariant and message.</summary>
    [Fact]
    public void ResultRequiresExactlyOneDisposableOnlyOnSuccess()
    {
        var missing = Assert.Throws<ArgumentException>("lease", () => new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.None));
        Assert.StartsWith("A successful writer lease must own exactly one handle.", missing.Message, StringComparison.Ordinal);
        using var disposable = new DisposableStub();
        Assert.Throws<ArgumentException>("lease", () => new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.Busy, disposable));
        using var result = new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.None, disposable);
        result.Dispose();
        result.Dispose();
        Assert.Equal(1, disposable.DisposeCount);
        Assert.ThrowsAny<ArgumentException>(() => result.HoldsStatePath(" "));
    }

    private static IOException NativeIo(int code) => new("Synthetic native failure.", unchecked((int)(0x80070000u | (uint)code)));

    private sealed class DisposableStub : IDisposable
    {
        internal int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
