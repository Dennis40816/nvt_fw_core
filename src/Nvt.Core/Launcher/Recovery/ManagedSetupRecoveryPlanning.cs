// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>The only actions selected by the managed Setup recovery policy.</summary>
/// <remarks>The zero value keeps the committed installation and durable state, so <c>default(ManagedSetupRecoveryAction)</c> never selects the removal of an incomplete installation.</remarks>
public enum ManagedSetupRecoveryAction
{
    /// <summary>Preserve the committed installation and remove exact Setup residue.</summary>
    ConvergeReady,
    /// <summary>Delete only the exact incomplete transaction state and residue.</summary>
    RemoveIncompleteInstallation,
}

/// <summary>Stable issue from exact candidate, inventory, and launcher-state observation.</summary>
public enum ManagedSetupRecoveryEvidenceIssue
{
    /// <summary>All evidence and its deterministic restart-prefix proof are exact.</summary>
    None,
    /// <summary>State or inventory evidence could not be observed completely.</summary>
    StateUnavailable,
    /// <summary>The exact evidence could not be opened by the current user.</summary>
    PermissionDenied,
    /// <summary>Candidate or inventory authority changed since the marker was written.</summary>
    SourceChanged,
    /// <summary>Evidence is malformed, foreign, unsafe, or lacks complete prefix proof.</summary>
    Invalid,
}

/// <summary>
/// Opaque, immutable exact-object and deterministic-prefix evidence supplied by Infrastructure.
/// Every concrete token must be sealed, deeply immutable, and value-equal across equivalent
/// observations; reference identity, mutable collections, and replayable lease claims are forbidden.
/// </summary>
internal abstract record ManagedSetupRecoveryExecutionToken;

/// <summary>
/// Action-safe observation whose raw Application and Launcher snapshots remain internal.
/// </summary>
public sealed class ManagedSetupRecoveryEvidenceObservation
{
    /// <summary>Creates one incomplete observation without destructive authority.</summary>
    public ManagedSetupRecoveryEvidenceObservation(ManagedSetupRecoveryEvidenceIssue issue)
    {
        if (issue == ManagedSetupRecoveryEvidenceIssue.None)
        {
            throw new ArgumentException(
                "Exact recovery evidence requires all typed snapshots and its opaque token.",
                nameof(issue));
        }
        Issue = issue;
    }

    /// <summary>Creates one exact observation for Application-owned policy classification.</summary>
    internal ManagedSetupRecoveryEvidenceObservation(
        ManagedVersionAdmission admission,
        ManagedLauncherIdentity? installedLauncher,
        LauncherBootstrapStateLoadResult launcherState,
        ManagedSetupRecoveryExecutionToken executionToken)
    {
        Admission = admission ?? throw new ArgumentNullException(nameof(admission));
        InstalledLauncher = installedLauncher;
        LauncherState = launcherState ?? throw new ArgumentNullException(nameof(launcherState));
        ExecutionToken = executionToken ?? throw new ArgumentNullException(nameof(executionToken));
        Issue = ManagedSetupRecoveryEvidenceIssue.None;
    }

    /// <summary>Gets the action-safe observation category.</summary>
    public ManagedSetupRecoveryEvidenceIssue Issue { get; }

    internal ManagedVersionAdmission? Admission { get; }
    internal ManagedLauncherIdentity? InstalledLauncher { get; }
    internal LauncherBootstrapStateLoadResult? LauncherState { get; }
    internal ManagedSetupRecoveryExecutionToken? ExecutionToken { get; }
}

/// <summary>
/// Exact recovery evidence probe. It verifies mechanics but never classifies a state pair or action.
/// </summary>
public interface IManagedSetupRecoveryEvidenceProbe
{
    /// <summary>Observes exact candidate, root inventory, Launcher state, and restart-prefix evidence.</summary>
    ValueTask<ManagedSetupRecoveryEvidenceObservation> ObserveAsync(
        ManagedSetupRecoveryTransaction transaction,
        CancellationToken cancellationToken);
}

/// <summary>
/// Canonical Application state store used for both recovery reads and the one writer lease.
/// </summary>
public interface IManagedSetupRecoveryStateStore
    : IManagedSetupRecoveryStateReader, IVersionManagerStateStore;

/// <summary>Immutable Application-owned action plan produced only by actionable diagnosis.</summary>
public sealed class ManagedSetupRecoveryPlan
{
    internal ManagedSetupRecoveryPlan(
        string managedRoot,
        ManagedSetupRecoveryTransaction transaction,
        ManagedSetupRecoveryAction action,
        VersionManagerStateLoadResult applicationState,
        ManagedSetupRecoveryEvidenceObservation evidence)
    {
        ManagedRoot = managedRoot;
        Transaction = transaction;
        Action = action;
        ApplicationStateWasMissing = applicationState.Issue == VersionManagerStateLoadIssue.Missing;
        ApplicationStateToken = applicationState.State?.CreateDurableSnapshotToken();
        Evidence = evidence;
    }

    /// <summary>Gets the one action selected from the closed Application policy.</summary>
    public ManagedSetupRecoveryAction Action { get; }

    internal string ManagedRoot { get; }
    internal ManagedSetupRecoveryTransaction Transaction { get; }
    internal bool ApplicationStateWasMissing { get; }
    internal VersionManagerState.DurableSnapshotToken? ApplicationStateToken { get; }
    internal ManagedSetupRecoveryEvidenceObservation Evidence { get; }
}

