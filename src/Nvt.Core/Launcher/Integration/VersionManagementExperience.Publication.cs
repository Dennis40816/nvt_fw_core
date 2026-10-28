// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Launcher.Integration;

public sealed partial class VersionManagementExperience
{
    private ReloadObservation CaptureReloadObservation()
    {
        lock (_generationSync)
        {
            bool recovering = _current is null || !HasUsableAuthority(_current);
            return new ReloadObservation(recovering ? _recoverableSourceAuthority : _current, recovering);
        }
    }

    private void ObserveCoordinatorSnapshot(ManagedMutationSnapshot snapshot, object? context, bool isRelease)
    {
        lock (_generationSync)
        {
            if (_disposed) { return; }
            MutationObservation? observation = _services.MutationObservation.Value;
            // A release cannot adopt an unobserved coordinator cache or throw for an absent
            // publication: either would replace this operation's outcome with unrelated state.
            if (isRelease && (_current is null || observation?.HasSnapshot != true)) { return; }
            if (context is ReloadObservation reload)
            {
                PublishReloadedSnapshot(snapshot, reload);
            }
            else if (snapshot.StateIssue == VersionManagerStateLoadIssue.Unavailable)
            {
                _ = PublishStateUnavailable();
            }
            else if (_current is not { } current)
            {
                return;
            }
            else if (snapshot.InventoryIssue != ManagedVersionInventoryReadIssue.None)
            {
                PreserveRecoverableSourceAuthority();
                _current = current with
                {
                    State = snapshot.State,
                    Inventory = snapshot.Inventory,
                    InventoryIssue = snapshot.InventoryIssue,
                    Catalog = null,
                    VerifiedCandidate = null,
                    SourceStatus = VersionSourceStatus.Offline,
                    CatalogIssue = null,
                    Generation = 0,
                    ShouldPromptForUpdate = false,
                };
            }
            else
            {
                _current = current with
                {
                    State = snapshot.State,
                    Inventory = snapshot.Inventory,
                    StateIssue = snapshot.StateIssue,
                    InventoryIssue = snapshot.InventoryIssue,
                };
            }
            if (_current is { } published) { observation?.Record(published); }
        }
    }

    private MutationObservation ObserveMutation() => new(_services.MutationObservation);

    private sealed class MutationObservation : IDisposable
    {
        private readonly AsyncLocal<MutationObservation?> _slot;
        private readonly MutationObservation? _prior;
        // Guard: the coordinator's _mutation writes the receipt before completing this invocation.
        private VersionManagementSnapshot? _snapshot;

        internal MutationObservation(AsyncLocal<MutationObservation?> slot)
        {
            _slot = slot;
            _prior = slot.Value;
            slot.Value = this;
        }

        internal VersionManagementSnapshot Snapshot => _snapshot ??
            throw new InvalidOperationException("Version-manager state is invalid and requires recovery.");

        internal bool HasSnapshot => _snapshot is not null;

        internal void Record(VersionManagementSnapshot snapshot) => _snapshot = snapshot;

        public void Dispose() => _slot.Value = _prior;
    }

    private sealed record ReloadObservation(VersionManagementSnapshot? Prior, bool Recovering);

    private void PublishReloadedSnapshot(ManagedMutationSnapshot snapshot, ReloadObservation observation)
    {
        bool recovering = observation.Recovering;
        VersionManagementSnapshot? prior = observation.Prior;
        VersionManagerState? state = snapshot.State;
        bool sameSource = state is not null && snapshot.InventoryIssue == ManagedVersionInventoryReadIssue.None &&
            prior?.State?.UpdateSource == state.UpdateSource;
        bool sameContext = sameSource && prior?.State?.ActiveVersion == state?.ActiveVersion;
        _current = new(
            state, snapshot.Inventory, sameSource ? prior?.Catalog : null,
            sameContext && !recovering ? prior?.VerifiedCandidate : null,
            sameSource && prior is not null && !recovering ? prior.SourceStatus
                : state?.UpdateSource is null && state is not null
                    ? VersionSourceStatus.NotConfigured : VersionSourceStatus.Offline,
            sameSource && !recovering ? prior?.CatalogIssue : null,
            sameContext && !recovering ? prior?.Generation ?? 0 : 0,
            sameContext && !recovering && prior?.ShouldPromptForUpdate == true,
            snapshot.StateIssue, snapshot.InventoryIssue,
            state?.SourceRegistryState?.IsManualPin == true ? VersionRegistryStatus.ManualPin
                : sameSource ? prior?.RegistryStatus ?? VersionRegistryStatus.NotConfigured
                    : VersionRegistryStatus.NotConfigured,
            sameSource ? prior?.RegistryIssue ?? UpdateSourceRegistryIssue.None : UpdateSourceRegistryIssue.None);
        if ((state is not null && snapshot.InventoryIssue == ManagedVersionInventoryReadIssue.None) ||
            snapshot.StateIssue is VersionManagerStateLoadIssue.ManagedRootMismatch or VersionManagerStateLoadIssue.Invalid)
        {
            _recoverableSourceAuthority = null;
        }
    }

