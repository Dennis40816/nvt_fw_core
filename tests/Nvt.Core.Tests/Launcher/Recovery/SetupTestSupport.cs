// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nvt.Core.Files;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Repository;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

internal sealed class SetupFixture : IDisposable
{
    internal const int Ceiling = 65_536;
    private readonly Nvt.Core.TestSupport.TestWorkspace _workspace = Nvt.Core.TestSupport.TestWorkspace.Create();
    internal SetupFixture()
    {
        Root = Path.Combine(Parent, "managed");
        StatePath = Path.Combine(Parent, "state.json");
        string source = Directory.CreateDirectory(Path.Combine(Parent, "source")).FullName;
        var package = ContractFixture.CreateSnapshot(version: ManagedAppVersion.Parse("1.0.4"), packageSize: 4096);
        Candidate = new(new FreshInstallationCandidateIdentity(128, "fixture-registry", 1, HashA, 1,
            package.Version, HashB, Path.Combine(source, "catalog.json"), source, FreshInstallationSourceStatus.Latest,
            package.PackagePath.Value!, package.PackageSize, package.PackageSha256, package.ReleaseManifestSha256),
            package, new VerifiedUpdateCandidate(package.Version, package.Identity, package.ReleaseNotes));
        Admission = new(package.Version, package.Identity, package.ReleaseManifestSha256);
        Seed = ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(Admission);
        Repository = new(Admission);
    }

    internal string Parent => _workspace.RootPath;
    internal string Root { get; }
    internal string StatePath { get; }
    internal static string HashA => new('a', 64);
    internal static string HashB => new('b', 64);
    internal static ManagedSetupLayout Layout { get; } = new(ContractFixture.Descriptor, "Distribution.exe", "seed.json");
    internal FreshInstallationCandidate Candidate { get; }
    internal ManagedVersionAdmission Admission { get; }
    internal VersionManagerState Seed { get; }
    internal SyntheticRepository Repository { get; }
    internal SyntheticMarkerCodec Codec { get; } = new();
    internal string MarkerPath => FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(Root);
    internal string StagingPath => FileSystemManagedInstallationRootProbe.GetStagingContainerPath(Root);

    internal IVersionManagerStateStore SeedStore(string path) => new SyntheticSeedStore(path, Seed);
    internal FileSystemManagedFirstInstallationRootMaterializer Materializer(ManagedSetupTestHooks? hooks = null,
        Action<Exception>? diagnostics = null) => new(Repository, Repository.InstallHeldAsync,
            new WindowsStableTreeLimits(10_001, 10_000, 1_000_000_000), Layout, SeedStore, Codec,
            Ceiling, Ceiling, ManagedImmutableBootstrapIdentity.MaximumExecutableBytes, hooks ?? new(), diagnostics);
    internal FileSystemManagedSetupRecoveryProbe Probe() => new(Codec, Ceiling, Ceiling);
    internal async ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        ManagedSetupTestHooks? hooks = null, TestPayloadCapture? payload = null, CancellationToken? token = null)
    {
        using var owned = payload is null ? new TestPayloadCapture(PayloadIdentity()) : null;
        return await Materializer(hooks).MaterializeAsync(Root, StatePath, payload ?? owned!, Candidate, Seed,
            token ?? TestContext.Current.CancellationToken);
    }

    internal ManagedSetupMarker Marker(string phase = "staging") => ManagedSetupMarker.Create(
        "0123456789abcdef0123456789abcdef", Root, StatePath,
        Path.Combine(StagingPath, "0123456789abcdef0123456789abcdef"), PayloadIdentity(), Candidate, phase);
    internal static ManagedDistributionPayloadIdentity PayloadIdentity() => new(Ceiling,
        ManagedAppVersion.Parse("1.0.4"), new string('c', 40), "distribution-launcher"u8.Length,
        Hash("distribution-launcher"u8), 512, HashA, ManagedImmutableBootstrapIdentity.Create(
            ContractFixture.Descriptor, Layout.BootstrapFileName, "immutable-bootstrap"u8.Length, Hash("immutable-bootstrap"u8)));
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void RequireNative() => RepositoryFixture.RequireWindows();
    internal static async Task<byte[]> ReadAsync(string path) => (await BoundedFileReader.ReadFileAsync(path,
        allowedRoots: null, 2_000_000, FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken)).Bytes!;
    internal static async Task<string> TextAsync(string path) => Encoding.UTF8.GetString(await ReadAsync(path));
    internal static void Write(string path, string value) => TestFiles.WriteAllBytes(path, Encoding.UTF8.GetBytes(value));
    internal async Task<SortedDictionary<string, string>> SnapshotAsync()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFileSystemEntries(Parent, "*", SearchOption.AllDirectories))
            result[Path.GetRelativePath(Parent, path)] = Directory.Exists(path) ? "directory" : Convert.ToBase64String(await ReadAsync(path));
        return result;
    }
    public void Dispose() => _workspace.Dispose();
}

