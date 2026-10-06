// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Tests.ReportList;

/// <summary>Concurrency regression coverage for indexed report row projections.</summary>
public sealed class ReportIndexedReadOnlyListsTests
{
    /// <summary>Unopened report rows retain only the index array rather than one Lazy and closure per row.</summary>
    [Fact]
    public void MemoizedIndexDefersPerRowAllocationUntilAccess()
    {
        const int rowCount = 10_000;
        static object factory(int _)
        {
            return new object();
        }

        var warmup = new MemoizedIndexedReadOnlyList<object>(1, factory);
        _ = warmup[0];

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var rows = new MemoizedIndexedReadOnlyList<object>(rowCount, factory);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(rowCount, rows.Count);
        Assert.Equal(0, rows.MaterializedCount);
        Assert.InRange(allocated, (long)rowCount * IntPtr.Size, ((long)rowCount * IntPtr.Size) + 32_768);
    }

    /// <summary>A failed row factory remains single-execution and rethrows the same cached failure.</summary>
    [Fact]
    public void MemoizedIndexCachesFactoryFailure()
    {
        var expected = new InvalidOperationException("Synthetic row projection failure.");
        int invocationCount = 0;
        var rows = new MemoizedIndexedReadOnlyList<object>(
            1,
            _ =>
            {
                _ = Interlocked.Increment(ref invocationCount);
                throw expected;
            });

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(() => rows[0]);
        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => rows[0]);

        Assert.Same(expected, first);
        Assert.Same(expected, second);
        Assert.Equal(1, invocationCount);
        Assert.Equal(0, rows.MaterializedCount);
    }

    /// <summary>Concurrent readers execute an expensive row factory once and share its published instance.</summary>
    [Fact]
    public async Task MemoizedIndexPublishesOneFactoryResultToConcurrentReaders()
    {
        object expected = new();
        int invocationCount = 0;
        using var factoryEntered = new ManualResetEventSlim(initialState: false);
        using var releaseFactory = new ManualResetEventSlim(initialState: false);
        using var secondReaderStarted = new ManualResetEventSlim(initialState: false);
        var rows = new MemoizedIndexedReadOnlyList<object>(
            1,
            _ =>
            {
                _ = Interlocked.Increment(ref invocationCount);
                factoryEntered.Set();
                releaseFactory.Wait(TestContext.Current.CancellationToken);
                return expected;
            });
        Task<object> firstReader = StartDedicatedReader(() => rows[0]);
        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Thread? secondReaderThread = null;
        Task<object> secondReader = StartDedicatedReader(() =>
        {
            Volatile.Write(ref secondReaderThread, Thread.CurrentThread);
            secondReaderStarted.Set();
            return rows[0];
        });

        try
        {
            Assert.True(secondReaderStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref secondReaderThread) is { } thread &&
                    (thread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)));
            Assert.Equal(1, Volatile.Read(ref invocationCount));
        }
        finally
        {
            releaseFactory.Set();
        }

        object[] results = await Task.WhenAll(firstReader, secondReader);

        Assert.Equal(1, invocationCount);
        Assert.Equal(1, rows.MaterializedCount);
        Assert.All(results, result => Assert.Same(expected, result));

        static Task<object> StartDedicatedReader(Func<object> reader)
        {
            return Task.Factory.StartNew(
                reader,
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }
}
