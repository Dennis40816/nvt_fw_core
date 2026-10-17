// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

using static Nvt.Core.Tests.Launcher.Setup.FirstInstallationTestSupport;
using static Nvt.Core.Tests.Launcher.Setup.SetupTestWorkspace;

internal sealed record InstallHarness : IDisposable
{
    internal required ManagedFirstInstallationExperience Experience { get; init; }
    internal required ManagedFirstInstallationPlan Plan { get; init; }
    internal required SequencedStateStore State { get; init; }
    internal required SequencedRootProbe Roots { get; init; }
    internal required RecordingPayloadSource Payload { get; init; }
    internal required RecordingCandidateSource CandidateSource { get; init; }
    internal required RecordingRootMaterializer Materializer { get; init; }
    internal required SetupBootstrapHandoff Handoff { get; init; }

    public void Dispose()
    {
        Handoff.Dispose();
        Payload.Dispose();
        State.Dispose();
        Assert.False(Materializer.Transaction.ContractViolated);
        Assert.False(State.ContractViolated);
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class CancellingBootstrapHandoff(
    SequencedStateStore state,
    CancellationTokenSource cancellation,
    bool duringAdmission) : IImmutableBootstrapLeaseHandoff, IDisposable
{
    // Guard: the scenario driver and awaited signals serialize creation and cleanup.
    private LeaseOwningBootstrapLaunch? _ownedLaunch;

    public void Dispose() => _ownedLaunch?.Dispose();

    internal CancellingBootstrapLaunch Launch { get; } = new(cancellation, duringAdmission);

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.RecordContractViolation(state.LeaseActive);
        _ownedLaunch = new LeaseOwningBootstrapLaunch(Launch, ownedLease);
        return ValueTask.FromResult(StartResultOwningLease(_ownedLaunch));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class CancellingBootstrapLaunch(
    CancellationTokenSource cancellation,
    bool duringAdmission) : IImmutableBootstrapLaunch
{
    internal bool CleanupReturned { get; private set; }
    internal bool Disposed { get; private set; }

    public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        return duringAdmission
            ? CancelAdmissionAsync()
            : ValueTask.FromResult(new ImmutableBootstrapAdmissionResult(
                ImmutableBootstrapAdmissionOutcome.Admitted));
    }

    public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        return CancelCompletionAsync();
    }

    public void Dispose()
    {
        Disposed = true;
    }

    private ValueTask<ImmutableBootstrapAdmissionResult> CancelAdmissionAsync()
    {
        Cancel(cancellation);
        CleanupReturned = true;
        return ValueTask.FromException<ImmutableBootstrapAdmissionResult>(
            new OperationCanceledException(cancellation.Token));
    }

    private ValueTask<ImmutableBootstrapCompletionResult> CancelCompletionAsync()
    {
        Cancel(cancellation);
        CleanupReturned = true;
        return ValueTask.FromException<ImmutableBootstrapCompletionResult>(
            new OperationCanceledException(cancellation.Token));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class TypedFailureCancellingHandoff(
    SequencedStateStore state,
    CancellationTokenSource cancellation) : IImmutableBootstrapLeaseHandoff, IDisposable
{
    // Guard: the scenario driver and awaited signals serialize creation and cleanup.
    private LeaseOwningBootstrapLaunch? _ownedLaunch;

    public void Dispose() => _ownedLaunch?.Dispose();

    internal TypedFailureCancellingLaunch Launch { get; } = new(cancellation);

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        state.RecordContractViolation(state.LeaseActive);
        _ownedLaunch = new LeaseOwningBootstrapLaunch(Launch, ownedLease);
        return ValueTask.FromResult(StartResultOwningLease(_ownedLaunch));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class TypedFailureCancellingLaunch(CancellationTokenSource cancellation)
    : IImmutableBootstrapLaunch
{
    internal bool Disposed { get; private set; }

    public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        Cancel(cancellation);
        return ValueTask.FromResult(new ImmutableBootstrapAdmissionResult(
            ImmutableBootstrapAdmissionOutcome.HealthUnavailable));
    }

    public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Completion must not run after caller cancellation.");
    }

    public void Dispose()
    {
        Disposed = true;
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class CancellingStartBootstrapHandoff(
    SequencedStateStore state,
    CancellationTokenSource cancellation) : IImmutableBootstrapLeaseHandoff, IDisposable
{
    // Guard: the scenario driver and awaited signals serialize creation and cleanup.
    private LeaseOwningBootstrapLaunch? _ownedLaunch;

    public void Dispose() => _ownedLaunch?.Dispose();

    internal TrackingBootstrapLaunch Launch { get; } = new(state);

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        state.RecordContractViolation(state.LeaseActive);
        Cancel(cancellation);
        _ownedLaunch = new LeaseOwningBootstrapLaunch(Launch, ownedLease);
        return ValueTask.FromResult(StartResultOwningLease(_ownedLaunch));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class MalformedStartBootstrapHandoff(
    SequencedStateStore state,
    bool attachLaunch,
    ImmutableBootstrapStartIssue issue,
    CancellationTokenSource? cancellation = null) : IImmutableBootstrapLeaseHandoff, IDisposable
{
    // Guard: the scenario driver and awaited signals serialize creation and cleanup.
    private LeaseOwningBootstrapLaunch? _ownedLaunch;

    public void Dispose() => _ownedLaunch?.Dispose();

    internal TrackingBootstrapLaunch Launch { get; } = new(state);

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.RecordContractViolation(state.LeaseActive);
        Cancel(cancellation);
        if (attachLaunch)
        {
            _ownedLaunch = new LeaseOwningBootstrapLaunch(Launch, ownedLease);
            return ValueTask.FromResult(StartResultOwningLease(_ownedLaunch, issue));
        }
        ownedLease.Dispose();
        return ValueTask.FromResult(new ImmutableBootstrapStartResult(null, issue));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class TrackingBootstrapLaunch(SequencedStateStore state) : IImmutableBootstrapLaunch
{
    internal int AdmissionWaitCount { get; private set; }

    internal bool Disposed { get; private set; }

    public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        AdmissionWaitCount++;
        state.RecordContractViolation(!cancellationToken.IsCancellationRequested);
        return ValueTask.FromResult(new ImmutableBootstrapAdmissionResult(
            ImmutableBootstrapAdmissionOutcome.HealthUnavailable));
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
