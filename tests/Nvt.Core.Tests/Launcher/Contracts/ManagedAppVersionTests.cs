// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Contracts;

public sealed class ManagedAppVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("0.10.6")]
    [InlineData("1.0.0")]
    [InlineData("12.345.6789")]
    public void CanonicalStableVersionRoundTrips(string value)
    {
        ManagedAppVersion version = ManagedAppVersion.Parse(value);
        Assert.Equal(value, version.ToString());
    }

    // Ported with every original input and assertion from the frozen source test.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.00.0")]
    [InlineData("1.0.00")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("1.0.0+build.1")]
    [InlineData("1.0.0 ")]
    [InlineData("-1.0.0")]
    [InlineData("1.a.0")]
    public void NonCanonicalOrNonStableVersionFailsClosed(string? value)
    {
        Assert.False(ManagedAppVersion.TryParse(value, out _));
    }

    [Fact]
    public void ParseRejectsMalformedVersion()
    {
        Assert.Throws<FormatException>(() => ManagedAppVersion.Parse("v1.0.0"));
    }

    [Fact]
    public void VersionOrderingUsesNumericComponents()
    {
        ManagedAppVersion[] versions =
        [
            ManagedAppVersion.Parse("1.0.0"),
            ManagedAppVersion.Parse("0.10.10"),
            ManagedAppVersion.Parse("0.10.2"),
            ManagedAppVersion.Parse("0.9.99"),
        ];

        Assert.Equal(["0.9.99", "0.10.2", "0.10.10", "1.0.0"],
            versions.Order().Select(version => version.ToString()));

        ManagedAppVersion lower = ManagedAppVersion.Parse("1.9.99");
        ManagedAppVersion higher = ManagedAppVersion.Parse("1.10.0");
        ManagedAppVersion equal = ManagedAppVersion.Parse("1.10.0");
        Assert.True(lower < higher);
        Assert.True(higher > lower);
        Assert.True(lower <= higher);
        Assert.True(higher >= lower);
        Assert.True(higher <= equal);
        Assert.True(higher >= equal);
        Assert.Equal(0, higher.CompareTo(equal));
    }

    [Fact]
    public void DefaultVersionAndMaximumComponentsRemainCanonical()
    {
        Assert.Equal("0.0.0", default(ManagedAppVersion).ToString());
        ManagedAppVersion maximum = ManagedAppVersion.Parse("2147483647.2147483647.2147483647");
        Assert.Equal(int.MaxValue, maximum.Major);
        Assert.Equal(int.MaxValue, maximum.Minor);
        Assert.Equal(int.MaxValue, maximum.Patch);
        Assert.False(ManagedAppVersion.TryParse("2147483648.0.0", out ManagedAppVersion rejected));
        Assert.Equal(default, rejected);
    }
}
