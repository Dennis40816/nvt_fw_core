// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Integrity classification for one installed managed version.</summary>
public enum ManagedVersionIntegrity
{
    /// <summary>The closed installed payload passed verification.</summary>
    Healthy,
    /// <summary>The installed payload cannot be trusted or launched.</summary>
    Damaged,
}

/// <summary>Stable reason an installed managed version is damaged.</summary>
public enum ManagedVersionDamageReason
{
    /// <summary>A required managed payload file is missing.</summary>
    MissingFile,
    /// <summary>The managed payload contains an undeclared path.</summary>
    UnexpectedPath,
    /// <summary>The release manifest is missing, malformed, or inconsistent.</summary>
    ManifestMismatch,
    /// <summary>A declared file length or SHA-256 digest differs.</summary>
    ContentMismatch,
    /// <summary>The payload could not be read completely.</summary>
    Unreadable,
    /// <summary>The version failed its bounded activation-ready handshake.</summary>
    FailedActivation,
}

/// <summary>Trust relationship between a discovered directory and launcher state.</summary>
public enum ManagedVersionAdmissionState
{
    /// <summary>The directory is bound to an admission in committed launcher state.</summary>
    Admitted,
    /// <summary>The directory has a valid self-admission and may close a matching durable transaction.</summary>
    RecoveryCandidate,
    /// <summary>The directory has no valid admission and is never ordinary installed inventory.</summary>
    Unadmitted,
}

/// <summary>Immutable inventory row for one managed version directory.</summary>
public sealed record InstalledVersionSnapshot(
    ManagedAppVersion Version,
    string AdmissionIdentity,
    ManagedVersionIntegrity Integrity,
    ManagedVersionDamageReason? DamageReason,
    bool IsActive,
    bool IsLastKnownGood,
    ManagedVersionAdmissionState AdmissionState = ManagedVersionAdmissionState.Admitted,
    ManagedVersionAdmission? ObservedAdmission = null);

/// <summary>Stable reason a managed-version deletion is blocked.</summary>
public enum ManagedVersionDeleteBlock
{
    /// <summary>The requested installed version may be deleted after confirmation.</summary>
    None,
    /// <summary>The active version is always protected.</summary>
    ActiveVersion,
    /// <summary>The requested version is absent from admitted inventory.</summary>
    NotInstalled,
    /// <summary>The directory is not admitted and requires a separate recovery action.</summary>
    RecoveryRequired,
    /// <summary>A launcher activation transaction fences every managed-version mutation.</summary>
    LauncherActivationPending,
    /// <summary>The exact admission owns active or pending launcher authority.</summary>
    LauncherOwner,
}

/// <summary>Application-owned delete decision before destructive confirmation.</summary>
public sealed record ManagedVersionDeleteDecision(
    ManagedVersionDeleteBlock Block,
    bool RequiresRollbackLossWarning)
{
    /// <summary>Gets whether the request may proceed to explicit confirmation.</summary>
    public bool IsAllowed => Block == ManagedVersionDeleteBlock.None;
}

/// <summary>Validated installed-version inventory and health summary.</summary>
public sealed class ManagedVersionInventory
{
    private ManagedVersionInventory(IReadOnlyList<InstalledVersionSnapshot> versions)
    {
        Versions = versions;
        HealthyCount = versions.Count(version =>
            version.AdmissionState == ManagedVersionAdmissionState.Admitted &&
            version.Integrity == ManagedVersionIntegrity.Healthy);
        DamagedCount = versions.Count(version =>
            version.AdmissionState == ManagedVersionAdmissionState.Admitted &&
            version.Integrity == ManagedVersionIntegrity.Damaged);
        UnadmittedCount = versions.Count - HealthyCount - DamagedCount;
    }

    /// <summary>Gets rows ordered newest first.</summary>
    public IReadOnlyList<InstalledVersionSnapshot> Versions { get; }

    /// <summary>Gets the number of fully verified installed versions.</summary>
    public int HealthyCount { get; }

    /// <summary>Gets the number of damaged installed versions.</summary>
    public int DamagedCount { get; }

