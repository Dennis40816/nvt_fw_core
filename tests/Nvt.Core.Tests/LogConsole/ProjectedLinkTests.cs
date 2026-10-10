// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Projected rows reuse the segmented scanner, cache invalidation, and shared retention budgets.</summary>
public sealed class ProjectedLinkTests
{
    /// <summary>A normal row scans in bounded chunks and shares cache identity with its raw entry.</summary>
    [Fact]
    public void NormalRowUsesSegmentedScannerAndSharesEntryCache()
    {
        var text = new string('x', 1022) + " " + @"C:\Demo\file.cs:7:2";
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 128);
        var content = new ReadContent(text, 42);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState());
        var row = Assert.Single(projection.Rows);
        var cache = new ConsoleLinkCache();
        var index = cache.GetLinks(snapshot, row);
        Assert.Equal(ConsoleLinkScanner.Scan(text), index.Spans);
        Assert.Equal(new LinkTarget(LinkKind.File, @"C:\Demo\file.cs", 7, 2), Assert.Single(index.Spans).Target);
        var reads = content.Reads;
        Assert.Same(index, cache.GetLinks(snapshot, row));
        Assert.Same(index, cache.GetLinks(snapshot, snapshot.Entries[0]));
        Assert.Equal(reads, content.Reads);
        Assert.InRange(content.MaximumRead, 1, 1024);
    }

    /// <summary>Groups keep readable scan content and cache identity after the original member is evicted.</summary>
    [Fact]
    public void DedupeRowLinksSurviveOriginalMemberEviction()
    {
        using var store = LogStoreTests.CreateStore(3);
        const string text = "https://example.test/export_(v2)";
        var originalId = store.Add(LogLevel.Info, "app", text);
        store.Add(LogLevel.Info, "app", "other");
        var representative = new ReadContent(text, 9);
        var survivorId = store.Add(new LogWrite(LogLevel.Info, "app", representative));
        var cache = new ConsoleLinkCache();
        ConsoleRowId groupId;
        ConsoleLinkIndex index;
        using (var first = LogStoreTests.Capture(store))
        using (var before = ConsoleProjector.Project(first, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState()))
        {
            var row = before.Rows.Single(row => row.Count == 2);
            groupId = row.Id;
            index = cache.GetLinks(first, row);
        }
        store.Add(LogLevel.Info, "app", "new row");
        using var snapshot = LogStoreTests.Capture(store);
        Assert.DoesNotContain(snapshot.Entries, entry => entry.EntryId == originalId);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        var survivor = projection.Rows.Single(row => row.Id == groupId);
        Assert.Equal(new[] { survivorId }, survivor.MemberSequences);
        var reads = representative.Reads;
        Assert.Same(index, cache.GetLinks(snapshot, survivor));
        Assert.Equal(reads, representative.Reads);
        // A fresh cache must also work: this proves the survivor, not the cached index, supplies scan content.
        var rescanned = new ConsoleLinkCache().GetLinks(snapshot, survivor);
        Assert.Equal(index.Spans, rescanned.Spans);
        Assert.True(representative.Reads > reads);
    }

    /// <summary>Search and presentation changes do not invalidate row links.</summary>
    [Fact]
    public void RowCacheHitIgnoresSearchAndSourceNames()
    {
        using var store = LogStoreTests.CreateStore();
        var content = new ReadContent("https://example.test", 4);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        using var snapshot = LogStoreTests.Capture(store);
        using var initial = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState());
        var cache = new ConsoleLinkCache();
        var index = cache.GetLinks(snapshot, initial.Rows[0]);
        using var searched = ConsoleProjector.Project(snapshot, new ConsoleFilter { SearchText = "example" }, new ConsoleViewState(),
            new ConsoleProjectionOptions { SourceRegistry = [new("app", "Renamed application")] });
        var reads = content.Reads;
        Assert.Same(index, cache.GetLinks(snapshot, searched.Rows[0]));
        Assert.Equal(reads, content.Reads);
    }

    /// <summary>Rows and raw entries compete for the same entry, span, and target-character budgets.</summary>
    /// <param name="budget">The independent cache budget that forces eviction.</param>
    [Theory]
    [InlineData("entries")]
    [InlineData("spans")]
    [InlineData("characters")]
    public void ProjectedRowsShareCacheEvictionBudgetsWithRawEntries(string budget)
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "https://example.test/1");
        store.Add(LogLevel.Info, "app", "https://example.test/2");
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        var cache = budget switch
        {
            "entries" => new ConsoleLinkCache(maxEntries: 1),
            "spans" => new ConsoleLinkCache(maxSpans: 1),
            _ => new ConsoleLinkCache(maxTargetCharacters: "https://example.test/1".Length),
        };
        var first = cache.GetLinks(snapshot, projection.Rows[0]);
        var second = cache.GetLinks(snapshot, snapshot.Entries[1]);
        Assert.Same(second, cache.GetLinks(snapshot, snapshot.Entries[1]));
        Assert.NotSame(first, cache.GetLinks(snapshot, projection.Rows[0]));
    }

    /// <summary>Results that exceed cache budgets stay complete and are never retained.</summary>
    [Fact]
    public void OversizedProjectedLinksAreReturnedWithoutCaching()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "https://example.test/1 https://example.test/2");
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState());
        var cache = new ConsoleLinkCache(maxSpans: 1);
        var first = cache.GetLinks(snapshot, projection.Rows[0]);
        Assert.Equal(2, first.Spans.Length);
        Assert.NotSame(first, cache.GetLinks(snapshot, projection.Rows[0]));
    }

    /// <summary>Clear invalidates row links; stale rows cannot refill the live cache.</summary>
    [Fact]
    public void ProjectedCacheRejectsStaleSnapshots()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "https://example.test");
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        var cache = new ConsoleLinkCache();
        var first = cache.GetLinks(snapshot, projection.Rows[0]);
        Assert.Same(first, cache.GetLinks(snapshot, projection.Rows[0]));
        store.Clear();
        using var cleared = LogStoreTests.Capture(store);
        cache.Synchronize(cleared);
        var stale = cache.GetLinks(snapshot, projection.Rows[0]);
        Assert.NotSame(first, stale);
        Assert.NotSame(stale, cache.GetLinks(snapshot, projection.Rows[0]));
    }

    /// <summary>A new representative's text version invalidates group links even when dedupe text is equal.</summary>
    [Fact]
    public void GroupCacheTracksRepresentativeTextVersion()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(new LogWrite(LogLevel.Info, "app", new ReadContent("https://example.test", 1)));
        using var before = LogStoreTests.Capture(store);
        using var first = ConsoleProjector.Project(before, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        var cache = new ConsoleLinkCache();
        var index = cache.GetLinks(before, first.Rows[0]);
        store.Add(new LogWrite(LogLevel.Info, "app", new ReadContent("https://example.test", 2)));
        using var after = LogStoreTests.Capture(store);
        using var second = ConsoleProjector.Project(after, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        Assert.Equal(first.Rows[0].Id, second.Rows[0].Id);
        Assert.Equal(2, second.Rows[0].TextVersion);
        Assert.NotSame(index, cache.GetLinks(after, second.Rows[0]));
    }

    /// <summary>A new representative's supplied spans win even if its text version equals the previous one.</summary>
    [Fact]
    public void GroupCacheHonorsChangedStructuredSpansAtSameTextVersion()
    {
        using var store = LogStoreTests.CreateStore();
        const string text = "https://example.test";
        store.Add(LogLevel.Info, "app", text);
        using var before = LogStoreTests.Capture(store);
        using var first = ConsoleProjector.Project(before, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        var cache = new ConsoleLinkCache();
        Assert.Single(cache.GetLinks(before, first.Rows[0]).Spans);
        store.Add(new LogWrite(LogLevel.Info, "app", new InMemoryLogTextContent(text), LinkSpans: []));
        using var after = LogStoreTests.Capture(store);
        using var second = ConsoleProjector.Project(after, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        Assert.Equal(first.Rows[0].Id, second.Rows[0].Id);
        Assert.Equal(first.Rows[0].TextVersion, second.Rows[0].TextVersion);
        Assert.Empty(cache.GetLinks(after, second.Rows[0]).Spans);
        // The old row still returns its own links, but cannot populate the current representative's slot.
        Assert.Single(cache.GetLinks(after, first.Rows[0]).Spans);
        Assert.Empty(cache.GetLinks(after, second.Rows[0]).Spans);
    }

    // Reads can run concurrently on writer and test threads. Interlocked owns diagnostics.
    private sealed class ReadContent(string text, long version) : ILogTextContent
    {
        private int _reads;
        private int _maximumRead;
        internal int Reads => Volatile.Read(ref _reads);
        internal int MaximumRead => Volatile.Read(ref _maximumRead);
        public int Length => text.Length;
        public int ResidentCharacterCount => 64;
        public long Version => version;
        public void Read(int offset, Span<char> destination)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(destination.Length, 1024);
            Interlocked.Increment(ref _reads);
            var previous = Volatile.Read(ref _maximumRead);
            while (previous < destination.Length)
            {
                var observed = Interlocked.CompareExchange(ref _maximumRead, destination.Length, previous);
                if (observed == previous) break;
                previous = observed;
            }
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose() { }
    }
}
