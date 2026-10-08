// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Async publication barriers and reentrant published readers.</summary>
public sealed class SnapshotPublicationTests
{
    /// <summary>A latest read covers admitted writes and clears; rejection adds no reader fence.</summary>
    /// <param name="operation">The operation preceding capture.</param>
    [Theory]
    [InlineData("add")]
    [InlineData("clear")]
    [InlineData("reject")]
    public async Task CaptureWaitsForPreviouslyAdmittedOperations(string operation)
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(maxPendingCharacters: 8, clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "old");
        Take(callbacks)(); // Publish only the initial fixture.
        var id = operation == "clear" ? 0 : store.Add(LogLevel.Info, "app", operation == "reject" ? "oversized" : "new");
        if (operation == "clear") store.Clear();
        var capture = store.CaptureLatestAsync(waitCancellation.Token).AsTask();
        AwaitCaptureOrFence(store, capture);
        if (operation != "reject") Take(callbacks)(); // Rejection schedules no writer.
        using var snapshot = await capture.WaitAsync(waitCancellation.Token);
        if (operation == "clear")
        {
            Assert.Equal(store.Generation, snapshot.Generation);
            Assert.Empty(snapshot.Entries);
        }
        else if (operation == "reject")
        {
            Assert.Equal(0, id);
            Assert.Equal(1, snapshot.LastSequence);
            Assert.Equal(0, snapshot.EvictedCount);
            Assert.Single(snapshot.Entries);
        }
        else
        {
            Assert.Equal(id, snapshot.LastSequence);
            Assert.Equal(id, snapshot.Entries[^1].EntryId);
        }
    }

    /// <summary>Export freezes the latest accepted data even while its writer callback is held.</summary>
    [Fact]
    public async Task LatestExportWaitsForAdmittedAddWithoutFlush()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "old");
        Take(callbacks)();
        store.Add(LogLevel.Info, "app", "latest");
        var export = Task.Run(async () =>
        {
            using var snapshot = await store.CaptureLatestAsync(waitCancellation.Token);
            using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState());
            return ConsoleExportFormatter.FormatVisible(projection, new ConsoleExportOptions(false, false));
        }, waitCancellation.Token);
        AwaitCaptureOrFence(store, export);
        Take(callbacks)();
        Assert.Equal("[app] old\n[app] latest", await export.WaitAsync(waitCancellation.Token));
    }

    /// <summary>Closing admission releases a reader waiting for a held writer.</summary>
    [Fact]
    public async Task DisposeReleasesWaitingLatestReader()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "queued");
        var capture = Record.ExceptionAsync(async () => { using var snapshot = await store.CaptureLatestAsync(waitCancellation.Token); }).AsTask();
        AwaitCaptureOrFence(store, capture);
        store.Dispose();
        Assert.IsAssignableFrom<OperationCanceledException>(await capture.WaitAsync(waitCancellation.Token));
        Take(callbacks)();
    }

    /// <summary>Writer callbacks capture the current publication even after enqueueing another operation.</summary>
    [Fact]
    public async Task CaptureInsideChangedAndContentDisposeDoesNotWaitForItself()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        using var store = LogStoreTests.CreateStore(entries: 1);
        var observed = new ConcurrentQueue<long>();
        var failures = new ConcurrentQueue<Exception>();
        var content = new TestContent("first", 5, () =>
        {
            store.Clear();
            using var snapshot = store.CaptureSnapshot();
            observed.Enqueue(snapshot.Version);
        });
        store.Changed += (_, changes) =>
        {
            if (changes.Version != 1) return;
            try
            {
                store.Add(LogLevel.Info, "app", "next");
                using var snapshot = store.CaptureSnapshot();
                Assert.Equal(changes.Version, snapshot.Version);
                observed.Enqueue(snapshot.Version);
            }
            catch (Exception exception) { failures.Enqueue(exception); }
        };
        store.SetReady(true);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        await Task.Run(() => LogStoreTests.Flush(store), waitCancellation.Token)
            .WaitAsync(waitCancellation.Token);
        Assert.Empty(failures);
        Assert.Null(content.Failure);
        Assert.Equal(new long[] { 1, 2 }, observed);
        Assert.Equal(1, content.Disposals);
        using var latest = store.CaptureSnapshot();
        Assert.Equal(store.Generation, latest.Generation);
        Assert.Empty(latest.Entries);
    }
}
