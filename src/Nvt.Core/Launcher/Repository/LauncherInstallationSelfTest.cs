// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Repository;

/// <summary>Stable terminal category for the read-only managed launcher installation self-test.</summary>
public enum LauncherInstallationSelfTestIssue
{
    /// <summary>Bootstrap was safely observed and the exact active launcher admission verified.</summary>
    None,
    /// <summary>The application state file is absent.</summary>
    AppStateMissing,
    /// <summary>The application state file is structurally or semantically invalid.</summary>
    AppStateInvalid,
    /// <summary>The application state file could not be read completely.</summary>
    AppStateUnavailable,
    /// <summary>The application state belongs to another managed root.</summary>
    ManagedRootMismatch,
    /// <summary>An application activation or filesystem mutation is pending.</summary>
    AppTransactionPending,
    /// <summary>No committed active application version exists.</summary>
    NoActiveVersion,
    /// <summary>The active application has no exact persisted admission.</summary>
    ActiveAdmissionMissing,
    /// <summary>The launcher journal is absent.</summary>
    LauncherStateMissing,
    /// <summary>The launcher journal is structurally or semantically invalid.</summary>
    LauncherStateInvalid,
    /// <summary>The launcher journal could not be read completely.</summary>
    LauncherStateUnavailable,
    /// <summary>A launcher activation transaction is pending.</summary>
    LauncherActivationPending,
    /// <summary>The committed active launcher does not own the exact active application admission.</summary>
    ActiveLauncherMismatch,
    /// <summary>The immutable Bootstrap path is missing, empty, oversized, or unsafe.</summary>
    BootstrapInvalid,
    /// <summary>The immutable Bootstrap could not be observed completely.</summary>
    BootstrapUnavailable,
    /// <summary>The active launcher payload or manifest could not be read completely.</summary>
    ActiveLauncherUnavailable,
    /// <summary>The active launcher release manifest is invalid.</summary>
    ActiveLauncherInvalid,
    /// <summary>The active launcher bytes differ from their admitted identity.</summary>
    ActiveLauncherTampered,
    /// <summary>The active launcher protocol is unsupported.</summary>
    ActiveLauncherProtocolMismatch,
    /// <summary>The active launcher path is unsafe.</summary>
    ActiveLauncherUnsafePath,
    /// <summary>An application or launcher authority changed or became unreadable during the query.</summary>
    StateChanged,
}

/// <summary>Bounded observation of the immutable root Bootstrap, not an update admission.</summary>
public sealed record ImmutableBootstrapObservation
{
    internal ImmutableBootstrapObservation(string path, long size, string sha256)
    {
        Path = path;
        Size = size;
        Sha256 = sha256;
    }

    /// <summary>Gets the exact observed root executable path.</summary>
    public string Path { get; }

    /// <summary>Gets the observed byte length.</summary>
    public long Size { get; }

    /// <summary>Gets the lowercase SHA-256 observation.</summary>
    public string Sha256 { get; }
}

/// <summary>Exact verified active launcher identity bound to its owning application admission.</summary>
public sealed record ActiveLauncherAdmission
{
    internal ActiveLauncherAdmission(
        ManagedVersionAdmission ownerAdmission,
        ManagedAppVersion launcherVersion,
        int protocolVersion,
        string executableRelativePath,
        long size,
        string sha256)
    {
        OwnerAdmission = ownerAdmission;
        LauncherVersion = launcherVersion;
        ProtocolVersion = protocolVersion;
        ExecutableRelativePath = executableRelativePath;
        Size = size;
        Sha256 = sha256;
    }

    /// <summary>Gets the exact application admission that owns this launcher.</summary>
    public ManagedVersionAdmission OwnerAdmission { get; }

    /// <summary>Gets the independently declared launcher version.</summary>
    public ManagedAppVersion LauncherVersion { get; }

    /// <summary>Gets the verified launcher protocol version.</summary>
    public int ProtocolVersion { get; }

    /// <summary>Gets the verified relative executable path.</summary>
    public string ExecutableRelativePath { get; }

    /// <summary>Gets the verified launcher byte length.</summary>
    public long Size { get; }

    /// <summary>Gets the verified lowercase launcher SHA-256.</summary>
    public string Sha256 { get; }
}

/// <summary>Complete read-only launcher installation self-test result.</summary>
public sealed record LauncherInstallationSelfTestResult
{
    internal LauncherInstallationSelfTestResult(
        LauncherInstallationSelfTestIssue issue,
        ImmutableBootstrapObservation? bootstrap,
        ActiveLauncherAdmission? activeLauncher)
    {
        Issue = issue;
        Bootstrap = bootstrap;
        ActiveLauncher = activeLauncher;
    }

    /// <summary>Gets the terminal issue.</summary>
    public LauncherInstallationSelfTestIssue Issue { get; }

    /// <summary>Gets the Bootstrap observation only for a fully healthy result.</summary>
    public ImmutableBootstrapObservation? Bootstrap { get; }

    /// <summary>Gets the verified active launcher only for a fully healthy result.</summary>
    public ActiveLauncherAdmission? ActiveLauncher { get; }

    /// <summary>Gets whether both read-only checks completed successfully.</summary>
    public bool IsHealthy =>
        Issue == LauncherInstallationSelfTestIssue.None &&
        Bootstrap is not null &&
        ActiveLauncher is not null;
}

/// <summary>Application-facing read-only managed launcher installation health query.</summary>
public interface ILauncherInstallationSelfTest
{
    /// <summary>Observes immutable Bootstrap and verifies the exact committed active launcher.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One complete result without mutating durable state or activation.</returns>
    ValueTask<LauncherInstallationSelfTestResult> QueryAsync(CancellationToken cancellationToken);
}
