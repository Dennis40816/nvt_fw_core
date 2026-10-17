// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.Launcher.Setup;

/// <summary>Stable presentation outcome for first-install planning and execution.</summary>
public enum ManagedFirstInstallationOutcome
{
    /// <summary>One immutable plan is ready for user confirmation.</summary>
    ReadyToInstall,
    /// <summary>The exact root completed Bootstrap READY and marker finalization.</summary>
    Completed = 2,
    /// <summary>The distribution payload is absent or could not be read.</summary>
    PayloadUnavailable,
    /// <summary>The distribution payload failed strict admission.</summary>
    PayloadInvalid,
    /// <summary>The Registry, Catalog, or package cannot be observed completely.</summary>
    SourceUnavailable,
    /// <summary>The observed Registry or Catalog publication is invalid.</summary>
    SourceRejected,
    /// <summary>No compatible fully admitted package is available.</summary>
    CandidateUnavailable,
    /// <summary>The exact payload or source changed after planning.</summary>
    SourceChanged,
    /// <summary>The selected destination is not admitted.</summary>
    InvalidDestination,
    /// <summary>The selected destination cannot be inspected or written by the current user.</summary>
    PermissionDenied,
    /// <summary>Another process owns the version-state writer.</summary>
    Busy,
    /// <summary>Existing or residual facts require the recovery owner.</summary>
    RecoveryRequired,
    /// <summary>The root was promoted but Bootstrap could not complete first READY.</summary>
    InstalledButLaunchFailed,
    /// <summary>A complete state or transaction fact could not be observed or persisted.</summary>
    StateUnavailable,
    /// <summary>The caller cancelled the current planning or installation attempt.</summary>
    Cancelled,
}

/// <summary>Immutable user-reviewed first-install plan.</summary>
public sealed class ManagedFirstInstallationPlan
{
    internal ManagedFirstInstallationPlan(
        string managedRoot,
        string statePathIdentity,
        ManagedDistributionPayloadIdentity payload,
        FreshInstallationCandidate candidate)
    {
        ManagedRoot = managedRoot;
        StatePathIdentity = statePathIdentity;
        Payload = payload;
        Candidate = candidate;
    }

    /// <summary>Gets the exact normalized destination root.</summary>
    public string ManagedRoot { get; }
    /// <summary>Gets the exact canonical state-path identity.</summary>
    public string StatePathIdentity { get; }
    /// <summary>Gets the exact reviewed distribution payload.</summary>
    public ManagedDistributionPayloadIdentity Payload { get; }
    /// <summary>Gets the exact reviewed Registry/Catalog/package candidate.</summary>
    public FreshInstallationCandidate Candidate { get; }
}

/// <summary>Typed first-install planning result.</summary>
public sealed record ManagedFirstInstallationPlanResult(
    ManagedFirstInstallationPlan? Plan,
    ManagedFirstInstallationOutcome Outcome)
{
    /// <summary>Gets whether one exact plan is ready for confirmation.</summary>
    public bool IsReady => Plan is not null && Outcome == ManagedFirstInstallationOutcome.ReadyToInstall;
}

/// <summary>Exact post-promotion stage that prevented first application READY.</summary>
public enum ManagedFirstInstallationLaunchStage
{
    /// <summary>No post-promotion launch failure was observed.</summary>
    None,
    /// <summary>The immutable Root Bootstrap process could not be started safely.</summary>
    BootstrapStart,
    /// <summary>Root Bootstrap did not admit the exact version Launcher.</summary>
    LauncherAdmission,
    /// <summary>The admitted Launcher did not complete selected-application READY.</summary>
    ApplicationReady,
    /// <summary>
    /// Root promotion completed, but the Bootstrap-launch transaction was not yet durably recorded
    /// or its durable record operation was still in progress.
    /// </summary>
    PostPromotion,
}

