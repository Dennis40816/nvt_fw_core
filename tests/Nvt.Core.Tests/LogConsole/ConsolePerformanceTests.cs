// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Opt-in projection measurement. It has no timing acceptance bound.</summary>
public sealed class ConsolePerformanceTests(ITestOutputHelper output)
{
    /// <summary>Gets whether the opt-in environment variable is set.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("NVT_CORE_PERF") == "1";

    /// <summary>Measures a full 10,000-event dedupe and search projection after one warm-up.</summary>
    [Fact(SkipUnless = nameof(Enabled), Skip = "Set NVT_CORE_PERF=1 to measure projection.")]
    public void ProjectTenThousandEntries()
    {
        using var store = LogStoreTests.CreateStore(pending: 4 * 1024 * 1024);
        store.AddBatch(store.Generation, Enumerable.Range(0, 10_000)
            .Select(i => LogStoreTests.Write($"export event {i % 1000}: completed")));
        using var snapshot = LogStoreTests.Capture(store);
        var filter = new ConsoleFilter { Deduplicate = true, SearchText = "export" };
        var view = new ConsoleViewState();
        using (ConsoleProjector.Project(snapshot, filter, view)) { }
        var stopwatch = Stopwatch.StartNew();
        using var projection = ConsoleProjector.Project(snapshot, filter, view);
        stopwatch.Stop();
        output.WriteLine($"10,000 events, dedupe + search: {stopwatch.Elapsed.TotalMilliseconds:F3} ms; {projection.RowCount} rows.");
        Assert.Equal(10_000, projection.EventCount);
        Assert.Equal(1_000, projection.RowCount);
    }
}
