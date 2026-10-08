// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Stable issue returned before or during exact immutable Bootstrap process creation.</summary>
public enum ImmutableBootstrapStartIssue
{
    /// <summary>The exact Bootstrap process started and returned a completion receipt.</summary>
    None,
    /// <summary>Another state writer owns the launch transaction.</summary>
    Busy,
    /// <summary>The exact Bootstrap path, PE, length, or SHA-256 is invalid.</summary>
    Damaged,
    /// <summary>The exact process could not be created.</summary>
    StartFailed,
    /// <summary>The Bootstrap or process result could not be observed safely.</summary>
    Unavailable,
}

/// <summary>Terminal outcome observed after an exact Bootstrap process was started.</summary>
public enum ImmutableBootstrapCompletionOutcome
{
    /// <summary>The selected application completed READY.</summary>
    Ready,
    /// <summary>The Bootstrap completed READY through the admitted LKG rollback.</summary>
    RolledBack,
    /// <summary>The Bootstrap completed without a READY application.</summary>
    Failed,
    /// <summary>The started process result could not be observed safely.</summary>
    Unavailable,
    /// <summary>The outer Bootstrap job could not be proven empty or safely released.</summary>
    TerminationUnconfirmed,
}

/// <summary>Typed outcome observed before Root Bootstrap may continue its READY wait.</summary>
public enum ImmutableBootstrapAdmissionOutcome
{
    /// <summary>Root Bootstrap started the exact version Launcher.</summary>
    Admitted,
    /// <summary>Another writer owns the bounded Bootstrap startup transaction.</summary>
    Busy,
    /// <summary>Installed state or executable evidence requires recovery.</summary>
    RecoveryRequired,
    /// <summary>Root Bootstrap could not start the version Launcher.</summary>
    LaunchFailed,
    /// <summary>The pre-launch health result could not be observed safely.</summary>
    HealthUnavailable,
    /// <summary>The outer Bootstrap job could not be proven empty after failed admission.</summary>
    TerminationUnconfirmed,
}

/// <summary>Exact path-free reason represented by an observed immutable Bootstrap exit code.</summary>
public enum ImmutableBootstrapExitIssue
{
    /// <summary>The process did not report a failure exit.</summary>
    None,
    /// <summary>Another process owns the required state writer.</summary>
    Busy,
    /// <summary>The managed version state is invalid.</summary>
    InvalidState,
    /// <summary>The state is bound to a different managed root.</summary>
    ManagedRootMismatch,
    /// <summary>An application mutation transaction is still pending.</summary>
    MutationPending,
    /// <summary>The installed version Launcher failed immutable verification.</summary>
    DamagedLauncher,
    /// <summary>The installed version Launcher uses an incompatible protocol.</summary>
    ProtocolMismatch,
    /// <summary>The exact version Launcher process could not be started.</summary>
    StartFailed,
    /// <summary>No admitted last-known-good rollback target is available.</summary>
    RollbackUnavailable,
    /// <summary>The managed version state changed during launch.</summary>
    StateChanged,
    /// <summary>The managed version state could not be observed or persisted.</summary>
    StateUnavailable,
    /// <summary>The managed process tree could not be proven terminated.</summary>
    TerminationUnconfirmed,
    /// <summary>The immutable Bootstrap received invalid arguments.</summary>
    InvalidArguments,
    /// <summary>The immutable Bootstrap rejected an internal invariant.</summary>
    InvariantViolation,
    /// <summary>The immutable Bootstrap inherited an incomplete process context.</summary>
    InvalidInheritedContext,
    /// <summary>The inherited start gate did not authorize Bootstrap.</summary>
    StartNotAuthorized,
    /// <summary>The immutable Bootstrap returned its reserved undefined-failure code.</summary>
    UndefinedFailure,
    /// <summary>The immutable Bootstrap returned an unrecognized failure code.</summary>
    Unknown,
}

/// <summary>Single numeric wire codec shared by immutable Bootstrap producers and consumers.</summary>
public static class ImmutableBootstrapExitCodeCodec
{
    /// <summary>Successful completion through the active Launcher.</summary>
    public const int Ready = 0;
    /// <summary>Successful completion through the admitted last-known-good Launcher.</summary>
    public const int RolledBack = 1;