/// <summary>Safe typed reason retained for one post-promotion launch failure.</summary>
public enum ManagedFirstInstallationLaunchIssue
{
    /// <summary>No launch issue was observed.</summary>
    None,
    /// <summary>The bounded operation did not complete in time.</summary>
    TimedOut,
    /// <summary>A process result violated its typed custody/result contract.</summary>
    InvalidReceipt,
    /// <summary>Another process owns the required launch transaction.</summary>
    Busy,
    /// <summary>The immutable Bootstrap identity failed verification.</summary>
    Damaged,
    /// <summary>The exact process could not be started.</summary>
    StartFailed,
    /// <summary>The required process result could not be observed safely.</summary>
    Unavailable,
    /// <summary>The installed state requires the recovery owner.</summary>
    RecoveryRequired,
    /// <summary>The pre-launch health result could not be observed safely.</summary>
    HealthUnavailable = 9,
    /// <summary>The Bootstrap state is invalid.</summary>
    InvalidState,
    /// <summary>The Bootstrap state is bound to a different managed root.</summary>
    ManagedRootMismatch,
    /// <summary>An application mutation transaction is still pending.</summary>
    MutationPending,
    /// <summary>The installed version Launcher failed immutable verification.</summary>
    DamagedLauncher,
    /// <summary>The version Launcher protocol is incompatible with Bootstrap.</summary>
    ProtocolMismatch,
    /// <summary>No admitted last-known-good rollback target is available.</summary>
    RollbackUnavailable,
    /// <summary>The version state changed during the launch transaction.</summary>
    StateChanged,
    /// <summary>The version state could not be read or persisted.</summary>
    StateUnavailable,
    /// <summary>The immutable Bootstrap received invalid launch arguments.</summary>
    InvalidArguments,
    /// <summary>The immutable Bootstrap rejected an internal invariant.</summary>
    InvariantViolation,
    /// <summary>The immutable Bootstrap inherited an incomplete or invalid process context.</summary>
    InvalidInheritedContext,
    /// <summary>The parent did not authorize Bootstrap through the inherited start gate.</summary>
    StartNotAuthorized,
    /// <summary>The immutable Bootstrap returned an undefined terminal failure.</summary>
    UndefinedFailure,
    /// <summary>The immutable Bootstrap returned an unrecognized exit code.</summary>
    UnknownExit,
    /// <summary>The launched process tree could not be proven terminated.</summary>
    TerminationUnconfirmed,
    /// <summary>Only the last-known-good rollback, not the selected version, reported READY.</summary>
    RolledBack,
    /// <summary>The caller cancelled after the managed root was promoted.</summary>
    Cancelled,
}

