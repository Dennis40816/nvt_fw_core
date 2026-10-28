// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Launcher.Integration;

/// <summary>Immutable state projected to desktop and launcher consumers.</summary>
public sealed record VersionManagementSnapshot(
    VersionManagerState? State,
    ManagedVersionInventory Inventory,
    UpdateCatalogSnapshot? Catalog,
    VerifiedUpdateCandidate? VerifiedCandidate,
    VersionSourceStatus SourceStatus,
    UpdateCatalogLoadIssue? CatalogIssue,
    long Generation,
    bool ShouldPromptForUpdate,
    VersionManagerStateLoadIssue StateIssue,
    ManagedVersionInventoryReadIssue InventoryIssue = ManagedVersionInventoryReadIssue.None,
    VersionRegistryStatus RegistryStatus = VersionRegistryStatus.NotConfigured,
    UpdateSourceRegistryIssue RegistryIssue = UpdateSourceRegistryIssue.None);

/// <summary>Install operation plus its refreshed version-management snapshot.</summary>
public sealed record VersionInstallOperationResult(
    ManagedVersionInstallResult Install,
    VersionManagementSnapshot Snapshot);

/// <summary>Delete operation plus the revalidated policy and refreshed snapshot.</summary>
public sealed record VersionDeleteOperationResult(
    ManagedVersionDeleteDecision Decision,
    VersionDeleteOperationIssue OperationIssue,
    ManagedVersionDeleteIssue? RepositoryIssue,
    VersionManagementSnapshot Snapshot);

/// <summary>Typed Application use cases for managed-version Settings and startup coordination.</summary>
public interface IVersionManagementExperience
{
    /// <summary>Loads state and installed inventory without contacting the update source.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="isReadOnly">Reads without acquiring a writer lease or reconciling pending mutations.</param>
    /// <returns>The initial immutable snapshot.</returns>
    /// <remarks>The token precedes the optional flag. A later overload with the token last would need its own compatibility check, because calls that name both parameters become ambiguous.</remarks>
    ValueTask<VersionManagementSnapshot> InitializeAsync(CancellationToken cancellationToken, bool isReadOnly = false);

    /// <summary>Loads durable state after an exact inherited READY write while the launcher commits.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The durable post-launcher snapshot.</returns>
    ValueTask<VersionManagementSnapshot> InitializeAfterManagedReadyAsync(
        CancellationToken cancellationToken);

    /// <summary>Runs one generation-keyed source check.</summary>
    /// <param name="isAutomatic">Whether a verified newer package may consume the session prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current generation snapshot.</returns>
    ValueTask<VersionManagementSnapshot> CheckAsync(
        bool isAutomatic,
        CancellationToken cancellationToken);

    /// <summary>Commits a source path once and starts one superseding check.</summary>
    /// <param name="sourceRoot">Confirmed source folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The checked snapshot.</returns>
    ValueTask<VersionManagementSnapshot> CommitUpdateSourceAsync(
        string sourceRoot,
        CancellationToken cancellationToken);

    /// <summary>Resumes automatic registry resolution without clearing a durable pin on failure.</summary>
    ValueTask<VersionManagementSnapshot> ResumeRegistryAsync(CancellationToken cancellationToken);

    /// <summary>Verifies the fixed registry and every automatic source without mutating session or durable state.</summary>
    ValueTask<VersionEnvironmentSelfTestResult> RunEnvironmentSelfTestAsync(
        CancellationToken cancellationToken);

    /// <summary>Explicitly installs one catalog version after UI consent.</summary>
    /// <param name="version">Exact catalog version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The install result and refreshed snapshot.</returns>
    ValueTask<VersionInstallOperationResult> InstallAsync(
        ManagedAppVersion version,
        CancellationToken cancellationToken);

    /// <summary>Deletes one revalidated non-active admitted version after UI confirmation.</summary>
    /// <param name="version">Exact installed version.</param>
    /// <param name="rollbackLossConfirmed">Whether the separate last-known-good warning was confirmed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The delete decision, adapter result, and refreshed snapshot.</returns>
    ValueTask<VersionDeleteOperationResult> DeleteAsync(
        ManagedAppVersion version,
        bool rollbackLossConfirmed,
        CancellationToken cancellationToken);

    /// <summary>Acknowledges the soft retention reminder without deleting any version.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refreshed snapshot with the reminder cleared.</returns>
    ValueTask<VersionManagementSnapshot> AcknowledgeRetentionReviewAsync(
        CancellationToken cancellationToken);

    /// <summary>Persists one pending activation after the target inventory row is healthy.</summary>
    /// <param name="version">Exact installed target.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pending state to hand to the stable launcher.</returns>
    ValueTask<VersionManagerState> PrepareActivationAsync(
        ManagedAppVersion version,
        CancellationToken cancellationToken);

    /// <summary>Clears an unlaunched activation after stable-launcher handoff fails.</summary>
    ValueTask<VersionManagementSnapshot> CancelPendingActivationAsync(
        CancellationToken cancellationToken);
}
