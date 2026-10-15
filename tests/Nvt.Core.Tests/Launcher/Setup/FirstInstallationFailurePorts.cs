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

using static Nvt.Core.Tests.Launcher.Setup.FirstInstallationTestSupport;
using static Nvt.Core.Tests.Launcher.Setup.SetupTestWorkspace;

internal sealed record InstallHarness(
    ManagedFirstInstallationExperience Experience,
    ManagedFirstInstallationPlan Plan,
    SequencedStateStore State,
    SequencedRootProbe Roots,
    RecordingPayloadSource Payload,
    RecordingCandidateSource CandidateSource,
    RecordingRootMaterializer Materializer,
    SetupBootstrapHandoff Handoff);

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class CancellingBootstrapHandoff(
    SequencedStateStore state,
    CancellationTokenSource cancellation,
    bool duringAdmission) : IImmutableBootstrapLeaseHandoff
{
    internal CancellingBootstrapLaunch Launch { get; } = new(cancellation, duringAdmission);

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.False(state.LeaseActive);
        return ValueTask.FromResult(StartResultOwningLease(Launch, ownedLease));
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
    CancellationTokenSource cancellation) : IImmutableBootstrapLeaseHandoff
{
    internal TypedFailureCancellingLaunch Launch { get; } = new(cancellation);

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        Assert.False(state.LeaseActive);
        return ValueTask.FromResult(StartResultOwningLease(Launch, ownedLease));
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
    CancellationTokenSource cancellation) : IImmutableBootstrapLeaseHandoff
{
    internal TrackingBootstrapLaunch Launch { get; } = new();

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        Assert.False(state.LeaseActive);
        Cancel(cancellation);
        return ValueTask.FromResult(StartResultOwningLease(Launch, ownedLease));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class MalformedStartBootstrapHandoff(
    SequencedStateStore state,
    bool attachLaunch,
    ImmutableBootstrapStartIssue issue,
    CancellationTokenSource? cancellation = null) : IImmutableBootstrapLeaseHandoff
{
    internal TrackingBootstrapLaunch Launch { get; } = new();

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.False(state.LeaseActive);
        Cancel(cancellation);
        if (attachLaunch)
        {
            return ValueTask.FromResult(StartResultOwningLease(Launch, ownedLease, issue));
        }
        ownedLease.Dispose();
        return ValueTask.FromResult(new ImmutableBootstrapStartResult(null, issue));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class TrackingBootstrapLaunch : IImmutableBootstrapLaunch
{
    internal int AdmissionWaitCount { get; private set; }

    internal bool Disposed { get; private set; }

    public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        AdmissionWaitCount++;
        Assert.True(cancellationToken.IsCancellationRequested);
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
