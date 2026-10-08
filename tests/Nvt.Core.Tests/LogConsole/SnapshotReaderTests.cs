// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Nonblocking published readers and cancellable async barriers.</summary>
public sealed class SnapshotReaderTests
{
    /// <summary>A dispatcher caller can read the last publication before running queued work.</summary>
    [Fact]
    public async Task SnapshotOnSingleThreadSchedulerReturnsBeforeWriterRuns()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var pump = new SingleThreadPump(waitCancellation.Token);
        var callbacks = new ConcurrentQueue<Action>();
        await pump.Run(async () =>
        {
            Assert.Same(pump, SynchronizationContext.Current);
            using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
            store.Add(LogLevel.Info, "app", "queued");
            using var snapshot = store.CaptureSnapshot();
            Assert.Empty(snapshot.Entries);
            pump.Schedule(Take(callbacks));
            using var published = await store.CaptureLatestAsync(waitCancellation.Token);
            Assert.Same(pump, SynchronizationContext.Current);
            Assert.Single(published.Entries);
            store.Dispose();
            pump.Schedule(Take(callbacks));
        }).WaitAsync(waitCancellation.Token);
    }

    /// <summary>An awaiting dispatcher caller yields to the writer on that same dispatcher.</summary>
    [Fact]
    public async Task LatestCaptureOnDispatcherYieldsToWriterAndCompletes()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var pump = new SingleThreadPump(waitCancellation.Token);
        await pump.Run(async () =>
        {
            var dispatcherThread = Environment.CurrentManagedThreadId;
            var writerThreads = new ConcurrentQueue<int>();
            using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: action => pump.Schedule(() =>
            {
                writerThreads.Enqueue(Environment.CurrentManagedThreadId);
                action();
            }));
            store.Add(LogLevel.Info, "app", "queued");
            var capture = store.CaptureLatestAsync(waitCancellation.Token);
            Assert.False(capture.IsCompleted);
            using var published = await capture;
            Assert.Single(published.Entries);
            Assert.Same(pump, SynchronizationContext.Current);
            Assert.Equal(dispatcherThread, Environment.CurrentManagedThreadId);
            Assert.All(writerThreads, id => Assert.Equal(dispatcherThread, id));
        }).WaitAsync(waitCancellation.Token);
    }

    /// <summary>An async barrier yields its caller and covers accepted writes and reset markers.</summary>
    [Fact]
    public async Task LatestCaptureYieldsAndCoversAddClearAndRejection()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(maxPendingCharacters: 4, clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "seed");
        var capture = Latest(store, waitCancellation.Token);
        Assert.False(capture.IsCompleted);
        Take(callbacks)();
        using (var added = await capture.WaitAsync(waitCancellation.Token))
            Assert.Equal("seed", LogText.ReadAll(Assert.Single(added.Entries).TextContent));
        store.Clear();
        capture = Latest(store, waitCancellation.Token);
        Assert.False(capture.IsCompleted);
        Take(callbacks)();
        using (var cleared = await capture.WaitAsync(waitCancellation.Token))
        {
            Assert.Equal(store.Generation, cleared.Generation);
            Assert.Empty(cleared.Entries);
        }
        Assert.Equal(0, store.Add(LogLevel.Info, "app", "oversized"));
        capture = Latest(store, waitCancellation.Token);
        Assert.True(capture.IsCompletedSuccessfully);
        using var rejected = await capture;
        Assert.Empty(rejected.Entries);
        Assert.Empty(callbacks);
    }

    /// <summary>Cancellation and disposal end outstanding barriers without needing a writer turn.</summary>
    /// <param name="dispose">Whether store disposal supplies cancellation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatestCaptureCancelsWithoutWriterTurn(bool dispose)
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(schedule: callbacks.Enqueue);
        using var cancellation = new CancellationTokenSource();
        store.Add(LogLevel.Info, "app", "queued");
        var capture = Latest(store, cancellation.Token);
        Assert.False(capture.IsCompleted);
        if (dispose) store.Dispose(); else cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.WaitAsync(waitCancellation.Token));
        Assert.True(capture.IsCanceled);
        if (!dispose)
        {
            var next = Latest(store, waitCancellation.Token);
            Take(callbacks)();
            using var snapshot = await next;
            Assert.Single(snapshot.Entries);
        }
        else
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => Latest(store, waitCancellation.Token));
            Take(callbacks)();
        }
        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        if (dispose) await Assert.ThrowsAsync<ObjectDisposedException>(() => Latest(store, alreadyCancelled.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Latest(store, alreadyCancelled.Token));
    }

    /// <summary>Both writer callback types read the publication instead of waiting on themselves.</summary>
    [Fact]
    public void LatestCaptureInsideChangedAndContentDisposeCompletesImmediately()
    {
        using var store = LogStoreTests.CreateStore(entries: 1);
        var observed = new List<long>();
        var content = new TestContent("seed", 4, () =>
        {
            store.Clear();
            var capture = Latest(store, TestContext.Current.CancellationToken);
            Assert.True(capture.IsCompletedSuccessfully);
            using var snapshot = capture.Result;
            observed.Add(snapshot.Version);
        });
        store.Changed += (_, changes) =>
        {
            if (changes.Version != 1) return;
            store.Add(LogLevel.Info, "app", "next");
            var capture = Latest(store, TestContext.Current.CancellationToken);
            Assert.True(capture.IsCompletedSuccessfully);
            using var snapshot = capture.Result;
            Assert.Equal(changes.Version, snapshot.Version);
            observed.Add(snapshot.Version);
        };
        store.SetReady(true);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        LogStoreTests.Flush(store);
        Assert.Equal(new long[] { 1, 2 }, observed);
    }
}
