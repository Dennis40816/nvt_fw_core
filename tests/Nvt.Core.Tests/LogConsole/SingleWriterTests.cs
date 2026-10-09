// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Single-writer admission, atomic publication, generation and ownership regressions.</summary>
public sealed class SingleWriterTests
{
    /// <summary>Both subscriber and disposal callbacks can enqueue without blocking the writer.</summary>
    [Fact]
    public async Task ChangedAndContentDisposeCanAddWithoutDeadlock()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var store = LogStoreTests.CreateStore(entries: 1);
        var disposed = new CountingContent("first", onDispose: () => store.Add(LogLevel.Info, "app", "dispose callback"));
        var added = false;
        store.Changed += (_, _) =>
        {
            if (added) return;
            added = true;
            store.Add(LogLevel.Info, "app", "changed callback");
        };
        store.SetReady(true);
        store.Add(new LogWrite(LogLevel.Info, "app", disposed));
        await Task.Run(() => LogStoreTests.Flush(store), waitCancellation.Token).WaitAsync(waitCancellation.Token);
        using var snapshot = store.CaptureSnapshot();
        Assert.Equal(3, snapshot.LastSequence);
        Assert.Equal("dispose callback", LogText.ReadAll(Assert.Single(snapshot.Entries).TextContent));
        Assert.Equal(1, disposed.Disposals);
    }

    /// <summary>Eight producers cannot publish removal without its causative addition in the same version.</summary>
    [Fact]
    public async Task EightProducersPublishEvictionsTogetherWithAdditions()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var store = new LogStore(maxEntries: 8, maxCharacters: 100_000, maxPendingCharacters: 100_000);
        var observed = new ConcurrentQueue<long>();
        var retained = new HashSet<long>();
        var previousVersion = 0L;
        Exception? failure = null;
        store.Changed += (_, changes) =>
        {
            try
            {
                Assert.True(changes.Version > previousVersion);
                if (retained.Except(changes.Snapshot.Entries.Select(e => e.EntryId)).Any()) Assert.NotEmpty(changes.AddedEntries);
                if (changes.RequiresReset) retained.Clear();
                foreach (var id in changes.RemovedEntryIds) retained.Remove(id);
                foreach (var entry in changes.AddedEntries) retained.Add(entry.EntryId);
                Assert.Equal(changes.Snapshot.Entries.Select(e => e.EntryId).Order(), retained.Order());
                previousVersion = changes.Version;
                observed.Enqueue(changes.Snapshot.LastSequence);
            }
            catch (Exception exception) { failure = exception; }
        };
        store.SetReady(true);
        using var start = new Barrier(9);
        var accepted = new ConcurrentBag<long>();
        var producers = Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            start.SignalAndWait(waitCancellation.Token);
            for (var i = 0; i < 100; i++)
            {
                var id = store.Add(LogLevel.Info, "app", $"{producer}/{i}");
                if (id != 0) accepted.Add(id);
            }
        }, waitCancellation.Token)).ToArray();
        start.SignalAndWait(waitCancellation.Token);
        await Task.WhenAll(producers).WaitAsync(waitCancellation.Token);
        Assert.True(StoreRegressionSupport.WaitUntil(() => observed.Contains(accepted.Max()), waitCancellation.Token));
        Assert.Equal(800, accepted.Count + store.RejectedCount);
        Assert.Null(failure);
        Assert.NotEmpty(observed);
    }

    /// <summary>Pending overflow rejects new writes without removing queued or retained data.</summary>
    [Fact]
    public void PendingOverflowRejectsNewWritesWithoutEvictionOrDisposal()
    {
        using var store = LogStoreTests.CreateStore(characters: 1000, pending: 8);
        var retainedId = store.Add(LogLevel.Info, "app", "kept");
        LogStoreTests.Flush(store);
        var contents = Enumerable.Range(0, 5).Select(i => new CountingContent($"{i}aaa")).ToArray();
        var ids = contents.Select(content => store.Add(new LogWrite(LogLevel.Info, "app", content))).ToArray();
        using (var unpublished = store.GetChangesSince(0))
            Assert.Equal(retainedId, Assert.Single(unpublished.Snapshot.Entries).EntryId);
        Assert.All(contents, content => Assert.Equal(0, content.Disposals));
        using (var snapshot = LogStoreTests.Capture(store))
        {
            Assert.Equal(new[] { retainedId, ids[0], ids[1] }, snapshot.Entries.Select(e => e.EntryId));
            Assert.Equal(0, snapshot.EvictedCount);
            Assert.Equal(3, store.RejectedCount);
            Assert.All(ids.Skip(2), id => Assert.Equal(0, id));
            Assert.All(contents, content => Assert.Equal(0, content.Disposals));
        }
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.All(contents.Take(2), content => Assert.Equal(1, content.Disposals));
        Assert.All(contents.Skip(2), content => Assert.Equal(0, content.Disposals));
    }

    /// <summary>Clear fences concurrent old batches immediately and publishes only new-generation writes after reset.</summary>
    [Fact]
    public async Task ClearDuringConcurrentAddsRejectsOldGenerationAfterReset()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var store = LogStoreTests.CreateStore(pending: 100_000);
        var oldGeneration = store.Generation;
        using var entered = new CountdownEvent(8);
        using var release = new ManualResetEventSlim();
        var accepted = new CountingContent("enqueued old");
        store.Add(new LogWrite(LogLevel.Info, "app", accepted));
        var rejected = Enumerable.Range(0, 8).Select(_ => new CountingContent("unenqueued old")).ToArray();
        var producers = rejected.Select(content => Task.Run(() => store.AddBatch(oldGeneration, Writes(content)),
            waitCancellation.Token)).ToArray();
        IEnumerable<LogWrite> Writes(CountingContent content)
        {
            entered.Signal();
            release.Wait(waitCancellation.Token);
            yield return new LogWrite(LogLevel.Info, "app", content);
        }
        entered.Wait(waitCancellation.Token);
        store.Clear();
        Assert.False(store.IsCurrent(oldGeneration));
        var newId = store.Add(LogLevel.Info, "app", "new");
        release.Set();
        Assert.All(await Task.WhenAll(producers).WaitAsync(waitCancellation.Token), result => Assert.False(result));
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(newId, Assert.Single(snapshot.Entries).EntryId);
        Assert.All(snapshot.Entries, entry => Assert.Equal(oldGeneration + 1, entry.Generation));
        Assert.Equal(1, accepted.Disposals);
        Assert.All(rejected, content => { Assert.Equal(0, content.Disposals); content.Dispose(); });
    }

    /// <summary>Dispose races admission; every accepted and caller-retained content is released once.</summary>
    [Fact]
    public async Task DisposeDuringConcurrentAddsReleasesEveryContentExactlyOnce()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var store = LogStoreTests.CreateStore(pending: 100_000);
        var retained = new CountingContent("retained");
        store.Add(new LogWrite(LogLevel.Info, "app", retained));
        LogStoreTests.Flush(store);
        var frozen = store.CaptureSnapshot();
        var queued = new CountingContent("queued");
        store.Add(new LogWrite(LogLevel.Info, "app", queued));
        using var entered = new CountdownEvent(8);
        using var release = new ManualResetEventSlim();
        var contents = Enumerable.Range(0, 8).Select(_ => new CountingContent("racing", onMetadata: () =>
        {
            entered.Signal();
            release.Wait(waitCancellation.Token);
        })).ToArray();
        var producers = contents.Select(content => Task.Run(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => store.Add(new LogWrite(LogLevel.Info, "app", content)));
            content.Dispose(); // Admission failed; ownership never transferred.
        }, waitCancellation.Token)).ToArray();
        entered.Wait(waitCancellation.Token);
        store.Dispose();
        release.Set();
        await Task.WhenAll(producers).WaitAsync(waitCancellation.Token);
        LogStoreTests.Flush(store);
        Assert.Equal(1, queued.Disposals);
        Assert.Equal(0, retained.Disposals);
        Assert.Equal("retained", LogText.ReadAll(Assert.Single(frozen.Entries).TextContent));
        frozen.Dispose();
        LogStoreTests.Flush(store); // Final snapshot release schedules cleanup even after store shutdown.
        Assert.Equal(1, retained.Disposals);
        Assert.All(contents, content => Assert.Equal(1, content.Disposals));
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.Equal(1, retained.Disposals);
    }

    /// <summary>IDs are assigned at the enqueue lock, so snapshot order is ascending across many producers.</summary>
    [Fact]
    public void ConcurrentReturnedIdsAppearInAscendingSnapshotOrder()
    {
        using var store = LogStoreTests.CreateStore(pending: 100_000);
        var ids = new ConcurrentBag<long>();
        Parallel.For(0, 800, i => ids.Add(store.Add(LogLevel.Info, "app", i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(ids.Order(), snapshot.Entries.Select(entry => entry.EntryId));
        Assert.Equal(800, ids.Distinct().Count());
    }

    /// <summary>Even inline scheduling and blocked callbacks do not block producers.</summary>
    [Fact]
    public async Task InlineSchedulerAndBlockedCallbackNeverRunOnProducer()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var producerThread = Environment.CurrentManagedThreadId;
        var callbackThread = producerThread;
        using var store = new LogStore(schedule: action => action());
        store.Changed += (_, _) =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            entered.Set();
            release.Wait(waitCancellation.Token);
            completed.Set();
        };
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "first");
        entered.Wait(waitCancellation.Token);
        try
        {
            await Task.Run(() => store.Add(LogLevel.Info, "app", "second"), waitCancellation.Token).WaitAsync(waitCancellation.Token);
            Assert.NotEqual(producerThread, callbackThread);
        }
        finally { release.Set(); }
        completed.Wait(waitCancellation.Token);
    }

    /// <summary>An empty or faulted empty enumerable cannot evict a retained entry or publish a version.</summary>
    [Fact]
    public void EmptyAndFaultedBatchesDoNotEvictOrPublish()
    {
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 128);
        var id = store.Add(LogLevel.Info, "app", new string('x', 128));
        using var before = LogStoreTests.Capture(store);
        IEnumerable<LogWrite> Empty() { yield break; }
        IEnumerable<LogWrite> Faulted() { foreach (var write in Empty()) yield return write; throw new InvalidOperationException("empty fault"); }
        Assert.True(store.AddBatch(store.Generation, Empty()));
        Assert.Throws<InvalidOperationException>(() => store.AddBatch(store.Generation, Faulted()));
        using var after = LogStoreTests.Capture(store);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(id, Assert.Single(after.Entries).EntryId);
        Assert.Equal(0, after.EvictedCount);
    }

    /// <summary>Clock preparation sees the prior publication intact and permits nested logging.</summary>
    [Fact]
    public async Task ReentrantPreparationClockKeepsPriorPublicationAndCanAdd()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        LogStore? current = null;
        var armed = false;
        var oldId = 0L;
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 256, clock: new DelegateTimeProvider(() =>
        {
            if (armed)
            {
                armed = false;
                using var observed = current!.CaptureSnapshot();
                Assert.Equal(oldId, Assert.Single(observed.Entries).EntryId);
                current.Add(LogLevel.Info, "app", "nested");
            }
            return DateTimeOffset.UnixEpoch;
        }));
        current = store;
        oldId = store.Add(LogLevel.Info, "app", new string('x', 128));
        LogStoreTests.Flush(store);
        armed = true;
        var id = await Task.Run(() => store.Add(LogLevel.Info, "app", new string('y', 128)), waitCancellation.Token)
            .WaitAsync(waitCancellation.Token);
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(id, Assert.Single(snapshot.Entries).EntryId);
        Assert.Equal(2, snapshot.EvictedCount); // Old retained entry and nested write both leave the ring.
    }

    /// <summary>Input validation cannot remove already published data.</summary>
    /// <param name="nullSource">Whether the source is null.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidInlineInputDoesNotEvict(bool nullSource)
    {
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 128);
        var id = store.Add(LogLevel.Info, "app", new string('x', 128));
        using var before = LogStoreTests.Capture(store);
        Assert.ThrowsAny<ArgumentException>(() => store.Add(nullSource ? LogLevel.Info : (LogLevel)int.MaxValue,
            nullSource ? null! : "app", new string('y', 128)));
        using var after = LogStoreTests.Capture(store);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(id, Assert.Single(after.Entries).EntryId);
    }

    /// <summary>The reset watermark includes IDs assigned to old queued writes that were discarded.</summary>
    [Fact]
    public void ClearDiscardedEnqueuePreservesAssignedSequenceWatermark()
    {
        using var store = LogStoreTests.CreateStore();
        var id = store.Add(LogLevel.Info, "app", "old queued");
        store.Clear();
        using var reset = LogStoreTests.Capture(store);
        Assert.Empty(reset.Entries);
        Assert.Equal(id, reset.LastSequence);
        var next = store.Add(LogLevel.Info, "app", "new");
        using var changes = LogStoreTests.Changes(store, reset.Version);
        Assert.Equal(next, Assert.Single(changes.AddedEntries).EntryId);
        Assert.True(next > reset.LastSequence);
    }

    /// <summary>A generation change during fingerprinting rejects the batch input before ownership transfers.</summary>
    [Fact]
    public void GenerationChangeDuringFingerprintLeavesInputCallerOwned()
    {
        LogStore? current = null;
        using var store = LogStoreTests.CreateStore(fingerprint: _ => { current!.Clear(); return 1; });
        current = store;
        var input = new CountingContent("input");
        Assert.False(store.AddBatch(store.Generation, [new LogWrite(LogLevel.Info, "app", input)]));
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(0, input.Disposals);
        input.Dispose();
    }

    /// <summary>A failed preparation leaves content caller-owned and does not poison later enqueue.</summary>
    /// <param name="batch">Whether to use a batch.</param>
    /// <param name="cancel">Whether preparation throws cancellation.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PreparationFaultAndCancellationLeaveOwnershipWithCaller(bool batch, bool cancel)
    {
        var fail = true;
        using var store = LogStoreTests.CreateStore(fingerprint: _ =>
        {
            if (!fail) return 1;
            fail = false;
            if (cancel) throw new OperationCanceledException();
            throw new InvalidOperationException("preparation fault");
        });
        var input = new CountingContent("input");
        var write = new LogWrite(LogLevel.Info, "app", input);
        var fault = Record.Exception(() => { if (batch) store.AddBatch(store.Generation, [write]); else store.Add(write); });
        if (cancel) Assert.IsType<OperationCanceledException>(fault);
        else Assert.IsType<InvalidOperationException>(fault);
        Assert.Equal(0, input.Disposals);
        input.Dispose();
        store.Add(LogLevel.Info, "app", "later");
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Single(snapshot.Entries);
    }

    // Interlocked protects lifetime counters. Immutable text may be read concurrently.
    private sealed class CountingContent(string text, Action? onDispose = null, Action? onMetadata = null) : ILogTextContent
    {
        private int _disposals;
        private int _metadataObserved;
        internal int Disposals => Volatile.Read(ref _disposals);
        public int Length { get { if (Interlocked.Exchange(ref _metadataObserved, 1) == 0) onMetadata?.Invoke(); return text.Length; } }
        public int ResidentCharacterCount => text.Length;
        public long Version => 0;
        public void Read(int offset, Span<char> destination) => text.AsSpan(offset, destination.Length).CopyTo(destination);
        public void Dispose() { Interlocked.Increment(ref _disposals); onDispose?.Invoke(); }
    }
}
