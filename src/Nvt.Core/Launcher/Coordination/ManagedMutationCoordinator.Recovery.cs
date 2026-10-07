// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinator
{
    private async ValueTask<VersionManagerState> ReconcilePendingMutationAsync(
        VersionManagerState state,
        CancellationToken cancellationToken)
    {
        PendingManagedVersionMutation pending = state.PendingMutation ??
            throw new InvalidOperationException("No managed-version mutation requires recovery.");
        ManagedVersionInventoryReadResult inventoryResult = await InventoryAsync(
            state,
            cancellationToken).ConfigureAwait(false);
        if (!inventoryResult.IsSuccess)
        {
            return state;
        }
        ManagedVersionInventory inventory = inventoryResult.Inventory!;
        VersionManagerState? converged = null;
        if (pending.Kind == ManagedVersionMutationKind.Install)
        {
            InstalledVersionSnapshot? row = inventory.Find(pending.Admission.Version);
            if (row is null)
            {
                converged = state.WithPendingMutation(null);
            }
            else if (row.AdmissionState == ManagedVersionAdmissionState.RecoveryCandidate &&
                     row.Integrity == ManagedVersionIntegrity.Healthy &&
                     row.ObservedAdmission == pending.Admission)
            {
                converged = CommitInstall(state, pending.Admission);
                ManagedVersionInventoryReadResult committedInventoryResult = await InventoryAsync(
                    converged,
                    cancellationToken).ConfigureAwait(false);
                if (!committedInventoryResult.IsSuccess)
                {
                    return state;
                }
                converged = MarkRetentionReviewDue(
                    converged,
                    committedInventoryResult.Inventory!,
                    updateSucceeded: true);
            }
        }
        else
        {
            ManagedVersionDeleteIssue issue = await _repository.DeleteAsync(
                _managedRoot,
                pending.Admission,
                state.ActiveVersion,
                cancellationToken).ConfigureAwait(false);
            if (issue is ManagedVersionDeleteIssue.None or ManagedVersionDeleteIssue.NotInstalled)
            {
                converged = CommitDelete(state, pending.Admission);
                if (converged.RetentionReviewDue)
                {
                    ManagedVersionInventoryReadResult committedInventoryResult = await InventoryAsync(
                        converged,
                        cancellationToken).ConfigureAwait(false);
                    if (!committedInventoryResult.IsSuccess)
                    {
                        return state;
                    }
                    converged = ClearRetentionReviewIfAtOrBelowThreshold(
                        converged,
                        committedInventoryResult.Inventory!);
                }
            }
        }

        return converged is not null &&
               await TrySaveAsync(converged, cancellationToken).ConfigureAwait(false)
            ? converged
            : state;
    }

    private static VersionManagerState CommitInstall(
        VersionManagerState state,
        ManagedVersionAdmission admission)
    {
        _ = state.PendingMutation is
        { Kind: ManagedVersionMutationKind.Install, Admission: var pendingAdmission } &&
            pendingAdmission == admission
                ? true
                : throw new InvalidOperationException("Install commit differs from its durable journal.");
        return state.CompletePendingMutation(
            [.. state.Admissions, admission],
            state.LastKnownGoodVersion,
            state.FailedActivationVersion);
    }

    private VersionManagerState MarkRetentionReviewDue(
        VersionManagerState state,
        ManagedVersionInventory inventory,
        bool updateSucceeded)
    {
        return !state.RetentionReviewDue &&
               _retention.ShouldOfferRetentionReview(inventory, updateSucceeded)
            ? state.WithRetentionReviewDue(retentionReviewDue: true)
            : state;
    }

    private VersionManagerState ClearRetentionReviewIfAtOrBelowThreshold(
        VersionManagerState state,
        ManagedVersionInventory inventory)
    {
        return state.RetentionReviewDue &&
               _retention.ShouldClearRetentionReview(inventory)
            ? state.WithRetentionReviewDue(retentionReviewDue: false)
            : state;
    }

    private static VersionManagerState CommitDelete(
        VersionManagerState state,
        ManagedVersionAdmission admission)
    {
        _ = state.PendingMutation is
        { Kind: ManagedVersionMutationKind.Delete, Admission: var pendingAdmission } &&
            pendingAdmission == admission
                ? true
                : throw new InvalidOperationException("Delete commit differs from its durable journal.");
        return state.CompletePendingMutation(
            state.Admissions.Where(item => item.Version != admission.Version),
            state.LastKnownGoodVersion == admission.Version ? null : state.LastKnownGoodVersion,
            state.FailedActivationVersion == admission.Version ? null : state.FailedActivationVersion);
    }

    private async ValueTask<bool> TrySaveAsync(
        VersionManagerState state,
        CancellationToken cancellationToken)
    {
        VersionManagerStateSaveResult saved = await _stateStore.TrySaveAsync(
            state,
            cancellationToken).ConfigureAwait(false);
        return saved.IsSuccess;
    }

    private async ValueTask SaveOrThrowAsync(
        VersionManagerState state,
        CancellationToken cancellationToken)
    {
        if (!await TrySaveAsync(state, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Version-manager state is unavailable.");
        }
    }

    private VersionManagerState EmptyState()
    {
        return VersionManagerState.Create(null, null, null, [], null, null, false, managedRootIdentity: _managedRoot);
    }

    private static InvalidOperationException InvalidState()
    {
        return new("Version-manager state is invalid and requires recovery.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