/// <summary>Safe diagnostic retained from the exact Bootstrap/Launcher process boundary.</summary>
public sealed record ManagedFirstInstallationLaunchFailure(
    ManagedFirstInstallationLaunchStage Stage,
    ManagedFirstInstallationLaunchIssue Issue,
    int? ExitCode = null)
{
    /// <summary>Gets the exact non-empty post-promotion failure stage.</summary>
    public ManagedFirstInstallationLaunchStage Stage { get; } =
        !Enum.IsDefined(Stage) || Stage == ManagedFirstInstallationLaunchStage.None
            ? throw new ArgumentOutOfRangeException(nameof(Stage))
            : Stage;

    /// <summary>Gets the single authoritative path-free failure reason.</summary>
    public ManagedFirstInstallationLaunchIssue Issue { get; } =
        !Enum.IsDefined(Issue) || Issue == ManagedFirstInstallationLaunchIssue.None
            ? throw new ArgumentOutOfRangeException(nameof(Issue))
            : Issue;

    /// <summary>Gets the optional exit code only when it agrees with the typed failure reason.</summary>
    public int? ExitCode { get; } =
        HasValidFailureShape(Stage, Issue, ExitCode)
            ? ExitCode
            : throw new ArgumentException(
                "Bootstrap boundary, issue, and exit code contradict one another.",
                nameof(ExitCode));

    /// <summary>Gets whether the current record still contains one non-contradictory failure.</summary>
    public bool HasValidShape =>
        Enum.IsDefined(Stage) &&
        Enum.IsDefined(Issue) &&
        Stage != ManagedFirstInstallationLaunchStage.None &&
        Issue != ManagedFirstInstallationLaunchIssue.None &&
        HasValidFailureShape(Stage, Issue, ExitCode);

    internal bool IsCompatibleWithOutcome(ManagedFirstInstallationOutcome outcome)
    {
        if (!HasValidShape)
        {
            return false;
        }
        bool recoveryRequired =
            (Stage == ManagedFirstInstallationLaunchStage.PostPromotion && Issue is
                ManagedFirstInstallationLaunchIssue.RecoveryRequired or
                ManagedFirstInstallationLaunchIssue.StateUnavailable) ||
            (Stage == ManagedFirstInstallationLaunchStage.BootstrapStart &&
                Issue == ManagedFirstInstallationLaunchIssue.Damaged) ||
            (Stage == ManagedFirstInstallationLaunchStage.LauncherAdmission &&
                Issue is ManagedFirstInstallationLaunchIssue.RecoveryRequired or
                    ManagedFirstInstallationLaunchIssue.InvalidState or
                    ManagedFirstInstallationLaunchIssue.ManagedRootMismatch or
                    ManagedFirstInstallationLaunchIssue.MutationPending or
                    ManagedFirstInstallationLaunchIssue.DamagedLauncher or
                    ManagedFirstInstallationLaunchIssue.ProtocolMismatch);
        ManagedFirstInstallationOutcome expected = recoveryRequired
            ? ManagedFirstInstallationOutcome.RecoveryRequired
            : ManagedFirstInstallationOutcome.InstalledButLaunchFailed;
        return outcome == expected;
    }

    internal static ManagedFirstInstallationLaunchIssue MapBootstrapExitIssue(
        ImmutableBootstrapExitIssue issue)
    {
        return issue switch
        {
            ImmutableBootstrapExitIssue.Busy => ManagedFirstInstallationLaunchIssue.Busy,
            ImmutableBootstrapExitIssue.InvalidState => ManagedFirstInstallationLaunchIssue.InvalidState,
            ImmutableBootstrapExitIssue.ManagedRootMismatch =>
                ManagedFirstInstallationLaunchIssue.ManagedRootMismatch,
            ImmutableBootstrapExitIssue.MutationPending =>
                ManagedFirstInstallationLaunchIssue.MutationPending,
            ImmutableBootstrapExitIssue.DamagedLauncher =>
                ManagedFirstInstallationLaunchIssue.DamagedLauncher,
            ImmutableBootstrapExitIssue.ProtocolMismatch =>
                ManagedFirstInstallationLaunchIssue.ProtocolMismatch,
            ImmutableBootstrapExitIssue.StartFailed => ManagedFirstInstallationLaunchIssue.StartFailed,
            ImmutableBootstrapExitIssue.RollbackUnavailable =>
                ManagedFirstInstallationLaunchIssue.RollbackUnavailable,
            ImmutableBootstrapExitIssue.StateChanged => ManagedFirstInstallationLaunchIssue.StateChanged,
            ImmutableBootstrapExitIssue.StateUnavailable =>
                ManagedFirstInstallationLaunchIssue.StateUnavailable,
            ImmutableBootstrapExitIssue.TerminationUnconfirmed =>
                ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed,
            ImmutableBootstrapExitIssue.InvalidArguments =>
                ManagedFirstInstallationLaunchIssue.InvalidArguments,
            ImmutableBootstrapExitIssue.InvariantViolation =>
                ManagedFirstInstallationLaunchIssue.InvariantViolation,
            ImmutableBootstrapExitIssue.InvalidInheritedContext =>
                ManagedFirstInstallationLaunchIssue.InvalidInheritedContext,
            ImmutableBootstrapExitIssue.StartNotAuthorized =>
                ManagedFirstInstallationLaunchIssue.StartNotAuthorized,
            ImmutableBootstrapExitIssue.UndefinedFailure =>
                ManagedFirstInstallationLaunchIssue.UndefinedFailure,
            ImmutableBootstrapExitIssue.Unknown => ManagedFirstInstallationLaunchIssue.UnknownExit,
            ImmutableBootstrapExitIssue.None => throw new ArgumentOutOfRangeException(
                nameof(issue), issue, "A successful Bootstrap exit has no launch failure."),
            _ => throw new ArgumentOutOfRangeException(nameof(issue), issue, "Issue is undefined."),
        };
    }

    private static bool HasValidFailureShape(
        ManagedFirstInstallationLaunchStage stage,
        ManagedFirstInstallationLaunchIssue issue,
        int? exitCode)
    {
        bool stageAllowsIssue = StageAllowsIssue(stage, issue, exitCode);
        bool hasExpectedExitPresence = HasExpectedExitPresence(stage, issue, exitCode);
        return stageAllowsIssue &&
            hasExpectedExitPresence &&
            (exitCode is null || issue == ManagedFirstInstallationLaunchIssue.InvalidReceipt ||
                (exitCode == ImmutableBootstrapExitCodeCodec.RolledBack
                ? issue == ManagedFirstInstallationLaunchIssue.RolledBack
                : exitCode != ImmutableBootstrapExitCodeCodec.Ready &&
                    issue == MapBootstrapExitIssue(
                        ImmutableBootstrapExitCodeCodec.DecodeFailure(exitCode.Value))));
    }
    private static bool StageAllowsIssue(ManagedFirstInstallationLaunchStage stage,
        ManagedFirstInstallationLaunchIssue issue, int? exitCode)
    {
        return stage switch
        {
            ManagedFirstInstallationLaunchStage.PostPromotion =>
                exitCode is null && issue is
                    ManagedFirstInstallationLaunchIssue.RecoveryRequired or
                    ManagedFirstInstallationLaunchIssue.StateUnavailable or
                    ManagedFirstInstallationLaunchIssue.Cancelled,
            ManagedFirstInstallationLaunchStage.BootstrapStart =>
                exitCode is null && issue is
                    ManagedFirstInstallationLaunchIssue.TimedOut or
                    ManagedFirstInstallationLaunchIssue.InvalidReceipt or
                    ManagedFirstInstallationLaunchIssue.Busy or
                    ManagedFirstInstallationLaunchIssue.Damaged or
                    ManagedFirstInstallationLaunchIssue.StartFailed or
                    ManagedFirstInstallationLaunchIssue.Unavailable or
                    ManagedFirstInstallationLaunchIssue.Cancelled,
            ManagedFirstInstallationLaunchStage.LauncherAdmission => issue is
                ManagedFirstInstallationLaunchIssue.TimedOut or
                ManagedFirstInstallationLaunchIssue.InvalidReceipt or
                ManagedFirstInstallationLaunchIssue.Busy or
                ManagedFirstInstallationLaunchIssue.HealthUnavailable or
                ManagedFirstInstallationLaunchIssue.InvalidState or
                ManagedFirstInstallationLaunchIssue.ManagedRootMismatch or
                ManagedFirstInstallationLaunchIssue.MutationPending or
                ManagedFirstInstallationLaunchIssue.DamagedLauncher or
                ManagedFirstInstallationLaunchIssue.ProtocolMismatch or
                ManagedFirstInstallationLaunchIssue.StartFailed or
                ManagedFirstInstallationLaunchIssue.RollbackUnavailable or
                ManagedFirstInstallationLaunchIssue.StateChanged or
                ManagedFirstInstallationLaunchIssue.StateUnavailable or
                ManagedFirstInstallationLaunchIssue.InvalidArguments or
                ManagedFirstInstallationLaunchIssue.InvariantViolation or
                ManagedFirstInstallationLaunchIssue.InvalidInheritedContext or
                ManagedFirstInstallationLaunchIssue.StartNotAuthorized or
                ManagedFirstInstallationLaunchIssue.UndefinedFailure or
                ManagedFirstInstallationLaunchIssue.UnknownExit or
                ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed or
                ManagedFirstInstallationLaunchIssue.Cancelled,
            ManagedFirstInstallationLaunchStage.ApplicationReady => issue is
                ManagedFirstInstallationLaunchIssue.TimedOut or
                ManagedFirstInstallationLaunchIssue.InvalidReceipt or
                ManagedFirstInstallationLaunchIssue.Busy or
                ManagedFirstInstallationLaunchIssue.Unavailable or
                ManagedFirstInstallationLaunchIssue.InvalidState or
                ManagedFirstInstallationLaunchIssue.ManagedRootMismatch or
                ManagedFirstInstallationLaunchIssue.MutationPending or
                ManagedFirstInstallationLaunchIssue.DamagedLauncher or
                ManagedFirstInstallationLaunchIssue.ProtocolMismatch or
                ManagedFirstInstallationLaunchIssue.StartFailed or
                ManagedFirstInstallationLaunchIssue.RollbackUnavailable or
                ManagedFirstInstallationLaunchIssue.StateChanged or
                ManagedFirstInstallationLaunchIssue.StateUnavailable or
                ManagedFirstInstallationLaunchIssue.InvalidArguments or
                ManagedFirstInstallationLaunchIssue.InvariantViolation or
                ManagedFirstInstallationLaunchIssue.InvalidInheritedContext or
                ManagedFirstInstallationLaunchIssue.StartNotAuthorized or
                ManagedFirstInstallationLaunchIssue.UndefinedFailure or
                ManagedFirstInstallationLaunchIssue.UnknownExit or
                ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed or
                ManagedFirstInstallationLaunchIssue.RolledBack or
                ManagedFirstInstallationLaunchIssue.Cancelled,
            ManagedFirstInstallationLaunchStage.None => false,
            _ => false,
        };
    }

    private static bool HasExpectedExitPresence(ManagedFirstInstallationLaunchStage stage,
        ManagedFirstInstallationLaunchIssue issue, int? exitCode)
    {
        return stage switch
        {
            ManagedFirstInstallationLaunchStage.PostPromotion or
            ManagedFirstInstallationLaunchStage.BootstrapStart => exitCode is null,
            ManagedFirstInstallationLaunchStage.LauncherAdmission when issue is
                ManagedFirstInstallationLaunchIssue.StartFailed or
                ManagedFirstInstallationLaunchIssue.InvalidReceipt or
                ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed => true,
            ManagedFirstInstallationLaunchStage.LauncherAdmission when issue is
                ManagedFirstInstallationLaunchIssue.TimedOut or
                ManagedFirstInstallationLaunchIssue.HealthUnavailable or
                ManagedFirstInstallationLaunchIssue.Cancelled => exitCode is null,
            ManagedFirstInstallationLaunchStage.LauncherAdmission => exitCode is not null,
            ManagedFirstInstallationLaunchStage.ApplicationReady when
                issue is ManagedFirstInstallationLaunchIssue.InvalidReceipt or
                    ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed => true,
            ManagedFirstInstallationLaunchStage.ApplicationReady when issue is
                ManagedFirstInstallationLaunchIssue.TimedOut or
                ManagedFirstInstallationLaunchIssue.Unavailable or
                ManagedFirstInstallationLaunchIssue.Cancelled => exitCode is null,
            ManagedFirstInstallationLaunchStage.ApplicationReady => exitCode is not null,
            ManagedFirstInstallationLaunchStage.None => false,
            _ => false,
        };
    }

}

