// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Contracts;

public sealed class PackageContractTests
{
    [Fact]
    public void PackageAndInstallResultsRequireBothValuesAndTheSuccessIssue()
    {
        var candidate = new VerifiedUpdateCandidate(ContractFixture.App100, "synthetic-admission", "synthetic-notes");
        var admission = new ManagedVersionAdmission(candidate.Version, candidate.AdmissionIdentity, ContractFixture.ManifestSha);
        foreach (ManagedVersionInstallIssue issue in Enum.GetValues<ManagedVersionInstallIssue>())
        {
            Assert.Equal(issue == ManagedVersionInstallIssue.None,
                new ManagedPackageVerificationResult(candidate, issue).IsVerified);
            Assert.False(new ManagedPackageVerificationResult(null, issue).IsVerified);
            Assert.Equal(issue == ManagedVersionInstallIssue.None,
                new ManagedVersionInstallResult(admission, issue, WasAlreadyInstalled: true).IsSuccess);
            Assert.False(new ManagedVersionInstallResult(null, issue, WasAlreadyInstalled: false).IsSuccess);
        }

        var verified = new ManagedPackageVerificationResult(candidate, ManagedVersionInstallIssue.None);
        Assert.False(verified.HasSupportedManagedLauncher);
        Assert.True((verified with { HasSupportedManagedLauncher = true }).IsVerified);
        Assert.False((verified with { Candidate = null, HasSupportedManagedLauncher = true }).IsVerified);
        Assert.False(new ManagedPackageVerificationResult(candidate, (ManagedVersionInstallIssue)99).IsVerified);
        Assert.False(new ManagedVersionInstallResult(admission, (ManagedVersionInstallIssue)99, true).IsSuccess);
    }

    [Fact]
    public void LeaseResultsRequireTheHeldLeaseIdentityAndSuccessIssue()
    {
        using var lease = new SyntheticLease();
        ManagedLauncherIdentity identity = ContractFixture.CreateIdentity();
        foreach (ManagedExecutableLaunchIssue issue in Enum.GetValues<ManagedExecutableLaunchIssue>())
        {
            Assert.Equal(issue == ManagedExecutableLaunchIssue.None,
                new ManagedExecutableLaunchLeaseResult(lease, issue).IsAcquired);
            Assert.False(new ManagedExecutableLaunchLeaseResult(null, issue).IsAcquired);
        }
        foreach (InstalledLauncherIssue issue in Enum.GetValues<InstalledLauncherIssue>())
        {
            Assert.Equal(issue == InstalledLauncherIssue.None, new InstalledLauncherResult(identity, issue).IsVerified);
            Assert.False(new InstalledLauncherResult(null, issue).IsVerified);
            Assert.Equal(issue == InstalledLauncherIssue.None,
                new InstalledLauncherLaunchResult(identity, lease, issue).IsAcquired);
            Assert.False(new InstalledLauncherLaunchResult(null, lease, issue).IsAcquired);
            Assert.False(new InstalledLauncherLaunchResult(identity, null, issue).IsAcquired);
        }

        IManagedExecutableLaunchLease port = lease;
        Assert.Equal("C:/synthetic/held/FixtureProduct.exe", port.ExecutablePath);
        Assert.Equal("C:/synthetic/held", port.WorkingDirectory);
        Assert.True(port.TryValidateForStart());
        lease.IsValid = false;
        Assert.False(port.TryValidateForStart());
    }

