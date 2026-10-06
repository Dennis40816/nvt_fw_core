// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Contracts;

public sealed class LauncherBootstrapContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LauncherIdentityRejectsMissingOwnerAdmissionIdentity(string? value)
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(ownerAdmissionIdentity: value!));
    }

    [Fact]
    public void LauncherIdentityRejectsOversizedOwnerAdmissionIdentity()
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(ownerAdmissionIdentity: new string('a', 2049)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void LauncherIdentityRejectsInvalidOwnerManifestIdentity(string? value)
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(ownerReleaseManifestSha256: value!));
    }

    [Fact]
    public void LauncherIdentityRejectsUnsupportedProtocol()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateIdentity(protocolVersion: 2));
    }

    [Fact]
    public void LauncherIdentityRejectsNonCanonicalExecutablePath()
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(executableRelativePath: "launcher.exe"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ManagedImmutableBootstrapIdentity.MaximumExecutableBytes + 1)]
    public void LauncherIdentityRejectsUnsafeExecutableSize(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateIdentity(size: value));
    }

    // Same frozen length and assertion, using the descriptor-bound Core factory.
    [Fact]
    public void ImmutableBootstrapIdentityAdmitsCanonicalMaximum()
    {
        ManagedImmutableBootstrapIdentity identity = ManagedImmutableBootstrapIdentity.Create(
            ContractFixture.Descriptor, ContractFixture.BootstrapFileName, 200_000_000, ContractFixture.LauncherSha);
        Assert.Equal(200_000_000, identity.Length);
    }

    [Fact]
    public void ImmutableBootstrapIdentityRejectsAboveCanonicalMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedImmutableBootstrapIdentity.Create(
            ContractFixture.Descriptor, ContractFixture.BootstrapFileName,
            ManagedImmutableBootstrapIdentity.MaximumExecutableBytes + 1, ContractFixture.LauncherSha));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void LauncherIdentityRejectsInvalidExecutableIdentity(string? value)
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(sha256: value!));
    }

    // Preserves all exact-owner assertions from the frozen test and its local fixture.
    [Fact]
    public void LauncherIdentityMatchesOnlyItsExactOwnerAdmission()
    {
        ManagedLauncherIdentity identity = ContractFixture.CreateIdentity();
        ManagedVersionAdmission exact = ContractFixture.Admission(ContractFixture.App100, "admission-1.0.0", 'c');
        Assert.True(identity.MatchesOwner(exact));
        Assert.False(identity.MatchesOwner(ContractFixture.Admission(ContractFixture.App101, "admission-1.0.0", 'c')));
        Assert.False(identity.MatchesOwner(ContractFixture.Admission(ContractFixture.App100, "other-admission", 'c')));
        Assert.False(identity.MatchesOwner(ContractFixture.Admission(ContractFixture.App100, "admission-1.0.0", 'd')));
        Assert.Throws<ArgumentNullException>(() => identity.MatchesOwner(null!));
    }

    [Fact]
    public void OwnerAndExecutablePathsAreComparedOrdinally()
    {
        ManagedLauncherIdentity identity = ContractFixture.CreateIdentity();
        Assert.False(identity.MatchesOwner(ContractFixture.Admission(ContractFixture.App100, "Admission-1.0.0", 'c')));
        Assert.False(identity.MatchesOwner(ContractFixture.Admission(ContractFixture.App100, "admission-1.0.0", 'C')));
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(
            executableRelativePath: "launcher/fixtureproduct.Launcher.exe"));
    }

    [Fact]
    public void IdentityUsesTheSuppliedDescriptorAndExecutableCeiling()
    {
        ProductDescriptor descriptor = ContractFixture.CreateDescriptor(launcherPath: "bin/FixtureProduct.Launcher.exe");
        ManagedLauncherIdentity identity = ContractFixture.CreateIdentity(descriptor: descriptor,
            executableRelativePath: descriptor.LauncherExecutableRelativePath, maximumExecutableBytes: 123);
        Assert.Equal(descriptor.LauncherExecutableRelativePath, identity.ExecutableRelativePath);
        Assert.Equal(123, identity.Size);
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateIdentity(maximumExecutableBytes: 122));
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(descriptor: descriptor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ManagedImmutableBootstrapIdentity.MaximumExecutableBytes + 1)]
    public void IdentityRejectsInvalidExplicitExecutableCeilings(long maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateIdentity(maximumExecutableBytes: maximum));
    }

    [Fact]
    public void LauncherIdentityRetainsEveryFrozenIdentityField()
    {
        ManagedLauncherIdentity identity = ContractFixture.CreateIdentity();
        Assert.Equal(ContractFixture.App100, identity.OwnerAppVersion);
        Assert.Equal("admission-1.0.0", identity.OwnerAdmissionIdentity);
        Assert.Equal(ContractFixture.ManifestSha, identity.OwnerReleaseManifestSha256);
        Assert.Equal(ContractFixture.App100, identity.LauncherVersion);
        Assert.Equal(1, identity.ProtocolVersion);
        Assert.Equal(ContractFixture.LauncherPath, identity.ExecutableRelativePath);
        Assert.Equal(123, identity.Size);
        Assert.Equal(ContractFixture.LauncherSha, identity.Sha256);
        Assert.Equal(identity, ContractFixture.CreateIdentity());
        Assert.NotEqual(identity, ContractFixture.CreateIdentity(owner: ContractFixture.App101));
        Assert.NotEqual(identity, ContractFixture.CreateIdentity(ownerAdmissionIdentity: "other-admission"));
        Assert.NotEqual(identity, ContractFixture.CreateIdentity(ownerReleaseManifestSha256: new string('d', 64)));
        Assert.NotEqual(identity, ContractFixture.CreateIdentity(size: 124));
        Assert.NotEqual(identity, ContractFixture.CreateIdentity(sha256: new string('b', 64)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FixtureProduct.Bootstrap.exe ")]
    [InlineData("fixtureproduct.Bootstrap.exe")]
    [InlineData("dir/FixtureProduct.Bootstrap.exe")]
    public void BootstrapIdentityRequiresTheExactDescriptorFilename(string? fileName)
    {
        Assert.Throws<ArgumentException>(() => ManagedImmutableBootstrapIdentity.Create(
            ContractFixture.Descriptor, fileName!, 123, ContractFixture.LauncherSha));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BootstrapIdentityRejectsNonpositiveLengths(long length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedImmutableBootstrapIdentity.Create(
            ContractFixture.Descriptor, ContractFixture.BootstrapFileName, length, ContractFixture.LauncherSha));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void BootstrapIdentityRejectsInvalidExecutableDigests(string? digest)
    {
        Assert.Throws<ArgumentException>(() => ManagedImmutableBootstrapIdentity.Create(
            ContractFixture.Descriptor, ContractFixture.BootstrapFileName, 123, digest!));
    }

    [Fact]
    public void IdentityFactoriesRequireTheDescriptor()
    {
        Assert.Throws<ArgumentNullException>(() => ManagedLauncherIdentity.Create(null!, 123,
            ContractFixture.App100, "admission-1.0.0", ContractFixture.ManifestSha, ContractFixture.App100,
            1, ContractFixture.LauncherPath, 123, ContractFixture.LauncherSha));
        Assert.Throws<ArgumentNullException>(() => ManagedImmutableBootstrapIdentity.Create(
            null!, ContractFixture.BootstrapFileName, 123, ContractFixture.LauncherSha));
    }
}
