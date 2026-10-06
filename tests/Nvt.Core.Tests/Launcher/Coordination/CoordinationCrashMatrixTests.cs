// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Compares full synthetic durable state, writer scopes and process traces across both journals.</summary>
public sealed class CoordinationCrashMatrixTests
{
    private static readonly ManagedAppVersion Current = ManagedAppVersion.Parse("1.0.0");
    private static readonly ManagedAppVersion Candidate = ManagedAppVersion.Parse("1.0.1");
    private static readonly ManagedVersionAdmission CurrentAdmission = Admission(Current);
    private static readonly ManagedVersionAdmission CandidateAdmission = Admission(Candidate);
    private static readonly ManagedLauncherIdentity CurrentLauncher = Identity(Current, 'a');
    private static readonly ManagedLauncherIdentity CandidateLauncher = Identity(Candidate, 'b');
    private static readonly ManagedLauncherIdentity[] Launchers = [CurrentLauncher, CandidateLauncher];
    private static readonly string[][] Plans =
    [
        ["new", "requested", "recordRollback", "rollback", "launcherGuardNew"],
        ["existing", "conflict", "conflict", "conflict", "conflict"],
        ["existing", "conflict", "conflict", "conflict", "launcherGuardExisting"],
        ["existing", "conflict", "conflict", "conflict", "launcherGuardExisting"],
        ["appGuardExisting", "conflict", "conflict", "conflict", "conflict"],
        ["conflict", "conflict", "conflict", "conflict", "conflict"],
        ["conflict", "conflict", "conflict", "conflict", "conflict"],
    ];
    private static readonly string[] ApplicationRestartTrace =
        ["acquire:50000000", "writer:None", "load:app", "save:app:RollbackLaunchRecorded", "inventory:app",
            "repository:app:1.0.0", "process:app:1.0.0", "process:writer:held", "save:app:committed"];
    private static readonly ManagedProcessLifetimeStatus[] Lifetimes =
        [ManagedProcessLifetimeStatus.Active, ManagedProcessLifetimeStatus.Exited, ManagedProcessLifetimeStatus.Unavailable];

    /// <summary>Every valid application/launcher journal pair with every authoritative lifetime observation.</summary>
    public static IEnumerable<object[]> CrossJournalCases()
    {
        for (int app = 0; app < Plans.Length; app++)
        {
            for (int launcher = 0; launcher < Plans[app].Length; launcher++)
            {
                foreach (ManagedProcessLifetimeStatus lifetime in Lifetimes)
                { yield return [app, launcher, lifetime, Plans[app][launcher]]; }
            }
        }
    }

