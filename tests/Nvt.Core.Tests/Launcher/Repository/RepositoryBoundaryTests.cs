// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Files;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Exercises repository admission, inventory and physical installation at inclusive frozen ceilings.</summary>
public sealed class RepositoryBoundaryTests
{
    /// <summary>Both repository consumers recheck every explicit positive limit, including copied record values.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    [InlineData(4, 0)]
    [InlineData(4, -1)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    [InlineData(6, 0)]
    [InlineData(6, -1)]
    public void RepositoriesRejectNonPositiveLimits(int dimension, int value)
    {
        var limits = dimension switch
        {
            0 => PackageFixture.FrozenLimits with { MaximumArchiveEntries = value },
            1 => PackageFixture.FrozenLimits with { MaximumPackageBytes = value },
            2 => PackageFixture.FrozenLimits with { MaximumExpandedBytes = value },
            3 => PackageFixture.FrozenLimits with { MaximumManifestBytes = value },
            4 => PackageFixture.FrozenLimits with { MaximumAdmissionBytes = value },
            5 => PackageFixture.FrozenLimits with { MaximumExecutableBytes = value },
            _ => PackageFixture.FrozenLimits with { MaximumInstalledDirectories = value },
        };
        var package = PackageFixture.Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemManagedVersionRepository(ContractFixture.Descriptor,
            package.Policy, limits, new AdmissionCodec()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemInstalledLauncherRepository(ContractFixture.Descriptor,
            package.Policy, limits, new AdmissionCodec()));
    }

    /// <summary>The admission document admits 4,095 and 4,096 bytes and rejects 4,097 before staging.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AdmissionBytesPreserveFrozenBoundary(int delta)
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture(codec: new AdmissionCodec { PadToBytes = 4096 + delta });
        var result = await fixture.InstallAsync();
        Assert.Equal(delta <= 0, result.IsSuccess);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.InvalidPayload, result.Issue);
        if (delta <= 0)
        {
            Assert.Equal(4096 + delta, new FileInfo(fixture.InstalledPath(".managed-admission.v1.json")).Length);
            Assert.Equal(1, (await fixture.InventoryAsync(result.Admission!)).Inventory!.HealthyCount);
        }
        fixture.AssertEmptyStaging();
    }

    /// <summary>Installed admission reading rechecks the same inclusive byte ceiling without native installation.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task InstalledAdmissionBytesPreserveFrozenBoundary(int delta)
    {
        using var fixture = new RepositoryFixture(codec: new AdmissionCodec { PadToBytes = 4096 + delta });
        var admission = await fixture.SeedInstalledAsync();
        var result = await fixture.InventoryAsync(admission);
        Assert.True(result.IsSuccess);
        Assert.Equal(delta <= 0 ? ManagedVersionIntegrity.Healthy : ManagedVersionIntegrity.Damaged,
            Assert.Single(result.Inventory!.Versions).Integrity);
        Assert.Equal(delta <= 0 ? null : ManagedVersionDamageReason.ManifestMismatch,
            Assert.Single(result.Inventory.Versions).DamageReason);
    }

    /// <summary>Archive member counts become the exact 4,097-file held installation reservation.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ArchiveEntriesAndInstalledFilesPreserveFrozenBoundary(int delta)
    {
        RepositoryFixture.RequireWindows();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int index = 0; index < 4096 + delta - 2; index++)
        {
            files[$"reference/file-{index:D4}.txt"] = [0];
        }
        using var fixture = new RepositoryFixture(PackageFixture.Create(files));
        var result = await fixture.InstallAsync();
        Assert.Equal(delta <= 0, result.IsSuccess);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
        if (delta <= 0)
        {
            Assert.Equal(4097 + delta, Directory.EnumerateFiles(fixture.VersionRoot, "*", SearchOption.AllDirectories).Count());
        }
        fixture.AssertEmptyStaging();
    }

    /// <summary>The independent 4,096-directory reservation counts every implicit parent once.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task InstalledDirectoriesPreserveFrozenBoundary(int delta)
    {
        RepositoryFixture.RequireWindows();
        int count = 4096 + delta;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int index = 0; index < count / 2; index++)
        {
            files[$"parent-{index:D4}/child/file.txt"] = [0];
        }
        if (count % 2 != 0)
        {
            files["odd-parent/file.txt"] = [0];
        }
        using var fixture = new RepositoryFixture(PackageFixture.Create(files));
        var result = await fixture.InstallAsync();
        Assert.Equal(delta <= 0, result.IsSuccess);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
        if (delta <= 0)
        {
            Assert.Equal(count, Directory.EnumerateDirectories(fixture.VersionRoot, "*", SearchOption.AllDirectories).Count());
        }
    }

    /// <summary>Inventory rechecks actual physical bytes at 512 MiB and reserves a separate exact 4 KiB admission.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task InstalledActualExpandedBytesPreserveFrozenBoundary(int delta)
    {
        using var workspace = new TestWorkspace();
        string managedRoot = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        string versionRoot = Directory.CreateDirectory(workspace.PathFor("managed/versions/0.10.6")).FullName;
        const string path = "content.bin";
        long targetExpanded = 536_870_912L + delta;
        string zeroHash = new('0', 64);
        byte[] Manifest(long size, string hash) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "fixture-1", role = "reference", assetName = "fixture.spdx.json",
            files = new[] { new { path, size, sha256 = hash } },
        });
        int checksumLength = Encoding.UTF8.GetByteCount($"{zeroHash}  {path}\n{zeroHash}  RELEASE-MANIFEST.json\n");
        long length = targetExpanded - Manifest(targetExpanded, zeroHash).Length - checksumLength;
        string contentPath = Path.Combine(versionRoot, path);
        await using (var output = new FileStream(contentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            output.SetLength(length);
        }
        string hash;
        await using (var input = File.OpenRead(contentPath))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(input, TestContext.Current.CancellationToken)).ToLowerInvariant();
        }
        byte[] manifest = Manifest(length, hash);
        string manifestHash = PackageFixture.Hash(manifest);
        byte[] checksums = Encoding.UTF8.GetBytes($"{hash}  {path}\n{manifestHash}  RELEASE-MANIFEST.json\n");
        Assert.Equal(targetExpanded, length + manifest.Length + checksums.Length);
        var projection = new PackageManifest(ContractFixture.ProductId, ContractFixture.RuntimeIdentifier, PackageFixture.Version,
            [new(path, length, hash)], null);
        var policy = new FixturePolicy(projection);
        var codec = new AdmissionCodec { PadToBytes = 4096 };
        var admission = new ManagedVersionAdmission(PackageFixture.Version, "large-synthetic-admission", manifestHash);
        await File.WriteAllBytesAsync(Path.Combine(versionRoot, "RELEASE-MANIFEST.json"), manifest, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(versionRoot, "SHA256SUMS.txt"), checksums, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(versionRoot, ".managed-admission.v1.json"), codec.Encode(admission).ToArray(),
            TestContext.Current.CancellationToken);
        var repository = new FileSystemManagedVersionRepository(ContractFixture.Descriptor, policy, PackageFixture.FrozenLimits, codec);
        var result = await repository.InventoryAsync(managedRoot, [admission], null, null, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Inventory!.Versions);
        Assert.Equal(delta <= 0 ? ManagedVersionIntegrity.Healthy : ManagedVersionIntegrity.Damaged, row.Integrity);
        Assert.Equal(delta <= 0 ? null : ManagedVersionDamageReason.ContentMismatch, row.DamageReason);
        Assert.Equal(536_875_008L + delta, Directory.EnumerateFiles(versionRoot).Sum(file => new FileInfo(file).Length));
    }
}
