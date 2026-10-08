// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Atomic rejection, derived unpublished ownership and writer handoff boundaries.</summary>
public sealed class Fix7RegressionTests
{
    /// <summary>Capacity failures are silent, caller-owned, ID-free and separate from ring eviction.</summary>
    [Fact]
    public void RejectionDoesNotPublishNotifyThrowDisposeOrConsumeIds()
    {
        using var store = LogStoreTests.CreateStore(entries: 2, pending: 4);
        var notifications = 0;
        store.Changed += (_, _) => notifications++;
        store.SetReady(true);
        var accepted = new Fix6RegressionTests.Content("seed", 4);
        var first = store.Add(new LogWrite(LogLevel.Info, "app", accepted));
        var rejected = new Fix6RegressionTests.Content("next", 4);
        Assert.Null(Record.Exception(() => Assert.Equal(0, store.Add(new LogWrite(LogLevel.Info, "app", rejected)))));
        Assert.Null(Record.Exception(() => Assert.False(store.AddBatch(store.Generation,
            [new LogWrite(LogLevel.Info, "app", rejected)]))));
        Assert.Equal(2, store.RejectedCount);
        Assert.Equal(0, notifications);
        using (var before = store.GetChangesSince(0)) Assert.Equal(0, before.Version);
        LogStoreTests.Flush(store);
        Assert.Equal(1, notifications);
        using var published = store.CaptureSnapshot();
        Assert.Equal(0, published.EvictedCount);
        Assert.Null(Record.Exception(() => Assert.Equal(0, store.Add(LogLevel.Info, "app", "oversized"))));
        LogStoreTests.Flush(store);
        using (var unchanged = store.CaptureSnapshot()) Assert.Equal(published.Version, unchanged.Version);
        Assert.Equal(1, notifications);
        Assert.Equal(first + 1, store.Add(LogLevel.Info, "app", "next"));
        LogStoreTests.Flush(store);
        Assert.Equal(first + 2, store.Add(LogLevel.Info, "app", "last"));
        using (var evicted = LogStoreTests.Capture(store)) Assert.Equal(1, evicted.EvictedCount);
        Assert.Equal(3, store.RejectedCount);
        published.Dispose();
        store.Clear();
        LogStoreTests.Flush(store);
        Assert.Equal(3, store.RejectedCount);
        Assert.Equal(1, accepted.Disposals);
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.Equal(0, rejected.Disposals);
        rejected.Dispose();
    }

    /// <summary>A zero-charge unending batch is inspected only to the first excess handle.</summary>
    [Fact]
    public void UnendingZeroChargeBatchHasBoundedPreparationAndNoOwnershipTransfer()
    {
        using var store = LogStoreTests.CreateStore(entries: 3, pending: 4);
        var generated = new List<Fix6RegressionTests.Content>();
        IEnumerable<LogWrite> Writes()
        {
            while (true)
            {
                Assert.InRange(generated.Count, 0, 3);
                var content = new Fix6RegressionTests.Content("zero", 0);
                generated.Add(content);
                yield return new LogWrite(LogLevel.Info, "app", content);
            }
        }
        Assert.False(store.AddBatch(store.Generation, Writes()));
        Assert.Equal(4, generated.Count);
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(1, store.RejectedCount);
        Assert.Equal(1, store.Add(LogLevel.Info, "app", "next"));
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.All(generated, content => { Assert.Equal(0, content.Disposals); content.Dispose(); });
    }

    /// <summary>Generation rejection is one rejected call, including stale batches never enumerated.</summary>
    [Fact]
    public void StaleBatchRejectionIsCountedOnceAndDoesNotEnumerate()
    {
        using var store = LogStoreTests.CreateStore();
        var generation = store.Generation;
        store.Clear();
        var enumerated = false;
        IEnumerable<LogWrite> Stale() { enumerated = true; yield return LogStoreTests.Write("stale"); }
        Assert.False(store.AddBatch(generation, Stale()));
        Assert.False(enumerated);
        Assert.Equal(1, store.RejectedCount);
        LogStoreTests.Flush(store);
        Assert.Equal(1, store.Add(LogLevel.Info, "app", "next"));
    }

