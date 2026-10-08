// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Files;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Characterizes the read-only installation query and exact terminal snapshot checks.</summary>
public sealed class LauncherInstallationSelfTestTests
{
    /// <summary>A healthy query observes the Bootstrap and exact active launcher without writing any state.</summary>
    [Fact]
    public async Task HealthyQueryPreservesExactOwnerAndPerformsNoWrites()
    {
        using var fixture = new SelfTestFixture();
        var result = await fixture.Query().QueryAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsHealthy, result.Issue.ToString());
        Assert.Equal(fixture.Identity.OwnerAdmissionIdentity, result.ActiveLauncher!.OwnerAdmission.AdmissionIdentity);
        Assert.Equal(fixture.Identity.LauncherVersion, result.ActiveLauncher.LauncherVersion);
        Assert.Equal(fixture.BootstrapPath, result.Bootstrap!.Path);
        Assert.Equal(3, result.Bootstrap.Size);
        Assert.Equal(2, fixture.AppReader.Reads);
        Assert.Equal(2, fixture.LauncherStore.Reads);
        Assert.Equal(0, fixture.LauncherStore.Writes);
    }

    /// <summary>A terminal authority change discards both observations and reports StateChanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalSnapshotChangePublishesNoPartialFacts(bool launcherChanged)
    {
        using var fixture = new SelfTestFixture();
        if (launcherChanged)
        {
            fixture.LauncherStore.Terminal = new(LauncherBootstrapState.Create(fixture.Root, fixture.Identity,
                fixture.Identity, null, fixture.Identity), LauncherBootstrapStateLoadIssue.None);
        }
        else
        {
            fixture.AppReader.Terminal = new(VersionManagerState.Create(null, fixture.Identity.OwnerAppVersion,
                fixture.Identity.OwnerAppVersion, [fixture.Admission], null, null, true,
                managedRootIdentity: fixture.Root), VersionManagerStateLoadIssue.None);
        }
        var result = await fixture.Query().QueryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LauncherInstallationSelfTestIssue.StateChanged, result.Issue);
        Assert.Null(result.Bootstrap);
        Assert.Null(result.ActiveLauncher);
        Assert.Equal(0, fixture.LauncherStore.Writes);
    }

    /// <summary>The source's application read categories map in order before any launcher observation.</summary>
    [Theory]
    [InlineData(VersionManagerStateLoadIssue.Missing, LauncherInstallationSelfTestIssue.AppStateMissing)]
    [InlineData(VersionManagerStateLoadIssue.Invalid, LauncherInstallationSelfTestIssue.AppStateInvalid)]
    [InlineData(VersionManagerStateLoadIssue.Unavailable, LauncherInstallationSelfTestIssue.AppStateUnavailable)]
    [InlineData(VersionManagerStateLoadIssue.ManagedRootMismatch, LauncherInstallationSelfTestIssue.ManagedRootMismatch)]
    public async Task AppReadFailurePrecedesLauncherRead(VersionManagerStateLoadIssue input, LauncherInstallationSelfTestIssue expected)
    {
        using var fixture = new SelfTestFixture();
        fixture.AppReader.Initial = new(null, input);
        var result = await fixture.Query().QueryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Issue);
        Assert.Null(result.Bootstrap);
        Assert.Null(result.ActiveLauncher);
        Assert.Equal(0, fixture.LauncherStore.Reads);
    }

    /// <summary>The fixed executable ceiling rejects zero, negative and one byte above the frozen maximum.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(200000001)]
    public void SelfTestRejectsInvalidBootstrapLimit(int maximumBytes)
    {
        using var fixture = new SelfTestFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Query(maximumBytes));
    }

    /// <summary>Bootstrap observation admits the exact frozen 200,000,000-byte ceiling and one byte below it.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task BootstrapObservationPreservesFrozenBoundary(int delta)
    {
        using var fixture = new SelfTestFixture();
        using (var output = new FileStream(fixture.BootstrapPath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            output.SetLength(200_000_000L + delta);
        }
        var result = await fixture.Query(200_000_000).QueryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(delta <= 0, result.IsHealthy);
        Assert.Equal(delta <= 0 ? LauncherInstallationSelfTestIssue.None : LauncherInstallationSelfTestIssue.BootstrapInvalid,
            result.Issue);
        if (delta <= 0)
        {
            Assert.Equal(200_000_000L + delta, result.Bootstrap!.Size);
        }
        else
        {
            Assert.Null(result.Bootstrap);
            Assert.Null(result.ActiveLauncher);
        }
    }
}

