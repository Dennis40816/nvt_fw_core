// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Pending character and handle bounds across resets.</summary>
public sealed class PendingOwnershipTests
{
    /// <summary>Full admission keeps accepted ownership bounded and leaves rejected content untouched.</summary>
    [Fact]
    public void HeldWriterNeverExceedsPendingCharacterLimit()
    {
        using var store = LogStoreTests.CreateStore(characters: 1000, pending: 8);
        var contents = Enumerable.Range(0, 5).Select(_ => new TestContent("aaaa", 4, () =>
            Assert.False(Monitor.IsEntered(Field(store, "_gate")!)))).ToArray();
        try
        {
            foreach (var content in contents)
            {
                store.Add(new LogWrite(LogLevel.Info, "app", content));
                Assert.InRange(PendingCharge(store), 0, 8);
                Assert.All(contents, value => Assert.Equal(0, value.Disposals));
            }
        }
        finally { LogStoreTests.Flush(store); }
        using (var snapshot = store.CaptureSnapshot())
        {
            Assert.Equal(2, snapshot.EventCount);
            Assert.Equal(0, snapshot.EvictedCount);
            Assert.Equal(3, store.RejectedCount);
            Assert.All(contents, content => Assert.Equal(0, content.Disposals));
        }
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.All(contents.Take(2), content => Assert.Equal(1, content.Disposals));
        Assert.All(contents.Skip(2), content => Assert.Equal(0, content.Disposals));
        Assert.All(contents, content => Assert.Null(content.Failure));
    }

    /// <summary>Zero-charge writes have an independent handle limit and keep every accepted ID.</summary>
    [Fact]
    public void HeldWriterBoundsZeroChargeHandles()
    {
        using var store = LogStoreTests.CreateStore(entries: 4, pending: 8);
        var contents = Enumerable.Range(0, 20).Select(_ => new TestContent("a", 0)).ToArray();
        var ids = new List<long>();
        try
        {
            foreach (var content in contents)
            {
                ids.Add(store.Add(new LogWrite(LogLevel.Info, "app", content)));
                Assert.InRange(PendingCount(store), 0, 4);
                Assert.Equal(0, PendingCharge(store));
                Assert.All(contents, value => Assert.Equal(0, value.Disposals));
            }
        }
        finally { LogStoreTests.Flush(store); }
        using (var snapshot = store.CaptureSnapshot())
        {
            Assert.Equal(ids.Take(4), snapshot.Entries.Select(entry => entry.EntryId));
            Assert.All(ids.Skip(4), id => Assert.Equal(0, id));
            Assert.Equal(0, snapshot.EvictedCount);
            Assert.Equal(16, store.RejectedCount);
            Assert.All(contents, content => Assert.Equal(0, content.Disposals));
        }
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.All(contents.Take(4), content => Assert.Equal(1, content.Disposals));
        Assert.All(contents.Skip(4), content => Assert.Equal(0, content.Disposals));
    }

    /// <summary>Consecutive resets merge and mixed resets cannot accumulate obsolete queued writes.</summary>
    [Fact]
    public void HeldWriterMergesClearMarkersAndReleasesDiscardedWritesOnce()
    {
        using var store = LogStoreTests.CreateStore(entries: 4, pending: 8);
        var contents = Enumerable.Range(0, 20).Select(_ => new TestContent("aaaa", 4)).ToArray();
        try
        {
            for (var i = 0; i < 20; i++)
            {
                store.Clear();
                Assert.Equal(1, PendingCount(store));
            }
            foreach (var content in contents)
            {
                store.Add(new LogWrite(LogLevel.Info, "app", content));
                store.Clear();
                Assert.Equal(1, PendingCount(store));
                Assert.InRange(PendingCharge(store), 4, 8);
                Assert.Equal(0, content.Disposals);
            }
        }
        finally { LogStoreTests.Flush(store); }
        using var snapshot = store.CaptureSnapshot();
        Assert.Equal(40, snapshot.Generation);
        Assert.Equal(2, snapshot.LastSequence);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(0, snapshot.EvictedCount);
        Assert.Equal(18, store.RejectedCount);
        Assert.All(contents.Take(2), content => Assert.Equal(1, content.Disposals));
        Assert.All(contents.Skip(2), content => Assert.Equal(0, content.Disposals));
    }
}