/// <summary>Typed first-install execution result.</summary>
public sealed record ManagedFirstInstallationResult(
    ManagedFirstInstallationOutcome Outcome,
    string ManagedRoot,
    ManagedAppVersion Version,
    ManagedFirstInstallationLaunchFailure? LaunchFailure = null)
{
    /// <summary>Gets the defined execution outcome.</summary>
    public ManagedFirstInstallationOutcome Outcome { get; } =
        Enum.IsDefined(Outcome)
            ? Outcome
            : throw new ArgumentOutOfRangeException(nameof(Outcome));

    /// <summary>Gets the exact managed root supplied by the caller.</summary>
    public string ManagedRoot { get; } = ManagedRoot;

    /// <summary>Gets the exact selected application version.</summary>
    public ManagedAppVersion Version { get; } = Version;

    /// <summary>Gets the exact post-promotion failure, when this result is launch-terminal.</summary>
    public ManagedFirstInstallationLaunchFailure? LaunchFailure { get; } =
        IsValidLaunchFailureShape(Outcome, LaunchFailure)
            ? LaunchFailure
            : throw new ArgumentException(
                "First-install result carries an invalid launch-failure shape.",
                nameof(LaunchFailure));

    /// <summary>Gets whether this is one complete terminal presentation result.</summary>
    public bool HasValidShape =>
        Enum.IsDefined(Outcome) &&
        Outcome != ManagedFirstInstallationOutcome.ReadyToInstall &&
        (LaunchFailure is not { } failure
            ? Outcome != ManagedFirstInstallationOutcome.InstalledButLaunchFailed
            : (Outcome is ManagedFirstInstallationOutcome.InstalledButLaunchFailed or
                ManagedFirstInstallationOutcome.RecoveryRequired) &&
                failure.IsCompatibleWithOutcome(Outcome));

    /// <summary>Gets whether only the separate recovery owner may continue this terminal result.</summary>
    public bool IsRecoveryOwned =>
        HasValidShape && Outcome is
            ManagedFirstInstallationOutcome.RecoveryRequired or
            ManagedFirstInstallationOutcome.InstalledButLaunchFailed;

    private static bool IsValidLaunchFailureShape(
        ManagedFirstInstallationOutcome outcome,
        ManagedFirstInstallationLaunchFailure? failure)
    {
        return Enum.IsDefined(outcome) &&
            (outcome == ManagedFirstInstallationOutcome.InstalledButLaunchFailed
                ? failure is not null && failure.IsCompatibleWithOutcome(outcome)
                : failure is null ||
                    (outcome == ManagedFirstInstallationOutcome.RecoveryRequired &&
                        failure.IsCompatibleWithOutcome(outcome)));
    }
}
