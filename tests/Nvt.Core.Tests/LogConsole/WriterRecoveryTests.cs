// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Writer clock recovery and notification continuity.</summary>
public sealed class WriterRecoveryTests
{
    /// <summary>A clock fault after publication leaves notification pending until the next explicit wake.</summary>
    [Fact]
    public void ThrowOnceWriterClockRearmsAndStoreKeepsWorking()
    {
        var callbacks = new ConcurrentQueue<Action>();
        var reads = 0;
        using var store = new LogStore(clock: new DelegateTimeProvider(() =>
        {
            if (Interlocked.Increment(ref reads) == 1) throw new InvalidOperationException("clock fault");
            return DateTimeOffset.UnixEpoch;
        }), schedule: callbacks.Enqueue);
        var versions = new List<long>();
        store.Changed += (_, changes) => versions.Add(changes.Version);
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "first", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.Empty(versions);
        Assert.Equal(0, (int)Field(store, "_writerScheduled")!);
        Assert.Empty(callbacks);
        Assert.Equal((0, 0L), store.PendingUsage);
        store.SetReady(true);
        Take(callbacks)();
        Assert.Equal(new long[] { 1 }, versions);
        store.Add(LogLevel.Info, "app", "second", DateTimeOffset.UnixEpoch);
        Take(callbacks)();
        Assert.Equal(new long[] { 1, 2 }, versions);
        using var snapshot = store.CaptureSnapshot();
        Assert.Equal(2, snapshot.EventCount);
        store.Dispose();
        Take(callbacks)();
    }
}
