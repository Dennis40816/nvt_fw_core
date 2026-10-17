// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Checks every mechanical bound of caller-admitted setup projections.</summary>
public sealed class SetupProjectionBoundaryTests : FirstInstallationTestSupport
{
    /// <summary>The registry identity uses the caller's exact character ceiling.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    public void RegistryIdentityAcceptsPositiveLengthsThroughTheExactCeiling(int length)
    {
        FreshInstallationCandidateIdentity identity = Identity(registryId: new string('r', length));
        Assert.Equal(length, identity.RegistryId.Length);
    }

    /// <summary>One character beyond the frozen caller ceiling is rejected.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RegistryIdentityRejectsMissingIdentity(string? registryId)
    {
        var exception = Assert.Throws<ArgumentException>(() => Identity(registryId: registryId));
        Assert.Equal("Fresh installation candidate identity is invalid.", exception.Message);
    }

    /// <summary>The explicit registry bound is positive and is applied without a fallback.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void RegistryIdentityRejectsNonpositiveCallerCeiling(int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Identity(maximum: maximum));
    }

    /// <summary>Exact caller ceilings reject one extra identity character.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public void RegistryIdentityRejectsOneAboveCallerCeiling(int maximum)
    {
        Assert.Throws<ArgumentException>(() => Identity(maximum: maximum, registryId: new string('r', maximum + 1)));
    }

    /// <summary>Registry revision, catalog schema and package size preserve their positive bound.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void CandidateIdentityRejectsEachNonpositiveProjection(long value)
    {
        Assert.Throws<ArgumentException>(() => Identity(revision: value));
        Assert.Throws<ArgumentException>(() => Identity(schema: (int)value));
        Assert.Throws<ArgumentException>(() => Identity(packageSize: value));
    }

    /// <summary>Positive numeric boundaries retain exact values without a new product cap.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void CandidateIdentityRetainsPositiveProjectionValues(int value)
    {
        FreshInstallationCandidateIdentity identity = Identity(revision: value, schema: value, packageSize: value);
        Assert.Equal(value, identity.RegistryRevision);
        Assert.Equal(value, identity.CatalogSchemaVersion);
        Assert.Equal(value, identity.PackageSize);
    }

    /// <summary>Every captured digest requires exactly 64 lowercase hexadecimal characters.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("g")]
    [InlineData("short")]
    public void CandidateIdentityRejectsMalformedDigests(string? digest)
    {
        Assert.Throws<ArgumentException>(() => Identity(registryDigest: digest));
        Assert.Throws<ArgumentException>(() => Identity(catalogDigest: digest));
        Assert.Throws<ArgumentException>(() => Identity(packageDigest: digest));
        Assert.Throws<ArgumentException>(() => Identity(manifestDigest: digest));
    }

    /// <summary>Both neighboring lengths fail for every exact digest field.</summary>
    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void CandidateIdentityRejectsDigestLengthNeighbors(int length)
    {
        string digest = new('a', length);
        Assert.Throws<ArgumentException>(() => Identity(registryDigest: digest));
        Assert.Throws<ArgumentException>(() => Identity(catalogDigest: digest));
        Assert.Throws<ArgumentException>(() => Identity(packageDigest: digest));
        Assert.Throws<ArgumentException>(() => Identity(manifestDigest: digest));
    }

    /// <summary>The mandatory projection cannot smuggle an undefined selection role.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void CandidateIdentityRejectsUndefinedSourceStatus(int status)
    {
        Assert.Throws<ArgumentException>(() => Identity(status: (FreshInstallationSourceStatus)status));
    }

    /// <summary>The source role remains a captured fact rather than Core selection policy.</summary>
    [Theory]
    [InlineData(FreshInstallationSourceStatus.Latest)]
    [InlineData(FreshInstallationSourceStatus.Available)]
    [InlineData(FreshInstallationSourceStatus.Deprecated)]
    public void CandidateIdentityRetainsEveryAdmittedSourceStatus(FreshInstallationSourceStatus status)
    {
        Assert.Equal(status, Identity(status: status).SourceStatus);
    }

    /// <summary>The existing 512-character path mechanism retains its exact boundary.</summary>
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    public void CandidateIdentityAcceptsPackagePathBoundary(int length)
    {
        Assert.Equal(length, Identity(packagePath: new string('p', length)).PackagePath.Length);
    }

    /// <summary>Unsafe and oversized package paths cannot become publication identities.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../package.zip")]
    [InlineData("/package.zip")]
    [InlineData("packages/CON.zip")]
    [InlineData("packages/a:stream")]
    public void CandidateIdentityRejectsUnsafePackagePath(string? path)
    {
        Assert.Throws<ArgumentException>(() => Identity(packagePath: path));
    }

    /// <summary>One character beyond the fixed relative path ceiling fails.</summary>
    [Fact]
    public void CandidateIdentityRejectsPackagePathAboveBoundary()
    {
        Assert.Throws<ArgumentException>(() => Identity(packagePath: new string('p', 513)));
    }

    /// <summary>Catalog identity must name an absolute file in the exact supplied root.</summary>
    [Theory]
    [InlineData("relative.json")]
    [InlineData("")]
    [InlineData(null)]
    public void CandidateIdentityRejectsNonabsoluteCatalogPath(string? catalog)
    {
        Assert.Throws<ArgumentException>(() => Identity(catalogPath: catalog));
    }

    /// <summary>A different parent directory is not the captured catalog authority.</summary>
    [Fact]
    public void CandidateIdentityRejectsDifferentCatalogParent()
    {
        Assert.Throws<ArgumentException>(() => Identity(catalogPath: Root("other/catalog.json")));
    }

    /// <summary>Descriptor bytes preserve the caller's 65,536-byte projection ceiling.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(65_535)]
    [InlineData(65_536)]
    public void PayloadIdentityAcceptsDescriptorBoundary(long length)
    {
        Assert.Equal(length, Payload(descriptorSize: length).DescriptorSize);
    }

    /// <summary>Descriptor bytes are positive and cannot exceed the explicit ceiling.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65_537)]
    public void PayloadIdentityRejectsDescriptorOutsideBoundary(long length)
    {
        var exception = Assert.Throws<ArgumentException>(() => Payload(descriptorSize: length));
        Assert.Equal("Distribution Launcher payload identity is invalid.", exception.Message);
    }

    /// <summary>No omitted or nonpositive descriptor ceiling can admit a payload.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void PayloadIdentityRejectsNonpositiveCallerCeiling(long maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Payload(maximum: maximum));
    }

    /// <summary>The smallest positive ceiling applies exactly.</summary>
    [Fact]
    public void PayloadIdentityUsesTheExactCallerCeiling()
    {
        Assert.Equal(1, Payload(maximum: 1, descriptorSize: 1).DescriptorSize);
        Assert.Throws<ArgumentException>(() => Payload(maximum: 1, descriptorSize: 2));
    }

    /// <summary>Launcher lengths retain their positive-only source predicate.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void PayloadIdentityRejectsNonpositiveLauncherSize(long size)
    {
        Assert.Throws<ArgumentException>(() => Payload(launcherSize: size));
    }

    /// <summary>The source adds no executable ceiling to the running-launcher observation.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(long.MaxValue)]
    public void PayloadIdentityRetainsPositiveLauncherSize(long size)
    {
        Assert.Equal(size, Payload(launcherSize: size).LauncherSize);
    }

    /// <summary>Source commits retain the exact 40-character lowercase-hex grammar.</summary>
    [Theory]
    [InlineData(39)]
    [InlineData(41)]
    public void PayloadIdentityRejectsCommitLengthNeighbors(int length)
    {
        Assert.Throws<ArgumentException>(() => Payload(commit: new string('a', length)));
    }

    /// <summary>Malformed exact-length commit and digest projections remain invalid.</summary>
    [Theory]
    [InlineData('A')]
    [InlineData('g')]
    public void PayloadIdentityRejectsNonlowercaseHex(char character)
    {
        Assert.Throws<ArgumentException>(() => Payload(commit: new string(character, 40)));
        Assert.Throws<ArgumentException>(() => Payload(launcherDigest: new string(character, 64)));
        Assert.Throws<ArgumentException>(() => Payload(descriptorDigest: new string(character, 64)));
    }

    /// <summary>Payload digests have the same exact SHA-256 length bound.</summary>
    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void PayloadIdentityRejectsDigestLengthNeighbors(int length)
    {
        Assert.Throws<ArgumentException>(() => Payload(launcherDigest: new string('a', length)));
        Assert.Throws<ArgumentException>(() => Payload(descriptorDigest: new string('a', length)));
    }

    /// <summary>Progress uses exact zero, total and overflow-safe long boundaries.</summary>
    [Theory]
    [InlineData(0, long.MaxValue, 0)]
    [InlineData(long.MaxValue - 1, long.MaxValue, 99)]
    [InlineData(long.MaxValue, long.MaxValue, 100)]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 100)]
    public void ProgressCalculatesExactBoundaryPercent(long completed, long total, int percent)
    {
        Assert.Equal(percent, new ManagedFirstInstallationProgress(
            ManagedFirstInstallationProgressStage.ReadingPackage, completed, total).Percent);
    }

    /// <summary>Invalid work bounds retain the exact source error.</summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    public void ProgressRejectsWorkOutsideBoundaries(long completed, long total)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedFirstInstallationProgress(
            ManagedFirstInstallationProgressStage.ReadingPackage, completed, total));
        Assert.Equal("completedWork", exception.ParamName);
    }

    /// <summary>Long publication values retain the frozen positive-only domain.</summary>
    [Fact]
    public void CandidateIdentityRetainsMaximumPositiveLongProjections()
    {
        FreshInstallationCandidateIdentity identity = Identity(revision: long.MaxValue, packageSize: long.MaxValue);
        Assert.Equal(long.MaxValue, identity.RegistryRevision);
        Assert.Equal(long.MaxValue, identity.PackageSize);
    }

    /// <summary>Null payload fields and missing Bootstrap custody cannot form a projection.</summary>
    [Fact]
    public void PayloadIdentityRejectsMissingFieldsInSourceOrder()
    {
        Assert.Throws<ArgumentException>(() => Payload(commit: null));
        Assert.Throws<ArgumentException>(() => Payload(launcherDigest: null));
        Assert.Throws<ArgumentException>(() => Payload(descriptorDigest: null));
        Assert.Equal("bootstrap", Assert.Throws<ArgumentNullException>(() => new ManagedDistributionPayloadIdentity(
            65_536, ManagedAppVersion.Parse("1.0.4"), new string('a', 40), 1, HashA, 1, HashB, null!)).ParamName);
        Assert.Throws<ArgumentException>(() => new ManagedDistributionPayloadIdentity(
            65_536, ManagedAppVersion.Parse("1.0.4"), new string('a', 39), 1, HashA, 1, HashB, null!));
    }

    private FreshInstallationCandidateIdentity Identity(
        int maximum = 128, string? registryId = "fixture", long revision = 1,
        string? registryDigest = HashA, int schema = 1, string? catalogDigest = HashB,
        string? catalogPath = "default", FreshInstallationSourceStatus status = FreshInstallationSourceStatus.Latest,
        string? packagePath = "package.zip", long packageSize = 1,
        string? packageDigest = HashA, string? manifestDigest = HashB) =>
        new(maximum, registryId!, revision, registryDigest!, schema, ManagedAppVersion.Parse("1.0.4"),
            catalogDigest!, catalogPath == "default" ? Root("source/catalog.json") : catalogPath!,
            Root("source"), status, packagePath!, packageSize, packageDigest!, manifestDigest!);

    private static ManagedDistributionPayloadIdentity Payload(long maximum = 65_536,
        long descriptorSize = 1, long launcherSize = 1, string? commit = "default",
        string? launcherDigest = HashA, string? descriptorDigest = HashB) =>
        new(maximum, ManagedAppVersion.Parse("1.0.4"), commit == "default" ? new string('a', 40) : commit!,
            launcherSize, launcherDigest!, descriptorSize, descriptorDigest!, Bootstrap);
}
