// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Integration;

/// <summary>Immutable normalized catalog admitted after mandatory product validation.</summary>
public sealed class UpdateCatalogSnapshot
{
    private UpdateCatalogSnapshot(UpdateCatalogVersionSnapshot[] versions)
    {
        Versions = Array.AsReadOnly(versions);
    }

    /// <summary>Rechecks normalized rows against the consuming product's inclusive ceilings.</summary>
    /// <param name="limits">Mandatory positive admission ceilings.</param>
    /// <param name="versions">Product-admitted unique rows, in any order.</param>
    /// <returns>An owned snapshot ordered newest first.</returns>
    /// <remarks>Schema, product/runtime, timestamp text and release authority remain adapter obligations.</remarks>
    public static UpdateCatalogSnapshot Create(
        VersionManagementLimits limits,
        IReadOnlyList<UpdateCatalogVersionSnapshot> versions)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(versions);
        UpdateCatalogVersionSnapshot[] rows = [.. versions];
        if (rows.Length < 1 || rows.Length > limits.MaximumCatalogEntries ||
            rows.Any(static row => row is null) ||
            rows.Select(static row => row.Version).Distinct().Count() != rows.Length)
        {
            throw new ArgumentException("Update catalog snapshot is inconsistent.", nameof(versions));
        }
        foreach (UpdateCatalogVersionSnapshot row in rows)
        {
            ValidatePackage(limits, row);
        }
        Array.Sort(rows, static (left, right) => right.Version.CompareTo(left.Version));
        return new(rows);
    }

    /// <summary>Gets the admitted entries ordered newest first through an owned read-only view.</summary>
    public IReadOnlyList<UpdateCatalogVersionSnapshot> Versions { get; }

    internal UpdateCatalogVersionSnapshot? FindNewestNewerThan(ManagedAppVersion current) =>
        Versions.FirstOrDefault(version => version.Version > current);

    internal UpdateCatalogVersionSnapshot? FindNewestNotifyNewerThan(ManagedAppVersion current) =>
        Versions.FirstOrDefault(version => version.Version > current &&
            version.NotificationPolicy == UpdateNotificationPolicy.Notify);

    internal static void ValidatePackage(
        VersionManagementLimits limits, UpdateCatalogVersionSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(package);
        // Reuse the existing admission owner, including its literal messages and check order.
        _ = UpdateCatalogVersionSnapshot.Create(
            limits.MaximumPackageBytes, limits.MaximumReleaseNoteBytes,
            package.Version, package.PublishedAt, package.PackagePath, package.PackageSize,
            package.PackageSha256, package.ReleaseManifestSha256, package.ReleaseNotes,
            package.NotificationPolicy);
        if (package.PackagePath.Value!.Length > limits.MaximumRelativePathCharacters)
        {
            throw new ArgumentException("Package path is unsafe.", nameof(package));
        }
    }
}
