// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.TestSupport;
using Xunit;
using Xunit.Sdk;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Measures user intent plus viewport layout on the shared headless session.</summary>
public sealed class ConsoleScrollPerformanceTests(ITestOutputHelper output)
{
    // Reference median: 3,992.52 units for a full headless viewport traversal; allow a 2x margin (rounded up).
    // This includes text layout/container recycling rather than timing only an offset assignment.
    private const double ScrollCalibrationRatio = 8000;
    // Count row visits during height rebuilding, synchronization, measure/realization and arrangement.
    // Linear traversal doubles the work (2.0); allow 25% margin, independent of host scheduling.
    private const double DoublingRatio = 2.5;
    // Small identity/state records fit in 4 KiB; copying 10,000 row IDs allocates over 160 KiB.
    private const long ScrollAllocationLimit = 4096;
    // One invalidation plus one geometry correction; theme propagation permits one additional pass.
    private const int ScrollMeasureLimit = 2;
    private const int ThemeMeasureLimit = 3;

    [AvaloniaFact, Trait("Category", "Performance")]
    public void TenThousandRowScrollStaysWithinCalibrationRatio()
    {
        using var scene = new ConsoleScrollScene(10_000);
        scene.Pause();
        var unit = RelativePerf.CalibrationUnit();
        var units = RelativePerf.InUnits(RelativePerf.MedianTime(scene.Traverse), unit);
        output.WriteLine($"Traversal calibration ratio: {units:F2}; limit {ScrollCalibrationRatio}");
        RequireScrollRatio(units);
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void CalibrationPolicyRejectsADeliberatelySlowPath()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var unit = RelativePerf.CalibrationUnit();
        var work = RelativePerf.MedianTime(() => clock.Advance(unit * (ScrollCalibrationRatio + 1)), clock: clock);
        Assert.ThrowsAny<XunitException>(() => RequireScrollRatio(RelativePerf.InUnits(work, unit)));
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void TwentyThousandRowScrollWorkScalesWithinTwoPointFive()
    {
        using var smaller = new ConsoleScrollScene(10_000);
        using var larger = new ConsoleScrollScene(20_000);
        smaller.Pause();
        larger.Pause();
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var smallVisits = smaller.TraversalRowVisits();
        var largeVisits = larger.TraversalRowVisits();
        // Encode operation counts in the shared manual clock; no host elapsed time enters the scale policy.
        var ratio = RelativePerf.ScaleRatio(() => clock.Advance(TimeSpan.FromTicks(smaller.TraversalRowVisits())),
            () => clock.Advance(TimeSpan.FromTicks(larger.TraversalRowVisits())), clock: clock);
        output.WriteLine($"Row visits: {smallVisits} at 10,000; {largeVisits} at 20,000");
        output.WriteLine($"20,000 / 10,000 traversal ratio: {ratio:F3}; limit {DoublingRatio}");
        RequireScaleRatio(ratio);
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void ScalePolicyRejectsQuadraticWork()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var ratio = RelativePerf.ScaleRatio(() => clock.Advance(TimeSpan.FromTicks(10_000L * 10_000)),
            () => clock.Advance(TimeSpan.FromTicks(20_000L * 20_000)), clock: clock);
        Assert.ThrowsAny<XunitException>(() => RequireScaleRatio(ratio));
    }

    [AvaloniaTheory, Trait("Category", "Performance")]
    [InlineData(10_000)]
    [InlineData(20_000)]
    public void PausedUserScrollAllocatesOnlySmallStateRecords(int rowCount)
    {
        using var scene = new ConsoleScrollScene(rowCount);
        scene.Pause();
        var bytes = RelativePerf.MedianAllocatedBytes(scene.OneUserScroll);
        output.WriteLine($"{rowCount} rows: {bytes} bytes per user scroll; limit {ScrollAllocationLimit}");
        RequireAllocation(bytes);
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void AllocationPolicyRejectsTheOldWholeOrderCopy()
    {
        using var scene = new ConsoleScrollScene(10_000);
        scene.Pause();
        var bytes = RelativePerf.MedianAllocatedBytes(scene.CopyOldOrderAndScroll);
        output.WriteLine($"Old order copy: {bytes} bytes; rejected above {ScrollAllocationLimit}");
        Assert.ThrowsAny<XunitException>(() => RequireAllocation(bytes));
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void UserScrollUsesAtMostTwoMeasurePasses()
    {
        using var scene = new ConsoleScrollScene(10_000);
        scene.Pause();
        var passes = scene.ScrollMeasures();
        output.WriteLine($"User scroll host measure passes: {passes}; limit {ScrollMeasureLimit}");
        Assert.InRange(passes, 1, ScrollMeasureLimit);
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void ThemeChangeUsesAtMostThreeMeasurePasses()
    {
        using var scene = new ConsoleScrollScene(10_000);
        var passes = scene.ThemeMeasures();
        output.WriteLine($"Theme host measure passes: {passes}; limit {ThemeMeasureLimit}");
        Assert.InRange(passes, 1, ThemeMeasureLimit);
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void ScrollMeasurePolicyRejectsRepeatedWork()
    {
        using var scene = new ConsoleScrollScene(10_000);
        var passes = scene.RedundantMeasures();
        Assert.ThrowsAny<XunitException>(() => Assert.InRange(passes, 1, ScrollMeasureLimit));
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void ThemeMeasurePolicyRejectsRepeatedWork()
    {
        using var scene = new ConsoleScrollScene(10_000);
        var passes = scene.RedundantMeasures();
        Assert.ThrowsAny<XunitException>(() => Assert.InRange(passes, 1, ThemeMeasureLimit));
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void StableRowsAreNotMeasuredAgainOnASmallScroll()
    {
        using var scene = new ConsoleScrollScene(10_000);
        scene.Pause();
        var passes = scene.StableRowMeasures();
        output.WriteLine($"Stable row measure passes: {passes}; limit 2");
        Assert.InRange(passes, 0, 2);
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void RowMeasurePolicyRejectsReconfiguringEveryVisibleRow()
    {
        using var scene = new ConsoleScrollScene(10_000);
        var passes = scene.RepeatedRowMeasures();
        Assert.ThrowsAny<XunitException>(() => Assert.InRange(passes, 0, 2));
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void ScalePolicyRejectsWorkAboveTwoPointFive()
    {
        Assert.ThrowsAny<XunitException>(() => RequireScaleRatio(2.75));
    }

    private static void RequireScrollRatio(double ratio) => Assert.InRange(ratio, 0, ScrollCalibrationRatio);
    private static void RequireScaleRatio(double ratio) => Assert.InRange(ratio, 0, DoublingRatio);
    private static void RequireAllocation(long bytes) => Assert.InRange(bytes, 0, ScrollAllocationLimit);
}
