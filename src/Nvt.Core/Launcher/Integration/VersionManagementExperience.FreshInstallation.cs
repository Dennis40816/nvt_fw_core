// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;

namespace Nvt.Core.Launcher.Integration;

public sealed partial class VersionManagementExperience : IFreshInstallationCandidateSource
{
    /// <inheritdoc />
    public async ValueTask<FreshInstallationCandidateResult> InspectFreshInstallationAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_services.SourceRegistry is null)
        {
            return FreshFailure(FreshInstallationCandidateIssue.RegistryNotConfigured);
        }

        UpdateSourceRegistryLoadResult loaded = await _services.SourceRegistry.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.IsSuccess)
        {
            return FreshFailure(MapFreshRegistryIssue(loaded.Issue));
        }

        UpdateSourceRegistrySnapshot registry = loaded.Snapshot!;
        bool sourceUnavailable = false;
        bool sourceRejected = false;
        foreach (UpdateSourceRegistryEntry entry in registry.AutomaticCandidates())
        {
            RegistryCandidateInspection inspection = await InspectCandidateAsync(
                entry, registry.CatalogPublication, cancellationToken).ConfigureAwait(false);
            if (inspection.Admission is { HasSupportedManagedLauncher: true } admission &&
                admission.NewestPackage.Version >= _services.CurrentAppVersion)
            {
                return FreshSuccess(CreateFreshCandidate(registry, admission));
            }
            if (IsFreshSourceUnavailable(inspection.Attempt))
            {
                sourceUnavailable = true;
            }
            else if (inspection.Admission is null || !inspection.Admission.HasSupportedManagedLauncher)
            {
                sourceRejected = true;
            }
        }
        return FreshFailure(sourceUnavailable
            ? FreshInstallationCandidateIssue.SourceUnavailable
            : sourceRejected
                ? FreshInstallationCandidateIssue.SourceRejected
                : FreshInstallationCandidateIssue.CandidateUnavailable);
    }

    /// <inheritdoc />
    public async ValueTask<FreshInstallationCandidateResult> ReverifyFreshInstallationAsync(
        FreshInstallationCandidate expected, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        if (_services.SourceRegistry is null)
        {
            return FreshFailure(FreshInstallationCandidateIssue.RegistryNotConfigured);
        }

        UpdateSourceRegistryLoadResult loaded = await _services.SourceRegistry.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.IsSuccess)
        {
            return FreshFailure(MapFreshRegistryIssue(loaded.Issue));
        }
        UpdateSourceRegistrySnapshot registry = loaded.Snapshot!;
        FreshInstallationCandidateIdentity identity = expected.Identity;
        if (!string.Equals(registry.RegistryId, identity.RegistryId, StringComparison.Ordinal) ||
            registry.RegistryRevision != identity.RegistryRevision ||
            !string.Equals(registry.ContentDigest, identity.RegistryDigest, StringComparison.Ordinal) ||
            registry.CatalogPublication.CatalogSchemaVersion != identity.CatalogSchemaVersion ||
            registry.CatalogPublication.LatestVersion != identity.CatalogLatestVersion ||
            !string.Equals(registry.CatalogPublication.CatalogSha256, identity.CatalogDigest, StringComparison.Ordinal))
        {
            return FreshFailure(FreshInstallationCandidateIssue.SourceChanged);
        }

        UpdateSourceRegistryEntry? entry = registry.AutomaticCandidates().SingleOrDefault(candidate =>
            (FreshInstallationSourceStatus)candidate.Status == identity.SourceStatus &&
            SourcePathEquals(candidate.CatalogPath, identity.CatalogPath) &&
            SourcePathEquals(candidate.SourceRoot, identity.SourceRoot));
        if (entry is null)
        {
            return FreshFailure(FreshInstallationCandidateIssue.SourceChanged);
        }

        RegistryCandidateInspection inspection = await InspectCandidateAsync(
            entry, registry.CatalogPublication, cancellationToken).ConfigureAwait(false);
        if (inspection.Admission is null)
        {
            return FreshFailure(IsFreshSourceUnavailable(inspection.Attempt)
                ? FreshInstallationCandidateIssue.SourceUnavailable
                : FreshInstallationCandidateIssue.SourceChanged);
        }
        if (!inspection.Admission.HasSupportedManagedLauncher)
        {
            return FreshFailure(FreshInstallationCandidateIssue.SourceChanged);
        }
        FreshInstallationCandidate actual = CreateFreshCandidate(registry, inspection.Admission);
        return actual.Identity == identity
            ? FreshSuccess(actual)
            : FreshFailure(FreshInstallationCandidateIssue.SourceChanged);
    }

    private FreshInstallationCandidate CreateFreshCandidate(
        UpdateSourceRegistrySnapshot registry, RegistryCandidateAdmission admission)
    {
        UpdateCatalogVersionSnapshot package = admission.NewestPackage;
        return new(
            new(
                _services.Limits.MaximumRegistryIdCharacters,
                registry.RegistryId,
                registry.RegistryRevision,
                registry.ContentDigest,
                registry.CatalogPublication.CatalogSchemaVersion,
                registry.CatalogPublication.LatestVersion,
                registry.CatalogPublication.CatalogSha256,
                admission.Entry.CatalogPath,
                admission.Entry.SourceRoot,
                (FreshInstallationSourceStatus)admission.Entry.Status,
                package.PackagePath.Value!,
                package.PackageSize,
                package.PackageSha256,
                package.ReleaseManifestSha256),
            package,
            admission.VerifiedCandidate);
    }

    private static FreshInstallationCandidateResult FreshSuccess(FreshInstallationCandidate candidate) =>
        new(candidate, FreshInstallationCandidateIssue.None);

    private static FreshInstallationCandidateResult FreshFailure(FreshInstallationCandidateIssue issue) =>
        new(null, issue);

    private static FreshInstallationCandidateIssue MapFreshRegistryIssue(UpdateSourceRegistryLoadIssue issue) =>
        issue switch
        {
            UpdateSourceRegistryLoadIssue.None => FreshInstallationCandidateIssue.SourceRejected,
            UpdateSourceRegistryLoadIssue.NotConfigured => FreshInstallationCandidateIssue.RegistryNotConfigured,
            UpdateSourceRegistryLoadIssue.InvalidManifest or
            UpdateSourceRegistryLoadIssue.UnsafeLocator or
            UpdateSourceRegistryLoadIssue.RegistryTooLarge or
            UpdateSourceRegistryLoadIssue.UnstableRead or
            UpdateSourceRegistryLoadIssue.ReplicaConflict => FreshInstallationCandidateIssue.SourceRejected,
            UpdateSourceRegistryLoadIssue.RegistryMissing or
            UpdateSourceRegistryLoadIssue.RegistryUnavailable or
            UpdateSourceRegistryLoadIssue.PermissionDenied or
            UpdateSourceRegistryLoadIssue.AuthenticationRequired or
            UpdateSourceRegistryLoadIssue.RegistryTimedOut => FreshInstallationCandidateIssue.SourceUnavailable,
            _ => throw new InvalidOperationException("Registry returned an undefined issue."),
        };

    private static bool IsFreshSourceUnavailable(VersionEnvironmentSelfTestAttempt attempt) =>
        attempt.CatalogIssue is UpdateCatalogLoadIssue.SourceMissing or
            UpdateCatalogLoadIssue.SourceUnavailable or UpdateCatalogLoadIssue.PermissionDenied ||
        attempt.PackageIssue is ManagedVersionInstallIssue.PackageUnavailable or ManagedVersionInstallIssue.StateUnavailable;
}
