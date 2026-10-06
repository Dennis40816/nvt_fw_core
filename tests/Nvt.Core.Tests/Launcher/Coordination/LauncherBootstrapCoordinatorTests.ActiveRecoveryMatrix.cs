// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class LauncherBootstrapCoordinatorTests
{
    /// <summary>Candidate and rollback app journals recover an active Launcher only after exact exit.</summary>
    [Theory]
    [InlineData(VersionActivationPhase.CandidateLaunchRecorded, ManagedProcessLifetimeStatus.Active)]
    [InlineData(VersionActivationPhase.CandidateLaunchRecorded, ManagedProcessLifetimeStatus.Unavailable)]
    [InlineData(VersionActivationPhase.CandidateLaunchRecorded, ManagedProcessLifetimeStatus.Exited)]
    [InlineData(VersionActivationPhase.RollbackLaunchRecorded, ManagedProcessLifetimeStatus.Active)]
    [InlineData(VersionActivationPhase.RollbackLaunchRecorded, ManagedProcessLifetimeStatus.Unavailable)]
    [InlineData(VersionActivationPhase.RollbackLaunchRecorded, ManagedProcessLifetimeStatus.Exited)]
    public async Task AppRecoveryPhaseAllowsOnlyAuthoritativelyExitedActiveLauncher(
        VersionActivationPhase appPhase,
        ManagedProcessLifetimeStatus lifetime)
    {
        var appStore = new RecordingAppStateStore(AppActivationState(appPhase));
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null)
                .RecordActiveLaunch());
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);
        process.LauncherLifetimeStatuses.Enqueue(lifetime);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        if (lifetime == ManagedProcessLifetimeStatus.Exited)
        {
            Assert.Equal(LauncherBootstrapOutcome.Ready, result.Outcome);
            Assert.Equal([Launcher100], process.Started);
            Assert.Null(launcherStore.Current!.Pending);
        }
        else
        {
            Assert.Equal(LauncherBootstrapOutcome.TerminationUnconfirmed, result.Outcome);
            Assert.Empty(process.Started);
            Assert.Equal(LauncherActivationPhase.ActiveLaunchRecorded, launcherStore.Current!.Pending?.Phase);
        }
        Assert.Equal(appPhase, appStore.ReadyState.PendingActivation?.Phase);
    }

    /// <summary>A power cut while clearing the active Launcher guard preserves both durable journals.</summary>
    [Theory]
    [InlineData(VersionActivationPhase.CandidateLaunchRecorded)]
    [InlineData(VersionActivationPhase.RollbackLaunchRecorded)]
    public async Task ActiveLauncherRecoveryPowerCutPreservesBothJournals(
        VersionActivationPhase appPhase)
    {
        var appStore = new RecordingAppStateStore(AppActivationState(appPhase));
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null)
                .RecordActiveLaunch())
        {
            FailSaveAt = 1,
        };
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);
        process.LauncherLifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Exited);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(appPhase, appStore.ReadyState.PendingActivation?.Phase);
        Assert.Equal(LauncherActivationPhase.ActiveLaunchRecorded, launcherStore.Current!.Pending?.Phase);
    }

    /// <summary>Candidate or rollback journals on both authorities never bypass cross-journal exclusion.</summary>
    [Theory]
    [InlineData(VersionActivationPhase.CandidateLaunchRecorded, 1)]
    [InlineData(VersionActivationPhase.CandidateLaunchRecorded, 2)]
    [InlineData(VersionActivationPhase.RollbackLaunchRecorded, 1)]
    [InlineData(VersionActivationPhase.RollbackLaunchRecorded, 2)]
    public async Task NonActiveCrossJournalCombinationsRemainFailClosed(
        VersionActivationPhase appPhase,
        int launcherPhaseValue)
    {
        LauncherActivationPhase launcherPhase = (LauncherActivationPhase)launcherPhaseValue;
        LauncherBootstrapState launcherState = LauncherBootstrapState.Create(
                Root,
                Launcher100,
                Launcher100,
                pending: null,
                failed: null)
            .Begin(Launcher101)
            .RecordCandidateLaunch();
        if (launcherPhase == LauncherActivationPhase.RollbackLaunchRecorded)
        {
            launcherState = launcherState.RecordRollbackLaunch();
        }
        var launcherStore = new RecordingLauncherStateStore(launcherState);
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppActivationState(appPhase)),
            launcherStore,
            new RecordingLauncherRepository(Launcher100, Launcher101),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, launcherStore.SaveCount);
        Assert.Equal(launcherPhase, launcherStore.Current!.Pending?.Phase);
    }

    /// <summary>Two ordinary active guards are ambiguous and neither may be cleared.</summary>
    [Fact]
    public async Task DualActiveGuardsRemainFailClosedWithoutLifetimeInspection()
    {
        var appStore = new RecordingAppStateStore(
            VersionActivationPolicy.RecordActiveLaunch(AppState(App100)));
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null)
                .RecordActiveLaunch());
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, appStore.SaveCount);
        Assert.Equal(0, launcherStore.SaveCount);
    }

    /// <summary>An application active guard cannot be cleared across a launcher candidate or rollback journal.</summary>
    [Theory]
    [InlineData((int)LauncherActivationPhase.CandidateLaunchRecorded)]
    [InlineData((int)LauncherActivationPhase.RollbackLaunchRecorded)]
    public async Task ApplicationActiveGuardWithLauncherRecoveryJournalRemainsFailClosed(
        int launcherPhaseValue)
    {
        LauncherActivationPhase launcherPhase = (LauncherActivationPhase)launcherPhaseValue;
        LauncherBootstrapState launcherState = LauncherBootstrapState.Create(
                Root,
                Launcher100,
                Launcher100,
                pending: null,
                failed: null)
            .Begin(Launcher101)
            .RecordCandidateLaunch();
        if (launcherPhase == LauncherActivationPhase.RollbackLaunchRecorded)
        {
            launcherState = launcherState.RecordRollbackLaunch();
        }
        var appStore = new RecordingAppStateStore(
            VersionActivationPolicy.RecordActiveLaunch(AppState(App100)));
        var launcherStore = new RecordingLauncherStateStore(launcherState);
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100, Launcher101),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, appStore.SaveCount);
        Assert.Equal(0, launcherStore.SaveCount);
    }

    /// <summary>A launcher active guard cannot bypass an unrecorded application request.</summary>
    [Fact]
    public async Task LauncherActiveGuardWithRequestedApplicationRemainsFailClosed()
    {
        var appStore = new RecordingAppStateStore(AppStateWithPendingActivation());
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null)
                .RecordActiveLaunch());
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.AppMutationPending, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(0, appStore.SaveCount);
        Assert.Equal(0, launcherStore.SaveCount);
    }

    /// <summary>A power cut while clearing the Desktop guard preserves the exact active attempt.</summary>
    [Fact]
    public async Task ApplicationGuardRecoveryPowerCutPreservesGuard()
    {
        var appStore = new RecordingAppStateStore(
            VersionActivationPolicy.RecordActiveLaunch(AppState(App100)))
        {
            FailSaveAt = 1,
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);
        process.ApplicationLifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Exited);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Empty(process.Started);
        Assert.Equal(VersionActivationPhase.ActiveLaunchRecorded, appStore.ReadyState.PendingActivation?.Phase);
        Assert.Equal(0, launcherStore.SaveCount);
    }

    private static VersionManagerState AppActivationState(VersionActivationPhase phase)
    {
        VersionManagerState requested = AppStateWithPendingActivation();
        VersionManagerState candidate = VersionActivationPolicy.RecordCandidateLaunch(requested);
        return phase == VersionActivationPhase.CandidateLaunchRecorded
            ? candidate
            : phase == VersionActivationPhase.RollbackLaunchRecorded
                ? VersionActivationPolicy.RecordRollbackLaunch(candidate, App101).State
                : throw new ArgumentOutOfRangeException(nameof(phase));
    }
}
