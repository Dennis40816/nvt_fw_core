// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Integration;

/// <summary>Composes discovery and the existing managed-version mutation owner.</summary>
public sealed partial class VersionManagementExperience : IVersionManagementExperience, IDisposable
{
    private readonly ExperienceServices _services;
    private readonly ManagedMutationCoordinator _coordinator;
    private readonly Lock _generationSync = new();
    private readonly VersionDiscoverySession _session = new();
    // Guard: _generationSync protects discovery ownership and all published source observations.
    private DiscoveryCheck? _activeCheck;
    private VersionManagementSnapshot? _current;
    private VersionManagementSnapshot? _recoverableSourceAuthority;
    // Guard: Dispose writes this under _generationSync; ThrowIfDisposed reads it with Volatile and takes no lock,
    // as the frozen helper takes none, so a query can never wait behind a cancellation callback.
    private bool _disposed;

    /// <summary>Creates a use-case graph with explicit product admission and retention ports.</summary>
    /// <param name="currentAppVersion">Running application version.</param>
    /// <param name="managedRoot">Stable launcher-owned root.</param>
    /// <param name="statePath">Exact application-state writer path.</param>
    /// <param name="stateStore">Atomic application-state port.</param>
    /// <param name="catalogSource">Product-admitted catalog port.</param>
    /// <param name="repository">Managed payload repository using the supplied product adapters.</param>
    /// <param name="launcherFence">Launcher journal fence sharing the application writer.</param>
    /// <param name="descriptor">Product identity and runtime names.</param>
    /// <param name="limits">Mandatory positive normalized admission ceilings.</param>
    /// <param name="retention">Product retention advice.</param>
    /// <param name="sourceRegistry">Optional fixed-registry port.</param>
    public VersionManagementExperience(
        ManagedAppVersion currentAppVersion, string managedRoot, string statePath,
        IVersionManagerStateStore stateStore, IUpdateCatalogSource catalogSource,
        IManagedVersionRepository repository, ILauncherMutationFence launcherFence,
        ProductDescriptor descriptor, VersionManagementLimits limits, IManagedRetentionPolicy retention,
        IUpdateSourceRegistry? sourceRegistry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        string root = ManagedRootPathIdentity.Normalize(managedRoot);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(catalogSource);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(launcherFence);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(retention);
        _services = new(currentAppVersion, stateStore, catalogSource, repository,
            sourceRegistry, descriptor, limits);
        _coordinator = new(root, statePath, stateStore, repository, launcherFence,
            new ExperiencePackageSelection(this), retention);
        _coordinator.SupersedeRunningCheck = SupersedeRunningCheck;
        _coordinator.CaptureExperienceReload = CaptureReloadObservation;
        _coordinator.ObserveExperienceSnapshot = ObserveCoordinatorSnapshot;
    }

    /// <inheritdoc />
    public async ValueTask<VersionManagementSnapshot> InitializeAsync(
        CancellationToken cancellationToken, bool isReadOnly = false)
    {
        ThrowIfDisposed();
        using var observation = ObserveMutation();
        _ = await _coordinator.InitializeAsync(cancellationToken, isReadOnly).ConfigureAwait(false);
        return observation.Snapshot;
    }

    /// <inheritdoc />
    public async ValueTask<VersionManagementSnapshot> InitializeAfterManagedReadyAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var observation = ObserveMutation();
        _ = await _coordinator.InitializeAfterManagedReadyAsync(cancellationToken).ConfigureAwait(false);
        return observation.Snapshot;
    }

    /// <inheritdoc />
    public async ValueTask<VersionInstallOperationResult> InstallAsync(
        ManagedAppVersion version, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var observation = ObserveMutation();
        Coordination.VersionInstallOperationResult installed = await _coordinator.InstallAsync(
            version, cancellationToken).ConfigureAwait(false);
        return new(installed.Install, observation.Snapshot);
    }

    /// <inheritdoc />
    public async ValueTask<VersionDeleteOperationResult> DeleteAsync(
        ManagedAppVersion version, bool rollbackLossConfirmed, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var observation = ObserveMutation();
        Coordination.VersionDeleteOperationResult deleted = await _coordinator.DeleteAsync(
            version, rollbackLossConfirmed, cancellationToken).ConfigureAwait(false);
        return new(deleted.Decision, deleted.OperationIssue, deleted.RepositoryIssue, observation.Snapshot);
    }

    /// <inheritdoc />
    public async ValueTask<VersionManagementSnapshot> AcknowledgeRetentionReviewAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var observation = ObserveMutation();
        _ = await _coordinator.AcknowledgeRetentionReviewAsync(cancellationToken).ConfigureAwait(false);
        return observation.Snapshot;
    }

    /// <inheritdoc />
    public async ValueTask<VersionManagerState> PrepareActivationAsync(
        ManagedAppVersion version, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var observation = ObserveMutation();
        return await _coordinator.PrepareActivationAsync(version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<VersionManagementSnapshot> CancelPendingActivationAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var observation = ObserveMutation();
        _ = await _coordinator.CancelPendingActivationAsync(cancellationToken).ConfigureAwait(false);
        return observation.Snapshot;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_generationSync)
        {
            if (_disposed) { return; }
            CancelRunningCheckUnderLock();
            _coordinator.Dispose();
            Volatile.Write(ref _disposed, true);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
    }

    private sealed record ExperienceServices(
        ManagedAppVersion CurrentAppVersion, IVersionManagerStateStore StateStore,
        IUpdateCatalogSource CatalogSource, IManagedVersionRepository Repository,
        IUpdateSourceRegistry? SourceRegistry, ProductDescriptor Descriptor, VersionManagementLimits Limits)
    {
        // Guard: ExecutionContext scopes each operation; _mutation records its receipt before release.
        internal AsyncLocal<MutationObservation?> MutationObservation { get; } = new();
    }

    private sealed record DiscoveryCheck(
        CancellationTokenSource Cancellation, long Generation,
        VersionManagementSnapshot Prior, CancellationToken Token);

    private sealed record CheckStart(DiscoveryCheck? Check, VersionManagementSnapshot? Snapshot);
}
