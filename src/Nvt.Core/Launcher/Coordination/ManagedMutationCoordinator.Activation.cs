// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinator
{
    /// <summary>Changes durable activation or retention intent under exact writer custody.</summary>
    public async ValueTask<ManagedMutationSnapshot> AcknowledgeRetentionReviewAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using VersionManagerWriteLeaseResult lease = await AcquireWriteLeaseAsync(
                TimeSpan.Zero,
                cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                return PublishStateUnavailable();
            }
            if (await LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                return PublishStateUnavailable();
            }
            ManagedMutationSnapshot current = await ReloadDurableCurrentWithoutLockAsync(cancellationToken)
                .ConfigureAwait(false);
            if (current.InventoryIssue != ManagedVersionInventoryReadIssue.None ||
                current.State is not { } state)
            {
                return current;
            }
            if (state.RetentionReviewDue)
            {
                state = state.WithRetentionReviewDue(retentionReviewDue: false);
                if (!await TrySaveAsync(state, cancellationToken).ConfigureAwait(false))
                {
                    return PublishStateUnavailable();
                }
                _current = current with { State = state };
            }
            return _current ?? current;
        }
        finally
        {
            _ = _mutation.Release();
        }
    }

    /// <summary>Changes durable activation or retention intent under exact writer custody.</summary>
    public async ValueTask<VersionManagerState> PrepareActivationAsync(
        ManagedAppVersion version,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using VersionManagerWriteLeaseResult lease = await AcquireWriteLeaseAsync(
                TimeSpan.Zero,
                cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                throw new InvalidOperationException("Another version-management writer is active.");
            }
            if (await LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                throw new InvalidOperationException("Launcher activation fences managed-version mutation.");
            }
            ManagedMutationSnapshot current = await ReloadDurableCurrentWithoutLockAsync(cancellationToken)
                .ConfigureAwait(false);
            VersionManagerState state = current.State ?? throw InvalidState();
            if (current.InventoryIssue != ManagedVersionInventoryReadIssue.None)
            {
                throw new InvalidOperationException("Installed-version inventory is unavailable.");
            }
            if (state.PendingMutation is not null)
            {
                throw new InvalidOperationException(
                    "A managed-version filesystem mutation still requires recovery.");
            }
            ManagedVersionInventoryReadResult inventoryResult = await InventoryAsync(
                state,
                cancellationToken).ConfigureAwait(false);
            if (!inventoryResult.IsSuccess)
            {
                _ = PublishInventoryUnavailable(state);
                throw new InvalidOperationException("Installed-version inventory is unavailable.");
            }
            ManagedVersionInventory inventory = inventoryResult.Inventory!;
            InstalledVersionSnapshot target = inventory.Find(version) ??
                throw new InvalidOperationException("Activation target is not installed.");
            if (target.AdmissionState != ManagedVersionAdmissionState.Admitted ||
                target.Integrity != ManagedVersionIntegrity.Healthy)
            {
                throw new InvalidOperationException("Activation target is damaged.");
            }
            state = VersionActivationPolicy.BeginActivation(state, version);
            await SaveOrThrowAsync(state, cancellationToken).ConfigureAwait(false);
            _current = current with { State = state, Inventory = inventory };
            return state;
        }
        finally
        {
            _ = _mutation.Release();
        }
    }

    /// <summary>Changes durable activation or retention intent under exact writer custody.</summary>
    public async ValueTask<ManagedMutationSnapshot> CancelPendingActivationAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using VersionManagerWriteLeaseResult lease = await AcquireWriteLeaseAsync(
                TimeSpan.Zero,
                cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                throw new InvalidOperationException("Another version-management writer is active.");
            }
            if (await LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                throw new InvalidOperationException("Launcher activation fences managed-version mutation.");
            }
            ManagedMutationSnapshot current = await ReloadDurableCurrentWithoutLockAsync(cancellationToken)
                .ConfigureAwait(false);
            VersionManagerState state = current.State ?? throw InvalidState();
            state = VersionActivationPolicy.CancelRequestedActivation(state);
            await SaveOrThrowAsync(state, cancellationToken).ConfigureAwait(false);
            ManagedVersionInventoryReadResult inventoryResult = await InventoryAsync(
                state,
                cancellationToken).ConfigureAwait(false);
            _current = WithInventory(current, state, inventoryResult);
            return _current;
        }
        finally
        {
            _ = _mutation.Release();
        }
    }

}
