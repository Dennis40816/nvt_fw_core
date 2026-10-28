// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.Files;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

internal sealed class RecoveryEvidenceFixture : IDisposable
{
    private readonly TestWorkspace _workspace = TestWorkspace.Create();
    internal const string LauncherSuffix = ".launcher";
    internal static ManagedSetupLayout Layout { get; } = new(ContractFixture.Descriptor, "Distribution.exe", "seed.json");

    private RecoveryEvidenceFixture(bool includeLauncher = false)
    {
        ManagedRoot = Path.Combine(_workspace.RootPath, "managed");
        StatePath = Path.Combine(_workspace.RootPath, "state", "state.json");
        Package = PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContractFixture.ApplicationPath] = [0x4d, 0x5a, 1],
            ["tools/worker/1.0.0/worker.exe"] = [0x4d, 0x5a, 2],
            ["THIRD-PARTY-NOTICES.txt"] = "notices"u8.ToArray(),
            ["LICENSE.txt"] = "license"u8.ToArray(),
            ["README.txt"] = "readme"u8.ToArray(),
            ["docs/contracts/policy.json"] = "{}"u8.ToArray(),
            ["profiles/built-in/index.json"] = "{}"u8.ToArray(),
        }, includeLauncher: includeLauncher);
        Admission = new(ManagedAppVersion.Parse("1.0.6"), Package.Candidate.Identity, Package.Candidate.ReleaseManifestSha256);
        Policy = new(Package.Manifest with { Version = Admission.Version });
        Seed = ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(Admission);
        ApplicationStore = new(StatePath, Seed.BindToManagedRoot(ManagedRoot));
        var launcher = Package.Manifest.Launcher;
        Launcher = ManagedLauncherIdentity.Create(ContractFixture.Descriptor, PackageFixture.FrozenLimits.MaximumExecutableBytes,
            Admission.Version, Admission.AdmissionIdentity, Admission.ReleaseManifestSha256,
            launcher?.LauncherVersion ?? Admission.Version, launcher?.ProtocolVersion ?? 1,
            ContractFixture.LauncherPath, launcher?.Size ?? 7, launcher?.Sha256 ?? new string('c', 64));
        LauncherStore = new(LauncherStatePath, null);
        Transaction = CreateTransaction(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded, 21, 19);
    }

    internal string ManagedRoot { get; }
    internal string StatePath { get; }
    internal string LauncherStatePath => LauncherBootstrapStateFile.DerivePath(StatePath, LauncherSuffix);
    internal string MarkerPath => FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(ManagedRoot);
    internal string VersionRoot => Path.Combine(ManagedRoot, "versions", Admission.Version.ToString());
    internal PackageFixture Package { get; }
    internal FixturePolicy Policy { get; }
    internal ManagedVersionAdmission Admission { get; }
    internal VersionManagerState Seed { get; }
    internal ManagedLauncherIdentity Launcher { get; }
    internal RecoveryPhysicalStateStore ApplicationStore { get; }
    // Mutable fixture configuration belongs to the owning test's single asynchronous flow.
    internal RecoveryPhysicalLauncherStore LauncherStore { get; private set; }
    internal ManagedSetupRecoveryTransaction Transaction { get; private set; }
    internal FileSystemManagedSetupRecoveryExecution Executor { get; private set; } = null!;

    internal FileSystemManagedSetupRecoveryExecution CreateExecutor(Action<int>? afterDelete = null,
        Action<long>? afterHashedFile = null, PackageVerificationLimits? limits = null,
        bool useProductionRepository = false) =>
        new(ApplicationStore, LauncherSuffix, LauncherStore, Layout,
            path => new SyntheticSeedStore(path, Seed), Policy, limits ?? PackageFixture.FrozenLimits,
            new AdmissionCodec(), new RecoveryExecutionTestHooks
            {
                Repository = useProductionRepository ? null : new SyntheticRepository(Admission),
                LauncherRepository = new FixedRecoveryLauncherRepository(Launcher),
                AfterDelete = afterDelete,
                AfterHashedFile = afterHashedFile,
            });

    internal static Task<RecoveryEvidenceFixture> CreateAsync(Action<int>? afterDelete = null,
        bool withLauncherState = false, bool readyLauncherState = false, bool includeLauncher = false)
    {
        Assert.False(withLauncherState && readyLauncherState);
        var fixture = new RecoveryEvidenceFixture(includeLauncher);
        try
        {
            fixture.CreateTree();
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
            Write(fixture.StatePath, "synthetic-state\n");
            if (withLauncherState || readyLauncherState)
            {
                var state = readyLauncherState
                    ? LauncherBootstrapState.Create(fixture.ManagedRoot, fixture.Launcher, fixture.Launcher, null, null)
                    : LauncherBootstrapState.Create(fixture.ManagedRoot, null, null, null, fixture.Launcher);
                fixture.LauncherStore = new(fixture.LauncherStatePath, state);
                Write(fixture.LauncherStatePath, "synthetic-launcher-state");
            }
            Write(fixture.MarkerPath, "exact-marker");
            fixture.Executor = fixture.CreateExecutor(afterDelete);
            return Task.FromResult(fixture);
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private void CreateTree()
    {
        Directory.CreateDirectory(VersionRoot);
        foreach (var pair in Package.Files)
        {
            string path = Path.Combine(VersionRoot, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            TestFiles.WriteAllBytes(path, pair.Value);
        }
        TestFiles.WriteAllBytes(Path.Combine(VersionRoot, "RELEASE-MANIFEST.json"), Package.ManifestBytes);
        TestFiles.WriteAllBytes(Path.Combine(VersionRoot, "SHA256SUMS.txt"), Package.ChecksumBytes);
        TestFiles.WriteAllBytes(Path.Combine(VersionRoot, ".managed-admission.v1.json"), new AdmissionCodec().Encode(Admission).ToArray());
        Write(Path.Combine(ManagedRoot, Layout.DistributionLauncherFileName), "distribution-launcher");
        Write(Path.Combine(ManagedRoot, Layout.BootstrapFileName), "immutable-bootstrap");
        Write(Path.Combine(ManagedRoot, Layout.SeedFileName), $"synthetic-seed:{Admission.Version}:{Admission.AdmissionIdentity}");
    }

    internal static Task<RecoveryEvidenceFixture> CreateEarlyStagingAsync(Action<int>? afterDelete = null,
        long launcherSize = 10, long bootstrapSize = 10) =>
        CreateMissingStatePrefixAsync(false, afterDelete, launcherSize, bootstrapSize);

    internal static Task<RecoveryEvidenceFixture> CreateTerminalSkeletonAsync() =>
        CreateMissingStatePrefixAsync(true, null, 10, 10);

    private static Task<RecoveryEvidenceFixture> CreateMissingStatePrefixAsync(bool skeleton,
        Action<int>? afterDelete, long launcherSize, long bootstrapSize)
    {
        var fixture = new RecoveryEvidenceFixture();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
            if (skeleton) Directory.CreateDirectory(fixture.VersionRoot);
            fixture.Transaction = fixture.CreateTransaction(skeleton ? ManagedSetupRecoveryPhase.RootPromoted
                : ManagedSetupRecoveryPhase.Staging, launcherSize, bootstrapSize);
            Write(fixture.MarkerPath, "exact-marker");
            fixture.Executor = fixture.CreateExecutor(afterDelete);
            return Task.FromResult(fixture);
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal static async Task<RecoveryEvidenceFixture> CreateFullStagingAsync(Action<int>? afterDelete = null)
    {
        RecoveryEvidenceFixture fixture = await CreateAsync(afterDelete);
        try
        {
            string container = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(fixture.ManagedRoot);
            Directory.CreateDirectory(container);
            new DirectoryInfo(fixture.ManagedRoot).MoveTo(Path.Combine(container, fixture.Transaction.TransactionId));
            fixture.Transaction = fixture.CreateTransaction(ManagedSetupRecoveryPhase.Staging, 21, 19);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private ManagedSetupRecoveryTransaction CreateTransaction(ManagedSetupRecoveryPhase phase, long launcherSize, long bootstrapSize) =>
        new("0123456789abcdef0123456789abcdef", ManagedRoot, StatePath, phase,
            ["managed", Path.GetFileName(MarkerPath), ".managed-setup-staging/0123456789abcdef0123456789abcdef"],
            new(launcherSize, Hash("distribution-launcher"), 10, new string('d', 64), Layout.BootstrapFileName,
                bootstrapSize, Hash("immutable-bootstrap")),
            new(1, new string('e', 64), 1, Admission.Version.ToString(), new string('f', 64),
                Path.Combine(_workspace.RootPath, "source", "catalog.json"), "registry",
                Path.Combine(_workspace.RootPath, "source"), "latest", Admission.Version.ToString(),
                Package.Candidate.PackagePath.Value!, Package.Candidate.PackageSize, Package.Candidate.PackageSha256,
                Admission.ReleaseManifestSha256, Admission.AdmissionIdentity));

    internal static void RequireNative() => RepositoryFixture.RequireWindows();
    internal static string Hash(string text) => PackageFixture.Hash(Encoding.UTF8.GetBytes(text));
    internal static string LauncherPathFor(string path) => LauncherBootstrapStateFile.DerivePath(path, LauncherSuffix);
    internal static FileSystemManagedSetupRecoveryExecution CreateUnobservedExecutor(string statePath)
    {
        var package = PackageFixture.Create();
        return new(new RecoveryPhysicalStateStore(statePath, RecoveryTestData.CanonicalAppState.State!), LauncherSuffix,
            new RecoveryPhysicalLauncherStore(LauncherPathFor(statePath), null), Layout,
            path => new SyntheticSeedStore(path, RecoveryTestData.CanonicalAppState.State!),
            package.Policy, PackageFixture.FrozenLimits, new AdmissionCodec());
    }
    internal static void Write(string path, string text) => TestFiles.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    internal static Task WriteAsync(string path, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(path, text);
        return Task.CompletedTask;
    }
    internal static Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TestFiles.WriteAllBytes(path, bytes);
        return Task.CompletedTask;
    }
    internal static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) =>
        (await BoundedFileReader.ReadFileAsync(path, null, 2_000_000, FileCaptureMode.CaptureBytes, cancellationToken)).Bytes!;
    internal static async Task<string> TextAsync(string path, CancellationToken cancellationToken) =>
        Encoding.UTF8.GetString(await ReadAsync(path, cancellationToken));
    internal static string DirectoryProof(string root) => string.Join('\n', Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => string.Concat(Path.GetRelativePath(root, path).Replace('\\', '/'), "|", HashFile(path)))
        .Order(StringComparer.Ordinal));
    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
    public void Dispose() => _workspace.Dispose();
}

internal sealed class RecoveryPhysicalStateStore(string path, VersionManagerState expected) : IManagedSetupRecoveryStateStore
{
    private readonly VersionManagerStateFile _file = new(path, 1_048_576);
    public string StatePathIdentity => _file.StatePathIdentity;
    public async ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes = await _file.ReadAsync(cancellationToken);
        return bytes is null ? new(null, File.Exists(StatePathIdentity) ? VersionManagerStateLoadIssue.Invalid : VersionManagerStateLoadIssue.Missing)
            : Encoding.UTF8.GetString(bytes).TrimEnd() == "synthetic-state" ? new(expected, VersionManagerStateLoadIssue.None)
            : new(null, VersionManagerStateLoadIssue.Invalid);
    }
    public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken) =>
        _file.TryAcquireWriteLeaseAsync(waitTimeout, cancellationToken);
    public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Recovery must not write replacement state.");
}

