// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Integration;

/// <summary>Stable result category for configured-folder catalog discovery.</summary>
public enum UpdateCatalogLoadIssue
{
    /// <summary>No issue occurred.</summary>
    None,
    /// <summary>The configured folder or root catalog is absent.</summary>
    SourceMissing,
    /// <summary>The configured source could not be read.</summary>
    SourceUnavailable,
    /// <summary>The configured source exists but the current user cannot read it.</summary>
    PermissionDenied,
    /// <summary>The source or catalog crosses a link/reparse boundary.</summary>
    UnsafeSource,
    /// <summary>The catalog exceeds its raw byte ceiling.</summary>
    CatalogTooLarge,
    /// <summary>The catalog JSON or admitted values are invalid.</summary>
    InvalidManifest,
    /// <summary>The file changed during its stable read.</summary>
    UnstableRead,
}

/// <summary>Fail-closed result from one configured-folder catalog read.</summary>
/// <param name="Snapshot">The complete normalized catalog when present.</param>
/// <param name="Issue">The stable read issue.</param>
/// <param name="ContentIdentity">The exact validated wire identity when available.</param>
public sealed record UpdateCatalogLoadResult(
    UpdateCatalogSnapshot? Snapshot,
    UpdateCatalogLoadIssue Issue,
    UpdateCatalogContentIdentity? ContentIdentity = null)
{
    /// <summary>Gets whether a complete immutable catalog was published.</summary>
    public bool IsSuccess => Snapshot is not null && Issue == UpdateCatalogLoadIssue.None;
}

/// <summary>Exact wire identity of one validated Catalog publication.</summary>
public sealed record UpdateCatalogContentIdentity
{
    /// <summary>Creates a validated exact Catalog identity.</summary>
    public UpdateCatalogContentIdentity(int schemaVersion, string sha256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (!ContractValidation.IsLowerSha256(sha256))
        {
            throw new ArgumentException("Catalog SHA-256 is invalid.", nameof(sha256));
        }
        SchemaVersion = schemaVersion;
        Sha256 = sha256;
    }

    /// <summary>Gets the validated Catalog schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the lowercase SHA-256 of the exact Catalog bytes.</summary>
    public string Sha256 { get; }
}

/// <summary>Reads one immutable update-catalog snapshot from a configured root.</summary>
public interface IUpdateCatalogSource
{
    /// <summary>Loads and validates the configured source without recursive discovery.</summary>
    /// <param name="sourceRoot">Configured local or UNC folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A fail-closed catalog result.</returns>
    ValueTask<UpdateCatalogLoadResult> LoadAsync(
        string sourceRoot,
        CancellationToken cancellationToken);

    /// <summary>Loads one exact Catalog JSON file selected by a Registry publication.</summary>
    /// <param name="catalogPath">Normalized absolute Catalog JSON path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A fail-closed Catalog result including exact wire identity.</returns>
    ValueTask<UpdateCatalogLoadResult> LoadCatalogAsync(
        string catalogPath,
        CancellationToken cancellationToken);
}
