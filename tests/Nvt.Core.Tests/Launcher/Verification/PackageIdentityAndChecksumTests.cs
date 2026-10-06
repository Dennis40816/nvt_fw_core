// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using System.Text.Json.Nodes;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

/// <summary>Checks strict checksum bytes and distrusts forged normalized policy facts.</summary>
public sealed class PackageIdentityAndChecksumTests
{
    /// <summary>Mechanical product, runtime and version identities are compared exactly after callback success.</summary>
    [Theory]
    [InlineData("product-case")]
    [InlineData("runtime")]
    [InlineData("runtime-case")]
    [InlineData("version")]
    [InlineData("null-files")]
    [InlineData("null-file")]
    [InlineData("duplicate")]
    [InlineData("duplicate-case")]
    [InlineData("zero-size")]
    [InlineData("negative-size")]
    [InlineData("overflow-size")]
    [InlineData("upper-hash")]
    [InlineData("short-hash")]
    [InlineData("null-hash")]
    [InlineData("reserved-admission")]
    [InlineData("reserved-manifest")]
    [InlineData("reserved-checksum")]
    [InlineData("unsafe-path")]
    [InlineData("overlong-path")]
    [InlineData("too-many-files")]
    [InlineData("misreported-count")]
    [InlineData("negative-count")]
    public async Task ForgedNormalizedManifestFactsNeverBypassMechanicalChecks(string mutation)
    {
        PackageFixture fixture = PackageFixture.Create(project: manifest =>
        {
            PackageFile first = manifest.Files[0];
            return mutation switch
            {
                "product-case" => manifest with { ProductId = manifest.ProductId.ToLowerInvariant() },
                "runtime" => manifest with { RuntimeIdentifier = "different-runtime" },
                "runtime-case" => manifest with { RuntimeIdentifier = manifest.RuntimeIdentifier.ToUpperInvariant() },
                "version" => manifest with { Version = ContractFixture.App100 },
                "null-files" => manifest with { Files = null! },
                "null-file" => manifest with { Files = [null!] },
                "duplicate" => manifest with { Files = [.. manifest.Files, first] },
                "duplicate-case" => manifest with { Files = [.. manifest.Files, first with { Path = first.Path.ToUpperInvariant() }] },
                "zero-size" => manifest with { Files = [first with { Size = 0 }] },
                "negative-size" => manifest with { Files = [first with { Size = -1 }] },
                "overflow-size" => manifest with { Files = [first with { Size = long.MaxValue }] },
                "upper-hash" => manifest with { Files = [first with { Sha256 = new string('A', 64) }] },
                "short-hash" => manifest with { Files = [first with { Sha256 = new string('a', 63) }] },
                "null-hash" => manifest with { Files = [first with { Sha256 = null! }] },
                "reserved-admission" => manifest with { Files = [first with { Path = ManagedPackageVerifier.AdmissionFileName }] },
                "reserved-manifest" => manifest with { Files = [first with { Path = ManagedPackageVerifier.ManifestFileName }] },
                "reserved-checksum" => manifest with { Files = [first with { Path = ManagedPackageVerifier.ChecksumFileName }] },
                "unsafe-path" => manifest with { Files = [first with { Path = "../payload.txt" }] },
                "overlong-path" => manifest with { Files = [first with { Path = new string('a', 513) }] },
                "too-many-files" => manifest with { Files = Enumerable.Repeat(first, 4096).ToArray() },
                "misreported-count" => manifest with { Files = new ForgedFileInventory(1) },
                "negative-count" => manifest with { Files = new ForgedFileInventory(-1) },
                _ => throw new InvalidOperationException(),
            };
        });
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        Assert.Equal(1, fixture.Policy.Projections);
    }

    /// <summary>Digest identity grammar is exactly 64 lowercase hexadecimal characters at both document and payload boundaries.</summary>
    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void ChecksumDigestPreservesTheExactIdentityLength(int length)
    {
        PackageFixture fixture = PackageFixture.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(new string('a', length) + "  file.txt\n" +
            PackageFixture.Hash(fixture.ManifestBytes) + "  RELEASE-MANIFEST.json\n");
        PackageFile[] files = [new("file.txt", 1, new string('a', length))];
        Assert.Equal(length == 64, fixture.Verifier().VerifyChecksumDocument(bytes, fixture.ManifestBytes, files));
    }

