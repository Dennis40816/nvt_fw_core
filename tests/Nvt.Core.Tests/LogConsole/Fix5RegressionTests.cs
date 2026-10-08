// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Nvt.Core.LogConsole;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Admission peaks, latest-reader ordering, clock recovery and Unicode regressions.</summary>
public sealed class Fix5RegressionTests
{
    /// <summary>Full admission keeps accepted ownership bounded and leaves rejected content untouched.</summary>
    [Fact]
    public void HeldWriterNeverExceedsPendingCharacterLimit()
    {
        using var store = LogStoreTests.CreateStore(characters: 1000, pending: 8);
        var contents = Enumerable.Range(0, 5).Select(_ => new Content("aaaa", 4, () =>
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
        var contents = Enumerable.Range(0, 20).Select(_ => new Content("a", 0)).ToArray();
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
        var contents = Enumerable.Range(0, 20).Select(_ => new Content("aaaa", 4)).ToArray();
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

    /// <summary>A latest read covers admitted writes and clears; rejection adds no reader fence.</summary>
    /// <param name="operation">The operation preceding capture.</param>
    [Theory]
    [InlineData("add")]
    [InlineData("clear")]
    [InlineData("reject")]
    public async Task CaptureWaitsForPreviouslyAdmittedOperations(string operation)
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(maxPendingCharacters: 8, clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "old");
        Take(callbacks)(); // Publish only the initial fixture.
        var id = operation == "clear" ? 0 : store.Add(LogLevel.Info, "app", operation == "reject" ? "oversized" : "new");
        if (operation == "clear") store.Clear();
        var capture = Task.Run(store.CaptureSnapshot, TestContext.Current.CancellationToken);
        AwaitCaptureOrFence(store, capture);
        if (operation != "reject") Take(callbacks)(); // Rejection schedules no writer.
        using var snapshot = await capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "old");
        Take(callbacks)();
        store.Add(LogLevel.Info, "app", "latest");
        var export = Task.Run(() =>
        {
            using var snapshot = store.CaptureSnapshot();
            using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState());
            return ConsoleExportFormatter.FormatVisible(projection, new ConsoleExportOptions(false, false));
        }, TestContext.Current.CancellationToken);
        AwaitCaptureOrFence(store, export);
        Take(callbacks)();
        Assert.Equal("[app] old\n[app] latest", await export.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    /// <summary>Closing admission releases a reader waiting for a held writer.</summary>
    [Fact]
    public async Task DisposeReleasesWaitingLatestReader()
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(schedule: callbacks.Enqueue);
        store.Add(LogLevel.Info, "app", "queued");
        var capture = Task.Run(() => Record.Exception(() => { using var snapshot = store.CaptureSnapshot(); }),
            TestContext.Current.CancellationToken);
        AwaitCaptureOrFence(store, capture);
        store.Dispose();
        Assert.IsType<ObjectDisposedException>(await capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Take(callbacks)();
    }

    /// <summary>Writer callbacks capture the current publication even after enqueueing another operation.</summary>
    [Fact]
    public async Task CaptureInsideChangedAndContentDisposeDoesNotWaitForItself()
    {
        using var store = LogStoreTests.CreateStore(entries: 1);
        var observed = new ConcurrentQueue<long>();
        var failures = new ConcurrentQueue<Exception>();
        var content = new Content("first", 5, () =>
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
        await Task.Run(() => LogStoreTests.Flush(store), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(failures);
        Assert.Null(content.Failure);
        Assert.Equal(new long[] { 1, 2 }, observed);
        Assert.Equal(1, content.Disposals);
        using var latest = store.CaptureSnapshot();
        Assert.Equal(store.Generation, latest.Generation);
        Assert.Empty(latest.Entries);
    }

    /// <summary>A clock fault after publication rearms the outstanding notification once.</summary>
    [Fact]
    public void ThrowOnceNotificationClockRearmsAndStoreKeepsWorking()
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
        Assert.Equal(1, (int)Field(store, "_writerScheduled")!);
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

    /// <summary>Both scanner entry points preserve whole scalar values, including split surrogate pairs.</summary>
    /// <param name="path">The relative path.</param>
    /// <param name="padding">Padding that places surrogate pairs across read boundaries.</param>
    /// <param name="segmented">Whether to use the segmented scanner entry point.</param>
    [Theory]
    [InlineData("𠀀/報表", 0, false)]
    [InlineData("𠀀/報表", 0, true)]
    [InlineData("報𠀀/表", 0, false)]
    [InlineData("報𠀀/表", 0, true)]
    [InlineData("𠀀/報表", 1023, false)]
    [InlineData("𠀀/報表", 1023, true)]
    [InlineData("報𠀀/表", 1022, false)]
    [InlineData("報𠀀/表", 1022, true)]
    [InlineData("報表/𠀀", 1020, false)]
    [InlineData("報表/𠀀", 1020, true)]
    [InlineData("𠀀.cs", 1023, false)]
    [InlineData("𠀀.cs", 1023, true)]
    public void SupplementaryRelativePathsHaveScannerParity(string path, int padding, bool segmented)
    {
        var text = new string(' ', padding) + path + ":12:3";
        var expected = new ConsoleLinkSpan(padding, path.Length + 5, new LinkTarget(LinkKind.File, path, 12, 3));
        using var content = new Content(text, 0);
        Assert.Equal(expected, Assert.Single(segmented ? ConsoleLinkScanner.Scan(content) : ConsoleLinkScanner.Scan(text)));
        if (segmented) Assert.InRange(content.MaximumRead, 1, 1024);
    }

    /// <summary>CJK prose does not mask recognized URL schemes or leak into a punctuation-delimited target.</summary>
    /// <param name="before">Text before the URL.</param>
    /// <param name="after">Text after the URL.</param>
    /// <param name="segmented">Whether to use the segmented scanner entry point.</param>
    [Theory]
    [InlineData("請見", "。", false)]
    [InlineData("請見", "。", true)]
    [InlineData("請見", "。完成", false)]
    [InlineData("請見", "。完成", true)]
    [InlineData("", "。完成", false)]
    [InlineData("", "。完成", true)]
    [InlineData("", "", false)]
    [InlineData("", "", true)]
    [InlineData("𠀀", "。完成", false)]
    [InlineData("𠀀", "。完成", true)]
    public void UrlBesideCjkProseHasScannerParity(string before, string after, bool segmented)
    {
        const string url = "https://example.test/export_(v2)";
        var text = before + url + after;
        var expected = new ConsoleLinkSpan(before.Length, url.Length, new LinkTarget(LinkKind.Url, url));
        using var content = new Content(text, 0);
        Assert.Equal(expected, Assert.Single(segmented ? ConsoleLinkScanner.Scan(content) : ConsoleLinkScanner.Scan(text)));
    }

    private static object? Field(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value);
    private static int PendingCount(LogStore store) => ((IEnumerable)Field(store, "_pending")!).Cast<object>().Count();
    private static long PendingCharge(LogStore store) => store.PendingUsage.Characters;
    private static void AwaitCaptureOrFence(LogStore store, Task capture) => Assert.True(SpinWait.SpinUntil(() =>
    {
        lock (Field(store, "_gate")!)
            return capture.IsCompleted || Field(store, "_snapshotWaiters") is ICollection { Count: > 0 };
    }, TimeSpan.FromSeconds(10)));
    private static Action Take(ConcurrentQueue<Action> callbacks)
    {
        Action? result = null;
        Assert.True(SpinWait.SpinUntil(() => callbacks.TryDequeue(out result), TimeSpan.FromSeconds(10)));
        return result!;
    }

    // Tests control reads; Interlocked protects disposal across writer and test threads.
    private sealed class Content(string text, int resident, Action? onDispose = null) : ILogTextContent
    {
        private int _disposals;
        internal int Disposals => Volatile.Read(ref _disposals);
        internal Exception? Failure { get; private set; }
        internal int MaximumRead { get; private set; }
        public int Length => text.Length;
        public int ResidentCharacterCount => resident;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            try { onDispose?.Invoke(); }
            catch (Exception exception) { Failure = exception; }
        }
    }
}
