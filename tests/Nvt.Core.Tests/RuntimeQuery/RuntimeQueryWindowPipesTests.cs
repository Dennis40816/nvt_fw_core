// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks names and the selection rule without real pipes or processes.</summary>
public sealed class RuntimeQueryWindowPipesTests
{
    /// <summary>The name retains the base text and uses invariant decimal digits.</summary>
    [Fact]
    public void BuildNameUsesExactFormat()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal("synthetic.runtime.v1.123", RuntimeQueryWindowPipes.BuildName("synthetic.runtime.v1", 123));
            Assert.Equal(" base .2147483647", RuntimeQueryWindowPipes.BuildName(" base ", int.MaxValue));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Null, empty and whitespace base names are rejected.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void BuildNameRejectsBlankBaseName(string? baseName)
    {
        Assert.ThrowsAny<ArgumentException>(() => RuntimeQueryWindowPipes.BuildName(baseName!, 123));
    }

    /// <summary>Zero and negative process IDs are rejected.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void BuildNameRejectsNonpositiveProcessId(int processId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(nameof(processId), () => RuntimeQueryWindowPipes.BuildName("synthetic", processId));
    }

    /// <summary>Literal candidates and results cover selection, ties, requested IDs and empty inputs.</summary>
    public static IEnumerable<object?[]> Selections =>
    [
        [new[] { (10, DateTime.UnixEpoch) }, null, 10],
        [new[] { (10, DateTime.UnixEpoch.AddSeconds(1)), (20, DateTime.UnixEpoch) }, null, 10],
        [new[] { (10, DateTime.UnixEpoch), (20, DateTime.UnixEpoch.AddSeconds(1)) }, null, 20],
        [new[] { (10, DateTime.UnixEpoch), (20, DateTime.UnixEpoch) }, null, 20],
        [new[] { (20, DateTime.UnixEpoch), (10, DateTime.UnixEpoch) }, null, 20],
        [new[] { (10, DateTime.UnixEpoch), (20, DateTime.UnixEpoch.AddSeconds(1)) }, 10, 10],
        [new[] { (10, DateTime.UnixEpoch), (20, DateTime.UnixEpoch.AddSeconds(1)) }, 20, 20],
        [new[] { (10, DateTime.UnixEpoch) }, 30, null],
        [Array.Empty<(int, DateTime)>(), null, null],
        [Array.Empty<(int, DateTime)>(), 10, null]
    ];

    /// <summary>Selection depends only on supplied candidates and the requested process ID.</summary>
    [Theory]
    [MemberData(nameof(Selections))]
    public void SelectProcessIdReturnsExactResult((int ProcessId, DateTime StartTime)[] candidates, int? requested, int? expected)
    {
        Assert.Equal(expected, RuntimeQueryWindowPipes.SelectProcessId(candidates, requested));
    }

    /// <summary>Other platforms have no discovery candidates.</summary>
    [Fact]
    public void DiscoveryReturnsNoCandidatesOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("This contract applies outside Windows.");
            return;
        }

        Assert.Empty(RuntimeQueryWindowPipes.DiscoverCandidates("synthetic"));
    }
}
