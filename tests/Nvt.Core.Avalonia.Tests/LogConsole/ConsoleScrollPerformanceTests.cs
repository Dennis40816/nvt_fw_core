// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia.Headless.XUnit;
using Nvt.Core.TestSupport;
using Xunit;
using Xunit.Sdk;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Measures user intent plus viewport layout on the shared headless session.</summary>
[Collection(ConsolePerformanceIsolation.Name)]
public sealed class ConsoleScrollPerformanceTests(ITestOutputHelper output)
{
    // The gates below count work. A count does not change with the load of the host, so a solution-level parallel run
    // cannot fail them (issue 206). Reference on 2026-10-12, 10,000 rows, one full traversal:
    // 39,980 row visits (3.998 per row), 9,998 row measures (1.000 per row), at most 2 host measure passes in any one of its 770 steps.
    // The visit and row measure limits add 25% to the reference. The host limit is the one of a single user scroll (below).
    // A count needs no warm-up and no median: one traversal gives the same number each time.
    private const double VisitsPerRowLimit = 5.0;
    private const double RowMeasuresPerRowLimit = 1.25;
    // Count row visits during height rebuilding, synchronization, measure/realization and arrangement.
    // Linear traversal doubles the work (2.0); allow 25% margin, independent of host scheduling.
    private const double DoublingRatio = 2.5;
    // Small identity/state records fit in 4 KiB; copying 10,000 row IDs allocates over 160 KiB.
    private const long ScrollAllocationLimit = 4096;
    // One invalidation plus one geometry correction; theme propagation permits one additional pass.
    private const int ScrollMeasureLimit = 2;
    private const int ThemeMeasureLimit = 3;

    [AvaloniaFact, Trait("Category", "Performance")]
    public void TenThousandRowTraversalStaysWithinTheWorkBudget()
    {
        const int rows = 10_000;
        using var scene = new ConsoleScrollScene(rows);
        scene.Pause();
        var work = scene.MeasureTraversal();
        var visitsPerRow = ((double)work.RowVisits / rows).ToString("F3", CultureInfo.InvariantCulture);
        var measuresPerRow = ((double)work.RowMeasures / rows).ToString("F3", CultureInfo.InvariantCulture);
        output.WriteLine($"Traversal work at {rows} rows: {work.RowVisits} row visits ({visitsPerRow} per row; limit {VisitsPerRowLimit}), "
            + $"{work.HostMeasurePasses} host measure passes in {work.Steps} steps, at most {work.MaxStepMeasurePasses} in one step (limit {ScrollMeasureLimit}), "
            + $"{work.RowMeasures} row measures ({measuresPerRow} per row; limit {RowMeasuresPerRowLimit})");
        RequireWorkBudget(work, rows);
    }

    [Theory, Trait("Category", "Performance")]
    [InlineData("visits")]
    [InlineData("row measures")]
    [InlineData("host passes in one step")]
    [InlineData("visits counter stopped")]
    [InlineData("row measures counter stopped")]
    [InlineData("host pass counter stopped")]
    [InlineData("step pass counter stopped")]
    public void WorkBudgetPolicyRejectsEachCountOutOfBudget(string broken)
    {
        const int rows = 1_000;
        var good = new ConsoleTraversalWork(rows * 4, 10, ScrollMeasureLimit, rows, 10);
        RequireWorkBudget(good, rows);
        var bad = broken switch
        {
            "visits" => good with { RowVisits = (long)(rows * VisitsPerRowLimit) + 1 },
            "row measures" => good with { RowMeasures = (long)(rows * RowMeasuresPerRowLimit) + 1 },
            "host passes in one step" => good with { MaxStepMeasurePasses = ScrollMeasureLimit + 1 },
            "visits counter stopped" => good with { RowVisits = 0 },
            "row measures counter stopped" => good with { RowMeasures = 0 },
            "host pass counter stopped" => good with { HostMeasurePasses = 0 },
            "step pass counter stopped" => good with { MaxStepMeasurePasses = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(broken), broken, "Unknown case."),
        };
        Assert.ThrowsAny<XunitException>(() => RequireWorkBudget(bad, rows));
    }

    [AvaloniaFact, Trait("Category", "Performance")]
    public void TwentyThousandRowScrollWorkScalesWithinTwoPointFive()
    {
        using var smaller = new ConsoleScrollScene(10_000);
        using var larger = new ConsoleScrollScene(20_000);
        smaller.Pause();
        larger.Pause();
        var smallVisits = smaller.MeasureTraversal().RowVisits;
        var largeVisits = larger.MeasureTraversal().RowVisits;
        // Operation counts, not elapsed time: no host scheduling enters the scale policy.
        Assert.InRange(smallVisits, 10_000, 50_000);
        Assert.InRange(largeVisits, 20_000, 100_000);
        var ratio = (double)largeVisits / smallVisits;
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

    private static void RequireWorkBudget(ConsoleTraversalWork work, int rows)
    {
        // A lower bound keeps a counter that stopped counting from passing.
        Assert.InRange(work.RowVisits, rows, (long)(rows * VisitsPerRowLimit));
        Assert.InRange(work.RowMeasures, rows / 2, (long)(rows * RowMeasuresPerRowLimit));
        Assert.InRange(work.HostMeasurePasses, 1, work.Steps * ScrollMeasureLimit);
        Assert.InRange(work.MaxStepMeasurePasses, 1, ScrollMeasureLimit);
    }
    private static void RequireScaleRatio(double ratio) => Assert.InRange(ratio, 0, DoublingRatio);
    private static void RequireAllocation(long bytes) => Assert.InRange(bytes, 0, ScrollAllocationLimit);
}
