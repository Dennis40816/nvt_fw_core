// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.Tests.Launcher.Setup;

using static Nvt.Core.Tests.Launcher.Setup.FirstInstallationTestSupport;

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class LeaseOwningBootstrapLaunch(
    IImmutableBootstrapLaunch launch,
    IManagedExecutableLaunchLease ownedLease) : IImmutableBootstrapLaunch
{
    public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        return launch.WaitForAdmissionAsync(budget, cancellationToken);
    }

    public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        return launch.WaitForCompletionAsync(budget, cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            launch.Dispose();
        }
        finally
        {
            ownedLease.Dispose();
        }
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class SetupBootstrapHandoff(SequencedStateStore state)
    : IImmutableBootstrapLeaseHandoff, IDisposable
{
    internal Action? AdmissionAction { get; set; }
    internal Action<ImmutableBootstrapWaitBudget>? AdmissionBudgetObserved { get; set; }
    internal ImmutableBootstrapAdmissionOutcome AdmissionOutcome { get; set; } =
        ImmutableBootstrapAdmissionOutcome.Admitted;
    internal int? AdmissionExitCode { get; set; }
    internal ImmutableBootstrapExitIssue AdmissionExitIssue { get; set; }
    internal Action? CompletionAction { get; set; }
    internal ImmutableBootstrapCompletionOutcome CompletionOutcome { get; set; } =
        ImmutableBootstrapCompletionOutcome.Ready;
    internal int? CompletionExitCode { get; set; } = 0;
    internal ImmutableBootstrapExitIssue CompletionExitIssue { get; set; }
    internal bool IgnoreAdmissionCancellation { get; set; }
    internal SetupBootstrapLaunch? LastLaunch { get; private set; }

    public void Dispose()
    {
        if (LastLaunch is { Disposed: false } launch)
        {
            launch.Dispose();
        }
    }
    internal Action? StartAction { get; set; }
    internal ImmutableBootstrapStartIssue StartIssue { get; set; }
    internal int StartCount { get; private set; }
    internal IManagedExecutableLaunchLease? ReceivedLease { get; private set; }

    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.RecordContractViolation(state.LeaseActive);
        state.RecordContractViolation(Bootstrap != expectedIdentity);
        state.RecordContractViolation(!ownedLease.TryValidateForStart());
        ReceivedLease = ownedLease;
        state.Events.Add("handoff-owned-lease");
        state.Observe?.Invoke("handoff-owned-lease");
        StartCount++;
        try
        {
            StartAction?.Invoke();
        }
        catch
        {
            // The start adapter still owns custody when its hook throws before returning a launch.
            ownedLease.Dispose();
            throw;
        }
        if (StartIssue == ImmutableBootstrapStartIssue.None)
        {
            LastLaunch = new SetupBootstrapLaunch(
                    state,
                    ownedLease,
                    AdmissionOutcome,
                    AdmissionExitCode,
                    AdmissionExitIssue,
                    CompletionOutcome,
                    CompletionExitCode,
                    CompletionExitIssue,
                    AdmissionAction,
                    CompletionAction,
                    AdmissionBudgetObserved,
                    IgnoreAdmissionCancellation);
            return ValueTask.FromResult(new ImmutableBootstrapStartResult(
                LastLaunch,
                ImmutableBootstrapStartIssue.None));
        }
        ownedLease.Dispose();
        return ValueTask.FromResult(new ImmutableBootstrapStartResult(null, StartIssue));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class SetupBootstrapLaunch(
    SequencedStateStore state,
    IManagedExecutableLaunchLease ownedLease,
    ImmutableBootstrapAdmissionOutcome admissionOutcome,
    int? admissionExitCode,
    ImmutableBootstrapExitIssue admissionExitIssue,
    ImmutableBootstrapCompletionOutcome completionOutcome,
    int? completionExitCode,
    ImmutableBootstrapExitIssue completionExitIssue,
    Action? admissionAction,
    Action? completionAction,
    Action<ImmutableBootstrapWaitBudget>? admissionBudgetObserved,
    bool ignoreAdmissionCancellation) : IImmutableBootstrapLaunch
{
    internal bool Disposed { get; private set; }

    public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        if (!ignoreAdmissionCancellation)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        admissionBudgetObserved?.Invoke(budget);
        admissionAction?.Invoke();
        state.Events.Add("admitted");
        state.Observe?.Invoke("admitted");
        return ValueTask.FromResult(new ImmutableBootstrapAdmissionResult(
            admissionOutcome,
            admissionExitCode,
            admissionExitIssue));
    }

    public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        completionAction?.Invoke();
        state.Events.Add("ready");
        state.Observe?.Invoke("ready");
        return ValueTask.FromResult(new ImmutableBootstrapCompletionResult(
            completionOutcome,
            completionExitCode,
            completionExitIssue));
    }

    public void Dispose()
    {
        Disposed = true;
        ownedLease.Dispose();
    }
}
