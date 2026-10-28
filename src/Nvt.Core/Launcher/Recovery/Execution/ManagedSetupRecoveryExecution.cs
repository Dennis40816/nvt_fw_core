// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Closed terminal outcomes for one explicit recovery execution attempt.</summary>
public enum ManagedSetupRecoveryExecutionOutcome
{
    /// <summary>The selected sequence completed and removed the exact marker last.</summary>
    Completed,
    /// <summary>Rollback was invoked without selecting Remove incomplete installation.</summary>
    ConfirmationRequired,
    /// <summary>Another process owns the canonical writer lease.</summary>
    Busy,
    /// <summary>One managed process lifetime role remains active.</summary>
    LifetimeActive,
    /// <summary>A complete general health observation was unavailable.</summary>
    HealthUnavailable,
    /// <summary>State, root, or marker I/O could not produce complete stable evidence.</summary>
    StateUnavailable,
    /// <summary>An exact authorized object could not be opened for required access.</summary>
    PermissionDenied,
    /// <summary>Candidate or inventory authority no longer matches the plan.</summary>
    SourceChanged,
    /// <summary>Facts changed or a safe deterministic restart prefix remains.</summary>
    RecoveryRequired,
    /// <summary>Foreign, malformed, unsafe, or unprovable residue requires human handling.</summary>
    ManualInterventionRequired,
    /// <summary>Cancellation occurred before the first irreversible deletion.</summary>
    Cancelled,
}

/// <summary>Terminal typed result from one explicit execution attempt.</summary>
/// <param name="Outcome">The closed terminal execution result.</param>
public sealed record ManagedSetupRecoveryExecutionResult(
    ManagedSetupRecoveryExecutionOutcome Outcome);

/// <summary>Mechanics-only outcome from the exact Infrastructure executor.</summary>
public enum ManagedSetupRecoveryExecutionPortOutcome
{
    /// <summary>The selected sequence completed and the marker was removed last.</summary>
    Completed,
    /// <summary>General health evidence became unavailable.</summary>
    HealthUnavailable,
    /// <summary>State or exact-object I/O became unavailable.</summary>
    StateUnavailable,
    /// <summary>An exact object could not be opened for required access.</summary>
    PermissionDenied,
    /// <summary>Candidate or inventory authority changed.</summary>
    SourceChanged,
    /// <summary>A safe deterministic restart prefix remains.</summary>
    RecoveryRequired,
    /// <summary>Foreign, unsafe, replacement, hole, or unprovable residue was found.</summary>
    ManualInterventionRequired,
    /// <summary>Cancellation occurred before the first irreversible deletion.</summary>
    Cancelled,
}

/// <summary>Selected action and current exact evidence passed to the mechanics-only executor.</summary>
public sealed class ManagedSetupRecoveryExecutionRequest
{
    internal ManagedSetupRecoveryExecutionRequest(
        ManagedSetupRecoveryAction action,
        ManagedSetupRecoveryExecutionToken executionToken)
    {
        Action = action;
        ExecutionToken = executionToken;
    }

    /// <summary>Gets the already-selected Application action.</summary>
    public ManagedSetupRecoveryAction Action { get; }

    /// <summary>Gets the current opaque exact-object and restart-prefix evidence.</summary>
    internal ManagedSetupRecoveryExecutionToken ExecutionToken { get; }
}

/// <summary>Exact mutation port that neither acquires a writer lease nor selects recovery policy.</summary>
public interface IManagedSetupRecoveryExecutionPort
{
    /// <summary>
    /// Executes the supplied sequence while Application retains the supplied live canonical lease.
    /// The adapter must validate that lease against its exact state path and must not dispose it.
    /// Cancellation before the first irreversible delete returns Cancelled; after the first delete
    /// the adapter must retain the marker and return RecoveryRequired instead of throwing cancellation.
    /// </summary>
    ValueTask<ManagedSetupRecoveryExecutionPortOutcome> ExecuteAsync(
        ManagedSetupRecoveryExecutionRequest request,
        VersionManagerWriteLeaseResult writerLease,
        CancellationToken cancellationToken);
}

