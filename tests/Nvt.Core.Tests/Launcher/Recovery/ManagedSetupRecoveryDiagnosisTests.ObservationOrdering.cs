// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Recovery;
using Xunit;


namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Locks diagnosis planning and the closed first-install state-pair table.</summary>
public sealed partial class ManagedSetupRecoveryDiagnosisTests
{
    /// <summary>Any active role wins over every distinct unavailable role before durable reads.</summary>
    [Theory]
    [MemberData(nameof(ActiveUnavailablePrecedenceCases))]
    public async Task EveryActiveLifetimeRoleWinsOverEveryDistinctUnavailableRole(
        ManagedProcessLifetimeKind activeRole,
        ManagedProcessLifetimeKind unavailableRole)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.CanonicalAppState,
            new(ManagedSetupRecoveryFactKind.Absent, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            ManagedInstallationRootStatus.Present);
        harness.Lifetime.Statuses[activeRole] = ManagedProcessLifetimeStatus.Active;
        harness.Lifetime.Statuses[unavailableRole] = ManagedProcessLifetimeStatus.Unavailable;

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.Busy, result.Outcome);
        Assert.Equal(3, harness.Lifetime.Calls.Count);
        Assert.Equal(0, harness.State.LoadCount);
        Assert.Equal(0, harness.Root.CallCount);
        Assert.Equal(0, harness.Marker.CallCount);
    }

    /// <summary>Cancellation propagates at every read-only observation boundary and stops later reads.</summary>
    [Theory]
    [InlineData(RecoveryObservationBoundary.BootstrapLifetime, 1, 0, 0, 0, 0)]
    [InlineData(RecoveryObservationBoundary.ApplicationLifetime, 2, 0, 0, 0, 0)]
    [InlineData(RecoveryObservationBoundary.LauncherLifetime, 3, 0, 0, 0, 0)]
    [InlineData(RecoveryObservationBoundary.State, 3, 1, 0, 0, 0)]
    [InlineData(RecoveryObservationBoundary.Root, 3, 1, 1, 0, 0)]
    [InlineData(RecoveryObservationBoundary.Marker, 3, 1, 1, 1, 0)]
    [InlineData(RecoveryObservationBoundary.Evidence, 3, 1, 1, 1, 1)]
    public async Task CancellationAtEveryBoundaryPropagatesWithoutLaterObservation(
        RecoveryObservationBoundary boundary,
        int expectedLifetimeCalls,
        int expectedStateCalls,
        int expectedRootCalls,
        int expectedMarkerCalls,
        int expectedEvidenceCalls)
    {
        using var cancellation = new CancellationTokenSource();
        var lifetime = new RecordingRecoveryLifetimeProbe(kind =>
        {
            if (boundary == LifetimeBoundary(kind))
            {
                cancellation.Cancel();
            }
        });
        bool cancelAtEvidence = boundary == RecoveryObservationBoundary.Evidence;
        var state = new RecordingRecoveryStateStore(
            [cancelAtEvidence ? RecoveryTestData.MissingAppState :
                RecoveryTestData.CanonicalAppState],
            beforeReturn: () => CancelAt(RecoveryObservationBoundary.State));
        var root = new RecordingRecoveryRootProbe(
            cancelAtEvidence
                ? ManagedInstallationRootStatus.Residue
                : ManagedInstallationRootStatus.Present,
            () => CancelAt(RecoveryObservationBoundary.Root));
        var marker = new RecordingRecoveryMarkerProbe(
            [cancelAtEvidence
                ? new ManagedSetupRecoveryFact(
                    ManagedSetupRecoveryFactKind.Exact,
                    RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging))
                : new ManagedSetupRecoveryFact(
                    ManagedSetupRecoveryFactKind.Absent,
                    transaction: null)],
            () => CancelAt(RecoveryObservationBoundary.Marker));
        var evidence = new RecordingRecoveryEvidenceProbe(
            [RecoveryTestData.Evidence(RecoveryLauncherCase.Missing)],
            () => CancelAt(RecoveryObservationBoundary.Evidence));
        var experience = new ManagedInstallationRecoveryExperience(
            state,
            root,
            marker,
            lifetime,
            evidence);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await experience.DiagnoseAsync(RecoveryTestData.ManagedRoot, cancellation.Token));

        Assert.Equal(expectedLifetimeCalls, lifetime.Calls.Count);
        Assert.Equal(expectedStateCalls, state.LoadCount);
        Assert.Equal(expectedRootCalls, root.CallCount);
        Assert.Equal(expectedMarkerCalls, marker.CallCount);
        Assert.Equal(expectedEvidenceCalls, evidence.CallCount);
        return;

        void CancelAt(RecoveryObservationBoundary current)
        {
            if (boundary == current)
            {
                cancellation.Cancel();
            }
        }
    }

    /// <summary>Any unavailable role returns health unavailable when no role is active.</summary>
    [Theory]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap)]
    [InlineData(ManagedProcessLifetimeKind.Application)]
    [InlineData(ManagedProcessLifetimeKind.Launcher)]
    public async Task EveryUnavailableLifetimeRoleReturnsHealthUnavailable(
        ManagedProcessLifetimeKind unavailableRole)
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.CanonicalAppState,
            new(ManagedSetupRecoveryFactKind.Absent, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            ManagedInstallationRootStatus.Present);
        harness.Lifetime.Statuses[unavailableRole] = ManagedProcessLifetimeStatus.Unavailable;

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.HealthUnavailable, result.Outcome);
        Assert.Equal(3, harness.Lifetime.Calls.Count);
        Assert.Equal(0, harness.State.LoadCount);
        Assert.Equal(0, harness.Root.CallCount);
        Assert.Equal(0, harness.Marker.CallCount);
    }

    /// <summary>All three roles are observed exactly once through the read-only lifetime port.</summary>
    [Fact]
    public async Task DiagnosisObservesAllLifetimeRolesExactlyOnce()
    {
        RecoveryHarness harness = RecoveryHarness.Create(
            RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Absent, transaction: null),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing),
            ManagedInstallationRootStatus.Absent);

        ManagedSetupRecoveryDiagnosis result = await harness.Experience.DiagnoseAsync(
            RecoveryTestData.ManagedRoot,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryOutcome.NoRecoveryNeeded, result.Outcome);
        Assert.Equal(
            [
                ManagedProcessLifetimeKind.Bootstrap,
                ManagedProcessLifetimeKind.Application,
                ManagedProcessLifetimeKind.Launcher,
            ],
            harness.Lifetime.Calls);
    }

    /// <summary>Marker facts reject contradictory transaction payloads.</summary>
    [Fact]
    public void FactRejectsContradictoryPayloads()
    {
        _ = Assert.Throws<ArgumentException>(() =>
            new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Exact, transaction: null));
        _ = Assert.Throws<ArgumentException>(() =>
            new ManagedSetupRecoveryFact(
                ManagedSetupRecoveryFactKind.Absent,
                RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging)));
    }

    /// <summary>Builds the complete exact-marker phase/state/root decision table.</summary>
    public static TheoryData<ManagedSetupRecoveryPhase, RecoveryStateCase,
        ManagedInstallationRootStatus, ManagedSetupRecoveryOutcome> ExactMarkerMatrix()
    {
        var data = new TheoryData<ManagedSetupRecoveryPhase, RecoveryStateCase,
            ManagedInstallationRootStatus, ManagedSetupRecoveryOutcome>();
        foreach (ManagedSetupRecoveryPhase phase in Enum.GetValues<ManagedSetupRecoveryPhase>())
        {
            foreach (RecoveryStateCase state in Enum.GetValues<RecoveryStateCase>())
            {
                foreach (ManagedInstallationRootStatus root in
                    Enum.GetValues<ManagedInstallationRootStatus>())
                {
                    bool unavailable = state == RecoveryStateCase.Unavailable ||
                        root is ManagedInstallationRootStatus.PermissionDenied or
                            ManagedInstallationRootStatus.Unavailable;
                    bool actionable = _actionableExactTuples.Contains((phase, state, root));
                    ManagedSetupRecoveryOutcome expected = unavailable
                        ? ManagedSetupRecoveryOutcome.HealthUnavailable
                        : actionable
                            ? ManagedSetupRecoveryOutcome.ActionAvailable
                            : ManagedSetupRecoveryOutcome.ManualInterventionRequired;
                    data.Add(phase, state, root, expected);
                }
            }
        }
        return data;
    }

    /// <summary>Builds the complete absent-marker state/root decision table.</summary>
    public static TheoryData<RecoveryStateCase, ManagedInstallationRootStatus,
        ManagedSetupRecoveryOutcome> AbsentMarkerMatrix()
    {
        var data = new TheoryData<RecoveryStateCase, ManagedInstallationRootStatus,
            ManagedSetupRecoveryOutcome>();
        foreach (RecoveryStateCase state in Enum.GetValues<RecoveryStateCase>())
        {
            foreach (ManagedInstallationRootStatus root in
                Enum.GetValues<ManagedInstallationRootStatus>())
            {
                bool unavailable = state == RecoveryStateCase.Unavailable ||
                    root is ManagedInstallationRootStatus.PermissionDenied or
                        ManagedInstallationRootStatus.Unavailable;
                bool healthy = _healthyAbsentTuples.Contains((state, root));
                ManagedSetupRecoveryOutcome expected = unavailable
                    ? ManagedSetupRecoveryOutcome.HealthUnavailable
                    : healthy
                        ? ManagedSetupRecoveryOutcome.NoRecoveryNeeded
                        : ManagedSetupRecoveryOutcome.ManualInterventionRequired;
                data.Add(state, root, expected);
            }
        }
        return data;
    }

    /// <summary>Every ordered pair of distinct lifetime roles exercises Busy precedence.</summary>
    public static TheoryData<ManagedProcessLifetimeKind, ManagedProcessLifetimeKind>
        ActiveUnavailablePrecedenceCases()
    {
        var data = new TheoryData<ManagedProcessLifetimeKind, ManagedProcessLifetimeKind>();
        foreach (ManagedProcessLifetimeKind active in Enum.GetValues<ManagedProcessLifetimeKind>())
        {
            foreach (ManagedProcessLifetimeKind unavailable in
                Enum.GetValues<ManagedProcessLifetimeKind>().Where(value => value != active))
            {
                data.Add(active, unavailable);
            }
        }
        return data;
    }

    /// <summary>The launcher-missing read-only projection contains four actionable and two healthy tuples.</summary>
    [Fact]
    public void LauncherMissingReadOnlyProjectionContainsOnlyAuthorizedTuples()
    {
        Assert.Equal(4, _actionableExactTuples.Count);
        Assert.Equal(2, _healthyAbsentTuples.Count);
        Assert.All(_actionableExactTuples, static tuple =>
            Assert.Equal(ManagedInstallationRootStatus.Residue, tuple.Root));
        Assert.DoesNotContain(
            _healthyAbsentTuples,
            static tuple => tuple.Root is ManagedInstallationRootStatus.Residue or
                ManagedInstallationRootStatus.PermissionDenied or
                ManagedInstallationRootStatus.Unavailable);
    }

    private static VersionManagerStateLoadResult RecoveryStateResult(RecoveryStateCase value)
    {
        return value switch
        {
            RecoveryStateCase.Missing => RecoveryTestData.MissingAppState,
            RecoveryStateCase.Exact => RecoveryTestData.CanonicalAppState,
            RecoveryStateCase.Invalid => new(null, VersionManagerStateLoadIssue.Invalid),
            RecoveryStateCase.ManagedRootMismatch =>
                new(null, VersionManagerStateLoadIssue.ManagedRootMismatch),
            RecoveryStateCase.Unbound => new(
                ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(RecoveryTestData.Admission),
                VersionManagerStateLoadIssue.None),
            RecoveryStateCase.WrongBound => new(
                ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(RecoveryTestData.Admission)
                    .BindToManagedRoot(@"C:\managed\foreign"),
                VersionManagerStateLoadIssue.None),
            RecoveryStateCase.Unavailable => new(null, VersionManagerStateLoadIssue.Unavailable),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static RecoveryObservationBoundary LifetimeBoundary(ManagedProcessLifetimeKind kind)
    {
        return kind switch
        {
            ManagedProcessLifetimeKind.Bootstrap =>
                RecoveryObservationBoundary.BootstrapLifetime,
            ManagedProcessLifetimeKind.Application =>
                RecoveryObservationBoundary.ApplicationLifetime,
            ManagedProcessLifetimeKind.Launcher =>
                RecoveryObservationBoundary.LauncherLifetime,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

