// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Coordination;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

internal static partial class ManagedMutationTestSupport
{
    internal sealed class TransactionRepository : IManagedVersionRepository
    {
        private readonly Dictionary<ManagedAppVersion, ManagedVersionAdmission?> _installed;

        internal TransactionRepository(
            IEnumerable<ManagedVersionAdmission> installed,
            string? unadmittedVersion = null)
        {
            _installed = installed.ToDictionary(
                admission => admission.Version,
                admission => (ManagedVersionAdmission?)admission);
            if (unadmittedVersion is not null)
            {
                _installed.Add(ManagedAppVersion.Parse(unadmittedVersion), null);
            }
        }

        internal Action? BeforeDelete { get; set; }

        internal Action? BeforeInstall { get; set; }

        internal int DeleteCalls { get; private set; }

        internal ManagedVersionDeleteIssue DeleteIssue { get; set; }

        internal int InstallCalls { get; private set; }

        internal ManagedVersionInstallIssue InstallIssue { get; set; }

        internal ManagedVersionInstallResult? InstallResultOverride { get; set; }

        internal int InventoryCalls { get; private set; }

        internal int VerifyPackageCalls { get; private set; }

        internal ManagedVersionInventoryReadResult? InventoryResultOverride { get; set; }

        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            VerifyPackageCalls++;
            return ValueTask.FromResult(new ManagedPackageVerificationResult(
                new(package.Version, package.Identity, package.ReleaseNotes),
                ManagedVersionInstallIssue.None));
        }

        public ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot,
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            BeforeInstall?.Invoke();
            InstallCalls++;
            if (InstallResultOverride is { } result)
            {
                return ValueTask.FromResult(result);
            }
            if (InstallIssue != ManagedVersionInstallIssue.None)
            {
                return ValueTask.FromResult(new ManagedVersionInstallResult(
                    Admission: null,
                    InstallIssue,
                    WasAlreadyInstalled: false));
            }
            var admission = new ManagedVersionAdmission(
                package.Version,
                package.Identity,
                package.ReleaseManifestSha256);
            _installed[package.Version] = admission;
            return ValueTask.FromResult(new ManagedVersionInstallResult(
                admission,
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
            InventoryCalls++;
            if (InventoryResultOverride is { } result)
            {
                return ValueTask.FromResult(result);
            }
            var committed = admissions.ToDictionary(admission => admission.Version);
            return ValueTask.FromResult(ManagedVersionInventoryReadResult.Success(
                ManagedVersionInventory.Create(_installed.Select(pair =>
            {
                bool isAdmitted = committed.TryGetValue(pair.Key, out ManagedVersionAdmission? admission);
                ManagedVersionAdmission? observed = pair.Value;
                if (!isAdmitted && observed is null)
                {
                    return new InstalledVersionSnapshot(
                        pair.Key,
                        $"unadmitted:{pair.Key}",
                        ManagedVersionIntegrity.Damaged,
                        ManagedVersionDamageReason.UnexpectedPath,
                        IsActive: false,
                        IsLastKnownGood: false,
                        ManagedVersionAdmissionState.Unadmitted);
                }
                ManagedVersionAdmission identity = admission ?? observed!;
                return new InstalledVersionSnapshot(
                    pair.Key,
                    identity.AdmissionIdentity,
                    ManagedVersionIntegrity.Healthy,
                    DamageReason: null,
                    activeVersion == pair.Key,
                    lastKnownGoodVersion == pair.Key,
                    isAdmitted
                        ? ManagedVersionAdmissionState.Admitted
                        : ManagedVersionAdmissionState.Unadmitted,
                    identity);
            }))));
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken)
        {
            BeforeDelete?.Invoke();
            DeleteCalls++;
            if (DeleteIssue == ManagedVersionDeleteIssue.NotInstalled)
            {
                _ = _installed.Remove(admission.Version);
                return ValueTask.FromResult(ManagedVersionDeleteIssue.NotInstalled);
            }
            return ValueTask.FromResult(DeleteIssue != ManagedVersionDeleteIssue.None
                ? DeleteIssue
                : _installed.Remove(admission.Version)
                    ? ManagedVersionDeleteIssue.None
                    : ManagedVersionDeleteIssue.NotInstalled);
        }
    }
}
