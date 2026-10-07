// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Tests launcher ready commit and bounded fallback orchestration.</summary>
public sealed partial class ManagedActivationCoordinatorTests
{
    /// <summary>Pending ready commits active and last-known-good.</summary>
    [Fact]
    public async Task ReadyCommitsCandidate()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(State(), ManagedAppVersion.Parse("0.10.6"));
        var store = new FakeStateStore(pending);
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.Ready, result.Outcome);
        Assert.Equal("0.10.6", store.State.ActiveVersion?.ToString());
        Assert.Equal("0.10.6", store.State.LastKnownGoodVersion?.ToString());
        Assert.Equal(["0.10.6"], process.Starts);
    }

    /// <summary>Candidate failure starts the prior last-known-good once and clears pending before fallback.</summary>
    [Fact]
    public async Task FailureRollsBackExactlyOnce()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(State(), ManagedAppVersion.Parse("0.10.6"));
        var store = new FakeStateStore(pending);
        var process = new FakeProcess(
            ManagedProcessStartOutcome.ReadyTimeout,
            ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.RolledBack, result.Outcome);
        Assert.Equal(["0.10.6", "0.10.5"], process.Starts);
        Assert.Null(store.State.PendingActivation);
        Assert.Equal("0.10.6", store.State.FailedActivationVersion?.ToString());
    }

    /// <summary>Missing or unreadable launcher state never guesses an active version.</summary>
    [Theory]
    [InlineData(VersionManagerStateLoadIssue.Missing)]
    [InlineData(VersionManagerStateLoadIssue.Invalid)]
    [InlineData(VersionManagerStateLoadIssue.Unavailable)]
    public async Task InvalidStateReturnsTypedOutcome(VersionManagerStateLoadIssue issue)
    {
        var process = new FakeProcess();
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new LoadIssueStateStore(issue),
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.InvalidState, result.Outcome);
        Assert.Empty(process.Starts);
    }

    /// <summary>A valid empty state reports that no active version is selectable.</summary>
    [Fact]
    public async Task EmptyStateReportsNoActiveVersion()
    {
        VersionManagerState state = VersionManagerState.Create(
            null, null, null, [], null, null, false, managedRootIdentity: "managed");
        var process = new FakeProcess();
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(state),
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.NoActiveVersion, result.Outcome);
        Assert.Empty(process.Starts);
    }

    /// <summary>A launcher cannot use Active or LKG admissions owned by another managed root.</summary>
    [Fact]
    public async Task DifferentManagedRootCannotLaunchActiveOrLastKnownGood()
    {
        VersionManagerState state = State();
        var process = new FakeProcess();
        var coordinator = new ManagedActivationCoordinator(
            "other-managed-root",
            new FakeStateStore(state),
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.InvalidState, result.Outcome);
        Assert.Null(result.RunningVersion);
        Assert.Null(result.FailedVersion);
        Assert.Empty(process.Starts);
    }

    /// <summary>A damaged committed active version cannot launch and has no implicit rollback.</summary>
    [Fact]
    public async Task DamagedCommittedVersionDoesNotLaunch()
    {
        var process = new FakeProcess();
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(State()),
            new HealthyRepository("0.10.5"),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.DamagedVersion, result.Outcome);
        Assert.Equal("0.10.5", result.FailedVersion?.ToString());
        Assert.Empty(process.Starts);
    }

    /// <summary>A failed committed process returns StartFailed without launching last-known-good again.</summary>
    [Fact]
    public async Task CommittedVersionStartFailureDoesNotAutoRollback()
    {
        var process = new FakeProcess(ManagedProcessStartOutcome.ExitedBeforeReady);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(State()),
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StartFailed, result.Outcome);
        Assert.Equal(["0.10.5"], process.Starts);
    }

    /// <summary>A damaged pending candidate skips candidate start and launches healthy rollback once.</summary>
    [Fact]
    public async Task DamagedPendingCandidateRollsBackBeforeProcessStart()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(pending),
            new HealthyRepository("0.10.6"),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.RolledBack, result.Outcome);
        Assert.Equal(["0.10.5"], process.Starts);
    }

    /// <summary>A fallback process that also misses readiness ends after exactly two starts.</summary>
    [Fact]
    public async Task FailedFallbackStopsAfterOneRollbackAttempt()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        var process = new FakeProcess(
            ManagedProcessStartOutcome.ReadyTimeout,
            ManagedProcessStartOutcome.InvalidReadySignal);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(pending),
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StartFailed, result.Outcome);
        Assert.Equal(["0.10.6", "0.10.5"], process.Starts);
    }

    /// <summary>A failed candidate-launch journal save prevents any process from starting.</summary>
    [Fact]
    public async Task CandidateLaunchJournalFailureStartsNoProcess()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        var store = new FailingStateStore(pending, failOnSave: 1);
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);

        ManagedLauncherResult result = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, result.Outcome);
        Assert.Empty(process.Starts);
        Assert.Equal(VersionActivationPhase.Requested, store.State.PendingActivation?.Phase);
    }

    /// <summary>A failed rollback-journal save prevents fallback launch after candidate failure.</summary>
    [Fact]
    public async Task RollbackLaunchJournalFailureStartsNoFallback()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        var store = new FailingStateStore(pending, failOnSave: 2);
        var process = new FakeProcess(ManagedProcessStartOutcome.ReadyTimeout);

        ManagedLauncherResult result = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(["0.10.6"], process.Starts);
        Assert.Equal(VersionActivationPhase.CandidateLaunchRecorded, store.State.PendingActivation?.Phase);
    }

    /// <summary>An unavailable rollback target closes the durable journal without starting an arbitrary process.</summary>
    [Fact]
    public async Task MissingRollbackTargetCommitsTerminalFailure()
    {
        ManagedAppVersion current = ManagedAppVersion.Parse("0.10.5");
        VersionManagerState state = VersionManagerState.Create(
            null,
            current,
            lastKnownGoodVersion: null,
            [Admission("0.10.5"), Admission("0.10.6")],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed");
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            state,
            ManagedAppVersion.Parse("0.10.6"));
        var store = new FakeStateStore(pending);
        var process = new FakeProcess(ManagedProcessStartOutcome.ReadyTimeout);

        ManagedLauncherResult result = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StartFailed, result.Outcome);
        Assert.Equal(["0.10.6"], process.Starts);
        Assert.Null(store.State.PendingActivation);
        Assert.Equal("0.10.6", store.State.FailedActivationVersion?.ToString());
    }

    /// <summary>An uncertain ready commit is never retried as another candidate launch after restart.</summary>
    [Fact]
    public async Task ReadyCommitFailureRestartsDirectlyIntoRecordedRollback()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        var store = new FailingStateStore(pending, failOnSave: 2);
        var firstProcess = new FakeProcess(ManagedProcessStartOutcome.Ready);
        ManagedLauncherResult interrupted = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            firstProcess,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, interrupted.Outcome);
        Assert.Equal(["0.10.6"], firstProcess.Starts);
        Assert.Equal(VersionActivationPhase.CandidateLaunchRecorded, store.State.PendingActivation?.Phase);

        var restartedProcess = new FakeProcess(ManagedProcessStartOutcome.Ready);
        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            restartedProcess,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(["0.10.5"], restartedProcess.Starts);
        Assert.Null(store.State.PendingActivation);
    }

    /// <summary>A failed rollback commit restarts only the recorded fallback and then converges.</summary>
    [Fact]
    public async Task RollbackCommitFailureRestartsOnlyRecordedFallback()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        var store = new FailingStateStore(pending, failOnSave: 3);
        var firstProcess = new FakeProcess(
            ManagedProcessStartOutcome.ReadyTimeout,
            ManagedProcessStartOutcome.Ready);
        ManagedLauncherResult interrupted = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            firstProcess,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, interrupted.Outcome);
        Assert.Equal(["0.10.6", "0.10.5"], firstProcess.Starts);
        Assert.Equal(VersionActivationPhase.RollbackLaunchRecorded, store.State.PendingActivation?.Phase);

        var restartedProcess = new FakeProcess(ManagedProcessStartOutcome.Ready);
        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            restartedProcess,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(["0.10.5"], restartedProcess.Starts);
        Assert.Null(store.State.PendingActivation);
    }

    /// <summary>A power cut after candidate-launch recording skips that candidate on restart.</summary>
    [Fact]
    public async Task CandidateLaunchRecordedPowerCutRestartsIntoRollbackOnly()
    {
        VersionManagerState requested = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        VersionManagerState recorded = VersionActivationPolicy.RecordCandidateLaunch(requested);
        var store = new FakeStateStore(recorded);
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);

        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(["0.10.5"], process.Starts);
        Assert.Null(store.State.PendingActivation);
    }

    /// <summary>A power cut after rollback-launch recording starts only the exact fallback.</summary>
    [Fact]
    public async Task RollbackLaunchRecordedPowerCutRestartsFallbackOnly()
    {
        VersionManagerState requested = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        VersionManagerState candidateRecorded = VersionActivationPolicy.RecordCandidateLaunch(requested);
        VersionManagerState rollbackRecorded = VersionActivationPolicy.RecordRollbackLaunch(
            candidateRecorded,
            ManagedAppVersion.Parse("0.10.6")).State;
        var store = new FakeStateStore(rollbackRecorded);
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);

        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(["0.10.5"], process.Starts);
        Assert.Null(store.State.PendingActivation);
    }

    /// <summary>A failed terminal save retains rollback phase and never re-launches the candidate.</summary>
    [Fact]
    public async Task FallbackFailureCommitFailureRetriesOnlyFallbackAndConverges()
    {
        VersionManagerState requested = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        VersionManagerState candidateRecorded = VersionActivationPolicy.RecordCandidateLaunch(requested);
        VersionManagerState rollbackRecorded = VersionActivationPolicy.RecordRollbackLaunch(
            candidateRecorded,
            ManagedAppVersion.Parse("0.10.6")).State;
        var store = new FailingStateStore(rollbackRecorded, failOnSave: 1);
        var firstProcess = new FakeProcess(ManagedProcessStartOutcome.InvalidReadySignal);

        ManagedLauncherResult interrupted = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            firstProcess,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, interrupted.Outcome);
        Assert.Equal(["0.10.5"], firstProcess.Starts);
        Assert.Equal(VersionActivationPhase.RollbackLaunchRecorded, store.State.PendingActivation?.Phase);

        var secondProcess = new FakeProcess(ManagedProcessStartOutcome.Ready);
        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            secondProcess,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(["0.10.5"], secondProcess.Starts);
        Assert.Null(store.State.PendingActivation);
    }

    /// <summary>A failed terminal save with no rollback target closes on the next restart without a process.</summary>
    [Fact]
    public async Task MissingRollbackTerminalSaveFailureClosesOnNextRestart()
    {
        ManagedAppVersion current = ManagedAppVersion.Parse("0.10.5");
        VersionManagerState state = VersionManagerState.Create(
            null,
            current,
            lastKnownGoodVersion: null,
            [Admission("0.10.5"), Admission("0.10.6")],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed");
        VersionManagerState requested = VersionActivationPolicy.BeginActivation(
            state,
            ManagedAppVersion.Parse("0.10.6"));
        VersionManagerState candidateRecorded = VersionActivationPolicy.RecordCandidateLaunch(requested);
        VersionManagerState rollbackRecorded = VersionActivationPolicy.RecordRollbackLaunch(
            candidateRecorded,
            ManagedAppVersion.Parse("0.10.6")).State;
        var store = new FailingStateStore(rollbackRecorded, failOnSave: 1);
        var process = new FakeProcess();

        ManagedLauncherResult interrupted = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, interrupted.Outcome);
        Assert.Equal(VersionActivationPhase.RollbackLaunchRecorded, store.State.PendingActivation?.Phase);

        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1)).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StartFailed, recovered.Outcome);
        Assert.Empty(process.Starts);
        Assert.Null(store.State.PendingActivation);
        Assert.Equal("0.10.6", store.State.FailedActivationVersion?.ToString());
    }

    private static VersionManagerState State()
    {
        ManagedAppVersion old = ManagedAppVersion.Parse("0.10.5");
        return VersionManagerState.Create(
            null,
            old,
            old,
            [Admission("0.10.5"), Admission("0.10.6")],
            null,
            null,
            false,
            managedRootIdentity: "managed");
    }

    private static ManagedVersionAdmission Admission(string version)
    {
        return new(ManagedAppVersion.Parse(version), $"identity-{version}", new string('a', 64));
    }

    private sealed class FakeStateStore(VersionManagerState state) : IVersionManagerStateStore
    {
        internal VersionManagerState State { get; private set; } = state;

        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(VersionManagerWriteLeaseTestSupport.Acquired());
        }

        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new VersionManagerStateLoadResult(State, VersionManagerStateLoadIssue.None));
        }

        public ValueTask SaveAsync(VersionManagerState stateToSave, CancellationToken cancellationToken)
        {
            State = stateToSave;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoadIssueStateStore(VersionManagerStateLoadIssue issue) : IVersionManagerStateStore
    {
        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(VersionManagerWriteLeaseTestSupport.Acquired());
        }

        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new VersionManagerStateLoadResult(null, issue));
        }

        public ValueTask SaveAsync(VersionManagerState stateToSave, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FailingStateStore(
        VersionManagerState state,
        int failOnSave) : IVersionManagerStateStore
    {
        private int _saveCount;

        internal VersionManagerState State { get; private set; } = state;

        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(VersionManagerWriteLeaseTestSupport.Acquired());
        }

        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new VersionManagerStateLoadResult(State, VersionManagerStateLoadIssue.None));
        }

        public ValueTask SaveAsync(VersionManagerState stateToSave, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _saveCount) == failOnSave)
            {
                throw new IOException("Injected activation state failure.");
            }
            State = stateToSave;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeProcess(params ManagedProcessStartOutcome[] outcomes) : IManagedApplicationProcess
    {
        private readonly Queue<ManagedProcessStartOutcome> _outcomes = new(outcomes);

        internal List<string> Starts { get; } = [];
        internal Queue<ManagedProcessLifetimeStatus> LifetimeStatuses { get; } = [];

        public ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(
            string managedRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                LifetimeStatuses.Count == 0
                    ? ManagedProcessLifetimeStatus.Active
                    : LifetimeStatuses.Dequeue());
        }

        public ValueTask<ManagedProcessStartResult> StartUntilReadyAsync(
            string managedRoot,
            ManagedAppVersion version,
            IManagedExecutableLaunchLease executableLease,
            TimeSpan readyDeadline,
            CancellationToken cancellationToken)
        {
            Starts.Add(version.ToString());
            return ValueTask.FromResult(new ManagedProcessStartResult(_outcomes.Dequeue(), null));
        }
    }

    private sealed class HealthyRepository(params string[] damagedVersions) : IManagedVersionRepository
    {
        private readonly HashSet<ManagedAppVersion> _damaged =
            [.. damagedVersions.Select(ManagedAppVersion.Parse)];

        public ManagedExecutableLaunchIssue LaunchLeaseIssue { get; init; }

        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot,
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
            string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions,
            ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion,
            ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(ManagedVersionInventoryReadResult.Success(
                ManagedVersionInventory.Create(admissions.Select(admission =>
            {
                bool failedActivation = failedActivationVersion == admission.Version;
                bool damaged = failedActivation || _damaged.Contains(admission.Version);
                return new InstalledVersionSnapshot(
                    admission.Version,
                    admission.AdmissionIdentity,
                    damaged ? ManagedVersionIntegrity.Damaged : ManagedVersionIntegrity.Healthy,
                    failedActivation
                        ? ManagedVersionDamageReason.FailedActivation
                        : damaged
                            ? ManagedVersionDamageReason.ContentMismatch
                            : null,
                    activeVersion == admission.Version,
                    lastKnownGoodVersion == admission.Version,
                    ManagedVersionAdmissionState.Admitted,
                    admission);
            }))));
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ManagedExecutableLaunchLeaseResult> AcquireApplicationLaunchLeaseAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(LaunchLeaseIssue != ManagedExecutableLaunchIssue.None
                ? new ManagedExecutableLaunchLeaseResult(null, LaunchLeaseIssue)
                : new ManagedExecutableLaunchLeaseResult(
                    NoOpExecutableLease.Instance,
                    ManagedExecutableLaunchIssue.None));
        }
    }

    private sealed class NoOpExecutableLease : IManagedExecutableLaunchLease
    {
        internal static readonly NoOpExecutableLease Instance = new();
        public string ExecutablePath => "SyntheticApp.exe";
        public string WorkingDirectory => ".";
        public bool TryValidateForStart()
        {
            return true;
        }
        public void Dispose() { }
    }

}
