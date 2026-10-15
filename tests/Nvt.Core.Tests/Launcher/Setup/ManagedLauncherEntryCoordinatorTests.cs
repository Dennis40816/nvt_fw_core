// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Tests the single local-only Launcher entry owner.</summary>
public sealed partial class ManagedLauncherEntryCoordinatorTests : SetupTestWorkspace
{
    private static readonly ManagedImmutableBootstrapIdentity _bootstrap = ManagedImmutableBootstrapIdentity.Create(
        Transport.TransportFixture.Descriptor,
        "Fixture.Bootstrap.exe",
        1024,
        new string('b', 64));

    /// <summary>Typed payload failures stop before state, root, or process access.</summary>
    [Theory]
    [InlineData(ManagedDistributionPayloadIssue.Unavailable, ManagedLauncherEntryOutcome.PayloadUnavailable)]
    [InlineData(ManagedDistributionPayloadIssue.Invalid, ManagedLauncherEntryOutcome.PayloadInvalid)]
    [InlineData(ManagedDistributionPayloadIssue.Changed, ManagedLauncherEntryOutcome.PayloadInvalid)]
    public async Task PayloadFailureStopsBeforeLocalHealthAndHandoff(
        ManagedDistributionPayloadIssue issue,
        ManagedLauncherEntryOutcome expected)
    {
        string root = Root($"payload-{issue}");
        var payload = new EntryPayloadSource { AdmissionIssue = issue };
        var state = new EntryStateStore(BoundState(root));
        var roots = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            state,
            roots,
            handoff,
            payloadSource: payload);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, payload.AdmissionCount);
        Assert.Equal(0, state.LoadCount);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>The embedded descriptor version is bound to the running Launcher before health I/O.</summary>
    [Fact]
    public async Task PayloadLauncherVersionMismatchStopsBeforeLocalHealth()
    {
        string root = Root("payload-version-mismatch");
        var payload = new EntryPayloadSource
        {
            LauncherVersion = ManagedAppVersion.Parse("1.0.5"),
        };
        var state = new EntryStateStore(BoundState(root));
        var roots = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            state,
            roots,
            handoff,
            payloadSource: payload);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.PayloadInvalid, result.Outcome);
        Assert.Equal(0, state.LoadCount);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>A bound installation immediately delegates to immutable Bootstrap.</summary>
    [Fact]
    public async Task HealthyBoundStateUsesOnlyBoundedRootProbeAndNoWriterLease()
    {
        string root = Root("healthy");
        EntryStateStore state = new(BoundState(root));
        RecordingRootProbe roots = new(ManagedInstallationRootStatus.Present);
        using RecordingBootstrapHandoff handoff = new(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(root, state, roots, handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
        Assert.Equal(root, result.ManagedRoot);
        Assert.Equal(1, state.LoadCount);
        Assert.Equal(0, state.LeaseCount);
        Assert.Equal(1, roots.ObserveCount);
        Assert.Equal(1, handoff.StartCount);
        Assert.Equal(root, handoff.LastRoot);
        Assert.Equal(_bootstrap, handoff.LastIdentity);
    }

    /// <summary>Only genuinely absent state and root permit Setup.</summary>
    [Fact]
    public async Task MissingStateAndAbsentRootShowsSetupWithoutHandoff()
    {
        string root = Root("absent");
        EntryStateStore state = new(state: null);
        RecordingRootProbe roots = new(ManagedInstallationRootStatus.Absent);
        using RecordingBootstrapHandoff handoff = new(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(root, state, roots, handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.SetupRequired, result.Outcome);
        Assert.Equal(root, result.ManagedRoot);
        Assert.Equal(1, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>Existing, residual, and unsafe roots never become a fresh install.</summary>
    [Theory]
    [InlineData(ManagedInstallationRootStatus.Present)]
    [InlineData(ManagedInstallationRootStatus.Residue)]
    [InlineData(ManagedInstallationRootStatus.InvalidDestination)]
    public async Task MissingStateWithAnyExistingOrUnsafeRootRequiresRecovery(
        ManagedInstallationRootStatus rootStatus)
    {
        string root = Root(rootStatus.ToString());
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(state: null),
            new RecordingRootProbe(rootStatus),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.RecoveryRequired, result.Outcome);
    }

    /// <summary>Every non-missing state failure remains a non-Setup terminal result.</summary>
    [Theory]
    [InlineData(VersionManagerStateLoadIssue.Invalid, ManagedLauncherEntryOutcome.RecoveryRequired)]
    [InlineData(VersionManagerStateLoadIssue.ManagedRootMismatch, ManagedLauncherEntryOutcome.RecoveryRequired)]
    [InlineData(VersionManagerStateLoadIssue.Unavailable, ManagedLauncherEntryOutcome.HealthUnavailable)]
    public async Task NonMissingStateFailuresNeverFallThroughToSetup(
        VersionManagerStateLoadIssue issue,
        ManagedLauncherEntryOutcome expected)
    {
        string root = Root(issue.ToString());
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(issue),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.NotEqual(ManagedLauncherEntryOutcome.SetupRequired, result.Outcome);
    }

    /// <summary>Bound state never bypasses missing, residual, unsafe, or unobservable root facts.</summary>
    [Theory]
    [InlineData(ManagedInstallationRootStatus.Absent, ManagedLauncherEntryOutcome.RecoveryRequired)]
    [InlineData(ManagedInstallationRootStatus.Residue, ManagedLauncherEntryOutcome.RecoveryRequired)]
    [InlineData(ManagedInstallationRootStatus.InvalidDestination, ManagedLauncherEntryOutcome.RecoveryRequired)]
    [InlineData(ManagedInstallationRootStatus.PermissionDenied, ManagedLauncherEntryOutcome.HealthUnavailable)]
    [InlineData(ManagedInstallationRootStatus.Unavailable, ManagedLauncherEntryOutcome.HealthUnavailable)]
    public async Task BoundStateRequiresOneCompletelyObservablePresentRoot(
        ManagedInstallationRootStatus rootStatus,
        ManagedLauncherEntryOutcome expected)
    {
        string root = Root($"bound-{rootStatus}");
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(rootStatus),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>Root Bootstrap completion maps without a fallback to Setup.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapCompletionOutcome.RolledBack, ManagedLauncherEntryOutcome.LaunchInstalled)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Failed, ManagedLauncherEntryOutcome.LaunchFailed)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Unavailable, ManagedLauncherEntryOutcome.HealthUnavailable)]
    [InlineData(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed, ManagedLauncherEntryOutcome.TerminationUnconfirmed)]
    public async Task BootstrapCompletionMapsWithoutSetupFallback(
        ImmutableBootstrapCompletionOutcome handoffOutcome,
        ManagedLauncherEntryOutcome expected)
    {
        string root = Root(handoffOutcome.ToString());
        using var handoff = new RecordingBootstrapHandoff(handoffOutcome);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.NotEqual(ManagedLauncherEntryOutcome.SetupRequired, result.Outcome);
    }

    /// <summary>Pre-READY admission outcomes preserve their typed entry classification.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Busy, ManagedLauncherEntryOutcome.Busy)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.RecoveryRequired, ManagedLauncherEntryOutcome.RecoveryRequired)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.LaunchFailed, ManagedLauncherEntryOutcome.LaunchFailed)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, ManagedLauncherEntryOutcome.HealthUnavailable)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed, ManagedLauncherEntryOutcome.TerminationUnconfirmed)]
    public async Task BootstrapAdmissionMapsBeforeReadyWait(
        ImmutableBootstrapAdmissionOutcome admissionOutcome,
        ManagedLauncherEntryOutcome expected)
    {
        string root = Root($"admission-{admissionOutcome}");
        using var handoff = new RecordingBootstrapHandoff(admissionOutcome);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        TimeSpan totalDuration = result.TotalElapsed;
        TimeSpan admissionDuration = result.AdmissionElapsed;
        Assert.Equal(totalDuration, admissionDuration);
        Assert.Equal(0, handoff.CompletionWaitCount);
    }

    /// <summary>Pre-start admission failures map without opening Setup or awaiting READY.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapStartIssue.Busy, ManagedLauncherEntryOutcome.Busy)]
    [InlineData(ImmutableBootstrapStartIssue.Damaged, ManagedLauncherEntryOutcome.RecoveryRequired)]
    [InlineData(ImmutableBootstrapStartIssue.StartFailed, ManagedLauncherEntryOutcome.LaunchFailed)]
    [InlineData(ImmutableBootstrapStartIssue.Unavailable, ManagedLauncherEntryOutcome.HealthUnavailable)]
    public async Task BootstrapStartIssueMapsWithoutSetupFallback(
        ImmutableBootstrapStartIssue startIssue,
        ManagedLauncherEntryOutcome expected)
    {
        string root = Root(startIssue.ToString());
        using var handoff = new RecordingBootstrapHandoff(startIssue);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.NotEqual(ManagedLauncherEntryOutcome.SetupRequired, result.Outcome);
    }

    /// <summary>An undefined start issue is an invalid receipt, never a switch exception.</summary>
    [Fact]
    public async Task UndefinedBootstrapStartIssueFailsClosed()
    {
        string root = Root("undefined-start-issue");
        using var handoff = new RecordingBootstrapHandoff((ImmutableBootstrapStartIssue)999);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
    }

    /// <summary>A receipt attached to a failed start is disposed and classified as uncertain.</summary>
    [Fact]
    public async Task BootstrapStartIssueWithReceiptFailsClosed()
    {
        string root = Root("malformed-start-with-receipt");
        var handoff = new MalformedStartHandoff(
            attachLaunch: true,
            ImmutableBootstrapStartIssue.Busy);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.True(handoff.Launch.Disposed);
    }

    /// <summary>A purported successful start without a receipt is classified as uncertain.</summary>
    [Fact]
    public async Task BootstrapSuccessfulStartWithoutReceiptFailsClosed()
    {
        string root = Root("malformed-start-without-receipt");
        var handoff = new MalformedStartHandoff(
            attachLaunch: false,
            ImmutableBootstrapStartIssue.None);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.False(handoff.Launch.Disposed);
    }

    /// <summary>The child READY wait is independent from the local admission deadline.</summary>
    [Fact]
    public async Task ReadyWaitMayOutliveLocalAdmissionDeadline()
    {
        string root = Root("slow-ready");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new DeferredBootstrapHandoff();
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            admissionDeadline: TimeSpan.FromMilliseconds(25),
            timeProvider: time);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        _ = await Task.WhenAny(running, handoff.CompletionWaitStarted)
            .WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(handoff.CompletionWaitStarted.IsCompletedSuccessfully);
        time.Advance(TimeSpan.FromMilliseconds(75));

        Assert.False(running.IsCompleted);
        handoff.Complete(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryResult result = await running;
        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
        TimeSpan totalDuration = result.TotalElapsed;
        TimeSpan admissionDuration = result.AdmissionElapsed;
        Assert.Equal(TimeSpan.FromMilliseconds(75), totalDuration);
        Assert.True(admissionDuration < totalDuration);
        Assert.False(handoff.CompletionWaitCancelled);
    }

    /// <summary>An internal admission deadline after process creation is termination-uncertain.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task BootstrapAdmissionAfterLocalDeadlineFailsClosedBeforeReadyWait(
        int payloadDelayMilliseconds)
    {
        string root = Root("late-admission");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var payload = new PendingPayloadSource();
        using var handoff = new DeferredAdmissionBootstrapHandoff();
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            TimeSpan.FromMilliseconds(25),
            timeProvider: time,
            payloadSource: payload);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        await payload.Entered.WaitAsync(TestContext.Current.CancellationToken);
        // Model a slow runner before process creation without consuming the manual deadline.
        Assert.InRange(payloadDelayMilliseconds, 0, 100);
        await Task.Yield();
        payload.Complete();
        _ = await Task.WhenAny(running, handoff.AdmissionWaitStarted)
            .WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(25));
        ManagedLauncherEntryResult result = await running.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.True(handoff.AdmissionWaitStarted.IsCompletedSuccessfully);
        Assert.Equal(root, result.ManagedRoot);
        TimeSpan totalDuration = result.TotalElapsed;
        TimeSpan admissionDuration = result.AdmissionElapsed;
        Assert.True(admissionDuration > TimeSpan.Zero);
        Assert.Equal(totalDuration, admissionDuration);
        Assert.True(handoff.AdmissionWaitCancelled);
        Assert.Equal(0, handoff.CompletionWaitCount);
    }

    /// <summary>An internal completion deadline after process creation is termination-uncertain.</summary>
    [Fact]
    public async Task ReadyCompletionDeadlineIsIndependentAndFailsClosed()
    {
        string root = Root("completion-timeout");
        using var handoff = new DeferredBootstrapHandoff();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            completionDeadline: TimeSpan.FromMilliseconds(25), timeProvider: time);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(TestContext.Current.CancellationToken).AsTask();
        await handoff.CompletionWaitStarted.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(25));
        ManagedLauncherEntryResult result = await running.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        TimeSpan totalDuration = result.TotalElapsed;
        TimeSpan admissionDuration = result.AdmissionElapsed;
        Assert.True(admissionDuration < totalDuration);
        Assert.True(handoff.CompletionWaitCancelled);
    }

    /// <summary>The outer completion budget permits one candidate timeout followed by READY rollback.</summary>
    [Fact]
    public async Task CandidateTimeoutThenRollbackReadyCompletesWithinOuterBudget()
    {
        string root = Root("candidate-timeout-rollback-ready");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new TwoAttemptBootstrapHandoff(
            time,
            TimeSpan.FromMilliseconds(30));
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            admissionDeadline: TimeSpan.FromMilliseconds(25),
            completionDeadline: TimeSpan.FromMilliseconds(100),
            timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
        Assert.Equal(2, handoff.CompletedAttemptCount);
        TimeSpan totalDuration = result.TotalElapsed;
        TimeSpan admissionDuration = result.AdmissionElapsed;
        Assert.Equal(TimeSpan.FromMilliseconds(60), totalDuration);
        Assert.True(admissionDuration < totalDuration);
    }

    /// <summary>The local hard deadline fails closed and starts no Setup flow.</summary>
    [Fact]
    public async Task LocalAdmissionDeadlineReturnsUnavailableAndNeverShowsSetup()
    {
        string root = Root("timeout");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var roots = new PendingRootProbe();
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(state: null),
            roots,
            handoff,
            TimeSpan.FromMilliseconds(25), timeProvider: time);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(TestContext.Current.CancellationToken).AsTask();
        await roots.Entered.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(25));
        ManagedLauncherEntryResult result = await running.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.HealthUnavailable, result.Outcome);
        Assert.NotEqual(ManagedLauncherEntryOutcome.SetupRequired, result.Outcome);
        Assert.Equal(0, handoff.StartCount);
        roots.Complete(ManagedInstallationRootStatus.Present);
        await roots.Returned.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Caller cancellation remains cancellation rather than health failure.</summary>
    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoHealthFailure()
    {
        string root = Root("cancel");
        using CancellationTokenSource cancellation = new();
        Cancel(cancellation);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(state: null),
            new RecordingRootProbe(ManagedInstallationRootStatus.Absent),
            handoff);

        OperationCanceledException _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.RunAsync(cancellation.Token).AsTask());
    }

    /// <summary>Caller cancellation after start owns and disposes any attached receipt first.</summary>
    [Fact]
    public async Task CallerCancellationAfterStartWinsAndDisposesReceipt()
    {
        string root = Root("cancel-after-start");
        using var cancellation = new CancellationTokenSource();
        var handoff = new CancellingStartHandoff(cancellation);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.RunAsync(cancellation.Token).AsTask());

        Assert.True(handoff.Launch.Disposed);
        Assert.Equal(1, handoff.Launch.AdmissionWaitCount);
    }

    /// <summary>Cancellation cannot hide a contradictory start receipt and issue.</summary>
    [Fact]
    public async Task CallerCancellationCannotHideMalformedStartedResult()
    {
        string root = Root("cancel-malformed-start");
        using var cancellation = new CancellationTokenSource();
        var handoff = new CancellingStartHandoff(
            cancellation,
            ImmutableBootstrapStartIssue.StartFailed);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(cancellation.Token);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.True(handoff.Launch.Disposed);
        Assert.Equal(0, handoff.Launch.AdmissionWaitCount);
    }

    /// <summary>Cancellation cannot hide failure to prove the started process tree empty.</summary>
    [Fact]
    public async Task CallerCancellationCannotHideTerminationUnconfirmedCleanup()
    {
        string root = Root("cancel-unconfirmed-cleanup");
        using var cancellation = new CancellationTokenSource();
        var handoff = new CancellingStartHandoff(
            cancellation,
            ImmutableBootstrapStartIssue.None,
            ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(cancellation.Token);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.True(handoff.Launch.Disposed);
        Assert.Equal(1, handoff.Launch.AdmissionWaitCount);
    }

    /// <summary>Caller cancellation cannot cancel the bounded cleanup observation itself.</summary>
    [Fact]
    public async Task CallerCancellationAfterAdmissionStillObservesCleanup()
    {
        string root = Root("cancel-after-admission");
        using var cancellation = new CancellationTokenSource();
        using var handoff = new RecordingBootstrapHandoff(
            ImmutableBootstrapCompletionOutcome.Unavailable)
        {
            AdmissionAction = cancellation.Cancel,
        };
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.RunAsync(cancellation.Token).AsTask());

        Assert.Equal(1, handoff.CompletionWaitCount);
    }

    /// <summary>Late admission uses the reserved cleanup budget before returning health failure.</summary>
    [Fact]
    public async Task LateAdmissionUsesFreshCleanupAuthority()
    {
        string root = Root("late-admission-cleanup");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new RecordingBootstrapHandoff(
            ImmutableBootstrapCompletionOutcome.Unavailable)
        {
            AdmissionAction = () => time.Advance(TimeSpan.FromSeconds(1)),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.HealthUnavailable, result.Outcome);
        Assert.Equal(1, handoff.CompletionWaitCount);
    }

    /// <summary>Cold exact Bootstrap verification is not rejected by the former 1.5-second cutoff.</summary>
    [Fact]
    public async Task DefaultAdmissionBudgetAllowsColdExactBootstrapVerification()
    {
        string root = Root("cold-bootstrap-verification");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready)
        {
            StartAction = () => time.Advance(TimeSpan.FromSeconds(2)),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
        TimeSpan admissionDuration = result.AdmissionElapsed;
        Assert.Equal(TimeSpan.FromSeconds(2), admissionDuration);
        Assert.Equal(1, handoff.StartCount);
    }

    /// <summary>A READY returned after the completion cutoff is never accepted.</summary>
    [Fact]
    public async Task ReadyReturnedAfterCompletionCutoffFailsClosed()
    {
        string root = Root("late-ready");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready)
        {
            CompletionAction = () => time.Advance(TimeSpan.FromSeconds(1)),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            completionDeadline: TimeSpan.FromMilliseconds(500),
            timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.Equal(1, handoff.CompletionWaitCount);
    }

    /// <summary>Accepted READY or rollback remains committed if caller cancellation races the receipt.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapCompletionOutcome.Ready)]
    [InlineData(ImmutableBootstrapCompletionOutcome.RolledBack)]
    public async Task AcceptedCompletionWinsConcurrentCallerCancellation(
        ImmutableBootstrapCompletionOutcome completion)
    {
        string root = Root($"accepted-{completion}");
        using var cancellation = new CancellationTokenSource();
        using var handoff = new RecordingBootstrapHandoff(completion)
        {
            CompletionAction = cancellation.Cancel,
        };
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(cancellation.Token);

        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
        Assert.Equal(root, result.ManagedRoot);
        Assert.Equal(1, handoff.CompletionWaitCount);
    }

    /// <summary>The public timing constants match the accepted startup contract.</summary>
    [Fact]
    public void PublishedStartupBudgetsMatchTheAcceptedContract()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), ManagedLauncherEntryCoordinator.HealthyRoutingP95Target);
        Assert.Equal(TimeSpan.FromMilliseconds(250), ManagedLauncherEntryCoordinator.ProgressDelay);
        Assert.Equal(
            ManagedLauncherEntryCoordinator.ProgressDelay,
            ManagedLauncherEntryCoordinator.DefaultHealthObservationDeadline);
        Assert.Equal(
            TimeSpan.FromMilliseconds(5500),
            ManagedLauncherEntryCoordinator.DefaultAdmissionDeadline);
        Assert.Equal(
            ManagedLauncherEntryCoordinator.DefaultAdmissionDeadline,
            ManagedLauncherEntryCoordinator.DefaultAdmissionOperationCutoff +
                ManagedLauncherEntryCoordinator.DefaultCleanupObservationBudget);
        Assert.Equal(TimeSpan.FromSeconds(45), ManagedLauncherEntryCoordinator.DefaultCompletionDeadline);
        Assert.Equal(
            ManagedLauncherEntryCoordinator.DefaultCompletionDeadline,
            ManagedLauncherEntryCoordinator.DefaultCompletionOperationCutoff +
                ManagedLauncherEntryCoordinator.DefaultCleanupObservationBudget);
        Assert.True(
            ManagedLauncherEntryCoordinator.DefaultCompletionDeadline >=
            (LauncherBootstrapCoordinator.DefaultReadyDeadline * 2) + TimeSpan.FromSeconds(5));
    }
}
