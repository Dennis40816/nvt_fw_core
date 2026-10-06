// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class ManagedActivationCoordinatorTests
{
    /// <summary>An active lifetime blocks overlap, then authoritative exit permits one later start.</summary>
    [Fact]
    public async Task ActiveTerminationUnconfirmedBlocksSecondRunThenRecoversAfterExit()
    {
        var store = new FakeStateStore(State());
        var process = new FakeProcess(
            ManagedProcessStartOutcome.TerminationUnconfirmed,
            ManagedProcessStartOutcome.Ready);
        process.LifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Active);
        process.LifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Unavailable);
        process.LifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Exited);

        ManagedLauncherResult first = await new ManagedActivationCoordinator(
            "managed", store, new HealthyRepository(), process, TimeSpan.FromSeconds(1))
            .RunAsync(TestContext.Current.CancellationToken);
        ManagedLauncherResult second = await new ManagedActivationCoordinator(
            "managed", store, new HealthyRepository(), process, TimeSpan.FromSeconds(1))
            .RunAsync(TestContext.Current.CancellationToken);
        ManagedLauncherResult unreadable = await new ManagedActivationCoordinator(
            "managed", store, new HealthyRepository(), process, TimeSpan.FromSeconds(1))
            .RunAsync(TestContext.Current.CancellationToken);
        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(
            "managed", store, new HealthyRepository(), process, TimeSpan.FromSeconds(1))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.TerminationUnconfirmed, first.Outcome);
        Assert.Equal(ManagedLauncherOutcome.TerminationUnconfirmed, second.Outcome);
        Assert.Equal(ManagedLauncherOutcome.TerminationUnconfirmed, unreadable.Outcome);
        Assert.Equal(ManagedLauncherOutcome.Ready, recovered.Outcome);
        Assert.Equal(["0.10.5", "0.10.5"], process.Starts);
        Assert.Null(store.State.PendingActivation);
    }

    /// <summary>A confirmed active-launch failure clears its guard so a later invocation can recover.</summary>
    [Fact]
    public async Task ActiveConfirmedFailureClearsGuardAndAllowsLaterReadyStart()
    {
        var store = new FakeStateStore(State());
        var process = new FakeProcess(ManagedProcessStartOutcome.StartFailed, ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed", store, new HealthyRepository(), process, TimeSpan.FromSeconds(1));

        ManagedLauncherResult first = await coordinator.RunAsync(TestContext.Current.CancellationToken);
        ManagedLauncherResult second = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StartFailed, first.Outcome);
        Assert.Equal(ManagedLauncherOutcome.Ready, second.Outcome);
        Assert.Equal(["0.10.5", "0.10.5"], process.Starts);
        Assert.Null(store.State.PendingActivation);
    }
}

