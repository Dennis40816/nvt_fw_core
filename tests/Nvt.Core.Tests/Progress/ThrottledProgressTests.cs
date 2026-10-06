// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.Progress;
using Xunit;

namespace Nvt.Core.Tests.Progress;

/// <summary>Characterizes the frozen interval gate with a manual clock and synchronous targets.</summary>
public sealed class ThrottledProgressTests
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The first report passes even when the clock starts at zero.</summary>
    [Fact]
    public void FirstReportPassesAtTimestampZero()
    {
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), MinimumInterval, static _ => false, new ManualTimeProvider());

        progress.Report("first");

        Assert.Equal<string>(["first"], values);
    }

    /// <summary>A report at 119 milliseconds drops, and a report at exactly 120 milliseconds passes.</summary>
    [Fact]
    public void ReportsPassAtTheExactIntervalBoundary()
    {
        var clock = new ManualTimeProvider();
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), MinimumInterval, static _ => false, clock);

        progress.Report("first");
        clock.Advance(TimeSpan.FromMilliseconds(119));
        progress.Report("dropped");
        Assert.Equal<string>(["first"], values);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        progress.Report("boundary");

        Assert.Equal<string>(["first", "boundary"], values);

        clock.Advance(TimeSpan.FromMilliseconds(119));
        progress.Report("dropped after boundary");
        Assert.Equal<string>(["first", "boundary"], values);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        progress.Report("second boundary");

        Assert.Equal<string>(["first", "boundary", "second boundary"], values);
    }

    /// <summary>Elapsed time uses the provider's timestamp frequency, including fractions of a millisecond.</summary>
    [Fact]
    public void ReportsUseTimestampPrecision()
    {
        var clock = new ManualTimeProvider();
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), MinimumInterval, static _ => false, clock);

        progress.Report("first");
        clock.Advance(MinimumInterval - TimeSpan.FromTicks(1));
        progress.Report("dropped");
        Assert.Equal<string>(["first"], values);

        clock.Advance(TimeSpan.FromTicks(1));
        progress.Report("boundary");

        Assert.Equal<string>(["first", "boundary"], values);
    }

    /// <summary>A final report always passes and starts the next interval.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(119)]
    [InlineData(120)]
    [InlineData(500)]
    public void FinalReportPassesAndResetsTheInterval(int elapsedMilliseconds)
    {
        var clock = new ManualTimeProvider();
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), MinimumInterval, static value => value == "final", clock);

        progress.Report("first");
        clock.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        progress.Report("final");
        progress.Report("dropped immediately");
        clock.Advance(TimeSpan.FromMilliseconds(119));
        progress.Report("dropped before boundary");
        Assert.Equal<string>(["first", "final"], values);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        progress.Report("boundary");

        Assert.Equal<string>(["first", "final", "boundary"], values);
    }

    /// <summary>A dropped report neither moves the interval nor appears in a later delivery.</summary>
    [Fact]
    public void DroppedReportsDoNotResetTheIntervalOrReplay()
    {
        var clock = new ManualTimeProvider();
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), MinimumInterval, static _ => false, clock);

        progress.Report("first");
        clock.Advance(TimeSpan.FromMilliseconds(60));
        progress.Report("dropped");
        clock.Advance(TimeSpan.FromMilliseconds(60));
        progress.Report("boundary");
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal<string>(["first", "boundary"], values);
    }

    /// <summary>A zero interval forwards all values without advancing the clock.</summary>
    [Fact]
    public void ZeroIntervalForwardsEveryReport()
    {
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), TimeSpan.Zero,
            static value => value == "final", new ManualTimeProvider());

        progress.Report("first");
        progress.Report("second");
        progress.Report("final");
        progress.Report("third");

        Assert.Equal<string>(["first", "second", "final", "third"], values);
    }

    /// <summary>The caller rejects invalid reports before the throttle, including final reports.</summary>
    [Fact]
    public void CallerValidityCheckRejectsFinalReportsWithoutResettingTheInterval()
    {
        var clock = new ManualTimeProvider();
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), MinimumInterval, static value => value == "final", clock);
        var isValid = true;
        var caller = new CallbackProgress<string>(value =>
        {
            if (isValid)
            {
                progress.Report(value);
            }
        });

        caller.Report("first");
        clock.Advance(TimeSpan.FromMilliseconds(60));
        isValid = false;
        caller.Report("final");
        clock.Advance(TimeSpan.FromMilliseconds(60));
        isValid = true;
        caller.Report("boundary");

        Assert.Equal<string>(["first", "boundary"], values);
    }

    /// <summary>Concurrent calls forward one non-bypassing value per elapsed interval.</summary>
    [Fact]
    public async Task ConcurrentReportsShareTheIntervalGate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new ManualTimeProvider();
        var values = new ConcurrentQueue<int>();
        var progress = new ThrottledProgress<int>(
            new CallbackProgress<int>(values.Enqueue), MinimumInterval, static _ => false, clock);

        await ReportBatchAsync();
        Assert.Single(values);

        clock.Advance(TimeSpan.FromMilliseconds(119));
        await ReportBatchAsync();
        Assert.Single(values);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await ReportBatchAsync();
        Assert.Equal(2, values.Count);

        async Task ReportBatchAsync()
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reports = Enumerable.Range(0, 32).Select(value => Task.Run(async () =>
            {
                await start.Task.WaitAsync(cancellationToken);
                progress.Report(value);
            }, cancellationToken)).ToArray();
            start.SetResult();
            await Task.WhenAll(reports).WaitAsync(TestTimeout, cancellationToken);
        }
    }

    /// <summary>A blocked target lets another thread drop or forward through the gate.</summary>
    [Fact]
    public async Task TargetRunsOutsideTheGateLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new ManualTimeProvider();
        var values = new ConcurrentQueue<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var target = new CallbackProgress<string>(value =>
        {
            values.Enqueue(value);
            if (value == "first")
            {
                entered.SetResult();
                if (!release.Wait(TestTimeout, cancellationToken))
                {
                    throw new TimeoutException("The target was not released.");
                }
            }
        });
        var progress = new ThrottledProgress<string>(target, MinimumInterval, static _ => false, clock);
        var firstReport = Task.Run(() => progress.Report("first"), cancellationToken);

        try
        {
            await entered.Task.WaitAsync(TestTimeout, cancellationToken);
            await Task.Run(() => progress.Report("dropped"), cancellationToken).WaitAsync(TestTimeout, cancellationToken);
            clock.Advance(MinimumInterval);
            await Task.Run(() => progress.Report("next"), cancellationToken).WaitAsync(TestTimeout, cancellationToken);

            Assert.Equal<string>(["first", "next"], values);
        }
        finally
        {
            release.Set();
            await firstReport.WaitAsync(TestTimeout, cancellationToken);
        }
    }

    /// <summary>A null clock selects the system provider.</summary>
    [Fact]
    public void NullTimeProviderUsesTheDefaultClock()
    {
        var values = new List<string>();
        var progress = new ThrottledProgress<string>(
            new CallbackProgress<string>(values.Add), MinimumInterval, static _ => false, timeProvider: null);

        progress.Report("first");

        Assert.Equal<string>(["first"], values);
    }

    /// <summary>A null target fails during construction.</summary>
    [Fact]
    public void ConstructorRejectsNullTarget()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ThrottledProgress<int>(null!, MinimumInterval, static _ => false));

        Assert.Equal("target", exception.ParamName);
    }

    /// <summary>A null bypass condition fails during construction.</summary>
    [Fact]
    public void ConstructorRejectsNullBypass()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ThrottledProgress<int>(new CallbackProgress<int>(static _ => { }), MinimumInterval, null!));

        Assert.Equal("bypass", exception.ParamName);
    }

    /// <summary>Any negative interval fails during construction.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-1200000)]
    public void ConstructorRejectsNegativeInterval(long ticks)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ThrottledProgress<int>(
                new CallbackProgress<int>(static _ => { }), TimeSpan.FromTicks(ticks), static _ => false));

        Assert.Equal("minimumInterval", exception.ParamName);
    }

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        // Use a frequency distinct from TimeSpan ticks to check elapsed-time conversion.
        public override long TimestampFrequency => 2 * TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks * 2);
    }
}
