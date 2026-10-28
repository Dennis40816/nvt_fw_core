// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Integration;

public sealed partial class VersionManagementExperience
{
    /// <inheritdoc />
    public async ValueTask<VersionEnvironmentSelfTestResult> RunEnvironmentSelfTestAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_services.SourceRegistry is null)
        {
            return new(_services.Limits, UpdateSourceRegistryLoadIssue.NotConfigured, []);
        }

        UpdateSourceRegistryLoadResult registry = await LoadRegistryAsync(_services.SourceRegistry, cancellationToken)
            .ConfigureAwait(false);
        if (!registry.IsSuccess)
        {
            return new(_services.Limits, registry.Issue, [], registry.Replicas);
        }

        UpdateSourceRegistrySnapshot snapshot = registry.Snapshot!;
        (VersionSourceRegistryState? acceptedAuthority, UpdateSourceRegistryIssue? authorityIssue) =
            await ReadAndValidateRegistryAuthorityAsync(
                snapshot,
                cancellationToken).ConfigureAwait(false);
        if (authorityIssue is { } rejected)
        {
            return new(_services.Limits,
                UpdateSourceRegistryLoadIssue.None,
                [],
                registry.Replicas,
                rejected,
                acceptedAuthority?.AcceptedRevision);
        }

        var attempts = new List<VersionEnvironmentSelfTestAttempt>(
            snapshot.Entries.Count);
        foreach (UpdateSourceRegistryEntry entry in snapshot.AutomaticCandidates())
        {
            RegistryCandidateInspection inspection = await InspectCandidateAsync(
                entry,
                snapshot.CatalogPublication,
                cancellationToken).ConfigureAwait(false);
            attempts.Add(inspection.Attempt);
        }
        (acceptedAuthority, authorityIssue) = await ReadAndValidateRegistryAuthorityAsync(
            snapshot,
            cancellationToken).ConfigureAwait(false);
        return authorityIssue is { } changedAuthority
            ? new(_services.Limits,
                UpdateSourceRegistryLoadIssue.None,
                [],
                registry.Replicas,
                changedAuthority,
                acceptedAuthority?.AcceptedRevision)
            : new(_services.Limits,
                UpdateSourceRegistryLoadIssue.None,
                attempts,
                registry.Replicas,
                acceptedRegistryRevision: acceptedAuthority?.AcceptedRevision);
    }

    private async ValueTask<(
        VersionSourceRegistryState? Accepted,
        UpdateSourceRegistryIssue? Issue)> ReadAndValidateRegistryAuthorityAsync(
        UpdateSourceRegistrySnapshot candidate,
        CancellationToken cancellationToken)
    {
        VersionManagerStateLoadResult durable = await _services.StateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        VersionSourceRegistryState? accepted = durable.IsSuccess
            ? durable.State!.SourceRegistryState
            : null;
        return durable.Issue is not (
                VersionManagerStateLoadIssue.None or VersionManagerStateLoadIssue.Missing)
            ? (accepted, UpdateSourceRegistryIssue.StateUnavailable)
            : (accepted, ValidateAntiRollback(accepted, candidate));
    }

    private async ValueTask<RegistryCandidateInspection> InspectCandidateAsync(
        UpdateSourceRegistryEntry entry,
        UpdateCatalogPublicationAssertion expectedPublication,
        CancellationToken cancellationToken)
    {
        RegistryCandidateInspection catalogInspection = await InspectCatalogPublicationAsync(
            entry,
            expectedPublication,
            cancellationToken).ConfigureAwait(false);
        return catalogInspection.Catalog is not { } catalog
            ? catalogInspection
            : await VerifyRegistryPackageAsync(
                entry,
                catalog,
                catalog.Versions[0],
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RegistryCandidateInspection> InspectAutomaticCandidateAsync(
        UpdateSourceRegistryEntry entry,
        UpdateCatalogPublicationAssertion expectedPublication,
        ManagedAppVersion currentVersion,
        CancellationToken cancellationToken)
    {
        RegistryCandidateInspection catalogInspection = await InspectCatalogPublicationAsync(
            entry,
            expectedPublication,
            cancellationToken).ConfigureAwait(false);
        if (catalogInspection.Catalog is not { } catalog)
        {
            return catalogInspection;
        }
        UpdateCatalogVersionSnapshot? selectedPackage =
            catalog.FindNewestNotifyNewerThan(currentVersion);
        return selectedPackage is null
            ? catalogInspection
            : await VerifyRegistryPackageAsync(
                entry,
                catalog,
                selectedPackage,
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RegistryCandidateInspection> InspectCatalogPublicationAsync(
        UpdateSourceRegistryEntry entry,
        UpdateCatalogPublicationAssertion expectedPublication,
        CancellationToken cancellationToken)
    {
        UpdateCatalogLoadResult loaded = await _services.CatalogSource.LoadCatalogAsync(
            entry.CatalogPath,
            cancellationToken).ConfigureAwait(false);
        loaded = ReadmitCatalog(loaded);
        bool publicationMatches = loaded is
        {
            IsSuccess: true,
            Snapshot.Versions.Count: > 0,
            ContentIdentity: { } identity,
        } &&
            identity.SchemaVersion == expectedPublication.CatalogSchemaVersion &&
            string.Equals(
                identity.Sha256,
                expectedPublication.CatalogSha256,
                StringComparison.Ordinal) &&
            loaded.Snapshot.Versions[0].Version == expectedPublication.LatestVersion;
        if (!publicationMatches)
        {
            return new(
                new(
                    entry.SourceRoot,
                    entry.Status,
                    loaded.IsSuccess ? UpdateCatalogLoadIssue.InvalidManifest : loaded.Issue,
                    packageIssue: null,
                    newestVersion: null,
                    isVerified: false),
                Admission: null,
                Catalog: null);
        }
        UpdateCatalogSnapshot admittedCatalog = loaded.Snapshot ??
            throw new InvalidOperationException("Matched Catalog publication has no snapshot.");
        return new(
            new(
                entry.SourceRoot,
                entry.Status,
                UpdateCatalogLoadIssue.None,
                packageIssue: ManagedVersionInstallIssue.InvalidPayload,
                newestVersion: admittedCatalog.Versions[0].Version,
                isVerified: false),
            Admission: null,
            Catalog: admittedCatalog);
    }

    private async ValueTask<RegistryCandidateInspection> VerifyRegistryPackageAsync(
        UpdateSourceRegistryEntry entry,
        UpdateCatalogSnapshot catalog,
        UpdateCatalogVersionSnapshot selectedPackage,
        CancellationToken cancellationToken)
    {
        ManagedPackageVerificationResult verification = await _services.Repository.VerifyPackageAsync(
            entry.SourceRoot,
            selectedPackage,
            cancellationToken).ConfigureAwait(false);
        bool verified = verification is { IsVerified: true, Candidate: { } candidate } &&
            candidate.Version == selectedPackage.Version &&
            string.Equals(candidate.AdmissionIdentity, selectedPackage.Identity, StringComparison.Ordinal);
        ManagedVersionInstallIssue packageIssue = verified
            ? ManagedVersionInstallIssue.None
            : verification.Issue == ManagedVersionInstallIssue.None
                ? ManagedVersionInstallIssue.InvalidPayload
                : verification.Issue;
        RegistryCandidateAdmission? admission = !verified
            ? null
            : new(
                entry,
                catalog,
                selectedPackage,
                verification.Candidate!,
                verification.HasSupportedManagedLauncher);
        return new(
            new(
                entry.SourceRoot,
                entry.Status,
                UpdateCatalogLoadIssue.None,
                packageIssue,
                selectedPackage.Version,
                verified),
            admission,
            catalog);
    }

    private static UpdateSourceRegistryIssue? ValidateAntiRollback(
        VersionSourceRegistryState? accepted,
        UpdateSourceRegistrySnapshot candidate)
    {
        return accepted is null || accepted.AcceptedRevision == 0
            ? null
            : candidate.RegistryRevision < accepted.AcceptedRevision
                ? UpdateSourceRegistryIssue.RevisionRollback
                : candidate.RegistryRevision == accepted.AcceptedRevision &&
                  !string.Equals(candidate.ContentDigest, accepted.AcceptedDigest, StringComparison.Ordinal)
                    ? UpdateSourceRegistryIssue.RevisionConflict
                    : null;
    }

    internal static bool CanPersistAllManualRegistryAuthority(
        VersionManagerState state,
        string selectedSourceRoot)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedSourceRoot);
        return state.UpdateSource is { } effectiveSource &&
            state.SourceRegistryState is { IsManualPin: false } &&
            SourcePathEquals(selectedSourceRoot, effectiveSource);
    }

    private static bool CatalogPublicationEquals(
        UpdateCatalogSnapshot left,
        UpdateCatalogSnapshot right)
    {
        if (left.Versions.Count != right.Versions.Count)
        {
            return false;
        }
        for (int index = 0; index < left.Versions.Count; index++)
        {
            UpdateCatalogVersionSnapshot first = left.Versions[index];
            UpdateCatalogVersionSnapshot second = right.Versions[index];
            if (first.Version != second.Version ||
                first.PublishedAt != second.PublishedAt ||
                first.PackagePath != second.PackagePath ||
                first.PackageSize != second.PackageSize ||
                !string.Equals(first.PackageSha256, second.PackageSha256, StringComparison.Ordinal) ||
                !string.Equals(
                    first.ReleaseManifestSha256,
                    second.ReleaseManifestSha256,
                    StringComparison.Ordinal) ||
                !string.Equals(first.ReleaseNotes, second.ReleaseNotes, StringComparison.Ordinal) ||
                first.NotificationPolicy != second.NotificationPolicy)
            {
                return false;
            }
        }
        return true;
    }

    private static VersionRegistryStatus ResolveLoadStatus(UpdateSourceRegistryLoadIssue issue)
    {
        return issue is UpdateSourceRegistryLoadIssue.InvalidManifest or
            UpdateSourceRegistryLoadIssue.UnsafeLocator or
            UpdateSourceRegistryLoadIssue.RegistryTooLarge or
            UpdateSourceRegistryLoadIssue.UnstableRead or
            UpdateSourceRegistryLoadIssue.ReplicaConflict
                ? VersionRegistryStatus.Rejected
                : VersionRegistryStatus.Unavailable;
    }

    private static UpdateSourceRegistryIssue ResolveLoadIssue(UpdateSourceRegistryLoadIssue issue)
    {
        return issue switch
        {
            UpdateSourceRegistryLoadIssue.NotConfigured => UpdateSourceRegistryIssue.NotConfigured,
            UpdateSourceRegistryLoadIssue.PermissionDenied => UpdateSourceRegistryIssue.PermissionDenied,
            UpdateSourceRegistryLoadIssue.AuthenticationRequired =>
                UpdateSourceRegistryIssue.AuthenticationRequired,
            UpdateSourceRegistryLoadIssue.RegistryTimedOut => UpdateSourceRegistryIssue.TimedOut,
            UpdateSourceRegistryLoadIssue.InvalidManifest or
            UpdateSourceRegistryLoadIssue.UnsafeLocator or
            UpdateSourceRegistryLoadIssue.RegistryTooLarge or
            UpdateSourceRegistryLoadIssue.UnstableRead or
            UpdateSourceRegistryLoadIssue.ReplicaConflict => UpdateSourceRegistryIssue.Invalid,
            UpdateSourceRegistryLoadIssue.RegistryMissing or
            UpdateSourceRegistryLoadIssue.RegistryUnavailable => UpdateSourceRegistryIssue.Unavailable,
            UpdateSourceRegistryLoadIssue.None => UpdateSourceRegistryIssue.Invalid,
            _ => UpdateSourceRegistryIssue.Invalid,
        };
    }

    private static bool SourcePathEquals(string? left, string? right)
    {
        return OperatingSystem.IsWindows()
            ? string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            : string.Equals(left, right, StringComparison.Ordinal);
    }

    private sealed record RegistryCandidateAdmission(
        UpdateSourceRegistryEntry Entry,
        UpdateCatalogSnapshot Catalog,
        UpdateCatalogVersionSnapshot NewestPackage,
        VerifiedUpdateCandidate VerifiedCandidate,
        bool HasSupportedManagedLauncher);

    private sealed record RegistryCandidateInspection(
        VersionEnvironmentSelfTestAttempt Attempt,
        RegistryCandidateAdmission? Admission,
        UpdateCatalogSnapshot? Catalog);
}
