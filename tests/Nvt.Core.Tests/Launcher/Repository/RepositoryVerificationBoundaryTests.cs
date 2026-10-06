// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Verifies that repository source admission consumes the frozen verification and path ceilings unchanged.</summary>
public sealed class RepositoryVerificationBoundaryTests
{
    /// <summary>The source-file repository preserves the 134,217,728-byte compressed-package ceiling and its neighbors.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CompressedPackagePreservesFrozenBoundary(int delta)
    {
        using var fixture = new RepositoryFixture();
        byte[] bytes = PackageFixture.PadPackageTo(fixture.Package.PackageBytes, 134_217_728 + delta);
        string packagePath = Path.Combine(fixture.SourceRoot, fixture.Package.Candidate.PackagePath.Value);
        await File.WriteAllBytesAsync(packagePath, bytes, TestContext.Current.CancellationToken);
        var result = await fixture.Repository.VerifyPackageAsync(fixture.SourceRoot, fixture.Package.CandidateFor(bytes),
            TestContext.Current.CancellationToken);
        Assert.Equal(delta <= 0, result.IsVerified);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.PackageUnavailable, result.Issue);
        Assert.Equal(delta <= 0 ? 1 : 0, fixture.Package.Policy.Calls);
    }

    /// <summary>Both exact manifest and checksum bytes consume the same inclusive 1,048,576-byte ceiling.</summary>
    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task ManifestAndChecksumBytesPreserveFrozenBoundary(bool checksum, int delta)
    {
        byte[] Pad(byte[] original)
        {
            byte[] bytes = new byte[1_048_576 + delta];
            original.CopyTo(bytes, 0);
            bytes.AsSpan(original.Length).Fill(checksum ? (byte)'\n' : (byte)' ');
            return bytes;
        }
        var package = PackageFixture.Create(mutateManifest: checksum ? null : Pad, mutateChecksums: checksum ? Pad : null);
        using var fixture = new RepositoryFixture(package);
        var result = await fixture.Repository.VerifyPackageAsync(fixture.SourceRoot, package.Candidate,
            TestContext.Current.CancellationToken);
        Assert.Equal(delta <= 0, result.IsVerified);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.InvalidPayload, result.Issue);
    }

    /// <summary>Relative payload admission preserves the validated 512-character receipt and its neighbors.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RelativePayloadPathPreservesFrozenBoundary(int delta)
    {
        string path = new string('a', 180) + "/" + new string('b', 180) + "/" + new string('c', 150 + delta);
        Assert.Equal(512 + delta, path.Length);
        var package = PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = [0] });
        using var fixture = new RepositoryFixture(package);
        var result = await fixture.Repository.VerifyPackageAsync(fixture.SourceRoot, package.Candidate,
            TestContext.Current.CancellationToken);
        Assert.Equal(delta <= 0, result.IsVerified);
        Assert.Equal(delta <= 0 ? ManagedVersionInstallIssue.None : ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
    }

    /// <summary>Both repository constructors preserve the identity receipt's fixed 200,000,000-byte executable maximum.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void ExecutableLimitParameterPreservesFrozenMaximum(int delta)
    {
        var package = PackageFixture.Create();
        var limits = PackageFixture.FrozenLimits with { MaximumExecutableBytes = 200_000_000L + delta };
        if (delta > 0)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemManagedVersionRepository(ContractFixture.Descriptor,
                package.Policy, limits, new AdmissionCodec()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemInstalledLauncherRepository(ContractFixture.Descriptor,
                package.Policy, limits, new AdmissionCodec()));
        }
        else
        {
            _ = new FileSystemManagedVersionRepository(ContractFixture.Descriptor, package.Policy, limits, new AdmissionCodec());
            _ = new FileSystemInstalledLauncherRepository(ContractFixture.Descriptor, package.Policy, limits, new AdmissionCodec());
        }
    }

    /// <summary>Source policy rejects paths through its mandatory callback before any extraction destination exists.</summary>
    [Fact]
    public async Task MandatoryPayloadPathCallbackCanRejectMechanicallySafeMembers()
    {
        var package = PackageFixture.Create();
        package.Policy.PathPolicy = path => path != "README.txt";
        using var fixture = new RepositoryFixture(package);
        var result = await fixture.Repository.VerifyPackageAsync(fixture.SourceRoot, package.Candidate,
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInstallIssue.UnsafeArchive, result.Issue);
        Assert.Equal(0, package.Policy.Calls);
        Assert.False(Directory.Exists(fixture.VersionRoot));
        fixture.AssertEmptyStaging();
    }
}
