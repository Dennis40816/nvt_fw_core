// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Recovery;
using Xunit;


namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Locks diagnosis planning and the closed first-install state-pair table.</summary>
public sealed partial class ManagedSetupRecoveryDiagnosisTests
{
    private static readonly HashSet<(ManagedSetupRecoveryPhase Phase,
        RecoveryStateCase State, ManagedInstallationRootStatus Root)> _actionableExactTuples =
        [
            (ManagedSetupRecoveryPhase.Staging, RecoveryStateCase.Missing,
                ManagedInstallationRootStatus.Residue),
            (ManagedSetupRecoveryPhase.RootPromoted, RecoveryStateCase.Missing,
                ManagedInstallationRootStatus.Residue),
            (ManagedSetupRecoveryPhase.BootstrapLaunchRecorded, RecoveryStateCase.Missing,
                ManagedInstallationRootStatus.Residue),
            (ManagedSetupRecoveryPhase.BootstrapLaunchRecorded, RecoveryStateCase.Exact,
                ManagedInstallationRootStatus.Residue),
        ];
    private static readonly HashSet<(RecoveryStateCase State,
        ManagedInstallationRootStatus Root)> _healthyAbsentTuples =
        [
            (RecoveryStateCase.Missing, ManagedInstallationRootStatus.Absent),
            (RecoveryStateCase.Exact, ManagedInstallationRootStatus.Present),
        ];