internal sealed class SelfTestFixture : IDisposable
{
    private readonly TestWorkspace workspace = new();

    internal SelfTestFixture()
    {
        Root = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        Admission = ContractFixture.Admission(ContractFixture.App100, "self-test-admission", 'c');
        Identity = ContractFixture.CreateIdentity(owner: Admission.Version, ownerAdmissionIdentity: Admission.AdmissionIdentity,
            ownerReleaseManifestSha256: Admission.ReleaseManifestSha256);
        AppReader = new() { Initial = new(VersionManagerState.Create(null, Admission.Version, Admission.Version,
            [Admission], null, null, false, managedRootIdentity: Root), VersionManagerStateLoadIssue.None) };
        LauncherStore = new() { Initial = new(LauncherBootstrapState.Create(Root, Identity, Identity, null, null),
            LauncherBootstrapStateLoadIssue.None) };
        BootstrapPath = workspace.Write("managed/" + ContractFixture.BootstrapFileName, [1, 2, 3]);
    }

    internal string Root { get; }
    internal string BootstrapPath { get; }
    internal ManagedVersionAdmission Admission { get; }
    internal ManagedLauncherIdentity Identity { get; }
    internal SelfTestAppReader AppReader { get; }
    internal SelfTestLauncherStore LauncherStore { get; }
    internal FileSystemLauncherInstallationSelfTest Query(int maximumBytes = 200_000_000) =>
        new(ContractFixture.Descriptor, maximumBytes, Root, AppReader, LauncherStore, new SelfTestLauncherRepository(Identity));
    public void Dispose() => workspace.Dispose();
}

internal sealed class SelfTestAppReader : IVersionManagerStateReader
{
    internal VersionManagerStateLoadResult Initial { get; set; } = null!;
    internal VersionManagerStateLoadResult? Terminal { get; set; }
    internal int Reads { get; private set; }
    public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(++Reads == 1 ? Initial : Terminal ?? Initial);
    }
}

internal sealed class SelfTestLauncherStore : ILauncherBootstrapStateStore
{
    internal LauncherBootstrapStateLoadResult Initial { get; set; } = null!;
    internal LauncherBootstrapStateLoadResult? Terminal { get; set; }
    internal int Reads { get; private set; }
    internal int Writes { get; private set; }
    public ValueTask<LauncherBootstrapStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(++Reads == 1 ? Initial : Terminal ?? Initial);
    }
    public ValueTask<LauncherBootstrapStateSaveResult> TrySaveAsync(LauncherBootstrapState state, CancellationToken cancellationToken)
    {
        Writes++;
        throw new InvalidOperationException("Read-only self-test must not write.");
    }
}

internal sealed class SelfTestLauncherRepository(ManagedLauncherIdentity identity) : IInstalledLauncherRepository
{
    public ValueTask<InstalledLauncherResult> VerifyAsync(string managedRoot, ManagedVersionAdmission admission,
        CancellationToken cancellationToken) => ValueTask.FromResult(new InstalledLauncherResult(identity, InstalledLauncherIssue.None));
    public ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(string managedRoot, ManagedVersionAdmission admission,
        CancellationToken cancellationToken) => throw new InvalidOperationException("Read-only self-test must not acquire a launch lease.");
}
