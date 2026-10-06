// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

/// <summary>Ports the frozen closed-package admission regressions using synthetic content and policy.</summary>
public sealed class ManagedPackageVerifierTests
{
    /// <summary>Complete content verifies and preserves the catalog admission identity and release notes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletePackagePreservesCandidateAndLauncherAdmission(bool includeLauncher)
    {
        PackageFixture fixture = PackageFixture.Create(includeLauncher: includeLauncher);
        await using var package = new MemoryStream(fixture.PackageBytes);
        package.Position = 13;
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.True(result.IsVerified, result.Issue.ToString());
        Assert.Equal(includeLauncher, result.HasSupportedManagedLauncher);
        Assert.Equal(fixture.Candidate.Version, result.Candidate!.Version);
        Assert.Equal(fixture.Candidate.Identity, result.Candidate.AdmissionIdentity);
        Assert.Equal(fixture.Candidate.ReleaseNotes, result.Candidate.ReleaseNotes);
        Assert.Equal(fixture.ManifestBytes, fixture.Policy.ObservedBytes);
        Assert.Equal(fixture.Files.Keys.Append(ManagedPackageVerifier.ManifestFileName).Append(ManagedPackageVerifier.ChecksumFileName).Order(),
            fixture.Policy.ObservedPaths!.Order());
        Assert.True(package.CanRead);
    }

    /// <summary>Frozen package-length and same-length digest mutations stop before ZIP or manifest admission.</summary>
    [Theory]
    [InlineData("length", ManagedVersionInstallIssue.PackageUnavailable)]
    [InlineData("digest", ManagedVersionInstallIssue.PackageMismatch)]
    public async Task ChangedPackageNeverReachesZipAdmission(string mutation, ManagedVersionInstallIssue expected)
    {
        PackageFixture fixture = PackageFixture.Create();
        UpdateCatalogVersionSnapshot candidate = fixture.Candidate;
        byte[] bytes = (byte[])fixture.PackageBytes.Clone();
        if (mutation == "length")
        {
            bytes = [.. bytes, (byte)'x'];
        }
        else
        {
            bytes[^1] ^= 0xff;
        }
        await using var package = new MemoryStream(bytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, candidate, TestContext.Current.CancellationToken);
        Assert.False(result.IsVerified);
        Assert.Equal(expected, result.Issue);
        Assert.Equal(0, fixture.Policy.Calls);
        using var workspace = new ExtractionWorkspace();
        Assert.Equal(expected, await workspace.VerifyAndExtractAsync(fixture.Verifier(), bytes, candidate));
        Assert.False(Directory.Exists(workspace.VersionRoot));
        Assert.False(Directory.Exists(workspace.StagingRoot));
        Assert.Equal(0, workspace.Destinations);
    }

