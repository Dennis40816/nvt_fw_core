// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Locks the rollback-safe launcher activation transaction and its power phases.</summary>
public sealed partial class LauncherBootstrapCoordinatorTests
{
    private static readonly ManagedAppVersion App100 = ManagedAppVersion.Parse("1.0.0");
    private static readonly ManagedAppVersion App101 = ManagedAppVersion.Parse("1.0.1");
    private static readonly ManagedLauncherIdentity Launcher100 = Identity(App100, "1.0.0", 'a');
    private static readonly ManagedLauncherIdentity Launcher101 = Identity(App101, "1.1.0", 'b');
    private static readonly TimeSpan ExpectedStartupWriterLeaseTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>First launch becomes trusted only after nested readiness and durable reload.</summary>
    [Fact]
    public async Task FirstVerifiedLauncherBecomesActiveOnlyAfterReadyAndDurableReload()
    {
        var appStore = new RecordingAppStateStore(AppState(App100));
        var launcherStore = new RecordingLauncherStateStore(load: null);
        var repository = new RecordingLauncherRepository(Launcher100);
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);
        LauncherBootstrapCoordinator coordinator = Create(appStore, launcherStore, repository, process);

        LauncherBootstrapResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.Ready, result.Outcome);
        Assert.Equal(Launcher100, result.RunningLauncher);
        Assert.Equal(Launcher100, launcherStore.Current!.Active);
        Assert.Equal(Launcher100, launcherStore.Current.LastKnownGood);
        Assert.Null(launcherStore.Current.Pending);
        Assert.Equal(3, launcherStore.SaveCount);
        Assert.Equal(2, appStore.LoadCount);
        Assert.Equal(2, launcherStore.LoadCount);
        Assert.Equal([Launcher100], process.Started);
        Assert.Equal(
            [
                ExpectedStartupWriterLeaseTimeout,
                ExpectedStartupWriterLeaseTimeout,
            ],
            appStore.WriterLeaseTimeouts);
    }

    /// <summary>A failed candidate selects only the exact recorded prior launcher.</summary>
    [Fact]
    public async Task FailedCandidateRollsBackOnlyToExactPriorLauncher()
    {
        var appStore = new RecordingAppStateStore(AppState(App101, App100));
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var repository = new RecordingLauncherRepository(Launcher101, Launcher100);
        var process = new RecordingLauncherProcess(
            LauncherProcessStartOutcome.StartFailed,
            LauncherProcessStartOutcome.Ready);
        LauncherBootstrapCoordinator coordinator = Create(appStore, launcherStore, repository, process);

        LauncherBootstrapResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.RolledBack, result.Outcome);
        Assert.Equal(Launcher100, result.RunningLauncher);
        Assert.Equal(Launcher101, result.FailedLauncher);
        Assert.Equal([Launcher101, Launcher100], process.Started);
        Assert.Equal(Launcher100, launcherStore.Current!.Active);
        Assert.Equal(Launcher100, launcherStore.Current.LastKnownGood);
        Assert.Equal(Launcher101, launcherStore.Current.Failed);
        Assert.Null(launcherStore.Current.Pending);
        Assert.Equal(
            [
                ExpectedStartupWriterLeaseTimeout,
                ExpectedStartupWriterLeaseTimeout,
                ExpectedStartupWriterLeaseTimeout,
            ],
            appStore.WriterLeaseTimeouts);
    }

    /// <summary>Startup contention is reported as Busy after only the dedicated short wait.</summary>
    [Fact]
    public async Task InitialWriterContentionReturnsBusyWithinStartupBudget()
    {
        var appStore = new RecordingAppStateStore(AppState(App100))
        {
            BusyLeaseAt = 1,
        };
        var launcherStore = new RecordingLauncherStateStore(load: null);
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.Busy, result.Outcome);
        Assert.Equal([ExpectedStartupWriterLeaseTimeout], appStore.WriterLeaseTimeouts);
        Assert.Equal(0, appStore.LoadCount);
        Assert.Equal(0, launcherStore.LoadCount);
        Assert.Empty(process.Started);
    }

    /// <summary>Contention after candidate READY preserves the recorded candidate and stays StateUnavailable.</summary>
    [Fact]
    public async Task CandidateReadyCommitContentionPreservesRecordedCandidate()
    {
        var appStore = new RecordingAppStateStore(AppState(App101, App100))
        {
            BusyLeaseAt = 2,
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(2, appStore.WriterLeaseTimeouts.Count);
        Assert.All(appStore.WriterLeaseTimeouts, AssertStartupWriterLeaseTimeout);
        Assert.Equal(LauncherActivationPhase.CandidateLaunchRecorded, launcherStore.Current!.Pending?.Phase);
    }

    /// <summary>Contention while recording rollback preserves the recorded candidate and stays StateUnavailable.</summary>
    [Fact]
    public async Task RollbackRecordContentionPreservesRecordedCandidate()
    {
        var appStore = new RecordingAppStateStore(AppState(App101, App100))
        {
            BusyLeaseAt = 2,
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            new RecordingLauncherProcess(LauncherProcessStartOutcome.StartFailed))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(2, appStore.WriterLeaseTimeouts.Count);
        Assert.All(appStore.WriterLeaseTimeouts, AssertStartupWriterLeaseTimeout);
        Assert.Equal(LauncherActivationPhase.CandidateLaunchRecorded, launcherStore.Current!.Pending?.Phase);
    }

    /// <summary>Contention after rollback READY preserves the rollback record and stays StateUnavailable.</summary>
    [Fact]
    public async Task RollbackReadyCommitContentionPreservesRecordedRollback()
    {
        var appStore = new RecordingAppStateStore(AppState(App101, App100))
        {
            BusyLeaseAt = 3,
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            new RecordingLauncherProcess(
                LauncherProcessStartOutcome.StartFailed,
                LauncherProcessStartOutcome.Ready))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(3, appStore.WriterLeaseTimeouts.Count);
        Assert.All(appStore.WriterLeaseTimeouts, AssertStartupWriterLeaseTimeout);
        Assert.Equal(LauncherActivationPhase.RollbackLaunchRecorded, launcherStore.Current!.Pending?.Phase);
    }

    /// <summary>Existing-launch READY/failure contention preserves its guard and stays StateUnavailable.</summary>
    [Theory]
    [InlineData((int)LauncherProcessStartOutcome.Ready)]
    [InlineData((int)LauncherProcessStartOutcome.StartFailed)]
    public async Task ExistingLauncherFollowUpContentionPreservesActiveGuard(
        int processOutcomeValue)
    {
        var processOutcome = (LauncherProcessStartOutcome)processOutcomeValue;
        var appStore = new RecordingAppStateStore(AppState(App100))
        {
            BusyLeaseAt = 2,
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            new RecordingLauncherProcess(processOutcome))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(2, appStore.WriterLeaseTimeouts.Count);
        Assert.All(appStore.WriterLeaseTimeouts, AssertStartupWriterLeaseTimeout);
        Assert.Equal(LauncherActivationPhase.ActiveLaunchRecorded, launcherStore.Current!.Pending?.Phase);
    }

    private static void AssertStartupWriterLeaseTimeout(TimeSpan timeout)
    {
        Assert.Equal(ExpectedStartupWriterLeaseTimeout, timeout);
    }

    /// <summary>An uncertain recorded candidate is never started twice.</summary>
    [Fact]
    public async Task RestartFromRecordedCandidateNeverStartsCandidateAgain()
    {
        var pending = PendingLauncherActivation.Create(
            Launcher101,
            Launcher100,
            Launcher100,
            LauncherActivationPhase.CandidateLaunchRecorded);
        var appStore = new RecordingAppStateStore(AppState(App101, App100));
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending, failed: null));
        var repository = new RecordingLauncherRepository(Launcher101, Launcher100);
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            repository,
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.RolledBack, result.Outcome);
        Assert.Equal([Launcher100], process.Started);
        Assert.DoesNotContain(Launcher101, process.Started);
    }

    /// <summary>A recorded rollback retries only the exact durable fallback.</summary>
    [Fact]
    public async Task RestartFromRecordedRollbackRetriesOnlyRecordedFallback()
    {
        var pending = PendingLauncherActivation.Create(
            Launcher101,
            Launcher100,
            Launcher100,
            LauncherActivationPhase.RollbackLaunchRecorded);
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App101, App100)),
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.RolledBack, result.Outcome);
        Assert.Equal([Launcher100], process.Started);
    }

    /// <summary>Tamper and protocol failures cause no process start or durable mutation.</summary>
    [Theory]
    [InlineData((int)InstalledLauncherIssue.Tampered, (int)LauncherBootstrapOutcome.DamagedLauncher)]
    [InlineData((int)InstalledLauncherIssue.ProtocolMismatch, (int)LauncherBootstrapOutcome.ProtocolMismatch)]
    public async Task VerificationFailureNeverStartsOrMutatesLauncher(
        int issueValue,
        int expectedValue)
    {
        var issue = (InstalledLauncherIssue)issueValue;
        var expected = (LauncherBootstrapOutcome)expectedValue;
        var initial = LauncherBootstrapState.Create(
            Root,
            Launcher100,
            Launcher100,
            pending: null,
            failed: null);
        var launcherStore = new RecordingLauncherStateStore(initial);
        var repository = new RecordingLauncherRepository(Launcher101) { ForcedIssue = issue };
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App101, App100)),
            launcherStore,
            repository,
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, launcherStore.SaveCount);
        Assert.Same(initial, launcherStore.Current);
    }

    /// <summary>Two roots cannot share one state path and mutate launcher authority.</summary>
    [Fact]
    public async Task DifferentManagedRootSharingStatePathFailsBeforeInventoryOrMutation()
    {
        var appStore = new RecordingAppStateStore(AppState(App100, managedRoot: Root + "-other"));
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var repository = new RecordingLauncherRepository(Launcher100);
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            repository,
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.ManagedRootMismatch, result.Outcome);
        Assert.Equal(0, repository.VerifyCount);
        Assert.Equal(0, launcherStore.LoadCount);
        Assert.Equal(0, launcherStore.SaveCount);
        Assert.Empty(process.Started);
    }

    /// <summary>Failure to reload after READY leaves the power-safe candidate phase.</summary>
    [Fact]
    public async Task PostReadyReloadFailureLeavesRecordedCandidateForPowerSafeRollback()
    {
        var appStore = new RecordingAppStateStore(AppState(App101, App100))
        {
            FailLoadAfter = 1,
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(
            LauncherActivationPhase.CandidateLaunchRecorded,
            launcherStore.Current!.Pending!.Phase);
        Assert.Equal(Launcher100, launcherStore.Current.Active);
    }

    /// <summary>A requested transaction resumes only its exact candidate.</summary>
    [Fact]
    public async Task RestartFromRequestedStartsOnlyTheRecordedCandidate()
    {
        var pending = PendingLauncherActivation.Create(
            Launcher101,
            Launcher100,
            Launcher100,
            LauncherActivationPhase.Requested);
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App101, App100)),
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.Ready, result.Outcome);
        Assert.Equal([Launcher101], process.Started);
        Assert.Equal(Launcher101, launcherStore.Current!.Active);
    }

    /// <summary>Every candidate journal save is fail-closed at its exact phase.</summary>
    [Theory]
    [InlineData(1, 0, -1)]
    [InlineData(2, 0, (int)LauncherActivationPhase.Requested)]
    [InlineData(3, 1, (int)LauncherActivationPhase.CandidateLaunchRecorded)]
    public async Task EveryCandidateStateSaveFailureFailsClosed(
        int failedSave,
        int expectedStarts,
        int expectedPhaseValue)
    {
        LauncherActivationPhase? expectedPhase = expectedPhaseValue < 0
            ? null
            : (LauncherActivationPhase)expectedPhaseValue;
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null))
        {
            FailSaveAt = failedSave,
        };
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App101, App100)),
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(expectedStarts, process.Started.Count);
        Assert.Equal(expectedPhase, launcherStore.Current!.Pending?.Phase);
        Assert.Equal(Launcher100, launcherStore.Current.Active);
    }

    /// <summary>Changed app admission after outer READY aborts launcher commit.</summary>
    [Fact]
    public async Task ChangedCommittedAppAdmissionAfterOuterReadyAbortsLauncherCommit()
    {
        VersionManagerState original = AppState(App101, App100);
        var changed = VersionManagerState.Create(
            updateSource: null,
            App101,
            App100,
            [
                new ManagedVersionAdmission(App101, "replacement-admission", new string('d', 64)),
                new ManagedVersionAdmission(App100, $"admission-{App100}", new string('c', 64)),
            ],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: Root);
        var appStore = new RecordingAppStateStore(original)
        {
            StateAfterFirstLoad = changed,
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateChanged, result.Outcome);
        Assert.Equal(LauncherActivationPhase.CandidateLaunchRecorded, launcherStore.Current!.Pending!.Phase);
        Assert.Equal(Launcher100, launcherStore.Current.Active);
    }

    /// <summary>Pending app filesystem mutation prevents any launcher process or state change.</summary>
    [Fact]
    public async Task PendingAppMutationStartsNoProcessAndMutatesNoLauncherState()
    {
        VersionManagerState appState = AppStateWithPendingMutation();
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(appState),
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, launcherStore.SaveCount);
    }

    /// <summary>An app activation and a requested launcher transaction cannot coexist or start either process.</summary>
    [Fact]
    public async Task PendingAppActivationWithLauncherTransactionIsRejectedBeforeStartOrSave()
    {
        PendingLauncherActivation pending = PendingLauncherActivation.Create(
            Launcher101,
            Launcher100,
            Launcher100,
            LauncherActivationPhase.Requested);
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppStateWithPendingActivation()),
            launcherStore,
            new RecordingLauncherRepository(Launcher100, Launcher101),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, launcherStore.SaveCount);
    }

    /// <summary>An app filesystem mutation and a requested launcher transaction cannot coexist.</summary>
    [Fact]
    public async Task PendingAppMutationWithLauncherTransactionIsRejectedBeforeStartOrSave()
    {
        PendingLauncherActivation pending = PendingLauncherActivation.Create(
            Launcher101,
            Launcher100,
            Launcher100,
            LauncherActivationPhase.Requested);
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppStateWithPendingMutation()),
            launcherStore,
            new RecordingLauncherRepository(Launcher100, Launcher101),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, launcherStore.SaveCount);
    }

    /// <summary>A new app activation after candidate READY aborts launcher commit at the durable reload.</summary>
    [Fact]
    public async Task AppActivationAfterCandidateReadyLeavesCandidateRecorded()
    {
        var appStore = new RecordingAppStateStore(AppState(App101, App100))
        {
            StateAfterFirstLoad = AppStateWithPendingActivation(),
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100, Launcher101),
            new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Equal(LauncherActivationPhase.CandidateLaunchRecorded, launcherStore.Current!.Pending!.Phase);
        Assert.Equal(2, launcherStore.SaveCount);
    }

    /// <summary>A new app filesystem mutation after candidate failure blocks rollback journal mutation and start.</summary>
    [Fact]
    public async Task AppMutationAfterCandidateFailureLeavesCandidateRecordedWithoutRollbackStart()
    {
        var appStore = new RecordingAppStateStore(AppState(App101, App100))
        {
            StateAfterFirstLoad = AppStateWithPendingMutation(),
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(
            LauncherProcessStartOutcome.StartFailed,
            LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100, Launcher101),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Equal([Launcher101], process.Started);
        Assert.Equal(LauncherActivationPhase.CandidateLaunchRecorded, launcherStore.Current!.Pending!.Phase);
        Assert.Equal(2, launcherStore.SaveCount);
    }

    /// <summary>First-launch failure without LKG never scans another directory.</summary>
    [Fact]
    public async Task FirstLaunchFailureWithoutLastKnownGoodNeverScansForFallback()
    {
        var launcherStore = new RecordingLauncherStateStore(load: null);
        var repository = new RecordingLauncherRepository(Launcher100);
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.StartFailed);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App100)),
            launcherStore,
            repository,
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StartFailed, result.Outcome);
        Assert.Equal([Launcher100], process.Started);
        Assert.Null(launcherStore.Current!.Active);
        Assert.Null(launcherStore.Current.LastKnownGood);
        Assert.Equal(Launcher100, launcherStore.Current.Failed);
        Assert.Null(launcherStore.Current.Pending);
        Assert.Equal(1, repository.VerifyCount);
    }

    /// <summary>Failure to persist the terminal first-launch failure leaves the recorded launch uncertain.</summary>
    [Fact]
    public async Task FirstLaunchFailureSaveFailureKeepsRecordedCandidate()
    {
        var launcherStore = new RecordingLauncherStateStore(load: null)
        {
            FailSaveAt = 3,
        };

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App100)),
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            new RecordingLauncherProcess(LauncherProcessStartOutcome.StartFailed))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(LauncherActivationPhase.CandidateLaunchRecorded, launcherStore.Current!.Pending!.Phase);
        Assert.Null(launcherStore.Current.Active);
        Assert.Null(launcherStore.Current.Failed);
    }

    /// <summary>Rollback record and rollback commit save failures remain at their last durable phase.</summary>
    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public async Task EveryRollbackStateSaveFailureFailsClosed(
        int failedSave,
        int expectedStarts)
    {
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null))
        {
            FailSaveAt = failedSave,
        };
        var process = new RecordingLauncherProcess(
            LauncherProcessStartOutcome.StartFailed,
            LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App101, App100)),
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(expectedStarts, process.Started.Count);
        Assert.Equal(
            failedSave == 3
                ? LauncherActivationPhase.CandidateLaunchRecorded
                : LauncherActivationPhase.RollbackLaunchRecorded,
            launcherStore.Current!.Pending!.Phase);
        Assert.Equal(Launcher100, launcherStore.Current.Active);
    }

    /// <summary>A tampered exact fallback remains recorded and is never started.</summary>
    [Fact]
    public async Task TamperedRecordedFallbackIsNeverStarted()
    {
        var pending = PendingLauncherActivation.Create(
            Launcher101,
            Launcher100,
            Launcher100,
            LauncherActivationPhase.CandidateLaunchRecorded);
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending, failed: null));
        var repository = new RecordingLauncherRepository(Launcher101, Launcher100);
        repository.Issues[App100] = InstalledLauncherIssue.Tampered;
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App101, App100)),
            launcherStore,
            repository,
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.RollbackUnavailable, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(LauncherActivationPhase.RollbackLaunchRecorded, launcherStore.Current!.Pending!.Phase);
    }
}
