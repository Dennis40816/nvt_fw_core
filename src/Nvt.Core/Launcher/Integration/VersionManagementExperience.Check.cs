// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Launcher.Integration;

public sealed partial class VersionManagementExperience
{
    /// <inheritdoc />
    public async ValueTask<VersionManagementSnapshot> CheckAsync(bool isAutomatic, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_services.SourceRegistry is not null)
        {
            VersionManagementSnapshot? registryResult = await CheckRegistryAsync(
                isAutomatic, allowManualPin: false, cancellationToken).ConfigureAwait(false);
            if (registryResult is not null) { return registryResult; }
        }
        CheckStart started = await _coordinator.RunSourceTransactionAsync(
            false, BeginConfiguredCheckAsync, cancellationToken).ConfigureAwait(false);
        if (started.Check is not { } check) { return started.Snapshot!; }
        try
        {
            VersionManagerState state = check.Prior.State!;
            string source = state.UpdateSource!;
            UpdateCatalogLoadResult loaded = await LoadCatalogAsync(source, check.Token).ConfigureAwait(false);
            UpdateCatalogVersionSnapshot? selected = null;
            VerifiedUpdateCandidate? verified = null;
            if (loaded.IsSuccess)
            {
                ManagedAppVersion active = state.ActiveVersion ?? _services.CurrentAppVersion;
                selected = isAutomatic ? loaded.Snapshot!.FindNewestNotifyNewerThan(active)
                    : loaded.Snapshot!.FindNewestNewerThan(active);
                if (selected is not null)
                {
                    ManagedPackageVerificationResult verification = await _services.Repository.VerifyPackageAsync(
                        source, selected, check.Token).ConfigureAwait(false);
                    verified = verification is { IsVerified: true, Candidate: { } candidate } &&
                        candidate.Version == selected.Version &&
                        string.Equals(candidate.AdmissionIdentity, selected.Identity, StringComparison.Ordinal)
                            ? candidate : null;
                }
            }
            return PublishConfiguredCheck(check, loaded, selected, verified, isAutomatic);
        }
        catch (OperationCanceledException) when (check.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return PublishedSnapshot();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreCancelledCheck(check);
            throw;
        }
    }

    private async ValueTask<CheckStart> BeginConfiguredCheckAsync(
        ManagedMutationCoordinator.SourceTransaction transaction, CancellationToken cancellationToken)
    {
        VersionManagementSnapshot current = await RequireCurrentAsync(transaction, cancellationToken).ConfigureAwait(false);
        lock (_generationSync)
        {
            if (_services.SourceRegistry is null && current.State?.SourceRegistryState is { IsManualPin: false })
            {
                CancelRunningCheckUnderLock();
                _current = current with
                {
                    Catalog = null,
                    VerifiedCandidate = null,
                    SourceStatus = VersionSourceStatus.NotConfigured,
                    CatalogIssue = null,
                    ShouldPromptForUpdate = false,
                    RegistryStatus = VersionRegistryStatus.NotConfigured,
                    RegistryIssue = UpdateSourceRegistryIssue.NotConfigured,
                };
                return new(null, _current);
            }
            return !HasUsableAuthority(current) || current.State?.UpdateSource is null
                ? new(null, current) : new(BeginCheckUnderLock(current, registry: false, cancellationToken), null);
        }
    }

    private VersionManagementSnapshot PublishConfiguredCheck(
        DiscoveryCheck check, UpdateCatalogLoadResult loaded, UpdateCatalogVersionSnapshot? selected,
        VerifiedUpdateCandidate? verified, bool isAutomatic)
    {
        lock (_generationSync)
        {
            if (!OwnsCheck(check)) { return RequirePublishedSnapshotUnderLock(); }
            VersionManagementSnapshot prior = check.Prior;
            bool prompt = isAutomatic && verified is not null && _session.TryPublishAutomaticPrompt(
                check.Generation, prior.State!.ActiveVersion ?? _services.CurrentAppVersion, verified);
            VersionSourceStatus status = loaded.IsSuccess && selected is null
                ? VersionSourceStatus.Connected : ResolveSourceStatus(loaded, verified, prior);
            UpdateCatalogSnapshot? catalog = isAutomatic
                ? verified is not null && selected is not null ? UpdateCatalogSnapshot.Create(_services.Limits, [selected]) : null
                : loaded.Snapshot;
            _current = new(prior.State, prior.Inventory, catalog, verified, status, loaded.Issue,
                check.Generation, prompt, prior.StateIssue, prior.InventoryIssue,
                prior.State?.SourceRegistryState?.IsManualPin == true ? VersionRegistryStatus.ManualPin : prior.RegistryStatus,
                prior.RegistryIssue);
            CompleteOwnedCheckUnderLock(check);
            return _current;
        }
    }

    private async ValueTask<UpdateCatalogLoadResult> LoadCatalogAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        UpdateCatalogLoadResult loaded = await _services.CatalogSource.LoadAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        return ReadmitCatalog(loaded);
    }

    private UpdateCatalogLoadResult ReadmitCatalog(UpdateCatalogLoadResult loaded)
    {
        // Typed failures keep their frozen issue and cannot authorize verification.
        if (loaded.Snapshot is not { } catalog) { return loaded; }
        try
        {
            _ = UpdateCatalogSnapshot.Create(_services.Limits, catalog.Versions);
            return loaded;
        }
        catch (ArgumentException)
        {
            return new(null, UpdateCatalogLoadIssue.InvalidManifest);
        }
    }

    /// <inheritdoc />
    public async ValueTask<VersionManagementSnapshot> CommitUpdateSourceAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ThrowIfDisposed();
        VersionManagementSnapshot? failure = await _coordinator.RunSourceTransactionAsync<VersionManagementSnapshot?>(
            true, (transaction, token) => CommitManualSourceAsync(transaction, sourceRoot, token), cancellationToken).ConfigureAwait(false);
        return failure ?? await CheckAsync(isAutomatic: false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<VersionManagementSnapshot?> CommitManualSourceAsync(
        ManagedMutationCoordinator.SourceTransaction transaction, string sourceRoot, CancellationToken cancellationToken)
    {
        if (!await transaction.AcquireWriterAsync(cancellationToken).ConfigureAwait(false) ||
            await transaction.LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            _ = transaction.PublishStateUnavailable();
            return PublishStateUnavailable();
        }
        _ = await transaction.ReloadAsync(cancellationToken).ConfigureAwait(false);
        VersionManagementSnapshot current = PublishedSnapshot();
        if (current.State is not { } state) { return current; }
        bool registryAware = _services.SourceRegistry is not null || state.SourceRegistryState is not null;
        if (registryAware && await transaction.LoadClearLauncherFenceAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            return PublishRegistryStateUnavailable(current);
        }
        VersionSourceRegistryState? registryState = registryAware
            ? new(state.SourceRegistryState?.AcceptedRevision ?? 0, state.SourceRegistryState?.AcceptedDigest, true) : null;
        string committedSource = registryAware
            ? VersionManagerState.NormalizeRegistrySource(sourceRoot, requireAlreadyNormalized: false) : sourceRoot;
        VersionManagerState proposed = state.WithUpdateSource(committedSource, registryState);
        if (!await transaction.TrySaveAsync(state, proposed, true, cancellationToken).ConfigureAwait(false))
        {
            return registryAware ? PublishRegistryStateUnavailable(current) : PublishStateUnavailable();
        }
        lock (_generationSync)
        {
            _current = current with
            {
                State = proposed,
                Catalog = null,
                VerifiedCandidate = null,
                SourceStatus = VersionSourceStatus.Offline,
                CatalogIssue = null,
                ShouldPromptForUpdate = false,
                RegistryStatus = registryAware ? VersionRegistryStatus.ManualPin : VersionRegistryStatus.NotConfigured,
                RegistryIssue = UpdateSourceRegistryIssue.None,
            };
        }
        return null;
    }

    private VersionSourceStatus ResolveSourceStatus(
        UpdateCatalogLoadResult loaded, VerifiedUpdateCandidate? verified, VersionManagementSnapshot current)
    {
        if (loaded.IsSuccess)
        {
            bool newer = loaded.Snapshot!.FindNewestNewerThan(current.State!.ActiveVersion ?? _services.CurrentAppVersion) is not null;
            return verified is null && newer ? VersionSourceStatus.Invalid : VersionSourceStatus.Connected;
        }
        return ResolveCatalogIssueStatus(loaded.Issue);
    }

    private VersionSourceStatus ResolveInterruptedSourceStatus(VersionManagementSnapshot snapshot)
    {
        if (snapshot.State?.UpdateSource is null) { return VersionSourceStatus.NotConfigured; }
        if (snapshot.Catalog is not null)
        {
            bool newer = snapshot.Catalog.FindNewestNewerThan(snapshot.State.ActiveVersion ?? _services.CurrentAppVersion) is not null;
            return snapshot.VerifiedCandidate is null && newer ? VersionSourceStatus.Invalid : VersionSourceStatus.Connected;
        }
        return snapshot.CatalogIssue is { } issue ? ResolveCatalogIssueStatus(issue) : VersionSourceStatus.Offline;
    }

    private static VersionSourceStatus ResolveCatalogIssueStatus(UpdateCatalogLoadIssue issue) => issue switch
    {
        UpdateCatalogLoadIssue.SourceMissing or UpdateCatalogLoadIssue.SourceUnavailable => VersionSourceStatus.Offline,
        UpdateCatalogLoadIssue.PermissionDenied => VersionSourceStatus.PermissionDenied,
        _ => VersionSourceStatus.Invalid,
    };
}