    /// <summary>Frozen duplicates, UNIX links and Windows reparse attributes never admit an archive.</summary>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("link")]
    [InlineData("reparse")]
    public async Task DuplicateAndLinkArchiveMembersNeverVerify(string shape)
    {
        PackageFixture fixture = PackageFixture.Create(mutateArchive: (archive, root) =>
        {
            ZipArchiveEntry entry = archive.CreateEntry(root + (shape == "duplicate" ? "/readme.TXT" : "/link.txt"));
            entry.ExternalAttributes = shape switch { "link" => unchecked(0xA000 << 16), "reparse" => (int)FileAttributes.ReparsePoint, _ => 0 };
            using Stream output = entry.Open();
            output.WriteByte(1);
        });
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.UnsafeArchive);
        Assert.Equal(0, fixture.Policy.Calls);
    }

    /// <summary>Frozen underreported metadata fails actual-byte verification before any destination exists.</summary>
    [Fact]
    public async Task UnderreportedZipEntryFailsVerifyAndInstallWithoutMaterialization()
    {
        PackageFixture fixture = PackageFixture.Create(
            project: manifest => manifest with { Files = manifest.Files.Select(file => file.Path == "README.txt" ? file with { Size = 1 } : file).ToArray() },
            mutatePackage: bytes => PackageFixture.UnderreportEntry(bytes, "README.txt", 1));
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        using var workspace = new ExtractionWorkspace();
        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, await workspace.VerifyAndExtractAsync(fixture.Verifier(), fixture.PackageBytes, fixture.Candidate));
        Assert.Equal(0, workspace.Destinations);
        Assert.False(Directory.Exists(workspace.VersionRoot));
        Assert.False(Directory.Exists(workspace.StagingRoot));
    }

    /// <summary>Frozen ZIP64 long.MaxValue metadata is rejected by subtraction before addition.</summary>
    [Fact]
    public async Task Zip64DeclaredSizeOverflowFailsVerifyAndInstallWithoutResidue()
    {
        PackageFixture fixture = PackageFixture.Create(mutatePackage: PackageFixture.InflateSecondCentralEntryToLongMax);
        using (var stream = new MemoryStream(fixture.PackageBytes))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            Assert.True(archive.Entries[0].Length > 0);
            Assert.Equal(long.MaxValue, archive.Entries[1].Length);
        }
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.UnsafeArchive);
        // Even a long.MaxValue supplied ceiling must reject addition to a nonempty aggregate.
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.UnsafeArchive,
            PackageFixture.FrozenLimits with { MaximumExpandedBytes = long.MaxValue });
        using var workspace = new ExtractionWorkspace();
        Assert.Equal(ManagedVersionInstallIssue.UnsafeArchive, await workspace.VerifyAndExtractAsync(fixture.Verifier(), fixture.PackageBytes, fixture.Candidate));
        Assert.Equal(0, workspace.Destinations);
        Assert.False(Directory.Exists(workspace.VersionRoot));
        Assert.False(Directory.Exists(workspace.StagingRoot));
    }

    /// <summary>Frozen wrong hashes and undeclared checksum lines fail the closed checksum inventory.</summary>
    [Theory]
    [InlineData("changed-hash")]
    [InlineData("extra-line")]
    public async Task NonCanonicalChecksumDocumentNeverVerifies(string mutation)
    {
        PackageFixture fixture = PackageFixture.Create(mutateChecksums: bytes => mutation == "changed-hash"
            ? [.. bytes.Select((value, index) => index == 0 ? (byte)(value == '0' ? '1' : '0') : value)]
            : [.. bytes, .. Encoding.UTF8.GetBytes($"{new string('0', 64)}  undeclared.txt\n")]);
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
    }

    /// <summary>Frozen manifest and closed inventory mutations fail; synthetic strict schema cases stay in policy.</summary>
    [Theory]
    [InlineData("product")]
    [InlineData("version")]
    [InlineData("role")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("missing-fixed")]
    [InlineData("extra-file")]
    [InlineData("unknown-field")]
    public async Task InvalidManifestOrClosedPayloadNeverVerifies(string mutation)
    {
        PackageFixture fixture = PackageFixture.Create(
            omittedPayload: mutation == "missing-fixed" ? "README.txt" : null,
            project: manifest => mutation switch
            {
                "product" => manifest with { ProductId = "AnotherProduct" },
                "version" => manifest with { Version = ContractFixture.App100 },
                "hash" => manifest with { Files = manifest.Files.Select(file => file.Path == "README.txt" ? file with { Sha256 = new string('0', 64) } : file).ToArray() },
                "size" => manifest with { Files = manifest.Files.Select(file => file.Path == "README.txt" ? file with { Size = file.Size + 1 } : file).ToArray() },
                _ => manifest,
            },
            mutateManifest: bytes =>
            {
                JsonObject json = JsonNode.Parse(bytes)!.AsObject();
                if (mutation == "role") json["role"] = "forged";
                if (mutation == "unknown-field") json["unknown"] = true;
                return Encoding.UTF8.GetBytes(json.ToJsonString());
            },
            mutateArchive: (archive, root) =>
            {
                if (mutation == "extra-file") PackageFixture.WriteEntry(archive, root + "/undeclared.txt", "x"u8.ToArray());
            });
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        if (mutation is "role" or "unknown-field") Assert.Equal(0, fixture.Policy.Projections);
    }

    /// <summary>Frozen exact expansion accepts plan extraction; one byte less rejects before materialization.</summary>
    [Fact]
    public async Task ExactExpandedByteBudgetInstallsWhileOneByteLessFailsBeforeMaterialization()
    {
        PackageFixture fixture = PackageFixture.Create();
        long expandedBytes;
        using (var stream = new MemoryStream(fixture.PackageBytes))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) expandedBytes = archive.Entries.Sum(entry => entry.Length);
        Assert.Equal(fixture.ExpandedBytes, expandedBytes);
        ManagedPackageVerifier exact = fixture.Verifier(PackageFixture.FrozenLimits with { MaximumExpandedBytes = expandedBytes });
        ManagedPackageVerifier rejected = fixture.Verifier(PackageFixture.FrozenLimits with { MaximumExpandedBytes = expandedBytes - 1 });
        await using var package = new MemoryStream(fixture.PackageBytes);
        Assert.True((await exact.VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken)).IsVerified);
        Assert.Equal(ManagedVersionInstallIssue.UnsafeArchive, (await rejected.VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken)).Issue);
        using var exactWorkspace = new ExtractionWorkspace();
        Assert.Equal(ManagedVersionInstallIssue.None, await exactWorkspace.VerifyAndExtractAsync(exact, fixture.PackageBytes, fixture.Candidate));
        Assert.Equal(fixture.Files.Count + 2, exactWorkspace.Destinations);
        Assert.Equal(fixture.ManifestBytes, await File.ReadAllBytesAsync(Path.Combine(exactWorkspace.VersionRoot, ManagedPackageVerifier.ManifestFileName), TestContext.Current.CancellationToken));
        foreach ((string path, byte[] bytes) in fixture.Files)
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(exactWorkspace.VersionRoot, path), TestContext.Current.CancellationToken));
        using var rejectedWorkspace = new ExtractionWorkspace();
        Assert.Equal(ManagedVersionInstallIssue.UnsafeArchive, await rejectedWorkspace.VerifyAndExtractAsync(rejected, fixture.PackageBytes, fixture.Candidate));
        Assert.Equal(0, rejectedWorkspace.Destinations);
        Assert.False(Directory.Exists(rejectedWorkspace.VersionRoot));
        Assert.False(Directory.Exists(rejectedWorkspace.StagingRoot));
    }

    /// <summary>A same-length payload rewrite still fails after the compressed package digest is repinned.</summary>
    [Fact]
    public async Task SameLengthPayloadTamperFailsTheInnerHash()
    {
        PackageFixture fixture = PackageFixture.Create(mutatePackage: bytes =>
        {
            int offset = bytes.AsSpan().IndexOf("readme"u8);
            Assert.True(offset >= 0);
            bytes[offset] ^= 1;
            return bytes;
        });
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        Assert.Equal(1, fixture.Policy.Calls);
    }

    /// <summary>Catalog-pinned manifest mismatches stop before a successful policy callback could bypass them.</summary>
    [Fact]
    public async Task CatalogPinnedManifestMismatchPrecedesPolicy()
    {
        PackageFixture fixture = PackageFixture.Create();
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package,
            fixture.CandidateFor(fixture.PackageBytes, new string('0', 64)), TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, result.Issue);
        Assert.Equal(0, fixture.Policy.Calls);
    }

    /// <summary>Rejected and absent normalized facts cannot produce a verification candidate.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PolicyRejectionAndNullProjectionFailClosed(bool nullProjection)
    {
        PackageFixture fixture = PackageFixture.Create(project: _ => null);
        fixture.Policy.Reject = !nullProjection;
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        Assert.Equal(1, fixture.Policy.Calls);
    }

    /// <summary>Adapter programming exceptions propagate; frozen I/O exception mapping remains unavailable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PolicyExceptionsCannotPublishVerification(bool ioException)
    {
        PackageFixture fixture = PackageFixture.Create();
        Exception failure = ioException ? new IOException("synthetic policy I/O fault") : new InvalidOperationException("synthetic policy fault");
        fixture.Policy.Exception = failure;
        await using var package = new MemoryStream(fixture.PackageBytes);
        if (ioException)
            Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, (await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken)).Issue);
        else
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => { await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken); }));
        Assert.True(package.CanRead);
    }

    /// <summary>Malformed ZIPs retain the frozen public verification failure category.</summary>
    [Fact]
    public async Task MalformedZipIsPackageUnavailable()
    {
        PackageFixture fixture = PackageFixture.Create(mutatePackage: bytes => { Array.Clear(bytes); return bytes; });
        await AssertIssueAsync(fixture, ManagedVersionInstallIssue.PackageUnavailable);
        Assert.Equal(0, fixture.Policy.Calls);
    }

    internal static async Task AssertIssueAsync(PackageFixture fixture, ManagedVersionInstallIssue expected, PackageVerificationLimits? limits = null)
    {
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackageVerificationResult result = await fixture.Verifier(limits).VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.False(result.IsVerified);
        Assert.Null(result.Candidate);
        Assert.False(result.HasSupportedManagedLauncher);
        Assert.Equal(expected, result.Issue);
        Assert.True(package.CanRead);
    }
}

internal sealed class ExtractionWorkspace : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Nvt.Core.PackageVerification", Guid.NewGuid().ToString("N"));
    internal string VersionRoot => Path.Combine(root, "versions", "0.10.6");
    internal string StagingRoot => Path.Combine(root, ".staging");
    internal int Destinations { get; private set; }

    // Tests the internal plan consumer only. Repository promotion remains a separate consumer.
    internal async Task<ManagedVersionInstallIssue> VerifyAndExtractAsync(ManagedPackageVerifier verifier,
        byte[] bytes, UpdateCatalogVersionSnapshot candidate)
    {
        await using var package = new MemoryStream(bytes);
        ManagedPackagePlanResult result = await verifier.CreatePlanAsync(package, candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan? plan = result.Plan;
        if (result.IsSuccess)
        {
            await plan!.ExtractAsync(path =>
            {
                Destinations++;
                string target = Path.Combine(VersionRoot, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                return File.Create(target);
            }, TestContext.Current.CancellationToken);
        }
        return result.Issue;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
