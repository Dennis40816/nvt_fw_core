// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Pins the relative performance helper. A manual clock makes every time deterministic.</summary>
public sealed class RelativePerfTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The minimum sample count is seven.</summary>
    [Fact]
    public void MinimumSamplesIsSeven()
    {
        Assert.Equal(7, RelativePerf.MinimumSamples);
    }

    /// <summary>The median is the middle sample and the warm-up run is discarded.</summary>
    [Fact]
    public void MedianTimeDiscardsWarmupAndPicksMiddleSample()
    {
        var clock = new ManualTimeProvider(_start);
        int[] seconds = [100, 9, 1, 8, 2, 7, 3, 6];
        int run = 0;

        TimeSpan median = RelativePerf.MedianTime(() => clock.Advance(TimeSpan.FromSeconds(seconds[run++])), samples: 7, warmups: 1, clock: clock);

        Assert.Equal(TimeSpan.FromSeconds(6), median);
        Assert.Equal(8, run);
    }

    /// <summary>A sample count below the minimum is rejected before any work runs.</summary>
    [Fact]
    public void FewSamplesAreRejected()
    {
        var clock = new ManualTimeProvider(_start);
        int runs = 0;

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RelativePerf.MedianTime(() => runs++, samples: 6, clock: clock));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RelativePerf.MedianTime(() => runs++, warmups: -1, clock: clock));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RelativePerf.MedianAllocatedBytes(() => runs++, samples: 1));
        Assert.Equal(0, runs);
    }

    /// <summary>Missing work is rejected.</summary>
    [Fact]
    public void MissingWorkIsRejected()
    {
        _ = Assert.Throws<ArgumentNullException>(() => RelativePerf.MedianTime(null!));
        _ = Assert.Throws<ArgumentNullException>(() => RelativePerf.MedianAllocatedBytes(null!));
    }

    /// <summary>A time is a multiple of the unit, and a unit of zero is rejected.</summary>
    [Fact]
    public void InUnitsDividesByTheUnit()
    {
        Assert.Equal(2.5, RelativePerf.InUnits(TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(10)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RelativePerf.InUnits(TimeSpan.FromSeconds(1), TimeSpan.Zero));
    }

    /// <summary>The scale ratio is the larger median divided by the smaller one.</summary>
    [Fact]
    public void ScaleRatioDividesTheMedians()
    {
        var clock = new ManualTimeProvider(_start);

        double ratio = RelativePerf.ScaleRatio(
            () => clock.Advance(TimeSpan.FromMilliseconds(10)),
            () => clock.Advance(TimeSpan.FromMilliseconds(25)),
            clock: clock);

        Assert.Equal(2.5, ratio);
    }

    /// <summary>A smaller work that measures no time gives no ratio.</summary>
    [Fact]
    public void ScaleRatioWithZeroSmallerTimeThrows()
    {
        var clock = new ManualTimeProvider(_start);

        _ = Assert.Throws<InvalidOperationException>(() => RelativePerf.ScaleRatio(() => { }, () => clock.Advance(TimeSpan.FromSeconds(1)), clock: clock));
    }

    /// <summary>A clock that measures nothing gives no calibration unit.</summary>
    [Fact]
    public void CalibrationUnitWithZeroTimeThrows()
    {
        var clock = new ManualTimeProvider(_start);

        _ = Assert.Throws<InvalidOperationException>(() => RelativePerf.CalibrationUnit(clock: clock));
    }

    /// <summary>The real clock gives a positive unit. The test asserts no particular size.</summary>
    [Fact]
    public void CalibrationUnitOnTheSystemClockIsPositive()
    {
        Assert.True(RelativePerf.CalibrationUnit() > TimeSpan.Zero);
    }

    /// <summary>The allocation count covers what the work allocates.</summary>
    [Fact]
    public void MedianAllocatedBytesCountsTheAllocation()
    {
        long bytes = RelativePerf.MedianAllocatedBytes(() => GC.KeepAlive(new byte[10_000]));

        Assert.InRange(bytes, 10_000, 100_000);
    }

    /// <summary>Work that allocates nothing reports a small count.</summary>
    [Fact]
    public void MedianAllocatedBytesOfEmptyWorkIsSmall()
    {
        long bytes = RelativePerf.MedianAllocatedBytes(static () => { });

        Assert.InRange(bytes, 0, 1_000);
    }
}