public sealed partial class LauncherBootstrapCoordinatorTests
{
    /// <summary>Bootstrap waits for the prior Desktop lease, then clears its guard and starts once.</summary>
    [Fact]
    public async Task BootstrapRecoversApplicationGuardOnlyAfterDesktopLifetimeExited()
    {
        var appStore = new RecordingAppStateStore(
            VersionActivationPolicy.RecordActiveLaunch(AppState(App100)));
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);
        process.ApplicationLifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Active);
        process.ApplicationLifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Exited);

        LauncherBootstrapResult blocked = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);
        LauncherBootstrapResult recovered = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.TerminationUnconfirmed, blocked.Outcome);
        Assert.Equal(LauncherBootstrapOutcome.Ready, recovered.Outcome);
        Assert.Equal([Launcher100], process.Started);
        Assert.Null(appStore.ReadyState.PendingActivation);
        Assert.Null(launcherStore.Current!.Pending);
    }

    /// <summary>Pending app activation guards and clears the admitted current launcher attempt.</summary>
    [Fact]
    public async Task PendingAppActivationRunsAlreadyAdmittedLauncherWithoutLauncherActivation()
    {
        VersionManagerState appState = AppStateWithPendingActivation();
        var appStore = new RecordingAppStateStore(appState)
        {
            StateAfterFirstLoad = AppState(App101, App100),
        };
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.Ready);

        LauncherBootstrapResult result = await Create(
            appStore,
            launcherStore,
            new RecordingLauncherRepository(Launcher100, Launcher101),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.Ready, result.Outcome);
        Assert.Equal([Launcher100], process.Started);
        Assert.Equal(2, launcherStore.SaveCount);
        Assert.Null(launcherStore.Current!.Pending);
    }

    private sealed class RecordingLauncherProcess(params LauncherProcessStartOutcome[] outcomes)
        : IManagedLauncherProcess
    {
        private int _index;
        public RecordingAppStateStore? AppStateStore { get; set; }
        public List<ManagedLauncherIdentity> Started { get; } = [];
        public Queue<ManagedProcessLifetimeStatus> ApplicationLifetimeStatuses { get; } = [];
        public Queue<ManagedProcessLifetimeStatus> LauncherLifetimeStatuses { get; } = [];

        public ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(
            string statePath,
            ManagedProcessLifetimeKind kind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queue<ManagedProcessLifetimeStatus> statuses = kind == ManagedProcessLifetimeKind.Application
                ? ApplicationLifetimeStatuses
                : LauncherLifetimeStatuses;
            return ValueTask.FromResult(
                statuses.Count == 0
                    ? ManagedProcessLifetimeStatus.Active
                    : statuses.Dequeue());
        }

        public ValueTask<LauncherProcessStartResult> StartUntilReadyAsync(
            string managedRoot,
            string statePath,
            ManagedLauncherIdentity launcher,
            IManagedExecutableLaunchLease executableLease,
            TimeSpan readyDeadline,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Started.Add(launcher);
            LauncherProcessStartOutcome outcome = outcomes[Math.Min(_index++, outcomes.Length - 1)];
            ManagedVersionAdmission? admission = outcome == LauncherProcessStartOutcome.Ready
                ? AppStateStore?.ReadyState.Admissions.SingleOrDefault(
                    candidate => candidate.Version == AppStateStore.ReadyState.ActiveVersion)
                : null;
            return ValueTask.FromResult(new LauncherProcessStartResult(outcome, ExitCode: null, admission));
        }
    }

    /// <summary>An active Launcher lifetime blocks overlap, then authoritative exit permits recovery.</summary>
    [Fact]
    public async Task ActiveLauncherTerminationUnconfirmedBlocksSecondRunThenRecoversAfterExit()
    {
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(
            LauncherProcessStartOutcome.TerminationUnconfirmed,
            LauncherProcessStartOutcome.Ready);
        process.LauncherLifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Active);
        process.LauncherLifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Unavailable);
        process.LauncherLifetimeStatuses.Enqueue(ManagedProcessLifetimeStatus.Exited);
        var appStore = new RecordingAppStateStore(AppState(App100));

        LauncherBootstrapResult first = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);
        LauncherBootstrapResult second = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);
        LauncherBootstrapResult unreadable = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);
        LauncherBootstrapResult recovered = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.TerminationUnconfirmed, first.Outcome);
        Assert.Equal(LauncherBootstrapOutcome.TerminationUnconfirmed, second.Outcome);
        Assert.Equal(LauncherBootstrapOutcome.TerminationUnconfirmed, unreadable.Outcome);
        Assert.Equal(LauncherBootstrapOutcome.Ready, recovered.Outcome);
        Assert.Equal([Launcher100, Launcher100], process.Started);
        Assert.Null(launcherStore.Current!.Pending);
    }

    /// <summary>A confirmed active Launcher failure clears its guard and permits a later retry.</summary>
    [Fact]
    public async Task ActiveLauncherConfirmedFailureClearsGuardAndAllowsLaterReadyStart()
    {
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(
            LauncherProcessStartOutcome.StartFailed,
            LauncherProcessStartOutcome.Ready);
        var appStore = new RecordingAppStateStore(AppState(App100));

        LauncherBootstrapResult first = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);
        LauncherBootstrapResult second = await Create(
            appStore, launcherStore, new RecordingLauncherRepository(Launcher100), process)
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.StartFailed, first.Outcome);
        Assert.Equal(LauncherBootstrapOutcome.Ready, second.Outcome);
        Assert.Equal([Launcher100, Launcher100], process.Started);
        Assert.Null(launcherStore.Current!.Pending);
    }
}
