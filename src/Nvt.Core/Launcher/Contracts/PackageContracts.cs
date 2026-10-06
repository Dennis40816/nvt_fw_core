// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Contracts;

/// <summary>Product-owned strict manifest and payload policy used by generic verification.</summary>
public interface IProductPackagePolicy
{
    /// <summary>Checks the product's canonical relative payload-path grammar and allowlist.</summary>
    /// <param name="path">The exact relative path.</param>
    /// <returns>Whether the path is admitted by product policy.</returns>
    bool IsSafeRelativePayloadPath(string path);

    /// <summary>Validates exact manifest bytes and projects their normalized values.</summary>
    /// <param name="exactBytes">The exact bytes whose digest the verifier checks.</param>
    /// <param name="expectedVersion">The exact expected package or installed version.</param>
    /// <param name="archivePaths">Archive paths, or null for installed-verification mode.</param>
    /// <param name="manifest">The complete normalized manifest on success, otherwise null.</param>
    /// <returns>Whether strict schema, product, runtime and closed payload validation succeeded.</returns>
    /// <remarks>The launcher is unowned here; exact owner admission and manifest hash are bound later.</remarks>
    bool TryReadManifest(
        ReadOnlyMemory<byte> exactBytes,
        ManagedAppVersion expectedVersion,
        IReadOnlyCollection<string>? archivePaths,
        out PackageManifest? manifest);
}

/// <summary>Explicit product-supplied verification ceilings; no product defaults are implied.</summary>
/// <param name="MaximumArchiveEntries">Maximum archive entry count.</param>
/// <param name="MaximumPackageBytes">Maximum complete package byte count.</param>
/// <param name="MaximumExpandedBytes">Maximum actual expanded byte count.</param>
/// <param name="MaximumManifestBytes">Maximum exact manifest byte count.</param>
/// <param name="MaximumAdmissionBytes">Maximum exact admission-document byte count.</param>
/// <param name="MaximumExecutableBytes">Maximum admitted executable byte count.</param>
public sealed record PackageVerificationLimits(
    int MaximumArchiveEntries,
    long MaximumPackageBytes,
    long MaximumExpandedBytes,
    int MaximumManifestBytes,
    int MaximumAdmissionBytes,
    long MaximumExecutableBytes);

/// <summary>Normalized declared file content, validated by the product manifest adapter.</summary>
/// <param name="Path">Exact canonical relative path.</param>
/// <param name="Size">Exact declared byte length.</param>
/// <param name="Sha256">Lowercase SHA-256 digest.</param>
public sealed record PackageFile(string Path, long Size, string Sha256);

/// <summary>Unowned launcher declaration from the parsed manifest; it conveys no launch authority.</summary>
/// <param name="LauncherVersion">Canonical launcher version.</param>
/// <param name="ProtocolVersion">Declared protocol integer.</param>
/// <param name="ExecutableRelativePath">Exact declared launcher path.</param>
/// <param name="Size">Exact executable byte length.</param>
/// <param name="Sha256">Lowercase executable SHA-256.</param>
public sealed record PackageLauncher(
    ManagedAppVersion LauncherVersion,
    int ProtocolVersion,
    string ExecutableRelativePath,
    long Size,
    string Sha256);

/// <summary>Generic manifest projection after product-owned schema and payload validation.</summary>
/// <param name="ProductId">Exact parsed product identity.</param>
/// <param name="RuntimeIdentifier">Exact parsed target runtime.</param>
/// <param name="Version">Exact parsed application version.</param>
/// <param name="Files">Complete normalized declared file inventory.</param>
/// <param name="Launcher">Optional unowned launcher declaration.</param>
public sealed record PackageManifest(
    string ProductId,
    string RuntimeIdentifier,
    ManagedAppVersion Version,
    IReadOnlyList<PackageFile> Files,
    PackageLauncher? Launcher);
