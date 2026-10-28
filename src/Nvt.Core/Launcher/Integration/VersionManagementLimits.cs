// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Integration;

/// <summary>Mandatory product ceilings for normalized catalog and registry admission.</summary>
public sealed class VersionManagementLimits
{
    /// <summary>Creates positive inclusive ceilings without product defaults.</summary>
    /// <param name="maximumCatalogEntries">Maximum catalog rows.</param>
    /// <param name="maximumPackageBytes">Maximum declared package bytes.</param>
    /// <param name="maximumReleaseNoteBytes">Maximum UTF-8 bytes per release note.</param>
    /// <param name="maximumRelativePathCharacters">Maximum relative-path characters, at most 512.</param>
    /// <param name="maximumRegistryEntries">Maximum registry entries and self-test attempts.</param>
    /// <param name="maximumRegistryIdCharacters">Maximum registry identity characters.</param>
    public VersionManagementLimits(
        int maximumCatalogEntries,
        long maximumPackageBytes,
        int maximumReleaseNoteBytes,
        int maximumRelativePathCharacters,
        int maximumRegistryEntries,
        int maximumRegistryIdCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCatalogEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPackageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReleaseNoteBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelativePathCharacters);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRelativePathCharacters, ContractValidation.MaximumRelativePathCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRegistryEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRegistryIdCharacters);
        MaximumCatalogEntries = maximumCatalogEntries;
        MaximumPackageBytes = maximumPackageBytes;
        MaximumReleaseNoteBytes = maximumReleaseNoteBytes;
        MaximumRelativePathCharacters = maximumRelativePathCharacters;
        MaximumRegistryEntries = maximumRegistryEntries;
        MaximumRegistryIdCharacters = maximumRegistryIdCharacters;
    }

    /// <summary>Gets the inclusive catalog-row ceiling.</summary>
    public int MaximumCatalogEntries { get; }

    /// <summary>Gets the inclusive declared-package byte ceiling.</summary>
    public long MaximumPackageBytes { get; }

    /// <summary>Gets the inclusive UTF-8 release-note byte ceiling.</summary>
    public int MaximumReleaseNoteBytes { get; }

    /// <summary>Gets the inclusive relative-path character ceiling.</summary>
    public int MaximumRelativePathCharacters { get; }

    /// <summary>Gets the inclusive registry-entry and self-test-attempt ceiling.</summary>
    public int MaximumRegistryEntries { get; }

    /// <summary>Gets the inclusive registry-identity character ceiling.</summary>
    public int MaximumRegistryIdCharacters { get; }
}
