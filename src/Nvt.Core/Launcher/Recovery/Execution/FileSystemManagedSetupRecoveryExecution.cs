// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Launcher.Verification;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Produces exact filesystem recovery evidence and executes its ordered deletion under live writer custody.</summary>
/// <remarks>Product codecs are mandatory. This adapter selects no action, acquires no writer, and writes no state or marker.</remarks>
public sealed partial class FileSystemManagedSetupRecoveryExecution :
    IManagedSetupRecoveryEvidenceProbe, IManagedSetupRecoveryExecutionPort
{
    private readonly IInstalledLauncherRepository _launcherRepository;
    private readonly ILauncherBootstrapStateStore _launcherStateStore;
    private readonly IManagedVersionRepository _repository;
    private readonly IManagedSetupRecoveryStateReader _applicationStateStore;
    private readonly Action<int>? _afterDelete;
    private readonly Action<long>? _afterHashedFile;
    private readonly string _launcherStatePath;
    private readonly string _statePath;
    private readonly ManagedSetupLayout _layout;
    private readonly Func<string, IVersionManagerStateStore> _seedStoreFactory;
    private readonly PackageVerificationLimits _limits;
    private readonly WindowsStableTreeLimits _payloadTreeLimits;
    private readonly IProductPackagePolicy _productPolicy;
    private readonly FileSystemManagedVersionRepository _packageRepository;
    private readonly ManagedPackageVerifier _verifier;

    /// <summary>Creates Core-owned evidence production with strict product adapters and explicit positive ceilings.</summary>
    /// <param name="applicationStateReader">Strict reader bound to the canonical application-state path.</param>
    /// <param name="launcherStatePathSuffix">Fixed suffix used by the product's launcher-state store.</param>
    /// <param name="launcherStateStore">Strict launcher-state adapter for that derived path.</param>
    /// <param name="layout">Exact distribution Launcher, Bootstrap and seed names.</param>
    /// <param name="seedStoreFactory">Strict seed adapter for each supplied exact seed path.</param>
    /// <param name="productPolicy">Strict manifest and payload-path admission.</param>
    /// <param name="limits">Positive product ceilings shared with repository verification.</param>
    /// <param name="admissionCodec">Strict installed-admission codec.</param>
    public FileSystemManagedSetupRecoveryExecution(
        IManagedSetupRecoveryStateReader applicationStateReader, string launcherStatePathSuffix,
        ILauncherBootstrapStateStore launcherStateStore, ManagedSetupLayout layout,
        Func<string, IVersionManagerStateStore> seedStoreFactory, IProductPackagePolicy productPolicy,
        PackageVerificationLimits limits, IManagedVersionAdmissionCodec admissionCodec)
        : this(applicationStateReader, launcherStatePathSuffix, launcherStateStore, layout,
            seedStoreFactory, productPolicy, limits, admissionCodec, new())
    {
    }

    internal FileSystemManagedSetupRecoveryExecution(
        IManagedSetupRecoveryStateReader applicationStateReader, string launcherStatePathSuffix,
        ILauncherBootstrapStateStore launcherStateStore, ManagedSetupLayout layout,
        Func<string, IVersionManagerStateStore> seedStoreFactory, IProductPackagePolicy productPolicy,
        PackageVerificationLimits limits, IManagedVersionAdmissionCodec admissionCodec,
        RecoveryExecutionTestHooks hooks)
    {
        _applicationStateStore = applicationStateReader ?? throw new ArgumentNullException(nameof(applicationStateReader));
        string statePath = applicationStateReader.StatePathIdentity;
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        if (!Path.IsPathFullyQualified(statePath))
        {
            throw new ArgumentException("Recovery state path must be absolute.", nameof(applicationStateReader));
        }
        _statePath = Path.GetFullPath(statePath);
        _launcherStatePath = LauncherBootstrapStateFile.DerivePath(_statePath, launcherStatePathSuffix);
        _launcherStateStore = launcherStateStore ?? throw new ArgumentNullException(nameof(launcherStateStore));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _seedStoreFactory = seedStoreFactory ?? throw new ArgumentNullException(nameof(seedStoreFactory));
        _productPolicy = productPolicy ?? throw new ArgumentNullException(nameof(productPolicy));
        _verifier = new(layout.Descriptor, productPolicy, limits);
        _limits = limits;
        _packageRepository = new(layout.Descriptor, productPolicy, limits, admissionCodec);
        _payloadTreeLimits = _packageRepository.TreeLimits;
        _repository = hooks.Repository ?? _packageRepository;
        _launcherRepository = hooks.LauncherRepository ??
            new FileSystemInstalledLauncherRepository(layout.Descriptor, productPolicy, limits, admissionCodec);
        _afterDelete = hooks.AfterDelete;
        _afterHashedFile = hooks.AfterHashedFile;
    }

    private WindowsStableTreeLimits CreateSetupTreeLimits(string root, ManagedSetupRecoveryPayloadIdentity payload)
    {
        long seedBytes = new FileInfo(Path.Combine(root, _layout.SeedFileName)).Length;
        return new(checked(_payloadTreeLimits.MaximumFiles + 3),
            checked(_payloadTreeLimits.MaximumDirectories + 2),
            checked(_payloadTreeLimits.MaximumBytes + payload.LauncherSize + payload.BootstrapSize + seedBytes));
    }

    private static string LowerHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}

internal sealed record RecoveryExecutionTestHooks
{
    internal IManagedVersionRepository? Repository { get; init; }
    internal IInstalledLauncherRepository? LauncherRepository { get; init; }
    internal Action<int>? AfterDelete { get; init; }
    internal Action<long>? AfterHashedFile { get; init; }
}
