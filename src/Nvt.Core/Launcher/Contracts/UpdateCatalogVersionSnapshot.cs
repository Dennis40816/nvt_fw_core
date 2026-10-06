// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text;

namespace Nvt.Core.Launcher.Contracts;

/// <summary>Closed effective policy for background update notification.</summary>
public enum UpdateNotificationPolicy
{
    /// <summary>The entry remains visible only to explicit checks and installation.</summary>
    ManualOnly,
    /// <summary>The entry may become an automatic notification candidate.</summary>
    Notify,
}

/// <summary>Catalog-relative package path supplied by the product catalog adapter.</summary>
/// <param name="Value">Exact relative path; the snapshot factory checks structural safety.</param>
public readonly record struct UpdateCatalogPackagePath(string Value);

/// <summary>Immutable normalized package metadata whose identity excludes the configured source root.</summary>
public sealed class UpdateCatalogVersionSnapshot
{
    private UpdateCatalogVersionSnapshot(
        ManagedAppVersion version,
        DateTimeOffset publishedAt,
        UpdateCatalogPackagePath packagePath,
        long packageSize,
        string packageSha256,
        string releaseManifestSha256,
        string releaseNotes,
        UpdateNotificationPolicy notificationPolicy)
    {
        Version = version;
        PublishedAt = publishedAt;
        PackagePath = packagePath;
        PackageSize = packageSize;
        PackageSha256 = packageSha256;
        ReleaseManifestSha256 = releaseManifestSha256;
        ReleaseNotes = releaseNotes;
        NotificationPolicy = notificationPolicy;
        Identity = string.Join(
            '|',
            Version,
            PackagePath.Value,
            PackageSize.ToString(CultureInfo.InvariantCulture),
            PackageSha256,
            ReleaseManifestSha256);
    }

    /// <summary>Creates a structurally validated snapshot after product catalog admission.</summary>
    /// <param name="maximumPackageBytes">The product's positive package-size ceiling.</param>
    /// <param name="maximumReleaseNoteBytes">The product's positive UTF-8 release-note ceiling.</param>
    /// <param name="version">Canonical package version.</param>
    /// <param name="publishedAt">Normalized UTC publication timestamp.</param>
    /// <param name="packagePath">Canonical safe relative package path.</param>
    /// <param name="packageSize">Positive package byte length.</param>
    /// <param name="packageSha256">Lowercase package SHA-256.</param>
    /// <param name="releaseManifestSha256">Lowercase exact release-manifest SHA-256.</param>
    /// <param name="releaseNotes">Present release-note text, including empty text.</param>
    /// <param name="notificationPolicy">The adapter's explicit effective notification policy.</param>
    /// <returns>The immutable snapshot with the unchanged identity composition.</returns>
    /// <remarks>The product supplies the size and note ceilings. Its adapter retains schema, ZIP-path and timestamp-text rules.</remarks>
    public static UpdateCatalogVersionSnapshot Create(
        long maximumPackageBytes,
        int maximumReleaseNoteBytes,
        ManagedAppVersion version,
        DateTimeOffset publishedAt,
        UpdateCatalogPackagePath packagePath,
        long packageSize,
        string packageSha256,
        string releaseManifestSha256,
        string releaseNotes,
        UpdateNotificationPolicy notificationPolicy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPackageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReleaseNoteBytes);
        if (publishedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Publication timestamp must be normalized UTC.", nameof(publishedAt));
        }
        if (!ContractValidation.IsSafeRelativePath(packagePath.Value))
        {
            throw new ArgumentException("Package path is unsafe.", nameof(packagePath));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(packageSize, maximumPackageBytes);
        if (!ContractValidation.IsLowerSha256(packageSha256))
        {
            throw new ArgumentException("Package digest must be lowercase SHA-256.", nameof(packageSha256));
        }
        if (!ContractValidation.IsLowerSha256(releaseManifestSha256))
        {
            throw new ArgumentException("Manifest digest must be lowercase SHA-256.", nameof(releaseManifestSha256));
        }
        ArgumentNullException.ThrowIfNull(releaseNotes);
        if (Encoding.UTF8.GetByteCount(releaseNotes) > maximumReleaseNoteBytes)
        {
            throw new ArgumentException("Release notes exceed the byte ceiling.", nameof(releaseNotes));
        }
        if (!Enum.IsDefined(notificationPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(notificationPolicy));
        }

        return new(version, publishedAt, packagePath, packageSize, packageSha256,
            releaseManifestSha256, releaseNotes, notificationPolicy);
    }

    /// <summary>Gets the package version.</summary>
    public ManagedAppVersion Version { get; }

    /// <summary>Gets the normalized UTC publication timestamp.</summary>
    public DateTimeOffset PublishedAt { get; }

    /// <summary>Gets the catalog-relative package path.</summary>
    public UpdateCatalogPackagePath PackagePath { get; }

    /// <summary>Gets the declared package byte length.</summary>
    public long PackageSize { get; }

    /// <summary>Gets the lowercase package SHA-256 digest.</summary>
    public string PackageSha256 { get; }

    /// <summary>Gets the lowercase exact release-manifest SHA-256 digest.</summary>
    public string ReleaseManifestSha256 { get; }

    /// <summary>Gets the product-admitted release notes.</summary>
    public string ReleaseNotes { get; }

    /// <summary>Gets the explicit effective background-notification policy.</summary>
    public UpdateNotificationPolicy NotificationPolicy { get; }

    /// <summary>Gets the location-independent catalog-entry identity.</summary>
    public string Identity { get; }
}