    /// <summary>Encodes one concrete Bootstrap failure issue.</summary>
    public static int EncodeFailure(ImmutableBootstrapExitIssue issue)
    {
        return issue switch
        {
            ImmutableBootstrapExitIssue.Busy => 2,
            ImmutableBootstrapExitIssue.InvalidState => 10,
            ImmutableBootstrapExitIssue.ManagedRootMismatch => 11,
            ImmutableBootstrapExitIssue.MutationPending => 12,
            ImmutableBootstrapExitIssue.DamagedLauncher => 13,
            ImmutableBootstrapExitIssue.ProtocolMismatch => 14,
            ImmutableBootstrapExitIssue.StartFailed => 15,
            ImmutableBootstrapExitIssue.RollbackUnavailable => 16,
            ImmutableBootstrapExitIssue.StateChanged => 17,
            ImmutableBootstrapExitIssue.StateUnavailable => 18,
            ImmutableBootstrapExitIssue.TerminationUnconfirmed => 19,
            ImmutableBootstrapExitIssue.InvalidArguments => 20,
            ImmutableBootstrapExitIssue.InvariantViolation => 21,
            ImmutableBootstrapExitIssue.InvalidInheritedContext => 22,
            ImmutableBootstrapExitIssue.StartNotAuthorized => 23,
            ImmutableBootstrapExitIssue.UndefinedFailure => 99,
            ImmutableBootstrapExitIssue.None or ImmutableBootstrapExitIssue.Unknown =>
                throw new ArgumentOutOfRangeException(nameof(issue), issue, "Issue has no failure encoding."),
            _ => throw new ArgumentOutOfRangeException(nameof(issue), issue, "Issue is undefined."),
        };
    }

    /// <summary>Decodes one observed failure code without treating unknown values as success.</summary>
    public static ImmutableBootstrapExitIssue DecodeFailure(int exitCode)
    {
        return exitCode switch
        {
            2 => ImmutableBootstrapExitIssue.Busy,
            10 => ImmutableBootstrapExitIssue.InvalidState,
            11 => ImmutableBootstrapExitIssue.ManagedRootMismatch,
            12 => ImmutableBootstrapExitIssue.MutationPending,
            13 => ImmutableBootstrapExitIssue.DamagedLauncher,
            14 => ImmutableBootstrapExitIssue.ProtocolMismatch,
            15 => ImmutableBootstrapExitIssue.StartFailed,
            16 => ImmutableBootstrapExitIssue.RollbackUnavailable,
            17 => ImmutableBootstrapExitIssue.StateChanged,
            18 => ImmutableBootstrapExitIssue.StateUnavailable,
            19 => ImmutableBootstrapExitIssue.TerminationUnconfirmed,
            20 => ImmutableBootstrapExitIssue.InvalidArguments,
            21 => ImmutableBootstrapExitIssue.InvariantViolation,
            22 => ImmutableBootstrapExitIssue.InvalidInheritedContext,
            23 => ImmutableBootstrapExitIssue.StartNotAuthorized,
            99 => ImmutableBootstrapExitIssue.UndefinedFailure,
            _ => ImmutableBootstrapExitIssue.Unknown,
        };
    }