/// <summary>Single pure owner of recovery lifetime ordering, state-pair policy, and plan matching.</summary>
internal static class ManagedSetupRecoveryPolicy
{
    internal static readonly ManagedProcessLifetimeKind[] _lifetimeKinds =
    [
        ManagedProcessLifetimeKind.Bootstrap,
        ManagedProcessLifetimeKind.Application,
        ManagedProcessLifetimeKind.Launcher,
    ];

    internal static ManagedSetupRecoveryAction? SelectAction(
        VersionManagerStateLoadResult applicationState,
        ManagedSetupRecoveryTransaction transaction,
        ManagedSetupRecoveryEvidenceObservation evidence,
        string managedRoot)
    {
        if (evidence.Issue != ManagedSetupRecoveryEvidenceIssue.None ||
            evidence.Admission is not { } admission ||
            evidence.LauncherState is not { } launcherLoad ||
            evidence.ExecutionToken is null ||
            !CandidateMatches(transaction, admission))
        {
            return null;
        }

        bool appMissing = applicationState.State is null &&
            applicationState.Issue == VersionManagerStateLoadIssue.Missing;
        bool launcherMissing = launcherLoad.State is null &&
            launcherLoad.Issue == LauncherBootstrapStateLoadIssue.Missing;
        if (appMissing && launcherMissing)
        {
            return ManagedSetupRecoveryAction.RemoveIncompleteInstallation;
        }
        if (evidence.InstalledLauncher is not { } launcher ||
            !launcher.MatchesOwner(admission))
        {
            return null;
        }
        if (!applicationState.IsSuccess ||
            transaction.Phase != ManagedSetupRecoveryPhase.BootstrapLaunchRecorded ||
            !ManagedVersionSeedPolicy.IsCanonicalBoundFirstRunState(
                applicationState.State!,
                managedRoot,
                admission) ||
            (!launcherMissing &&
                (!launcherLoad.IsSuccess ||
                    !launcherLoad.State!.IsBoundToManagedRoot(managedRoot))))
        {
            return null;
        }
        if (launcherMissing)
        {
            return ManagedSetupRecoveryAction.RemoveIncompleteInstallation;
        }

        LauncherBootstrapState launcherState = launcherLoad.State!;
        bool preReadyPending = launcherState is
        {
            Active: null,
            LastKnownGood: null,
            Failed: null,
            Pending:
            {
                Candidate: var pendingCandidate,
                PreviousActive: null,
                PreviousLastKnownGood: null,
                Phase: LauncherActivationPhase.Requested or
                    LauncherActivationPhase.CandidateLaunchRecorded,
            },
        } && pendingCandidate == launcher;
        bool failedFirstCandidate = launcherState is
        {
            Active: null,
            LastKnownGood: null,
            Pending: null,
            Failed: var failed,
        } && failed == launcher;
        if (preReadyPending || failedFirstCandidate)
        {
            return ManagedSetupRecoveryAction.RemoveIncompleteInstallation;
        }
        bool ready = launcherState is
        {
            Active: var active,
            LastKnownGood: var lastKnownGood,
            Pending: null,
            Failed: null,
        } && active == launcher && lastKnownGood == launcher;
        return ready ? ManagedSetupRecoveryAction.ConvergeReady : null;
    }

    internal static bool PlanMatches(
        ManagedSetupRecoveryPlan plan,
        VersionManagerStateLoadResult state,
        ManagedSetupRecoveryTransaction transaction,
        ManagedSetupRecoveryEvidenceObservation evidence)
    {
        bool stateMissing = state.State is null && state.Issue == VersionManagerStateLoadIssue.Missing;
        bool stateMatches = plan.ApplicationStateWasMissing
            ? stateMissing
            : state.IsSuccess && plan.ApplicationStateToken is { } expected &&
                expected.Matches(state.State!.CreateDurableSnapshotToken());
        return stateMatches &&
            TransactionMatches(plan.Transaction, transaction) &&
            Equals(plan.Evidence.ExecutionToken, evidence.ExecutionToken);
    }

    private static bool CandidateMatches(
        ManagedSetupRecoveryTransaction transaction,
        ManagedVersionAdmission admission)
    {
        return ManagedAppVersion.TryParse(transaction.Candidate.Version, out ManagedAppVersion version) &&
            admission.Version == version &&
            string.Equals(
                admission.AdmissionIdentity,
                transaction.Candidate.EntryIdentity,
                StringComparison.Ordinal) &&
            string.Equals(
                admission.ReleaseManifestSha256,
                transaction.Candidate.ReleaseManifestSha256,
                StringComparison.Ordinal);
    }

    private static bool TransactionMatches(
        ManagedSetupRecoveryTransaction expected,
        ManagedSetupRecoveryTransaction actual)
    {
        return string.Equals(expected.TransactionId, actual.TransactionId, StringComparison.Ordinal) &&
            string.Equals(
                expected.ManagedRootIdentity,
                actual.ManagedRootIdentity,
                StringComparison.Ordinal) &&
            string.Equals(
                expected.StatePathIdentity,
                actual.StatePathIdentity,
                StringComparison.Ordinal) &&
            expected.Phase == actual.Phase &&
            expected.OwnedPaths.SequenceEqual(actual.OwnedPaths, StringComparer.Ordinal) &&
            expected.Payload == actual.Payload &&
            expected.Candidate == actual.Candidate;
    }
}