    /// <summary>Each authorized state pair produces exactly its contract-selected immutable action.</summary>
    [Theory]
    [InlineData(RecoveryLauncherCase.Missing,
        ManagedSetupRecoveryAction.RemoveIncompleteInstallation)]
    [InlineData(RecoveryLauncherCase.Requested,
        ManagedSetupRecoveryAction.RemoveIncompleteInstallation)]
    [InlineData(RecoveryLauncherCase.CandidateLaunchRecorded,
        ManagedSetupRecoveryAction.RemoveIncompleteInstallation)]
    [InlineData(RecoveryLauncherCase.Failed,
        ManagedSetupRecoveryAction.RemoveIncompleteInstallation)]
    [InlineData(RecoveryLauncherCase.Ready, ManagedSetupRecoveryAction.ConvergeReady)]
    public async Task CanonicalApplicationRowsProduceOnlyTheirAuthorizedAction(
        RecoveryLauncherCase launcherCase,
        ManagedSetupRecoveryAction expected)
    {
        ManagedSetupRecoveryTransaction transaction = RecoveryTestData.Transaction(
            ManagedSetupRecoveryPhase.BootstrapLaunchRecorded);
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.CanonicalAppState,
            new(ManagedSetupRecoveryFactKind.Exact, transaction),
            RecoveryTestData.Evidence(launcherCase));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.ActionAvailable, result.Outcome);
        Assert.Same(transaction, result.Transaction);
        Assert.NotNull(result.Plan);
        Assert.Equal(expected, result.Plan.Action);
    }

    /// <summary>The missing/missing row remains rollback-eligible for every marker phase.</summary>
    [Theory]
    [InlineData(ManagedSetupRecoveryPhase.Staging)]
    [InlineData(ManagedSetupRecoveryPhase.RootPromoted)]
    [InlineData(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded)]
    public async Task MissingApplicationAndLauncherIsRollbackForEveryPhase(
        ManagedSetupRecoveryPhase phase)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Exact, RecoveryTestData.Transaction(phase)),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.ActionAvailable, result.Outcome);
        Assert.Equal(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, result.Plan?.Action);
    }

    /// <summary>Missing/missing needs exact proof but not an installed Launcher that may not exist yet.</summary>
    [Fact]
    public async Task MissingApplicationAndLauncherAcceptsExactTokenWithoutLauncherIdentity()
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging)),
            RecoveryTestData.Evidence(
                RecoveryLauncherCase.Missing,
                includeInstalledLauncher: false));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.ActionAvailable, result.Outcome);
        Assert.Equal(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, result.Plan?.Action);
    }

    /// <summary>Every App-present row still requires the verified installed Launcher identity.</summary>
    [Theory]
    [InlineData(RecoveryLauncherCase.Missing)]
    [InlineData(RecoveryLauncherCase.Requested)]
    [InlineData(RecoveryLauncherCase.CandidateLaunchRecorded)]
    [InlineData(RecoveryLauncherCase.Failed)]
    [InlineData(RecoveryLauncherCase.Ready)]
    public async Task ApplicationPresentRowsRejectMissingLauncherIdentity(
        RecoveryLauncherCase launcherCase)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.CanonicalAppState,
            new(ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded)),
            RecoveryTestData.Evidence(
                launcherCase,
                includeInstalledLauncher: false));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.ManualInterventionRequired, result.Outcome);
        Assert.Null(result.Plan);
    }

    /// <summary>Every valid but unlisted pair remains non-mutating.</summary>
    [Theory]
    [InlineData(RecoveryLauncherCase.RequestedDifferentCandidate)]
    [InlineData(RecoveryLauncherCase.OrdinaryUpdate)]
    [InlineData(RecoveryLauncherCase.ReadyWithFailure)]
    public async Task UnlistedLauncherPairsExposeNoPlan(RecoveryLauncherCase launcherCase)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.CanonicalAppState,
            new(ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded)),
            RecoveryTestData.Evidence(launcherCase));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.ManualInterventionRequired, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Null(result.Transaction);
    }

    /// <summary>Missing Application state never authorizes deletion of present Launcher state.</summary>
    [Fact]
    public async Task MissingApplicationWithPresentLauncherExposesNoPlan()
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded)),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Requested));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.ManualInterventionRequired, result.Outcome);
        Assert.Null(result.Plan);
    }

    /// <summary>A merely root-bound Application state cannot substitute for the canonical first-run shape.</summary>
    [Fact]
    public async Task NonCanonicalBoundApplicationStateExposesNoPlan()
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.NonCanonicalBoundAppState,
            new(ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded)),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.ManualInterventionRequired, result.Outcome);
        Assert.Null(result.Plan);
    }

    /// <summary>Healthy marker absence retains read-only behavior and creates no plan.</summary>
    [Theory]
    [InlineData(true, ManagedInstallationRootStatus.Absent)]
    [InlineData(false, ManagedInstallationRootStatus.Present)]
    public async Task HealthyMarkerAbsenceRetainsExistingDiagnosis(
        bool stateMissing,
        ManagedInstallationRootStatus root)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            stateMissing ? RecoveryTestData.MissingAppState : RecoveryTestData.CanonicalAppState,
            new(ManagedSetupRecoveryFactKind.Absent, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            root);

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.NoRecoveryNeeded, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, harness.Evidence.CallCount);
    }

    /// <summary>Active lifetime wins before every durable observation.</summary>
    [Fact]
    public async Task ActiveLifetimeBlocksAllDurableObservations()
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Absent, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing));
        harness.Lifetime.Statuses[ManagedProcessLifetimeKind.Application] =
            ManagedProcessLifetimeStatus.Active;

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.Busy, result.Outcome);
        Assert.Equal(3, harness.Lifetime.Calls.Count);
        Assert.Equal(0, harness.State.LoadCount);
        Assert.Equal(0, harness.Root.CallCount);
        Assert.Equal(0, harness.Marker.CallCount);
    }

    /// <summary>Incomplete candidate evidence maps to a closed outcome without a plan.</summary>
    [Theory]
    [InlineData(ManagedSetupRecoveryEvidenceIssue.StateUnavailable,
        ManagedSetupRecoveryOutcome.HealthUnavailable)]
    [InlineData(ManagedSetupRecoveryEvidenceIssue.PermissionDenied,
        ManagedSetupRecoveryOutcome.HealthUnavailable)]
    [InlineData(ManagedSetupRecoveryEvidenceIssue.SourceChanged,
        ManagedSetupRecoveryOutcome.ManualInterventionRequired)]
    [InlineData(ManagedSetupRecoveryEvidenceIssue.Invalid,
        ManagedSetupRecoveryOutcome.ManualInterventionRequired)]
    public async Task IncompleteEvidenceExposesNoPlan(
        ManagedSetupRecoveryEvidenceIssue issue,
        ManagedSetupRecoveryOutcome expected)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Exact,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging)),
            new ManagedSetupRecoveryEvidenceObservation(issue));

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Plan);
    }

    /// <summary>Every exact marker phase is evaluated against every state and root category.</summary>
    [Theory]
    [MemberData(nameof(ExactMarkerMatrix))]
    public async Task ExactMarkerDecisionCoversEveryPhaseStateAndRootCombination(
        ManagedSetupRecoveryPhase phase,
        RecoveryStateCase stateCase,
        ManagedInstallationRootStatus rootStatus,
        ManagedSetupRecoveryOutcome expected)
    {
        ManagedSetupRecoveryTransaction transaction = RecoveryTestData.Transaction(phase);
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryStateResult(stateCase),
            new(ManagedSetupRecoveryFactKind.Exact, transaction),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            rootStatus);

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        if (expected == ManagedSetupRecoveryOutcome.ActionAvailable)
        {
            Assert.Same(transaction, result.Transaction);
            Assert.NotNull(result.Plan);
        }
        else
        {
            Assert.Null(result.Transaction);
            Assert.Null(result.Plan);
        }
    }

    /// <summary>An absent marker is valid only for a clean empty root or healthy bound install.</summary>
    [Theory]
    [MemberData(nameof(AbsentMarkerMatrix))]
    public async Task AbsentMarkerDecisionCoversEveryStateAndRootCombination(
        RecoveryStateCase stateCase,
        ManagedInstallationRootStatus rootStatus,
        ManagedSetupRecoveryOutcome expected)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryStateResult(stateCase),
            new(ManagedSetupRecoveryFactKind.Absent, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            rootStatus);

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Transaction);
        Assert.Null(result.Plan);
    }

    /// <summary>Malformed, foreign, changed, or incomplete markers retain closed read-only outcomes.</summary>
    [Theory]
    [InlineData(ManagedSetupRecoveryFactKind.Malformed,
        ManagedSetupRecoveryOutcome.ManualInterventionRequired)]
    [InlineData(ManagedSetupRecoveryFactKind.IdentityMismatch,
        ManagedSetupRecoveryOutcome.ManualInterventionRequired)]
    [InlineData(ManagedSetupRecoveryFactKind.AccessDenied,
        ManagedSetupRecoveryOutcome.HealthUnavailable)]
    [InlineData(ManagedSetupRecoveryFactKind.Changed,
        ManagedSetupRecoveryOutcome.ManualInterventionRequired)]
    [InlineData(ManagedSetupRecoveryFactKind.Unavailable,
        ManagedSetupRecoveryOutcome.HealthUnavailable)]
    public async Task NonExactMarkerFactsUseClosedTerminalOutcomes(
        ManagedSetupRecoveryFactKind kind,
        ManagedSetupRecoveryOutcome expected)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.CanonicalAppState,
            new(kind, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            ManagedInstallationRootStatus.Present);

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Transaction);
        Assert.Null(result.Plan);
        Assert.Equal(0, harness.Evidence.CallCount);
    }

    /// <summary>Transient marker contention remains incomplete health without an action.</summary>
    [Fact]
    public async Task TransientMarkerContentionReturnsHealthUnavailableWithoutPlan()
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.CanonicalAppState,
            new(ManagedSetupRecoveryFactKind.Unavailable, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            ManagedInstallationRootStatus.Present);

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.HealthUnavailable, result.Outcome);
        Assert.Null(result.Transaction);
        Assert.Null(result.Plan);
        Assert.Equal(1, harness.Marker.CallCount);
    }

    /// <summary>The reader-owned exact state identity reaches every path-bound observation.</summary>
    [Fact]
    public async Task ReaderIdentityOwnsEveryRecoveryStatePathObservation()
    {
        const string ReaderStatePath = @"C:\state-a\version-manager-state.json";
        var state = new RecordingRecoveryStateStore(
            [RecoveryTestData.MissingAppState],
            statePathIdentity: ReaderStatePath);
        var marker = new RecordingRecoveryMarkerProbe(
            new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Absent, transaction: null));
        var lifetime = new RecordingRecoveryLifetimeProbe();
        var experience = new ManagedInstallationRecoveryExperience(
            state,
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Absent),
            marker,
            lifetime,
            new RecordingRecoveryEvidenceProbe(
                RecoveryTestData.Evidence(RecoveryLauncherCase.Missing)));

        ManagedSetupRecoveryDiagnosis result = await experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        string exactStatePath = Path.GetFullPath(ReaderStatePath);
        Assert.Equal(ManagedSetupRecoveryOutcome.NoRecoveryNeeded, result.Outcome);
        Assert.Equal(Enumerable.Repeat(exactStatePath, 3), lifetime.StatePaths);
        Assert.Equal([exactStatePath], marker.StatePaths);
    }

}
