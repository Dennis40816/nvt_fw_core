// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Durable phase of one release-coupled launcher transaction.</summary>
public enum LauncherActivationPhase
{
    /// <summary>The candidate was requested before handoff.</summary>
    Requested,
    /// <summary>Candidate launch may have begun.</summary>
    CandidateLaunchRecorded,
    /// <summary>Fallback selection was recorded before launch.</summary>
    RollbackLaunchRecorded,
    /// <summary>An ordinary active launcher attempt was recorded.</summary>
    ActiveLaunchRecorded,
}

/// <summary>Exact candidate and prior launcher authority for one recoverable transaction.</summary>
public sealed record PendingLauncherActivation
{
    private PendingLauncherActivation(
        ManagedLauncherIdentity candidate,
        ManagedLauncherIdentity? previousActive,
        ManagedLauncherIdentity? previousLastKnownGood,
        LauncherActivationPhase phase)
    {
        Candidate = candidate;
        PreviousActive = previousActive;
        PreviousLastKnownGood = previousLastKnownGood;
        Phase = phase;
    }

    /// <summary>Gets the exact candidate identity.</summary>
    public ManagedLauncherIdentity Candidate { get; }
    /// <summary>Gets the exact prior active identity.</summary>
    public ManagedLauncherIdentity? PreviousActive { get; }
    /// <summary>Gets the exact prior fallback identity.</summary>
    public ManagedLauncherIdentity? PreviousLastKnownGood { get; }
    /// <summary>Gets the recorded phase.</summary>
    public LauncherActivationPhase Phase { get; }

    /// <summary>Creates a journal with a present candidate and a defined phase.</summary>
    public static PendingLauncherActivation Create(
        ManagedLauncherIdentity candidate,
        ManagedLauncherIdentity? previousActive,
        ManagedLauncherIdentity? previousLastKnownGood,
        LauncherActivationPhase phase)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return !Enum.IsDefined(phase)
            ? throw new ArgumentOutOfRangeException(nameof(phase))
            : new(candidate, previousActive, previousLastKnownGood, phase);
    }

    internal PendingLauncherActivation WithPhase(LauncherActivationPhase phase)
    {
        return Create(Candidate, PreviousActive, PreviousLastKnownGood, phase);
    }
}

/// <summary>Strict durable launcher activation snapshot protected by the app-state lease.</summary>
public sealed class LauncherBootstrapState
{
    private LauncherBootstrapState(
        string managedRootIdentity,
        ManagedLauncherIdentity? active,
        ManagedLauncherIdentity? lastKnownGood,
        PendingLauncherActivation? pending,
        ManagedLauncherIdentity? failed)
    {
        ManagedRootIdentity = managedRootIdentity;
        Active = active;
        LastKnownGood = lastKnownGood;
        Pending = pending;
        Failed = failed;
    }

    /// <summary>Gets the normalized owning managed root.</summary>
    public string ManagedRootIdentity { get; }
    /// <summary>Gets the exact active launcher.</summary>
    public ManagedLauncherIdentity? Active { get; }
    /// <summary>Gets the exact recorded fallback launcher.</summary>
    public ManagedLauncherIdentity? LastKnownGood { get; }
    /// <summary>Gets the recoverable transaction.</summary>
    public PendingLauncherActivation? Pending { get; }
    /// <summary>Gets the most recently failed candidate.</summary>
    public ManagedLauncherIdentity? Failed { get; }

    /// <summary>Normalizes the root and validates the active pair and exact prior transaction snapshot.</summary>
    public static LauncherBootstrapState Create(
        string managedRootIdentity,
        ManagedLauncherIdentity? active,
        ManagedLauncherIdentity? lastKnownGood,
        PendingLauncherActivation? pending,
        ManagedLauncherIdentity? failed)
    {
        string root = ManagedRootPathIdentity.Normalize(managedRootIdentity);
        return (active is null) != (lastKnownGood is null)
            ? throw new ArgumentException("Launcher active and last-known-good must be absent together.")
            : pending is not null &&
            (pending.PreviousActive != active || pending.PreviousLastKnownGood != lastKnownGood)
            ? throw new ArgumentException("Launcher pending transaction does not preserve exact prior state.", nameof(pending))
            : pending is { Phase: LauncherActivationPhase.ActiveLaunchRecorded } &&
              pending.Candidate != active
            ? throw new ArgumentException("Active launcher guard does not match current active state.", nameof(pending))
            : new(root, active, lastKnownGood, pending, failed);
    }

    internal bool IsBoundToManagedRoot(string managedRoot)
    {
        return ManagedRootPathIdentity.Equals(ManagedRootIdentity, managedRoot);
    }

    /// <summary>Captures all durable fields for exact generation comparison by read-only observers.</summary>
    internal DurableSnapshotToken CreateDurableSnapshotToken()
    {
        return new(this);
    }

    internal sealed class DurableSnapshotToken
    {
        private readonly LauncherBootstrapState _state;

        internal DurableSnapshotToken(LauncherBootstrapState state)
        {
            _state = state;
        }

        internal bool Matches(DurableSnapshotToken other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return _state.HasSameDurableSnapshot(other._state);
        }
    }

    private bool HasSameDurableSnapshot(LauncherBootstrapState other)
    {
        return string.Equals(ManagedRootIdentity, other.ManagedRootIdentity, StringComparison.Ordinal) &&
               Active == other.Active &&
               LastKnownGood == other.LastKnownGood &&
               Pending == other.Pending &&
               Failed == other.Failed;
    }

