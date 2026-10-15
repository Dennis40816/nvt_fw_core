// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics.CodeAnalysis;

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Test fixtures for the single local-only Launcher entry owner.</summary>
public sealed partial class ManagedLauncherEntryCoordinatorTests
{
    private static ManagedLauncherEntryCoordinator Create(
        string root,
        IVersionManagerStateStore stateStore,
        IManagedInstallationRootProbe rootProbe,
        IImmutableBootstrapHandoff handoff,
        TimeSpan? admissionDeadline = null,
        TimeSpan? completionDeadline = null,
        TimeSpan? healthObservationDeadline = null,
        TimeProvider? timeProvider = null,
        IManagedDistributionPayloadSource? payloadSource = null,
        ManagedAppVersion? runningLauncherVersion = null)
    {
        return new(
            root,
            stateStore,
            rootProbe,
            payloadSource ?? new EntryPayloadSource(),
            runningLauncherVersion ?? ManagedAppVersion.Parse("1.0.4"),
            handoff,
            admissionDeadline,
            completionDeadline,
            timeProvider ?? new ManualTimeProvider(DateTimeOffset.UnixEpoch),
            healthObservationDeadline);
    }

    private sealed class EntryPayloadSource : IManagedDistributionPayloadSource
    {
        // Guard: the scenario driver configures this immutable override before entry starts.
        internal ManagedDistributionPayloadEntryAdmissionResult? AdmissionOverride { get; set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal Action? AdmissionAction { get; set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int AdmissionCount { get; private set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal ManagedDistributionPayloadIssue AdmissionIssue { get; set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal ManagedAppVersion LauncherVersion { get; set; } = ManagedAppVersion.Parse("1.0.4");

        public ValueTask<ManagedDistributionPayloadEntryAdmissionResult> AdmitEntryAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdmissionCount++;
            if (AdmissionOverride is { } overridden)
            {
                return ValueTask.FromResult(overridden);
            }
            AdmissionAction?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(AdmissionIssue == ManagedDistributionPayloadIssue.None
                ? new ManagedDistributionPayloadEntryAdmissionResult(
                    LauncherVersion,
                    _bootstrap,
                    ManagedDistributionPayloadIssue.None)
                : new ManagedDistributionPayloadEntryAdmissionResult(
                    default,
                    null,
                    AdmissionIssue));
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

    private static VersionManagerState BoundState(string root)
    {
        ManagedVersionAdmission admission = new(
            ManagedAppVersion.Parse("1.0.4"),
            "release|1.0.4",
            new string('a', 64));
        return VersionManagerState.Create(
            updateSource: null,
            activeVersion: admission.Version,
            lastKnownGoodVersion: admission.Version,
            [admission],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: root);
    }

    private sealed class EntryStateStore : IVersionManagerStateStore
    {
        private readonly VersionManagerStateLoadResult _result;

        internal EntryStateStore(VersionManagerState? state)
        {
            _result = state is null
                ? new(null, VersionManagerStateLoadIssue.Missing)
                : new(state, VersionManagerStateLoadIssue.None);
        }

        internal EntryStateStore(VersionManagerStateLoadIssue issue)
        {
            _result = new(null, issue);
        }

        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int LoadCount { get; private set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int LeaseCount { get; private set; }

        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return ValueTask.FromResult(_result);
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
            "Ownership transfers into the returned write-lease result and the coordinator disposes it.")]
        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            LeaseCount++;
            throw new InvalidOperationException("Healthy entry must not acquire the writer lease.");
        }

        public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Entry classification is read-only.");
        }
    }

    private sealed class RecordingRootProbe(ManagedInstallationRootStatus status)
        : IManagedInstallationRootProbe
    {
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int ObserveCount { get; private set; }

        public ValueTask<ManagedInstallationRootObservation> ObserveAsync(
            string managedRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObserveCount++;
            return ValueTask.FromResult(new ManagedInstallationRootObservation(status));
        }
    }

    private sealed class RecordingBootstrapHandoff : IImmutableBootstrapHandoff
    {
        private readonly ImmutableBootstrapCompletionOutcome? _completionOutcome;
        private readonly ImmutableBootstrapAdmissionOutcome _admissionOutcome =
            ImmutableBootstrapAdmissionOutcome.Admitted;
        private readonly ImmutableBootstrapStartIssue _startIssue;

        internal RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome completionOutcome)
        {
            _completionOutcome = completionOutcome;
        }

        internal RecordingBootstrapHandoff(ImmutableBootstrapStartIssue startIssue)
        {
            _startIssue = startIssue;
        }

        internal RecordingBootstrapHandoff(ImmutableBootstrapAdmissionOutcome admissionOutcome)
        {
            _completionOutcome = ImmutableBootstrapCompletionOutcome.Ready;
            _admissionOutcome = admissionOutcome;
        }

        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int StartCount { get; private set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal Action? StartAction { get; set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        private sealed record StartObservation(string Root, ManagedImmutableBootstrapIdentity Identity);
        // Guard: the scenario driver observes this snapshot after start completes.
        private StartObservation? _lastStart;
        internal string? LastRoot => _lastStart?.Root;
        internal ManagedImmutableBootstrapIdentity? LastIdentity => _lastStart?.Identity;
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal Action? AdmissionAction { get; set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int CompletionWaitCount { get; private set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal Action? CompletionAction { get; set; }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
            "Ownership transfers into the returned launch receipt and the coordinator disposes it.")]
        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            StartAction?.Invoke();
            _lastStart = new(managedRoot, expectedIdentity);
            return ValueTask.FromResult(_completionOutcome is { } completion
                ? new ImmutableBootstrapStartResult(
                    new ImmediateBootstrapLaunch(
                        _admissionOutcome,
                        completion,
                        AdmissionAction,
                        () =>
                        {
                            CompletionWaitCount++;
                            CompletionAction?.Invoke();
                        }),
                    ImmutableBootstrapStartIssue.None)
                : new ImmutableBootstrapStartResult(null, _startIssue));
        }
    }

    private sealed class ImmediateBootstrapLaunch(
        ImmutableBootstrapAdmissionOutcome admission,
        ImmutableBootstrapCompletionOutcome outcome,
        Action? admissionWaited,
        Action completionWaited)
        : IImmutableBootstrapLaunch
    {
        public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
            ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            admissionWaited?.Invoke();
            return ValueTask.FromResult(AdmissionResult(admission));
        }

        public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
            ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completionWaited();
            return ValueTask.FromResult(CompletionResult(outcome));
        }

