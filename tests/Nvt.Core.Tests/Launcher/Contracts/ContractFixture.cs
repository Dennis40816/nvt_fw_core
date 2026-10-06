// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Tests.Launcher.Contracts;

internal static class ContractFixture
{
    internal const string ProductId = "FixtureProduct";
    internal const string RuntimeIdentifier = "test-runtime";
    internal const string ApplicationPath = "FixtureProduct.exe";
    internal const string LauncherPath = "launcher/FixtureProduct.Launcher.exe";
    internal const string BootstrapFileName = "FixtureProduct.Bootstrap.exe";
    internal const string LauncherSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string PackageSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    internal const string ManifestSha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    internal static readonly ManagedAppVersion App100 = ManagedAppVersion.Parse("1.0.0");
    internal static readonly ManagedAppVersion App101 = ManagedAppVersion.Parse("1.0.1");

    internal static LauncherProtocolNames ProtocolNames { get; } = new(
        "FIXTURE_APP_READY", "FIXTURE_APP_VERSION", "FIXTURE_LAUNCHER_READY",
        "FIXTURE_LAUNCHER_IDENTITY", "FIXTURE_BOOTSTRAP_ADMISSION", "FIXTURE_BOOTSTRAP_START_CONTEXT",
        "FIXTURE_BOOTSTRAP_START", "FIXTURE_LIFETIME_CONTEXT", "FIXTURE_LIFETIME_HANDLE",
        "FIXTURE_LIFETIME_JOB", "FIXTURE_LIFETIME_STATE", "FIXTURE_LIFETIME_KIND", "FIXTURE_BOOTSTRAP_IDENTITY");

    internal static ProductDescriptor Descriptor { get; } = CreateDescriptor();

    internal static ProductDescriptor CreateDescriptor(
        string productId = ProductId,
        string runtimeIdentifier = RuntimeIdentifier,
        string registryId = "fixture-registry",
        string applicationPath = ApplicationPath,
        string launcherPath = LauncherPath,
        string bootstrapFileName = BootstrapFileName,
        Func<ManagedAppVersion, string>? archiveRootName = null) =>
        new(productId, runtimeIdentifier, registryId, applicationPath, launcherPath, bootstrapFileName,
            archiveRootName ?? (static version => $"FixtureProduct-v{version}-test-runtime"), ProtocolNames);

    internal static ManagedLauncherIdentity CreateIdentity(
        ManagedAppVersion? owner = null,
        string ownerAdmissionIdentity = "admission-1.0.0",
        string ownerReleaseManifestSha256 = ManifestSha,
        int protocolVersion = ManagedLauncherIdentity.SupportedProtocolVersion,
        string executableRelativePath = LauncherPath,
        long size = 123,
        string sha256 = LauncherSha,
        long maximumExecutableBytes = ManagedImmutableBootstrapIdentity.MaximumExecutableBytes,
        ProductDescriptor? descriptor = null) =>
        ManagedLauncherIdentity.Create(descriptor ?? Descriptor, maximumExecutableBytes,
            owner ?? App100, ownerAdmissionIdentity, ownerReleaseManifestSha256, App100,
            protocolVersion, executableRelativePath, size, sha256);

    internal static ManagedVersionAdmission Admission(
        ManagedAppVersion version,
        string admissionIdentity,
        char manifestHash) => new(version, admissionIdentity, new string(manifestHash, 64));

    // Synthetic ceilings equal to NFC's frozen catalog values.
    internal const long MaximumPackageBytes = 134_217_728;
    internal const int MaximumReleaseNoteBytes = 64 * 1024;

    internal static UpdateCatalogVersionSnapshot CreateSnapshot(
        ManagedAppVersion? version = null,
        DateTimeOffset? publishedAt = null,
        string packagePath = "packages/FixtureProduct-v1.0.0-test-runtime.zip",
        long packageSize = 42,
        string packageSha = PackageSha,
        string manifestSha = ManifestSha,
        string releaseNotes = "Synthetic release notes",
        UpdateNotificationPolicy notificationPolicy = UpdateNotificationPolicy.Notify,
        long maximumPackageBytes = MaximumPackageBytes,
        int maximumReleaseNoteBytes = MaximumReleaseNoteBytes) =>
        UpdateCatalogVersionSnapshot.Create(maximumPackageBytes, maximumReleaseNoteBytes, version ?? App100,
            publishedAt ?? new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
            new UpdateCatalogPackagePath(packagePath), packageSize, packageSha, manifestSha,
            releaseNotes, notificationPolicy);
}
