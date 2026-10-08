// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Deterministic storage, lifetime, and scheduling contracts.</summary>
public sealed class LogStoreTests
{
    /// <summary>A startup snapshot plus deltas gives each stable ID exactly once.</summary>
    [Fact]
    public void StartupSnapshotAndLaterAddsDoNotDuplicate()
    {
        using var store = CreateStore();
        var first = store.Add(LogLevel.Info, "app", "first");
        using var startup = LogStoreTests.Capture(store);
        var second = store.Add(LogLevel.Warn, "app", "second");
        var third = store.Add(LogLevel.Debug, "other", "third");
        using var changes = LogStoreTests.Changes(store, startup.Version);
        Assert.False(changes.RequiresReset);
        Assert.Equal(new[] { first, second, third }, startup.Entries.Concat(changes.AddedEntries).Select(e => e.EntryId));
        Assert.Equal(startup.Generation, changes.Generation);
        Assert.All(changes.AddedEntries, e => Assert.True(e.Sequence > startup.LastSequence));
        using var unchanged = LogStoreTests.Changes(store, changes.Version);
        Assert.Empty(unchanged.AddedEntries);
    }

    /// <summary>Clear invalidates pending callbacks and stale producer batches in one generation.</summary>
    [Fact]
    public void ClearDuringPendingAddsDropsOldGeneration()
    {
        using var store = CreateStore();
        var delivered = new List<(long Generation, long[] Ids, bool Reset)>();
        store.Changed += (_, changes) => delivered.Add((changes.Generation, changes.AddedEntries.Select(e => e.EntryId).ToArray(), changes.RequiresReset));
        store.SetReady(true);
        var oldGeneration = store.Generation;
        var oldId = store.Add(LogLevel.Info, "app", "old");
        store.Clear();
        var enumerated = false;
        IEnumerable<LogWrite> Stale() { enumerated = true; yield return Write("stale"); }
        Assert.False(store.AddBatch(oldGeneration, Stale()));
        Assert.False(enumerated);
        Assert.False(store.IsCurrent(oldGeneration, oldId, 0));
        var newId = store.Add(LogLevel.Info, "app", "new");
        Flush(store);
        var batch = Assert.Single(delivered);
        Assert.Equal(oldGeneration + 1, batch.Generation);
        Assert.Equal(new[] { newId }, batch.Ids);
        Assert.True(batch.Reset);
        Assert.True(newId > oldId);
    }

    /// <summary>Each ring retention budget independently evicts raw events.</summary>
    /// <param name="entries">The entry limit.</param>
    /// <param name="characters">The retained character limit.</param>
    /// <param name="pending">The pending character limit.</param>
    [Theory]
    [InlineData(2, 100, 100)]
    [InlineData(100, 8, 100)]
    public void EachRingBudgetEvictsOnItsOwn(int entries, long characters, long pending)
    {
        using var store = CreateStore(entries, characters, pending);
        for (var i = 0; i < 3; i++) { Assert.True(store.Add(Write("aaaa")) > 0); Flush(store); }
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(2, snapshot.EventCount);
        Assert.Equal(1, snapshot.EvictedCount);
        Assert.Equal(2, snapshot.FirstRetainedSequence);
        Assert.Equal(3, snapshot.LastRetainedSequence);
        store.Clear();
        using var empty = LogStoreTests.Capture(store);
        Assert.Equal(0, empty.EvictedCount);
        Assert.Equal(0, empty.EventCount);
    }

    /// <summary>Writing releases pending budget independently of Changed readiness.</summary>
    [Fact]
    public void WriterReleasesPendingBudgetWhileNotReady()
    {
        using var store = CreateStore(100, 100, 4);
        store.Add(LogLevel.Info, "app", "aaaa");
        Flush(store); // Writing releases pending capacity even when Changed is not ready.
        store.Add(LogLevel.Info, "app", "bbbb");
        using var snapshot = Capture(store);
        Assert.Equal(2, snapshot.EventCount);
        Assert.Equal(0, snapshot.EvictedCount);
    }

