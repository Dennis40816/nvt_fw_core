// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Exercises descriptor-bound executable custody and product-independent installed metadata.</summary>
public sealed class InstalledApplicationTests
{
    /// <summary>The verified application cannot be replaced until its exact repository lease is disposed.</summary>
    [Fact]
    public async Task AcquiredApplicationLeaseDeniesExecutableSwapUntilReleased()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        var installed = await fixture.InstallAsync();
        Assert.True(installed.IsSuccess, installed.Issue.ToString());
        var acquired = await fixture.Repository.AcquireApplicationLaunchLeaseAsync(fixture.ManagedRoot, installed.Admission!,
            TestContext.Current.CancellationToken);
        using var lease = Assert.IsAssignableFrom<IManagedExecutableLaunchLease>(acquired.Lease);
        Assert.True(acquired.IsAcquired, acquired.Issue.ToString());
        string executable = fixture.InstalledPath(ContractFixture.ApplicationPath);
        Assert.Equal(executable, lease.ExecutablePath);
        Assert.Throws<IOException>(() => File.Move(executable, executable + ".displaced"));
        Assert.Throws<IOException>(() => File.WriteAllBytes(executable, RepositoryFixture.PortableExecutable(9)));
        Assert.True(lease.TryValidateForStart());
        lease.Dispose();
        File.Move(executable, executable + ".displaced");
        File.Move(executable + ".displaced", executable);
        Assert.True(File.Exists(executable));
    }

    /// <summary>A child inserted between complete proof and leaf admission fails final closed-tree validation.</summary>
    [Fact]
    public async Task AddedChildAfterApplicationProofFailsClosedAndReleasesCustody()
    {
        RepositoryFixture.RequireWindows();
        string? child = null;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            BeforeLeaseCreation = () => File.WriteAllText(child!, "foreign"),
        });
        var admission = await fixture.SeedInstalledAsync();
        child = fixture.InstalledPath("unexpected.dll");
        var acquired = await fixture.Repository.AcquireApplicationLaunchLeaseAsync(fixture.ManagedRoot, admission,
            TestContext.Current.CancellationToken);
        Assert.False(acquired.IsAcquired);
        Assert.Null(acquired.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.UnsafePath, acquired.Issue);
        File.Delete(child);
        File.Move(fixture.InstalledPath(ContractFixture.ApplicationPath), fixture.InstalledPath("released.exe"));
    }

    /// <summary>A second synthetic application supplies every identity and presentation path explicitly.</summary>
    [Fact]
    public async Task SecondApplicationExposesItsInstalledVersionIdentityAndShortcutMetadata()
    {
        RepositoryFixture.RequireWindows();
        const string application = "SecondApplication.exe";
        var descriptor = ContractFixture.CreateDescriptor(productId: "SecondApplication", registryId: "second-registry",
            applicationPath: application, launcherPath: "launcher/SecondApplication.Launcher.exe",
            bootstrapFileName: "SecondApplication.Bootstrap.exe",
            archiveRootName: ContractFixture.Descriptor.GetArchiveRootName);
        var package = PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [application] = RepositoryFixture.PortableExecutable(),
        }, project: projection => projection with { ProductId = descriptor.ProductId });
        using var fixture = new RepositoryFixture(package);
        var repository = new FileSystemManagedVersionRepository(descriptor, package.Policy, PackageFixture.FrozenLimits, fixture.Codec);
        var installed = await repository.InstallAsync(fixture.ManagedRoot, fixture.SourceRoot, package.Candidate,
            TestContext.Current.CancellationToken);
        Assert.True(installed.IsSuccess, installed.Issue.ToString());
        string stableEntry = Path.Combine(fixture.ManagedRoot, descriptor.BootstrapExecutableFileName);
        var metadata = await repository.ReadInstalledApplicationAsync(fixture.ManagedRoot, installed.Admission!,
            "Second Application", stableEntry, application, TestContext.Current.CancellationToken);
        Assert.NotNull(metadata);
        Assert.Equal(PackageFixture.Version, metadata.Version);
        Assert.Equal("SecondApplication", metadata.ProductId);
        Assert.Equal("Second Application", metadata.DisplayName);
        Assert.Equal(fixture.InstalledPath(application), metadata.ExecutablePath);
        Assert.Equal(stableEntry, metadata.LaunchEntryPoint);
        Assert.Equal(metadata.ExecutablePath, metadata.IconPath);
    }

    /// <summary>Shortcut presentation paths remain explicit and cannot escape the verified payload.</summary>
    [Theory]
    [InlineData("relative-entry", "README.txt")]
    [InlineData("absolute-entry", "../escape.ico")]
    [InlineData("absolute-entry", "icon:stream")]
    public async Task ShortcutMetadataRejectsUnsafePresentationPaths(string entryKind, string icon)
    {
        using var fixture = new RepositoryFixture();
        var admission = new ManagedVersionAdmission(PackageFixture.Version, "admission", new string('a', 64));
        string entry = entryKind == "relative-entry" ? "relative.exe" : fixture.PathFor("entry.exe");
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Repository.ReadInstalledApplicationAsync(
            fixture.ManagedRoot, admission, "Application", entry, icon, TestContext.Current.CancellationToken));
    }

    /// <summary>Outer launcher acquisition hashes its own executable; full application activation rechecks all members.</summary>
    [Fact]
    public async Task InstalledLauncherLeasePreservesSeparateApplicationContentProof()
    {
        RepositoryFixture.RequireWindows();
        byte[] launcherBytes = RepositoryFixture.PortableExecutable(2);
        var package = PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContractFixture.ApplicationPath] = RepositoryFixture.PortableExecutable(),
            [ContractFixture.LauncherPath] = launcherBytes,
            ["README.txt"] = "readme"u8.ToArray(),
        }, project: manifest => manifest with
        {
            Launcher = new(PackageFixture.Version, 1, ContractFixture.LauncherPath,
                launcherBytes.LongLength, PackageFixture.Hash(launcherBytes)),
        });
        using var fixture = new RepositoryFixture(package);
        var admission = await fixture.SeedInstalledAsync();
        var launcherRepository = new FileSystemInstalledLauncherRepository(ContractFixture.Descriptor, package.Policy,
            PackageFixture.FrozenLimits, fixture.Codec);
        var verified = await launcherRepository.VerifyAsync(fixture.ManagedRoot, admission, TestContext.Current.CancellationToken);
        Assert.True(verified.IsVerified, verified.Issue.ToString());
        Assert.True(verified.Identity!.MatchesOwner(admission));
        await File.WriteAllTextAsync(fixture.InstalledPath("README.txt"), "changed", TestContext.Current.CancellationToken);
        var acquired = await launcherRepository.AcquireLaunchLeaseAsync(fixture.ManagedRoot, admission, TestContext.Current.CancellationToken);
        using var lease = acquired.Lease;
        Assert.True(acquired.IsAcquired, acquired.Issue.ToString());
        Assert.Equal(verified.Identity, acquired.Identity);
        Assert.True(lease!.TryValidateForStart());
        lease.Dispose();
        Assert.Equal(InstalledLauncherIssue.Tampered,
            (await launcherRepository.VerifyAsync(fixture.ManagedRoot, admission, TestContext.Current.CancellationToken)).Issue);
        Assert.Equal(ManagedExecutableLaunchIssue.Tampered,
            (await fixture.Repository.AcquireApplicationLaunchLeaseAsync(fixture.ManagedRoot, admission,
                TestContext.Current.CancellationToken)).Issue);
    }
}
