// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Tests.LogConsole.PublicationTestSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Nonblocking published readers and cancellable async barriers.</summary>
public sealed class SnapshotReaderTests
{
    /// <summary>A dispatcher caller can read the last publication before running queued work.</summary>
    [Fact]
    public async Task SnapshotOnSingleThreadSchedulerReturnsBeforeWriterRuns()
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        var caller = Task.Run(() =>
        {
            store.Add(LogLevel.Info, "app", "queued");
            using var snapshot = store.CaptureSnapshot();
            Assert.Empty(snapshot.Entries);
            Take(callbacks)(); // The scheduler's work runs later on this same thread.
            using var published = store.CaptureSnapshot();
            Assert.Single(published.Entries);
        }, TestContext.Current.CancellationToken);
        try { await caller.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken); }
        finally
        {
            // Release a blocked caller when verifying an implementation that waits synchronously.
            if (!caller.IsCompleted) Take(callbacks)();
            await caller.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>An async barrier yields its caller and covers accepted writes and reset markers.</summary>
    [Fact]
    public async Task LatestCaptureYieldsAndCoversAddClearAndRejection()
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(maxPendingCharacters: 4, clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "seed");
        var capture = Latest(store, TestContext.Current.CancellationToken);
        Assert.False(capture.IsCompleted);
        Take(callbacks)();
        using (var added = await capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            Assert.Equal("seed", LogText.ReadAll(Assert.Single(added.Entries).TextContent));
        store.Clear();
        capture = Latest(store, TestContext.Current.CancellationToken);
        Assert.False(capture.IsCompleted);
        Take(callbacks)();
        using (var cleared = await capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
        {
            Assert.Equal(store.Generation, cleared.Generation);
            Assert.Empty(cleared.Entries);
        }
        Assert.Equal(0, store.Add(LogLevel.Info, "app", "oversized"));
        capture = Latest(store, TestContext.Current.CancellationToken);
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
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(schedule: callbacks.Enqueue);
        using var cancellation = new CancellationTokenSource();
        store.Add(LogLevel.Info, "app", "queued");
        var capture = Latest(store, cancellation.Token);
        Assert.False(capture.IsCompleted);
        if (dispose) store.Dispose(); else cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(capture.IsCanceled);
        if (!dispose)
        {
            var next = Latest(store, TestContext.Current.CancellationToken);
            Take(callbacks)();
            using var snapshot = await next;
            Assert.Single(snapshot.Entries);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Latest(store, TestContext.Current.CancellationToken));
            Take(callbacks)();
        }
        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Latest(store, alreadyCancelled.Token));
    }

    /// <summary>Both writer callback types read the publication instead of waiting on themselves.</summary>
    [Fact]
    public void LatestCaptureInsideChangedAndContentDisposeCompletesImmediately()
    {
        using var store = LogStoreTests.CreateStore(entries: 1);
        var observed = new List<long>();
        var content = new ScannerTestContent("seed", 4, () =>
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
