// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Exact managed-process role bound to inherited lifetime authority.</summary>
public enum ManagedProcessLifetimeKind
{
    /// <summary>The immutable Root Bootstrap tree.</summary>
    Bootstrap,
    /// <summary>The version-scoped Desktop application tree.</summary>
    Application,
    /// <summary>The version-scoped inner Launcher tree.</summary>
    Launcher,
}

/// <summary>Stable result of nested launcher READY supervision.</summary>
public enum LauncherProcessStartOutcome
{
    /// <summary>Ready outcome.</summary>
    Ready,
    /// <summary>StartFailed outcome.</summary>
    StartFailed,
    /// <summary>ExitedBeforeReady outcome.</summary>
    ExitedBeforeReady,
    /// <summary>ReadyTimeout outcome.</summary>
    ReadyTimeout,
    /// <summary>InvalidReadySignal outcome.</summary>
    InvalidReadySignal,
    /// <summary>TerminationUnconfirmed outcome.</summary>
    TerminationUnconfirmed,
}
/// <summary>Process result and exact READY-qualified application admission.</summary>
public sealed record LauncherProcessStartResult(
    LauncherProcessStartOutcome Outcome,
    int? ExitCode,
    ManagedVersionAdmission? ReadyAdmission = null);

/// <summary>Contained launcher process and authoritative lifetime port.</summary>
public interface IManagedLauncherProcess
{
    /// <summary>Inspects the authoritative inherited lifetime for the exact state path and role.</summary>
    ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(
        string statePath,
        ManagedProcessLifetimeKind kind,
        CancellationToken cancellationToken);

    /// <summary>Bounds lease acquisition, validation, contained creation, and readiness from start entry. Cleanup confirmation may extend the terminal result by up to ten seconds.</summary>
    ValueTask<LauncherProcessStartResult> StartUntilReadyAsync(
        string managedRoot,
        string statePath,
        ManagedLauncherIdentity launcher,
        IManagedExecutableLaunchLease executableLease,
        TimeSpan readyDeadline,
        CancellationToken cancellationToken);
}

/// <summary>Stable launcher journal coordination outcome.</summary>
public enum LauncherBootstrapOutcome
{
    /// <summary>Ready outcome.</summary>
    Ready,
    /// <summary>RolledBack outcome.</summary>
    RolledBack,
    /// <summary>InvalidState outcome.</summary>
    InvalidState,
    /// <summary>ManagedRootMismatch outcome.</summary>
    ManagedRootMismatch,
    /// <summary>AppMutationPending outcome.</summary>
    AppMutationPending,
    /// <summary>DamagedLauncher outcome.</summary>
    DamagedLauncher,
    /// <summary>ProtocolMismatch outcome.</summary>
    ProtocolMismatch,
    /// <summary>StartFailed outcome.</summary>
    StartFailed,
    /// <summary>RollbackUnavailable outcome.</summary>
    RollbackUnavailable,
    /// <summary>StateChanged outcome.</summary>
    StateChanged,
    /// <summary>StateUnavailable outcome.</summary>
    StateUnavailable,
    /// <summary>Busy outcome.</summary>
    Busy,
    /// <summary>TerminationUnconfirmed outcome.</summary>
    TerminationUnconfirmed,
}

/// <summary>Selected and failed launcher identities from one transaction.</summary>
public sealed record LauncherBootstrapResult(
    LauncherBootstrapOutcome Outcome,
    ManagedLauncherIdentity? RunningLauncher,
    ManagedLauncherIdentity? FailedLauncher);
