// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinator
{
    private async ValueTask<VersionManagerWriteLeaseResult> AcquireWriteLeaseAsync(
        TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        VersionManagerWriteLeaseResult lease = await _stateStore.TryAcquireWriteLeaseAsync(
            waitTimeout, cancellationToken).ConfigureAwait(false);
        if (lease.IsAcquired && !lease.HoldsStatePath(_statePath))
        {
            lease.Dispose();
            return new(VersionManagerWriteLeaseIssue.Unavailable);
        }
        return lease;
    }

    private async ValueTask<ManagedMutationSnapshot> ReloadDurableCurrentWithoutLockAsync(
        CancellationToken cancellationToken, bool recoverPendingMutation = true)
    {
        VersionManagerStateLoadResult loaded = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        bool managedRootMismatch = loaded.IsSuccess && !loaded.State!.IsBoundToManagedRoot(_managedRoot);
        VersionManagerState? state = loaded.IsSuccess && !managedRootMismatch ? loaded.State
            : loaded.Issue == VersionManagerStateLoadIssue.Missing ? EmptyState() : null;
        bool launcherFenceUnavailable = false;
        if (recoverPendingMutation && state?.PendingMutation is not null &&
            await LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            state = null;
            launcherFenceUnavailable = true;
        }
        else if (recoverPendingMutation && state?.PendingMutation is not null)
        {
            state = await ReconcilePendingMutationAsync(state, cancellationToken).ConfigureAwait(false);
        }
        ManagedVersionInventoryReadResult inventory = state is null
            ? ManagedVersionInventoryReadResult.Unavailable()
            : await InventoryAsync(state, cancellationToken).ConfigureAwait(false);
        _current = new(state, inventory.Inventory ?? ManagedVersionInventory.Create([]),
            launcherFenceUnavailable ? VersionManagerStateLoadIssue.Unavailable
            : managedRootMismatch ? VersionManagerStateLoadIssue.ManagedRootMismatch
            : loaded.Issue == VersionManagerStateLoadIssue.Missing ? VersionManagerStateLoadIssue.None : loaded.Issue,
            inventory.Issue);
        return _current;
    }

    private ManagedMutationSnapshot PublishStateUnavailable()
    {
        _current = new(_current?.State, ManagedVersionInventory.Create([]),
            VersionManagerStateLoadIssue.Unavailable, ManagedVersionInventoryReadIssue.Unavailable);
        return _current;
    }

    private ManagedMutationSnapshot PublishInventoryUnavailable(VersionManagerState state)
    {
        _current = new(state, ManagedVersionInventory.Create([]),
            _current?.StateIssue ?? VersionManagerStateLoadIssue.None, ManagedVersionInventoryReadIssue.Unavailable);
        return _current;
    }

    private static ManagedMutationSnapshot WithInventory(
        ManagedMutationSnapshot snapshot, VersionManagerState state, ManagedVersionInventoryReadResult result)
    {
        return snapshot with { State = state, Inventory = result.Inventory ?? ManagedVersionInventory.Create([]),
            InventoryIssue = result.Issue };
    }

    private async ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
        VersionManagerState state,
        CancellationToken cancellationToken)
    {
        ManagedVersionInventoryReadResult observedResult = await _repository.InventoryAsync(
            _managedRoot,
            state.Admissions,
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            state.FailedActivationVersion,
            cancellationToken).ConfigureAwait(false);
        if (!observedResult.IsSuccess)
        {
            return ManagedVersionInventoryReadResult.Unavailable();
        }
        ManagedVersionInventory observed = observedResult.Inventory!;
        ManagedVersionAdmission? recoverable =
            state.PendingMutation is { Kind: ManagedVersionMutationKind.Install } pending
            ? pending.Admission
            : null;
        return ManagedVersionInventoryReadResult.Success(
            ManagedVersionInventory.Create(observed.Versions.Select(row =>
                row.AdmissionState == ManagedVersionAdmissionState.Admitted
                    ? row
                    : recoverable is not null &&
                      row.Integrity == ManagedVersionIntegrity.Healthy &&
                      row.ObservedAdmission == recoverable
                        ? row with { AdmissionState = ManagedVersionAdmissionState.RecoveryCandidate }
                        : row with { AdmissionState = ManagedVersionAdmissionState.Unadmitted })));
    }
}
