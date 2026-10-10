// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Pins the signal and its watchdog. The watchdog clock is manual, so no test waits in real time.</summary>
public sealed class SignalWaitTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(5);

    /// <summary>The default watchdog is 30 seconds.</summary>
    [Fact]
    public void DefaultWatchdogIsThirtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), SignalWait.DefaultWatchdog);
    }

    /// <summary>A signal that is set before the wait completes the wait at once.</summary>
    [Fact]
    public async Task WaitAsyncSetBeforeWaitCompletes()
    {
        var signal = new SignalWait("ready", Watchdog, new ManualTimeProvider(Start));
        Assert.False(signal.IsSet);

        Assert.True(signal.Set());
        await signal.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(signal.IsSet);
    }

    /// <summary>A pending wait completes when the signal is set later.</summary>
    [Fact]
    public async Task WaitAsyncSetAfterWaitStartsCompletes()
    {
        var signal = new SignalWait("ready", Watchdog, new ManualTimeProvider(Start));
        Task wait = signal.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        _ = signal.Set();

        await wait;
    }

    /// <summary>Only the first call to Set reports that it set the signal.</summary>
    [Fact]
    public void SetReportsOnlyTheFirstCall()
    {
        var signal = new SignalWait("ready", Watchdog, new ManualTimeProvider(Start));

        Assert.True(signal.Set());
        Assert.False(signal.Set());
    }

    /// <summary>When the watchdog expires, the wait throws a TimeoutException that names the signal.</summary>
    [Fact]
    public async Task WaitAsyncWatchdogExpiresThrowsTimeoutNamingTheSignal()
    {
        var clock = new ManualTimeProvider(Start);
        var signal = new SignalWait("reader stopped", Watchdog, clock);
        Task wait = signal.WaitAsync(TestContext.Current.CancellationToken);

        clock.Advance(Watchdog);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => wait);
        Assert.Contains("'reader stopped'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A wait that completed before the watchdog expires is not changed by the later expiry.</summary>
    [Fact]
    public async Task WaitAsyncSetBeforeTheWatchdogDoesNotTimeOut()
    {
        var clock = new ManualTimeProvider(Start);
        var signal = new SignalWait("ready", Watchdog, clock);
        Task wait = signal.WaitAsync(TestContext.Current.CancellationToken);

        _ = signal.Set();
        await wait;
        clock.Advance(Watchdog + Watchdog);

        Assert.True(wait.IsCompletedSuccessfully);
    }

    /// <summary>Cancellation surfaces as cancellation and is not mistaken for the watchdog.</summary>
    [Fact]
    public async Task WaitAsyncCanceledThrowsOperationCanceled()
    {
        var signal = new SignalWait("ready", Watchdog, new ManualTimeProvider(Start));
        using var cancellation = new CancellationTokenSource();
        Task wait = signal.WaitAsync(cancellation.Token);

        await cancellation.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    /// <summary>The static form passes the watched task's own failure through unchanged.</summary>
    [Fact]
    public async Task WaitAsyncStaticTaskFaultPassesThrough()
    {
        var source = new TaskCompletionSource();
        Task wait = SignalWait.WaitAsync(source.Task, "operation", Watchdog, new ManualTimeProvider(Start), TestContext.Current.CancellationToken);

        source.SetException(new InvalidOperationException("synthetic"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);
        Assert.Equal("synthetic", exception.Message);
    }

    /// <summary>The static form names the wait when the watchdog expires.</summary>
    [Fact]
    public async Task WaitAsyncStaticWatchdogExpiresThrowsTimeoutNamingTheWait()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new TaskCompletionSource();
        Task wait = SignalWait.WaitAsync(source.Task, "operation", Watchdog, clock, TestContext.Current.CancellationToken);

        clock.Advance(Watchdog);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => wait);
        Assert.Contains("'operation'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A watchdog must be finite and positive.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(-1)]
    public void ConstructorNonPositiveWatchdogThrows(int milliseconds)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SignalWait("ready", TimeSpan.FromMilliseconds(milliseconds)));
    }

    /// <summary>An infinite watchdog is rejected, because it would not prevent a hang.</summary>
    [Fact]
    public void ConstructorInfiniteWatchdogThrows()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SignalWait("ready", Timeout.InfiniteTimeSpan));
    }

    /// <summary>A signal needs a name for the watchdog message.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ConstructorEmptyNameThrows(string name)
    {
        _ = Assert.Throws<ArgumentException>(() => new SignalWait(name));
    }

    /// <summary>The static form validates its arguments.</summary>
    [Fact]
    public async Task WaitAsyncStaticInvalidArgumentsThrow()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => SignalWait.WaitAsync(null!, "operation", cancellationToken: TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => SignalWait.WaitAsync(Task.CompletedTask, " ", cancellationToken: TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SignalWait.WaitAsync(Task.CompletedTask, "operation", TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken));
    }
}
