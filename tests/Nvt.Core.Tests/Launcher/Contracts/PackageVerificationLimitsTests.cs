// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Contracts;

/// <summary>Checks explicit positive verification ceiling arguments and their unchanged order.</summary>
public sealed class PackageVerificationLimitsTests
{
    /// <summary>Every product ceiling rejects zero and negative constructor arguments.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EveryCeilingRequiresAPositiveArgument(int value)
    {
        Assert.Equal("MaximumArchiveEntries", Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVerificationLimits(value, 1, 1, 1, 1, 1, 1)).ParamName);
        Assert.Equal("MaximumPackageBytes", Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVerificationLimits(1, value, 1, 1, 1, 1, 1)).ParamName);
        Assert.Equal("MaximumExpandedBytes", Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVerificationLimits(1, 1, value, 1, 1, 1, 1)).ParamName);
        Assert.Equal("MaximumManifestBytes", Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVerificationLimits(1, 1, 1, value, 1, 1, 1)).ParamName);
        Assert.Equal("MaximumAdmissionBytes", Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVerificationLimits(1, 1, 1, 1, value, 1, 1)).ParamName);
        Assert.Equal("MaximumExecutableBytes", Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVerificationLimits(1, 1, 1, 1, 1, value, 1)).ParamName);
        Assert.Equal("MaximumInstalledDirectories", Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVerificationLimits(1, 1, 1, 1, 1, 1, value)).ParamName);
    }

    /// <summary>Mutable initialization syntax cannot bypass the consuming verifier's ceiling checks.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void VerifierRechecksEveryCopiedCeiling(int value)
    {
        PackageVerificationLimits limits = PackageFixture.FrozenLimits;
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumArchiveEntries = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumPackageBytes = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumExpandedBytes = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumManifestBytes = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumAdmissionBytes = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumExecutableBytes = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumInstalledDirectories = value }));
    }

    /// <summary>All minimum positive limits are admitted; NFC values are supplied explicitly by the product.</summary>
    [Fact]
    public void MinimumAndFrozenLimitsHaveNoImplicitProductDefaults()
    {
        _ = Create(new(1, 1, 1, 1, 1, 1, 1));
        PackageVerificationLimits limits = PackageFixture.FrozenLimits;
        var (entries, package, expanded, manifest, admission, executable, directories) = limits;
        Assert.Equal(4096, entries);
        Assert.Equal(134_217_728, package);
        Assert.Equal(536_870_912, expanded);
        Assert.Equal(1_048_576, manifest);
        Assert.Equal(4096, admission);
        Assert.Equal(200_000_000, executable);
        Assert.Equal(4096, directories);
        _ = Create(limits);
        _ = Create(limits with { MaximumExecutableBytes = 199_999_999 });
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(limits with { MaximumExecutableBytes = 200_000_001 }));
    }

    private static ManagedPackageVerifier Create(PackageVerificationLimits limits)
    {
        PackageFixture fixture = PackageFixture.Create();
        return fixture.Verifier(limits);
    }
}
