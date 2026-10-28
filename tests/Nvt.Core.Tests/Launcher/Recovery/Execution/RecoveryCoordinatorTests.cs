// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Locks explicit execution, canonical lease ownership, and plan revalidation.</summary>
public sealed class RecoveryCoordinatorTests
{
    /// <summary>The execution entry point rejects every nonpositive writer budget before acquisition.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    public async Task NonPositiveWriterTimeoutFailsBeforeLease(long ticks)
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            _ = await harness.Coordinator.ExecuteAsync(plan, plan.Action, TimeSpan.FromTicks(ticks),
                TestContext.Current.CancellationToken);
        });
        Assert.Equal(0, harness.State.LeaseCount);
        Assert.Equal(0, harness.Execution.CallCount);
    }

    /// <summary>The smallest positive writer budget and its adjacent value remain accepted.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    public async Task MinimumPositiveWriterTimeoutIsAccepted(long ticks)
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        var result = await harness.Coordinator.ExecuteAsync(plan, plan.Action, TimeSpan.FromTicks(ticks),
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.Completed, result.Outcome);
        Assert.Equal(1, harness.State.LeaseCount);
    }

    /// <summary>Rollback cannot acquire the writer or mutate without its explicit action.</summary>
    [Fact]
    public async Task MissingRollbackConfirmationDoesNotAcquireLeaseOrExecute()
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan,
            confirmedAction: null,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.ConfirmationRequired, result.Outcome);
        Assert.Equal(0, harness.State.LeaseCount);
        Assert.Equal(0, harness.Execution.CallCount);
    }

    /// <summary>A different action never refreshes or converts the immutable plan.</summary>
    [Fact]
    public async Task DifferentActionReturnsRecoveryRequiredWithoutLease()
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan,
            ManagedSetupRecoveryAction.ConvergeReady,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(0, harness.State.LeaseCount);
    }

    /// <summary>Canonical lease contention has a stable outcome and reaches no execution adapter.</summary>
    [Theory]
    [InlineData(VersionManagerWriteLeaseIssue.Busy, ManagedSetupRecoveryExecutionOutcome.Busy)]
    [InlineData(VersionManagerWriteLeaseIssue.Unavailable,
        ManagedSetupRecoveryExecutionOutcome.StateUnavailable)]
    public async Task WriterLeaseIssueStopsBeforeReobservation(
        VersionManagerWriteLeaseIssue issue,
        ManagedSetupRecoveryExecutionOutcome expected)
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        harness.State.LeaseIssue = issue;

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan,
            plan.Action,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, harness.State.LeaseCount);
        Assert.Equal(0, harness.Execution.CallCount);
    }

    /// <summary>Every representative pre-handoff cancellation releases any acquired lease.</summary>
    [Theory]
    [InlineData(RecoveryExecutionCancellationBoundary.WriterLease)]
    [InlineData(RecoveryExecutionCancellationBoundary.Lifetime)]
    [InlineData(RecoveryExecutionCancellationBoundary.ApplicationState)]
    [InlineData(RecoveryExecutionCancellationBoundary.Root)]
    [InlineData(RecoveryExecutionCancellationBoundary.Marker)]
    [InlineData(RecoveryExecutionCancellationBoundary.Evidence)]
    public async Task PreHandoffCancellationReleasesLeaseAndNeverCallsExecutionPort(
        RecoveryExecutionCancellationBoundary boundary)
    {
        ManagedSetupRecoveryTransaction transaction = RecoveryTestData.Transaction(
            ManagedSetupRecoveryPhase.Staging);
        ManagedSetupRecoveryEvidenceObservation exactEvidence = RecoveryTestData.Evidence(
            RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await DiagnoseStagingPlanAsync(transaction, exactEvidence);

        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        ExecutionStateStore state =
            boundary == RecoveryExecutionCancellationBoundary.ApplicationState
            ? new ExecutionStateStore(
                [RecoveryTestData.MissingAppState],
                beforeReturn: cancel)
            : new ExecutionStateStore(RecoveryTestData.MissingAppState);
        var root = new RecordingRecoveryRootProbe(
            ManagedInstallationRootStatus.Residue,
            boundary == RecoveryExecutionCancellationBoundary.Root ? cancel : null);
        var marker = new RecordingRecoveryMarkerProbe(
            [new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Exact, transaction)],
            boundary == RecoveryExecutionCancellationBoundary.Marker ? cancel : null);
        var lifetime = new RecordingRecoveryLifetimeProbe(kind =>
        {
            if (boundary == RecoveryExecutionCancellationBoundary.Lifetime &&
                kind == ManagedProcessLifetimeKind.Bootstrap)
            {
                cancel();
            }
        });
        var evidence = new RecordingRecoveryEvidenceProbe(
            [exactEvidence],
            boundary == RecoveryExecutionCancellationBoundary.Evidence ? cancel : null);
        var execution = new RecordingRecoveryExecutionPort();
        var coordinator = new ManagedSetupRecoveryExecutionCoordinator(
            state,
            root,
            marker,
            lifetime,
            evidence,
            execution);
        if (boundary == RecoveryExecutionCancellationBoundary.WriterLease)
        {
            await cancellation.CancelAsync();
        }

        ManagedSetupRecoveryExecutionResult result = await coordinator.ExecuteAsync(
            plan,
            plan.Action,
            TimeSpan.FromSeconds(1),
            cancellation.Token);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.Cancelled, result.Outcome);
        Assert.False(state.LeaseHeld);
        if (boundary == RecoveryExecutionCancellationBoundary.WriterLease)
        {
            Assert.Null(state.LastLease);
        }
        else
        {
            Assert.NotNull(state.LastLease);
        }
        Assert.Equal(0, execution.CallCount);
    }

    /// <summary>
    /// Missing/missing accepts both exact marker-derived prefix shapes: Launcher not created yet,
    /// or a Launcher identity already verified by Infrastructure for the still-exact prefix.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingMissingExactPrefixAllowsAbsentOrVerifiedLauncherIdentity(
        bool infrastructureVerifiedInstalledLauncher)
    {
        ExecutionHarness harness = ExecutionHarness.Create(
            RecoveryTestData.MissingAppState,
            new ManagedSetupRecoveryFact(
                ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging)),
            RecoveryTestData.Evidence(
                RecoveryLauncherCase.Missing,
                includeInstalledLauncher: infrastructureVerifiedInstalledLauncher));
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan,
            plan.Action,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, plan.Action);
        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.Completed, result.Outcome);
    }

    /// <summary>All three lifetimes are re-read under the lease and active roles block mutation.</summary>
    [Theory]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap)]
    [InlineData(ManagedProcessLifetimeKind.Application)]
    [InlineData(ManagedProcessLifetimeKind.Launcher)]
    public async Task ActiveLifetimeUnderLeaseReturnsLifetimeActive(
        ManagedProcessLifetimeKind active)
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        harness.Lifetime.Calls.Clear();
        harness.Lifetime.Statuses[active] = ManagedProcessLifetimeStatus.Active;

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan,
            plan.Action,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.LifetimeActive, result.Outcome);
        Assert.Equal(3, harness.Lifetime.Calls.Count);
        Assert.False(harness.State.LeaseHeld);
        Assert.Equal(0, harness.Execution.CallCount);
    }

    /// <summary>State, marker, and proof generations must still match the immutable plan.</summary>
    [Fact]
    public async Task EvidenceTokenDriftReturnsRecoveryRequiredWithoutExecution()
    {
        ManagedSetupRecoveryFact transaction = new(
            ManagedSetupRecoveryFactKind.Exact,
            RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging));
        var state = new ExecutionStateStore(
            RecoveryTestData.MissingAppState,
            RecoveryTestData.MissingAppState);
        var evidence = new RecordingRecoveryEvidenceProbe(
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing, "before"),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing, "after"));
        var execution = new RecordingRecoveryExecutionPort();
        var diagnosis = new ManagedInstallationRecoveryExperience(
            state,
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new RecordingRecoveryMarkerProbe(transaction, transaction),
            new RecordingRecoveryLifetimeProbe(),
            evidence);
        var coordinator = new ManagedSetupRecoveryExecutionCoordinator(
            state,
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new RecordingRecoveryMarkerProbe(transaction, transaction),
            new RecordingRecoveryLifetimeProbe(),
            evidence,
            execution);
        ManagedSetupRecoveryPlan plan = Assert.IsType<ManagedSetupRecoveryPlan>(
            (await diagnosis.DiagnoseAsync(
                RecoveryTestData.ManagedRoot,
                TestContext.Current.CancellationToken)).Plan);

        ManagedSetupRecoveryExecutionResult result = await coordinator.ExecuteAsync(
            plan,
            plan.Action,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(0, execution.CallCount);
    }

    /// <summary>Every immutable-plan family rejects drift before the execution port.</summary>
    [Theory]
    [InlineData(RecoveryPlanDriftCase.ApplicationSnapshot)]
    [InlineData(RecoveryPlanDriftCase.TransactionIdentity)]
    [InlineData(RecoveryPlanDriftCase.TransactionPhase)]
    [InlineData(RecoveryPlanDriftCase.TransactionOwnedPaths)]
    [InlineData(RecoveryPlanDriftCase.TransactionPayload)]
    [InlineData(RecoveryPlanDriftCase.TransactionCandidate)]
    [InlineData(RecoveryPlanDriftCase.Action)]
    [InlineData(RecoveryPlanDriftCase.EvidenceToken)]
    public async Task EveryPlanBoundFamilyDriftReturnsRecoveryRequiredWithoutPort(
        RecoveryPlanDriftCase drift)
    {
        ManagedSetupRecoveryTransaction original = RecoveryTestData.Transaction(
            ManagedSetupRecoveryPhase.BootstrapLaunchRecorded);
        ManagedSetupRecoveryTransaction current = MutateTransaction(original, drift);
        VersionManagerStateLoadResult currentState =
            drift == RecoveryPlanDriftCase.ApplicationSnapshot
                ? RecoveryTestData.NonCanonicalBoundAppState
                : RecoveryTestData.CanonicalAppState;
        ManagedSetupRecoveryEvidenceObservation currentEvidence = RecoveryTestData.Evidence(
            RecoveryLauncherCase.Missing,
            drift == RecoveryPlanDriftCase.EvidenceToken ? "changed" : "evidence-1");
        var harness = new ExecutionHarness(
            new ExecutionStateStore(
                RecoveryTestData.CanonicalAppState,
                currentState),
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new RecordingRecoveryMarkerProbe(
                new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Exact, original),
                new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Exact, current)),
            new RecordingRecoveryLifetimeProbe(),
            new RecordingRecoveryEvidenceProbe(
                RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
                currentEvidence),
            new RecordingRecoveryExecutionPort());
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        if (drift == RecoveryPlanDriftCase.Action)
        {
            plan = new ManagedSetupRecoveryPlan(
                plan.ManagedRoot,
                plan.Transaction,
                ManagedSetupRecoveryAction.ConvergeReady,
                RecoveryTestData.CanonicalAppState,
                plan.Evidence);
        }

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan,
            plan.Action,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(0, harness.Execution.CallCount);
    }

    /// <summary>Unavailable exact state or inventory evidence has the stable StateUnavailable outcome.</summary>
    [Fact]
    public async Task EvidenceStateUnavailableRemainsStateUnavailable()
    {
        ManagedSetupRecoveryFact transaction = new(
            ManagedSetupRecoveryFactKind.Exact,
            RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging));
        var harness = new ExecutionHarness(
            new ExecutionStateStore(RecoveryTestData.MissingAppState),
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new RecordingRecoveryMarkerProbe(transaction),
            new RecordingRecoveryLifetimeProbe(),
            new RecordingRecoveryEvidenceProbe(
                RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
                new ManagedSetupRecoveryEvidenceObservation(
                    ManagedSetupRecoveryEvidenceIssue.StateUnavailable)),
            new RecordingRecoveryExecutionPort());
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan, plan.Action, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(0, harness.Execution.CallCount);
    }

    /// <summary>The port receives the same fake lease instance while its lexical scope is active.</summary>
    [Theory]
    [InlineData(RecoveryLauncherCase.Missing,
        ManagedSetupRecoveryAction.RemoveIncompleteInstallation)]
    [InlineData(RecoveryLauncherCase.Ready, ManagedSetupRecoveryAction.ConvergeReady)]
    public async Task StablePlanHandsSameLeaseInstanceToPortInsideLexicalScope(
        RecoveryLauncherCase launcherCase,
        ManagedSetupRecoveryAction expectedAction)
    {
        ExecutionHarness harness = CreateActionable(launcherCase);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        bool leaseSeenByPort = false;
        harness.Execution.OnExecute = request =>
        {
            leaseSeenByPort = harness.State.LeaseHeld;
            Assert.Equal(expectedAction, request.Action);
            return ValueTask.CompletedTask;
        };

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan,
            plan.Action,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.Completed, result.Outcome);
        Assert.True(leaseSeenByPort);
        Assert.Same(harness.State.LastLease, harness.Execution.WriterLease);
        Assert.False(harness.State.LeaseHeld);
        Assert.Equal(1, harness.Execution.CallCount);
    }

    /// <summary>A non-mutating terminal port result permits an unchanged immutable-plan retry.</summary>
    [Theory]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable,
        ManagedSetupRecoveryExecutionOutcome.StateUnavailable)]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.Cancelled,
        ManagedSetupRecoveryExecutionOutcome.Cancelled)]
    public async Task NonMutatingTerminalAllowsUnchangedPlanRetryToComplete(
        ManagedSetupRecoveryExecutionPortOutcome firstPortOutcome,
        ManagedSetupRecoveryExecutionOutcome firstExpected)
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        harness.Execution.Outcome = firstPortOutcome;

        ManagedSetupRecoveryExecutionResult first = await harness.Coordinator.ExecuteAsync(
            plan, plan.Action, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        harness.Execution.Outcome = ManagedSetupRecoveryExecutionPortOutcome.Completed;
        ManagedSetupRecoveryExecutionResult second = await harness.Coordinator.ExecuteAsync(
            plan, plan.Action, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(firstExpected, first.Outcome);
        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.Completed, second.Outcome);
        Assert.Equal(2, harness.Execution.CallCount);
    }

    /// <summary>Unexpected cancellation after port handoff is conservatively recovery-required.</summary>
    [Fact]
    public async Task UnexpectedPortCancellationReturnsRecoveryRequired()
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        harness.Execution.OnExecute = _ =>
            ValueTask.FromException(new OperationCanceledException("unexpected port cancellation"));

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan, plan.Action, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionOutcome.RecoveryRequired, result.Outcome);
        Assert.False(harness.State.LeaseHeld);
    }

    /// <summary>Every low-level terminal fact is projected to its stable Application outcome.</summary>
    [Theory]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.HealthUnavailable,
        ManagedSetupRecoveryExecutionOutcome.HealthUnavailable)]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable,
        ManagedSetupRecoveryExecutionOutcome.StateUnavailable)]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.PermissionDenied,
        ManagedSetupRecoveryExecutionOutcome.PermissionDenied)]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged,
        ManagedSetupRecoveryExecutionOutcome.SourceChanged)]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired,
        ManagedSetupRecoveryExecutionOutcome.RecoveryRequired)]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired,
        ManagedSetupRecoveryExecutionOutcome.ManualInterventionRequired)]
    [InlineData(ManagedSetupRecoveryExecutionPortOutcome.Cancelled,
        ManagedSetupRecoveryExecutionOutcome.Cancelled)]
    public async Task ExecutionPortOutcomesRemainStable(
        ManagedSetupRecoveryExecutionPortOutcome portOutcome,
        ManagedSetupRecoveryExecutionOutcome expected)
    {
        ExecutionHarness harness = CreateActionable(RecoveryLauncherCase.Missing);
        ManagedSetupRecoveryPlan plan = await PlanAsync(harness);
        harness.Execution.Outcome = portOutcome;

        ManagedSetupRecoveryExecutionResult result = await harness.Coordinator.ExecuteAsync(
            plan, plan.Action, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
    }

    private static ExecutionHarness CreateActionable(RecoveryLauncherCase launcherCase)
    {
        bool missingApp = launcherCase == RecoveryLauncherCase.Missing;
        ManagedSetupRecoveryPhase phase = missingApp
            ? ManagedSetupRecoveryPhase.Staging
            : ManagedSetupRecoveryPhase.BootstrapLaunchRecorded;
        return ExecutionHarness.Create(
            missingApp ? RecoveryTestData.MissingAppState : RecoveryTestData.CanonicalAppState,
            new ManagedSetupRecoveryFact(
                ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(phase)),
            RecoveryTestData.Evidence(launcherCase));
    }

    private static async Task<ManagedSetupRecoveryPlan> PlanAsync(ExecutionHarness harness)
    {
        ManagedSetupRecoveryDiagnosis diagnosis = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);
        return Assert.IsType<ManagedSetupRecoveryPlan>(diagnosis.Plan);
    }

    private static async Task<ManagedSetupRecoveryPlan> DiagnoseStagingPlanAsync(
        ManagedSetupRecoveryTransaction transaction,
        ManagedSetupRecoveryEvidenceObservation evidence)
    {
        var diagnosis = new ManagedInstallationRecoveryExperience(
            new ExecutionStateStore(RecoveryTestData.MissingAppState),
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new RecordingRecoveryMarkerProbe(
                new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Exact, transaction)),
            new RecordingRecoveryLifetimeProbe(),
            new RecordingRecoveryEvidenceProbe(evidence));
        return Assert.IsType<ManagedSetupRecoveryPlan>(
            (await diagnosis.DiagnoseAsync(
                RecoveryTestData.ManagedRoot,
                TestContext.Current.CancellationToken)).Plan);
    }

    private static ManagedSetupRecoveryTransaction MutateTransaction(
        ManagedSetupRecoveryTransaction original,
        RecoveryPlanDriftCase drift)
    {
        string transactionId = drift == RecoveryPlanDriftCase.TransactionIdentity
            ? "fedcba9876543210fedcba9876543210"
            : original.TransactionId;
        ManagedSetupRecoveryPhase phase = drift == RecoveryPlanDriftCase.TransactionPhase
            ? ManagedSetupRecoveryPhase.RootPromoted
            : original.Phase;
        IReadOnlyList<string> ownedPaths = drift == RecoveryPlanDriftCase.TransactionOwnedPaths
            ? [.. original.OwnedPaths, "unexpected"]
            : original.OwnedPaths;
        ManagedSetupRecoveryPayloadIdentity payload =
            drift == RecoveryPlanDriftCase.TransactionPayload
                ? original.Payload with { LauncherSize = original.Payload.LauncherSize + 1 }
                : original.Payload;
        ManagedSetupRecoveryCandidateIdentity candidate =
            drift == RecoveryPlanDriftCase.TransactionCandidate
                ? original.Candidate with
                {
                    RegistryRevision = original.Candidate.RegistryRevision + 1,
                }
                : original.Candidate;
        return new(
            transactionId,
            original.ManagedRootIdentity,
            original.StatePathIdentity,
            phase,
            ownedPaths,
            payload,
            candidate);
    }
}

