// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Reflection;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Whole-batch rejection and bounded unpublished ownership.</summary>
public sealed class AdmissionOwnershipTests
{
    /// <summary>Observable accepted lifetimes include discarded writes across repeated resets.</summary>
    /// <param name="zeroCharge">Whether handles have no resident charge.</param>
    /// <param name="clear">Whether every attempt is followed by Clear.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HeldWriterBoundsAllAcceptedContent(bool zeroCharge, bool clear)
    {
        using var store = LogStoreTests.CreateStore(entries: 4, pending: 8);
        var attempted = new List<(ScannerTestContent ScannerTestContent, long Id)>();
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var content = new ScannerTestContent(i.ToString("D4", CultureInfo.InvariantCulture), zeroCharge ? 0 : 4);
                attempted.Add((content, store.Add(new LogWrite(LogLevel.Info, "app", content))));
                if (clear) { store.Clear(); store.Clear(); }
                var owned = attempted.Where(item => item.Id != 0 && item.ScannerTestContent.Disposals == 0).ToArray();
                Assert.InRange(owned.Length, 0, 4);
                Assert.InRange(owned.Sum(item => (long)item.ScannerTestContent.ResidentCharacterCount), 0, 8);
                Assert.Equal((owned.Length, owned.Sum(item => (long)item.ScannerTestContent.ResidentCharacterCount)), store.PendingUsage);
                Assert.All(attempted, item => Assert.Equal(0, item.ScannerTestContent.Disposals));
            }
        }
        finally { store.Dispose(); LogStoreTests.Flush(store); }
        Assert.Contains(attempted, item => item.Id == 0);
        Assert.All(attempted, item => Assert.Equal(item.Id == 0 ? 0 : 1, item.ScannerTestContent.Disposals));
    }

    /// <summary>A rejected batch cannot transfer a prefix, publish, or consume IDs.</summary>
    [Fact]
    public void BatchRejectionIsAtomicAndConsumesNoIds()
    {
        using var store = LogStoreTests.CreateStore(pending: 8);
        var first = store.Add(LogLevel.Info, "app", "seed");
        var contents = new[] { new ScannerTestContent("aaaa", 4), new ScannerTestContent("bbbb", 4) };
        Assert.False(store.AddBatch(store.Generation, contents.Select(content => new LogWrite(LogLevel.Info, "app", content))));
        using (var snapshot = LogStoreTests.Capture(store))
        {
            Assert.Equal(first, Assert.Single(snapshot.Entries).EntryId);
            Assert.Equal(0, snapshot.EvictedCount);
        }
        var next = store.Add(LogLevel.Info, "app", "next");
        Assert.Equal(first + 1, next);
        store.Dispose();
        LogStoreTests.Flush(store);
        Assert.All(contents, content => Assert.Equal(0, content.Disposals));
    }

    /// <summary>A pending total must be derived from ownership, not maintained as a second field.</summary>
    [Fact]
    public void PendingChargeHasNoIndependentStoredCounter()
    {
        Assert.Null(typeof(LogStore).GetField("_pendingCharacters", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
