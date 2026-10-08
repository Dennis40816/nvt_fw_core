// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Reflection;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Total admission ownership, atomic rejection and adjacent absolute paths.</summary>
public sealed class Fix6RegressionTests
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
        var attempted = new List<(Content Content, long Id)>();
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var content = new Content(i.ToString("D4", CultureInfo.InvariantCulture), zeroCharge ? 0 : 4);
                attempted.Add((content, store.Add(new LogWrite(LogLevel.Info, "app", content))));
                if (clear) { store.Clear(); store.Clear(); }
                var owned = attempted.Where(item => item.Id != 0 && item.Content.Disposals == 0).ToArray();
                Assert.InRange(owned.Length, 0, 4);
                Assert.InRange(owned.Sum(item => (long)item.Content.ResidentCharacterCount), 0, 8);
                Assert.Equal((owned.Length, owned.Sum(item => (long)item.Content.ResidentCharacterCount)), store.PendingUsage);
                Assert.All(attempted, item => Assert.Equal(0, item.Content.Disposals));
            }
        }
        finally { store.Dispose(); LogStoreTests.Flush(store); }
        Assert.Contains(attempted, item => item.Id == 0);
        Assert.All(attempted, item => Assert.Equal(item.Id == 0 ? 0 : 1, item.Content.Disposals));
    }

    /// <summary>A rejected batch cannot transfer a prefix, publish, or consume IDs.</summary>
    [Fact]
    public void BatchRejectionIsAtomicAndConsumesNoIds()
    {
        using var store = LogStoreTests.CreateStore(pending: 8);
        var first = store.Add(LogLevel.Info, "app", "seed");
        var contents = new[] { new Content("aaaa", 4), new Content("bbbb", 4) };
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

    /// <summary>Both scanner entry points prioritize absolute prefixes adjoining CJK prose.</summary>
    /// <param name="prose">The adjacent prose.</param>
    /// <param name="path">The absolute path.</param>
    [Theory]
    [InlineData("檔案", @"C:\Demo\file.cs")]
    [InlineData("開啟", @"\\server\share\檔案.log")]
    [InlineData("𠀀", @"D:/Demo/檔案.log")]
    public void AbsolutePathsBesideCjkHaveScannerParity(string prose, string path)
    {
        // Place the read boundary at each interior position of the drive or UNC prefix.
        foreach (var prefixOffset in new[] { 0, 1, 2 })
        {
            var padding = prefixOffset == 0 ? 0 : 1024 - prose.Length - prefixOffset;
            var text = new string(' ', padding) + prose + path + "。完成";
            var expected = new ConsoleLinkSpan(padding + prose.Length, path.Length, new LinkTarget(LinkKind.File, path));
            using var content = new Content(text, 0);
            Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(text)));
            Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(content)));
            Assert.InRange(content.MaximumRead, 1, 1024);
        }
    }

    // Immutable reads; Interlocked owns the lifetime counter across writer and test threads.
    internal sealed class Content(string text, int resident, Action? onDispose = null) : ILogTextContent
    {
        private int _disposals;
        internal int Disposals => Volatile.Read(ref _disposals);
        internal int MaximumRead { get; private set; }
        public int Length => text.Length;
        public int ResidentCharacterCount => resident;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose() { onDispose?.Invoke(); Interlocked.Increment(ref _disposals); }
    }
}
