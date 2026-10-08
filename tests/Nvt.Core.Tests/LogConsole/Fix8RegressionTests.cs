// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Quoted relative filename parity and admission fences derived from existing state.</summary>
public sealed class Fix8RegressionTests
{
    /// <summary>Quoted names need no separator when a location suffix identifies the path.</summary>
    /// <param name="name">The full filename, including spaces and Unicode.</param>
    /// <param name="suffix">The line and column syntax.</param>
    [Theory]
    [InlineData("Export Writer.cs", "(142,18)")]
    [InlineData("Export Writer.cs", ":142:18")]
    [InlineData("報表 檔案.cs", "(142,18)")]
    [InlineData("報表 檔案.cs", ":142:18")]
    [InlineData("𠀀 報表.cs", "(142,18)")]
    [InlineData("𠀀 報表.cs", ":142:18")]
    public void QuotedFilenameWithLocationHasScannerParityAcrossChunkBoundaries(string name, string suffix)
    {
        foreach (var quote in new[] { '\'', '"' })
        {
            var syntax = quote + name + quote + suffix;
            // Move the 1,024-character read boundary through every interior name and suffix position.
            foreach (var split in Enumerable.Range(0, syntax.Length))
            {
                var padding = split == 0 ? 0 : 1024 - split;
                var text = new string(' ', padding) + syntax + "。完成";
                var expected = new ConsoleLinkSpan(padding, syntax.Length, new LinkTarget(LinkKind.File, name, 142, 18));
                using var content = new Fix6RegressionTests.Content(text, 0);
                Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(text)));
                Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(content)));
                Assert.InRange(content.MaximumRead, 1, 1024);
            }
        }
    }

    /// <summary>Quoting a filename alone supplies no path evidence.</summary>
    /// <param name="name">The name without a path separator or location suffix.</param>
    [Theory]
    [InlineData("Export Writer.cs")]
    [InlineData("報表 檔案.cs")]
    [InlineData("𠀀 報表.cs")]
    public void QuotedFilenameWithoutLocationOrSeparatorIsNotALink(string name)
    {
        foreach (var quote in new[] { '\'', '"' })
        {
            var text = new string(' ', 1020) + quote + name + quote + "。完成";
            using var content = new Fix6RegressionTests.Content(text, 0);
            Assert.Empty(ConsoleLinkScanner.Scan(text));
            Assert.Empty(ConsoleLinkScanner.Scan(content));
        }
    }

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
            var capture = Task.Run(store.CaptureSnapshot, TestContext.Current.CancellationToken);
            if (pending)
            {
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    lock (Field("_gate")!)
                        return capture.IsCompleted || Field("_snapshotWaiters") is ICollection { Count: > 0 };
                }, TimeSpan.FromSeconds(10)));
                Assert.False(capture.IsCompleted);
                TakeWriter()();
            }
            using var snapshot = await capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
            Assert.True(SpinWait.SpinUntil(() => callbacks.TryDequeue(out callback), TimeSpan.FromSeconds(10)));
            return callback!;
        }
    }
}
