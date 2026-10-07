// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Caller-supplied presentation and stable entry point for one installed application.</summary>
public sealed record InstalledApplicationPresentation(string DisplayName, string LaunchEntryPoint, string IconPath);

/// <summary>Installed facts an upper layer can use to create or remove a desktop shortcut.</summary>
public sealed record InstalledApplicationInfo(
    string ProductId, ManagedAppVersion InstalledVersion, string ExecutablePath,
    string DisplayName, string LaunchEntryPoint, string IconPath);

/// <summary>Mandatory product presentation adapter; Core invents no executable, icon or display names.</summary>
public interface IInstalledApplicationPresentation
{
    /// <summary>Reads the display name, stable launch entry point and icon for this exact installed app.</summary>
    ValueTask<InstalledApplicationPresentation> ReadAsync(
        ProductDescriptor product, string installRoot, ManagedVersionAdmission admission, CancellationToken cancellationToken);
}

/// <summary>Starts one explicitly named installed app and reads its verified installed/shortcut facts.</summary>
/// <remarks>Every state, repository and process port must be composed for the supplied product identity.</remarks>
public sealed class InstalledApplicationCoordinator
{
    private readonly ProductDescriptor _product;
    private readonly string _installRoot;
    private readonly IVersionManagerStateStore _state;
    private readonly IManagedVersionRepository _repository;
    private readonly IInstalledApplicationPresentation _presentation;
    private readonly ManagedActivationCoordinator _activation;

    /// <summary>Creates coordination for the caller's named app and explicit installation root.</summary>
    public InstalledApplicationCoordinator(ProductDescriptor product, string installRoot,
        IVersionManagerStateStore state, IManagedVersionRepository repository,
        IManagedApplicationProcess process, IInstalledApplicationPresentation presentation, TimeSpan? readyDeadline = null)
    {
        _product = product ?? throw new ArgumentNullException(nameof(product));
        _installRoot = ManagedRootPathIdentity.Normalize(installRoot);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _activation = new(_installRoot, state, repository, process, readyDeadline);
    }

    /// <summary>Starts this named app through exact contained-process custody and the durable READY path.</summary>
    public ValueTask<ManagedLauncherResult> StartAsync(CancellationToken cancellationToken) => _activation.RunAsync(cancellationToken);

    /// <summary>Reads the committed active installation without initialization writes or recovery.</summary>
    public async ValueTask<InstalledApplicationInfo?> ReadInstalledApplicationAsync(CancellationToken cancellationToken)
    {
        VersionManagerStateLoadResult loaded = await _state.LoadAsync(cancellationToken).ConfigureAwait(false);
        return loaded.IsSuccess && loaded.State!.IsBoundToManagedRoot(_installRoot) && loaded.State.ActiveVersion is { } version
            ? await ReadInstalledApplicationAsync(loaded.State, version, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>Reads an exact admitted healthy installed version without creating or removing a shortcut.</summary>
    public async ValueTask<InstalledApplicationInfo?> ReadInstalledApplicationAsync(
        ManagedAppVersion version, CancellationToken cancellationToken)
    {
        VersionManagerStateLoadResult loaded = await _state.LoadAsync(cancellationToken).ConfigureAwait(false);
        return loaded.IsSuccess && loaded.State!.IsBoundToManagedRoot(_installRoot)
            ? await ReadInstalledApplicationAsync(loaded.State, version, cancellationToken).ConfigureAwait(false) : null;
    }

    private async ValueTask<InstalledApplicationInfo?> ReadInstalledApplicationAsync(
        VersionManagerState state, ManagedAppVersion version, CancellationToken cancellationToken)
    {
        ManagedVersionAdmission? admission = state.Admissions.SingleOrDefault(a => a.Version == version);
        if (admission is null) { return null; }
        ManagedVersionInventoryReadResult inventory = await _repository.InventoryAsync(_installRoot, state.Admissions,
            state.ActiveVersion, state.LastKnownGoodVersion, state.FailedActivationVersion, cancellationToken).ConfigureAwait(false);
        if (!inventory.IsSuccess || inventory.Inventory!.Find(version) is not
            { AdmissionState: ManagedVersionAdmissionState.Admitted, Integrity: ManagedVersionIntegrity.Healthy, ObservedAdmission: { } observed } ||
            observed != admission)
        { return null; }
        ManagedExecutableLaunchLeaseResult acquired = await _repository.AcquireApplicationLaunchLeaseAsync(
            _installRoot, admission, cancellationToken).ConfigureAwait(false);
        if (!acquired.IsAcquired) { acquired.Lease?.Dispose(); return null; }
        using IManagedExecutableLaunchLease lease = acquired.Lease!;
        if (!lease.TryValidateForStart()) { return null; }
        InstalledApplicationPresentation presentation = await _presentation.ReadAsync(_product, _installRoot, admission, cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentation.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentation.LaunchEntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentation.IconPath);
        return new(_product.ProductId, version, lease.ExecutablePath, presentation.DisplayName,
            presentation.LaunchEntryPoint, presentation.IconPath);
    }
}
