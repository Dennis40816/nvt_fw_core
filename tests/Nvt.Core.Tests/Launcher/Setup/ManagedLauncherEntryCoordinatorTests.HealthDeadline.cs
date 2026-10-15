// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

public sealed partial class ManagedLauncherEntryCoordinatorTests
{
    /// <summary>An uncooperative payload read cannot run before or beyond the health deadline.</summary>
    [Fact]
    public async Task UncooperativePayloadAdmissionIsAbandonedAtHealthDeadline()
    {
        string root = Root("uncooperative-payload");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var payload = new PendingPayloadSource();
        var state = new EntryStateStore(BoundState(root));
        var roots = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            state,
            roots,
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            healthObservationDeadline: TimeSpan.FromMilliseconds(250),
            timeProvider: time,
            payloadSource: payload);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        await payload.Entered.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(250));

        ManagedLauncherEntryResult result = await running.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherEntryOutcome.HealthUnavailable, result.Outcome);
        Assert.Equal(0, state.LoadCount);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);

        payload.Complete();
        await payload.Returned.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, state.LoadCount);
    }

    /// <summary>Payload, state, and root share one absolute 250 ms health budget.</summary>
    [Fact]
    public async Task PayloadAndStateShareOneAbsoluteHealthDeadline()
    {
        string root = Root("shared-payload-state-deadline");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var payload = new EntryPayloadSource
        {
            AdmissionAction = () => time.Advance(TimeSpan.FromMilliseconds(200)),
        };
        var state = new PendingStateStore();
        var roots = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            state,
            roots,
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            healthObservationDeadline: TimeSpan.FromMilliseconds(250),
            timeProvider: time,
            payloadSource: payload);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        await state.Entered.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(49));
        Assert.False(running.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        ManagedLauncherEntryResult result = await running.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherEntryOutcome.HealthUnavailable, result.Outcome);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);

        state.Complete(BoundState(root));
        await state.Returned.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, roots.ObserveCount);
    }

    /// <summary>Payload, state, and root consume one deadline without a root-stage reset.</summary>
    [Fact]
    public async Task PayloadStateAndRootShareOneAbsoluteHealthDeadline()
    {
        string root = Root("shared-payload-state-root-deadline");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var payload = new EntryPayloadSource
        {
            AdmissionAction = () => time.Advance(TimeSpan.FromMilliseconds(200)),
        };
        var roots = new PendingRootProbe();
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            roots,
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            healthObservationDeadline: TimeSpan.FromMilliseconds(250),
            timeProvider: time,
            payloadSource: payload);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        await roots.Entered.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(49));
        Assert.False(running.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        ManagedLauncherEntryResult result = await running.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherEntryOutcome.HealthUnavailable, result.Outcome);
        Assert.Equal(0, handoff.StartCount);

        roots.Complete(ManagedInstallationRootStatus.Present);
        await roots.Returned.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>An uncooperative state read cannot hold the entry path past 250 ms.</summary>
    [Fact]
    public async Task UncooperativeStateLoadIsAbandonedAtHealthDeadline()
    {
        string root = Root("uncooperative-state");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var state = new PendingStateStore();
        var roots = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            state,
            roots,
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            healthObservationDeadline: TimeSpan.FromMilliseconds(250),
            timeProvider: time);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        await state.Entered.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(running.IsCompleted);

        time.Advance(TimeSpan.FromMilliseconds(249));
        Assert.False(running.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        ManagedLauncherEntryResult result = await running.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherEntryOutcome.HealthUnavailable, result.Outcome);
        Assert.False(state.Pending.IsCompleted);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);

        state.Complete(BoundState(root));
        await state.Returned.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>An uncooperative exact-root read cannot start a child after timeout.</summary>
    [Fact]
    public async Task UncooperativeRootProbeIsAbandonedAtHealthDeadline()
    {
        string root = Root("uncooperative-root");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var roots = new PendingRootProbe();
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            roots,
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            healthObservationDeadline: TimeSpan.FromMilliseconds(250),
            timeProvider: time);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        await roots.Entered.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(250));

        ManagedLauncherEntryResult result = await running.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherEntryOutcome.HealthUnavailable, result.Outcome);
        Assert.False(roots.Pending.IsCompleted);
        Assert.Equal(0, handoff.StartCount);

        roots.Complete(ManagedInstallationRootStatus.Present);
        await roots.Returned.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>In-flight caller cancellation remains cancellation, not health failure.</summary>
    [Fact]
    public async Task InFlightCallerCancellationIsNotConvertedIntoHealthFailure()
    {
        string root = Root("in-flight-cancel");
        using var cancellation = new CancellationTokenSource();
        var state = new PendingStateStore();
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            state,
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(cancellation.Token).AsTask();
        await state.Entered.WaitAsync(TestContext.Current.CancellationToken);
        Cancel(cancellation);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, handoff.StartCount);
        state.Complete(BoundState(root));
        await state.Returned.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The health timer is retired before Bootstrap admission begins.</summary>
    [Fact]
    public async Task HealthDeadlineCannotCancelBootstrapAfterClassification()
    {
        string root = Root("health-retired-before-bootstrap");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new ControlledAdmissionHandoff();
        ManagedLauncherEntryCoordinator coordinator = Create(
            root,
            new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            admissionDeadline: TimeSpan.FromSeconds(1),
            healthObservationDeadline: TimeSpan.FromMilliseconds(250),
            timeProvider: time);

        Task<ManagedLauncherEntryResult> running = coordinator.RunAsync(
            TestContext.Current.CancellationToken).AsTask();
        await handoff.AdmissionStarted.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(250));

        Assert.False(running.IsCompleted);
        Assert.False(handoff.AdmissionCancelled);
        handoff.Admit();
        ManagedLauncherEntryResult result = await running.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
        Assert.False(handoff.AdmissionCancelled);
    }

    private sealed class PendingStateStore : IVersionManagerStateStore
    {
        private readonly TaskCompletionSource<VersionManagerStateLoadResult> _pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _returned =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Pending => _pending.Task;
        internal Task Returned => _returned.Task;
        internal Task Entered => _entered.Task;

        internal void Complete(VersionManagerState state)
        {
            _ = _pending.TrySetResult(new(state, VersionManagerStateLoadIssue.None));
        }

        public async ValueTask<VersionManagerStateLoadResult> LoadAsync(
            CancellationToken cancellationToken)
        {
            _ = _entered.TrySetResult();
            try
            {
                return await _pending.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _ = _returned.TrySetResult();
            }
        }

        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException();
        }

        public ValueTask SaveAsync(
            VersionManagerState state,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException();
        }
    }

    private sealed class PendingPayloadSource : IManagedDistributionPayloadSource
    {
        private readonly TaskCompletionSource<ManagedDistributionPayloadEntryAdmissionResult> _pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _returned =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;
        internal Task Returned => _returned.Task;

        internal void Complete()
        {
            _ = _pending.TrySetResult(new(
                ManagedAppVersion.Parse("1.0.4"),
                _bootstrap,
                ManagedDistributionPayloadIssue.None));
        }

        public async ValueTask<ManagedDistributionPayloadEntryAdmissionResult> AdmitEntryAsync(
            CancellationToken cancellationToken)
        {
            _ = _entered.TrySetResult();
            try
            {
                return await _pending.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _ = _returned.TrySetResult();
            }
        }

        public ValueTask<ManagedDistributionPayloadInspectionResult> InspectAsync(
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Entry routing must not inspect payload content.");
        }

        public ValueTask<ManagedDistributionPayloadCaptureResult> CaptureExactAsync(
            ManagedDistributionPayloadIdentity expected,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Entry routing must not capture payload content.");
        }
    }

    private sealed class PendingRootProbe : IManagedInstallationRootProbe
    {
        private readonly TaskCompletionSource<ManagedInstallationRootObservation> _pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _returned =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Pending => _pending.Task;
        internal Task Entered => _entered.Task;
        internal Task Returned => _returned.Task;

        internal void Complete(ManagedInstallationRootStatus status)
        {
            _ = _pending.TrySetResult(new(status));
        }

        public async ValueTask<ManagedInstallationRootObservation> ObserveAsync(
            string managedRoot,
            CancellationToken cancellationToken)
        {
            _ = _entered.TrySetResult();
            try
            {
                return await _pending.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _ = _returned.TrySetResult();
            }
        }
    }

    private sealed class ControlledAdmissionHandoff : IImmutableBootstrapHandoff, IDisposable
    {
        private readonly TaskCompletionSource<ImmutableBootstrapAdmissionResult> _admission =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _admissionStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Guard: the scenario driver and awaited signals serialize creation and cleanup.
        private ControlledAdmissionLaunch? _ownedLaunch;

        public void Dispose() => _ownedLaunch?.Dispose();

        internal Task AdmissionStarted => _admissionStarted.Task;
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal bool AdmissionCancelled { get; private set; }

        internal void Admit()
        {
            _ = _admission.TrySetResult(new(ImmutableBootstrapAdmissionOutcome.Admitted));
        }

        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ownedLaunch = new ControlledAdmissionLaunch(this);
            return ValueTask.FromResult(new ImmutableBootstrapStartResult(
                _ownedLaunch,
                ImmutableBootstrapStartIssue.None));
        }

        private sealed class ControlledAdmissionLaunch(ControlledAdmissionHandoff owner)
            : IImmutableBootstrapLaunch
        {
            public async ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
                ImmutableBootstrapWaitBudget budget,
                CancellationToken cancellationToken)
            {
                _ = owner._admissionStarted.TrySetResult();
                try
                {
                    return await owner._admission.Task.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    owner.AdmissionCancelled = true;
                    throw;
                }
            }

            public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
                ImmutableBootstrapWaitBudget budget,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new ImmutableBootstrapCompletionResult(
                    ImmutableBootstrapCompletionOutcome.Ready,
                    ImmutableBootstrapExitCodeCodec.Ready));
            }

            public void Dispose()
            {
            }
        }
    }

}