    /// <summary>Eight producers encounter both admissions and rejections without gaps or reordered IDs.</summary>
    [Fact]
    public async Task ConcurrentRejectionsPreserveQueueOrderAndBatchContiguity()
    {
        using var store = LogStoreTests.CreateStore(entries: 8, pending: 32);
        var accepted = new ConcurrentBag<(long Id, string Text)>();
        using var start = new Barrier(9);
        var tasks = Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            start.SignalAndWait(TestContext.Current.CancellationToken);
            for (var i = 0; i < 100; i++)
            {
                var text = $"{producer}/{i}";
                var id = store.Add(LogLevel.Info, "app", text);
                if (id != 0) accepted.Add((id, text));
                var usage = store.PendingUsage;
                Assert.InRange(usage.Handles, 0, 8);
                Assert.InRange(usage.Characters, 0, 32);
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        start.SignalAndWait(TestContext.Current.CancellationToken);
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(8, accepted.Count);
        Assert.Equal(792, store.RejectedCount);
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), snapshot.Entries.Select(e => e.EntryId));
        Assert.Equal(accepted.OrderBy(item => item.Id).Select(item => item.Text), snapshot.Entries.Select(e => LogText.ReadAll(e.TextContent)));
        Assert.True(store.AddBatch(store.Generation, [LogStoreTests.Write("aaaa"), LogStoreTests.Write("bbbb")]));
        Assert.Equal(11, store.Add(LogLevel.Info, "app", "next"));
        using var after = LogStoreTests.Capture(store);
        Assert.Equal(new long[] { 9, 10, 11 }, after.Entries.TakeLast(3).Select(e => e.EntryId));
        Assert.Equal(3, after.EvictedCount);
    }

    /// <summary>Clear leaves pending charge in place even while the writer is inside actual disposal.</summary>
    /// <param name="zeroCharge">Whether to enforce the handle bound independently of characters.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardedOwnershipRemainsChargedUntilDisposeReturns(bool zeroCharge)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var store = LogStoreTests.CreateStore(entries: 1, pending: 4);
        var content = new Fix6RegressionTests.Content("seed", zeroCharge ? 0 : 4, () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        });
        Assert.Equal(1, store.Add(new LogWrite(LogLevel.Info, "app", content)));
        store.Clear();
        Assert.Equal((1, zeroCharge ? 0L : 4L), store.PendingUsage);
        var writer = Task.Run(() => LogStoreTests.Flush(store), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            for (var i = 0; i < 100; i++)
            {
                Assert.Equal(0, store.Add(LogLevel.Info, "app", "next"));
                store.Clear();
                store.Clear();
                Assert.Equal((1, zeroCharge ? 0L : 4L), store.PendingUsage);
            }
            Assert.Equal(0, content.Disposals);
        }
        finally { release.Set(); await writer.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(1, content.Disposals);
        Assert.Equal(2, store.Add(LogLevel.Info, "app", "next"));
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(0, snapshot.EvictedCount);
        Assert.Equal(100, store.RejectedCount);
    }

    /// <summary>Dequeuing for comparison does not free unpublished ownership before publication.</summary>
    [Fact]
    public async Task WriterComparisonKeepsActiveContentInsidePendingBound()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var store = LogStoreTests.CreateStore(entries: 2, pending: 4);
        var seed = new BlockingContent("same");
        store.Add(new LogWrite(LogLevel.Info, "app", seed));
        LogStoreTests.Flush(store);
        seed.OnRead = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); };
        var candidate = new Fix6RegressionTests.Content("same", 4);
        Assert.Equal(2, store.Add(new LogWrite(LogLevel.Info, "app", candidate)));
        var writer = Task.Run(() => LogStoreTests.Flush(store), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal((1, 4L), store.PendingUsage);
            Assert.Equal(0, store.Add(LogLevel.Info, "app", "next"));
            store.Clear();
            Assert.Equal((1, 4L), store.PendingUsage);
        }
        finally { release.Set(); await writer.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(1, candidate.Disposals);
        using var reset = store.CaptureSnapshot();
        Assert.Empty(reset.Entries);
    }

    /// <summary>Final cleanup of published leased content does not compete with unpublished admission.</summary>
    [Fact]
    public void PublishedLeaseCleanupIsExcludedFromPendingCapacity()
    {
        using var store = LogStoreTests.CreateStore(entries: 1, pending: 4);
        var first = new Fix6RegressionTests.Content("seed", 4);
        store.Add(new LogWrite(LogLevel.Info, "app", first));
        var frozen = LogStoreTests.Capture(store);
        Assert.Equal(2, store.Add(LogLevel.Info, "app", "next"));
        LogStoreTests.Flush(store);
        Assert.Equal(0, first.Disposals);
        Assert.Equal((0, 0L), store.PendingUsage);
        frozen.Dispose(); // Published content now awaits writer-only cleanup.
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(3, store.Add(LogLevel.Info, "app", "last"));
        Assert.Equal((1, 4L), store.PendingUsage);
        LogStoreTests.Flush(store);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(0, store.RejectedCount);
    }

    /// <summary>Random adds, batches, clears and drains derive charge from actual unpublished lifetimes.</summary>
    [Fact]
    public void RandomAdmissionClearAndDrainMatchesObservableOwnedContent()
    {
        using var store = LogStoreTests.CreateStore(entries: 4, characters: 8, pending: 8);
        var attempted = new List<(Fix6RegressionTests.Content Content, bool Accepted)>();
        var published = new HashSet<Fix6RegressionTests.Content>();
        var random = new Random(7);
        for (var step = 0; step < 500; step++)
        {
            switch (random.Next(4))
            {
                case 0: store.Clear(); break;
                case 1:
                    LogStoreTests.Flush(store);
                    foreach (var item in attempted.Where(item => item.Accepted && item.Content.Disposals == 0)) published.Add(item.Content);
                    break;
                default:
                    var content = new Fix6RegressionTests.Content($"message {step}", random.Next(5));
                    var accepted = step % 2 == 0 ? store.Add(new LogWrite(LogLevel.Info, "app", content)) != 0
                        : store.AddBatch(store.Generation, [new LogWrite(LogLevel.Info, "app", content)]);
                    attempted.Add((content, accepted));
                    break;
            }
            var owned = attempted.Where(item => item.Accepted && item.Content.Disposals == 0 && !published.Contains(item.Content)).ToArray();
            Assert.Equal((owned.Length, owned.Sum(item => (long)item.Content.ResidentCharacterCount)), store.PendingUsage);
            Assert.InRange(store.PendingUsage.Handles, 0, 4);
            Assert.InRange(store.PendingUsage.Characters, 0, 8);
        }
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.All(attempted, item => Assert.Equal(item.Accepted ? 1 : 0, item.Content.Disposals));
    }

    /// <summary>Comparison faults preserve accepted entries and do not masquerade as ring evictions.</summary>
    [Fact]
    public void ComparisonFaultKeepsAcceptedWriteWithoutIncrementingEvictions()
    {
        using var store = LogStoreTests.CreateStore();
        var seed = new BlockingContent("same");
        store.Add(new LogWrite(LogLevel.Info, "app", seed));
        LogStoreTests.Flush(store);
        seed.OnRead = () => throw new InvalidOperationException("comparison fault");
        store.Add(LogLevel.Info, "app", "same");
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(2, snapshot.EventCount);
        Assert.Equal(0, snapshot.EvictedCount);
        Assert.NotEqual(snapshot.Entries[0].GroupId, snapshot.Entries[1].GroupId);
    }

    // The tests set OnRead before starting the writer and use events to control its callback.
    private sealed class BlockingContent(string text) : ILogTextContent
    {
        internal Action? OnRead { get; set; }
        public int Length => text.Length;
        public int ResidentCharacterCount => text.Length;
        public long Version => 0;
        public void Read(int offset, Span<char> destination) { OnRead?.Invoke(); text.AsSpan(offset, destination.Length).CopyTo(destination); }
        public void Dispose() { }
    }
}
