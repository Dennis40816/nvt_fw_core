// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Prepared mutation and startup recovery using the exact live application-state writer.</summary>
public sealed partial class ManagedMutationCoordinator : IManagedApplicationInitialization, IDisposable
{
    private readonly string _managedRoot;
    private readonly string _statePath;
    private readonly IVersionManagerStateStore _stateStore;
    private readonly IManagedVersionRepository _repository;
    private readonly ILauncherMutationFence _launcherFence;
    private readonly IManagedPackageSelection _packages;
    private readonly IManagedRetentionPolicy _retention;
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private ManagedMutationSnapshot? _current;
    private bool _disposed;

    /// <summary>Creates coordination with explicit paths, repositories and mandatory caller policies.</summary>
    public ManagedMutationCoordinator(string managedRoot, string statePath,
        IVersionManagerStateStore stateStore, IManagedVersionRepository repository,
        ILauncherMutationFence launcherFence, IManagedPackageSelection packages, IManagedRetentionPolicy retention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        _managedRoot = ManagedRootPathIdentity.Normalize(managedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _launcherFence = launcherFence ?? throw new ArgumentNullException(nameof(launcherFence));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
    }

    /// <inheritdoc />
    public async ValueTask<ManagedMutationSnapshot> InitializeAsync(CancellationToken cancellationToken, bool isReadOnly = false)
    {
        return await InitializeWithWriterLeaseTimeoutAsync(
            TimeSpan.Zero,
            cancellationToken,
            isReadOnly).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ManagedMutationSnapshot> InitializeAfterManagedReadyAsync(
        CancellationToken cancellationToken)
    {
        return await InitializeWithWriterLeaseTimeoutAsync(
            ManagedActivationCoordinator.DefaultWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ManagedMutationSnapshot> InitializeWithWriterLeaseTimeoutAsync(
        TimeSpan writerLeaseTimeout,
        CancellationToken cancellationToken,
        bool isReadOnly = false)
    {
        ThrowIfDisposed();
        await _mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await VersionManagementInitialization.LoadAsync(isReadOnly, writerLeaseTimeout,
                ReloadDurableCurrentWithoutLockAsync, AcquireWriteLeaseAsync, PublishStateUnavailable, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _mutation.Release();
        }
    }

    /// <summary>Deletes one exact installed version after the caller supplies rollback-loss consent.</summary>
    public async ValueTask<VersionDeleteOperationResult> DeleteAsync(
        ManagedAppVersion version,
        bool rollbackLossConfirmed,
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
                ManagedMutationSnapshot unavailable = PublishStateUnavailable();
                return new(
                    new(ManagedVersionDeleteBlock.RecoveryRequired, RequiresRollbackLossWarning: false),
                    VersionDeleteOperationIssue.StateUnavailable,
                    RepositoryIssue: null,
                    unavailable);
            }
            LauncherMutationProtection? launcherProtection = await LoadLauncherProtectionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (launcherProtection is null)
            {
                ManagedMutationSnapshot unavailable = PublishStateUnavailable();
                return new(
                    new(ManagedVersionDeleteBlock.LauncherActivationPending, RequiresRollbackLossWarning: false),
                    VersionDeleteOperationIssue.StateUnavailable,
                    RepositoryIssue: null,
                    unavailable);
            }
            ManagedMutationSnapshot current = await ReloadDurableCurrentWithoutLockAsync(cancellationToken)
                .ConfigureAwait(false);
            if (current.State is not { } state)
            {
                return new(
                    new(ManagedVersionDeleteBlock.RecoveryRequired, RequiresRollbackLossWarning: false),
                    VersionDeleteOperationIssue.StateUnavailable,
                    RepositoryIssue: null,
                    current);
            }
            if (current.InventoryIssue != ManagedVersionInventoryReadIssue.None)
            {
                return new(
                    new(ManagedVersionDeleteBlock.RecoveryRequired, RequiresRollbackLossWarning: false),
                    VersionDeleteOperationIssue.StateUnavailable,
                    RepositoryIssue: null,
                    current);
            }
            if (state.PendingActivation is not null)
            {
                return new(
                    new(ManagedVersionDeleteBlock.RecoveryRequired, RequiresRollbackLossWarning: false),
                    VersionDeleteOperationIssue.StateUnavailable,
                    RepositoryIssue: null,
                    current);
            }
            if (state.PendingMutation is not null)
            {
                state = await ReconcilePendingMutationAsync(state, cancellationToken).ConfigureAwait(false);
                ManagedVersionInventoryReadResult recoveredInventory = await InventoryAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);
                current = WithInventory(current, state, recoveredInventory);
                _current = current;
                if (!recoveredInventory.IsSuccess || state.PendingMutation is not null)
                {
                    return new(
                        new(ManagedVersionDeleteBlock.RecoveryRequired, RequiresRollbackLossWarning: false),
                        VersionDeleteOperationIssue.StateUnavailable,
                        RepositoryIssue: null,
                        current);
                }
            }
            ManagedVersionInventoryReadResult inventoryResult = await InventoryAsync(
                state,
                cancellationToken).ConfigureAwait(false);
            if (!inventoryResult.IsSuccess)
            {
                ManagedMutationSnapshot unavailable = PublishInventoryUnavailable(state);
                return new(
                    new(ManagedVersionDeleteBlock.RecoveryRequired, RequiresRollbackLossWarning: false),
                    VersionDeleteOperationIssue.StateUnavailable,
                    RepositoryIssue: null,
                    unavailable);
            }
            ManagedVersionInventory inventory = inventoryResult.Inventory!;
            ManagedVersionAdmission? admission = state.Admissions.SingleOrDefault(item => item.Version == version);
            ManagedVersionDeleteDecision decision = VersionManagementPolicy.DecideDelete(
                inventory,
                version,
                launcherProtection,
                admission);
            if (!decision.IsAllowed)
            {
                return new(
                    decision,
                    VersionDeleteOperationIssue.PolicyBlocked,
                    RepositoryIssue: null,
                    current with { Inventory = inventory });
            }
            if (decision.RequiresRollbackLossWarning && !rollbackLossConfirmed)
            {
                return new(
                    decision,
                    VersionDeleteOperationIssue.RollbackConfirmationRequired,
                    RepositoryIssue: null,
                    current with { Inventory = inventory });
            }
            ManagedVersionAdmission exactAdmission = admission ??
                throw new InvalidOperationException("Delete target admission is absent.");
            if (launcherProtection.IsLastKnownGoodOnly(exactAdmission))
            {
                LauncherMutationFenceIssue retired = await _launcherFence.RetireLastKnownGoodOwnerAsync(
                    exactAdmission,
                    cancellationToken).ConfigureAwait(false);
                if (retired != LauncherMutationFenceIssue.None)
                {
                    return new(
                        decision,
                        VersionDeleteOperationIssue.StateUnavailable,
                        RepositoryIssue: null,
                        current with { Inventory = inventory });
                }
            }
            VersionManagerState prepared = state.WithPendingMutation(
                new(ManagedVersionMutationKind.Delete, exactAdmission));
            if (!await TrySaveAsync(prepared, cancellationToken).ConfigureAwait(false))
            {
                return new(
                    decision,
                    VersionDeleteOperationIssue.StateUnavailable,
                    RepositoryIssue: null,
                    current with { Inventory = inventory });
            }
            state = prepared;
            ManagedVersionDeleteIssue deleteIssue = await _repository.DeleteAsync(
                _managedRoot,
                exactAdmission,
                state.ActiveVersion,
                cancellationToken).ConfigureAwait(false);
            bool filesystemDeleteCommitted = deleteIssue is
                ManagedVersionDeleteIssue.None or ManagedVersionDeleteIssue.NotInstalled;
            if (filesystemDeleteCommitted)
            {
                state = CommitDelete(state, exactAdmission);
            }
            else
            {
                VersionManagerState cleared = state.WithPendingMutation(null);
                if (!await TrySaveAsync(cleared, cancellationToken).ConfigureAwait(false))
                {
                    ManagedVersionInventoryReadResult preparedInventory = await InventoryAsync(
                        prepared,
                        cancellationToken).ConfigureAwait(false);
                    _current = WithInventory(current, prepared, preparedInventory);
                    return new(
                        decision,
                        VersionDeleteOperationIssue.StateUnavailable,
                        deleteIssue,
                        _current);
                }
                state = cleared;
                ManagedVersionInventoryReadResult clearedInventory = await InventoryAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);
                _current = WithInventory(current, state, clearedInventory);
                return new(
                    decision,
                    clearedInventory.IsSuccess
                        ? VersionDeleteOperationIssue.RepositoryFailure
                        : VersionDeleteOperationIssue.StateUnavailable,
                    deleteIssue,
                    _current);
            }
            inventoryResult = await InventoryAsync(state, cancellationToken).ConfigureAwait(false);
            if (!inventoryResult.IsSuccess)
            {
                _current = WithInventory(current, prepared, inventoryResult);
                return new(
                    decision,
                    VersionDeleteOperationIssue.StateUnavailable,
                    deleteIssue,
                    _current);
            }
            inventory = inventoryResult.Inventory!;
            state = ClearRetentionReviewIfAtOrBelowThreshold(state, inventory);
            if (!await TrySaveAsync(state, cancellationToken).ConfigureAwait(false))
            {
                VersionManagerState durablePrepared = prepared;
                ManagedVersionInventoryReadResult durableInventory = await InventoryAsync(
                    durablePrepared,
                    cancellationToken).ConfigureAwait(false);
                _current = WithInventory(current, durablePrepared, durableInventory);
                return new(
                    decision,
                    VersionDeleteOperationIssue.StateUnavailable,
                    deleteIssue,
                    _current);
            }
            _current = current with
            {
                State = state,
                Inventory = inventory,
                InventoryIssue = ManagedVersionInventoryReadIssue.None,
            };
            return new(
                decision,
                filesystemDeleteCommitted
                    ? VersionDeleteOperationIssue.None
                    : VersionDeleteOperationIssue.RepositoryFailure,
                deleteIssue,
                _current);
        }
        finally
        {
            _ = _mutation.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) { return; }
        _mutation.Dispose();
        _disposed = true;
    }
}