internal sealed class SyntheticMarkerCodec : IManagedSetupMarkerCodec
{
    internal int? PadTo { get; init; }
    internal Func<ManagedSetupMarker, ManagedSetupMarker>? Transform { get; init; }
    internal Action<ManagedSetupMarker>? BeforeEncode { get; init; }
    internal bool Reject { get; init; }
    public ReadOnlyMemory<byte> Encode(ManagedSetupMarker marker)
    {
        BeforeEncode?.Invoke(marker);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(marker);
        if (PadTo is { } size && size > bytes.Length)
        {
            int length = bytes.Length;
            Array.Resize(ref bytes, size);
            bytes.AsSpan(length).Fill((byte)' ');
        }
        return bytes;
    }
    public ManagedSetupMarker? Decode(ReadOnlyMemory<byte> exactBytes)
    {
        if (Reject) return null;
        try
        {
            var marker = JsonSerializer.Deserialize<ManagedSetupMarker>(exactBytes.Span);
            return marker is not null && Transform is not null ? Transform(marker) : marker;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed class SyntheticSeedStore(string path, VersionManagerState seed) : IVersionManagerStateStore
{
    private readonly VersionManagerStateFile _file = new(path, 1_048_576);
    private static byte[] Bytes(VersionManagerState seed) => Encoding.UTF8.GetBytes(
        $"synthetic-seed:{seed.Admissions[0].Version}:{seed.Admissions[0].AdmissionIdentity}");
    public async ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes = await _file.ReadAsync(cancellationToken);
        return bytes is not null && bytes.SequenceEqual(Bytes(seed)) ? new(seed, VersionManagerStateLoadIssue.None)
            : new(null, VersionManagerStateLoadIssue.Invalid);
    }
    public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken) => _file.WriteAsync(Bytes(state), cancellationToken);
    public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout,
        CancellationToken cancellationToken) => throw new InvalidOperationException("Seed setup does not acquire the application writer.");
}

internal sealed class TestPayloadCapture(ManagedDistributionPayloadIdentity identity, string launcher = "distribution-launcher")
    : IManagedDistributionPayloadCapture, IManagedDistributionPayloadContent
{
    public ManagedDistributionPayloadIdentity Identity => identity;
    internal int CopyCount { get; private set; }
    public ValueTask CopyDistributionLauncherAsync(string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); CopyCount++;
        SetupFixture.Write(destination, launcher); return ValueTask.CompletedTask;
    }
    public ValueTask CopyBootstrapAsync(string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); CopyCount++;
        SetupFixture.Write(destination, "immutable-bootstrap"); return ValueTask.CompletedTask;
    }
    public void Dispose() { }
}

internal sealed class SyntheticRepository(ManagedVersionAdmission admission) : IManagedVersionRepository
{
    internal int InstallCount { get; private set; }
    internal int InventoryCount { get; private set; }
    internal Action<int, string>? InventoryObserved { get; init; }
    internal bool CreateRepositoryStaging { get; init; }
    internal ValueTask<ManagedVersionInstallResult> InstallHeldAsync(WindowsStableRelativeWriteRoot root,
        string source, UpdateCatalogVersionSnapshot package, IProgress<ManagedFirstInstallationProgress>? _,
        CancellationToken cancellationToken) => InstallAsync(root.RootPath, source, package, cancellationToken);
    public ValueTask<ManagedVersionInstallResult> InstallAsync(string root, string source,
        UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); InstallCount++;
        if (CreateRepositoryStaging) Directory.CreateDirectory(Path.Combine(root, ".staging"));
        string versionRoot = Directory.CreateDirectory(Path.Combine(root, "versions", package.Version.ToString())).FullName;
        SetupFixture.Write(Path.Combine(versionRoot, "payload.txt"), "installed");
        return ValueTask.FromResult(new ManagedVersionInstallResult(admission, ManagedVersionInstallIssue.None, false));
    }
    public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string root, IReadOnlyList<ManagedVersionAdmission> admissions,
        ManagedAppVersion? active, ManagedAppVersion? lastKnownGood, ManagedAppVersion? failed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); InventoryCount++; InventoryObserved?.Invoke(InventoryCount, root);
        return ValueTask.FromResult(ManagedVersionInventoryReadResult.Success(ManagedVersionInventory.Create(
            [new(admission.Version, admission.AdmissionIdentity, ManagedVersionIntegrity.Healthy, null,
                true, true, ManagedVersionAdmissionState.Admitted, admission)])));
    }
    public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(string source, UpdateCatalogVersionSnapshot package,
        CancellationToken cancellationToken) => throw new InvalidOperationException("No source work is authorized.");
    public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string root, ManagedVersionAdmission target,
        ManagedAppVersion? active, CancellationToken cancellationToken) => throw new InvalidOperationException("No recovery mutation is authorized.");
}