        public void Dispose()
        {
        }

        private static ImmutableBootstrapAdmissionResult AdmissionResult(
            ImmutableBootstrapAdmissionOutcome outcome)
        {
            return outcome switch
            {
                ImmutableBootstrapAdmissionOutcome.Admitted or
                ImmutableBootstrapAdmissionOutcome.HealthUnavailable => new(outcome),
                ImmutableBootstrapAdmissionOutcome.LaunchFailed => new(
                    outcome,
                    ExitIssue: ImmutableBootstrapExitIssue.StartFailed),
                ImmutableBootstrapAdmissionOutcome.Busy => new(
                    outcome,
                    ImmutableBootstrapExitCodeCodec.EncodeFailure(ImmutableBootstrapExitIssue.Busy),
                    ImmutableBootstrapExitIssue.Busy),
                ImmutableBootstrapAdmissionOutcome.RecoveryRequired => new(
                    outcome,
                    ImmutableBootstrapExitCodeCodec.EncodeFailure(ImmutableBootstrapExitIssue.InvalidState),
                    ImmutableBootstrapExitIssue.InvalidState),
                ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed => new(
                    outcome,
                    ExitIssue: ImmutableBootstrapExitIssue.TerminationUnconfirmed),
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
            };
        }

        private static ImmutableBootstrapCompletionResult CompletionResult(
            ImmutableBootstrapCompletionOutcome outcome)
        {
            return outcome switch
            {
                ImmutableBootstrapCompletionOutcome.Ready => new(
                    outcome,
                    ImmutableBootstrapExitCodeCodec.Ready),
                ImmutableBootstrapCompletionOutcome.RolledBack => new(
                    outcome,
                    ImmutableBootstrapExitCodeCodec.RolledBack),
                ImmutableBootstrapCompletionOutcome.Failed => new(
                    outcome,
                    ImmutableBootstrapExitCodeCodec.EncodeFailure(ImmutableBootstrapExitIssue.StartFailed),
                    ImmutableBootstrapExitIssue.StartFailed),
                ImmutableBootstrapCompletionOutcome.Unavailable => new(outcome),
                ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed => new(
                    outcome,
                    ExitIssue: ImmutableBootstrapExitIssue.TerminationUnconfirmed),
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
            };
        }
    }