    /// <summary>Projects one decoded pre-admission issue into its stable coarse outcome.</summary>
    public static ImmutableBootstrapAdmissionOutcome ClassifyAdmission(
        ImmutableBootstrapExitIssue issue)
    {
        return issue switch
        {
            ImmutableBootstrapExitIssue.Busy => ImmutableBootstrapAdmissionOutcome.Busy,
            ImmutableBootstrapExitIssue.InvalidState or
            ImmutableBootstrapExitIssue.ManagedRootMismatch or
            ImmutableBootstrapExitIssue.MutationPending or
            ImmutableBootstrapExitIssue.DamagedLauncher or
            ImmutableBootstrapExitIssue.ProtocolMismatch =>
                ImmutableBootstrapAdmissionOutcome.RecoveryRequired,
            ImmutableBootstrapExitIssue.StartFailed or
            ImmutableBootstrapExitIssue.RollbackUnavailable or
            ImmutableBootstrapExitIssue.StateChanged =>
                ImmutableBootstrapAdmissionOutcome.LaunchFailed,
            ImmutableBootstrapExitIssue.TerminationUnconfirmed =>
                ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed,
            ImmutableBootstrapExitIssue.None or
            ImmutableBootstrapExitIssue.StateUnavailable or
            ImmutableBootstrapExitIssue.InvalidArguments or
            ImmutableBootstrapExitIssue.InvariantViolation or
            ImmutableBootstrapExitIssue.InvalidInheritedContext or
            ImmutableBootstrapExitIssue.StartNotAuthorized or
            ImmutableBootstrapExitIssue.UndefinedFailure or
            ImmutableBootstrapExitIssue.Unknown =>
                ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(issue), issue, "Issue is undefined."),
        };
    }

    /// <summary>Projects one observed post-admission code into its stable terminal outcome.</summary>
    public static ImmutableBootstrapCompletionOutcome ClassifyCompletion(int exitCode)
    {
        return exitCode switch
        {
            Ready => ImmutableBootstrapCompletionOutcome.Ready,
            RolledBack => ImmutableBootstrapCompletionOutcome.RolledBack,
            _ when DecodeFailure(exitCode) == ImmutableBootstrapExitIssue.TerminationUnconfirmed =>
                ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed,
            _ => ImmutableBootstrapCompletionOutcome.Failed,
        };
    }
}

/// <summary>One typed cross-process Root Bootstrap admission receipt.</summary>
public sealed record ImmutableBootstrapAdmissionResult(
    ImmutableBootstrapAdmissionOutcome Outcome,
    int? ExitCode = null,
    ImmutableBootstrapExitIssue ExitIssue = ImmutableBootstrapExitIssue.None)
{
    /// <summary>Gets whether outcome, optional code, and typed issue describe one receipt.</summary>
    public bool HasValidShape =>
        Enum.IsDefined(Outcome) &&
        Enum.IsDefined(ExitIssue) &&
        (Outcome switch
        {
            ImmutableBootstrapAdmissionOutcome.Admitted =>
                ExitCode is null && ExitIssue == ImmutableBootstrapExitIssue.None,
            ImmutableBootstrapAdmissionOutcome.LaunchFailed when ExitCode is null =>
                ExitIssue == ImmutableBootstrapExitIssue.StartFailed,
            ImmutableBootstrapAdmissionOutcome.HealthUnavailable when ExitCode is null =>
                ExitIssue == ImmutableBootstrapExitIssue.None,
            ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed when ExitCode is null =>
                ExitIssue == ImmutableBootstrapExitIssue.TerminationUnconfirmed,
            ImmutableBootstrapAdmissionOutcome.Busy or
            ImmutableBootstrapAdmissionOutcome.RecoveryRequired or
            ImmutableBootstrapAdmissionOutcome.LaunchFailed or
            ImmutableBootstrapAdmissionOutcome.HealthUnavailable or
            ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed when ExitCode is int exitCode =>
                exitCode is not
                    ImmutableBootstrapExitCodeCodec.Ready and not
                    ImmutableBootstrapExitCodeCodec.RolledBack &&
                ExitIssue == ImmutableBootstrapExitCodeCodec.DecodeFailure(exitCode) &&
                Outcome == ImmutableBootstrapExitCodeCodec.ClassifyAdmission(ExitIssue),
            _ => false,
        });
}

