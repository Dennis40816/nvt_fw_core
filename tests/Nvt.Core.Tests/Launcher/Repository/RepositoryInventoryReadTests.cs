// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Characterizes whole-observation failure and installed proof without native promotion.</summary>
public sealed class RepositoryInventoryReadTests
{
    /// <summary>An enumerated directory disappearing before observation discards the whole inventory.</summary>
    [Fact]
    public async Task EnumeratedDirectoryDisappearingBeforeObservationReturnsUnavailable()
    {
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            EnumerateDirectories = root => EnumerateAfterDeleting(Path.Combine(root, PackageFixture.Version.ToString())),
        });
        Directory.CreateDirectory(fixture.VersionRoot);
        var result = await fixture.Repository.InventoryAsync(fixture.ManagedRoot, [], null, null, null,
            TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(ManagedVersionInventoryReadIssue.Unavailable, result.Issue);
        Assert.Null(result.Inventory);
    }

    /// <summary>A terminal failed directory observation discards an otherwise complete self-admitted proof.</summary>
    [Fact]
    public async Task SelfAdmittedDirectoryDisappearingAfterVerificationReturnsUnavailable()
    {
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations { DirectoryExists = _ => false });
        await fixture.SeedInstalledAsync();
        var result = await fixture.Repository.InventoryAsync(fixture.ManagedRoot, [], null, null, null,
            TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(ManagedVersionInventoryReadIssue.Unavailable, result.Issue);
        Assert.Null(result.Inventory);
    }

    /// <summary>Read-only installed proof distinguishes missing and extra members, digest damage and malformed admission.</summary>
    [Theory]
    [InlineData("missing", ManagedVersionDamageReason.MissingFile)]
    [InlineData("extra", ManagedVersionDamageReason.UnexpectedPath)]
    [InlineData("digest", ManagedVersionDamageReason.ContentMismatch)]
    [InlineData("admission", ManagedVersionDamageReason.ManifestMismatch)]
    public async Task PhysicalInstalledProofPreservesDamageCategories(string mutation, ManagedVersionDamageReason expected)
    {
        using var fixture = new RepositoryFixture();
        var admission = await fixture.SeedInstalledAsync();
        if (mutation == "missing")
        {
            File.Delete(fixture.InstalledPath("README.txt"));
        }
        else if (mutation == "extra")
        {
            await File.WriteAllTextAsync(fixture.InstalledPath("extra.txt"), "extra", TestContext.Current.CancellationToken);
        }
        else if (mutation == "digest")
        {
            await File.WriteAllTextAsync(fixture.InstalledPath("README.txt"), "foobar", TestContext.Current.CancellationToken);
        }
        else
        {
            await File.WriteAllTextAsync(fixture.InstalledPath(".managed-admission.v1.json"), "{broken-json",
                TestContext.Current.CancellationToken);
        }
        var result = await fixture.InventoryAsync(admission);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Assert.Single(result.Inventory!.Versions).DamageReason);
    }

    /// <summary>Strict product-adapter rejection cannot be replaced by a permissive installed-manifest fallback.</summary>
    [Fact]
    public async Task InstalledProofRequiresStrictProductAdapter()
    {
        using var fixture = new RepositoryFixture();
        var admission = await fixture.SeedInstalledAsync();
        fixture.Package.Policy.Reject = true;
        var result = await fixture.InventoryAsync(admission);
        Assert.Equal(ManagedVersionDamageReason.ManifestMismatch, Assert.Single(result.Inventory!.Versions).DamageReason);
    }

    /// <summary>A cancelled observation propagates cancellation rather than publishing earlier rows.</summary>
    [Fact]
    public async Task CancelledInventoryPublishesNoPartialFacts()
    {
        using var fixture = new RepositoryFixture();
        var admission = await fixture.SeedInstalledAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Repository.InventoryAsync(
            fixture.ManagedRoot, [admission], null, null, null, cancellation.Token));
    }

    /// <summary>Application inventory retains its admission predicate without borrowing the launcher's owner-string ceiling.</summary>
    [Fact]
    public async Task ApplicationInventoryDoesNotBorrowLauncherOwnerAdmissionStringCeiling()
    {
        byte[] launcher = RepositoryFixture.PortableExecutable(2);
        var package = PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContractFixture.ApplicationPath] = RepositoryFixture.PortableExecutable(),
            [ContractFixture.LauncherPath] = launcher,
        }, project: manifest => manifest with
        {
            Launcher = new(PackageFixture.Version, 1, ContractFixture.LauncherPath, launcher.LongLength, PackageFixture.Hash(launcher)),
        });
        using var fixture = new RepositoryFixture(package);
        var admission = (await fixture.SeedInstalledAsync()) with { AdmissionIdentity = new string('a', 3000) };
        await File.WriteAllBytesAsync(fixture.InstalledPath(".managed-admission.v1.json"), fixture.Codec.Encode(admission).ToArray(),
            TestContext.Current.CancellationToken);
        var result = await fixture.InventoryAsync(admission);
        Assert.True(result.IsSuccess);
        Assert.Equal(ManagedVersionIntegrity.Healthy, Assert.Single(result.Inventory!.Versions).Integrity);
    }

    private static IEnumerable<string> EnumerateAfterDeleting(string directory)
    {
        Directory.Delete(directory);
        yield return directory;
    }
}
