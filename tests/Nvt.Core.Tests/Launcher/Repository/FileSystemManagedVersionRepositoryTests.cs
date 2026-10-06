// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Characterizes closed installation, inventory and exact admitted deletion.</summary>
public sealed class FileSystemManagedVersionRepositoryTests
{
    /// <summary>A malformed ZIP preserves distinct verification and installation failures without a partial target.</summary>
    [Fact]
    public async Task MalformedZipFailsWithoutPartialInstallation()
    {
        var package = PackageFixture.Create(mutatePackage: bytes =>
        {
            Array.Clear(bytes);
            return bytes;
        });
        using var fixture = new RepositoryFixture(package);
        var verified = await fixture.Repository.VerifyPackageAsync(fixture.SourceRoot, package.Candidate,
            TestContext.Current.CancellationToken);
        Assert.False(verified.IsVerified);
        Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, verified.Issue);
        RepositoryFixture.RequireWindows();
        var installed = await fixture.InstallAsync();
        Assert.False(installed.IsSuccess);
        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, installed.Issue);
        Assert.False(Directory.Exists(fixture.VersionRoot));
        fixture.AssertEmptyStaging();
    }

    /// <summary>A complete installation promotes and later physical content tamper is damaged.</summary>
    [Fact]
    public async Task InstallPromotesClosedPayloadAndInventoryDetectsTamper()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        var installed = await fixture.InstallAsync();
        Assert.True(installed.IsSuccess, installed.Issue.ToString());
        var healthy = await fixture.InventoryAsync(installed.Admission!);
        await File.AppendAllTextAsync(fixture.InstalledPath("README.txt"), "tampered", TestContext.Current.CancellationToken);
        var damaged = await fixture.InventoryAsync(installed.Admission!);
        Assert.False(installed.WasAlreadyInstalled);
        Assert.Equal(1, healthy.Inventory!.HealthyCount);
        Assert.Equal(1, damaged.Inventory!.DamagedCount);
        Assert.Equal(ManagedVersionDamageReason.ContentMismatch, Assert.Single(damaged.Inventory.Versions).DamageReason);
        fixture.AssertEmptyStaging();
    }

    /// <summary>Only an identical same-version identity is idempotent; changed content cannot overwrite it.</summary>
    [Fact]
    public async Task IdenticalInstallIsIdempotentButChangedIdentityConflicts()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        var first = await fixture.InstallAsync();
        var second = await fixture.InstallAsync();
        var changed = PackageFixture.Create(mutateManifest: bytes => [.. bytes, (byte)' ']);
        string otherSource = fixture.PathFor("other-source");
        string changedPath = Path.Combine(otherSource, changed.Candidate.PackagePath.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(changedPath)!);
        await File.WriteAllBytesAsync(changedPath, changed.PackageBytes, TestContext.Current.CancellationToken);
        var conflict = await new FileSystemManagedVersionRepository(ContractFixture.Descriptor, changed.Policy,
            PackageFixture.FrozenLimits, fixture.Codec).InstallAsync(fixture.ManagedRoot, otherSource, changed.Candidate,
                TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.Issue.ToString());
        Assert.True(second.IsSuccess, second.Issue.ToString());
        Assert.True(second.WasAlreadyInstalled);
        Assert.Equal(ManagedVersionInstallIssue.IdentityConflict, conflict.Issue);
        Assert.Equal(first.Admission, second.Admission);
        Assert.Equal(fixture.Package.Files["README.txt"], await File.ReadAllBytesAsync(fixture.InstalledPath("README.txt"),
            TestContext.Current.CancellationToken));
    }

    /// <summary>Uncommitted self-admission remains an observation rather than committed authority.</summary>
    [Fact]
    public async Task HealthySelfAdmissionWithoutCommittedStateIsOnlyObservedUnadmittedFact()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        var installed = await fixture.InstallAsync();
        var observed = await fixture.Repository.InventoryAsync(fixture.ManagedRoot, [], null, null, null,
            TestContext.Current.CancellationToken);
        var row = Assert.Single(observed.Inventory!.Versions);
        Assert.True(installed.IsSuccess);
        Assert.Equal(ManagedVersionAdmissionState.Unadmitted, row.AdmissionState);
        Assert.Equal(installed.Admission, row.ObservedAdmission);
        Assert.Equal(ManagedVersionIntegrity.Healthy, row.Integrity);
    }

    /// <summary>An unadmitted directory is damaged and a forged deletion cannot remove it.</summary>
    [Fact]
    public async Task UnadmittedDirectoryIsDamagedAndForgedDeleteIsBlocked()
    {
        using var fixture = new RepositoryFixture();
        Directory.CreateDirectory(fixture.VersionRoot);
        await File.WriteAllTextAsync(fixture.InstalledPath("unknown.txt"), "unknown", TestContext.Current.CancellationToken);
        var observed = await fixture.Repository.InventoryAsync(fixture.ManagedRoot, [], null, null, null,
            TestContext.Current.CancellationToken);
        var deleted = await fixture.Repository.DeleteAsync(fixture.ManagedRoot,
            new(PackageFixture.Version, "forged", new string('a', 64)), null, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionDamageReason.UnexpectedPath, Assert.Single(observed.Inventory!.Versions).DamageReason);
        Assert.Equal(ManagedVersionDeleteIssue.UnsafeTarget, deleted);
        Assert.True(Directory.Exists(fixture.VersionRoot));
    }

    /// <summary>Any enumeration failure discards previously observed facts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InventoryEnumerationFailureReturnsUnavailableWithoutPartialFacts(bool permissionDenied)
    {
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            EnumerateDirectories = _ => permissionDenied
                ? throw new UnauthorizedAccessException("Injected inventory denial.")
                : throw new IOException("Injected inventory read failure."),
        });
        Directory.CreateDirectory(Path.Combine(fixture.ManagedRoot, "versions"));
        var result = await fixture.InventoryAsync(new(PackageFixture.Version, "admission", new string('a', 64)));
        Assert.False(result.IsSuccess);
        Assert.Equal(ManagedVersionInventoryReadIssue.Unavailable, result.Issue);
        Assert.Null(result.Inventory);
    }

    /// <summary>Missing, extra, modified manifest and failed activation retain their exact damage categories.</summary>
    [Theory]
    [InlineData("missing", ManagedVersionDamageReason.MissingFile)]
    [InlineData("extra", ManagedVersionDamageReason.UnexpectedPath)]
    [InlineData("manifest", ManagedVersionDamageReason.ManifestMismatch)]
    [InlineData("failed", ManagedVersionDamageReason.FailedActivation)]
    public async Task InventoryReportsExactDamageReason(string mutation, ManagedVersionDamageReason expected)
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        var installed = await fixture.InstallAsync();
        Assert.True(installed.IsSuccess, installed.Issue.ToString());
        if (mutation == "missing")
        {
            File.Delete(fixture.InstalledPath("README.txt"));
        }
        else if (mutation == "extra")
        {
            await File.WriteAllTextAsync(fixture.InstalledPath("extra.txt"), "extra", TestContext.Current.CancellationToken);
        }
        else if (mutation == "manifest")
        {
            await File.AppendAllTextAsync(fixture.InstalledPath("RELEASE-MANIFEST.json"), " ", TestContext.Current.CancellationToken);
        }
        var result = await fixture.InventoryAsync(installed.Admission!, mutation == "failed" ? installed.Admission!.Version : null);
        Assert.Equal(expected, Assert.Single(result.Inventory!.Versions).DamageReason);
    }

    /// <summary>Active protection precedes filesystem checks and only the exact non-active admission is deleted.</summary>
    [Fact]
    public async Task DeleteProtectsActiveAndRemovesOnlyExactAdmittedChild()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        var installed = await fixture.InstallAsync();
        Assert.True(installed.IsSuccess, installed.Issue.ToString());
        Assert.Equal(ManagedVersionDeleteIssue.ActiveVersion, await fixture.Repository.DeleteAsync(fixture.ManagedRoot,
            installed.Admission!, installed.Admission!.Version, TestContext.Current.CancellationToken));
        Assert.Equal(ManagedVersionDeleteIssue.UnsafeTarget, await fixture.Repository.DeleteAsync(fixture.ManagedRoot,
            installed.Admission with { AdmissionIdentity = "forged" }, null, TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(fixture.VersionRoot));
        Assert.Equal(ManagedVersionDeleteIssue.None, await fixture.Repository.DeleteAsync(fixture.ManagedRoot,
            installed.Admission, null, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(fixture.VersionRoot));
        Assert.Equal(ManagedVersionDeleteIssue.NotInstalled, await fixture.Repository.DeleteAsync(fixture.ManagedRoot,
            installed.Admission, null, TestContext.Current.CancellationToken));
    }

    /// <summary>A missing managed root remains absent rather than being bootstrapped by an update.</summary>
    [Fact]
    public async Task InstallRejectsMissingManagedRootWithoutCreatingResidue()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        string missing = fixture.PathFor("missing");
        var result = await fixture.Repository.InstallAsync(missing, fixture.SourceRoot, fixture.Package.Candidate,
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInstallIssue.PromotionFailed, result.Issue);
        Assert.False(Directory.Exists(missing));
    }
}
