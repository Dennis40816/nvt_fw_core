// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Single Application owner for normalized managed-root state identity.</summary>
internal static class ManagedRootPathIdentity
{
    internal static string Normalize(string managedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(managedRoot));
    }

    internal static bool Equals(string? storedIdentity, string currentManagedRoot)
    {
        return storedIdentity is not null &&
               Comparer.Equals(storedIdentity, Normalize(currentManagedRoot));
    }

    private static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

/// <summary>Durable phase of one launcher-supervised activation transaction.</summary>
public enum VersionActivationPhase
{
    /// <summary>The desktop persisted the requested candidate before launcher handoff.</summary>
    Requested,
    /// <summary>The launcher durably recorded that candidate launch may have begun.</summary>
    CandidateLaunchRecorded,
    /// <summary>The launcher durably recorded fallback selection before starting it.</summary>
    RollbackLaunchRecorded,
    /// <summary>The launcher durably recorded one ordinary active-version launch attempt.</summary>
    ActiveLaunchRecorded,
}

/// <summary>Recoverable journal for one not-yet-ready activation.</summary>
public sealed record PendingVersionActivation(
    ManagedAppVersion CandidateVersion,
    string CandidateAdmissionIdentity,
    ManagedAppVersion? PreviousActiveVersion,
    ManagedAppVersion? PreviousLastKnownGoodVersion,
    VersionActivationPhase Phase = VersionActivationPhase.Requested);

/// <summary>Filesystem/state mutation represented by the durable state journal.</summary>
public enum ManagedVersionMutationKind
{
    /// <summary>A package promotion must converge with its admission.</summary>
    Install,
    /// <summary>An admitted directory deletion must converge with admission removal.</summary>
    Delete,
}

/// <summary>Recoverable journal written before an install promotion or admitted delete.</summary>
public sealed record PendingManagedVersionMutation(
    ManagedVersionMutationKind Kind,
    ManagedVersionAdmission Admission);

/// <summary>Immutable launcher-owned managed-version state.</summary>
public sealed class VersionManagerState
{
    private VersionManagerState(
        string? managedRootIdentity,
        string? updateSource,
        ManagedAppVersion? activeVersion,
        ManagedAppVersion? lastKnownGoodVersion,
        IReadOnlyList<ManagedVersionAdmission> admissions,
        PendingVersionActivation? pendingActivation,
        ManagedAppVersion? failedActivationVersion,
        bool retentionReviewDue,
        PendingManagedVersionMutation? pendingMutation,
        VersionSourceRegistryState? sourceRegistryState)
    {
        ManagedRootIdentity = managedRootIdentity;
        UpdateSource = updateSource;
        ActiveVersion = activeVersion;
        LastKnownGoodVersion = lastKnownGoodVersion;
        Admissions = admissions;
        PendingActivation = pendingActivation;
        FailedActivationVersion = failedActivationVersion;
        RetentionReviewDue = retentionReviewDue;
        PendingMutation = pendingMutation;
        SourceRegistryState = sourceRegistryState;
    }

    /// <summary>Gets the normalized managed root that exclusively owns this durable state.</summary>
    public string? ManagedRootIdentity { get; }

    /// <summary>Gets the committed configured update-source folder.</summary>
    public string? UpdateSource { get; }

    /// <summary>Gets the committed active version.</summary>
    public ManagedAppVersion? ActiveVersion { get; }

    /// <summary>Gets the most recent ready version eligible for rollback.</summary>
    public ManagedAppVersion? LastKnownGoodVersion { get; }

    /// <summary>Gets content admissions for installed managed versions.</summary>
    public IReadOnlyList<ManagedVersionAdmission> Admissions { get; }

    /// <summary>Gets the recoverable not-yet-ready activation journal.</summary>
    public PendingVersionActivation? PendingActivation { get; }

    /// <summary>Gets the candidate that most recently failed readiness.</summary>
    public ManagedAppVersion? FailedActivationVersion { get; }

    /// <summary>Gets whether post-update retention review remains due.</summary>
    public bool RetentionReviewDue { get; }

    /// <summary>Gets the durable install/delete transaction that must converge before another mutation.</summary>
    public PendingManagedVersionMutation? PendingMutation { get; }

    /// <summary>Gets durable fixed-registry anti-rollback and manual-pin state.</summary>
    public VersionSourceRegistryState? SourceRegistryState { get; }

