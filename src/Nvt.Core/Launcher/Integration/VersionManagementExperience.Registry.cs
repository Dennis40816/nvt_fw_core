// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Launcher.Integration;

public sealed partial class VersionManagementExperience
{
    /// <inheritdoc />
    public async ValueTask<VersionManagementSnapshot> ResumeRegistryAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_services.SourceRegistry is null)
        {
            return await _coordinator.RunSourceTransactionAsync(false, async (transaction, token) =>
            {
                VersionManagementSnapshot current = await RequireCurrentAsync(transaction, token).ConfigureAwait(false);
                lock (_generationSync)
                {
                    _current = current with
                    {
                        RegistryStatus = VersionRegistryStatus.NotConfigured,
                        RegistryIssue = UpdateSourceRegistryIssue.NotConfigured,
                    };
                    return _current;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        return await CheckRegistryAsync(false, true, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException("Configured registry resolution returned no result.");
    }

    private async ValueTask<VersionManagementSnapshot?> CheckRegistryAsync(
        bool isAutomatic, bool allowManualPin, CancellationToken cancellationToken)
    {
        IUpdateSourceRegistry registry = _services.SourceRegistry ??
            throw new InvalidOperationException("Registry resolution requires a configured port.");
        CheckStart started = await _coordinator.RunSourceTransactionAsync(false,
            (transaction, token) => BeginRegistryCheckAsync(transaction, allowManualPin, token), cancellationToken).ConfigureAwait(false);
        if (started.Check is not { } check) { return started.Snapshot; }
        try
        {
            UpdateSourceRegistryLoadResult loaded = await LoadRegistryAsync(registry, check.Token).ConfigureAwait(false);
            if (!loaded.IsSuccess)
            {
                return PublishRegistryFailure(check.Prior, check, ResolveLoadStatus(loaded.Issue), ResolveLoadIssue(loaded.Issue));
            }
            UpdateSourceRegistrySnapshot snapshot = loaded.Snapshot!;
            if (ValidateAntiRollback(check.Prior.State!.SourceRegistryState, snapshot) is { } issue)
            {
                return PublishRegistryFailure(check.Prior, check, VersionRegistryStatus.Rejected, issue);
            }
            RegistrySelection selection = await SelectRegistryCandidateAsync(snapshot, check, isAutomatic).ConfigureAwait(false);
            if (selection.Package is null && selection.AuthorityCatalog is null)
            {
                bool deprecated = check.Prior.State!.UpdateSource is { } prior && snapshot.Entries.Any(entry =>
                    entry.Status == UpdateSourceRegistryEntryStatus.Deprecated && SourcePathEquals(entry.SourceRoot, prior));
                return PublishRegistryFailure(check.Prior, check,
                    deprecated ? VersionRegistryStatus.DeprecatedRetained : VersionRegistryStatus.Exhausted,
                    deprecated ? UpdateSourceRegistryIssue.CurrentSourceDeprecated : UpdateSourceRegistryIssue.CandidatesExhausted);
            }
            return await _coordinator.RunSourceTransactionAsync(false,
                (transaction, token) => ReadmitRegistryAsync(transaction, registry, snapshot, selection,
                    check, isAutomatic, allowManualPin, token), check.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (check.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return PublishedSnapshot() with { RegistryIssue = UpdateSourceRegistryIssue.Superseded };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreCancelledCheck(check);
            throw;
        }
    }

    private async ValueTask<CheckStart> BeginRegistryCheckAsync(
        ManagedMutationCoordinator.SourceTransaction transaction, bool allowManualPin, CancellationToken cancellationToken)
    {
        VersionManagementSnapshot initial = await RequireCurrentAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!HasUsableAuthority(initial) || initial.State is null) { return new(null, initial); }
        lock (_generationSync)
        {
            if (initial.State.SourceRegistryState?.IsManualPin == true && !allowManualPin)
            {
                _current = initial with { RegistryStatus = VersionRegistryStatus.ManualPin, RegistryIssue = UpdateSourceRegistryIssue.None };
                return new(null, null);
            }
            return new(BeginCheckUnderLock(initial, registry: true, cancellationToken), null);
        }
    }

    private async ValueTask<RegistrySelection> SelectRegistryCandidateAsync(
        UpdateSourceRegistrySnapshot snapshot, DiscoveryCheck check, bool isAutomatic)
    {
        ManagedAppVersion active = check.Prior.State!.ActiveVersion ?? _services.CurrentAppVersion;
        foreach (UpdateSourceRegistryEntry entry in snapshot.AutomaticCandidates())
        {
            RegistryCandidateInspection inspection = isAutomatic
                ? await InspectAutomaticCandidateAsync(entry, snapshot.CatalogPublication, active, check.Token).ConfigureAwait(false)
                : await InspectCandidateAsync(entry, snapshot.CatalogPublication, check.Token).ConfigureAwait(false);
            if (inspection.Admission is { } admitted) { return new(admitted, null, null); }
            if (isAutomatic && inspection.Catalog is { } catalog && catalog.FindNewestNotifyNewerThan(active) is null)
            {
                return new(null, entry, catalog);
            }
        }
        return new(null, null, null);
    }

    private async ValueTask<VersionManagementSnapshot> ReadmitRegistryAsync(
        ManagedMutationCoordinator.SourceTransaction transaction, IUpdateSourceRegistry registry,
        UpdateSourceRegistrySnapshot snapshot, RegistrySelection selection, DiscoveryCheck check,
        bool isAutomatic, bool allowManualPin, CancellationToken cancellationToken)
    {
        if (!OwnsCheck(check)) { return PublishSuperseded(check); }
        if (!await transaction.AcquireWriterAsync(cancellationToken).ConfigureAwait(false))
        {
            return PublishRegistryStateUnavailable(check.Prior, check);
        }
        _ = await transaction.ReloadAsync(cancellationToken).ConfigureAwait(false);
        VersionManagementSnapshot durable = PublishedSnapshot();
        if (!HasUsableAuthority(durable) || durable.State is null ||
            !transaction.MatchesDurableSnapshot(check.Prior.State!) ||
            (durable.State.SourceRegistryState?.IsManualPin == true && !allowManualPin) ||
            await transaction.LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            return PublishRegistryStateUnavailable(durable, check);
        }
        UpdateSourceRegistryLoadResult reloaded = await LoadRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
        if (!reloaded.IsSuccess || reloaded.Snapshot!.RegistryRevision != snapshot.RegistryRevision ||
            !string.Equals(reloaded.Snapshot!.ContentDigest, snapshot.ContentDigest, StringComparison.Ordinal))
        {
            return PublishRegistryFailure(durable, check, VersionRegistryStatus.Rejected, UpdateSourceRegistryIssue.RegistryChanged);
        }
        UpdateSourceRegistrySnapshot reloadedSnapshot = reloaded.Snapshot!;
        if (ValidateAntiRollback(durable.State.SourceRegistryState, reloadedSnapshot) is { } issue)
        {
            return PublishRegistryFailure(durable, check, VersionRegistryStatus.Rejected, issue);
        }
        UpdateSourceRegistryEntry selectedEntry = selection.Package?.Entry ?? selection.AuthorityEntry!;
        UpdateSourceRegistryEntry? reloadedEntry = reloadedSnapshot.AutomaticCandidates().SingleOrDefault(entry =>
            entry.Status == selectedEntry.Status && SourcePathEquals(entry.CatalogPath, selectedEntry.CatalogPath));
        return selection.Package is null
            ? await CommitRegistryAuthorityAsync(transaction, durable, snapshot, reloadedSnapshot,
                reloadedEntry, selection, check, cancellationToken).ConfigureAwait(false)
            : await CommitRegistryPackageAsync(transaction, durable, snapshot, reloadedSnapshot,
                reloadedEntry, selection.Package, check, isAutomatic, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<VersionManagementSnapshot> CommitRegistryAuthorityAsync(
        ManagedMutationCoordinator.SourceTransaction transaction, VersionManagementSnapshot durable,
        UpdateSourceRegistrySnapshot snapshot, UpdateSourceRegistrySnapshot reloaded,
        UpdateSourceRegistryEntry? entry, RegistrySelection selection, DiscoveryCheck check, CancellationToken cancellationToken)
    {
        ManagedAppVersion active = check.Prior.State!.ActiveVersion ?? _services.CurrentAppVersion;
        RegistryCandidateInspection readmission = entry is null ? new(default!, null, null)
            : await InspectCatalogPublicationAsync(entry, reloaded.CatalogPublication, cancellationToken).ConfigureAwait(false);
        if (readmission.Catalog is not { } catalog || readmission.Admission is not null ||
            catalog.FindNewestNotifyNewerThan(active) is not null || !CatalogPublicationEquals(selection.AuthorityCatalog!, catalog))
        {
            return PublishRegistryFailure(durable, check, VersionRegistryStatus.Rejected, UpdateSourceRegistryIssue.RegistryChanged);
        }
        if (await transaction.LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            return PublishRegistryStateUnavailable(durable, check);
        }
        var accepted = new VersionSourceRegistryState(snapshot.RegistryRevision, snapshot.ContentDigest, false);
        VersionManagerState basis = durable.State!;
        bool maySave = CanPersistAllManualRegistryAuthority(basis, selection.AuthorityEntry!.SourceRoot);
        bool changed = maySave && basis.SourceRegistryState != accepted;
        VersionManagerState state = changed ? basis.WithUpdateSource(basis.UpdateSource!, accepted) : basis;
        if (!await transaction.TrySaveAsync(basis, state, changed, cancellationToken).ConfigureAwait(false))
        {
            return PublishRegistryStateUnavailable(durable, check);
        }
        lock (_generationSync)
        {
            _current = durable with
            {
                State = state,
                Catalog = null,
                VerifiedCandidate = null,
                SourceStatus = VersionSourceStatus.Connected,
                CatalogIssue = UpdateCatalogLoadIssue.None,
                Generation = check.Generation,
                ShouldPromptForUpdate = false,
                RegistryStatus = selection.AuthorityEntry.Status == UpdateSourceRegistryEntryStatus.Latest
                    ? VersionRegistryStatus.LatestSelected : VersionRegistryStatus.FallbackSelected,
                RegistryIssue = UpdateSourceRegistryIssue.None,
            };
            CompleteOwnedCheckUnderLock(check);
            return _current;
        }
    }

    private async ValueTask<VersionManagementSnapshot> CommitRegistryPackageAsync(
        ManagedMutationCoordinator.SourceTransaction transaction, VersionManagementSnapshot durable,
        UpdateSourceRegistrySnapshot snapshot, UpdateSourceRegistrySnapshot reloaded, UpdateSourceRegistryEntry? entry,
        RegistryCandidateAdmission selected, DiscoveryCheck check, bool isAutomatic, CancellationToken cancellationToken)
    {
        ManagedAppVersion active = check.Prior.State!.ActiveVersion ?? _services.CurrentAppVersion;
        RegistryCandidateAdmission? readmitted = entry is null ? null : (isAutomatic
            ? await InspectAutomaticCandidateAsync(entry, reloaded.CatalogPublication, active, cancellationToken).ConfigureAwait(false)
            : await InspectCandidateAsync(entry, reloaded.CatalogPublication, cancellationToken).ConfigureAwait(false)).Admission;
        if (readmitted is null || !CatalogPublicationEquals(selected.Catalog, readmitted.Catalog) ||
            selected.NewestPackage.Identity != readmitted.NewestPackage.Identity || selected.VerifiedCandidate != readmitted.VerifiedCandidate)
        {
            return PublishRegistryFailure(durable, check, VersionRegistryStatus.Rejected, UpdateSourceRegistryIssue.RegistryChanged);
        }
        if (await transaction.LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            return PublishRegistryStateUnavailable(durable, check);
        }
        var accepted = new VersionSourceRegistryState(snapshot.RegistryRevision, snapshot.ContentDigest, false);
        VersionManagerState basis = durable.State!;
        VersionManagerState state = basis.WithUpdateSource(selected.Entry.SourceRoot, accepted);
        bool changed = !SourcePathEquals(basis.UpdateSource, state.UpdateSource) || basis.SourceRegistryState != accepted;
        if (!await transaction.TrySaveAsync(basis, state, changed, cancellationToken).ConfigureAwait(false))
        {
            return PublishRegistryStateUnavailable(durable, check);
        }
        lock (_generationSync)
        {
            active = state.ActiveVersion ?? _services.CurrentAppVersion;
            VerifiedUpdateCandidate? visible = selected.NewestPackage.Version > active ? selected.VerifiedCandidate : null;
            bool prompt = isAutomatic && visible is not null && _session.TryPublishAutomaticPrompt(check.Generation, active, visible);
            _current = durable with
            {
                State = changed ? state : durable.State,
                Catalog = isAutomatic && visible is not null ? UpdateCatalogSnapshot.Create(_services.Limits, [selected.NewestPackage]) : selected.Catalog,
                VerifiedCandidate = visible,
                SourceStatus = VersionSourceStatus.Connected,
                CatalogIssue = UpdateCatalogLoadIssue.None,
                Generation = check.Generation,
                ShouldPromptForUpdate = prompt,
                RegistryStatus = selected.Entry.Status == UpdateSourceRegistryEntryStatus.Latest
                    ? VersionRegistryStatus.LatestSelected : VersionRegistryStatus.FallbackSelected,
                RegistryIssue = UpdateSourceRegistryIssue.None,
            };
            CompleteOwnedCheckUnderLock(check);
            return _current;
        }
    }

    private async ValueTask<UpdateSourceRegistryLoadResult> LoadRegistryAsync(
        IUpdateSourceRegistry registry, CancellationToken cancellationToken)
    {
        UpdateSourceRegistryLoadResult loaded = await registry.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess) { return loaded; }
        UpdateSourceRegistrySnapshot snapshot = loaded.Snapshot!;
        try
        {
            _ = UpdateSourceRegistrySnapshot.Create(_services.Limits, _services.Descriptor, snapshot.RegistryId,
                snapshot.RegistryRevision, snapshot.PublishedAtUtc, snapshot.CatalogPublication, snapshot.ContentDigest, snapshot.Entries);
            return loaded;
        }
        catch (ArgumentException) { return new(null, UpdateSourceRegistryLoadIssue.InvalidManifest, loaded.Replicas); }
    }

    private VersionManagementSnapshot PublishRegistryFailure(
        VersionManagementSnapshot basis, DiscoveryCheck check, VersionRegistryStatus status, UpdateSourceRegistryIssue issue)
    {
        lock (_generationSync)
        {
            if (!OwnsCheck(check)) { return RequirePublishedSnapshotUnderLock(); }
            _current = basis with
            {
                Catalog = null,
                VerifiedCandidate = null,
                SourceStatus = basis.State?.UpdateSource is null ? VersionSourceStatus.NotConfigured : VersionSourceStatus.Offline,
                CatalogIssue = null,
                Generation = check.Generation,
                ShouldPromptForUpdate = false,
                RegistryStatus = status,
                RegistryIssue = issue,
            };
            CompleteOwnedCheckUnderLock(check);
            return _current;
        }
    }

    private VersionManagementSnapshot PublishRegistryStateUnavailable(VersionManagementSnapshot basis, DiscoveryCheck? check = null)
    {
        _ = basis;
        lock (_generationSync)
        {
            VersionManagementSnapshot unavailable = PublishStateUnavailable();
            _current = unavailable with { RegistryStatus = VersionRegistryStatus.Unavailable, RegistryIssue = UpdateSourceRegistryIssue.StateUnavailable };
            if (check is not null) { CompleteOwnedCheckUnderLock(check); }
            return _current;
        }
    }

    private VersionManagementSnapshot PublishSuperseded(DiscoveryCheck check)
    {
        lock (_generationSync)
        {
            CompleteOwnedCheckUnderLock(check);
            return RequirePublishedSnapshotUnderLock() with { RegistryIssue = UpdateSourceRegistryIssue.Superseded };
        }
    }

    private sealed record RegistrySelection(
        RegistryCandidateAdmission? Package, UpdateSourceRegistryEntry? AuthorityEntry, UpdateCatalogSnapshot? AuthorityCatalog);
}
