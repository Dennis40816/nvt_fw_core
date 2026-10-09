// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Text;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Pure projection, paused reading, selection, and frozen export contracts.</summary>
public sealed class ConsoleProjectionTests
{
    private static readonly string[] OrderBAC = ["B", "A", "C"];
    private static readonly string[] OrderABC = ["A", "B", "C"];
    /// <summary>Dedupe ignores time, merges interleaved events, and preserves full newline differences.</summary>
    [Fact]
    public void DedupeUsesSourceLevelAndFullMessage()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "same", DateTimeOffset.UnixEpoch.AddSeconds(10));
        store.Add(LogLevel.Warn, "app", "other");
        store.Add(LogLevel.Info, "app", "same", DateTimeOffset.UnixEpoch.AddSeconds(1));
        store.Add(LogLevel.Info, "other", "same");
        store.Add(LogLevel.Error, "app", "same");
        store.Add(LogLevel.Info, "app", "a\r\nb");
        store.Add(LogLevel.Info, "app", "a\nb");
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        Assert.Equal(6, projection.RowCount);
        Assert.Equal(7, projection.EventCount);
        Assert.Equal(1, projection.DuplicatesMerged);
        var group = projection.Rows.Single(r => r.SourceId == "app" && r.Level == LogLevel.Info && Text(r) == "same");
        Assert.Equal(new long[] { 1, 3 }, group.MemberSequences);
        Assert.Equal(2, group.Count);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(10), group.FirstTimestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1), group.Timestamp);
        Assert.Equal(new long[] { 2, 3, 4, 5, 6, 7 }, projection.Rows.Select(r => r.LastSequence));
    }

    /// <summary>Eviction decrements membership, updates occurrence times, and preserves a surviving group ID.</summary>
    [Fact]
    public void EvictionRecomputesDedupeMembershipAndTimes()
    {
        using var store = LogStoreTests.CreateStore(3);
        store.Add(LogLevel.Info, "app", "A", DateTimeOffset.UnixEpoch);
        store.Add(LogLevel.Info, "app", "B", DateTimeOffset.UnixEpoch.AddSeconds(1));
        store.Add(LogLevel.Info, "app", "A", DateTimeOffset.UnixEpoch.AddSeconds(2));
        using var before = Project(store, true);
        var original = before.Rows.Single(r => Text(r) == "A");
        store.Add(LogLevel.Info, "app", "C", DateTimeOffset.UnixEpoch.AddSeconds(3));
        using var after = Project(store, true);
        var survivor = after.Rows.Single(r => Text(r) == "A");
        Assert.Equal(original.Id, survivor.Id);
        Assert.Equal(1, survivor.Count);
        Assert.Equal(new long[] { 3 }, survivor.MemberSequences);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), survivor.FirstTimestamp);
        Assert.Equal(survivor.FirstTimestamp, survivor.Timestamp);
        Assert.Equal(OrderBAC, after.Rows.Select(r => Text(r)));
        store.Add(LogLevel.Info, "app", "D");
        store.Add(LogLevel.Info, "app", "A");
        using var later = Project(store, true);
        Assert.Equal(original.Id, later.Rows.Single(r => Text(r) == "A").Id);
        store.Clear();
        store.Add(LogLevel.Info, "app", "A");
        using var cleared = Project(store, true);
        Assert.NotEqual(original.Id, cleared.Rows[0].Id);
    }

    /// <summary>Level and source counts use opposite filters and ignore search and dedupe.</summary>
    [Fact]
    public void CountsAreRawAndUseOnlyTheOppositeFilter()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "same");
        store.Add(LogLevel.Info, "app", "same");
        store.Add(LogLevel.Error, "app", "error");
        store.Add(LogLevel.Info, "other", "info");
        store.Add(LogLevel.Warn, "other", "warn");
        using var snapshot = LogStoreTests.Capture(store);
        var filter = new ConsoleFilter
        {
            EnabledLevels = [LogLevel.Info], SelectedSources = ["app"],
            SearchText = "absent", Deduplicate = true,
        };
        using var projection = ConsoleProjector.Project(snapshot, filter, new ConsoleViewState());
        Assert.True(projection.IsEmpty);
        Assert.Equal(5, projection.EventCount);
        Assert.Equal(2, projection.LevelCounts[LogLevel.Info]);
        Assert.Equal(1, projection.LevelCounts[LogLevel.Error]);
        Assert.Equal(0, projection.LevelCounts[LogLevel.Warn]);
        Assert.Equal(6, projection.LevelCounts.Count);
        Assert.Equal(2, projection.SourceCounts["app"]);
        Assert.Equal(1, projection.SourceCounts["other"]);
    }

    /// <summary>Search covers complete multiline content and sources with exact UTF-16 hit offsets.</summary>
    [Fact]
    public void SearchRangesAndOnlyMatchesUseFullText()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "needle-src", "Header\nNEEDLE needle");
        store.Add(LogLevel.Info, "other", "unmatched");
        using var snapshot = LogStoreTests.Capture(store);
        var filter = new ConsoleFilter { SearchText = "needle" };
        using var matching = ConsoleProjector.Project(snapshot, filter, new ConsoleViewState());
        var row = Assert.Single(matching.Rows);
        Assert.Equal(new[]
        {
            new ConsoleSearchHit(ConsoleSearchArea.Message, 7, 6),
            new ConsoleSearchHit(ConsoleSearchArea.Message, 14, 6),
            new ConsoleSearchHit(ConsoleSearchArea.Source, 0, 6),
        }, row.SearchHits);
        using var all = ConsoleProjector.Project(snapshot, filter with { OnlyMatches = false }, new ConsoleViewState());
        Assert.Equal(2, all.RowCount);
        Assert.Empty(all.Rows[1].SearchHits);
        using var empty = ConsoleProjector.Project(snapshot, filter with { SearchText = "" }, new ConsoleViewState());
        Assert.Equal(2, empty.RowCount);
        Assert.All(empty.Rows, r => Assert.Empty(r.SearchHits));
    }

    /// <summary>Pause freezes reading order and relative time while duplicate increments count as new events.</summary>
    [Fact]
    public void PausePreservesOrderAndCountsRawNewEvents()
    {
        var now = DateTimeOffset.UnixEpoch.AddSeconds(10);
        using var store = LogStoreTests.CreateStore(clock: new DelegateTimeProvider(() => now));
        store.Add(LogLevel.Info, "svc", "A", DateTimeOffset.UnixEpoch);
        store.Add(LogLevel.Info, "svc", "B", DateTimeOffset.UnixEpoch.AddSeconds(1));
        var filter = new ConsoleFilter { Deduplicate = true, TimeMode = ConsoleTimeMode.Relative };
        using var initial = LogStoreTests.Capture(store);
        using var before = ConsoleProjector.Project(initial, filter, new ConsoleViewState());
        var paused = new ConsoleViewState().Pause(before, before.Rows[0].Id, 2, 4);
        now = now.AddSeconds(10);
        store.Add(LogLevel.Info, "svc", "A", DateTimeOffset.UnixEpoch.AddSeconds(3));
        store.Add(LogLevel.Info, "svc", "C", DateTimeOffset.UnixEpoch.AddSeconds(4));
        using var latest = LogStoreTests.Capture(store);
        using var reading = ConsoleProjector.Project(latest, filter, paused with { IsExpanded = false });
        Assert.Equal(OrderABC, reading.Rows.Select(r => Text(r)));
        Assert.Equal(2, reading.NewSincePauseCount);
        Assert.Equal("7.0 s ago", reading.Rows[0].TimeText);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(10), reading.TimeBase);
        using var resumed = ConsoleProjector.Project(latest, filter, paused.Resume());
        Assert.Equal(OrderBAC, resumed.Rows.Select(r => Text(r)));
        Assert.Equal(0, resumed.NewSincePauseCount);
        using var searched = ConsoleProjector.Project(latest, filter with { SearchText = "A" }, paused);
        Assert.Equal(1, searched.NewSincePauseCount);
        store.Clear();
        store.Add(LogLevel.Info, "svc", "reset");
        using var reset = LogStoreTests.Capture(store);
        using var resetProjection = ConsoleProjector.Project(reset, filter, paused);
        Assert.Equal(0, resetProjection.NewSincePauseCount);
    }

    /// <summary>An evicted reading anchor resolves to its nearest retained successor.</summary>
    [Fact]
    public void EvictedAnchorResolvesToSuccessor()
    {
        using var store = LogStoreTests.CreateStore(2);
        store.Add(LogLevel.Info, "app", "A");
        store.Add(LogLevel.Info, "app", "B");
        using var before = Project(store);
        var paused = new ConsoleViewState().Pause(before, before.Rows[0].Id);
        store.Add(LogLevel.Info, "app", "C");
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), paused);
        Assert.Equal(before.Rows[1].Id, projection.ResolvedAnchorId);
    }

    /// <summary>Selection maps through grouping, survives filters, and loses evicted members.</summary>
    [Fact]
    public void SelectionRemapsAcrossDedupeAndEviction()
    {
        using var store = LogStoreTests.CreateStore(3);
        store.Add(LogLevel.Info, "app", "A");
        store.Add(LogLevel.Info, "app", "B");
        store.Add(LogLevel.Info, "app", "A");
        using var raw = Project(store);
        using var grouped = Project(store, true);
        var state = new ConsoleViewState { Selection = [1], ExpandedIds = [raw.Rows[0].Id] }.Remap(raw, grouped);
        var groupId = grouped.Rows.Single(r => Text(r) == "A").Id;
        Assert.Equal(ImmutableHashSet.Create(1L), state.Selection);
        Assert.Equal(ImmutableHashSet.Create(groupId), state.ExpandedIds);
        Assert.Equal(ImmutableHashSet.Create(1L), state.Remap(grouped, raw).Selection);
        Assert.Single(grouped.Rows.Where(row => row.MemberSequences.Any(state.Selection.Contains)));
        using var snapshot = LogStoreTests.Capture(store);
        using var hidden = ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = true, EnabledLevels = [] }, state);
        Assert.Single(state.Remap(grouped, hidden).Selection);
        Assert.Empty(ConsoleExportFormatter.FormatSelection(hidden, state.Selection));
        store.Add(LogLevel.Info, "app", "C");
        using var evicted = Project(store, true);
        Assert.Empty(state.Remap(grouped, evicted).Selection);
        store.Clear();
        using var cleared = Project(store, true);
        Assert.Empty(state.Remap(grouped, cleared).Selection);
    }

    /// <summary>Both options independently control exact selected and visible output.</summary>
    /// <param name="time">Include time.</param>
    /// <param name="level">Include level.</param>
    /// <param name="expected">The frozen selected text.</param>
    [Theory]
    [InlineData(true, true, "00:00:03.000 [Info] [app] full\r\n第二行 ×2")]
    [InlineData(true, false, "00:00:03.000 [app] full\r\n第二行 ×2")]
    [InlineData(false, true, "[Info] [app] full\r\n第二行 ×2")]
    [InlineData(false, false, "[app] full\r\n第二行 ×2")]
    public async Task ExportKeepsSourceFullMessageLineBreaksAndCount(bool time, bool level, string expected)
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "full\r\n第二行", DateTimeOffset.UnixEpoch);
        store.Add(LogLevel.Warn, "other", "second", DateTimeOffset.UnixEpoch.AddSeconds(2));
        store.Add(LogLevel.Info, "app", "full\r\n第二行", DateTimeOffset.UnixEpoch.AddSeconds(3));
        using var projection = Project(store, true);
        var options = new ConsoleExportOptions(time, level);
        var selected = ImmutableHashSet.Create(projection.Rows[1].MemberSequences[0]);
        Assert.Equal(expected, ConsoleExportFormatter.FormatSelection(projection, selected, options));
        var first = (time ? "00:00:02.000 " : "") + (level ? "[Warn] " : "") + "[other] second";
        Assert.Equal(first + "\n" + expected, ConsoleExportFormatter.FormatVisible(projection, options));
        store.Add(LogLevel.Info, "app", "newer");
        using var destination = new MemoryStream();
        await ConsoleExportFormatter.WriteLogAsync(destination, projection, options, waitCancellation.Token);
        Assert.Equal(Encoding.UTF8.GetBytes(first + "\n" + expected), destination.ToArray());
        Assert.True(destination.CanWrite);
        Assert.True(new ConsoleExportOptions().IncludeTime);
        Assert.True(new ConsoleExportOptions().IncludeLevel);
    }

    /// <summary>Dedupe changes remap the paused anchor and frozen order through the same raw event.</summary>
    [Fact]
    public void DedupeSwitchKeepsPausedReadingAnchorOnFirstA()
    {
        using var store = LogStoreTests.CreateStore();
        var firstA = store.Add(LogLevel.Info, "app", "A");
        store.Add(LogLevel.Info, "app", "B");
        store.Add(LogLevel.Info, "app", "A");
        using var raw = Project(store);
        var state = new ConsoleViewState().Pause(raw, new ConsoleRowId(firstA), 7, 2.5);
        var pause = Assert.IsType<ConsoleFollow.Paused>(state.Follow);
        using var grouped = Project(store, true);
        var remapped = state.Remap(raw, grouped);
        var mappedPause = Assert.IsType<ConsoleFollow.Paused>(remapped.Follow);
        Assert.Equal(new ConsoleRowId(firstA, true), mappedPause.Anchor.RowId);
        Assert.Equal(pause.PausedAt, mappedPause.PausedAt);
        Assert.Equal(pause.Anchor.ThroughSequence, mappedPause.Anchor.ThroughSequence);
        Assert.Equal(pause.Anchor.TextOffset, mappedPause.Anchor.TextOffset);
        Assert.Equal(pause.Anchor.PixelOffset, mappedPause.Anchor.PixelOffset);
        using var snapshot = LogStoreTests.Capture(store);
        using var reading = ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = true }, remapped);
        Assert.Equal(new ConsoleRowId(firstA, true), reading.ResolvedAnchorId);
        Assert.Equal("A", Text(reading.Rows[0]));
        var restored = remapped.Remap(grouped, raw);
        using var rawReading = ConsoleProjector.Project(snapshot, new ConsoleFilter(), restored);
        Assert.Equal(new ConsoleRowId(firstA), rawReading.ResolvedAnchorId);
    }

    /// <summary>Toggling dedupe twice keeps the raw anchor on a later duplicate as well.</summary>
    [Fact]
    public void DedupeRoundTripKeepsPausedAnchorOnLaterDuplicate()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "A");
        store.Add(LogLevel.Info, "app", "B");
        var laterA = store.Add(LogLevel.Info, "app", "A");
        using var raw = Project(store);
        using var grouped = Project(store, true);
        var state = new ConsoleViewState().Pause(raw, new ConsoleRowId(laterA));
        var restored = state.Remap(raw, grouped).Remap(grouped, raw);
        using var snapshot = LogStoreTests.Capture(store);
        using var reading = ConsoleProjector.Project(snapshot, new ConsoleFilter(), restored);
        Assert.Equal(new ConsoleRowId(laterA), reading.ResolvedAnchorId);
    }

    internal static ConsoleProjection Project(LogStore store, bool dedupe = false)
    {
        using var snapshot = LogStoreTests.Capture(store);
        return ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = dedupe }, new ConsoleViewState());
    }

    internal static string Text(ConsoleRow row) => LogText.ReadAll(row.TextContent);
}