/// <summary>Immutable plan-bound families independently mutated by execution tests.</summary>
public enum RecoveryPlanDriftCase
{
    /// <summary>Application durable state snapshot.</summary>
    ApplicationSnapshot,
    /// <summary>Setup transaction identifier.</summary>
    TransactionIdentity,
    /// <summary>Setup transaction phase.</summary>
    TransactionPhase,
    /// <summary>Setup-owned path sequence.</summary>
    TransactionOwnedPaths,
    /// <summary>Distribution payload identity.</summary>
    TransactionPayload,
    /// <summary>Registry, Catalog, and package candidate identity.</summary>
    TransactionCandidate,
    /// <summary>Application-selected action.</summary>
    Action,
    /// <summary>Opaque exact evidence token.</summary>
    EvidenceToken,
}

/// <summary>Representative coordinator boundaries before execution-port handoff.</summary>
public enum RecoveryExecutionCancellationBoundary
{
    /// <summary>Canonical writer lease acquisition.</summary>
    WriterLease,
    /// <summary>Managed process lifetime re-observation.</summary>
    Lifetime,
    /// <summary>Application durable state reload.</summary>
    ApplicationState,
    /// <summary>Managed root re-observation.</summary>
    Root,
    /// <summary>Exact Setup marker re-observation.</summary>
    Marker,
    /// <summary>Candidate, Launcher, inventory, and prefix evidence re-observation.</summary>
    Evidence,
}
