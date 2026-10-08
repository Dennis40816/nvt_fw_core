// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Notification time cannot gate publication, cleanup, or producer capacity.</summary>
public sealed class NotificationClockTests
{
    /// <summary>A wake inside a failed clock call survives the turn and covers a later admission.</summary>
    [Fact]
    public void ThrowOnceClockPreservesWakeDuringTurnAndNotifiesLaterAdd()
    {
        var callbacks = new ConcurrentQueue<Action>();
        var clockCalls = 0;
        var turns = 0;
        long during = 0;
        LogStore? current = null;
        using var store = new LogStore(clock: new DelegateTimeProvider(() =>
        {
            if (++clockCalls == 1)
            {
                during = current!.Add(LogLevel.Info, "app", "during", DateTimeOffset.UnixEpoch);
                throw new InvalidOperationException("clock unavailable");
            }
            return DateTimeOffset.UnixEpoch;
        }), schedule: action => callbacks.Enqueue(() => { turns++; action(); }));
        current = store;
        var versions = new List<long>();
        store.Changed += (_, changes) => versions.Add(changes.Version);
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "first", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.True(store.IsCurrent(store.Generation, during, 0));
        Assert.Empty(versions);
        Assert.Equal(1, (int)Field(store, "_writerScheduled")!);
        var later = store.Add(LogLevel.Info, "app", "later", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.Equal(new long[] { 3 }, versions);
        Assert.True(store.IsCurrent(store.Generation, later, 0));
        Assert.Equal(2, clockCalls);
        Assert.Equal(2, turns);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        store.Dispose();
        Take(callbacks)();
    }

    /// <summary>Admissions after the clock fault, while the writer is still applying work, also request a retry.</summary>
    [Fact]
    public void WakeAfterClockFaultDuringSameTurnRetriesLatestPublication()
    {
        var callbacks = new ConcurrentQueue<Action>();
        var clockCalls = 0;
        LogStore? current = null;
        using var store = new LogStore(clock: new DelegateTimeProvider(() =>
        {
            if (++clockCalls == 1)
            {
                current!.Add(LogLevel.Info, "app", "same", DateTimeOffset.UnixEpoch);
                throw new InvalidOperationException("clock unavailable");
            }
            return DateTimeOffset.UnixEpoch;
        }), schedule: callbacks.Enqueue);
        current = store;
        var seed = new TestContent("same", 4);
        long afterFault = 0;
        seed.OnRead = () =>
        {
            if (clockCalls == 0) return;
            seed.OnRead = null;
            afterFault = store.Add(LogLevel.Info, "app", "after fault", DateTimeOffset.UnixEpoch);
        };
        var versions = new List<long>();
        store.Changed += (_, changes) => versions.Add(changes.Version);
        store.SetReady(true);
        store.Add(new LogWrite(LogLevel.Info, "app", seed, DateTimeOffset.UnixEpoch));
        Take(callbacks)();
        Assert.True(afterFault > 0);
        Assert.True(store.IsCurrent(store.Generation, afterFault, 0));
        Assert.Empty(versions);
        Assert.Equal(1, (int)Field(store, "_writerScheduled")!);
        Take(callbacks)();
        Assert.Equal(new long[] { 3 }, versions);
        Assert.Equal(2, clockCalls);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        store.Dispose();
        Take(callbacks)();
    }

    /// <summary>A wake for pending data cannot be acknowledged by an attempt on an earlier publication.</summary>
    [Fact]
    public void WakeBeforeClockFaultWithPendingWriteRetriesLatestPublication()
    {
        var callbacks = new ConcurrentQueue<Action>();
        var clockCalls = 0;
        using var store = new LogStore(clock: new DelegateTimeProvider(() =>
        {
            if (++clockCalls == 1) throw new InvalidOperationException("clock unavailable");
            return DateTimeOffset.UnixEpoch;
        }), schedule: callbacks.Enqueue);
        var seed = new TestContent("same", 4);
        store.Add(new LogWrite(LogLevel.Info, "app", seed, DateTimeOffset.UnixEpoch));
        Take(callbacks)();
        long pending = 0;
        seed.OnRead = () =>
        {
            seed.OnRead = null;
            pending = store.Add(LogLevel.Info, "app", "pending", DateTimeOffset.UnixEpoch);
        };
        var versions = new List<long>();
        store.Changed += (_, changes) => versions.Add(changes.Version);
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "same", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.True(pending > 0);
        Assert.True(store.IsCurrent(store.Generation, pending, 0));
        Assert.Empty(versions);
        Assert.Equal(1, (int)Field(store, "_writerScheduled")!);
        Take(callbacks)();
        Assert.Equal(new long[] { 3 }, versions);
        Assert.Equal(2, clockCalls);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        store.Dispose();
        Take(callbacks)();
    }

    /// <summary>An explicitly overlapped Add or readiness wake survives failure, with no unrequested retries.</summary>
    /// <param name="add">Whether the overlapping request admits another entry instead of repeating readiness.</param>
    /// <param name="permanentFault">Whether the requested retry also encounters a clock fault.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ClockFaultOverlappingExplicitWakeRetriesOnce(bool add, bool permanentFault)
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var clockCalls = 0;
        var turns = 0;
        using var store = new LogStore(clock: new DelegateTimeProvider(() =>
        {
            if (Interlocked.Increment(ref clockCalls) == 1)
            {
                entered.Set();
                release.Wait(TestContext.Current.CancellationToken);
                throw new InvalidOperationException("clock unavailable");
            }
            if (permanentFault) throw new InvalidOperationException("clock unavailable");
            return DateTimeOffset.UnixEpoch;
        }), schedule: action => callbacks.Enqueue(() => { turns++; action(); }));
        var versions = new List<long>();
        store.Changed += (_, changes) => versions.Add(changes.Version);
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "first", DateTimeOffset.UnixEpoch);
        var turn = Task.Run(Take(callbacks), TestContext.Current.CancellationToken);
        try
        {
            entered.Wait(TestContext.Current.CancellationToken);
            if (add) store.Add(LogLevel.Info, "app", "overlap", DateTimeOffset.UnixEpoch);
            else store.SetReady(true);
        }
        finally { release.Set(); }
        await turn;
        Assert.Empty(versions);
        Assert.Equal(1, (int)Field(store, "_writerScheduled")!);
        Take(callbacks)();
        if (permanentFault) Assert.Empty(versions);
        else Assert.Equal(new long[] { add ? 2 : 1 }, versions);
        Assert.Equal(2, Volatile.Read(ref clockCalls));
        Assert.Equal(2, turns);
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        store.Dispose();
        Take(callbacks)();
        Assert.Equal(2, Volatile.Read(ref clockCalls));
        Assert.Equal(3, turns);
    }

    /// <summary>A permanently unavailable clock is attempted at most once per externally requested writer turn.</summary>
    [Fact]
    public void PermanentlyThrowingClockPublishesAndReleasesCapacityWithoutRescheduling()
    {
        var callbacks = new ConcurrentQueue<Action>();
        var clockCalls = 0;
        var turns = 0;
        using var store = new LogStore(maxEntries: 1, maxPendingCharacters: 4,
            clock: new DelegateTimeProvider(() =>
            {
                Interlocked.Increment(ref clockCalls);
                throw new InvalidOperationException("clock unavailable");
            }), schedule: action => callbacks.Enqueue(() => { turns++; action(); }));
        store.Changed += (_, _) => Assert.Fail("No notification has a usable clock.");
        store.SetReady(true);
        var first = store.Add(LogLevel.Info, "app", "seed", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.True(store.IsCurrent(store.Generation, first, 0));
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(1, Volatile.Read(ref clockCalls));
        Assert.Equal(1, turns);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        var second = store.Add(LogLevel.Info, "app", "next", DateTimeOffset.UnixEpoch);
        Assert.True(second > first);
        Take(callbacks)();
        Assert.True(store.IsCurrent(store.Generation, second, 0));
        Assert.False(store.IsCurrent(store.Generation, first, 0));
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(2, Volatile.Read(ref clockCalls));
        Assert.Equal(2, turns);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        store.Dispose();
        Take(callbacks)();
        Assert.Equal(2, Volatile.Read(ref clockCalls));
        Assert.Equal(3, turns);
        Assert.Empty(callbacks);
    }

    /// <summary>Unready publication and subscriber-free notification require no clock calls.</summary>
    /// <param name="ready">Whether notifications are ready without any subscribers.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationWithoutDeliveryDoesNotReadClock(bool ready)
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(clock: new DelegateTimeProvider(() => throw new InvalidOperationException("unexpected clock read")),
            schedule: callbacks.Enqueue);
        store.SetReady(ready);
        var id = store.Add(LogLevel.Info, "app", "seed", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.True(store.IsCurrent(store.Generation, id, 0));
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        store.Dispose();
        Take(callbacks)();
    }

    /// <summary>Notification capture time is sampled after comparison and actual content disposal.</summary>
    [Fact]
    public void NotificationCapturedAtFollowsComparisonAndCleanup()
    {
        var callbacks = new ConcurrentQueue<Action>();
        var now = DateTimeOffset.UnixEpoch;
        using var store = new LogStore(maxEntries: 1, clock: new DelegateTimeProvider(() => now), schedule: callbacks.Enqueue);
        var seed = new TestContent("same", 4, () => now += TimeSpan.FromMinutes(1));
        store.Add(new LogWrite(LogLevel.Info, "app", seed, DateTimeOffset.UnixEpoch));
        Take(callbacks)();
        seed.OnRead = () => now += TimeSpan.FromMinutes(1);
        DateTimeOffset? notified = null;
        store.Changed += (_, changes) => notified = changes.Snapshot.CapturedAt;
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "same", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.Equal(1, seed.Disposals);
        Assert.Equal(DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(2), notified);
        Assert.Equal(now, notified);
        store.Dispose();
        Take(callbacks)();
    }
}
