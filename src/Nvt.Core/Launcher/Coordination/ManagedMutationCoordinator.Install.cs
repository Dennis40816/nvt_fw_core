// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinator
{
    /// <summary>Installs the exact caller-selected package with a durable prepared mutation journal.</summary>
    public async ValueTask<VersionInstallOperationResult> InstallAsync(
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
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                    PublishStateUnavailable());
            }
            if (await LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                    PublishStateUnavailable());
            }
            ManagedMutationSnapshot current = await ReloadDurableCurrentWithoutLockAsync(cancellationToken)
                .ConfigureAwait(false);
            if (current.State is not { } state)
            {
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                    current);
            }
            if (current.InventoryIssue != ManagedVersionInventoryReadIssue.None)
            {
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                    current);
            }
            if (state.PendingActivation is not null)
            {
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
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
                        new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                        current);
                }
            }
            if (state.UpdateSource is not { } sourceRoot || await _packages.SelectPackageAsync(state, version, cancellationToken).ConfigureAwait(false) is not { } package || package.Version != version)
            {
                return new(new(null, ManagedVersionInstallIssue.PackageUnavailable, WasAlreadyInstalled: false), current);
            }
            var expectedAdmission = new ManagedVersionAdmission(package.Version, package.Identity, package.ReleaseManifestSha256);
            ManagedVersionAdmission? existingAdmission = state.Admissions.SingleOrDefault(
                admission => admission.Version == version);
            if (existingAdmission is not null)
            {
                ManagedVersionInventoryReadResult existingInventoryResult = await InventoryAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);
                _current = WithInventory(current, state, existingInventoryResult);
                if (!existingInventoryResult.IsSuccess)
                {
                    return new(
                        new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                        _current);
                }
                ManagedVersionInventory existingInventory = existingInventoryResult.Inventory!;
                InstalledVersionSnapshot? existing = existingInventory.Find(version);
                bool exactHealthyPayload = existing is
                {
                    AdmissionState: ManagedVersionAdmissionState.Admitted,
                    Integrity: ManagedVersionIntegrity.Healthy,
                } &&
                    string.Equals(
                        existing.AdmissionIdentity,
                        existingAdmission.AdmissionIdentity,
                        StringComparison.Ordinal);
                return existingAdmission == expectedAdmission && exactHealthyPayload
                    ? new(
                        new(existingAdmission, ManagedVersionInstallIssue.None, WasAlreadyInstalled: true),
                        _current)
                    : existingAdmission == expectedAdmission
                        ? new(
                            new(null, ManagedVersionInstallIssue.InvalidPayload, WasAlreadyInstalled: false),
                            _current)
                    : new(
                        new(null, ManagedVersionInstallIssue.IdentityConflict, WasAlreadyInstalled: false),
                        _current);
            }
            VersionManagerState prepared = state.WithPendingMutation(
                new(ManagedVersionMutationKind.Install, expectedAdmission));
            if (!await TrySaveAsync(prepared, cancellationToken).ConfigureAwait(false))
            {
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                    current);
            }
            state = prepared;
            _current = current with { State = state };

            ManagedVersionInstallResult result = await _repository.InstallAsync(
                _managedRoot,
                sourceRoot,
                package,
                cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || result.Admission != expectedAdmission)
            {
                state = await ReconcilePendingMutationAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);
                ManagedVersionInventoryReadResult failedInventoryResult = await InventoryAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);
                _current = WithInventory(current, state, failedInventoryResult);
                ManagedVersionInstallIssue issue = !failedInventoryResult.IsSuccess ||
                    state.PendingMutation is not null
                    ? ManagedVersionInstallIssue.StateUnavailable
                    : result.Issue == ManagedVersionInstallIssue.None
                        ? ManagedVersionInstallIssue.InvalidPayload
                        : result.Issue;
                return new(
                    new(null, issue, WasAlreadyInstalled: false),
                    _current);
            }

            state = CommitInstall(state, expectedAdmission);
            ManagedVersionInventoryReadResult inventoryResult = await InventoryAsync(
                state,
                cancellationToken).ConfigureAwait(false);
            if (!inventoryResult.IsSuccess)
            {
                _current = WithInventory(current, prepared, inventoryResult);
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                    _current);
            }
            ManagedVersionInventory inventory = inventoryResult.Inventory!;
            state = MarkRetentionReviewDue(
                state,
                inventory,
                result.IsSuccess && !result.WasAlreadyInstalled);
            if (!await TrySaveAsync(state, cancellationToken).ConfigureAwait(false))
            {
                VersionManagerState durablePrepared = prepared;
                ManagedVersionInventoryReadResult recoveryInventory = await InventoryAsync(
                    durablePrepared,
                    cancellationToken).ConfigureAwait(false);
                _current = WithInventory(current, durablePrepared, recoveryInventory);
                return new(
                    new(null, ManagedVersionInstallIssue.StateUnavailable, WasAlreadyInstalled: false),
                    _current);
            }
            _current = current with
            {
                State = state,
                Inventory = inventory,
                InventoryIssue = ManagedVersionInventoryReadIssue.None,
            };
            return new(result, _current);
        }
        finally
        {
            _ = _mutation.Release();
        }
    }
}
