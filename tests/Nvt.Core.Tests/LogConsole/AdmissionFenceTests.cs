// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Admission fences derived from generation and sequence.</summary>
public sealed class AdmissionFenceTests
{
    /// <summary>The active admission total must not duplicate generation and sequence.</summary>
    [Fact]
    public void AdmissionFenceHasNoIndependentStoredCounter()
    {
        Assert.Null(typeof(LogStore).GetField("_lastAdmitted", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    /// <summary>Each latest read covers mixed accepted operations while rejected calls leave its fence unchanged.</summary>
    [Fact]
    public async Task CaptureFenceCoversMixedAddsRejectionsBatchesAndClears()
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(maxPendingCharacters: 8, clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        await VerifyLatest(0, 0, [], false);
        Assert.Equal(1, store.Add(LogLevel.Info, "app", "first"));
        var version = await VerifyLatest(0, 1, ["first"], true);
        Assert.Equal(0, store.Add(LogLevel.Info, "app", "oversized"));
        Assert.Equal(version, await VerifyLatest(0, 1, ["first"], false));
        Assert.True(store.AddBatch(store.Generation, [LogStoreTests.Write("two"), LogStoreTests.Write("three")]));
        await VerifyLatest(0, 3, ["first", "two", "three"], true);
        store.Clear();
        await VerifyLatest(1, 3, [], true);
        store.Clear();
        await VerifyLatest(2, 3, [], true);
        Assert.Equal(4, store.Add(LogLevel.Info, "app", "last"));
        version = await VerifyLatest(2, 4, ["last"], true);
        Assert.False(store.AddBatch(0, [LogStoreTests.Write("stale")]));
        Assert.Equal(version, await VerifyLatest(2, 4, ["last"], false));
        Assert.True(store.AddBatch(store.Generation, []));
        Assert.Equal(version, await VerifyLatest(2, 4, ["last"], false));
        Assert.Equal(2, store.RejectedCount);
        store.Dispose();
        TakeWriter()();

        async Task<long> VerifyLatest(long generation, long sequence, string[] messages, bool pending)
        {
            var capture = store.CaptureLatestAsync(waitCancellation.Token).AsTask();
            if (pending)
            {
                Assert.True(StoreRegressionSupport.WaitUntil(() =>
                {
                    lock (Field("_gate")!)
                        return capture.IsCompleted || Field("_snapshotWaiters") is ICollection { Count: > 0 };
                }, waitCancellation.Token));
                Assert.False(capture.IsCompleted);
                TakeWriter()();
            }
            using var snapshot = await capture.WaitAsync(waitCancellation.Token);
            Assert.Equal(generation, snapshot.Generation);
            Assert.Equal(sequence, snapshot.LastSequence);
            Assert.Equal(messages, snapshot.Entries.Select(entry => LogText.ReadAll(entry.TextContent)));
            Assert.Equal(0, snapshot.EvictedCount);
            Assert.Empty(callbacks);
            return snapshot.Version;
        }

        object? Field(string name) => typeof(LogStore).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(store);
        Action TakeWriter()
        {
            Action? callback = null;
            Assert.True(StoreRegressionSupport.WaitUntil(() => callbacks.TryDequeue(out callback), waitCancellation.Token));
            return callback!;
        }
    }
}