/// <summary>
/// Owns explicit recovery execution, canonical writer custody, and terminal execution outcomes.
/// </summary>
public sealed class ManagedSetupRecoveryExecutionCoordinator
{
    private readonly IManagedSetupRecoveryStateStore _stateStore;
    private readonly IManagedInstallationRootProbe _rootProbe;
    private readonly IManagedSetupRecoveryProbe _markerProbe;
    private readonly IManagedProcessLifetimeProbe _lifetimeProbe;
    private readonly IManagedSetupRecoveryEvidenceProbe _evidenceProbe;
    private readonly IManagedSetupRecoveryExecutionPort _executionPort;
    private readonly string _statePathIdentity;

    /// <summary>Creates the Application execution owner over the canonical state store.</summary>
    public ManagedSetupRecoveryExecutionCoordinator(
        IManagedSetupRecoveryStateStore stateStore,
        IManagedInstallationRootProbe rootProbe,
        IManagedSetupRecoveryProbe markerProbe,
        IManagedProcessLifetimeProbe lifetimeProbe,
        IManagedSetupRecoveryEvidenceProbe evidenceProbe,
        IManagedSetupRecoveryExecutionPort executionPort)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        string statePathIdentity = stateStore.StatePathIdentity;
        if (string.IsNullOrWhiteSpace(statePathIdentity) ||
            !Path.IsPathFullyQualified(statePathIdentity))
        {
            throw new ArgumentException(
                "Recovery state store identity must be an absolute path.",
                nameof(stateStore));
        }
        _statePathIdentity = Path.GetFullPath(statePathIdentity);
        _rootProbe = rootProbe ?? throw new ArgumentNullException(nameof(rootProbe));
        _markerProbe = markerProbe ?? throw new ArgumentNullException(nameof(markerProbe));
        _lifetimeProbe = lifetimeProbe ?? throw new ArgumentNullException(nameof(lifetimeProbe));
        _evidenceProbe = evidenceProbe ?? throw new ArgumentNullException(nameof(evidenceProbe));
        _executionPort = executionPort ?? throw new ArgumentNullException(nameof(executionPort));
    }

    /// <summary>Executes one immutable plan after its action is explicitly selected.</summary>
    public async ValueTask<ManagedSetupRecoveryExecutionResult> ExecuteAsync(
        ManagedSetupRecoveryPlan plan,
        ManagedSetupRecoveryAction? confirmedAction,
        TimeSpan writerLeaseTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(writerLeaseTimeout, TimeSpan.Zero);
        if (confirmedAction is null)
        {
            return Result(plan.Action == ManagedSetupRecoveryAction.RemoveIncompleteInstallation
                ? ManagedSetupRecoveryExecutionOutcome.ConfirmationRequired
                : ManagedSetupRecoveryExecutionOutcome.RecoveryRequired);
        }
        if (confirmedAction != plan.Action)
        {
            return Result(ManagedSetupRecoveryExecutionOutcome.RecoveryRequired);
        }

        bool handedToExecutor = false;
        try
        {
            using VersionManagerWriteLeaseResult lease = await _stateStore.TryAcquireWriteLeaseAsync(
                writerLeaseTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                return Result(lease.Issue == VersionManagerWriteLeaseIssue.Busy
                    ? ManagedSetupRecoveryExecutionOutcome.Busy
                    : ManagedSetupRecoveryExecutionOutcome.StateUnavailable);
            }

            var prepared = await PrepareExecutionAsync(plan, cancellationToken).ConfigureAwait(false);
            if (prepared.Issue is { } issue)
            {
                return Result(issue);
            }
            ManagedSetupRecoveryExecutionRequest request = prepared.Request!;
            handedToExecutor = true;
            ManagedSetupRecoveryExecutionPortOutcome executed = await _executionPort.ExecuteAsync(
                request,
                lease,
                cancellationToken).ConfigureAwait(false);
            return Result(Map(executed));
        }
        catch (OperationCanceledException) when (!handedToExecutor)
        {
            return Result(ManagedSetupRecoveryExecutionOutcome.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return Result(ManagedSetupRecoveryExecutionOutcome.RecoveryRequired);
        }
    }

    private async ValueTask<(ManagedSetupRecoveryExecutionRequest? Request, ManagedSetupRecoveryExecutionOutcome? Issue)>
        PrepareExecutionAsync(ManagedSetupRecoveryPlan plan, CancellationToken cancellationToken)
    {
        ManagedSetupRecoveryExecutionOutcome? lifetime = await ObserveExecutionLifetimesAsync(
            cancellationToken).ConfigureAwait(false);
        if (lifetime is not null)
        {
            return (null, lifetime.Value);
        }

        VersionManagerStateLoadResult state = await _stateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ManagedInstallationRootObservation root = await _rootProbe.ObserveAsync(
            plan.ManagedRoot,
            cancellationToken).ConfigureAwait(false);
        ManagedSetupRecoveryFact marker = await _markerProbe.ObserveAsync(
            plan.ManagedRoot,
            _statePathIdentity,
            cancellationToken).ConfigureAwait(false);
        ManagedSetupRecoveryExecutionOutcome? observationIssue =
            ClassifyExecutionObservationIssue(state, root, marker);
        if (observationIssue is not null)
        {
            return (null, observationIssue.Value);
        }
        if (marker.Transaction is not { } transaction)
        {
            return (null, ManagedSetupRecoveryExecutionOutcome.RecoveryRequired);
        }

        ManagedSetupRecoveryEvidenceObservation evidence = await _evidenceProbe.ObserveAsync(
            transaction,
            cancellationToken).ConfigureAwait(false);
        ManagedSetupRecoveryExecutionOutcome? evidenceIssue =
            ClassifyExecutionEvidenceIssue(evidence);
        if (evidenceIssue is not null)
        {
            return (null, evidenceIssue.Value);
        }

        ManagedSetupRecoveryAction? currentAction = ManagedSetupRecoveryPolicy.SelectAction(
            state,
            transaction,
            evidence,
            plan.ManagedRoot);
        if (currentAction is null ||
            currentAction != plan.Action ||
            !ManagedSetupRecoveryPolicy.PlanMatches(plan, state, transaction, evidence))
        {
            return (null, ManagedSetupRecoveryExecutionOutcome.RecoveryRequired);
        }

        return (new ManagedSetupRecoveryExecutionRequest(
            currentAction.Value,
            evidence.ExecutionToken!), null);
    }

    private async ValueTask<ManagedSetupRecoveryExecutionOutcome?> ObserveExecutionLifetimesAsync(
        CancellationToken cancellationToken)
    {
        var lifetimes = new ManagedProcessLifetimeStatus[ManagedSetupRecoveryPolicy._lifetimeKinds.Length];
        for (int index = 0; index < ManagedSetupRecoveryPolicy._lifetimeKinds.Length; index++)
        {
            lifetimes[index] = await _lifetimeProbe.ObserveAsync(
                _statePathIdentity,
                ManagedSetupRecoveryPolicy._lifetimeKinds[index],
                cancellationToken).ConfigureAwait(false);
        }
        return lifetimes.Contains(ManagedProcessLifetimeStatus.Active)
            ? ManagedSetupRecoveryExecutionOutcome.LifetimeActive
            : lifetimes.Any(static status => status != ManagedProcessLifetimeStatus.Exited)
                ? ManagedSetupRecoveryExecutionOutcome.HealthUnavailable
                : null;
    }

    private static ManagedSetupRecoveryExecutionOutcome? ClassifyExecutionObservationIssue(
        VersionManagerStateLoadResult state,
        ManagedInstallationRootObservation root,
        ManagedSetupRecoveryFact marker)
    {
        return (state.Issue, root.Status, marker.Kind) switch
        {
            (VersionManagerStateLoadIssue.Unavailable, _, _) or
            (_, ManagedInstallationRootStatus.Unavailable, _) or
            (_, _, ManagedSetupRecoveryFactKind.Unavailable) =>
                ManagedSetupRecoveryExecutionOutcome.StateUnavailable,
            (_, ManagedInstallationRootStatus.PermissionDenied, _) or
            (_, _, ManagedSetupRecoveryFactKind.AccessDenied) =>
                ManagedSetupRecoveryExecutionOutcome.PermissionDenied,
            (_, _, ManagedSetupRecoveryFactKind.Changed) =>
                ManagedSetupRecoveryExecutionOutcome.RecoveryRequired,
            (VersionManagerStateLoadIssue.Invalid or
                VersionManagerStateLoadIssue.ManagedRootMismatch, _, _) or
            (_, ManagedInstallationRootStatus.InvalidDestination, _) or
            (_, _, ManagedSetupRecoveryFactKind.Malformed or
                ManagedSetupRecoveryFactKind.IdentityMismatch) =>
                ManagedSetupRecoveryExecutionOutcome.ManualInterventionRequired,
            (_, ManagedInstallationRootStatus.Residue, ManagedSetupRecoveryFactKind.Exact) => null,
            _ => ManagedSetupRecoveryExecutionOutcome.RecoveryRequired,
        };
    }

    private static ManagedSetupRecoveryExecutionOutcome? ClassifyExecutionEvidenceIssue(
        ManagedSetupRecoveryEvidenceObservation evidence)
    {
        return evidence.Issue switch
        {
            ManagedSetupRecoveryEvidenceIssue.None when
                evidence.LauncherState?.Issue == LauncherBootstrapStateLoadIssue.Unavailable =>
                    ManagedSetupRecoveryExecutionOutcome.StateUnavailable,
            ManagedSetupRecoveryEvidenceIssue.None when
                evidence.LauncherState?.Issue == LauncherBootstrapStateLoadIssue.Invalid =>
                    ManagedSetupRecoveryExecutionOutcome.ManualInterventionRequired,
            ManagedSetupRecoveryEvidenceIssue.None => null,
            ManagedSetupRecoveryEvidenceIssue.StateUnavailable =>
                ManagedSetupRecoveryExecutionOutcome.StateUnavailable,
            ManagedSetupRecoveryEvidenceIssue.PermissionDenied =>
                ManagedSetupRecoveryExecutionOutcome.PermissionDenied,
            ManagedSetupRecoveryEvidenceIssue.SourceChanged =>
                ManagedSetupRecoveryExecutionOutcome.SourceChanged,
            ManagedSetupRecoveryEvidenceIssue.Invalid =>
                ManagedSetupRecoveryExecutionOutcome.ManualInterventionRequired,
            _ => throw new InvalidOperationException(
                "Recovery evidence returned an undefined issue."),
        };
    }

    private static ManagedSetupRecoveryExecutionOutcome Map(
        ManagedSetupRecoveryExecutionPortOutcome outcome)
    {
        return outcome switch
        {
            ManagedSetupRecoveryExecutionPortOutcome.Completed =>
                ManagedSetupRecoveryExecutionOutcome.Completed,
            ManagedSetupRecoveryExecutionPortOutcome.HealthUnavailable =>
                ManagedSetupRecoveryExecutionOutcome.HealthUnavailable,
            ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable =>
                ManagedSetupRecoveryExecutionOutcome.StateUnavailable,
            ManagedSetupRecoveryExecutionPortOutcome.PermissionDenied =>
                ManagedSetupRecoveryExecutionOutcome.PermissionDenied,
            ManagedSetupRecoveryExecutionPortOutcome.SourceChanged =>
                ManagedSetupRecoveryExecutionOutcome.SourceChanged,
            ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired =>
                ManagedSetupRecoveryExecutionOutcome.RecoveryRequired,
            ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired =>
                ManagedSetupRecoveryExecutionOutcome.ManualInterventionRequired,
            ManagedSetupRecoveryExecutionPortOutcome.Cancelled =>
                ManagedSetupRecoveryExecutionOutcome.Cancelled,
            _ => throw new InvalidOperationException(
                "Recovery executor returned an undefined outcome."),
        };
    }

    private static ManagedSetupRecoveryExecutionResult Result(
        ManagedSetupRecoveryExecutionOutcome outcome)
    {
        return new(outcome);
    }
}

