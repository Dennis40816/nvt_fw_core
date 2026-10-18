// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;
using Native = Nvt.Core.Files.Windows.WindowsStablePathCustody.NativeMethods;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Checks repository staging cleanup, hook handle release and promoted marker mutation boundaries.</summary>
public sealed class ManagedSetupCleanupTests
{
    /// <summary>A status hook that throws still releases the directory handle opened before it ran.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThrowingStatusHookReleasesTheOpenedDirectoryHandle(bool deleteHook)
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        Func<int, int> hook = _ => throw new InvalidOperationException("status hook");
        var issue = ManagedSetupDestinationCustody.TryAcquire(fixture.Root, out var custody,
            deleteHook ? hook : null, deleteHook ? null : hook);
        Assert.Equal(ManagedFirstInstallationMaterializationIssue.None, issue);
        using (custody)
        {
            string child = Path.Combine(fixture.StagingPath, "child");
            Assert.Equal(ManagedFirstInstallationMaterializationIssue.None, custody!.CreateStaging(fixture.StagingPath, child));
            string staging = Path.Combine(child, ".staging");
            Directory.CreateDirectory(staging);
            var observed = custody.ObserveEmptyStagingChild(staging);
            Assert.Equal(WindowsSetupStagingCleanupState.Observed, observed.State);

            if (deleteHook) Assert.Throws<InvalidOperationException>(() => custody.DeleteObservedEmptyStagingChild(staging, observed.Identity, null));
            else Assert.Throws<InvalidOperationException>(() => custody.ObserveOwnedStagingDeletion(staging, observed.Identity));

            // A handle left to finalization keeps the name from being removed and created again.
            Directory.Delete(staging);
            Directory.CreateDirectory(staging);
        }
    }

    /// <summary>Contended cleanup stops after twenty attempts spaced by the frozen 250 ms delay.</summary>
    [Fact]
    public async Task StagingCleanupStopsAfterTwentyContendedAttempts()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var trace = new CleanupTrace();
        var result = await MaterializeAsync(fixture, trace.Hooks(deleteOpen: _ => Native.StatusSharingViolation));

        Assert.False(result.IsSuccess);
        Assert.Equal(20, trace.Attempts.Count);
        Assert.All(trace.Attempts, state => Assert.Equal(WindowsSetupStagingCleanupState.RetryableContention, state));
        Assert.Equal(19, trace.Delays.Count);
        Assert.All(trace.Delays, delay => Assert.Equal(TimeSpan.FromMilliseconds(250), delay));
    }

    /// <summary>After a deletion is pending, later attempts only observe it and never delete again.</summary>
    [Fact]
    public async Task PendingOwnedDeletionIsObservedAndNotDeletedAgain()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var trace = new CleanupTrace();
        var result = await MaterializeAsync(fixture, trace.Hooks(ownedObservation: _ => Native.StatusDeletePending));

        Assert.False(result.IsSuccess);
        Assert.Equal(20, trace.Attempts.Count);
        Assert.All(trace.Attempts, state => Assert.Equal(WindowsSetupStagingCleanupState.OwnedDeletionPending, state));
        Assert.Equal(1, trace.DeleteCalls);
        Assert.Equal(19, trace.Delays.Count);
    }

    /// <summary>A replacement directory under the same name is never deleted as the observed child.</summary>
    [Fact]
    public async Task ReplacedStagingChildIsRejectedAsChanged()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var trace = new CleanupTrace();
        int opens = 0;
        trace.OnAttempt = attempt =>
        {
            if (attempt != 1) return;
            Directory.Delete(trace.StagingPath!);
            Directory.CreateDirectory(trace.StagingPath!);
        };
        var result = await MaterializeAsync(fixture,
            trace.Hooks(deleteOpen: status => opens++ == 0 ? Native.StatusSharingViolation : status));

        Assert.False(result.IsSuccess);
        Assert.Equal(new[] { WindowsSetupStagingCleanupState.RetryableContention, WindowsSetupStagingCleanupState.ChangedOrUnsafe },
            trace.Attempts);
        Assert.Single(trace.Delays);
    }

    /// <summary>A child added after the final marker proof cannot advance the marker phase.</summary>
    [Fact]
    public async Task MarkerPhaseAdvanceRevalidatesAfterAdversarialProofHook()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        bool armed = false;
        var hooks = new ManagedSetupTestHooks
        {
            AfterMarkerTopologyProof = () =>
            {
                if (armed) SetupFixture.Write(Path.Combine(fixture.Root, "late-marker-child.txt"), "foreign");
            },
        };
        var result = await fixture.MaterializeAsync(hooks);
        Assert.True(result.IsSuccess, result.Issue.ToString());
        using var installation = result.Installation!;
        armed = true;

        var advanced = await installation.RecordBootstrapLaunchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationTransactionIssue.RecoveryRequired, advanced);
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "late-marker-child.txt")));
    }

    /// <summary>A child added after the delete proof keeps the exact marker as recovery evidence.</summary>
    [Fact]
    public async Task MarkerDeleteRevalidatesAfterAdversarialProofHook()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        bool armed = false;
        var hooks = new ManagedSetupTestHooks
        {
            AfterMarkerTopologyProof = () =>
            {
                if (armed) SetupFixture.Write(Path.Combine(fixture.Root, "late-delete-child.txt"), "foreign");
            },
        };
        var result = await fixture.MaterializeAsync(hooks);
        Assert.True(result.IsSuccess, result.Issue.ToString());
        using var installation = result.Installation!;
        Assert.Equal(ManagedFirstInstallationTransactionIssue.None,
            await installation.RecordBootstrapLaunchAsync(TestContext.Current.CancellationToken));
        armed = true;

        var completed = await installation.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationTransactionIssue.RecoveryRequired, completed);
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    /// <summary>A marker stream whose final flush fails is reported as a diagnostic and never replaces the typed result.</summary>
    [Fact]
    public async Task FailedMarkerDisposalIsReportedAndDoesNotEscape()
    {
        var failure = new IOException("final flush");
        var reported = new List<Exception>();
        using var stream = new FailingDisposeStream(failure);

        await FileSystemManagedFirstInstallationRootMaterializer.DisposeMarkerStreamAsync(stream, reported.Add);

        Assert.Same(failure, Assert.Single(reported));
        using var second = new FailingDisposeStream(failure);
        await FileSystemManagedFirstInstallationRootMaterializer.DisposeMarkerStreamAsync(
            second, _ => throw new InvalidOperationException("diagnostics observer"));
    }

    /// <summary>A fatal failure while disposing the marker stream still propagates.</summary>
    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public async Task FatalMarkerDisposalFailurePropagates(Type exceptionType)
    {
        var failure = (Exception)Activator.CreateInstance(exceptionType)!;
        using var stream = new FailingDisposeStream(failure);

        Exception? actual = await Record.ExceptionAsync(() =>
            FileSystemManagedFirstInstallationRootMaterializer.DisposeMarkerStreamAsync(stream, null).AsTask());

        Assert.Same(failure, actual);
    }

    /// <summary>The Bootstrap launch opportunity needs the recorded phase, survives cancellation and is used once.</summary>
    [Fact]
    public async Task BootstrapLaunchLeaseIsPhaseGatedAndOneShot()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var result = await fixture.MaterializeAsync();
        Assert.True(result.IsSuccess, result.Issue.ToString());
        using var installation = result.Installation!;
        ManagedImmutableBootstrapIdentity exact = SetupFixture.PayloadIdentity().Bootstrap;
        ManagedImmutableBootstrapIdentity other = ManagedImmutableBootstrapIdentity.Create(ContractFixture.Descriptor,
            SetupFixture.Layout.BootstrapFileName, 4, SetupFixture.Hash("zzzz"u8));

        var early = await installation.AcquireBootstrapLaunchLeaseAsync(exact, TestContext.Current.CancellationToken);
        Assert.Null(early.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.Unavailable, early.Issue);
        Assert.Equal(ManagedFirstInstallationTransactionIssue.None,
            await installation.RecordBootstrapLaunchAsync(TestContext.Current.CancellationToken));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await installation.AcquireBootstrapLaunchLeaseAsync(exact, cancelled.Token));
        var mismatched = await installation.AcquireBootstrapLaunchLeaseAsync(other, TestContext.Current.CancellationToken);
        Assert.Null(mismatched.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.UnsafePath, mismatched.Issue);

        var second = await installation.AcquireBootstrapLaunchLeaseAsync(exact, TestContext.Current.CancellationToken);
        Assert.Null(second.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.Unavailable, second.Issue);
    }

    private static async Task<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        SetupFixture fixture, ManagedSetupTestHooks hooks)
    {
        using var payload = new TestPayloadCapture(SetupFixture.PayloadIdentity());
        var repository = new SyntheticRepository(fixture.Admission) { CreateRepositoryStaging = true };
        var materializer = new FileSystemManagedFirstInstallationRootMaterializer(repository, repository.InstallHeldAsync,
            new WindowsStableTreeLimits(10_001, 10_000, 1_000_000_000), SetupFixture.Layout, fixture.SeedStore, fixture.Codec,
            SetupFixture.Ceiling, SetupFixture.Ceiling, ManagedImmutableBootstrapIdentity.MaximumExecutableBytes, hooks);
        return await materializer.MaterializeAsync(fixture.Root, fixture.StatePath, payload,
            fixture.Candidate, fixture.Seed, TestContext.Current.CancellationToken);
    }

    private sealed class FailingDisposeStream(Exception failure) : MemoryStream
    {
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            throw failure;
        }
    }

    private sealed class CleanupTrace
    {
        internal List<WindowsSetupStagingCleanupState> Attempts { get; } = [];
        internal List<TimeSpan> Delays { get; } = [];
        internal int DeleteCalls { get; private set; }
        internal string? StagingPath { get; private set; }
        internal Action<int>? OnAttempt { get; set; }

        internal ManagedSetupTestHooks Hooks(Func<int, int>? deleteOpen = null, Func<int, int>? ownedObservation = null) => new()
        {
            BeforeRepositoryStagingCleanup = path => StagingPath = path,
            BeforeRepositoryStagingDelete = _ => DeleteCalls++,
            RepositoryStagingCleanupAttemptObserved = (attempt, state) =>
            {
                Attempts.Add(state);
                OnAttempt?.Invoke(attempt);
            },
            RepositoryStagingCleanupDelay = (delay, _) =>
            {
                Delays.Add(delay);
                return ValueTask.CompletedTask;
            },
            RepositoryStagingDeleteOpenStatusOverride = deleteOpen,
            RepositoryOwnedDeletionObservationStatusOverride = ownedObservation,
        };
    }
}
