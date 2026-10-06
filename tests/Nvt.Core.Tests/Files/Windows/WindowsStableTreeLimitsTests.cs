// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Xunit;

namespace Nvt.Core.Tests.Files.Windows;

/// <summary>Characterizes explicit positive custody limits and checked installed reservations.</summary>
public sealed class WindowsStableTreeLimitsTests
{
    /// <summary>Every custody ceiling is positive, checked in file, directory, byte order.</summary>
    [Theory]
    [InlineData(0, 1, 1, "maximumFiles")]
    [InlineData(-1, 1, 1, "maximumFiles")]
    [InlineData(1, 0, 1, "maximumDirectories")]
    [InlineData(1, -1, 1, "maximumDirectories")]
    [InlineData(1, 1, 0, "maximumBytes")]
    [InlineData(1, 1, -1, "maximumBytes")]
    [InlineData(0, 0, 0, "maximumFiles")]
    public void TreeLimitsRejectNonpositiveCeilings(int files, int directories, long bytes, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WindowsStableTreeLimits(files, directories, bytes));
        Assert.Equal(parameter, error.ParamName);
        long invalid = parameter switch
        {
            "maximumFiles" => files,
            "maximumDirectories" => directories,
            _ => bytes,
        };
        if (invalid < 0)
        {
            var frozenError = Assert.Throws<ArgumentOutOfRangeException>(() =>
                ArgumentOutOfRangeException.ThrowIfNegative(invalid, parameter));
            Assert.Equal(frozenError.Message, error.Message);
        }
    }

    /// <summary>Every installed reservation input is explicitly positive.</summary>
    [Theory]
    [InlineData(0, 1, 1, 1, "maximumFiles")]
    [InlineData(-1, 1, 1, 1, "maximumFiles")]
    [InlineData(1, 0, 1, 1, "maximumDirectories")]
    [InlineData(1, -1, 1, 1, "maximumDirectories")]
    [InlineData(1, 1, 0, 1, "maximumExpandedBytes")]
    [InlineData(1, 1, -1, 1, "maximumExpandedBytes")]
    [InlineData(1, 1, 1, 0, "maximumAdmissionBytes")]
    [InlineData(1, 1, 1, -1, "maximumAdmissionBytes")]
    public void InstalledLimitsRejectNonpositiveInputs(
        int files, int directories, long expanded, int admission, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowsStableTreeLimits.ForInstalledVersion(files, directories, expanded, admission));
        Assert.Equal(parameter, error.ParamName);
    }

    /// <summary>The allowance uses checked addition at the long boundary.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void InstalledAllowanceUsesCheckedAddition(int offset)
    {
        long expanded = long.MaxValue - 4096 + offset;
        if (offset > 0)
        {
            Assert.Throws<OverflowException>(() =>
                WindowsStableTreeLimits.ForInstalledVersion(4097, 4096, expanded, 4096));
        }
        else
        {
            Assert.Equal(long.MaxValue + offset,
                WindowsStableTreeLimits.ForInstalledVersion(4097, 4096, expanded, 4096).MaximumBytes);
        }
    }

    /// <summary>Frozen expansion and admission neighbors retain their exact summed allowance.</summary>
    [Theory]
    [InlineData(536870911, 4096, 536875007)]
    [InlineData(536870912, 4096, 536875008)]
    [InlineData(536870913, 4096, 536875009)]
    [InlineData(536870912, 4095, 536875007)]
    [InlineData(536870912, 4097, 536875009)]
    public void InstalledAllowancePreservesExplicitProductInputs(long expanded, int admission, long expected)
    {
        Assert.Equal(expected,
            WindowsStableTreeLimits.ForInstalledVersion(4097, 4096, expanded, admission).MaximumBytes);
    }

    /// <summary>Every frozen dimension admits below and exact, and rejects above and negative reservations.</summary>
    [Theory]
    [InlineData(4096, 4096, 536875008, true)]
    [InlineData(4097, 4096, 536875008, true)]
    [InlineData(4098, 4096, 536875008, false)]
    [InlineData(4097, 4095, 536875008, true)]
    [InlineData(4097, 4097, 536875008, false)]
    [InlineData(4097, 4096, 536875007, true)]
    [InlineData(4097, 4096, 536875009, false)]
    [InlineData(-1, 0, 0, false)]
    [InlineData(0, -1, 0, false)]
    [InlineData(0, 0, -1, false)]
    [InlineData(0, 0, 0, true)]
    public void FrozenTreeReservationBoundaries(int files, int directories, long bytes, bool accepted)
    {
        var limits = WindowsStableTreeLimits.ForInstalledVersion(4097, 4096, 536870912, 4096);
        Assert.Equal(accepted, new WindowsStableTreeReservation(files, directories, bytes, limits).IsWithinLimits);
    }

    /// <summary>Default structs cannot bypass the mandatory positive limits.</summary>
    [Fact]
    public void DefaultLimitsFailBeforePathOrCancellation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowsStablePathCustody.TryAcquireImmutableTree("relative", default,
                TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowsStablePathCustody.TryAcquirePromotableTree("relative", default,
                TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WindowsStableTreeReservation(0, 0, 0, default).IsWithinLimits);
    }
}