    /// <summary>Conflicts and unknown lifetimes preserve both complete journals; allowed pairs follow exact writer/start traces.</summary>
    [Theory]
    [MemberData(nameof(CrossJournalCases))]
    public async Task CrossJournalCrashMatrixPreservesCompleteStateAndWriterTrace(
        int appJournal, int launcherJournal, ManagedProcessLifetimeStatus lifetime, string plan)
    {
        string root = Path.GetFullPath("synthetic-matrix-root");
        VersionManagerState initialApp = AppState(root, appJournal);
        LauncherBootstrapState initialLauncher = LauncherState(root, launcherJournal);
        using var app = new CoordinationStateStore(initialApp);
        var launcher = new CoordinationLauncherStateStore(app, initialLauncher);
        var process = new TraceLauncherProcess(app) { Lifetime = lifetime };
        var coordinator = new LauncherBootstrapCoordinator(root, app.StatePath, app, launcher,
            new TraceLauncherRepository(app, Launchers), process);

        LauncherBootstrapResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        var expectedTrace = new List<string> { "acquire:2500000", "writer:None", "load:app", "load:launcher" };
        bool guarded = plan.StartsWith("launcherGuard", StringComparison.Ordinal) || plan == "appGuardExisting";
        if (plan == "conflict" || guarded && lifetime != ManagedProcessLifetimeStatus.Exited)
        {
            if (guarded) { expectedTrace.Add(plan == "appGuardExisting" ? "lifetime:Application" : "lifetime:Launcher"); }
            Assert.Equal(plan == "conflict" ? LauncherBootstrapOutcome.AppMutationPending : LauncherBootstrapOutcome.TerminationUnconfirmed,
                result.Outcome);
            Assert.Equal(CoordinationStateCodec.Encode(initialApp), CoordinationStateCodec.Encode(app.State));
            Assert.Equal(CoordinationStateCodec.Encode(initialLauncher), CoordinationStateCodec.Encode(launcher.State!));
            Assert.Empty(process.Starts);
            Assert.Equal(expectedTrace, app.Trace);
            return;
        }

        if (guarded)
        {
            expectedTrace.Add(plan == "appGuardExisting" ? "lifetime:Application" : "lifetime:Launcher");
            expectedTrace.Add(plan == "appGuardExisting" ? "save:app:committed" : "save:launcher:committed");
        }
        bool rollback = plan is "recordRollback" or "rollback";
        bool newCandidate = plan is "new" or "requested" or "launcherGuardNew";
        ManagedLauncherIdentity running = newCandidate ? CandidateLauncher : CurrentLauncher;
        if (plan == "recordRollback") { expectedTrace.Add("save:launcher:RollbackLaunchRecorded"); }
        expectedTrace.Add($"repository:launcher:{running.OwnerAppVersion}");
        if (plan is "new" or "launcherGuardNew") { expectedTrace.Add("save:launcher:Requested"); }
        if (newCandidate) { expectedTrace.Add("save:launcher:CandidateLaunchRecorded"); }
        else if (!rollback) { expectedTrace.Add("save:launcher:ActiveLaunchRecorded"); }
        expectedTrace.Add($"process:launcher:{running.OwnerAppVersion}");
        expectedTrace.Add("process:writer:released");
        expectedTrace.Add("acquire:2500000");
        expectedTrace.Add("writer:None");
        expectedTrace.Add("load:app");
        expectedTrace.Add("load:launcher");
        expectedTrace.Add("save:launcher:committed");
        VersionManagerState expectedApp = plan == "appGuardExisting"
            ? VersionActivationPolicy.ClearActiveLaunch(initialApp, Current) : initialApp;
        LauncherBootstrapState expectedLauncher = LauncherBootstrapState.Create(root, running, running, null,
            rollback ? CandidateLauncher : null);
        Assert.Equal(rollback ? LauncherBootstrapOutcome.RolledBack : LauncherBootstrapOutcome.Ready, result.Outcome);
        Assert.Equal(running, result.RunningLauncher);
        Assert.Equal(rollback ? CandidateLauncher : null, result.FailedLauncher);
        Assert.Equal(CoordinationStateCodec.Encode(expectedApp), CoordinationStateCodec.Encode(app.State));
        Assert.Equal(CoordinationStateCodec.Encode(expectedLauncher), CoordinationStateCodec.Encode(launcher.State!));
        Assert.Equal(running, Assert.Single(process.Starts));
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(process.Deadlines));
        Assert.Equal(expectedTrace, app.Trace);
        using VersionManagerWriteLeaseResult released = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            app.StatePath, TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.True(released.HoldsStatePath(app.StatePath));
    }

    /// <summary>Candidate READY followed by a crash leaves the recorded app launch and restarts only the recorded fallback.</summary>
    [Fact]
    public async Task ApplicationReadyCrashBeforeCommitRecoversOnlyRecordedFallback()
    {
        string root = Path.GetFullPath("synthetic-crash-root");
        VersionManagerState initial = AppState(root, 1);
        using var app = new CoordinationStateStore(initial);
        var repository = new TraceApplicationRepository(app, CoordinationFixture.Product);
        var first = new TraceApplicationProcess(app) { AtReady = () => throw new IOException("Synthetic crash after READY.") };
        await Assert.ThrowsAsync<IOException>(() => new ManagedActivationCoordinator(root, app, repository, first)
            .RunAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(Candidate, Assert.Single(first.Starts));
        Assert.Equal(CoordinationStateCodec.Encode(VersionActivationPolicy.RecordCandidateLaunch(initial)),
            CoordinationStateCodec.Encode(app.State));
        app.Trace.Clear();
        var restarted = new TraceApplicationProcess(app);
        ManagedLauncherResult result = await new ManagedActivationCoordinator(root, app, repository, restarted)
            .RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherOutcome.RolledBack, result.Outcome);
        Assert.Equal(Current, Assert.Single(restarted.Starts));
        Assert.Null(app.State.PendingActivation);
        Assert.Equal(Candidate, app.State.FailedActivationVersion);
        Assert.All(repository.Leases, lease => Assert.True(lease.Disposed));
        Assert.Equal(ApplicationRestartTrace, app.Trace);
    }

    /// <summary>Launcher READY followed by a crash preserves its exact journal for fallback-only recovery.</summary>
    [Fact]
    public async Task LauncherReadyCrashBeforeCommitRecoversOnlyRecordedFallback()
    {
        string root = Path.GetFullPath("synthetic-crash-root");
        using var app = new CoordinationStateStore(AppState(root, 0));
        var launcher = new CoordinationLauncherStateStore(app, LauncherState(root, 0));
        var process = new TraceLauncherProcess(app) { AtReady = () => throw new IOException("Synthetic crash after READY.") };
        await Assert.ThrowsAsync<IOException>(() => new LauncherBootstrapCoordinator(root, app.StatePath, app, launcher,
            new TraceLauncherRepository(app, Launchers), process).RunAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(CandidateLauncher, Assert.Single(process.Starts));
        Assert.Equal(CoordinationStateCodec.Encode(LauncherState(root, 2)), CoordinationStateCodec.Encode(launcher.State!));
        var restart = new TraceLauncherProcess(app);
        LauncherBootstrapResult recovered = await new LauncherBootstrapCoordinator(root, app.StatePath, app, launcher,
            new TraceLauncherRepository(app, Launchers), restart).RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LauncherBootstrapOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(CurrentLauncher, Assert.Single(restart.Starts));
        Assert.Equal(CandidateLauncher, launcher.State!.Failed);
        Assert.Null(launcher.State.Pending);
    }

    /// <summary>Rollback READY with a failed durable commit survives serialization and retries only its recorded fallback.</summary>
    [Fact]
    public async Task LauncherRollbackReadyCommitFailureRemainsJournaledUntilRestart()
    {
        string root = Path.GetFullPath("synthetic-crash-root");
        using var app = new CoordinationStateStore(AppState(root, 0));
        LauncherBootstrapState recorded = LauncherState(root, 3);
        var launcher = new CoordinationLauncherStateStore(app, recorded) { FailOnSave = 1 };
        var first = new TraceLauncherProcess(app);
        LauncherBootstrapResult interrupted = await new LauncherBootstrapCoordinator(root, app.StatePath, app, launcher,
            new TraceLauncherRepository(app, Launchers), first).RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, interrupted.Outcome);
        Assert.Equal(CoordinationStateCodec.Encode(recorded), CoordinationStateCodec.Encode(launcher.State!));
        var restarted = new TraceLauncherProcess(app);
        LauncherBootstrapResult recovered = await new LauncherBootstrapCoordinator(root, app.StatePath, app, launcher,
            new TraceLauncherRepository(app, Launchers), restarted).RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LauncherBootstrapOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(CurrentLauncher, Assert.Single(first.Starts));
        Assert.Equal(CurrentLauncher, Assert.Single(restarted.Starts));
        Assert.Null(launcher.State!.Pending);
    }

    /// <summary>A failed guard-clear save preserves the exact app and launcher journals without starting a process.</summary>
    [Theory]
    [InlineData(2, 4, false)]
    [InlineData(3, 4, false)]
    [InlineData(4, 0, true)]
    public async Task CrossJournalGuardClearFailurePreservesBothSerializedJournals(int appJournal, int launcherJournal, bool failApp)
    {
        string root = Path.GetFullPath("synthetic-guard-root");
        VersionManagerState initialApp = AppState(root, appJournal);
        LauncherBootstrapState initialLauncher = LauncherState(root, launcherJournal);
        using var app = new CoordinationStateStore(initialApp) { FailOnSave = failApp ? 1 : int.MaxValue };
        var launcher = new CoordinationLauncherStateStore(app, initialLauncher) { FailOnSave = failApp ? int.MaxValue : 1 };
        var process = new TraceLauncherProcess(app);
        LauncherBootstrapResult result = await new LauncherBootstrapCoordinator(root, app.StatePath, app, launcher,
            new TraceLauncherRepository(app, Launchers), process).RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LauncherBootstrapOutcome.StateUnavailable, result.Outcome);
        Assert.Empty(process.Starts);
        Assert.Equal(CoordinationStateCodec.Encode(initialApp), CoordinationStateCodec.Encode(app.State));
        Assert.Equal(CoordinationStateCodec.Encode(initialLauncher), CoordinationStateCodec.Encode(launcher.State!));
        Assert.Equal(failApp ? 1 : 0, app.SaveCount);
        Assert.Equal(failApp ? 0 : 1, launcher.SaveCount);
    }

    /// <summary>Unavailable app inventory cannot discard either overlapping recovery journal or start a fallback.</summary>
    [Fact]
    public async Task InventoryUnavailablePreservesBothSerializedRecoveryJournals()
    {
        string root = Path.GetFullPath("synthetic-guard-root");
        VersionManagerState recordedApp = AppState(root, 3);
        LauncherBootstrapState recordedLauncher = LauncherState(root, 4);
        using var app = new CoordinationStateStore(recordedApp);
        var launcher = new CoordinationLauncherStateStore(app, recordedLauncher);
        var process = new TraceApplicationProcess(app);
        var repository = new TraceApplicationRepository(app, CoordinationFixture.Product) { InventoryUnavailable = true };
        ManagedLauncherResult result = await new ManagedActivationCoordinator(root, app, repository, process)
            .RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, result.Outcome);
        Assert.Empty(process.Starts);
        Assert.Equal(CoordinationStateCodec.Encode(recordedApp), CoordinationStateCodec.Encode(app.State));
        Assert.Equal(CoordinationStateCodec.Encode(recordedLauncher), CoordinationStateCodec.Encode(launcher.State!));
        Assert.Equal(0, app.SaveCount);
        Assert.Equal(0, launcher.SaveCount);
    }

    /// <summary>App rollback READY with a failed commit survives serialization and retries only the recorded fallback.</summary>
    [Fact]
    public async Task ApplicationRollbackReadyCommitFailureRemainsJournaledUntilRestart()
    {
        string root = Path.GetFullPath("synthetic-guard-root");
        VersionManagerState recorded = AppState(root, 3);
        using var app = new CoordinationStateStore(recorded, failOnSave: 1);
        var repository = new TraceApplicationRepository(app, CoordinationFixture.Product);
        var first = new TraceApplicationProcess(app);
        ManagedLauncherResult interrupted = await new ManagedActivationCoordinator(root, app, repository, first)
            .RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, interrupted.Outcome);
        Assert.Equal(CoordinationStateCodec.Encode(recorded), CoordinationStateCodec.Encode(app.State));
        var restart = new TraceApplicationProcess(app);
        ManagedLauncherResult recovered = await new ManagedActivationCoordinator(root, app, repository, restart)
            .RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherOutcome.RolledBack, recovered.Outcome);
        Assert.Equal(Current, Assert.Single(first.Starts));
        Assert.Equal(Current, Assert.Single(restart.Starts));
        Assert.Null(app.State.PendingActivation);
    }

    private static ManagedVersionAdmission Admission(ManagedAppVersion version) => new(version, $"admission-{version}", new string('c', 64));
    private static ManagedLauncherIdentity Identity(ManagedAppVersion owner, char hash) => ManagedLauncherIdentity.Create(
        CoordinationFixture.Product, 200_000_000, owner, $"admission-{owner}", new string('c', 64), owner, 1,
        CoordinationFixture.Product.LauncherExecutableRelativePath, 123, new string(hash, 64));
    private static VersionManagerState AppState(string root, int journal)
    {
        VersionManagerState state = VersionManagerState.Create(Path.GetFullPath("synthetic-source"), journal == 0 ? Candidate : Current,
            Current, [CurrentAdmission, CandidateAdmission], null, null, false, managedRootIdentity: root,
            sourceRegistryState: new VersionSourceRegistryState(7, new string('d', 64), false));
        if (journal is >= 1 and <= 3)
        {
            state = VersionActivationPolicy.BeginActivation(state, Candidate);
            if (journal >= 2) { state = VersionActivationPolicy.RecordCandidateLaunch(state); }
            if (journal == 3) { state = VersionActivationPolicy.RecordRollbackLaunch(state, Candidate).State; }
        }
        else if (journal == 4) { state = VersionActivationPolicy.RecordActiveLaunch(state); }
        else if (journal == 5) { state = state.WithPendingMutation(new(ManagedVersionMutationKind.Install, Admission(ManagedAppVersion.Parse("1.0.2")))); }
        else if (journal == 6) { state = state.WithPendingMutation(new(ManagedVersionMutationKind.Delete, CandidateAdmission)); }
        return state;
    }
    private static LauncherBootstrapState LauncherState(string root, int journal)
    {
        LauncherBootstrapState state = LauncherBootstrapState.Create(root, CurrentLauncher, CurrentLauncher, null, null);
        if (journal is >= 1 and <= 3)
        {
            state = state.Begin(CandidateLauncher);
            if (journal >= 2) { state = state.RecordCandidateLaunch(); }
            if (journal == 3) { state = state.RecordRollbackLaunch(); }
        }
        else if (journal == 4) { state = state.RecordActiveLaunch(); }
        return state;
    }
}
