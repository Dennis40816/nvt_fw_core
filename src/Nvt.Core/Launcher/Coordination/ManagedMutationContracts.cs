// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Durable application state and complete installed inventory without source or UI policy.</summary>
public sealed record ManagedMutationSnapshot(
    VersionManagerState? State,
    ManagedVersionInventory Inventory,
    VersionManagerStateLoadIssue StateIssue,
    ManagedVersionInventoryReadIssue InventoryIssue = ManagedVersionInventoryReadIssue.None);

/// <summary>Install result and its refreshed durable snapshot.</summary>
public sealed record VersionInstallOperationResult(ManagedVersionInstallResult Install, ManagedMutationSnapshot Snapshot);

/// <summary>Guarded deletion decision, repository result and refreshed durable snapshot.</summary>
public sealed record VersionDeleteOperationResult(
    ManagedVersionDeleteDecision Decision,
    VersionDeleteOperationIssue OperationIssue,
    ManagedVersionDeleteIssue? RepositoryIssue,
    ManagedMutationSnapshot Snapshot);

/// <summary>Stable application-side result of a guarded deletion request.</summary>
public enum VersionDeleteOperationIssue
{
    /// <summary>The exact admitted non-active version was deleted.</summary>
    None,
    /// <summary>Policy blocked the request before filesystem mutation.</summary>
    PolicyBlocked,
    /// <summary>The fallback target requires explicit rollback-loss consent.</summary>
    RollbackConfirmationRequired,
    /// <summary>The repository rejected or could not complete deletion.</summary>
    RepositoryFailure,
    /// <summary>Writer, inventory, journal or durable state authority was unavailable.</summary>
    StateUnavailable,
}

/// <summary>Mandatory caller policy selecting an already validated package for the reloaded state.</summary>
public interface IManagedPackageSelection
{
    /// <summary>Selects from the caller's current catalog, or null when source, active-version or catalog authority changed.</summary>
    ValueTask<UpdateCatalogVersionSnapshot?> SelectPackageAsync(
        VersionManagerState state, ManagedAppVersion version, CancellationToken cancellationToken);
}

/// <summary>Mandatory caller retention advice; Core supplies no threshold or deletion consent.</summary>
public interface IManagedRetentionPolicy
{
    /// <summary>Reports whether a successful update should set the durable review reminder.</summary>
    bool ShouldOfferRetentionReview(ManagedVersionInventory inventory, bool updateSucceeded);
    /// <summary>Reports whether the remaining complete inventory permits clearing that reminder.</summary>
    bool ShouldClearRetentionReview(ManagedVersionInventory inventory);
}

/// <summary>Startup initialization port distinguishing read-only reads from READY-qualified recovery.</summary>
public interface IManagedApplicationInitialization
{
    /// <summary>Loads inventory without a source check; read-only calls neither lease nor recover.</summary>
    ValueTask<ManagedMutationSnapshot> InitializeAsync(CancellationToken cancellationToken, bool isReadOnly = false);
    /// <summary>Waits for the launcher's writer before durable recovery after READY.</summary>
    ValueTask<ManagedMutationSnapshot> InitializeAfterManagedReadyAsync(CancellationToken cancellationToken);
}
