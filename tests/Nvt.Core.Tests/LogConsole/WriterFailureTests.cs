// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Writer failure ownership and asynchronous error delivery.</summary>
public sealed class WriterFailureTests
{
    private static readonly string[] RecoveredMessages = ["a", "b", "c"];
    private static readonly string[] CleanupMessages = ["first", "second", "third"];
    /// <summary>Disposal during a notification-only clock failure still releases every owned handle.</summary>
    /// <param name="throwOnDispose">Whether one content disposal throws.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotificationClockDisposalFailureRearmsCleanup(bool throwOnDispose)
    {
        var callbacks = new ConcurrentQueue<Action>();
        var fail = false;
        LogStore? owner = null;
        using var store = new LogStore(clock: new DelegateTimeProvider(() =>
        {
            if (!fail) return DateTimeOffset.UnixEpoch;
            owner!.Dispose();
            throw new InvalidOperationException("clock unavailable");
        }), schedule: callbacks.Enqueue);
        owner = store;
        var disposals = new int[2];
        var first = new TestContent("first", 5, () =>
        {
            disposals[0]++;
            if (throwOnDispose) throw new InvalidOperationException("content disposal failed");
        });
        var second = new TestContent("second", 6, () => disposals[1]++);
        store.Add(new LogWrite(LogLevel.Info, "app", first, DateTimeOffset.UnixEpoch));
        store.Add(new LogWrite(LogLevel.Info, "app", second, DateTimeOffset.UnixEpoch));
        Take(callbacks)();
        using (var published = store.CaptureSnapshot()) Assert.Equal(2, published.EventCount);
        Assert.Equal(0, (int)StoreRegressionSupport.Field(store, "_writerScheduled")!);

        fail = true;
        store.Changed += (_, _) => { };
        store.SetReady(true);
        Take(callbacks)();
        Assert.Equal(0, (int)StoreRegressionSupport.Field(store, "_writerScheduled")!);

        Assert.All(disposals, count => Assert.Equal(1, count));
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Empty((System.Collections.IEnumerable)StoreRegressionSupport.Field(store, "_entries")!);
        Assert.Empty((System.Collections.IEnumerable)StoreRegressionSupport.Field(store, "_cleanup")!);
        Assert.Equal(0, (int)StoreRegressionSupport.Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        store.Dispose();
        Assert.All(disposals, count => Assert.Equal(1, count));
        Assert.Empty(callbacks);
    }

    /// <summary>Notification faults leave publication barriers and pending ownership independent of app time.</summary>
    /// <param name="clear">Whether a reset has discarded the accepted content.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotificationClockFaultDoesNotRequeueWorkOrFaultBarrier(bool clear)
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        var reads = 0;
        LogStore? current = null;
        using var store = new LogStore(maxEntries: 1, maxPendingCharacters: 4,
            clock: new DelegateTimeProvider(() =>
            {
                if (current is not null && (int)StoreRegressionSupport.Field(current, "_writerThreadId")! == Environment.CurrentManagedThreadId)
                {
                    Interlocked.Increment(ref reads);
                    throw new InvalidOperationException("notification clock unavailable");
                }
                return DateTimeOffset.UnixEpoch;
            }), schedule: callbacks.Enqueue);
        current = store;
        store.Changed += (_, _) => { };
        store.SetReady(true);
        var content = new TestContent("seed", 4);
        store.Add(new LogWrite(LogLevel.Info, "app", content, DateTimeOffset.UnixEpoch));
        if (clear) store.Clear();
        var capture = Latest(store, waitCancellation.Token);
        Assert.False(capture.IsCompleted);
        Take(callbacks)();
        Assert.Equal(1, Volatile.Read(ref reads));
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(0, (int)StoreRegressionSupport.Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        using (var recovered = await capture.WaitAsync(waitCancellation.Token))
        {
            if (clear) Assert.Empty(recovered.Entries); else Assert.Single(recovered.Entries);
            Assert.Equal(store.Generation, recovered.Generation);
        }
        Assert.Equal(2, store.Add(LogLevel.Info, "app", "next", DateTimeOffset.UnixEpoch));
        var next = Latest(store, waitCancellation.Token);
        Take(callbacks)();
        using (var snapshot = await next.WaitAsync(waitCancellation.Token)) Assert.Equal(2, Assert.Single(snapshot.Entries).EntryId);
        Assert.Equal(1, content.Disposals);
        store.Dispose();
        Take(callbacks)();
    }
    /// <summary>An Apply failure preserves queued ownership and cleanup order until a successful retry.</summary>
    [Fact]
    public async Task ApplyFailureFaultsWaiterAndRecoversWorkAndCleanupInOrder()
    {
        using var waitCancellation = CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        var cleanupOrder = new List<string>();
        var failure = new InvalidOperationException("apply unavailable");
        var fail = false;
        LogStore? current = null;
        LogSnapshot? secondSnapshot = null;
        long newer = 0;
        (int Handles, long Characters) usageAtFailure = default;
        using var store = new LogStore(4, 6, 6, LogStoreTests.FixedClock(), callbacks.Enqueue, LogText.Fingerprint,
            beforeApply: () =>
            {
                if (!fail) return;
                fail = false;
                Assert.Equal((2, 2L), current!.PendingUsage);
                newer = current.Add(LogLevel.Info, "app", "c");
                secondSnapshot!.Dispose();
                usageAtFailure = current.PendingUsage;
                throw failure;
            });
        current = store;
        var first = new TestContent("first", 6, () => cleanupOrder.Add("first"));
        var second = new TestContent("second", 6, () => cleanupOrder.Add("second"));
        var third = new TestContent("third", 6, () => cleanupOrder.Add("third"));
        store.Add(new LogWrite(LogLevel.Info, "app", first));
        Take(callbacks)();
        using var firstSnapshot = store.CaptureSnapshot();
        store.Add(new LogWrite(LogLevel.Info, "app", second));
        Take(callbacks)();
        using var heldSecond = store.CaptureSnapshot();
        secondSnapshot = heldSecond;
        store.Add(new LogWrite(LogLevel.Info, "app", third));
        Take(callbacks)();
        firstSnapshot.Dispose();

        var earlier = store.Add(LogLevel.Info, "app", "a");
        var later = store.Add(LogLevel.Info, "app", "b");
        var capture = Latest(store, waitCancellation.Token);
        Assert.False(capture.IsCompleted);
        Assert.Equal((2, 2L), store.PendingUsage);
        fail = true;
        Take(callbacks)();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => capture.WaitAsync(waitCancellation.Token)));
        Assert.True(capture.IsFaulted);
        Assert.Equal((3, 3L), usageAtFailure);
        Assert.Equal(usageAtFailure, store.PendingUsage);
        Assert.Equal(3, PendingCount(store));
        Assert.Empty(cleanupOrder);
        Assert.Equal(1, (int)Field(store, "_writerScheduled")!);

        var recovery = Latest(store, waitCancellation.Token);
        Take(callbacks)();
        using (var recovered = await recovery.WaitAsync(waitCancellation.Token))
        {
            Assert.Equal(new[] { earlier, later, newer }, recovered.Entries.Select(entry => entry.EntryId));
            Assert.Equal(RecoveredMessages, recovered.Entries.Select(entry => LogText.ReadAll(entry.TextContent)));
        }
        Assert.Equal(CleanupMessages, cleanupOrder);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, third.Disposals);
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);

        var subsequent = store.Add(LogLevel.Info, "app", "d");
        var next = Latest(store, waitCancellation.Token);
        Take(callbacks)();
        using (var snapshot = await next.WaitAsync(waitCancellation.Token))
            Assert.Equal(subsequent, snapshot.Entries[^1].EntryId);
        Assert.Equal((0, 0L), store.PendingUsage);
        store.Dispose();
        Take(callbacks)();
    }

}