    /// <summary>Gets the number of directories outside committed admission state.</summary>
    public int UnadmittedCount { get; }

    /// <summary>Creates a unique deterministic inventory.</summary>
    /// <param name="versions">Admitted installed-version rows.</param>
    /// <returns>The validated inventory.</returns>
    public static ManagedVersionInventory Create(IEnumerable<InstalledVersionSnapshot> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        InstalledVersionSnapshot[] rows = [.. versions];
        if (rows.GroupBy(row => row.Version).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Installed inventory versions must be unique.", nameof(versions));
        }
        if (rows.Count(row => row.IsActive) > 1)
        {
            throw new ArgumentException("Installed inventory can contain at most one active version.", nameof(versions));
        }
        if (rows.Any(row =>
                string.IsNullOrWhiteSpace(row.AdmissionIdentity) ||
                (row.AdmissionState == ManagedVersionAdmissionState.RecoveryCandidate && row.ObservedAdmission is null) ||
                (row.Integrity == ManagedVersionIntegrity.Healthy && row.DamageReason is not null) ||
                (row.Integrity == ManagedVersionIntegrity.Damaged && row.DamageReason is null)))
        {
            throw new ArgumentException("Installed inventory integrity facts are inconsistent.", nameof(versions));
        }

        Array.Sort(rows, static (left, right) => right.Version.CompareTo(left.Version));
        return new(rows);
    }

    /// <summary>Finds an exact installed version.</summary>
    /// <param name="version">Version identity.</param>
    /// <returns>The inventory row, or <see langword="null"/>.</returns>
    public InstalledVersionSnapshot? Find(ManagedAppVersion version)
    {
        return Versions.FirstOrDefault(row => row.Version == version);
    }
}

/// <summary>Owner-approved non-destructive policy for managed application versions.</summary>
public static class VersionManagementPolicy
{
    /// <summary>Decides whether an installed version may enter destructive confirmation.</summary>
    /// <param name="inventory">Current verified inventory.</param>
    /// <param name="version">Exact requested version.</param>
    /// <returns>The stable policy decision.</returns>
    public static ManagedVersionDeleteDecision DecideDelete(
        ManagedVersionInventory inventory,
        ManagedAppVersion version)
    {
        return DecideDelete(
            inventory,
            version,
            new LauncherMutationProtection(
                LauncherMutationFenceIssue.None,
                HasPendingActivation: false,
                ActiveOwner: null,
                LastKnownGoodOwner: null,
                PendingOwners: []),
            installedAdmission: null);
    }

    /// <summary>Decides deletion with exact launcher-owner protection.</summary>
    public static ManagedVersionDeleteDecision DecideDelete(
        ManagedVersionInventory inventory,
        ManagedAppVersion version,
        LauncherMutationProtection launcherProtection,
        ManagedVersionAdmission? installedAdmission)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(launcherProtection);
        InstalledVersionSnapshot? installed = inventory.Find(version);
        return launcherProtection.Issue != LauncherMutationFenceIssue.None || launcherProtection.HasPendingActivation
            ? new(ManagedVersionDeleteBlock.LauncherActivationPending, RequiresRollbackLossWarning: false)
            : installedAdmission is not null && launcherProtection.IsHardProtected(installedAdmission)
            ? new(ManagedVersionDeleteBlock.LauncherOwner, RequiresRollbackLossWarning: false)
            : installed switch
            {
                null => new(ManagedVersionDeleteBlock.NotInstalled, RequiresRollbackLossWarning: false),
                { AdmissionState: not ManagedVersionAdmissionState.Admitted } =>
                    new(ManagedVersionDeleteBlock.RecoveryRequired, RequiresRollbackLossWarning: false),
                { IsActive: true } =>
                    new(ManagedVersionDeleteBlock.ActiveVersion, RequiresRollbackLossWarning: false),
                _ => new(
                    ManagedVersionDeleteBlock.None,
                    installed.IsLastKnownGood ||
                    (installedAdmission is not null && launcherProtection.IsLastKnownGoodOnly(installedAdmission))),
            };
    }

}