    /// <summary>Creates validated launcher state without inferring missing identities.</summary>
    /// <param name="updateSource">Committed source configuration.</param>
    /// <param name="activeVersion">Committed active version.</param>
    /// <param name="lastKnownGoodVersion">Committed fallback version.</param>
    /// <param name="admissions">Installed content admissions.</param>
    /// <param name="pendingActivation">Optional activation journal.</param>
    /// <param name="failedActivationVersion">Optional failed candidate.</param>
    /// <param name="retentionReviewDue">Whether retention review is due.</param>
    /// <param name="pendingMutation">Optional durable install/delete transaction.</param>
    /// <param name="managedRootIdentity">Normalized managed-root ownership, or null only for an unbound seed template.</param>
    /// <param name="sourceRegistryState">Optional durable fixed-registry authority.</param>
    /// <returns>Validated immutable state.</returns>
    public static VersionManagerState Create(
        string? updateSource,
        ManagedAppVersion? activeVersion,
        ManagedAppVersion? lastKnownGoodVersion,
        IEnumerable<ManagedVersionAdmission> admissions,
        PendingVersionActivation? pendingActivation,
        ManagedAppVersion? failedActivationVersion,
        bool retentionReviewDue,
        PendingManagedVersionMutation? pendingMutation = null,
        string? managedRootIdentity = null,
        VersionSourceRegistryState? sourceRegistryState = null)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        string? normalizedUpdateSource = string.IsNullOrWhiteSpace(updateSource)
            ? null
            : sourceRegistryState is null
                ? updateSource
                : NormalizeRegistrySource(updateSource, requireAlreadyNormalized: true);
        if (sourceRegistryState is not null && normalizedUpdateSource is null)
        {
            throw new ArgumentException(
                "Registry state requires one normalized effective update source.",
                nameof(updateSource));
        }
        ManagedVersionAdmission[] installed = [.. admissions];
        if (installed.GroupBy(admission => admission.Version).Any(group => group.Count() != 1) ||
            installed.Any(admission =>
                string.IsNullOrWhiteSpace(admission.AdmissionIdentity) ||
                !IsLowerSha256(admission.ReleaseManifestSha256)))
        {
            throw new ArgumentException("Installed admissions are inconsistent.", nameof(admissions));
        }

        HashSet<ManagedAppVersion> versions = [.. installed.Select(admission => admission.Version)];
        if ((activeVersion is not null && !versions.Contains(activeVersion.Value)) ||
            (lastKnownGoodVersion is not null && !versions.Contains(lastKnownGoodVersion.Value)) ||
            (failedActivationVersion is not null && !versions.Contains(failedActivationVersion.Value)) ||
            (pendingActivation is not null && !versions.Contains(pendingActivation.CandidateVersion)))
        {
            throw new ArgumentException("State references a version without an installed admission.");
        }

        if (pendingActivation is not null)
        {
            ManagedVersionAdmission candidate = installed.Single(
                admission => admission.Version == pendingActivation.CandidateVersion);
            if (!Enum.IsDefined(pendingActivation.Phase) ||
                (pendingActivation.PreviousActiveVersion is { } previousActive &&
                 !versions.Contains(previousActive)) ||
                (pendingActivation.PreviousLastKnownGoodVersion is { } previousLastKnownGood &&
                 !versions.Contains(previousLastKnownGood)) ||
                (pendingActivation.Phase == VersionActivationPhase.ActiveLaunchRecorded &&
                 (pendingActivation.CandidateVersion != activeVersion ||
                  pendingActivation.PreviousActiveVersion != activeVersion ||
                  pendingActivation.PreviousLastKnownGoodVersion != lastKnownGoodVersion)) ||
                !string.Equals(
                    candidate.AdmissionIdentity,
                    pendingActivation.CandidateAdmissionIdentity,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("Pending activation identity differs from installed admission.");
            }
        }
        if (pendingMutation is { } mutation)
        {
            if (!Enum.IsDefined(mutation.Kind) ||
                pendingActivation is not null ||
                string.IsNullOrWhiteSpace(mutation.Admission.AdmissionIdentity) ||
                !IsLowerSha256(mutation.Admission.ReleaseManifestSha256) ||
                (mutation.Kind == ManagedVersionMutationKind.Delete &&
                 !installed.Contains(mutation.Admission)) ||
                (mutation.Kind == ManagedVersionMutationKind.Install &&
                 versions.Contains(mutation.Admission.Version)))
            {
                throw new ArgumentException("Pending managed-version mutation is inconsistent.", nameof(pendingMutation));
            }
        }

        Array.Sort(installed, static (left, right) => right.Version.CompareTo(left.Version));
        return new(
            managedRootIdentity is null ? null : ManagedRootPathIdentity.Normalize(managedRootIdentity),
            normalizedUpdateSource,
            activeVersion,
            lastKnownGoodVersion,
            installed,
            pendingActivation,
            failedActivationVersion,
            retentionReviewDue,
            pendingMutation,
            sourceRegistryState);
    }

