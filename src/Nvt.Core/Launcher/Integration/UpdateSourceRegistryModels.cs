// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Integration;

/// <summary>Declared automatic-selection role of one registry entry.</summary>
public enum UpdateSourceRegistryEntryStatus
{
    /// <summary>The first and only preferred source.</summary>
    Latest,
    /// <summary>One ordered automatic fallback source.</summary>
    Available,
    /// <summary>A historical source that automatic resolution must never select.</summary>
    Deprecated,
}

/// <summary>One normalized absolute catalog-file path admitted by the registry adapter.</summary>
public sealed record UpdateSourceRegistryEntry
{
    private UpdateSourceRegistryEntry(string catalogPath, UpdateSourceRegistryEntryStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        CatalogPath = catalogPath;
        _ = Path.GetDirectoryName(catalogPath) ??
            throw new ArgumentException("Catalog path has no parent directory.", nameof(catalogPath));
        Status = Enum.IsDefined(status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(status));
        if (!RepositoryPathSafety.TryNormalizeExactAbsolutePath(catalogPath, out _) ||
            string.IsNullOrEmpty(Path.GetFileName(catalogPath)))
        {
            throw new ArgumentException("Catalog path must be normalized and absolute.", nameof(catalogPath));
        }
    }

    /// <summary>Rechecks a product-admitted normalized absolute catalog path and selection role.</summary>
    /// <param name="catalogPath">Exact normalized absolute catalog-file path.</param>
    /// <param name="status">Declared automatic-selection role.</param>
    /// <returns>The immutable entry.</returns>
    /// <remarks>Locator admission remains the mandatory product adapter's responsibility.</remarks>
    public static UpdateSourceRegistryEntry Create(string catalogPath, UpdateSourceRegistryEntryStatus status) =>
        new(catalogPath, status);

    /// <summary>Gets the normalized absolute catalog path.</summary>
    public string CatalogPath { get; }

    /// <summary>Gets the catalog parent used to resolve package paths.</summary>
    public string SourceRoot => Path.GetDirectoryName(CatalogPath)!;

    /// <summary>Gets the declared selection role.</summary>
    public UpdateSourceRegistryEntryStatus Status { get; }
}

/// <summary>Mandatory identity assertions for the catalog selected by a registry.</summary>
public sealed record UpdateCatalogPublicationAssertion
{
    private UpdateCatalogPublicationAssertion(string latestVersion, int catalogSchemaVersion, string catalogSha256)
    {
        LatestVersion = ManagedAppVersion.Parse(latestVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(catalogSchemaVersion);
        if (!ContractValidation.IsLowerSha256(catalogSha256))
        {
            throw new ArgumentException("Catalog SHA-256 is invalid.", nameof(catalogSha256));
        }
        CatalogSchemaVersion = catalogSchemaVersion;
        CatalogSha256 = catalogSha256;
    }

    /// <summary>Creates the mechanically checked publication assertions after product admission.</summary>
    /// <param name="latestVersion">Canonical newest application version.</param>
    /// <param name="catalogSchemaVersion">Positive catalog wire-schema version.</param>
    /// <param name="catalogSha256">Lowercase SHA-256 of the exact catalog bytes.</param>
    /// <returns>The immutable assertions.</returns>
    public static UpdateCatalogPublicationAssertion Create(
        string latestVersion, int catalogSchemaVersion, string catalogSha256) =>
        new(latestVersion, catalogSchemaVersion, catalogSha256);

    /// <summary>Gets the asserted newest application version.</summary>
    public ManagedAppVersion LatestVersion { get; }

    /// <summary>Gets the asserted catalog wire-schema version.</summary>
    public int CatalogSchemaVersion { get; }

    /// <summary>Gets the lowercase SHA-256 of the exact catalog bytes.</summary>
    public string CatalogSha256 { get; }
}

/// <summary>One complete immutable registry publication.</summary>
public sealed class UpdateSourceRegistrySnapshot
{
    private UpdateSourceRegistrySnapshot(
        string registryId, long registryRevision, DateTimeOffset publishedAtUtc,
        UpdateCatalogPublicationAssertion catalogPublication, string contentDigest,
        UpdateSourceRegistryEntry[] entries)
    {
        RegistryId = registryId;
        RegistryRevision = registryRevision;
        PublishedAtUtc = publishedAtUtc;
        CatalogPublication = catalogPublication;
        ContentDigest = contentDigest;
        Entries = Array.AsReadOnly(entries);
    }

    /// <summary>Rechecks a normalized publication against explicit product identity and ceilings.</summary>
    /// <param name="limits">Mandatory positive admission ceilings.</param>
    /// <param name="descriptor">Mandatory product identity supplied by the adapter.</param>
    /// <param name="registryId">Logical registry identity, matching the descriptor ordinally.</param>
    /// <param name="registryRevision">Positive administrative revision.</param>
    /// <param name="publishedAtUtc">Normalized UTC audit timestamp.</param>
    /// <param name="catalogPublication">Complete catalog assertions.</param>
    /// <param name="contentDigest">Lowercase digest of the exact stable registry bytes.</param>
    /// <param name="entries">Entries in declared order with one latest and unique paths and roots.</param>
    /// <returns>An owned read-only publication.</returns>
    /// <remarks>The mandatory product adapter owns schema, locators, exact-byte hashing and authority policy.</remarks>
    public static UpdateSourceRegistrySnapshot Create(
        VersionManagementLimits limits, ProductDescriptor descriptor,
        string registryId, long registryRevision, DateTimeOffset publishedAtUtc,
        UpdateCatalogPublicationAssertion catalogPublication, string contentDigest,
        IReadOnlyList<UpdateSourceRegistryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(registryRevision);
        ArgumentNullException.ThrowIfNull(catalogPublication);
        ArgumentNullException.ThrowIfNull(entries);
        if (!string.Equals(registryId, descriptor.RegistryId, StringComparison.Ordinal) ||
            registryId.Length > limits.MaximumRegistryIdCharacters ||
            publishedAtUtc.Offset != TimeSpan.Zero ||
            !ContractValidation.IsLowerSha256(contentDigest) ||
            entries.Count < 1 || entries.Count > limits.MaximumRegistryEntries ||
            entries.Any(static entry => entry is null) ||
            entries.Count(static entry => entry.Status == UpdateSourceRegistryEntryStatus.Latest) != 1 ||
            entries.Select(static entry => entry.CatalogPath).Distinct(RepositoryPathSafety.PathComparer).Count() != entries.Count ||
            entries.Select(static entry => entry.SourceRoot).Distinct(RepositoryPathSafety.PathComparer).Count() != entries.Count)
        {
            throw new ArgumentException("Update-source registry snapshot is inconsistent.", nameof(entries));
        }
        return new(registryId, registryRevision, publishedAtUtc, catalogPublication, contentDigest, entries.ToArray());
    }

