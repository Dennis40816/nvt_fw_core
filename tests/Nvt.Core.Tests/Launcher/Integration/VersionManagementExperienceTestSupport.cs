// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Provides shared helpers for version management experience tests.</summary>
internal static partial class VersionManagementExperienceTestSupport
{
    internal const string Hash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    internal static VersionManagerState State(
        IReadOnlyList<ManagedVersionAdmission> admissions,
        string active,
        string lastKnownGood,
        string source = "source-root",
        VersionSourceRegistryState? sourceRegistryState = null)
    {
        return VersionManagerState.Create(
            source,
            ManagedAppVersion.Parse(active),
            ManagedAppVersion.Parse(lastKnownGood),
            admissions,
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root",
            sourceRegistryState: sourceRegistryState);
    }

    internal static ManagedVersionAdmission Admission(string version)
    {
        return new(ManagedAppVersion.Parse(version), $"identity-{version}", Hash);
    }

    internal sealed class FixedCatalogSource(UpdateCatalogSnapshot snapshot) : IRootCatalogSourceTestDouble
    {
        public ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new UpdateCatalogLoadResult(
                snapshot,
                UpdateCatalogLoadIssue.None,
                new(1, CatalogContentDigest)));
        }
    }

    internal sealed class MutableCatalogSource(UpdateCatalogSnapshot snapshot) : IRootCatalogSourceTestDouble
    {
        // Guard: the owning test changes the catalog only between awaited experience calls.
        internal UpdateCatalogSnapshot Snapshot { get; set; } = snapshot;

        // Guard: the owning test awaits each catalog call before reading or starting another.
        internal int LoadCount { get; private set; }

        public ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            LoadCount++;
            return ValueTask.FromResult(new UpdateCatalogLoadResult(Snapshot, UpdateCatalogLoadIssue.None));
        }
    }

    internal sealed class HealthyRepository(
        bool verifyPackages = true,
        ManagedVersionDeleteIssue deleteIssue = ManagedVersionDeleteIssue.None,
        ManagedPackageVerificationResult? verificationResult = null) : IManagedVersionRepository
    {
        // Guard: the owning test reads the trace only after an awaited mutation completes.
        internal List<ManagedAppVersion> Deleted { get; } = [];

        // Guard: test gates or awaited calls serialize verification and observation of its count.
        internal int VerifyCount { get; private set; }
        // Guard: the owning test reads this count after awaited repository calls complete.
        internal int InstallCount { get; private set; }
        // Guard: the owning test configures the callback before starting verification.
        internal Action? BeforeVerification { get; set; }
        // Guard: the owning test replaces the receipt only between awaited verification calls.
        internal ManagedPackageVerificationResult? VerificationResultOverride { get; set; } = verificationResult;

        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            BeforeVerification?.Invoke();
            VerifyCount++;
            return ValueTask.FromResult(VerificationResultOverride ?? (verifyPackages
                ? new ManagedPackageVerificationResult(
                    new(package.Version, package.Identity, package.ReleaseNotes),
                    ManagedVersionInstallIssue.None)
                {
                    HasSupportedManagedLauncher = true,
                }
                : new ManagedPackageVerificationResult(
                    Candidate: null,
                    ManagedVersionInstallIssue.PackageMismatch)));
        }

        public ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot,
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            InstallCount++;
            return ValueTask.FromResult(new ManagedVersionInstallResult(
                new(package.Version, package.Identity, package.ReleaseManifestSha256),
                ManagedVersionInstallIssue.None,
                WasAlreadyInstalled: false));
        }

        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
            string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions,
            ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion,
            ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(ManagedVersionInventoryReadResult.Success(
                ManagedVersionInventory.Create(admissions.Select(admission =>
                    new InstalledVersionSnapshot(
                        admission.Version,
                        admission.AdmissionIdentity,
                        failedActivationVersion == admission.Version
                            ? ManagedVersionIntegrity.Damaged
                            : ManagedVersionIntegrity.Healthy,
                        failedActivationVersion == admission.Version
                            ? ManagedVersionDamageReason.FailedActivation
                            : null,
                        activeVersion == admission.Version,
                        lastKnownGoodVersion == admission.Version)))));
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken)
        {
            if (deleteIssue == ManagedVersionDeleteIssue.None)
            {
                Deleted.Add(admission.Version);
            }
            return ValueTask.FromResult(deleteIssue);
        }
    }

    internal const string FirstRegistryDigest =
        "1111111111111111111111111111111111111111111111111111111111111111";
    internal const string SecondRegistryDigest =
        "2222222222222222222222222222222222222222222222222222222222222222";
}
