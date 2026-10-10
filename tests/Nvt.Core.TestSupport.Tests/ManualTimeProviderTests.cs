// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Verifies deterministic time, timer lifecycle and framework integrations.</summary>
public sealed class ManualTimeProviderTests
{
    private static readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(30);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TimeSpan Seconds(int value) => TimeSpan.FromSeconds(value);
    private static Task BoundedAsync(Task task) => task.WaitAsync(_bound, TimeProvider.System, Token);

    /// <summary>UTC and monotonic ticks use the supplied instant rather than the machine clock.</summary>
    [Fact]
    public void StartIsNormalizedToUtcAndElapsedTimeUsesTicks()
    {
        var start = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        var time = new ManualTimeProvider(start);
        Assert.Same(TimeZoneInfo.Utc, time.LocalTimeZone);
        Assert.Equal(_start, time.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, time.GetUtcNow().Offset);
        Assert.Equal(_start, time.GetLocalNow());
        Assert.Equal(TimeSpan.TicksPerSecond, time.TimestampFrequency);
        long before = time.GetTimestamp();
        Assert.Equal(0, before);
        TimeSpan delta = Seconds(3) + TimeSpan.FromTicks(7);
        time.Advance(delta);
        Assert.Equal(_start + delta, time.GetUtcNow());
        Assert.Equal(delta.Ticks, time.GetTimestamp());
        Assert.Equal(delta, time.GetElapsedTime(before, time.GetTimestamp()));
        Assert.Equal(delta, time.GetElapsedTime(before));
    }

