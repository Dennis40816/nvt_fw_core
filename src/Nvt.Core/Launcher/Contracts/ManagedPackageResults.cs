// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Contracts;

/// <summary>Content admission persisted for one installed managed version.</summary>
/// <param name="Version">The exact admitted version.</param>
/// <param name="AdmissionIdentity">The exact admitted package identity.</param>
/// <param name="ReleaseManifestSha256">The exact release-manifest digest.</param>
public sealed record ManagedVersionAdmission(
    ManagedAppVersion Version,
    string AdmissionIdentity,
    string ReleaseManifestSha256);

/// <summary>Verified package candidate admitted for an install or update decision.</summary>
/// <param name="Version">The exact verified version.</param>
/// <param name="AdmissionIdentity">The exact verified package identity.</param>
/// <param name="ReleaseNotes">The admitted release-note text.</param>
public sealed record VerifiedUpdateCandidate(
    ManagedAppVersion Version,
    string AdmissionIdentity,
    string ReleaseNotes);

/// <summary>Stable managed-package installation and verification result category.</summary>
public enum ManagedVersionInstallIssue
{
    /// <summary>The version was verified, installed or already installed identically.</summary>
    None,
    /// <summary>The package is unavailable, unreadable or differs from its catalog length.</summary>
    PackageUnavailable,
    /// <summary>The package length matches but its SHA-256 differs.</summary>
    PackageMismatch,
    /// <summary>The archive path or expansion shape is unsafe.</summary>
    UnsafeArchive,
    /// <summary>The source or an admitted installed closed payload is invalid.</summary>
    InvalidPayload,
    /// <summary>The same version is installed with another identity.</summary>
    IdentityConflict,
    /// <summary>Staging or atomic promotion could not complete.</summary>
    PromotionFailed,
    /// <summary>Exact-handle staging cleanup failed and recovery residue remains.</summary>
    CleanupIncomplete,
    /// <summary>A transaction blocks mutation or writer, journal or commit state is unavailable.</summary>
    StateUnavailable,
}

/// <summary>Fail-closed managed-package installation result.</summary>
/// <param name="Admission">The complete admission when present.</param>
/// <param name="Issue">The stable terminal issue.</param>
/// <param name="WasAlreadyInstalled">Whether the exact admission was already installed.</param>
public sealed record ManagedVersionInstallResult(
    ManagedVersionAdmission? Admission,
    ManagedVersionInstallIssue Issue,
    bool WasAlreadyInstalled)
{
    /// <summary>Gets whether a complete admitted version is present.</summary>
    public bool IsSuccess => Admission is not null && Issue == ManagedVersionInstallIssue.None;
}

/// <summary>Fail-closed complete package verification result without installation.</summary>
/// <param name="Candidate">The complete verified candidate when present.</param>
/// <param name="Issue">The stable terminal issue.</param>
public sealed record ManagedPackageVerificationResult(
    VerifiedUpdateCandidate? Candidate,
    ManagedVersionInstallIssue Issue)
{
    /// <summary>Gets whether the complete package and closed payload verified.</summary>
    public bool IsVerified => Candidate is not null && Issue == ManagedVersionInstallIssue.None;

    /// <summary>Gets whether the package contains the supported managed launcher contract.</summary>
    public bool HasSupportedManagedLauncher { get; init; }
}

/// <summary>Stable issue for acquiring one verified executable launch lease.</summary>
public enum ManagedExecutableLaunchIssue
{
    /// <summary>The exact executable is held against write/delete through start.</summary>
    None,
    /// <summary>The executable or its owning manifest could not be observed safely.</summary>
    Unavailable,
    /// <summary>The executable no longer matches its admitted identity.</summary>
    Tampered,
    /// <summary>The path is unsafe or outside its admitted managed tree.</summary>
    UnsafePath,
}

/// <summary>Repository-owned verified executable custody held through process creation.</summary>
public interface IManagedExecutableLaunchLease : IDisposable
{
    /// <summary>Gets the exact stable executable path.</summary>
    string ExecutablePath { get; }

    /// <summary>Gets the exact stable working directory.</summary>
    string WorkingDirectory { get; }

    /// <summary>Revalidates the admitted tree immediately before process creation.</summary>
    /// <returns>Whether the held executable and admitted tree remain valid for start.</returns>
    bool TryValidateForStart();
}

/// <summary>Typed fail-closed executable launch-lease result.</summary>
/// <param name="Lease">The held executable custody when acquired.</param>
/// <param name="Issue">The stable terminal acquisition issue.</param>
public sealed record ManagedExecutableLaunchLeaseResult(
    IManagedExecutableLaunchLease? Lease,
    ManagedExecutableLaunchIssue Issue)
{
    /// <summary>Gets whether the exact verified executable is held for launch.</summary>
    public bool IsAcquired => Lease is not null && Issue == ManagedExecutableLaunchIssue.None;
}

/// <summary>Stable installed-launcher verification and lease acquisition issue.</summary>
public enum InstalledLauncherIssue
{
    /// <summary>The exact owner-bound launcher verified.</summary>
    None,
    /// <summary>The owning tree or launcher is unavailable.</summary>
    Unavailable,
    /// <summary>The owning release manifest is invalid.</summary>
    InvalidManifest,
    /// <summary>The launcher content differs from its admitted identity.</summary>
    Tampered,
    /// <summary>The launcher protocol is unsupported.</summary>
    ProtocolMismatch,
    /// <summary>The launcher path is unsafe.</summary>
    UnsafePath,
}

/// <summary>Normalized installed-launcher verification result.</summary>
/// <param name="Identity">The exact owner-bound identity when verified.</param>
/// <param name="Issue">The stable terminal verification issue.</param>
public sealed record InstalledLauncherResult(
    ManagedLauncherIdentity? Identity,
    InstalledLauncherIssue Issue)
{
    /// <summary>Gets whether the exact launcher identity verified.</summary>
    public bool IsVerified => Identity is not null && Issue == InstalledLauncherIssue.None;
}

/// <summary>Normalized installed-launcher custody result used at the process boundary.</summary>
/// <param name="Identity">The exact owner-bound launcher identity.</param>
/// <param name="Lease">The held executable custody.</param>
/// <param name="Issue">The stable terminal acquisition issue.</param>
public sealed record InstalledLauncherLaunchResult(
    ManagedLauncherIdentity? Identity,
    IManagedExecutableLaunchLease? Lease,
    InstalledLauncherIssue Issue)
{
    /// <summary>Gets whether both the verified identity and its custody were acquired.</summary>
    public bool IsAcquired => Identity is not null && Lease is not null && Issue == InstalledLauncherIssue.None;
}