    internal LauncherBootstrapState Begin(ManagedLauncherIdentity candidate)
    {
        return Pending is not null || candidate == Active
            ? throw new InvalidOperationException("Launcher candidate cannot begin from current state.")
            : Create(
            ManagedRootIdentity,
            Active,
            LastKnownGood,
            PendingLauncherActivation.Create(
                candidate,
                Active,
                LastKnownGood,
                LauncherActivationPhase.Requested),
            Failed);
    }

    internal LauncherBootstrapState RecordActiveLaunch()
    {
        ManagedLauncherIdentity active = Pending is null && Active is { } value
            ? value
            : throw new InvalidOperationException("Active launcher attempt cannot begin from current state.");
        return Create(
            ManagedRootIdentity,
            Active,
            LastKnownGood,
            PendingLauncherActivation.Create(
                active,
                Active,
                LastKnownGood,
                LauncherActivationPhase.ActiveLaunchRecorded),
            Failed);
    }

    internal LauncherBootstrapState ClearActiveLaunch(ManagedLauncherIdentity launcher)
    {
        _ = Pending is
        { Candidate: var active, Phase: LauncherActivationPhase.ActiveLaunchRecorded } &&
            active == launcher && Active == launcher
                ? true
                : throw new InvalidOperationException("Active launcher guard does not match confirmed process.");
        return Create(ManagedRootIdentity, Active, LastKnownGood, pending: null, Failed);
    }

    internal LauncherBootstrapState RecordCandidateLaunch()
    {
        PendingLauncherActivation pending = Pending is { Phase: LauncherActivationPhase.Requested } value
            ? value
            : throw new InvalidOperationException("Launcher candidate is not requested.");
        return Create(
            ManagedRootIdentity,
            Active,
            LastKnownGood,
            pending.WithPhase(LauncherActivationPhase.CandidateLaunchRecorded),
            Failed);
    }

    internal LauncherBootstrapState RecordRollbackLaunch()
    {
        PendingLauncherActivation pending = Pending is
        { Phase: LauncherActivationPhase.CandidateLaunchRecorded } value
                ? value
                : throw new InvalidOperationException("Launcher candidate launch is not recorded.");
        return Create(
            ManagedRootIdentity,
            Active,
            LastKnownGood,
            pending.WithPhase(LauncherActivationPhase.RollbackLaunchRecorded),
            Failed);
    }

    internal LauncherBootstrapState CommitReady()
    {
        ManagedLauncherIdentity candidate = Pending is
        { Phase: LauncherActivationPhase.CandidateLaunchRecorded } value
                ? value.Candidate
                : throw new InvalidOperationException("Launcher candidate readiness is not committable.");
        return Create(ManagedRootIdentity, candidate, candidate, pending: null, failed: null);
    }

    internal LauncherBootstrapState CommitRollback()
    {
        PendingLauncherActivation pending = Pending is
        { Phase: LauncherActivationPhase.RollbackLaunchRecorded } value
                ? value
                : throw new InvalidOperationException("Launcher rollback is not committable.");
        ManagedLauncherIdentity rollback = pending.PreviousLastKnownGood ??
            throw new InvalidOperationException("Launcher rollback target is absent.");
        return Create(ManagedRootIdentity, rollback, rollback, pending: null, pending.Candidate);
    }

    internal LauncherBootstrapState FailCandidate()
    {
        ManagedLauncherIdentity candidate = Pending?.Candidate ??
            throw new InvalidOperationException("Launcher candidate is absent.");
        return Create(ManagedRootIdentity, Active, LastKnownGood, pending: null, candidate);
    }
}

/// <summary>Stable launcher-state read category.</summary>
public enum LauncherBootstrapStateLoadIssue
{
    /// <summary>State loaded successfully.</summary>
    None,
    /// <summary>No state exists.</summary>
    Missing,
    /// <summary>The state is malformed or inconsistent.</summary>
    Invalid,
    /// <summary>The state could not be read.</summary>
    Unavailable,
}
/// <summary>Fail-closed launcher-state read result.</summary>
public sealed record LauncherBootstrapStateLoadResult(
    LauncherBootstrapState? State,
    LauncherBootstrapStateLoadIssue Issue)
{
    /// <summary>Gets whether a complete validated snapshot is present.</summary>
    public bool IsSuccess => State is not null && Issue == LauncherBootstrapStateLoadIssue.None;
}
/// <summary>Stable launcher-state write category.</summary>
public enum LauncherBootstrapStateSaveIssue
{
    /// <summary>The state committed successfully.</summary>
    None,
    /// <summary>The state could not be committed.</summary>
    Unavailable,
}
/// <summary>Typed launcher-state write result.</summary>
public readonly record struct LauncherBootstrapStateSaveResult(LauncherBootstrapStateSaveIssue Issue)
{
    /// <summary>Gets whether the state committed.</summary>
    public bool IsSuccess => Issue == LauncherBootstrapStateSaveIssue.None;
}

/// <summary>Persistence port that intentionally exposes no second writer lease.</summary>
public interface ILauncherBootstrapStateStore
{
    /// <summary>Loads one complete validated launcher-state snapshot.</summary>
    ValueTask<LauncherBootstrapStateLoadResult> LoadAsync(CancellationToken cancellationToken);
    /// <summary>Tries to commit state under the caller's application-state writer custody.</summary>
    ValueTask<LauncherBootstrapStateSaveResult> TrySaveAsync(
        LauncherBootstrapState state,
        CancellationToken cancellationToken);
}