    private sealed class CancellingStartHandoff(
        CancellationTokenSource cancellation,
        ImmutableBootstrapStartIssue issue = ImmutableBootstrapStartIssue.None,
        ImmutableBootstrapAdmissionOutcome admissionOutcome =
            ImmutableBootstrapAdmissionOutcome.HealthUnavailable)
        : IImmutableBootstrapHandoff
    {
        internal CancellingStartLaunch Launch { get; } = new(admissionOutcome);

        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            Cancel(cancellation);
            return ValueTask.FromResult(new ImmutableBootstrapStartResult(
                Launch,
                issue));
        }
    }

    private sealed class CancellingStartLaunch(
        ImmutableBootstrapAdmissionOutcome admissionOutcome =
            ImmutableBootstrapAdmissionOutcome.HealthUnavailable) : IImmutableBootstrapLaunch
    {
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int AdmissionWaitCount { get; private set; }

        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal bool Disposed { get; private set; }

        public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
            ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken)
        {
            AdmissionWaitCount++;
            Assert.True(cancellationToken.IsCancellationRequested);
            return ValueTask.FromResult(new ImmutableBootstrapAdmissionResult(admissionOutcome));
        }

        public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
            ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Cancellation must win before completion.");
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class MalformedStartHandoff(
        bool attachLaunch,
        ImmutableBootstrapStartIssue issue) : IImmutableBootstrapHandoff
    {
        internal CancellingStartLaunch Launch { get; } = new();

        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ImmutableBootstrapStartResult(
                attachLaunch ? Launch : null,
                issue));
        }
    }

    private sealed class DeferredBootstrapHandoff : IImmutableBootstrapHandoff
    {
        private readonly TaskCompletionSource<ImmutableBootstrapCompletionResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completionWaitStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task CompletionWaitStarted => _completionWaitStarted.Task;
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal bool CompletionWaitCancelled { get; private set; }

        internal void Complete(ImmutableBootstrapCompletionOutcome outcome)
        {
            Assert.Equal(ImmutableBootstrapCompletionOutcome.Ready, outcome);
            _ = _completion.TrySetResult(new ImmutableBootstrapCompletionResult(
                outcome,
                ImmutableBootstrapExitCodeCodec.Ready));
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
            "Ownership transfers into the returned launch receipt and the coordinator disposes it.")]
        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ImmutableBootstrapStartResult(
                new DeferredBootstrapLaunch(
                    _completion.Task,
                    () => _completionWaitStarted.TrySetResult(),
                    () => CompletionWaitCancelled = true),
                ImmutableBootstrapStartIssue.None));
        }
    }

    private sealed class DeferredBootstrapLaunch(
        Task<ImmutableBootstrapCompletionResult> completion,
        Action started,
        Action cancelled)
        : IImmutableBootstrapLaunch
    {
        public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
            ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ImmutableBootstrapAdmissionResult(
                ImmutableBootstrapAdmissionOutcome.Admitted));
        }

        public async ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
            ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken)
        {
            started();
            try
            {
                return await completion.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled();
                throw;
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class DeferredAdmissionBootstrapHandoff : IImmutableBootstrapHandoff
    {
        private readonly TaskCompletionSource _admissionWaitStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task AdmissionWaitStarted => _admissionWaitStarted.Task;
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal bool AdmissionWaitCancelled { get; private set; }
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int CompletionWaitCount { get; private set; }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
            "Ownership transfers into the returned launch receipt and the coordinator disposes it.")]
        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ImmutableBootstrapStartResult(
                new DeferredAdmissionBootstrapLaunch(this),
                ImmutableBootstrapStartIssue.None));
        }

        private sealed class DeferredAdmissionBootstrapLaunch(
            DeferredAdmissionBootstrapHandoff owner) : IImmutableBootstrapLaunch
        {
            public async ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
                ImmutableBootstrapWaitBudget budget,
                CancellationToken cancellationToken)
            {
                _ = owner._admissionWaitStarted.TrySetResult();
                try
                {
                    await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(cancellationToken);
                    throw new InvalidOperationException("Unreachable.");
                }
                catch (OperationCanceledException)
                {
                    owner.AdmissionWaitCancelled = true;
                    throw;
                }
            }

            public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
                ImmutableBootstrapWaitBudget budget,
                CancellationToken cancellationToken)
            {
                owner.CompletionWaitCount++;
                return ValueTask.FromResult(new ImmutableBootstrapCompletionResult(
                    ImmutableBootstrapCompletionOutcome.Ready,
                    ImmutableBootstrapExitCodeCodec.Ready));
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class TwoAttemptBootstrapHandoff(
        ManualTimeProvider timeProvider,
        TimeSpan attemptDuration)
        : IImmutableBootstrapHandoff
    {
        // Guard: the scenario driver and awaited signals serialize all accesses.
        internal int CompletedAttemptCount { get; private set; }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
            "Ownership transfers into the returned launch receipt and the coordinator disposes it.")]
        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ImmutableBootstrapStartResult(
                new TwoAttemptBootstrapLaunch(this, timeProvider, attemptDuration),
                ImmutableBootstrapStartIssue.None));
        }

        private sealed class TwoAttemptBootstrapLaunch(
            TwoAttemptBootstrapHandoff owner,
            ManualTimeProvider timeProvider,
            TimeSpan duration) : IImmutableBootstrapLaunch
        {
            public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
                ImmutableBootstrapWaitBudget budget,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new ImmutableBootstrapAdmissionResult(
                    ImmutableBootstrapAdmissionOutcome.Admitted));
            }

            public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
                ImmutableBootstrapWaitBudget budget,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                timeProvider.Advance(duration);
                owner.CompletedAttemptCount++;
                cancellationToken.ThrowIfCancellationRequested();
                timeProvider.Advance(duration);
                owner.CompletedAttemptCount++;
                return ValueTask.FromResult(new ImmutableBootstrapCompletionResult(
                    ImmutableBootstrapCompletionOutcome.RolledBack,
                    ImmutableBootstrapExitCodeCodec.RolledBack));
            }

            public void Dispose()
            {
            }
        }
    }
}