    [Fact]
    public void IssueAndNotificationValuesRetainTheirFrozenNamesAndNumbers()
    {
        Assert.Equal(["None", "PackageUnavailable", "PackageMismatch", "UnsafeArchive", "InvalidPayload",
            "IdentityConflict", "PromotionFailed", "CleanupIncomplete", "StateUnavailable"],
            Enum.GetNames<ManagedVersionInstallIssue>());
        Assert.Equal(Enumerable.Range(0, 9), Enum.GetValues<ManagedVersionInstallIssue>().Select(static issue => (int)issue));
        Assert.Equal(["None", "Unavailable", "Tampered", "UnsafePath"], Enum.GetNames<ManagedExecutableLaunchIssue>());
        Assert.Equal(Enumerable.Range(0, 4), Enum.GetValues<ManagedExecutableLaunchIssue>().Select(static issue => (int)issue));
        Assert.Equal(["None", "Unavailable", "InvalidManifest", "Tampered", "ProtocolMismatch", "UnsafePath"],
            Enum.GetNames<InstalledLauncherIssue>());
        Assert.Equal(Enumerable.Range(0, 6), Enum.GetValues<InstalledLauncherIssue>().Select(static issue => (int)issue));
        Assert.Equal(["ManualOnly", "Notify"], Enum.GetNames<UpdateNotificationPolicy>());
        Assert.Equal([0, 1], Enum.GetValues<UpdateNotificationPolicy>().Select(static policy => (int)policy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolicyPortPreservesExactBytesAndNullableInstalledVerificationMode(bool archiveMode)
    {
        var policy = new SyntheticPolicy();
        ReadOnlyMemory<byte> exactBytes = "synthetic-manifest-bytes"u8.ToArray();
        IReadOnlyCollection<string>? paths = archiveMode
            ? new[] { ContractFixture.ApplicationPath, ContractFixture.LauncherPath }
            : null;

        Assert.True(policy.TryReadManifest(exactBytes, ContractFixture.App100, paths, out PackageManifest? manifest));
        Assert.Equal(exactBytes, policy.ObservedBytes);
        Assert.Same(paths, policy.ObservedPaths);
        PackageManifest normalized = Assert.IsType<PackageManifest>(manifest);
        Assert.Equal(ContractFixture.ProductId, normalized.ProductId);
        Assert.Equal(ContractFixture.RuntimeIdentifier, normalized.RuntimeIdentifier);
        Assert.Equal(ContractFixture.App100, normalized.Version);
        Assert.Collection(normalized.Files,
            file => Assert.Equal(new PackageFile(ContractFixture.ApplicationPath, 17, ContractFixture.PackageSha), file),
            file => Assert.Equal(new PackageFile(ContractFixture.LauncherPath, 123, ContractFixture.LauncherSha), file));
        PackageLauncher launcher = Assert.IsType<PackageLauncher>(normalized.Launcher);
        Assert.Equal(ContractFixture.App100, launcher.LauncherVersion);
        Assert.Equal(1, launcher.ProtocolVersion);
        Assert.Equal(ContractFixture.LauncherPath, launcher.ExecutableRelativePath);
        Assert.Equal(123, launcher.Size);
        Assert.Equal(ContractFixture.LauncherSha, launcher.Sha256);

        // The parsed declaration receives the exact owner admission and manifest digest only here.
        UpdateCatalogVersionSnapshot package = ContractFixture.CreateSnapshot();
        var owner = new ManagedVersionAdmission(package.Version, package.Identity, package.ReleaseManifestSha256);
        var limits = new PackageVerificationLimits(4, 1024, 2048, 512, 256, 123, 4);
        ManagedLauncherIdentity identity = ManagedLauncherIdentity.Create(ContractFixture.Descriptor,
            limits.MaximumExecutableBytes, owner.Version, owner.AdmissionIdentity, owner.ReleaseManifestSha256,
            launcher.LauncherVersion, launcher.ProtocolVersion, launcher.ExecutableRelativePath, launcher.Size, launcher.Sha256);
        Assert.True(identity.MatchesOwner(owner));
        Assert.False(identity.MatchesOwner(owner with { ReleaseManifestSha256 = new string('d', 64) }));
        Assert.True(policy.IsSafeRelativePayloadPath(ContractFixture.ApplicationPath));
        Assert.False(policy.IsSafeRelativePayloadPath("../escape.exe"));

        Assert.False(policy.TryReadManifest("changed-bytes"u8.ToArray(), ContractFixture.App100, paths, out manifest));
        Assert.Null(manifest);
        Assert.False(policy.TryReadManifest(exactBytes, ContractFixture.App101, paths, out manifest));
        Assert.Null(manifest);
    }

    [Fact]
    public void PublicSurfaceContainsOnlyTheAssignedContractsAndGuardedIdentities()
    {
        string[] expected =
        [
            nameof(ProductDescriptor), nameof(LauncherProtocolNames), nameof(ManagedAppVersion),
            nameof(ManagedVersionAdmission), nameof(IManagedExecutableLaunchLease), nameof(IProductPackagePolicy),
            nameof(PackageVerificationLimits), nameof(PackageFile), nameof(PackageLauncher), nameof(PackageManifest),
            nameof(UpdateCatalogVersionSnapshot), nameof(UpdateCatalogPackagePath), nameof(UpdateNotificationPolicy),
            nameof(VerifiedUpdateCandidate), nameof(ManagedVersionInstallIssue), nameof(ManagedVersionInstallResult),
            nameof(ManagedPackageVerificationResult), nameof(ManagedExecutableLaunchIssue),
            nameof(ManagedExecutableLaunchLeaseResult), nameof(ManagedLauncherIdentity),
            nameof(ManagedImmutableBootstrapIdentity), nameof(InstalledLauncherIssue), nameof(InstalledLauncherResult),
            nameof(InstalledLauncherLaunchResult),
        ];
        IEnumerable<string> actual = typeof(ProductDescriptor).Assembly.GetExportedTypes()
            .Where(static type => type.Namespace == "Nvt.Core.Launcher.Contracts")
            .Select(static type => type.Name);
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.Empty(typeof(UpdateCatalogVersionSnapshot).GetConstructors());
        Assert.Empty(typeof(ManagedLauncherIdentity).GetConstructors());
        Assert.Empty(typeof(ManagedImmutableBootstrapIdentity).GetConstructors());
    }

    private sealed class SyntheticLease : IManagedExecutableLaunchLease
    {
        public string ExecutablePath => "C:/synthetic/held/FixtureProduct.exe";
        public string WorkingDirectory => "C:/synthetic/held";
        public bool IsValid { get; set; } = true;
        public bool TryValidateForStart() => IsValid;
        public void Dispose()
        {
            IsValid = false;
            GC.SuppressFinalize(this);
        }
    }

    // A port double only: no serialized product schema or updater implementation.
    private sealed class SyntheticPolicy : IProductPackagePolicy
    {
        public ReadOnlyMemory<byte> ObservedBytes { get; private set; }
        public IReadOnlyCollection<string>? ObservedPaths { get; private set; }

        public bool IsSafeRelativePayloadPath(string path) =>
            path is ContractFixture.ApplicationPath or ContractFixture.LauncherPath;

        public bool TryReadManifest(ReadOnlyMemory<byte> exactBytes, ManagedAppVersion expectedVersion,
            IReadOnlyCollection<string>? archivePaths, out PackageManifest? manifest)
        {
            ObservedBytes = exactBytes;
            ObservedPaths = archivePaths;
            manifest = null;
            if (!exactBytes.Span.SequenceEqual("synthetic-manifest-bytes"u8) || expectedVersion != ContractFixture.App100)
            {
                return false;
            }

            manifest = new(ContractFixture.ProductId, ContractFixture.RuntimeIdentifier, expectedVersion,
                [new(ContractFixture.ApplicationPath, 17, ContractFixture.PackageSha),
                 new(ContractFixture.LauncherPath, 123, ContractFixture.LauncherSha)],
                new(ContractFixture.App100, 1, ContractFixture.LauncherPath, 123, ContractFixture.LauncherSha));
            return true;
        }
    }
}
