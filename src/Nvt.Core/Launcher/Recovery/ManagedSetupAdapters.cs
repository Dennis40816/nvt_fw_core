// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Validated embedded Bootstrap facts supplied by the product's strict adapter.</summary>
/// <param name="InstalledFileName">Exact descriptor-bound root filename.</param>
/// <param name="Size">Positive executable length.</param>
/// <param name="Sha256">Lowercase executable digest.</param>
/// <param name="SourceCommit">Exact release source commit.</param>
public sealed record ManagedSetupEmbeddedBootstrap(
    string InstalledFileName, long Size, string Sha256, string SourceCommit);

/// <summary>Validated embedded descriptor facts; this is not a wire schema.</summary>
/// <param name="LauncherVersion">Canonical launcher version.</param>
/// <param name="SourceCommit">Exact release source commit.</param>
/// <param name="Bootstrap">Validated embedded Bootstrap declaration.</param>
public sealed record ManagedSetupPayloadDescriptor(
    string LauncherVersion, string SourceCommit, ManagedSetupEmbeddedBootstrap Bootstrap);

/// <summary>Product-owned strict descriptor and Bootstrap document admission.</summary>
public interface IManagedSetupPayloadDescriptorReader
{
    /// <summary>Validates exact descriptor bytes and returns normalized facts, or null.</summary>
    /// <remarks>The adapter validates its schema, product, runtime, protocol and embedded resource declaration.</remarks>
    ManagedSetupPayloadDescriptor? ReadValidated(ReadOnlyMemory<byte> exactBytes);
}

/// <summary>Product-owned strict marker encoding and decoding.</summary>
public interface IManagedSetupMarkerCodec
{
    /// <summary>Encodes the supplied normalized transaction using the product's strict schema.</summary>
    ReadOnlyMemory<byte> Encode(ManagedSetupMarker marker);

    /// <summary>Validates exact marker bytes and returns normalized facts, or null.</summary>
    ManagedSetupMarker? Decode(ReadOnlyMemory<byte> exactBytes);
}

/// <summary>Explicit root names used by one setup and recovery composition.</summary>
public sealed class ManagedSetupLayout
{
    /// <summary>Creates exact safe root filenames without product defaults.</summary>
    public ManagedSetupLayout(ProductDescriptor descriptor, string distributionLauncherFileName, string seedFileName)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (!ContractValidation.IsSafeFileName(distributionLauncherFileName))
        {
            throw new ArgumentException("Distribution Launcher filename is unsafe.", nameof(distributionLauncherFileName));
        }
        if (!ContractValidation.IsSafeFileName(seedFileName))
        {
            throw new ArgumentException("Seed filename is unsafe.", nameof(seedFileName));
        }
        string[] names = [distributionLauncherFileName, seedFileName, descriptor.BootstrapExecutableFileName, "versions", ".staging"];
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
        {
            throw new ArgumentException("Setup root names must be distinct.");
        }
        DistributionLauncherFileName = distributionLauncherFileName;
        SeedFileName = seedFileName;
    }

    /// <summary>Gets the product's admitted runtime descriptor.</summary>
    public ProductDescriptor Descriptor { get; }
    /// <summary>Gets the exact root distribution Launcher filename.</summary>
    public string DistributionLauncherFileName { get; }
    /// <summary>Gets the exact immutable Bootstrap filename.</summary>
    public string BootstrapFileName => Descriptor.BootstrapExecutableFileName;
    /// <summary>Gets the exact unbound seed filename.</summary>
    public string SeedFileName { get; }
}