    /// <summary>Creates the first durable root binding after its packaged seed payload was verified.</summary>
    internal VersionManagerState BindToManagedRoot(string managedRoot)
    {
        _ = ManagedRootIdentity is null
            ? true
            : throw new InvalidOperationException("Managed-version state is already bound to a managed root.");
        return Create(
            UpdateSource,
            ActiveVersion,
            LastKnownGoodVersion,
            Admissions,
            PendingActivation,
            FailedActivationVersion,
            RetentionReviewDue,
            PendingMutation,
            managedRoot,
            SourceRegistryState);
    }

    /// <summary>Checks that this durable state belongs to the exact current managed root.</summary>
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
        private readonly VersionManagerState _state;

        internal DurableSnapshotToken(VersionManagerState state)
        {
            _state = state;
        }

        internal bool Matches(DurableSnapshotToken other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return _state.HasSameDurableSnapshot(other._state);
        }
    }

    private bool HasSameDurableSnapshot(VersionManagerState other)
    {
        return string.Equals(ManagedRootIdentity, other.ManagedRootIdentity, StringComparison.Ordinal) &&
               string.Equals(UpdateSource, other.UpdateSource, StringComparison.Ordinal) &&
               ActiveVersion == other.ActiveVersion &&
               LastKnownGoodVersion == other.LastKnownGoodVersion &&
               Admissions.SequenceEqual(other.Admissions) &&
               PendingActivation == other.PendingActivation &&
               FailedActivationVersion == other.FailedActivationVersion &&
               RetentionReviewDue == other.RetentionReviewDue &&
               PendingMutation == other.PendingMutation &&
               SourceRegistryState == other.SourceRegistryState;
    }

    private static bool IsLowerSha256(string? value)
    {
        return value is { Length: 64 } &&
               value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }

    internal static string NormalizeRegistrySource(
        string updateSource,
        bool requireAlreadyNormalized)
    {
        if (!Path.IsPathFullyQualified(updateSource))
        {
            throw new ArgumentException(
                "Registry-managed update source must be fully qualified.",
                nameof(updateSource));
        }
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(updateSource));
        return !requireAlreadyNormalized || PathComparer.Equals(normalized, updateSource)
            ? normalized
            : throw new ArgumentException(
                "Registry-managed update source must already be normalized.",
                nameof(updateSource));
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal VersionManagerState Rebuild(
        ManagedAppVersion? activeVersion,
        ManagedAppVersion? lastKnownGoodVersion,
        PendingVersionActivation? pendingActivation,
        ManagedAppVersion? failedActivationVersion)
    {
        return Create(
            UpdateSource,
            activeVersion,
            lastKnownGoodVersion,
            Admissions,
            pendingActivation,
            failedActivationVersion,
            RetentionReviewDue,
            PendingMutation,
            ManagedRootIdentity,
            SourceRegistryState);
    }

    internal VersionManagerState WithRetentionReviewDue(bool retentionReviewDue)
    {
        return Create(
            UpdateSource,
            ActiveVersion,
            LastKnownGoodVersion,
            Admissions,
            PendingActivation,
            FailedActivationVersion,
            retentionReviewDue,
            PendingMutation,
            ManagedRootIdentity,
            SourceRegistryState);
    }

    internal VersionManagerState WithPendingMutation(PendingManagedVersionMutation? pendingMutation)
    {
        return Create(
            UpdateSource,
            ActiveVersion,
            LastKnownGoodVersion,
            Admissions,
            PendingActivation,
            FailedActivationVersion,
            RetentionReviewDue,
            pendingMutation,
            ManagedRootIdentity,
            SourceRegistryState);
    }

    internal VersionManagerState CompletePendingMutation(
        IEnumerable<ManagedVersionAdmission> admissions,
        ManagedAppVersion? lastKnownGoodVersion,
        ManagedAppVersion? failedActivationVersion)
    {
        _ = PendingMutation is not null
            ? true
            : throw new InvalidOperationException("No managed-version mutation is pending.");
        return Create(
            UpdateSource,
            ActiveVersion,
            lastKnownGoodVersion,
            admissions,
            PendingActivation,
            failedActivationVersion,
            RetentionReviewDue,
            pendingMutation: null,
            ManagedRootIdentity,
            SourceRegistryState);
    }

    internal VersionManagerState WithUpdateSource(
        string? updateSource,
        VersionSourceRegistryState? sourceRegistryState)
    {
        return Create(
            updateSource,
            ActiveVersion,
            LastKnownGoodVersion,
            Admissions,
            PendingActivation,
            FailedActivationVersion,
            RetentionReviewDue,
            PendingMutation,
            ManagedRootIdentity,
            sourceRegistryState);
    }
}

