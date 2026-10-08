// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;
using static Nvt.Core.Tests.LogConsole.PublicationTestSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Writer failure ownership and asynchronous error delivery.</summary>
public sealed class WriterFailureTests
{
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
        var first = new ScannerTestContent("first", 5, () =>
        {
            disposals[0]++;
            if (throwOnDispose) throw new InvalidOperationException("content disposal failed");
        });
        var second = new ScannerTestContent("second", 6, () => disposals[1]++);
        store.Add(new LogWrite(LogLevel.Info, "app", first, DateTimeOffset.UnixEpoch));
        store.Add(new LogWrite(LogLevel.Info, "app", second, DateTimeOffset.UnixEpoch));
        Take(callbacks)();
        using (var published = store.CaptureSnapshot()) Assert.Equal(2, published.EventCount);
        Assert.Equal(0, (int)StoreRegressionSupport.Field(store, "_writerScheduled")!);

        fail = true;
        store.SetReady(true);
        Take(callbacks)();
        Assert.Equal(1, (int)StoreRegressionSupport.Field(store, "_writerScheduled")!);
        Take(callbacks)();

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

    /// <summary>A clock failure after taking work faults its barrier and preserves bounded ownership.</summary>
    /// <param name="clear">Whether a reset has discarded the accepted content.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriterClockFailureRequeuesTakenWorkAndFaultsBarrier(bool clear)
    {
        var callbacks = new ConcurrentQueue<Action>();
        var reads = 0;
        var fault = new InvalidOperationException("clock unavailable");
        using var store = new LogStore(maxEntries: 1, maxPendingCharacters: 4,
            clock: new DelegateTimeProvider(() => Interlocked.Increment(ref reads) == 1 ? throw fault : DateTimeOffset.UnixEpoch),
            schedule: callbacks.Enqueue);
        var content = new ScannerTestContent("seed", 4);
        store.Add(new LogWrite(LogLevel.Info, "app", content, DateTimeOffset.UnixEpoch));
        if (clear) store.Clear();
        var capture = Latest(store, TestContext.Current.CancellationToken);
        Assert.False(capture.IsCompleted);
        Take(callbacks)();
        Assert.Same(fault, await Assert.ThrowsAsync<InvalidOperationException>(() => capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
        Assert.Equal((1, 4L), store.PendingUsage);
        Assert.Equal(0, store.Add(LogLevel.Info, "app", "next", DateTimeOffset.UnixEpoch));
        var retry = Latest(store, TestContext.Current.CancellationToken);
        Take(callbacks)();
        using (var recovered = await retry.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
        {
            if (clear) Assert.Empty(recovered.Entries); else Assert.Single(recovered.Entries);
            Assert.Equal(store.Generation, recovered.Generation);
        }
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(2, store.Add(LogLevel.Info, "app", "next", DateTimeOffset.UnixEpoch));
        var next = Latest(store, TestContext.Current.CancellationToken);
        Take(callbacks)();
        using (var snapshot = await next) Assert.Equal(2, Assert.Single(snapshot.Entries).EntryId);
        Assert.Equal(1, content.Disposals);
        store.Dispose();
        Take(callbacks)();
    }
}
