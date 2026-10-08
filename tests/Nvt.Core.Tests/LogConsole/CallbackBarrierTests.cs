// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Isolated app faults leave pending publication barriers intact.</summary>
public sealed class CallbackBarrierTests
{
    private static readonly string[] PublishedMessages = ["seed", "later"];
    /// <summary>A subscriber fault cannot fault a barrier for its newly admitted work.</summary>
    [Fact]
    public async Task ChangedFailureDoesNotFaultPendingCapture()
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        Task<LogSnapshot>? pending = null;
        store.Changed += (_, changes) =>
        {
            if (changes.Version != 1) return;
            pending = AdmitFromProducer(store);
            Assert.False(pending.IsCompleted);
            throw new InvalidOperationException("subscriber unavailable");
        };
        store.SetReady(true);
        store.Add(LogLevel.Info, "app", "seed");
        Take(callbacks)();
        Assert.NotNull(pending);
        using var published = await pending;
        Assert.Equal(2, published.Version);
        Assert.Equal(PublishedMessages, published.Entries.Select(e => LogText.ReadAll(e.TextContent)));
        store.Dispose();
        Take(callbacks)();
    }

    /// <summary>A content disposal fault cannot fault a barrier admitted during cleanup.</summary>
    [Fact]
    public async Task ContentDisposalFailureDoesNotFaultPendingCapture()
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(maxEntries: 1, clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        Task<LogSnapshot>? pending = null;
        var content = new TestContent("seed", 4, () =>
        {
            pending = AdmitFromProducer(store);
            Assert.False(pending.IsCompleted);
            throw new InvalidOperationException("dispose unavailable");
        });
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        Take(callbacks)();
        store.Add(LogLevel.Info, "app", "replacement");
        Take(callbacks)();
        Assert.NotNull(pending);
        using var published = await pending;
        Assert.Equal(3, published.Version);
        Assert.Equal("later", LogText.ReadAll(Assert.Single(published.Entries).TextContent));
        Assert.Equal(1, content.Disposals);
        Assert.IsType<InvalidOperationException>(content.Failure);
        store.Dispose();
        Take(callbacks)();
    }

    /// <summary>A failing dedupe read retains and publishes the accepted write for its barrier.</summary>
    [Fact]
    public async Task ComparisonFailureDoesNotFaultPendingCapture()
    {
        var callbacks = new ConcurrentQueue<Action>();
        using var store = new LogStore(clock: LogStoreTests.FixedClock(), schedule: callbacks.Enqueue);
        var seed = new TestContent("same", 4);
        store.Add(new LogWrite(LogLevel.Info, "app", seed));
        Take(callbacks)();
        seed.OnRead = () => throw new InvalidOperationException("comparison unavailable");
        store.Add(LogLevel.Info, "app", "same");
        var pending = Latest(store, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        Take(callbacks)();
        seed.OnRead = null;
        using var published = await pending;
        Assert.Equal(2, published.Version);
        Assert.Equal(2, published.EventCount);
        Assert.NotEqual(published.Entries[0].GroupId, published.Entries[1].GroupId);
        Assert.Equal(0, published.EvictedCount);
        store.Dispose();
        Take(callbacks)();
    }

    private static Task<LogSnapshot> AdmitFromProducer(LogStore store)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var completion = new TaskCompletionSource<Task<LogSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = new Thread(() =>
        {
            try
            {
                store.Add(LogLevel.Info, "app", "later");
                completion.SetResult(Latest(store, cancellationToken));
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        producer.Start();
        producer.Join();
        return completion.Task.GetAwaiter().GetResult();
    }
}
