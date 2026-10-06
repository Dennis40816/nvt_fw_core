// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Stable whole-inventory read result category.</summary>
public enum ManagedVersionInventoryReadIssue
{
    /// <summary>The complete managed-version inventory was observed.</summary>
    None,
    /// <summary>The complete inventory could not be observed without returning partial facts.</summary>
    Unavailable,
}

/// <summary>Fail-closed result for one complete managed-version inventory read.</summary>
public sealed record ManagedVersionInventoryReadResult
{
    private ManagedVersionInventoryReadResult(
        ManagedVersionInventory? inventory,
        ManagedVersionInventoryReadIssue issue)
    {
        Inventory = inventory;
        Issue = issue;
    }

    /// <summary>Gets the complete inventory when the read succeeded.</summary>
    public ManagedVersionInventory? Inventory { get; }

    /// <summary>Gets the terminal whole-inventory read issue.</summary>
    public ManagedVersionInventoryReadIssue Issue { get; }

    /// <summary>Gets whether the complete inventory is available.</summary>
    public bool IsSuccess =>
        Inventory is not null && Issue == ManagedVersionInventoryReadIssue.None;

    /// <summary>Creates one complete successful inventory result.</summary>
    public static ManagedVersionInventoryReadResult Success(ManagedVersionInventory inventory)
    {
        return new(
            inventory ?? throw new ArgumentNullException(nameof(inventory)),
            ManagedVersionInventoryReadIssue.None);
    }

    /// <summary>Creates one whole-inventory unavailable result with no partial facts.</summary>
    public static ManagedVersionInventoryReadResult Unavailable()
    {
        return new(null, ManagedVersionInventoryReadIssue.Unavailable);
    }
}

/// <summary>Stable guarded-delete result category.</summary>
public enum ManagedVersionDeleteIssue
{
    /// <summary>The exact admitted non-active directory was deleted.</summary>
    None,
    /// <summary>The requested version is active.</summary>
    ActiveVersion,
    /// <summary>The requested version is not an admitted installed child.</summary>
    NotInstalled,
    /// <summary>The resolved target is unsafe or outside the managed root.</summary>
    UnsafeTarget,
    /// <summary>The exact target could not be removed.</summary>
    DeleteFailed,
}

/// <summary>Filesystem/process-free Application port for managed payload storage.</summary>
public interface IManagedVersionRepository
{
    /// <summary>Verifies and holds the exact admitted application executable against replacement.</summary>
    ValueTask<ManagedExecutableLaunchLeaseResult> AcquireApplicationLaunchLeaseAsync(
        string managedRoot,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ManagedExecutableLaunchLeaseResult(
            null,
            ManagedExecutableLaunchIssue.Unavailable));
    }

    /// <summary>Fully verifies one catalog package without creating an installed version.</summary>
    /// <param name="sourceRoot">Committed update-source root.</param>
    /// <param name="package">Validated catalog entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete verification result.</returns>
    ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
        string sourceRoot,
        UpdateCatalogVersionSnapshot package,
        CancellationToken cancellationToken);

    /// <summary>Verifies, stages, and atomically promotes one catalog package.</summary>
    /// <param name="managedRoot">Stable launcher-owned managed root.</param>
    /// <param name="sourceRoot">Committed update-source root.</param>
    /// <param name="package">Validated catalog entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete install result.</returns>
    ValueTask<ManagedVersionInstallResult> InstallAsync(
        string managedRoot,
        string sourceRoot,
        UpdateCatalogVersionSnapshot package,
        CancellationToken cancellationToken);

    /// <summary>Inventories and verifies every admitted installed version.</summary>
    /// <param name="managedRoot">Stable launcher-owned managed root.</param>
    /// <param name="admissions">Persisted content admissions.</param>
    /// <param name="activeVersion">Current active version.</param>
    /// <param name="lastKnownGoodVersion">Current fallback version.</param>
    /// <param name="failedActivationVersion">Optional activation-failed version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete verified inventory, or a typed unavailable result without partial facts.</returns>
    ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
        string managedRoot,
        IReadOnlyList<ManagedVersionAdmission> admissions,
        ManagedAppVersion? activeVersion,
        ManagedAppVersion? lastKnownGoodVersion,
        ManagedAppVersion? failedActivationVersion,
        CancellationToken cancellationToken);

    /// <summary>Deletes one exact admitted non-active managed directory.</summary>
    /// <param name="managedRoot">Stable launcher-owned managed root.</param>
    /// <param name="admission">Exact admitted target identity.</param>
    /// <param name="activeVersion">Current active version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The guarded-delete issue.</returns>
    ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
        string managedRoot,
        ManagedVersionAdmission admission,
        ManagedAppVersion? activeVersion,
        CancellationToken cancellationToken);
}
