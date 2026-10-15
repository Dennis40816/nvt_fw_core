// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Setup;

/// <summary>Stable issue from read-only fresh-install candidate admission.</summary>
public enum FreshInstallationCandidateIssue
{
    /// <summary>One complete immutable candidate was admitted.</summary>
    None,
    /// <summary>No fixed production Registry is configured.</summary>
    RegistryNotConfigured,
    /// <summary>The Registry, Catalog, or package could not be observed completely.</summary>
    SourceUnavailable,
    /// <summary>The Registry or Catalog publication failed strict authority checks.</summary>
    SourceRejected,
    /// <summary>No automatic Registry entry supplied a completely verified package.</summary>
    CandidateUnavailable,
    /// <summary>The captured Registry/Catalog/package identity changed during exact revalidation.</summary>
    SourceChanged,
}

/// <summary>Closed value identity for one Registry/Catalog/package publication.</summary>
public sealed record FreshInstallationCandidateIdentity
{
    /// <summary>Creates one completely validated publication identity.</summary>
    public FreshInstallationCandidateIdentity(
        int maximumRegistryIdCharacters,
        string registryId,
        long registryRevision,
        string registryDigest,
        int catalogSchemaVersion,
        ManagedAppVersion catalogLatestVersion,
        string catalogDigest,
        string catalogPath,
        string sourceRoot,
        FreshInstallationSourceStatus sourceStatus,
        string packagePath,
        long packageSize,
        string packageSha256,
        string releaseManifestSha256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRegistryIdCharacters);
        if (string.IsNullOrWhiteSpace(registryId) || registryId.Length > maximumRegistryIdCharacters ||
            registryRevision <= 0 ||
            !ContractValidation.IsLowerSha256(registryDigest) ||
            catalogSchemaVersion <= 0 ||
            !ContractValidation.IsLowerSha256(catalogDigest) ||
            !IsExactCatalogPath(catalogPath, sourceRoot) ||
            !Enum.IsDefined(sourceStatus) ||
            !ContractValidation.IsSafeRelativePath(packagePath) ||
            packageSize <= 0 ||
            !ContractValidation.IsLowerSha256(packageSha256) ||
            !ContractValidation.IsLowerSha256(releaseManifestSha256))
        {
            throw new ArgumentException("Fresh installation candidate identity is invalid.");
        }

        RegistryId = registryId;
        RegistryRevision = registryRevision;
        RegistryDigest = registryDigest;
        CatalogSchemaVersion = catalogSchemaVersion;
        CatalogLatestVersion = catalogLatestVersion;
        CatalogDigest = catalogDigest;
        CatalogPath = catalogPath;
        SourceRoot = sourceRoot;
        SourceStatus = sourceStatus;
        PackagePath = packagePath;
        PackageSize = packageSize;
        PackageSha256 = packageSha256;
        ReleaseManifestSha256 = releaseManifestSha256;
    }

    /// <summary>Gets the Registry authority id.</summary>
    public string RegistryId { get; }
    /// <summary>Gets the monotonic Registry revision.</summary>
    public long RegistryRevision { get; }
    /// <summary>Gets the Registry content digest.</summary>
    public string RegistryDigest { get; }
    /// <summary>Gets the asserted Catalog schema version.</summary>
    public int CatalogSchemaVersion { get; }
    /// <summary>Gets the asserted latest Catalog version.</summary>
    public ManagedAppVersion CatalogLatestVersion { get; }
    /// <summary>Gets the asserted Catalog digest.</summary>
    public string CatalogDigest { get; }
    /// <summary>Gets the exact Catalog file path.</summary>
    public string CatalogPath { get; }
    /// <summary>Gets the exact source root.</summary>
    public string SourceRoot { get; }
    /// <summary>Gets the Registry source status.</summary>
    public FreshInstallationSourceStatus SourceStatus { get; }
    /// <summary>Gets the exact Catalog-relative package path.</summary>
    public string PackagePath { get; }
    /// <summary>Gets the exact package length.</summary>
    public long PackageSize { get; }
    /// <summary>Gets the exact package digest.</summary>
    public string PackageSha256 { get; }
    /// <summary>Gets the exact inner release-manifest digest.</summary>
    public string ReleaseManifestSha256 { get; }
    private static bool IsExactCatalogPath(string? catalogPath, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(catalogPath) || string.IsNullOrWhiteSpace(sourceRoot) ||
            !Path.IsPathFullyQualified(catalogPath) || !Path.IsPathFullyQualified(sourceRoot))
        {
            return false;
        }
        try
        {
            string catalog = Path.GetFullPath(catalogPath);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
            string? parent = Path.GetDirectoryName(catalog);
            return parent is not null && string.Equals(
                Path.TrimEndingDirectorySeparator(parent),
                root,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

}

/// <summary>Immutable fresh-install candidate plus its already admitted package values.</summary>
public sealed record FreshInstallationCandidate
{
    /// <summary>Creates one cross-checked immutable fresh-install token.</summary>
    public FreshInstallationCandidate(
        FreshInstallationCandidateIdentity identity,
        UpdateCatalogVersionSnapshot package,
        VerifiedUpdateCandidate verifiedCandidate)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(verifiedCandidate);
        if (identity.CatalogLatestVersion != package.Version ||
            !string.Equals(identity.PackagePath, package.PackagePath.Value, StringComparison.Ordinal) ||
            identity.PackageSize != package.PackageSize ||
            !string.Equals(identity.PackageSha256, package.PackageSha256, StringComparison.Ordinal) ||
            !string.Equals(
                identity.ReleaseManifestSha256,
                package.ReleaseManifestSha256,
                StringComparison.Ordinal) ||
            verifiedCandidate.Version != package.Version ||
            !string.Equals(
                verifiedCandidate.AdmissionIdentity,
                package.Identity,
                StringComparison.Ordinal) ||
            !string.Equals(verifiedCandidate.ReleaseNotes, package.ReleaseNotes, StringComparison.Ordinal))
        {
            throw new ArgumentException("Fresh installation candidate values disagree.");
        }
        Identity = identity;
        Package = package;
    }

    /// <summary>Gets the closed publication identity.</summary>
    public FreshInstallationCandidateIdentity Identity { get; }
    /// <summary>Gets the admitted Catalog package.</summary>
    public UpdateCatalogVersionSnapshot Package { get; }
}

/// <summary>Read-only fresh-install admission result.</summary>
public sealed record FreshInstallationCandidateResult(
    FreshInstallationCandidate? Candidate,
    FreshInstallationCandidateIssue Issue)
{
    /// <summary>Gets whether one complete immutable candidate was admitted.</summary>
    public bool IsSuccess => Candidate is not null && Issue == FreshInstallationCandidateIssue.None;
}

/// <summary>Narrow existing-owner seam used only after genuine Setup eligibility.</summary>
public interface IFreshInstallationCandidateSource
{
    /// <summary>Selects one complete latest compatible Registry/Catalog/package candidate.</summary>
    ValueTask<FreshInstallationCandidateResult> InspectFreshInstallationAsync(
        CancellationToken cancellationToken);

    /// <summary>Revalidates the exact captured authority without selecting another candidate.</summary>
    ValueTask<FreshInstallationCandidateResult> ReverifyFreshInstallationAsync(
        FreshInstallationCandidate expected,
        CancellationToken cancellationToken);
}