    /// <summary>Gets the stable logical registry identity shared by its replicas.</summary>
    public string RegistryId { get; }

    /// <summary>Gets the strictly increasing administrative revision.</summary>
    public long RegistryRevision { get; }

    /// <summary>Gets audit-only UTC publication time; it never selects a replica.</summary>
    public DateTimeOffset PublishedAtUtc { get; }

    /// <summary>Gets mandatory assertions for the selected catalog publication.</summary>
    public UpdateCatalogPublicationAssertion CatalogPublication { get; }

    /// <summary>Gets the lowercase digest of the exact stable registry bytes.</summary>
    public string ContentDigest { get; }

    /// <summary>Gets entries in declared order through an owned read-only view.</summary>
    public IReadOnlyList<UpdateSourceRegistryEntry> Entries { get; }

    internal IEnumerable<UpdateSourceRegistryEntry> AutomaticCandidates()
    {
        yield return Entries.First(static entry => entry.Status == UpdateSourceRegistryEntryStatus.Latest);
        foreach (UpdateSourceRegistryEntry entry in Entries)
        {
            if (entry.Status == UpdateSourceRegistryEntryStatus.Available)
            {
                yield return entry;
            }
        }
    }
}

/// <summary>Stable fixed-registry read issue.</summary>
public enum UpdateSourceRegistryLoadIssue
{
    /// <summary>The complete registry was loaded.</summary>
    None,
    /// <summary>No registry locator was configured.</summary>
    NotConfigured,
    /// <summary>The registry file is absent.</summary>
    RegistryMissing,
    /// <summary>The registry could not be read.</summary>
    RegistryUnavailable,
    /// <summary>The current user cannot read the registry.</summary>
    PermissionDenied,
    /// <summary>The remote Registry requires credentials unavailable to this client.</summary>
    AuthenticationRequired,
    /// <summary>The remote Registry did not complete within the bounded deadline.</summary>
    RegistryTimedOut,
    /// <summary>The locator or a traversed path is unsafe.</summary>
    UnsafeLocator,
    /// <summary>The raw registry exceeds the product reader byte ceiling.</summary>
    RegistryTooLarge,
    /// <summary>The schema, revision, entries, or normalized paths are invalid.</summary>
    InvalidManifest,
    /// <summary>The file changed during its stable read.</summary>
    UnstableRead,
    /// <summary>Replicas published different exact bytes at the same revision.</summary>
    ReplicaConflict,
}

/// <summary>Fail-closed result from one fixed-registry read.</summary>
/// <param name="Snapshot">The complete normalized registry when present.</param>
/// <param name="Issue">The stable read issue.</param>
/// <param name="Replicas">Ordered replica health when available.</param>
public sealed record UpdateSourceRegistryLoadResult(
    UpdateSourceRegistrySnapshot? Snapshot,
    UpdateSourceRegistryLoadIssue Issue,
    IReadOnlyList<UpdateSourceRegistryReplicaObservation>? Replicas = null)
{
    private readonly IReadOnlyList<UpdateSourceRegistryReplicaObservation>? _replicas =
        Replicas is null ? null : Array.AsReadOnly(Replicas.ToArray());

    /// <summary>Gets or initializes an owned read-only copy of the ordered replica health.</summary>
    public IReadOnlyList<UpdateSourceRegistryReplicaObservation>? Replicas
    {
        get => _replicas;
        init => _replicas = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>Gets whether one complete registry publication was admitted.</summary>
    public bool IsSuccess => Snapshot is not null && Issue == UpdateSourceRegistryLoadIssue.None;
}

/// <summary>Read-only health of one ordered Registry replica.</summary>
/// <param name="Position">One-based configured replica position.</param>
/// <param name="Issue">The observed read issue.</param>
/// <param name="RegistryRevision">The observed revision when a snapshot is present.</param>
/// <param name="IsSelected">Whether this exact snapshot supplied the selected publication.</param>
public sealed record UpdateSourceRegistryReplicaObservation(
    int Position,
    UpdateSourceRegistryLoadIssue Issue,
    long? RegistryRevision,
    bool IsSelected);

/// <summary>Reads one fixed registry without choosing or persisting an effective source.</summary>
public interface IUpdateSourceRegistry
{
    /// <summary>Loads one complete product-admitted registry publication.</summary>
    /// <param name="cancellationToken">Cancellation token for the adapter read.</param>
    /// <returns>The complete publication or typed read issue.</returns>
    ValueTask<UpdateSourceRegistryLoadResult> LoadAsync(CancellationToken cancellationToken);
}