    private async ValueTask<VersionManagementSnapshot> RequireCurrentAsync(
        ManagedMutationCoordinator.SourceTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_generationSync)
        {
            if (_current is { } current && HasUsableAuthority(current)) { return current; }
        }
        if (!await transaction.AcquireWriterAsync(cancellationToken).ConfigureAwait(false))
        {
            _ = transaction.PublishStateUnavailable();
            return PublishStateUnavailable();
        }
        _ = await transaction.ReloadAsync(cancellationToken).ConfigureAwait(false);
        return PublishedSnapshot();
    }

    private VersionManagementSnapshot PublishStateUnavailable()
    {
        lock (_generationSync)
        {
            PreserveRecoverableSourceAuthority();
            _current = _current is { } current ? current with
            {
                Inventory = ManagedVersionInventory.Create([]),
                Catalog = null,
                VerifiedCandidate = null,
                SourceStatus = VersionSourceStatus.Offline,
                CatalogIssue = null,
                Generation = 0,
                ShouldPromptForUpdate = false,
                StateIssue = VersionManagerStateLoadIssue.Unavailable,
                InventoryIssue = ManagedVersionInventoryReadIssue.Unavailable,
            } : new(null, ManagedVersionInventory.Create([]), null, null, VersionSourceStatus.Offline,
                null, 0, false, VersionManagerStateLoadIssue.Unavailable, ManagedVersionInventoryReadIssue.Unavailable);
            return _current;
        }
    }

    private void PreserveRecoverableSourceAuthority()
    {
        if (_current is { } current && HasUsableAuthority(current)) { _recoverableSourceAuthority = current; }
    }

    private static bool HasUsableAuthority(VersionManagementSnapshot snapshot) =>
        snapshot.StateIssue == VersionManagerStateLoadIssue.None &&
        snapshot.InventoryIssue == ManagedVersionInventoryReadIssue.None;

    private VersionManagementSnapshot PublishedSnapshot()
    {
        lock (_generationSync) { return RequirePublishedSnapshotUnderLock(); }
    }

    private VersionManagementSnapshot RequirePublishedSnapshotUnderLock() =>
        _current ?? throw new InvalidOperationException("Version-manager state is invalid and requires recovery.");

    private DiscoveryCheck BeginCheckUnderLock(VersionManagementSnapshot current, bool registry, CancellationToken token)
    {
        long generation = _session.BeginCheck();
        CancelRunningCheckUnderLock();
        DiscoveryCheck owned;
        CancellationTokenSource? cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            owned = new(cancellation, generation, current, cancellation.Token);
            _activeCheck = owned;
            cancellation = null;
        }
        finally { cancellation?.Dispose(); }
        _current = registry ? current with
        {
            SourceStatus = VersionSourceStatus.Checking,
            CatalogIssue = null,
            Generation = generation,
            ShouldPromptForUpdate = false,
        } : current with
        {
            SourceStatus = VersionSourceStatus.Checking,
            Generation = generation,
            ShouldPromptForUpdate = false,
        };
        return owned;
    }

    private void SupersedeRunningCheck()
    {
        lock (_generationSync)
        {
            _ = _session.BeginCheck();
            // Unlike the frozen cancel-first order, restore before cancellation: it can complete
            // the interrupted check inline, whose caller must see the restored source status.
            if (_current?.SourceStatus == VersionSourceStatus.Checking)
            {
                _current = _current with
                {
                    SourceStatus = ResolveInterruptedSourceStatus(_current),
                };
            }
            CancelRunningCheckUnderLock();
        }
    }

    private void CancelRunningCheckUnderLock()
    {
        _activeCheck?.Cancellation.Cancel();
        _activeCheck?.Cancellation.Dispose();
        _activeCheck = null;
    }

    private bool OwnsCheck(DiscoveryCheck check)
    {
        lock (_generationSync) { return ReferenceEquals(_activeCheck?.Cancellation, check.Cancellation); }
    }

    private void CompleteOwnedCheckUnderLock(DiscoveryCheck check)
    {
        if (ReferenceEquals(_activeCheck?.Cancellation, check.Cancellation))
        {
            _activeCheck = null;
            check.Cancellation.Dispose();
        }
    }

    private void RestoreCancelledCheck(DiscoveryCheck check)
    {
        lock (_generationSync)
        {
            if (ReferenceEquals(_activeCheck?.Cancellation, check.Cancellation))
            {
                _current = check.Prior;
                CompleteOwnedCheckUnderLock(check);
            }
        }
    }

    internal UpdateCatalogVersionSnapshot? SelectPackage(VersionManagerState state, ManagedAppVersion version)
    {
        lock (_generationSync)
        {
            if (_current is not { } current || !HasUsableAuthority(current) ||
                current.State is not { } observedState || observedState.UpdateSource != state.UpdateSource ||
                observedState.ActiveVersion != state.ActiveVersion)
            {
                return null;
            }
            UpdateCatalogVersionSnapshot? package = current.Catalog?.Versions.SingleOrDefault(row => row.Version == version);
            if (package is not null) { UpdateCatalogSnapshot.ValidatePackage(_services.Limits, package); }
            return package;
        }
    }
}