/// <summary>Typed terminal result from one already-started immutable Bootstrap.</summary>
public sealed record ImmutableBootstrapCompletionResult(
    ImmutableBootstrapCompletionOutcome Outcome,
    int? ExitCode = null,
    ImmutableBootstrapExitIssue ExitIssue = ImmutableBootstrapExitIssue.None)
{
    /// <summary>Gets whether outcome, optional code, and typed issue describe one receipt.</summary>
    public bool HasValidShape =>
        Enum.IsDefined(Outcome) &&
        Enum.IsDefined(ExitIssue) &&
        (Outcome switch
        {
            ImmutableBootstrapCompletionOutcome.Ready =>
                ExitCode == ImmutableBootstrapExitCodeCodec.Ready &&
                ExitIssue == ImmutableBootstrapExitIssue.None,
            ImmutableBootstrapCompletionOutcome.RolledBack =>
                ExitCode == ImmutableBootstrapExitCodeCodec.RolledBack &&
                ExitIssue == ImmutableBootstrapExitIssue.None,
            ImmutableBootstrapCompletionOutcome.Failed when ExitCode is int exitCode =>
                exitCode is not
                    ImmutableBootstrapExitCodeCodec.Ready and not
                    ImmutableBootstrapExitCodeCodec.RolledBack &&
                ImmutableBootstrapExitCodeCodec.ClassifyCompletion(exitCode) ==
                    ImmutableBootstrapCompletionOutcome.Failed &&
                ExitIssue == ImmutableBootstrapExitCodeCodec.DecodeFailure(exitCode),
            ImmutableBootstrapCompletionOutcome.Unavailable =>
                ExitCode is null && ExitIssue == ImmutableBootstrapExitIssue.None,
            ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed when ExitCode is null =>
                ExitIssue == ImmutableBootstrapExitIssue.TerminationUnconfirmed,
            ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed when ExitCode is int exitCode =>
                ImmutableBootstrapExitCodeCodec.ClassifyCompletion(exitCode) ==
                    ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed &&
                ExitIssue == ImmutableBootstrapExitCodeCodec.DecodeFailure(exitCode),
            _ => false,
        });
}

/// <summary>Remaining monotonic operation and total budgets for one process wait.</summary>
public readonly record struct ImmutableBootstrapWaitBudget
{
    /// <summary>Creates a fail-closed budget whose total includes cleanup observation.</summary>
    public ImmutableBootstrapWaitBudget(TimeSpan remainingOperation, TimeSpan remainingTotal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(remainingOperation, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(remainingTotal, remainingOperation);
        RemainingOperation = remainingOperation;
        RemainingTotal = remainingTotal;
    }

    /// <summary>Gets remaining time in which the operation may reach its terminal signal.</summary>
    public TimeSpan RemainingOperation { get; }

    /// <summary>Gets remaining caller-visible time including bounded cleanup observation.</summary>
    public TimeSpan RemainingTotal { get; }
}

/// <summary>Receipt for one already-started immutable Bootstrap process.</summary>
public interface IImmutableBootstrapLaunch : IDisposable
{
    /// <summary>Waits for exact Launcher admission within the remaining absolute budget.</summary>
    ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken);

    /// <summary>Waits for Bootstrap/READY completion within the remaining absolute budget.</summary>
    ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken);
}

/// <summary>Typed result from bounded stable verification and process creation.</summary>
public sealed record ImmutableBootstrapStartResult(
    IImmutableBootstrapLaunch? Launch,
    ImmutableBootstrapStartIssue Issue)
{
    /// <summary>Gets whether exact process creation completed and returned its receipt.</summary>
    public bool IsStarted => Launch is not null && Issue == ImmutableBootstrapStartIssue.None;

    /// <summary>Gets whether receipt custody and the typed issue describe one valid result shape.</summary>
    public bool HasValidShape =>
        Enum.IsDefined(Issue) &&
        (Launch is not null) == (Issue == ImmutableBootstrapStartIssue.None);
}

/// <summary>Stable process seam shared by healthy entry and post-install launch.</summary>
public interface IImmutableBootstrapHandoff
{
    /// <summary>Verifies and starts the exact Root Bootstrap without waiting for child READY.</summary>
    ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        CancellationToken cancellationToken);
}

/// <summary>
/// Setup-only process seam that consumes already verified promoted-tree launch custody.
/// </summary>
public interface IImmutableBootstrapLeaseHandoff
{
    /// <summary>
    /// Consumes <paramref name="ownedLease"/> on every path and starts the exact Root Bootstrap
    /// without reacquiring custody from the promoted managed path.
    /// </summary>
    ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken);
}

