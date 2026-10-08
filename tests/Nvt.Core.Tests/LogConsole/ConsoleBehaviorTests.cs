// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Projection order, callback isolation, bounded content, and documentation hygiene.</summary>
public sealed class ConsoleBehaviorTests
{
    private static readonly string[] SurvivingPausedOrder = ["B", "C"];
    private static readonly string[] PostPauseOrder = ["A", "B", "C"];
    private static readonly string[] ResumedPostPauseOrder = ["A", "C", "B"];
    private static readonly string[] SurvivingPostPauseOrder = ["B", "C", "D"];

    /// <summary>A duplicate of a group created after pausing cannot move that group's reading position.</summary>
    [Fact]
    public void PostPauseDuplicatePreservesInsertionOrder()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "A");
        using var before = ConsoleProjectionTests.Project(store, true);
        var paused = new ConsoleViewState().Pause(before, before.Rows[0].Id);
        store.Add(LogLevel.Info, "app", "B");
        store.Add(LogLevel.Info, "app", "C");
        using var initial = LogStoreTests.Capture(store);
        using var initialReading = ConsoleProjector.Project(initial, new ConsoleFilter { Deduplicate = true }, paused);
        Assert.Equal(PostPauseOrder, initialReading.Rows.Select(ConsoleProjectionTests.Text));
        store.Add(LogLevel.Info, "app", "B");
        using var latest = LogStoreTests.Capture(store);
        using var reading = ConsoleProjector.Project(latest, new ConsoleFilter { Deduplicate = true }, paused);
        Assert.Equal(PostPauseOrder, reading.Rows.Select(ConsoleProjectionTests.Text));
        Assert.Equal(initialReading.Rows.Select(row => row.Id), reading.Rows.Select(row => row.Id));
        Assert.Equal(2, reading.Rows[1].Count);
        Assert.Equal(3, reading.NewSincePauseCount);
        using var resumed = ConsoleProjector.Project(latest, new ConsoleFilter { Deduplicate = true }, paused.Resume());
        Assert.Equal(ResumedPostPauseOrder, resumed.Rows.Select(ConsoleProjectionTests.Text));
    }

    /// <summary>Evicting the first member of a post-pause group keeps its original insertion key.</summary>
    [Fact]
    public void PostPauseGroupFirstMemberEvictionPreservesInsertionOrder()
    {
        using var store = LogStoreTests.CreateStore(4);
        store.Add(LogLevel.Info, "app", "A");
        using var before = ConsoleProjectionTests.Project(store, true);
        var paused = new ConsoleViewState().Pause(before, before.Rows[0].Id);
        var firstB = store.Add(LogLevel.Info, "app", "B");
        store.Add(LogLevel.Info, "app", "C");
        LogStoreTests.Flush(store); // Establish the group before testing retained-member eviction.
        store.Add(LogLevel.Info, "app", "B");
        store.Add(LogLevel.Info, "app", "D"); // Evicts A.
        var survivingB = store.Add(LogLevel.Info, "app", "B"); // Evicts the first B, but its group survives.
        using var latest = LogStoreTests.Capture(store);
        Assert.DoesNotContain(latest.Entries, entry => entry.EntryId == firstB);
        using var reading = ConsoleProjector.Project(latest, new ConsoleFilter { Deduplicate = true }, paused);
        Assert.Equal(SurvivingPostPauseOrder, reading.Rows.Select(ConsoleProjectionTests.Text));
        Assert.Equal(new ConsoleRowId(firstB, true), reading.Rows[0].Id);
        Assert.Equal(survivingB, reading.Rows[0].LastSequence);
        Assert.Equal(2, reading.Rows[0].Count);
    }

    /// <summary>A reentrant Clear and its retained addition have distinct observable versions.</summary>
    [Fact]
    public void ReentrantClockClearDoesNotHideRetainedAddition()
    {
        LogStore? current = null;
        var clearOnRead = true;
        long clearVersion = -1;
        using var store = LogStoreTests.CreateStore(clock: new DelegateTimeProvider(() =>
        {
            if (clearOnRead && current is not null)
            {
                clearOnRead = false;
                current.Clear();
                using var cleared = LogStoreTests.Capture(current);
                clearVersion = cleared.Version;
            }
            return DateTimeOffset.UnixEpoch;
        }));
        current = store;
        var id = store.Add(LogLevel.Info, "app", "retained");
        using var changes = LogStoreTests.Changes(store, clearVersion);
        Assert.Equal(clearVersion + 1, changes.Version);
        Assert.Equal(id, Assert.Single(changes.AddedEntries).EntryId);
        Assert.Equal(changes.Generation, Assert.Single(changes.Snapshot.Entries).Generation);
        Assert.False(changes.RequiresReset);
    }

    /// <summary>App clocks, enumeration, metadata, reads, and disposal run outside internal locks.</summary>
    [Fact]
    public void AppCallbacksRunOutsideStoreContentAndCacheLocks()
    {
        var gates = new List<object>();
        void Observe() => Assert.All(gates, gate => Assert.False(Monitor.IsEntered(gate)));
        using var store = LogStoreTests.CreateStore(clock: new DelegateTimeProvider(() =>
        {
            Observe();
            return DateTimeOffset.UnixEpoch;
        }));
        gates.Add(Gate(store, "_gate"));
        var content = new ObservedContent(@"C:\Demo\file:2:3", Observe);
        IEnumerable<LogWrite> Writes()
        {
            Observe();
            yield return new LogWrite(LogLevel.Info, "app", content);
        }
        Assert.True(store.AddBatch(store.Generation, Writes()));
        using var snapshot = LogStoreTests.Capture(store);
        var owner = snapshot.Entries[0].TextContent.GetType().GetField("_owner", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(snapshot.Entries[0].TextContent)!;
        gates.Add(Gate(owner, "_contentGate"));
        var cache = new ConsoleLinkCache();
        gates.Add(Gate(cache, "_cacheGate"));
        Assert.Single(cache.GetLinks(snapshot, snapshot.Entries[0]).Spans);
        store.Add(new LogWrite(LogLevel.Info, "app", new ObservedContent(@"C:\Demo\file:2:3", Observe)));
        using var changes = LogStoreTests.Changes(store, snapshot.Version);
        store.SetReady(true);
        store.Clear();
        changes.Dispose();
        snapshot.Dispose();
        LogStoreTests.Flush(store);
        Assert.Equal(1, content.Disposals);
    }

    /// <summary>A generation change during preparation leaves the whole batch caller-owned.</summary>
    [Fact]
    public void ClearDuringBatchPreparationLeavesWholeBatchCallerOwned()
    {
        using var store = LogStoreTests.CreateStore();
        using var first = new ObservedContent("first");
        using var second = new ObservedContent("second");
        IEnumerable<LogWrite> Writes()
        {
            yield return new LogWrite(LogLevel.Info, "app", first);
            store.Clear();
            yield return new LogWrite(LogLevel.Info, "app", second);
        }
        Assert.False(store.AddBatch(store.Generation, Writes()));
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(1, snapshot.Version);
        Assert.Equal(0, first.Disposals);
        Assert.Equal(0, second.Disposals);
        Assert.Equal(1, store.Add(LogLevel.Info, "app", "next"));
    }

    /// <summary>An enumeration fault transfers no prefix and consumes no IDs.</summary>
    [Fact]
    public void EnumerationFaultLeavesPreparedPrefixCallerOwned()
    {
        using var store = LogStoreTests.CreateStore();
        using var content = new ObservedContent("prefix");
        IEnumerable<LogWrite> Writes()
        {
            yield return new LogWrite(LogLevel.Info, "app", content);
            throw new InvalidOperationException("enumeration fault");
        }
        Assert.Throws<InvalidOperationException>(() => store.AddBatch(store.Generation, Writes()));
        using var changes = LogStoreTests.Changes(store, 0);
        Assert.Equal(0, changes.Version);
        Assert.Empty(changes.AddedEntries);
        Assert.Equal(0, content.Disposals);
        Assert.Equal(1, store.Add(LogLevel.Info, "app", "next"));
    }

    /// <summary>A lazy oversized batch stops preparation at the pending limit and transfers nothing.</summary>
    /// <param name="residentLimit">The independent resident character limit.</param>
    /// <param name="pendingLimit">The independent pending character limit.</param>
    /// <param name="charge">The resident charge of each generated content handle.</param>
    [Theory]
    [InlineData(4096, 128, 30)]
    [InlineData(4096, 128, 50)]
    [InlineData(4096, 128, 64)]
    [InlineData(128, 4096, 30)]
    [InlineData(128, 4096, 50)]
    [InlineData(128, 4096, 64)]
    public void LazySegmentedBurstRejectsAtomicallyWithBoundedPreparation(long residentLimit, long pendingLimit, int charge)
    {
        using var store = LogStoreTests.CreateStore(characters: residentLimit, pending: pendingLimit);
        var contents = new List<LogStoreTests.TrackingContent>();
        IEnumerable<LogWrite> Writes()
        {
            for (var i = 0; i < 1000; i++)
            {
                var content = new LogStoreTests.TrackingContent("segmented", charge);
                contents.Add(content);
                Assert.InRange(contents.Count, 1, (int)(pendingLimit / charge) + 1);
                Assert.Equal((0, 0L), store.PendingUsage);
                yield return new LogWrite(LogLevel.Info, "app", content);
            }
        }
        Assert.False(store.AddBatch(store.Generation, Writes()));
        using (var snapshot = LogStoreTests.Capture(store))
        {
            Assert.Empty(snapshot.Entries);
            Assert.Equal(0, snapshot.LastSequence);
            Assert.Equal(0, snapshot.EvictedCount);
            Assert.Equal(1, store.RejectedCount);
            Assert.Equal(pendingLimit / charge + 1, contents.Count);
        }
        store.Clear();
        LogStoreTests.Flush(store);
        Assert.All(contents, c => { Assert.Equal(0, c.Disposals); c.Dispose(); });
    }

    /// <summary>Clear during enumeration rejects every segmented input before ownership transfer.</summary>
    [Fact]
    public void ClearDuringBatchEnumerationRejectsAllContent()
    {
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 128);
        using var first = new LogStoreTests.TrackingContent("first", 64);
        using var rejected = new ObservedContent("second");
        IEnumerable<LogWrite> Writes()
        {
            yield return new LogWrite(LogLevel.Info, "app", first);
            store.Clear();
            yield return new LogWrite(LogLevel.Info, "app", rejected);
        }
        Assert.False(store.AddBatch(store.Generation, Writes()));
        LogStoreTests.Flush(store);
        Assert.Equal(0, first.Disposals);
        Assert.Equal(0, rejected.Disposals);
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(1, snapshot.Generation);
    }

    /// <summary>Clear during writer comparison discards the old-generation step without retrying it.</summary>
    [Fact]
    public void ReentrantContentComparisonClearDiscardsOldGenerationWithoutRetry()
    {
        using var store = LogStoreTests.CreateStore();
        var content = new ObservedContent("same");
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        LogStoreTests.Flush(store);
        content.OnRead = () => { content.OnRead = null; store.Clear(); };
        var candidate = new ObservedContent("same");
        store.Add(new LogWrite(LogLevel.Info, "app", candidate));
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(1, snapshot.Generation);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(1, content.Disposals);
        Assert.Equal(1, candidate.Disposals);
    }

    /// <summary>An inline scheduler still delivers one callback at a time in version order.</summary>
    [Fact]
    public async Task EagerSchedulerDeliversOneCallbackAtATimeInVersionOrder()
    {
        using var completedSecond = new ManualResetEventSlim();
        using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: action => action());
        var active = 0;
        var maximum = 0;
        var completed = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var faults = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        store.Changed += (_, changes) =>
        {
            var current = Interlocked.Increment(ref active);
            int previous;
            do { previous = Volatile.Read(ref maximum); }
            while (current > previous && Interlocked.CompareExchange(ref maximum, current, previous) != previous);
            try
            {
                if (changes.Version == 1)
                {
                    store.Add(LogLevel.Info, "app", "second");
                    using var inside = store.CaptureSnapshot();
                    Assert.Equal(1, inside.Version);
                }
                completed.Enqueue(changes.Version);
            }
            catch (Exception exception) { faults.Enqueue(exception); }
            finally
            {
                Interlocked.Decrement(ref active);
                if (changes.Version == 2) completedSecond.Set();
            }
        };
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "first");
        await Task.Run(() => Assert.True(completedSecond.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)),
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(faults);
        Assert.Equal(1, maximum);
        Assert.Equal(new long[] { 1, 2 }, completed);
    }

    /// <summary>Callback faults and readiness switches cannot stop the writer or overlap delivery.</summary>
    [Fact]
    public void SubscriberFailureAndReadySwitchRearmOneDrain()
    {
        using var store = LogStoreTests.CreateStore();
        var versions = new List<long>();
        store.Changed += (_, changes) =>
        {
            versions.Add(changes.Version);
            if (changes.Version != 1) return;
            store.SetReady(false);
            store.Add(LogLevel.Info, "app", "second");
            store.SetReady(true);
            throw new InvalidOperationException("subscriber fault");
        };
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "first");
        LogStoreTests.Flush(store);
        Assert.Equal(new long[] { 1, 2 }, versions);
    }

    /// <summary>Large content remains segmented through projection, cross-boundary search, and UTF-8 export.</summary>
    [Fact]
    public async Task LargeSegmentedContentStaysBoundedThroughProjectionSearchAndExport()
    {
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 128);
        var content = new GeneratedContent(16 * 1024 * 1024);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        using var snapshot = LogStoreTests.Capture(store);
        var reads = content.Reads;
        using (var unsearched = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState()))
        {
            Assert.Equal(reads, content.Reads);
            Assert.Equal(content.Length, Assert.Single(unsearched.Rows).TextContent.Length);
            Assert.Equal(64, unsearched.Rows[0].TextContent.ResidentCharacterCount);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { SearchText = "NEEDLE" }, new ConsoleViewState());
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - allocated, 0, 256 * 1024);
        Assert.Equal(new ConsoleSearchHit(ConsoleSearchArea.Message, 1021, 6), Assert.Single(projection.Rows[0].SearchHits));
        store.Clear();
        snapshot.Dispose();
        Assert.Equal(0, content.Disposals);
        using var destination = new BoundedOutputStream();
        await ConsoleExportFormatter.WriteLogAsync(destination, projection, new ConsoleExportOptions(false, false),
            TestContext.Current.CancellationToken);
        Assert.Equal((long)content.Length + 8, destination.Length); // Six prefix bytes; surrogate pair adds two bytes.
        var prefix = new char[4096];
        // Build a small expected prefix independently of export's chunk boundaries.
        for (var i = 0; i < prefix.Length; i++) prefix[i] = GeneratedContent.CharacterAt(i);
        var expected = Encoding.UTF8.GetBytes("[app] " + new string(prefix));
        Assert.Equal(expected, destination.Prefix.AsSpan(0, expected.Length).ToArray());
        Assert.InRange(content.MaximumRead, 1, 1024);
        Assert.InRange(destination.MaximumWrite, 1, 4096);
        projection.Dispose();
        LogStoreTests.Flush(store);
        Assert.Equal(1, content.Disposals);
    }

    /// <summary>Link scanning uses bounded reads and scratch space over the same 16 Mi-character content.</summary>
    /// <param name="quotedUrl">Whether a short URL overlaps an otherwise very long quoted candidate.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeSegmentedContentStaysBoundedThroughLinkScanning(bool quotedUrl)
    {
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 128);
        var content = new GeneratedContent(16 * 1024 * 1024, quotedUrl);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        using var snapshot = LogStoreTests.Capture(store);
        var cache = new ConsoleLinkCache();
        var reads = content.Reads;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var index = cache.GetLinks(snapshot, snapshot.Entries[0]);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - allocated, 0, 256 * 1024);
        if (quotedUrl)
        {
            var span = Assert.Single(index.Spans);
            Assert.Equal(1021, span.Start);
            Assert.Equal(new LinkTarget(LinkKind.Url, GeneratedContent.Url), span.Target);
        }
        else Assert.Empty(index.Spans);
        Assert.True(content.Reads > reads);
        Assert.InRange(content.MaximumRead, 1, 1024);
        reads = content.Reads;
        Assert.Same(index, cache.GetLinks(snapshot, snapshot.Entries[0]));
        Assert.Equal(reads, content.Reads);
    }

    /// <summary>Search literals longer than a chunk retain matches and nonoverlapping offsets.</summary>
    [Fact]
    public void SearchMatchesAcrossMultipleChunkBoundaries()
    {
        var query = new string('a', 2300) + "b";
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", new string('x', 1023) + query.ToUpperInvariant() + query);
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { SearchText = query }, new ConsoleViewState());
        Assert.Equal(new[]
        {
            new ConsoleSearchHit(ConsoleSearchArea.Message, 1023, query.Length),
            new ConsoleSearchHit(ConsoleSearchArea.Message, 1023 + query.Length, query.Length),
        }, Assert.Single(projection.Rows).SearchHits);
    }

    /// <summary>Batch dedupe follows a recreated group when an oversized first occurrence evicts itself.</summary>
    [Fact]
    public void BatchDedupeKeepsRecreatedGroupAfterSelfEviction()
    {
        using var store = LogStoreTests.CreateStore(characters: 4, pending: 100);
        store.AddBatch(store.Generation,
        [
            new LogWrite(LogLevel.Info, "app", new LogStoreTests.TrackingContent("same", 5)),
            new LogWrite(LogLevel.Info, "app", new LogStoreTests.TrackingContent("same", 1)),
            new LogWrite(LogLevel.Info, "app", new LogStoreTests.TrackingContent("same", 1)),
        ]);
        using var projection = ConsoleProjectionTests.Project(store, true);
        Assert.Equal(1, projection.EvictedCount);
        var row = Assert.Single(projection.Rows);
        Assert.Equal(2, row.Count);
        Assert.Equal(new ConsoleRowId(2, true), row.Id);
    }

    /// <summary>An evicted anchor uses frozen paused order even when its successor's latest sequence moves.</summary>
    [Fact]
    public void EvictedPausedAnchorUsesFrozenSuccessorAfterDuplicate()
    {
        using var store = LogStoreTests.CreateStore(3);
        store.Add(LogLevel.Info, "app", "A");
        store.Add(LogLevel.Info, "app", "B");
        store.Add(LogLevel.Info, "app", "C");
        using var before = ConsoleProjectionTests.Project(store, true);
        var paused = new ConsoleViewState().Pause(before, before.Rows[0].Id);
        store.Add(LogLevel.Info, "app", "B");
        using var latest = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(latest, new ConsoleFilter { Deduplicate = true }, paused);
        Assert.Equal(SurvivingPausedOrder, projection.Rows.Select(ConsoleProjectionTests.Text));
        Assert.Equal(before.Rows[1].Id, projection.ResolvedAnchorId);
        Assert.Equal(2, projection.Rows[0].Count);
    }

    /// <summary>External code has no usable base constructor, including a record copy constructor.</summary>
    [Fact]
    public void FollowBaseHasOnlyPrivateConstructors()
    {
        var constructors = typeof(ConsoleFollow).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor => Assert.True(constructor.IsPrivate));
        Assert.True(typeof(ConsoleFollow.Following).IsSealed);
        Assert.True(typeof(ConsoleFollow.Paused).IsSealed);
        Assert.False(typeof(ConsoleFollow.Paused).GetProperty(nameof(ConsoleFollow.Paused.Anchor))!.CanWrite);
        Assert.False(typeof(ConsoleFollow.Paused).GetProperty(nameof(ConsoleFollow.Paused.PausedAt))!.CanWrite);
    }

    /// <summary>Public module docs describe the baseline without repository refs or commit identifiers.</summary>
    /// <param name="name">The module document filename.</param>
    [Theory]
    [InlineData("LogConsole.md")]
    [InlineData("LogConsole.zh-TW.md")]
    public void ModuleDocsContainNoRepositoryCommitIdentifiers(string name)
    {
        var text = ReadModuleDocument(name, new DirectoryInfo(AppContext.BaseDirectory));
        Assert.DoesNotMatch(CommitIdentifierPattern, text);
        Assert.DoesNotContain("origin/", text, StringComparison.OrdinalIgnoreCase);
    }

    internal static readonly Regex CommitIdentifierPattern = new(
        @"\b(?:[0-9a-f]{40}|(?=[0-9a-f]{7,39}\b)(?=[0-9a-f]*[0-9])(?=[0-9a-f]*[a-f])[0-9a-f]{7,39})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string ReadModuleDocument(string name, DirectoryInfo? root)
    {
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "docs", "core", "modules", name))) root = root.Parent;
        Assert.SkipWhen(root is null, "Module docs folder was not found; documentation hygiene requires a source checkout.");
        return File.ReadAllText(Path.Combine(root!.FullName, "docs", "core", "modules", name));
    }

    /// <summary>Hex identifiers require commit-like length and abbreviated identifiers need a digit.</summary>
    /// <param name="text">The candidate token.</param>
    /// <param name="expected">Whether the token is a commit identifier.</param>
    [Theory]
    [InlineData("defaced", false)]
    [InlineData("acceded", false)]
    [InlineData("abcdef", false)]
    [InlineData("1234567", false)]
    [InlineData("a1b2c3d", true)]
    [InlineData("A1B2C3D", true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("worda1b2c3dword", false)]
    public void CommitIdentifierRegexDistinguishesOrdinaryWords(string text, bool expected)
        => Assert.Equal(expected, CommitIdentifierPattern.IsMatch(text));

    /// <summary>Copied test outputs clearly skip source-dependent documentation checks.</summary>
    [Fact]
    public void MissingModuleDocsProducesExplicitSkip()
    {
        try { ReadModuleDocument("LogConsole.md", null); }
        catch (Xunit.Sdk.SkipException exception)
        {
            Assert.Contains("Module docs folder was not found", exception.Message, StringComparison.Ordinal);
            return;
        }
        Assert.Fail("Missing source documentation must produce an explicit skip.");
    }

    /// <summary>Public docs describe behavior without internal process history or local test setup.</summary>
    /// <param name="name">The module document filename.</param>
    [Theory]
    [InlineData("LogConsole.md")]
    [InlineData("LogConsole.zh-TW.md")]
    public void ModuleDocsContainNoInternalProcessNarration(string name)
    {
        var text = ReadModuleDocument(name, new DirectoryInfo(AppContext.BaseDirectory));
        Assert.DoesNotMatch(@"(?i)sandbox|\bTEMP\b|\bTMP\b", text);
        Assert.DoesNotContain("| Passed | Skipped | Failed |", text, StringComparison.Ordinal);
        Assert.DoesNotContain("| 通過 | 跳過 | 失敗 |", text, StringComparison.Ordinal);
        Assert.Contains("CaptureLatestAsync", text, StringComparison.Ordinal);
    }

    /// <summary>Collapse does not cause export to use the earlier display projection.</summary>
    [Fact]
    public async Task CollapsedConsoleExportsLatestSnapshotAfterLaterAdd()
    {
        using var store = new LogStore(clock: LogStoreTests.FixedClock());
        var first = store.Add(LogLevel.Info, "app", "before");
        using var initial = await store.CaptureLatestAsync(TestContext.Current.CancellationToken);
        using var displayed = ConsoleProjector.Project(initial, new ConsoleFilter(), new ConsoleViewState());
        var collapsed = new ConsoleViewState { Selection = [first] }.Pause(displayed, displayed.Rows[0].Id) with { IsExpanded = false };
        store.Add(LogLevel.Info, "app", "later\r\nsecond line");
        using var latest = await store.CaptureLatestAsync(TestContext.Current.CancellationToken);
        using var exported = ConsoleProjector.Project(latest, new ConsoleFilter(), collapsed);
        Assert.True(exported.Version > displayed.Version);
        Assert.Equal(latest.Version, exported.Version);
        Assert.Equal(2, exported.EventCount);
        Assert.Equal(1, exported.NewSincePauseCount);
        Assert.False(collapsed.IsExpanded);
        Assert.IsType<ConsoleFollow.Paused>(collapsed.Follow);
        var options = new ConsoleExportOptions(false, false);
        const string expected = "[app] before\n[app] later\r\nsecond line";
        Assert.Equal(expected, ConsoleExportFormatter.FormatVisible(exported, options));
        Assert.Equal("[app] before", ConsoleExportFormatter.FormatSelection(exported, collapsed.Selection, options));
        using var destination = new MemoryStream();
        await ConsoleExportFormatter.WriteLogAsync(destination, exported, options, TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), destination.ToArray());
    }

    /// <summary>Absolute and hidden screen time modes have exact output and independent export defaults.</summary>
    /// <param name="mode">The screen time mode.</param>
    /// <param name="expected">The screen time text.</param>
    [Theory]
    [InlineData(ConsoleTimeMode.Absolute, "04:05:06.789")]
    [InlineData(ConsoleTimeMode.Hidden, "")]
    public void AbsoluteAndHiddenTimeModesHaveExactText(ConsoleTimeMode mode, string expected)
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "message", new DateTimeOffset(2026, 1, 2, 4, 5, 6, 789, TimeSpan.Zero));
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { TimeMode = mode }, new ConsoleViewState());
        Assert.Equal(expected, Assert.Single(projection.Rows).TimeText);
        Assert.Equal("04:05:06.789 [Info] [app] message", ConsoleExportFormatter.FormatVisible(projection));
    }

    private static object Gate(object owner, string name)
        => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    // All helper counters and observers below belong to the single deterministic test thread.
    private sealed class ObservedContent(string text, Action? observe = null) : ILogTextContent
    {
        internal int Disposals { get; private set; }
        internal Action? OnRead { get; set; }
        public int Length { get { observe?.Invoke(); return text.Length; } }
        public int ResidentCharacterCount { get { observe?.Invoke(); return text.Length; } }
        public long Version { get { observe?.Invoke(); return 0; } }
        public void Read(int offset, Span<char> destination)
        {
            observe?.Invoke();
            Assert.Equal(0, Disposals);
            OnRead?.Invoke();
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose() { observe?.Invoke(); Disposals++; }
    }

    private sealed class GeneratedContent(int length, bool quotedUrl = false) : ILogTextContent
    {
        internal const string Url = "https://example.test/export_(v2)";
        internal int Disposals { get; private set; }
        internal int Reads { get; private set; }
        internal int MaximumRead { get; private set; }
        public int Length => length;
        public int ResidentCharacterCount => 64;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            ObjectDisposedException.ThrowIf(Disposals != 0, this);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(destination.Length, 1024);
            Reads++;
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            for (var i = 0; i < destination.Length; i++)
            {
                var position = offset + i;
                destination[i] = quotedUrl ? position switch
                {
                    0 => '"',
                    1020 => ' ',
                    _ when position >= 1021 && position < 1021 + Url.Length => Url[position - 1021],
                    _ when position == 1021 + Url.Length => ' ',
                    _ when position == length - 1 => '"',
                    _ => 'x',
                } : CharacterAt(position);
            }
        }
        internal static char CharacterAt(int offset) => offset switch
        {
            >= 1021 and < 1027 => "needle"[offset - 1021],
            2047 => '\ud83d', 2048 => '\ude00',
            4094 => '\r', 4095 => '\n',
            _ => 'x',
        };
        public void Dispose() => Disposals++;
    }

    private sealed class BoundedOutputStream : Stream
    {
        private long _written;
        internal byte[] Prefix { get; } = new byte[8192];
        internal int MaximumWrite { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            MaximumWrite = Math.Max(MaximumWrite, buffer.Length);
            if (_written < Prefix.Length)
                buffer[..Math.Min(buffer.Length, Prefix.Length - (int)_written)].CopyTo(Prefix.AsSpan((int)_written));
            _written += buffer.Length;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
