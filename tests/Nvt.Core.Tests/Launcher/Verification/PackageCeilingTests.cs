// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

/// <summary>Exercises every frozen package ceiling and neighboring mechanical boundaries.</summary>
public sealed class PackageCeilingTests
{
    /// <summary>The frozen 128 MiB compressed ceiling admits its two lower neighbors and rejects one byte more before policy.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CompressedPackagePreservesFrozenBoundary(int delta)
    {
        PackageFixture fixture = PackageFixture.Create();
        byte[] bytes = PackageFixture.PadPackageTo(fixture.PackageBytes, checked((int)PackageFixture.FrozenLimits.MaximumPackageBytes + delta));
        await using var package = new MemoryStream(bytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.CandidateFor(bytes), TestContext.Current.CancellationToken);
        Assert.Equal(delta <= 0, result.IsVerified);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.PackageUnavailable, result.Issue);
        Assert.Equal(delta <= 0 ? 1 : 0, fixture.Policy.Calls);
    }

    /// <summary>The frozen 4,096 member ceiling also reserves exactly one additional installed admission file.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ArchiveMembersAndInstalledFilesPreserveFrozenBoundary(int delta)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int index = 0; index < 4096 + delta - 2; index++) files[$"reference/entry-{index:D4}.txt"] = [0];
        PackageFixture fixture = PackageFixture.Create(files);
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackagePlanResult result = await fixture.Verifier().CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan? plan = result.Plan;
        Assert.Equal(delta <= 0, result.IsSuccess);
        if (delta <= 0)
        {
            Assert.Equal(4096 + delta, plan!.FileCount);
            Assert.Equal(4097 + delta, plan.InstalledFileCount);
            Assert.Equal(4097, plan.MaximumInstalledFiles);
            Assert.True(plan.AdmitsInstalledFileCount(4096));
            Assert.True(plan.AdmitsInstalledFileCount(4097));
            Assert.False(plan.AdmitsInstalledFileCount(4098));
        }
        else
        {
            Assert.Null(plan);
            Assert.Equal(ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
            Assert.Equal(0, fixture.Policy.Calls);
        }
    }

    /// <summary>The independent 4,096 implicit-directory ceiling counts every unique parent once.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task InstalledDirectoriesPreserveFrozenBoundary(int delta)
    {
        int count = 4096 + delta;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int index = 0; index < count / 2; index++) files[$"parent-{index:D4}/child/file.txt"] = [0];
        if (count % 2 != 0) files["odd-parent/file.txt"] = [0];
        PackageFixture fixture = PackageFixture.Create(files);
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackagePlanResult result = await fixture.Verifier().CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan? plan = result.Plan;
        Assert.Equal(delta <= 0, result.IsSuccess);
        if (delta <= 0) Assert.Equal(count, plan!.ImplicitDirectoryCount);
        else Assert.Equal(ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
    }

    /// <summary>Manifest and checksum documents use the same frozen 1 MiB limit, including exact equality.</summary>
    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task DocumentsPreserveFrozenBoundary(bool checksum, int delta)
    {
        byte[] Pad(byte[] bytes)
        {
            int length = 1_048_576 + delta;
            byte[] padded = new byte[length];
            bytes.CopyTo(padded, 0);
            padded.AsSpan(bytes.Length).Fill(checksum ? (byte)'\n' : (byte)' ');
            return padded;
        }
        PackageFixture fixture = PackageFixture.Create(mutateManifest: checksum ? null : Pad, mutateChecksums: checksum ? Pad : null);
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.Equal(delta <= 0, result.IsVerified);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.InvalidPayload, result.Issue);
    }

    /// <summary>Both document lengths must be positive before their contents can be admitted.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyDocumentsFailBeforeTheirAdmission(bool checksum)
    {
        PackageFixture fixture = PackageFixture.Create(mutateManifest: checksum ? null : _ => [], mutateChecksums: checksum ? _ => [] : null);
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        Assert.Equal(checksum ? 1 : 0, fixture.Policy.Calls);
    }

    /// <summary>Actual document expansion is bounded even when both local and central lengths claim one byte.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnderreportedDocumentsCannotBypassTheDocumentCeiling(bool checksum)
    {
        byte[] Pad(byte[] bytes)
        {
            byte[] padded = new byte[1_048_577];
            bytes.CopyTo(padded, 0);
            padded.AsSpan(bytes.Length).Fill(checksum ? (byte)'\n' : (byte)' ');
            return padded;
        }
        PackageFixture fixture = PackageFixture.Create(mutateManifest: checksum ? null : Pad, mutateChecksums: checksum ? Pad : null,
            mutatePackage: bytes => PackageFixture.UnderreportEntry(bytes,
                checksum ? ManagedPackageVerifier.ChecksumFileName : ManagedPackageVerifier.ManifestFileName, 1));
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
    }

    /// <summary>The exact actual expanded total is checked independently of declared document sizes.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ActualExpansionHasOneSharedBoundary(int delta)
    {
        PackageFixture fixture = PackageFixture.Create(mutatePackage: bytes => PackageFixture.UnderreportEntry(bytes, ManagedPackageVerifier.ManifestFileName, 1));
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier(PackageFixture.FrozenLimits with { MaximumExpandedBytes = fixture.ExpandedBytes + delta })
            .VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.Equal(delta >= 0, result.IsVerified);
        Assert.Equal(delta >= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
    }

    /// <summary>Application payloads may exceed the executable ceiling; declared launchers retain the frozen bound.</summary>
    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task OnlyDeclaredLaunchersUseTheExecutableCeiling(bool launcher, int delta)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContractFixture.ApplicationPath] = [1],
        };
        string path = launcher ? ContractFixture.LauncherPath : ContractFixture.ApplicationPath;
        files[path] = new byte[200_000_000 + delta];
        PackageFixture fixture = PackageFixture.Create(files, compressionLevel: CompressionLevel.SmallestSize,
            project: manifest => launcher ? manifest with { Launcher = new(PackageFixture.Version, 1, path, files[path].LongLength, PackageFixture.Hash(files[path])) } : manifest);
        Assert.True(fixture.ExpandedBytes <= PackageFixture.FrozenLimits.MaximumExpandedBytes);
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        bool expected = !launcher || delta <= 0;
        Assert.Equal(expected, result.IsVerified);
        Assert.Equal(expected ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.InvalidPayload, result.Issue);
        Assert.Equal(launcher && expected, result.HasSupportedManagedLauncher);
    }

    /// <summary>Path safety is rechecked mechanically at 512 characters even if policy accepts longer strings.</summary>
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    public async Task RelativePathsPreserveFrozenBoundary(int length)
    {
        string path = new('a', length);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = [1] };
        PackageFixture fixture = PackageFixture.Create(files);
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.Equal(length <= 512, result.IsVerified);
        Assert.Equal(length <= 512 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
    }

    /// <summary>The frozen 200-character manifest asset-name predicate is retained in strict adapter admission.</summary>
    [Theory]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(201)]
    public async Task StrictPolicyRetainsTheFrozenAssetNameBoundary(int length)
    {
        PackageFixture fixture = PackageFixture.Create(mutateManifest: bytes =>
        {
            JsonObject json = JsonNode.Parse(bytes)!.AsObject();
            json["assetName"] = new string('a', length);
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        });
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.Equal(length <= 200, result.IsVerified);
        Assert.Equal(length <= 200 ? 1 : 0, fixture.Policy.Projections);
    }

    /// <summary>The internal extraction receipt preserves the positive 4,096-byte admission reservation.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(4095, true)]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public async Task AdmissionReservationPreservesFrozenBoundary(long length, bool expected)
    {
        PackageFixture fixture = PackageFixture.Create();
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackagePlanResult result = await fixture.Verifier().CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);
        Assert.Equal(expected, plan.AdmitsAdmissionLength(length));
    }

    /// <summary>No archive entries are admitted, and explicit directory records consume only the archive member count.</summary>
    [Fact]
    public async Task EmptyArchivesAndDirectoryAccountingMatchTheSource()
    {
        PackageFixture fixture = PackageFixture.Create();
        using var empty = new MemoryStream();
        using (var archive = new ZipArchive(empty, ZipArchiveMode.Create, leaveOpen: true)) { }
        byte[] bytes = empty.ToArray();
        Assert.Equal(ManagedVersionInstallIssue.UnsafeArchive,
            (await fixture.Verifier().VerifyAsync(empty, fixture.CandidateFor(bytes), TestContext.Current.CancellationToken)).Issue);
        PackageFixture directories = PackageFixture.Create(mutateArchive: (archive, root) =>
        {
            _ = archive.CreateEntry(root + "/");
            _ = archive.CreateEntry(root + "/empty/");
            _ = archive.CreateEntry(root + "/empty/");
        });
        await using var package = new MemoryStream(directories.PackageBytes);
        ManagedPackagePlanResult result = await directories.Verifier().CreatePlanAsync(package, directories.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);
        Assert.Equal(9, plan.FileCount);
        Assert.Equal(1, plan.ImplicitDirectoryCount);
        Assert.Equal(directories.ExpandedBytes, plan.ExpandedBytes);
    }

    /// <summary>Explicit directory content and unsafe directory paths preserve the frozen rejection order.</summary>
    [Theory]
    [InlineData("content")]
    [InlineData("unsafe")]
    [InlineData("link")]
    public async Task InvalidDirectoryRecordsNeverReachPolicy(string shape)
    {
        PackageFixture fixture = PackageFixture.Create(mutateArchive: (archive, root) =>
        {
            ZipArchiveEntry entry = archive.CreateEntry(root + (shape == "unsafe" ? "/../" : "/empty/"));
            if (shape == "link") entry.ExternalAttributes = unchecked(0xA000 << 16);
            if (shape == "content")
            {
                using Stream output = entry.Open();
                output.WriteByte(1);
            }
        });
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.UnsafeArchive);
        Assert.Equal(0, fixture.Policy.Calls);
    }

    /// <summary>ZIP64 negative declarations fail before accumulation just as overflowing positive declarations do.</summary>
    [Fact]
    public async Task NegativeZip64LengthIsUnsafe()
    {
        PackageFixture fixture = PackageFixture.Create(mutatePackage: original =>
        {
            byte[] bytes = PackageFixture.InflateSecondCentralEntryToLongMax(original);
            ReadOnlySpan<byte> marker = [1, 0, 8, 0, 255, 255, 255, 255, 255, 255, 255, 127];
            int offset = bytes.AsSpan().IndexOf(marker);
            Assert.True(offset >= 0);
            bytes[offset + marker.Length - 1] = 255;
            return bytes;
        });
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.UnsafeArchive);
        Assert.Equal(0, fixture.Policy.Calls);
    }

    /// <summary>Admission reservation arithmetic remains representable beyond the signed 32-bit archive count.</summary>
    [Fact]
    public async Task InstalledFileReservationUsesLongArithmetic()
    {
        PackageFixture fixture = PackageFixture.Create();
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackagePlanResult result = await fixture.Verifier(PackageFixture.FrozenLimits with { MaximumArchiveEntries = int.MaxValue })
            .CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);
        Assert.Equal(2_147_483_648, plan.MaximumInstalledFiles);
        Assert.True(plan.AdmitsInstalledFileCount(2_147_483_648));
        Assert.False(plan.AdmitsInstalledFileCount(2_147_483_649));
        Assert.False(plan.AdmitsInstalledFileCount(-1));
    }

    /// <summary>A forged positive metadata length cannot admit an actually empty manifest before policy.</summary>
    [Fact]
    public async Task ActuallyEmptyManifestFailsBeforeNormalizedFacts()
    {
        PackageFixture fixture = PackageFixture.Create(mutatePackage: bytes =>
            PackageFixture.EmptyEntryWithPositiveDeclaredLength(bytes, ManagedPackageVerifier.ManifestFileName));
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package,
            fixture.CandidateFor(fixture.PackageBytes, PackageFixture.Hash([])), TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, result.Issue);
        Assert.Equal(0, fixture.Policy.Calls);
    }
}
