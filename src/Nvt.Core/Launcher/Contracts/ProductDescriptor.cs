// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Contracts;

/// <summary>Product-supplied runtime names; this is not a serialized manifest or schema.</summary>
public sealed class ProductDescriptor
{
    private readonly Func<ManagedAppVersion, string> archiveRootName;

    /// <summary>Creates the runtime descriptor without supplying product-specific defaults.</summary>
    /// <param name="productId">Exact product identity supplied by the adapter.</param>
    /// <param name="runtimeIdentifier">Exact target runtime supplied by the adapter.</param>
    /// <param name="registryId">Exact registry identity supplied by the adapter.</param>
    /// <param name="applicationExecutableRelativePath">Canonical slash-separated application executable path.</param>
    /// <param name="launcherExecutableRelativePath">Canonical slash-separated launcher executable path.</param>
    /// <param name="bootstrapExecutableFileName">Canonical single root Bootstrap filename.</param>
    /// <param name="archiveRootName">Returns the single archive-root name for a version.</param>
    /// <param name="protocolNames">Exact protocol names supplied by the adapter.</param>
    public ProductDescriptor(
        string productId,
        string runtimeIdentifier,
        string registryId,
        string applicationExecutableRelativePath,
        string launcherExecutableRelativePath,
        string bootstrapExecutableFileName,
        Func<ManagedAppVersion, string> archiveRootName,
        LauncherProtocolNames protocolNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentNullException.ThrowIfNull(archiveRootName);
        ArgumentNullException.ThrowIfNull(protocolNames);
        if (!ContractValidation.IsSafeRelativePath(applicationExecutableRelativePath))
        {
            throw new ArgumentException("Application executable path is unsafe.", nameof(applicationExecutableRelativePath));
        }
        if (!ContractValidation.IsSafeRelativePath(launcherExecutableRelativePath))
        {
            throw new ArgumentException("Launcher executable path is unsafe.", nameof(launcherExecutableRelativePath));
        }
        if (!ContractValidation.IsSafeFileName(bootstrapExecutableFileName))
        {
            throw new ArgumentException("Bootstrap executable filename is unsafe.", nameof(bootstrapExecutableFileName));
        }

        ProductId = productId;
        RuntimeIdentifier = runtimeIdentifier;
        RegistryId = registryId;
        ApplicationExecutableRelativePath = applicationExecutableRelativePath;
        LauncherExecutableRelativePath = launcherExecutableRelativePath;
        BootstrapExecutableFileName = bootstrapExecutableFileName;
        ProtocolNames = protocolNames;
        this.archiveRootName = archiveRootName;
    }

    /// <summary>Gets the exact product identity.</summary>
    public string ProductId { get; }

    /// <summary>Gets the exact target runtime.</summary>
    public string RuntimeIdentifier { get; }

    /// <summary>Gets the exact registry identity.</summary>
    public string RegistryId { get; }

    /// <summary>Gets the application executable's relative path.</summary>
    public string ApplicationExecutableRelativePath { get; }

    /// <summary>Gets the launcher executable's relative path.</summary>
    public string LauncherExecutableRelativePath { get; }

    /// <summary>Gets the immutable root Bootstrap filename.</summary>
    public string BootstrapExecutableFileName { get; }

    /// <summary>Gets the adapter-supplied protocol names.</summary>
    public LauncherProtocolNames ProtocolNames { get; }

    /// <summary>Evaluates and validates the archive-root name for this exact version.</summary>
    /// <param name="version">The archive's canonical version.</param>
    /// <returns>A safe single directory name, preserved unchanged.</returns>
    /// <exception cref="ArgumentException">The callback returned a blank or unsafe name.</exception>
    public string GetArchiveRootName(ManagedAppVersion version)
    {
        string name = archiveRootName(version);
        return ContractValidation.IsSafeFileName(name)
            ? name
            : throw new ArgumentException("Archive root must be a safe single directory name.", nameof(version));
    }
}

/// <summary>Exact protocol names transported by the product's retained process adapter.</summary>
/// <param name="ApplicationReadyHandle">Application READY handle name.</param>
/// <param name="ExpectedApplicationVersion">Expected application-version name.</param>
/// <param name="LauncherReadyHandle">Launcher READY handle name.</param>
/// <param name="ExpectedLauncherReady">Expected launcher-identity name.</param>
/// <param name="BootstrapAdmissionHandle">Bootstrap ADMITTED handle name.</param>
/// <param name="BootstrapStartContext">Bootstrap START context name.</param>
/// <param name="BootstrapStartHandle">Bootstrap START handle name.</param>
/// <param name="LifetimeContext">Lifetime context name.</param>
/// <param name="LifetimeHandle">Lifetime handle name.</param>
/// <param name="LifetimeJob">Lifetime Job name.</param>
/// <param name="LifetimeStatePath">Lifetime state-path name.</param>
/// <param name="LifetimeKind">Lifetime role name.</param>
/// <param name="BootstrapIdentity">Immutable Bootstrap identity name.</param>
public sealed record LauncherProtocolNames(
    string ApplicationReadyHandle,
    string ExpectedApplicationVersion,
    string LauncherReadyHandle,
    string ExpectedLauncherReady,
    string BootstrapAdmissionHandle,
    string BootstrapStartContext,
    string BootstrapStartHandle,
    string LifetimeContext,
    string LifetimeHandle,
    string LifetimeJob,
    string LifetimeStatePath,
    string LifetimeKind,
    string BootstrapIdentity);