    /// <summary>Every negative delta is rejected without moving time or firing timers.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(-10000L)]
    [InlineData(-10000000L)]
    public void BackwardAdvanceIsRejected(long ticks)
    {
        var time = new ManualTimeProvider(_start);
        int calls = 0;
        using ITimer timer = time.CreateTimer(_ => calls++, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        Assert.Equal("delta", Assert.Throws<ArgumentOutOfRangeException>(() => time.Advance(TimeSpan.FromTicks(ticks))).ParamName);
        Assert.Equal(_start, time.GetUtcNow());
        Assert.Equal(0, calls);
        time.Advance(TimeSpan.Zero);
        Assert.Equal(1, calls);
    }

    /// <summary>UTC range overflow is rejected before mutating the clock.</summary>
    [Fact]
    public void AdvancePastUtcRangeIsRejected()
    {
        var time = new ManualTimeProvider(DateTimeOffset.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => time.Advance(TimeSpan.FromTicks(1)));
        Assert.Equal(DateTimeOffset.MaxValue, time.GetUtcNow());
        Assert.Equal(0, time.GetTimestamp());
    }

    /// <summary>Large advances catch up every period, interleaved with one-shots in stable due order.</summary>
    [Fact]
    public void PeriodsAndOneShotsFireInDueAndSchedulingOrder()
    {
        var time = new ManualTimeProvider(_start);
        var events = new List<(string Name, long Ticks)>();
        using ITimer late = time.CreateTimer(_ => events.Add(("late", time.GetTimestamp())), null, Seconds(5), Timeout.InfiniteTimeSpan);
        using ITimer periodic = time.CreateTimer(_ => events.Add(("periodic", time.GetTimestamp())), null, Seconds(1), Seconds(2));
        using ITimer peer = time.CreateTimer(_ => events.Add(("peer", time.GetTimestamp())), null, Seconds(3), TimeSpan.Zero);
        time.Advance(Seconds(7));
        (string, long)[] expected =
        [
            ("periodic", Seconds(1).Ticks), ("peer", Seconds(3).Ticks), ("periodic", Seconds(3).Ticks),
            ("late", Seconds(5).Ticks), ("periodic", Seconds(5).Ticks), ("periodic", Seconds(7).Ticks),
        ];
        Assert.Equal(expected, events);
        Assert.Equal(_start + Seconds(7), time.GetUtcNow());
        time.Advance(Seconds(1));
        Assert.Equal(6, events.Count);
    }

    /// <summary>Both zero and infinite periods make one-shot timers; infinite due times disable timers.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OneShotAndDisabledTimers(int periodMilliseconds)
    {
        var time = new ManualTimeProvider(_start);
        object state = new();
        int calls = 0;
        using ITimer once = time.CreateTimer(value => { Assert.Same(state, value); calls++; }, state,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(periodMilliseconds));
        using ITimer disabled = time.CreateTimer(_ => throw new InvalidOperationException("Disabled timer fired."),
            null, Timeout.InfiniteTimeSpan, Seconds(1));
        Assert.Equal(0, calls);
        time.Advance(TimeSpan.Zero);
        time.Advance(Seconds(10));
        Assert.Equal(1, calls);
    }

    /// <summary>Change is relative to current time and can disable then re-enable a pending timer.</summary>
    [Fact]
    public void ChangeReplacesAndDisablesTheSchedule()
    {
        var time = new ManualTimeProvider(_start);
        var ticks = new List<long>();
        using ITimer timer = time.CreateTimer(_ => ticks.Add(time.GetTimestamp()), null, Seconds(1), Seconds(1));
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(timer.Change(Seconds(2), Seconds(3)));
        time.Advance(Seconds(2));
        Assert.Equal(Seconds(2).Ticks + TimeSpan.FromMilliseconds(500).Ticks, Assert.Single(ticks));
        Assert.True(timer.Change(Timeout.InfiniteTimeSpan, Seconds(1)));
        time.Advance(Seconds(10));
        Assert.Single(ticks);
        Assert.True(timer.Change(TimeSpan.Zero, TimeSpan.Zero));
        time.Advance(TimeSpan.Zero);
        Assert.Equal(2, ticks.Count);
    }

    /// <summary>Disposed timers cannot fire or be revived through Change, for either disposal form.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCancelsPendingPeriodsAndIsIdempotent(bool asynchronous)
    {
        var time = new ManualTimeProvider(_start);
        int calls = 0;
        ITimer timer = time.CreateTimer(_ => calls++, null, Seconds(1), Seconds(1));
        if (asynchronous) { await timer.DisposeAsync(); } else { timer.Dispose(); }
        timer.Dispose();
        await timer.DisposeAsync();
        Assert.False(timer.Change(TimeSpan.Zero, Seconds(1)));
        time.Advance(Seconds(10));
        Assert.Equal(0, calls);
    }

    /// <summary>A callback can create, change and cancel other timers due during the same advance.</summary>
    [Fact]
    public void CallbackCanMutateOtherSchedules()
    {
        var time = new ManualTimeProvider(_start);
        var events = new List<string>();
        using ITimer changed = time.CreateTimer(_ => events.Add("changed"), null, Seconds(2), Timeout.InfiniteTimeSpan);
        using ITimer canceled = time.CreateTimer(_ => events.Add("canceled"), null, Seconds(2), Seconds(1));
        ITimer? created = null;
        using ITimer first = time.CreateTimer(_ =>
        {
            events.Add("first");
            Assert.Equal(_start + Seconds(1), time.GetUtcNow());
            Assert.True(changed.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
            canceled.Dispose();
            created = time.CreateTimer(_ => events.Add("created"), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }, null, Seconds(1), Timeout.InfiniteTimeSpan);
        try
        {
            time.Advance(Seconds(10));
            string[] expected = ["first", "changed", "created"];
            Assert.Equal(expected, events);
        }
        finally { created?.Dispose(); }
    }

    /// <summary>Self-change replaces the pre-scheduled period; self-disposal cancels every remaining period.</summary>
    [Fact]
    public void PeriodicCallbackCanChangeAndDisposeItself()
    {
        var time = new ManualTimeProvider(_start);
        var ticks = new List<long>();
        ITimer? timer = null;
        timer = time.CreateTimer(_ =>
        {
            ticks.Add(time.GetTimestamp());
            if (ticks.Count == 1) { Assert.True(timer!.Change(Seconds(5), Seconds(2))); }
            else if (ticks.Count == 3) { timer!.Dispose(); }
        }, null, Seconds(1), Seconds(1));
        using (timer)
        {
            time.Advance(Seconds(20));
            long[] expected = [Seconds(1).Ticks, Seconds(6).Ticks, Seconds(8).Ticks];
            Assert.Equal(expected, ticks);
        }
    }

    /// <summary>Recursive advances fail while the enclosing advance still reaches its target.</summary>
    [Fact]
    public void CallbackCannotAdvanceRecursively()
    {
        var time = new ManualTimeProvider(_start);
        using ITimer timer = time.CreateTimer(_ =>
            Assert.Throws<InvalidOperationException>(() => time.Advance(TimeSpan.Zero)),
            null, Seconds(1), Timeout.InfiniteTimeSpan);
        time.Advance(Seconds(3));
        Assert.Equal(_start + Seconds(3), time.GetUtcNow());
        time.Advance(Seconds(1));
        Assert.Equal(_start + Seconds(4), time.GetUtcNow());
    }

    /// <summary>Another thread can read and create timers during a callback; concurrent advances are rejected.</summary>
    [Fact]
    public async Task CallbackDoesNotHoldTheClockLock()
    {
        var time = new ManualTimeProvider(_start);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using ITimer timer = time.CreateTimer(_ =>
        {
            entered.SetResult();
            // The finally block of the test always sets the event, so this wait needs no bound of its own.
            _ = release.WaitHandle.WaitOne();
        }, null, Seconds(1), Timeout.InfiniteTimeSpan);
        Task advance = Task.Run(() => time.Advance(Seconds(1)), Token);
        try
        {
            await BoundedAsync(entered.Task);
            Assert.Throws<InvalidOperationException>(() => time.Advance(TimeSpan.Zero));
            await BoundedAsync(Task.Run(() =>
            {
                Assert.Equal(_start + Seconds(1), time.GetUtcNow());
                using ITimer other = time.CreateTimer(static _ => { }, null, Seconds(2), Seconds(1));
                Assert.True(other.Change(Seconds(3), TimeSpan.Zero));
            }, Token));
        }
        finally
        {
            release.Set();
            await BoundedAsync(advance);
        }
    }

    /// <summary>Asynchronous timer disposal waits for a claimed callback and cancels its next period.</summary>
    [Fact]
    public async Task DisposeAsyncWaitsForAnActiveCallback()
    {
        var time = new ManualTimeProvider(_start);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int calls = 0;
        using ITimer timer = time.CreateTimer(_ =>
        {
            calls++;
            entered.SetResult();
            // The finally block of the test always sets the event, so this wait needs no bound of its own.
            _ = release.WaitHandle.WaitOne();
        }, null, Seconds(1), Seconds(1));
        Task advance = Task.Run(() => time.Advance(Seconds(5)), Token);
        Task? disposal = null;
        try
        {
            await BoundedAsync(entered.Task);
            disposal = timer.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.False(timer.Change(TimeSpan.Zero, Seconds(1)));
        }
        finally
        {
            release.Set();
            await BoundedAsync(advance);
        }
        await BoundedAsync(disposal!);
        Assert.Equal(1, calls);
    }

    /// <summary>A callback exception propagates without leaving the advance guard stuck.</summary>
    [Fact]
    public void CallbackFailureStopsAtDueTimeAndAllowsLaterAdvance()
    {
        var time = new ManualTimeProvider(_start);
        using ITimer timer = time.CreateTimer(_ => throw new IOException("callback"),
            null, Seconds(1), Timeout.InfiniteTimeSpan);
        Assert.Throws<IOException>(() => time.Advance(Seconds(3)));
        Assert.Equal(_start + Seconds(1), time.GetUtcNow());
        time.Advance(Seconds(2));
        Assert.Equal(_start + Seconds(3), time.GetUtcNow());
    }

    /// <summary>Invalid timer durations are rejected without replacing an existing valid schedule.</summary>
    [Theory]
    [InlineData(-1L, 0L, "dueTime")]
    [InlineData(0L, -1L, "period")]
    [InlineData(-20000L, 0L, "dueTime")]
    public void InvalidTimerChangeDoesNotMutateSchedule(long dueTicks, long periodTicks, string parameterName)
    {
        var time = new ManualTimeProvider(_start);
        int calls = 0;
        using ITimer timer = time.CreateTimer(_ => calls++, null, Seconds(1), TimeSpan.Zero);
        Assert.Equal(parameterName, Assert.Throws<ArgumentOutOfRangeException>(() =>
            timer.Change(TimeSpan.FromTicks(dueTicks), TimeSpan.FromTicks(periodTicks))).ParamName);
        Assert.Equal(parameterName, Assert.Throws<ArgumentOutOfRangeException>(() =>
            time.CreateTimer(static _ => { }, null, TimeSpan.FromTicks(dueTicks), TimeSpan.FromTicks(periodTicks))).ParamName);
        time.Advance(Seconds(1));
        Assert.Equal(1, calls);
    }

    /// <summary>Null timer callbacks are rejected explicitly.</summary>
    [Fact]
    public void NullCallbackIsRejected()
    {
        var time = new ManualTimeProvider(_start);
        Assert.Equal("callback", Assert.Throws<ArgumentNullException>(() =>
            time.CreateTimer(null!, null, TimeSpan.Zero, TimeSpan.Zero)).ParamName);
    }

    /// <summary>Task.Delay completes at the exact manual deadline.</summary>
    [Fact]
    public async Task DelayUsesManualTime()
    {
        var time = new ManualTimeProvider(_start);
        Task delay = Task.Delay(Seconds(2), time, Token);
        time.Advance(Seconds(1));
        Assert.False(delay.IsCompleted);
        time.Advance(Seconds(1));
        await BoundedAsync(delay);
        Assert.True(delay.IsCompletedSuccessfully);
    }

    /// <summary>Canceling a pending delay removes its timer and later advances remain safe.</summary>
    [Fact]
    public async Task PendingDelayCanBeCanceled()
    {
        var time = new ManualTimeProvider(_start);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task delay = Task.Delay(Seconds(2), time, cancellation.Token);
        await cancellation.CancelAsync();
        OperationCanceledException? canceled = null;
        try { await BoundedAsync(delay); } catch (OperationCanceledException exception) { canceled = exception; }
        Assert.NotNull(canceled);
        time.Advance(Seconds(10));
        Assert.True(delay.IsCanceled);
    }

    /// <summary>CTS deadlines use manual time; disposing a pending CTS cancels its timer.</summary>
    [Fact]
    public void CancellationTokenSourceUsesManualTimeAndCanBeDisposedWhilePending()
    {
        var time = new ManualTimeProvider(_start);
        using var source = new CancellationTokenSource(Seconds(2), time);
        using var pending = new CancellationTokenSource(Seconds(3), time);
        time.Advance(Seconds(1));
        Assert.False(source.IsCancellationRequested);
        pending.Dispose();
        time.Advance(Seconds(1));
        Assert.True(source.IsCancellationRequested);
        time.Advance(Seconds(10));
        Assert.False(pending.IsCancellationRequested);
    }

    /// <summary>The retained internal registration signal still observes near-term timers deterministically.</summary>
    [Fact]
    public async Task InternalPendingSignalExcludesDistantTimers()
    {
        var time = new ManualTimeProvider(_start);
        Task registered = time.WhenPendingAsync(1, Seconds(2), Token);
        using ITimer far = time.CreateTimer(static _ => { }, null, Seconds(10), TimeSpan.Zero);
        Assert.False(registered.IsCompleted);
        using ITimer near = time.CreateTimer(static _ => { }, null, Seconds(1), TimeSpan.Zero);
        await BoundedAsync(registered);
    }

    /// <summary>Two waiters can be pending together; the second call does not replace the first.</summary>
    [Fact]
    public async Task EveryPendingWaiterIsCompleted()
    {
        var time = new ManualTimeProvider(_start);
        Task first = time.WhenPendingAsync(1, Seconds(2), Token);
        Task second = time.WhenPendingAsync(2, Seconds(2), Token);
        using ITimer one = time.CreateTimer(static _ => { }, null, Seconds(1), TimeSpan.Zero);
        await BoundedAsync(first);
        Assert.False(second.IsCompleted);
        using ITimer two = time.CreateTimer(static _ => { }, null, Seconds(2), TimeSpan.Zero);
        await BoundedAsync(second);
    }

    /// <summary>A periodic re-arm into the window completes a waiter that no earlier timer could satisfy.</summary>
    [Fact]
    public async Task PeriodicRearmSignalsWaiter()
    {
        var time = new ManualTimeProvider(_start);
        using ITimer timer = time.CreateTimer(static _ => { }, null, Seconds(3), Seconds(1));
        Task waiter = time.WhenPendingAsync(1, Seconds(1), Token);
        Assert.False(waiter.IsCompleted);
        time.Advance(Seconds(3));
        await BoundedAsync(waiter);
    }

    /// <summary>A canceled wait for pending timers ends in cancellation without affecting the clock.</summary>
    [Fact]
    public async Task PendingWaitCanBeCanceled()
    {
        var time = new ManualTimeProvider(_start);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task waiter = time.WhenPendingAsync(1, Seconds(1), source.Token);
        await source.CancelAsync();
        OperationCanceledException? canceled = null;
        try { await BoundedAsync(waiter); } catch (OperationCanceledException exception) { canceled = exception; }
        Assert.NotNull(canceled);
        using ITimer timer = time.CreateTimer(static _ => { }, null, Seconds(1), TimeSpan.Zero);
        time.Advance(Seconds(1));
    }

    /// <summary>A period too large for the timestamp range fires once and then stops instead of wrapping.</summary>
    [Fact]
    public void PeriodBeyondTimestampRangeFiresOnce()
    {
        var time = new ManualTimeProvider(_start);
        int count = 0;
        using ITimer timer = time.CreateTimer(_ => count++, null, Seconds(1), TimeSpan.MaxValue);
        time.Advance(Seconds(10));
        time.Advance(Seconds(10));
        Assert.Equal(1, count);
    }

    /// <summary>A callback may start disposal of its own timer without waiting for it.</summary>
    [Fact]
    public async Task CallbackMayDisposeItsOwnTimerAsynchronously()
    {
        var time = new ManualTimeProvider(_start);
        ITimer? timer = null;
        ValueTask disposal = default;
        timer = time.CreateTimer(_ => disposal = timer!.DisposeAsync(), null, Seconds(1), Seconds(1));
        time.Advance(Seconds(3));
        await BoundedAsync(disposal.AsTask());
    }

    /// <summary>Preserved from the old clock: a one-shot timer disposed by an earlier callback in the same advance is not invoked.</summary>
    [Fact]
    public void TimerDisposedByEarlierCallbackDoesNotFire()
    {
        var time = new ManualTimeProvider(_start);
        int fired = 0;
        using ITimer victim = time.CreateTimer(_ => fired++, null, Seconds(2), TimeSpan.Zero);
        using ITimer killer = time.CreateTimer(_ => victim.Dispose(), null, Seconds(1), TimeSpan.Zero);
        time.Advance(Seconds(5));
        Assert.Equal(0, fired);
    }
}