/// <summary>State and optional exact fallback selected after activation failure.</summary>
public sealed record ActivationRecoveryDecision(
    VersionManagerState State,
    ManagedAppVersion? RollbackVersion);

/// <summary>Pure Application owner for ready commit and bounded rollback transitions.</summary>
public static class VersionActivationPolicy
{
    /// <summary>Records one ordinary active-version launch before crossing the process boundary.</summary>
    public static VersionManagerState RecordActiveLaunch(VersionManagerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.PendingActivation is not null || state.PendingMutation is not null)
        {
            throw new InvalidOperationException("Another managed-version transaction is already pending.");
        }
        ManagedVersionAdmission active = state.ActiveVersion is { } activeVersion
            ? state.Admissions.Single(admission => admission.Version == activeVersion)
            : throw new InvalidOperationException("No admitted active version is available.");
        return state.Rebuild(
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            new(
                active.Version,
                active.AdmissionIdentity,
                state.ActiveVersion,
                state.LastKnownGoodVersion,
                VersionActivationPhase.ActiveLaunchRecorded),
            state.FailedActivationVersion);
    }

    /// <summary>Clears an ordinary launch guard only after that exact attempt has a confirmed outcome.</summary>
    public static VersionManagerState ClearActiveLaunch(
        VersionManagerState state,
        ManagedAppVersion version)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = state.PendingActivation is
        { CandidateVersion: var active, Phase: VersionActivationPhase.ActiveLaunchRecorded } &&
            active == version && state.ActiveVersion == version
                ? true
                : throw new InvalidOperationException("Active launch guard does not match the confirmed process.");
        return state.Rebuild(
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            pendingActivation: null,
            state.FailedActivationVersion);
    }

    /// <summary>Begins one recoverable activation for an admitted installed version.</summary>
    /// <param name="state">Current launcher state.</param>
    /// <param name="candidateVersion">Exact admitted candidate.</param>
    /// <returns>State with a pending activation journal.</returns>
    public static VersionManagerState BeginActivation(
        VersionManagerState state,
        ManagedAppVersion candidateVersion)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.PendingActivation is not null)
        {
            throw new InvalidOperationException("Another managed-version activation is already pending.");
        }
        if (state.PendingMutation is not null)
        {
            throw new InvalidOperationException("A managed-version filesystem mutation is still pending.");
        }
        ManagedVersionAdmission candidate = state.Admissions.SingleOrDefault(
            admission => admission.Version == candidateVersion) ??
            throw new InvalidOperationException("Activation candidate is not installed and admitted.");
        return state.Rebuild(
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            new(
                candidate.Version,
                candidate.AdmissionIdentity,
                state.ActiveVersion,
                state.LastKnownGoodVersion,
                VersionActivationPhase.Requested),
            failedActivationVersion: null);
    }

    /// <summary>Records the candidate-launch seam before any process can be started.</summary>
    public static VersionManagerState RecordCandidateLaunch(VersionManagerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        PendingVersionActivation pending = state.PendingActivation is { Phase: VersionActivationPhase.Requested } value
            ? value
            : throw new InvalidOperationException("Candidate launch is not in the requested phase.");
        return state.Rebuild(
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            pending with { Phase = VersionActivationPhase.CandidateLaunchRecorded },
            state.FailedActivationVersion);
    }

    /// <summary>Cancels a requested activation when launcher handoff never started.</summary>
    public static VersionManagerState CancelRequestedActivation(VersionManagerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = state.PendingActivation is { Phase: VersionActivationPhase.Requested }
            ? true
            : throw new InvalidOperationException("Only an unlaunched activation request can be cancelled.");
        return state.Rebuild(
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            pendingActivation: null,
            state.FailedActivationVersion);
    }

    /// <summary>Commits a pending candidate only after its authenticated ready signal.</summary>
    /// <param name="state">State with a pending activation.</param>
    /// <param name="readyVersion">Version that reported ready.</param>
    /// <returns>Committed state.</returns>
    public static VersionManagerState CommitReady(
        VersionManagerState state,
        ManagedAppVersion readyVersion)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = state.PendingActivation is
        { CandidateVersion: var candidate, Phase: VersionActivationPhase.CandidateLaunchRecorded } &&
            candidate == readyVersion
                ? true
                : throw new InvalidOperationException(
                    "Ready signal does not match a durably recorded candidate launch.");
        return state.Rebuild(
            readyVersion,
            readyVersion,
            pendingActivation: null,
            failedActivationVersion: null);
    }

    /// <summary>Fails one matching pending activation and selects its prior LKG at most once.</summary>
    /// <param name="state">Current launcher state.</param>
    /// <param name="failedVersion">Candidate that failed start, exit, integrity, or deadline.</param>
    /// <returns>Restored state and the exact fallback to launch once.</returns>
    public static ActivationRecoveryDecision FailActivation(
        VersionManagerState state,
        ManagedAppVersion failedVersion)
    {
        ArgumentNullException.ThrowIfNull(state);
        PendingVersionActivation? pending = state.PendingActivation;
        if (pending?.CandidateVersion != failedVersion)
        {
            return new(state, RollbackVersion: null);
        }

        ManagedAppVersion? rollback = pending.PreviousLastKnownGoodVersion;
        bool rollbackAdmitted = rollback is not null &&
                                state.Admissions.Any(admission => admission.Version == rollback.Value) &&
                                rollback.Value != failedVersion;
        ManagedAppVersion? restoredActive = rollbackAdmitted
            ? rollback
            : pending.PreviousActiveVersion;
        VersionManagerState restored = state.Rebuild(
            restoredActive,
            rollbackAdmitted ? rollback : state.LastKnownGoodVersion,
            pendingActivation: null,
            failedActivationVersion: failedVersion);
        return new(restored, rollbackAdmitted ? rollback : null);
    }

    /// <summary>Records rollback selection before the fallback process starts.</summary>
    public static ActivationRecoveryDecision RecordRollbackLaunch(
        VersionManagerState state,
        ManagedAppVersion failedVersion)
    {
        ArgumentNullException.ThrowIfNull(state);
        PendingVersionActivation? pending = state.PendingActivation;
        if (pending?.CandidateVersion != failedVersion ||
            pending.Phase == VersionActivationPhase.RollbackLaunchRecorded)
        {
            return new(state, RollbackVersion: null);
        }

        ManagedAppVersion? rollback = pending.PreviousLastKnownGoodVersion;
        bool rollbackAdmitted = rollback is not null &&
                                state.Admissions.Any(admission => admission.Version == rollback.Value) &&
                                rollback.Value != failedVersion;
        VersionManagerState recorded = state.Rebuild(
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            pending with { Phase = VersionActivationPhase.RollbackLaunchRecorded },
            failedActivationVersion: failedVersion);
        return new(recorded, rollbackAdmitted ? rollback : null);
    }

    /// <summary>Commits a ready fallback and closes the activation journal.</summary>
    public static VersionManagerState CommitRollback(
        VersionManagerState state,
        ManagedAppVersion rollbackVersion)
    {
        ArgumentNullException.ThrowIfNull(state);
        PendingVersionActivation pending = state.PendingActivation is
        { Phase: VersionActivationPhase.RollbackLaunchRecorded } value
                ? value
                : throw new InvalidOperationException("Rollback is not durably recorded.");
        _ = pending.PreviousLastKnownGoodVersion == rollbackVersion
            ? true
            : throw new InvalidOperationException("Ready fallback differs from the recorded rollback target.");
        return state.Rebuild(
            rollbackVersion,
            rollbackVersion,
            pendingActivation: null,
            failedActivationVersion: pending.CandidateVersion);
    }
}