internal sealed class RecoveryPhysicalLauncherStore(string path, LauncherBootstrapState? expected) : ILauncherBootstrapStateStore
{
    public async ValueTask<LauncherBootstrapStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return new(null, LauncherBootstrapStateLoadIssue.Missing);
        byte[] bytes = await RecoveryEvidenceFixture.ReadAsync(path, cancellationToken);
        return expected is not null && bytes.AsSpan().SequenceEqual("synthetic-launcher-state"u8)
            ? new(expected, LauncherBootstrapStateLoadIssue.None) : new(null, LauncherBootstrapStateLoadIssue.Invalid);
    }
    public ValueTask<LauncherBootstrapStateSaveResult> TrySaveAsync(LauncherBootstrapState state, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Recovery must not write replacement launcher state.");
}

internal sealed class FixedRecoveryLauncherRepository(ManagedLauncherIdentity launcher) : IInstalledLauncherRepository
{
    public ValueTask<InstalledLauncherResult> VerifyAsync(string root, ManagedVersionAdmission admission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new InstalledLauncherResult(launcher, InstalledLauncherIssue.None));
    }
    public ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(string root,
        ManagedVersionAdmission admission, CancellationToken cancellationToken) => throw new InvalidOperationException("Recovery must not start a process.");
}

internal sealed class RecoveryLeaseSource(string path)
{
    private readonly VersionManagerStateFile _file = new(path, 1_048_576);
    internal ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _file.TryAcquireWriteLeaseAsync(timeout, cancellationToken);
}

internal sealed record ForeignToken : ManagedSetupRecoveryExecutionToken;

internal sealed class NoOpDisposable : IDisposable
{
    public void Dispose() { }
}