    /// <summary>Oversized pending messages are caller-owned. Segmented handles charge only resident characters.</summary>
    [Fact]
    public void OversizedAndSegmentedContentHaveBoundedOwnership()
    {
        using var store = CreateStore(10, 4, 4);
        var inline = new TrackingContent("12345", 5);
        Assert.Equal(0, store.Add(new LogWrite(LogLevel.Info, "app", inline)));
        Flush(store);
        Assert.Equal(0, inline.Disposals);
        inline.Dispose();
        var segmented = new TrackingContent(new string('x', 10_000), 1);
        store.Add(new LogWrite(LogLevel.Info, "app", segmented));
        using (var snapshot = LogStoreTests.Capture(store))
        {
            Assert.Equal(10_000, Assert.Single(snapshot.Entries).TextContent.Length);
            using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState());
            Assert.Equal(10_000, Assert.Single(projection.Rows).TextContent.Length);
        }
        store.Clear();
        Flush(store);
        Assert.Equal(1, segmented.Disposals);
    }

    /// <summary>Eviction releases the store reference while a frozen snapshot remains readable.</summary>
    [Fact]
    public void FrozenSnapshotRetainsContentUntilDisposed()
    {
        using var store = CreateStore(1);
        var content = new TrackingContent("full\r\nmessage", 13);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        var frozen = LogStoreTests.Capture(store);
        store.Add(LogLevel.Info, "app", "later");
        Flush(store);
        Assert.Equal(0, content.Disposals);
        using var projection = ConsoleProjector.Project(frozen, new ConsoleFilter(), new ConsoleViewState());
        frozen.Dispose();
        frozen.Dispose();
        Assert.Equal(0, content.Disposals);
        Assert.Contains("full\r\nmessage", ConsoleExportFormatter.FormatVisible(projection), StringComparison.Ordinal);
        projection.Dispose();
        projection.Dispose();
        Flush(store);
        Assert.Equal(1, content.Disposals);
    }

    /// <summary>Lost delta history forces a reset from the complete latest snapshot.</summary>
    [Fact]
    public void HistoryLossAndClearRequireReset()
    {
        using var store = CreateStore(2);
        for (var i = 0; i < 5; i++) { store.Add(LogLevel.Info, "app", i.ToString(System.Globalization.CultureInfo.InvariantCulture)); Flush(store); }
        using var lost = LogStoreTests.Changes(store, 0);
        Assert.True(lost.RequiresReset);
        Assert.Equal(2, lost.AddedEntries.Length);
        store.Clear();
        using var clear = LogStoreTests.Changes(store, lost.Version);
        Assert.True(clear.RequiresReset);
        Assert.Empty(clear.AddedEntries);
    }

    /// <summary>Hash collisions compare the entire content before merging.</summary>
    [Fact]
    public void FingerprintCollisionDoesNotMergeDifferentText()
    {
        using var store = CreateStore(100, 1000, 1000, fingerprint: _ => 1);
        store.Add(LogLevel.Info, "app", "alpha");
        store.Add(LogLevel.Info, "app", "bravo");
        store.Add(LogLevel.Info, "app", "alpha");
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter { Deduplicate = true }, new ConsoleViewState());
        var rows = projection.Rows;
        Assert.Equal(2, rows.Length);
        Assert.Equal(2, rows.Single(r => ConsoleProjectionTests.Text(r) == "alpha").Count);
    }

    /// <summary>All reachable enqueue, ready-switch, post, and drain interleavings deliver both events.</summary>
    [Fact]
    public void EveryDeterministicSchedulingInterleavingDrains()
    {
        var completed = 0;
        Explore([], 0, 0);
        Assert.True(completed > 0);
        void Explore(List<char> trace, int adds, int switches)
        {
            var queue = new ConcurrentQueue<Action>();
            var dispatcher = new Queue<Action>();
            var ids = new List<long>();
            using var store = CreateStore(schedule: queue.Enqueue);
            store.Changed += (_, changes) => { foreach (var entry in changes.AddedEntries) ids.Add(entry.EntryId); };
            var switchIndex = 0;
            foreach (var step in trace)
            {
                switch (step)
                {
                    case 'E': store.Add(LogLevel.Info, "app", "event"); break;
                    case 'R': store.SetReady(switchIndex++ != 1); break;
                    case 'P': dispatcher.Enqueue(Take(queue)); break;
                    case 'D': dispatcher.Dequeue()(); break;
                }
            }
            var field = typeof(LogStore).GetField("_writerScheduled", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var scheduled = (int)field.GetValue(store)! != 0;
            if (adds == 2 && switches == 3 && !scheduled)
            {
                Assert.Equal(new long[] { 1, 2 }, ids.Order());
                completed++;
                return;
            }
            if (adds < 2) Branch('E', adds + 1, switches);
            if (switches < 3) Branch('R', adds, switches + 1);
            if (scheduled && dispatcher.Count == 0) Branch('P', adds, switches);
            if (dispatcher.Count != 0) Branch('D', adds, switches);
            void Branch(char step, int nextAdds, int nextSwitches)
            {
                trace.Add(step); Explore(trace, nextAdds, nextSwitches); trace.RemoveAt(trace.Count - 1);
            }
        }
    }

    /// <summary>Reentrant adds schedule another drain, and queued work after disposal is inert.</summary>
    [Fact]
    public void ReentrantAddAndDisposeDoNotLoseOrReviveWork()
    {
        var delivered = new List<long>();
        using var store = CreateStore();
        store.SetReady(true);
        store.Changed += (_, changes) =>
        {
            delivered.AddRange(changes.AddedEntries.Select(e => e.EntryId));
            if (delivered.Count == 1) store.Add(LogLevel.Info, "app", "reentrant");
        };
        store.Add(LogLevel.Info, "app", "first");
        Flush(store);
        Assert.Equal(new long[] { 1, 2 }, delivered);
        store.Add(LogLevel.Info, "app", "discard");
        store.Dispose();
        Flush(store);
        Assert.Equal(2, delivered.Count);
        Assert.False(store.IsCurrent(store.Generation));
    }

    /// <summary>Parallel producers and Clear leave a single-generation sequence-ordered snapshot.</summary>
    [Fact]
    public void ConcurrentAddsAndClearLinearize()
    {
        using var store = CreateStore(1000, 100_000, 100_000);
        Parallel.For(0, 100, i =>
        {
            if (i % 10 == 0) store.Clear();
            store.Add(LogLevel.Info, "app", "event");
        });
        using var snapshot = LogStoreTests.Capture(store);
        Assert.All(snapshot.Entries, e => Assert.Equal(snapshot.Generation, e.Generation));
        Assert.Equal(snapshot.EventCount, snapshot.Entries.Select(e => e.EntryId).Distinct().Count());
        Assert.Equal(snapshot.Entries.OrderBy(e => e.Sequence), snapshot.Entries);
    }

    /// <summary>A scheduler failure falls back on the writer dispatch thread without losing accepted entries.</summary>
    [Fact]
    public void SchedulingFailureFallsBackWithoutLosingAcceptedEntry()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var done = new ManualResetEventSlim();
        using var store = CreateStore(schedule: _ => throw new InvalidOperationException("Scheduler unavailable."));
        store.Changed += (_, _) => done.Set();
        store.SetReady(true);
        Assert.True(store.Add(LogLevel.Info, "app", "accepted") > 0);
        done.Wait(waitCancellation.Token);
        using var snapshot = store.CaptureSnapshot();
        Assert.Equal(1, snapshot.EventCount);
    }

    internal static TimeProvider FixedClock() => new DelegateTimeProvider(() => DateTimeOffset.UnixEpoch.AddSeconds(10));
    internal static LogWrite Write(string message) => new(LogLevel.Info, "app", new InMemoryLogTextContent(message));
    private static readonly ConditionalWeakTable<LogStore, WriterScheduler> Writers = new();

    internal static LogStore CreateStore(int entries = 10_000, long characters = 4 * 1024 * 1024,
        long pending = 256 * 1024, Action<Action>? schedule = null, TimeProvider? clock = null,
        Func<ILogTextContent, ulong>? fingerprint = null)
    {
        var writer = new WriterScheduler(schedule);
        var store = new LogStore(entries, characters, pending, clock ?? FixedClock(), writer.Schedule, fingerprint ?? LogText.Fingerprint);
        Writers.Add(store, writer);
        return store;
    }

    internal static void Flush(LogStore store) => Writers.GetValue(store, _ => throw new InvalidOperationException()).Flush(store);
    internal static LogSnapshot Capture(LogStore store) { Flush(store); return store.CaptureSnapshot(); }
    internal static LogChangeSet Changes(LogStore store, long version) { Flush(store); return store.GetChangesSince(version); }

    private static Action Take(ConcurrentQueue<Action> queue)
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        Action? action = null;
        Assert.True(StoreRegressionSupport.WaitUntil(() => queue.TryDequeue(out action), waitCancellation.Token));
        return action!;
    }

    // The scheduler queues are thread-safe. Tests explicitly execute the writer, without sleeps.
    internal sealed class WriterScheduler(Action<Action>? schedule = null)
    {
        private readonly ConcurrentQueue<Action> _callbacks = new();
        internal void Schedule(Action action)
        {
            if (schedule is null) _callbacks.Enqueue(action);
            else schedule(action);
        }
        internal void Flush(LogStore store)
        {
            var field = typeof(LogStore).GetField("_writerScheduled", BindingFlags.Instance | BindingFlags.NonPublic)!;
            while ((int)field.GetValue(store)! != 0)
            {
                Take(_callbacks)();
            }
        }
    }

    // Mutable counters are accessed only by this test's thread, except content access serialized by ContentOwner.
    internal sealed class TrackingContent(string text, int resident, long version = 0) : ILogTextContent
    {
        internal int Disposals { get; private set; }
        internal int Reads { get; private set; }
        public int Length => text.Length;
        public int ResidentCharacterCount => resident;
        public long Version => version;
        public void Read(int offset, Span<char> destination)
        {
            Assert.Equal(0, Disposals);
            Reads++;
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose() => Disposals++;
    }
}