    /// <summary>The product's path callback remains mandatory even for a mechanically safe member.</summary>
    [Fact]
    public async Task RejectedProductPathNeverReachesManifestAdmission()
    {
        PackageFixture fixture = PackageFixture.Create();
        fixture.Policy.PathPolicy = _ => false;
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.UnsafeArchive);
        Assert.Equal(0, fixture.Policy.Calls);
    }

    /// <summary>The frozen launcher-contract check precedes normalized payload path callbacks.</summary>
    [Fact]
    public async Task InvalidLauncherPrecedesNormalizedPathPolicy()
    {
        PackageFixture fixture = PackageFixture.Create(includeLauncher: true,
            project: manifest => manifest with { Launcher = manifest.Launcher! with { ProtocolVersion = 2 } });
        bool projected = false;
        fixture.Policy.BeforeProjection = () => projected = true;
        fixture.Policy.PathPolicy = _ => projected
            ? throw new InvalidOperationException("Normalized paths were checked before the invalid launcher.")
            : true;
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        Assert.Equal(1, fixture.Policy.Projections);
    }

    /// <summary>A successful callback cannot forge launcher protocol, descriptor path, declared content or inventory binding.</summary>
    [Theory]
    [InlineData("protocol")]
    [InlineData("path")]
    [InlineData("path-case")]
    [InlineData("zero-size")]
    [InlineData("size")]
    [InlineData("ceiling")]
    [InlineData("hash")]
    [InlineData("uppercase-hash")]
    [InlineData("missing-file")]
    public async Task ForgedLauncherFactsNeverReceiveOwnerAdmission(string mutation)
    {
        PackageFixture fixture = PackageFixture.Create(includeLauncher: true, project: manifest =>
        {
            PackageLauncher launcher = manifest.Launcher!;
            return mutation switch
            {
                "protocol" => manifest with { Launcher = launcher with { ProtocolVersion = 2 } },
                "path" => manifest with { Launcher = launcher with { ExecutableRelativePath = ContractFixture.ApplicationPath } },
                "path-case" => manifest with { Launcher = launcher with { ExecutableRelativePath = launcher.ExecutableRelativePath.ToUpperInvariant() } },
                "zero-size" => manifest with { Launcher = launcher with { Size = 0 } },
                "size" => manifest with { Launcher = launcher with { Size = launcher.Size + 1 } },
                "ceiling" => manifest with { Launcher = launcher with { Size = 200_000_001 } },
                "hash" => manifest with { Launcher = launcher with { Sha256 = new string('0', 64) } },
                "uppercase-hash" => manifest with { Launcher = launcher with { Sha256 = new string('A', 64) } },
                "missing-file" => manifest with { Files = manifest.Files.Where(file => file.Path != launcher.ExecutableRelativePath).ToArray() },
                _ => throw new InvalidOperationException(),
            };
        });
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        Assert.Equal(1, fixture.Policy.Projections);
    }

    /// <summary>Every owner field is taken from the exact catalog admission; descriptor paths infer no authority.</summary>
    [Fact]
    public async Task ClosedPlanBindsLauncherToEveryExactOwnerField()
    {
        PackageFixture fixture = PackageFixture.Create(includeLauncher: true);
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackagePlanResult result = await fixture.Verifier().CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);
        ManagedLauncherIdentity launcher = Assert.IsType<ManagedLauncherIdentity>(plan.LauncherIdentity);
        var owner = new ManagedVersionAdmission(fixture.Candidate.Version, fixture.Candidate.Identity, fixture.Candidate.ReleaseManifestSha256);
        Assert.True(launcher.MatchesOwner(owner));
        Assert.False(launcher.MatchesOwner(owner with { Version = ContractFixture.App100 }));
        Assert.False(launcher.MatchesOwner(owner with { AdmissionIdentity = owner.AdmissionIdentity + "x" }));
        Assert.False(launcher.MatchesOwner(owner with { ReleaseManifestSha256 = new string('0', 64) }));
        Assert.Equal(owner.AdmissionIdentity, launcher.OwnerAdmissionIdentity);
        Assert.Equal(owner.ReleaseManifestSha256, launcher.OwnerReleaseManifestSha256);
        Assert.Equal(ContractFixture.LauncherPath, launcher.ExecutableRelativePath);
        Assert.Equal(fixture.ManifestBytes, plan.ManifestBytes.ToArray());
        Assert.Equal(fixture.ChecksumBytes, plan.ChecksumBytes.ToArray());
    }

    /// <summary>Invalid UTF-8, delimiters, digests and closed checksum inventories all fail.</summary>
    [Theory]
    [InlineData("invalid-utf8")]
    [InlineData("bom")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("uppercase-hash")]
    [InlineData("nonhex-hash")]
    [InlineData("short-hash")]
    [InlineData("long-hash")]
    [InlineData("one-space")]
    [InlineData("tab-delimiter")]
    [InlineData("empty-path")]
    [InlineData("unsafe-path")]
    [InlineData("case-path")]
    [InlineData("bare-cr")]
    [InlineData("mixed-cr")]
    public async Task ChecksumGrammarAndInventoryFailClosed(string mutation)
    {
        PackageFixture fixture = PackageFixture.Create(mutateChecksums: bytes =>
        {
            string text = Encoding.UTF8.GetString(bytes);
            string first = text.Split('\n')[0];
            string changed = mutation switch
            {
                "duplicate" => text + first + "\n",
                "missing" => text[(first.Length + 1)..],
                "extra" => text + $"{new string('0', 64)}  extra.txt\n",
                "uppercase-hash" => new string('A', 64) + text[64..],
                "nonhex-hash" => 'g' + text[1..],
                "short-hash" => text[1..],
                "long-hash" => '0' + text,
                "one-space" => text.Remove(64, 1),
                "tab-delimiter" => text[..64] + "\t " + text[66..],
                "empty-path" => text[..66] + '\n',
                "unsafe-path" => text[..66] + "../payload.txt\n",
                "case-path" => text[..66] + first[66..].ToLowerInvariant() + text[first.Length..],
                "bare-cr" => text.Replace('\n', '\r'),
                "mixed-cr" => text.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r",
                _ => text,
            };
            return mutation switch
            {
                "invalid-utf8" => [.. bytes, 0xc3, 0x28],
                "bom" => [0xef, 0xbb, 0xbf, .. bytes],
                _ => Encoding.UTF8.GetBytes(changed),
            };
        });
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
    }

    /// <summary>Source parsing permits LF, CRLF, blank lines, reordered lines and an EOF without a newline.</summary>
    [Theory]
    [InlineData("crlf")]
    [InlineData("blank-lines")]
    [InlineData("reordered")]
    [InlineData("no-final-newline")]
    public async Task ChecksumAcceptedByteShapesMatchTheFrozenParser(string shape)
    {
        PackageFixture fixture = PackageFixture.Create(mutateChecksums: bytes =>
        {
            string text = Encoding.UTF8.GetString(bytes);
            return Encoding.UTF8.GetBytes(shape switch
            {
                "crlf" => text.Replace("\n", "\r\n", StringComparison.Ordinal),
                "blank-lines" => "\n" + text.Replace("\n", "\n\n", StringComparison.Ordinal),
                "reordered" => string.Join('\n', text.Split('\n', StringSplitOptions.RemoveEmptyEntries).AsEnumerable().Reverse()),
                "no-final-newline" => text.TrimEnd('\n'),
                _ => throw new InvalidOperationException(),
            });
        });
        await using var package = new MemoryStream(fixture.PackageBytes);
        Assert.True((await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken)).IsVerified);
    }

    /// <summary>Synthetic malformed schema is rejected by the mandatory adapter before facts are returned.</summary>
    [Theory]
    [InlineData("json")]
    [InlineData("schema")]
    [InlineData("unknown")]
    public async Task StrictPolicyRejectsBeforeProjectingFacts(string shape)
    {
        PackageFixture fixture = PackageFixture.Create(mutateManifest: bytes =>
        {
            if (shape == "json") return "{malformed"u8.ToArray();
            JsonObject json = JsonNode.Parse(bytes)!.AsObject();
            if (shape == "schema") json["schema"] = "other";
            else json["unexpected"] = true;
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        });
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.InvalidPayload);
        Assert.Equal(1, fixture.Policy.Calls);
        Assert.Equal(0, fixture.Policy.Projections);
    }

    /// <summary>Structural path checks run even when the adapter accepts every unsafe archive name.</summary>
    [Theory]
    [InlineData("/absolute.txt")]
    [InlineData("wrong-root/payload.txt")]
    [InlineData("{root}\\backslash.txt")]
    [InlineData("{root}/payload:stream")]
    [InlineData("{root}/folder//payload.txt")]
    [InlineData("{root}/folder/../payload.txt")]
    [InlineData("{root}/folder./payload.txt")]
    [InlineData("{root}/CON/payload.txt")]
    public async Task UnsafeArchivePathShapesNeverVerify(string pattern)
    {
        PackageFixture fixture = PackageFixture.Create(mutateArchive: (archive, root) =>
            PackageFixture.WriteEntry(archive, pattern.Replace("{root}", root, StringComparison.Ordinal), "x"u8.ToArray()));
        await ManagedPackageVerifierTests.AssertIssueAsync(fixture, ManagedVersionInstallIssue.UnsafeArchive);
        Assert.Equal(0, fixture.Policy.Calls);
    }
}

internal sealed class ForgedFileInventory(int count) : IReadOnlyList<PackageFile>
{
    public int Count => count;
    public PackageFile this[int index] => new($"reference/forged-{index}.txt", 1, new string('a', 64));
    public IEnumerator<PackageFile> GetEnumerator()
    {
        // A dishonest projection must be bounded by actual iteration, not its Count hint.
        for (int index = 0; index < 4096; index++) yield return this[index];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
