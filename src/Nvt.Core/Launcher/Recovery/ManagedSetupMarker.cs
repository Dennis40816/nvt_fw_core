// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Normalized marker facts; product schema fields remain in the mandatory codec.</summary>
/// <param name="TransactionId">Exact lowercase transaction identifier.</param>
/// <param name="ManagedRootIdentity">Exact absolute root identity.</param>
/// <param name="StatePathIdentity">Exact absolute state identity.</param>
/// <param name="Payload">Exact distribution payload identity.</param>
/// <param name="Candidate">Exact publication and package identity.</param>
/// <param name="OwnedPaths">Closed ordered set of relative owned paths.</param>
/// <param name="Phase">One of staging, root-promoted or bootstrap-launch-recorded.</param>
public sealed record ManagedSetupMarker(
    string TransactionId,
    string ManagedRootIdentity,
    string StatePathIdentity,
    ManagedSetupRecoveryPayloadIdentity Payload,
    ManagedSetupRecoveryCandidateIdentity Candidate,
    IReadOnlyList<string> OwnedPaths,
    string Phase)
{
    internal static ManagedSetupMarker Create(string transactionId, string managedRoot,
        string statePathIdentity, string stagingPath, ManagedDistributionPayloadIdentity payload,
        FreshInstallationCandidate candidate, string phase)
    {
        string? parent = Path.GetDirectoryName(managedRoot);
        string? stagingContainer = Path.GetDirectoryName(stagingPath);
        if (parent is null || stagingContainer is null ||
            !RepositoryPathSafety.PathComparer.Equals(parent, Path.GetDirectoryName(stagingContainer)))
        {
            throw new InvalidOperationException("Setup owned paths do not share one parent.");
        }
        return new(transactionId, managedRoot, statePathIdentity,
            new(payload.LauncherSize, payload.LauncherSha256, payload.DescriptorSize,
                payload.DescriptorSha256, payload.Bootstrap.FileName, payload.Bootstrap.Length,
                payload.Bootstrap.Sha256),
            new(candidate.Identity.RegistryRevision, candidate.Identity.RegistryDigest,
                candidate.Identity.CatalogSchemaVersion, candidate.Identity.CatalogLatestVersion.ToString(),
                candidate.Identity.CatalogDigest, candidate.Identity.CatalogPath, candidate.Identity.RegistryId,
                candidate.Identity.SourceRoot, candidate.Identity.SourceStatus switch
                {
                    FreshInstallationSourceStatus.Latest => "latest",
                    FreshInstallationSourceStatus.Available => "available",
                    FreshInstallationSourceStatus.Deprecated => "deprecated",
                    _ => throw new InvalidOperationException("Candidate source status is undefined."),
                }, candidate.Package.Version.ToString(), candidate.Identity.PackagePath,
                candidate.Identity.PackageSize, candidate.Identity.PackageSha256,
                candidate.Package.ReleaseManifestSha256, candidate.Package.Identity),
            Array.AsReadOnly(new[] { Path.GetFileName(managedRoot),
                Path.GetFileName(FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(managedRoot)),
                string.Join('/', Path.GetFileName(stagingContainer), Path.GetFileName(stagingPath)) }), phase);
    }

    internal static bool Equivalent(ManagedSetupMarker left, ManagedSetupMarker right) =>
        left.TransactionId == right.TransactionId &&
        RepositoryPathSafety.PathComparer.Equals(left.ManagedRootIdentity, right.ManagedRootIdentity) &&
        RepositoryPathSafety.PathComparer.Equals(left.StatePathIdentity, right.StatePathIdentity) &&
        left.Payload == right.Payload && left.Candidate == right.Candidate &&
        left.OwnedPaths.SequenceEqual(right.OwnedPaths, StringComparer.Ordinal) && left.Phase == right.Phase;
}
