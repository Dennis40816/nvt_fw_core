// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.Time;

/// <summary>Verifies delegate-based time reads and inherited time-provider behavior.</summary>
public sealed class DelegateTimeProviderTests
{
    /// <summary>The UTC-only constructor rejects a null delegate.</summary>
    [Fact]
    public void UtcNowConstructorRejectsNullDelegate()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new DelegateTimeProvider(null!));

        Assert.Equal("utcNow", error.ParamName);
    }

    /// <summary>The timestamp constructor rejects a null UTC delegate.</summary>
    [Fact]
    public void TimestampConstructorRejectsNullUtcNowDelegate()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new DelegateTimeProvider(null!, () => 1000, 1000));

        Assert.Equal("utcNow", error.ParamName);
    }

    /// <summary>The timestamp constructor rejects a null timestamp delegate.</summary>
    [Fact]
    public void TimestampConstructorRejectsNullTimestampDelegate()
    {
        var error = Assert.Throws<ArgumentNullException>(
            () => new DelegateTimeProvider(() => DateTimeOffset.UnixEpoch, null!, 1000));

        Assert.Equal("timestamp", error.ParamName);
    }

    /// <summary>The timestamp constructor rejects zero and negative frequencies.</summary>
    /// <param name="frequency">The invalid frequency to reject.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void TimestampConstructorRejectsNonPositiveFrequency(long frequency)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DelegateTimeProvider(() => DateTimeOffset.UnixEpoch, () => 1000, frequency));

        Assert.Equal("timestampFrequency", error.ParamName);
        Assert.Equal(frequency, error.ActualValue);
    }

    /// <summary>UTC reads preserve the value and offset and invoke the delegate exactly once per read.</summary>
    [Fact]
    public void GetUtcNowPreservesValueAndOffsetWithOneCallPerRead()
    {
        var expected = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.FromHours(5));
        var calls = 0;
        var provider = new DelegateTimeProvider(() =>
        {
            calls++;
            return expected;
        });

        Assert.Equal(0, calls);
        Assert.True(expected.EqualsExact(provider.GetUtcNow()));
        Assert.Equal(1, calls);
        Assert.True(expected.EqualsExact(provider.GetUtcNow()));
        Assert.Equal(2, calls);
    }

    /// <summary>Queue delegates return values in order and propagate the empty-queue exception.</summary>
    [Fact]
    public void GetUtcNowDequeuesValuesInOrder()
    {
        var start = DateTimeOffset.UnixEpoch;
        var second = start.AddSeconds(1);
        var third = start.AddSeconds(2);
        var queue = new Queue<DateTimeOffset>([start, second, third]);
        var provider = new DelegateTimeProvider(queue.Dequeue);

        Assert.Equal(3, queue.Count);
        Assert.Equal(start, provider.GetUtcNow());
        Assert.Equal(second, provider.GetUtcNow());
        Assert.Equal(third, provider.GetUtcNow());
        Assert.Empty(queue);
        Assert.Throws<InvalidOperationException>(() => provider.GetUtcNow());
    }

    /// <summary>A counter delegate advances its synthetic UTC value once per read.</summary>
    [Fact]
    public void GetUtcNowSupportsCounterDelegate()
    {
        var start = DateTimeOffset.UnixEpoch.AddDays(10);
        var calls = 0;
        var provider = new DelegateTimeProvider(() => start.AddSeconds(calls++));

        Assert.Equal(start, provider.GetUtcNow());
        Assert.Equal(start.AddSeconds(1), provider.GetUtcNow());
        Assert.Equal(start.AddSeconds(2), provider.GetUtcNow());
        Assert.Equal(3, calls);
    }

    /// <summary>Timestamp reads invoke only the timestamp delegate and expose the configured frequency.</summary>
    [Fact]
    public void GetTimestampUsesDelegateAndConfiguredFrequency()
    {
        var utcCalls = 0;
        var timestampCalls = 0;
        var provider = new DelegateTimeProvider(
            () =>
            {
                utcCalls++;
                return DateTimeOffset.UnixEpoch;
            },
            () => 2500 + timestampCalls++,
            1000);

        Assert.Equal(0, utcCalls);
        Assert.Equal(0, timestampCalls);
        Assert.Equal(1000L, provider.TimestampFrequency);
        Assert.Equal(0, timestampCalls);
        Assert.Equal(2500L, provider.GetTimestamp());
        Assert.Equal(1, timestampCalls);
        Assert.Equal(2501L, provider.GetTimestamp());
        Assert.Equal(2, timestampCalls);
        Assert.Equal(0, utcCalls);
    }

    /// <summary>Elapsed time between explicit timestamps uses the configured frequency without reading delegates.</summary>
    [Fact]
    public void GetElapsedTimeUsesConfiguredFrequency()
    {
        var provider = new DelegateTimeProvider(
            () => throw new InvalidOperationException("Unexpected UTC read."),
            () => throw new InvalidOperationException("Unexpected timestamp read."),
            1000);

        Assert.Equal(TimeSpan.FromSeconds(1.5), provider.GetElapsedTime(1000, 2500));
    }

    /// <summary>Elapsed time from a starting timestamp obtains the ending timestamp with one delegate call.</summary>
    [Fact]
    public void GetElapsedTimeFromStartReadsTimestampOnce()
    {
        var calls = 0;
        var provider = new DelegateTimeProvider(
            () => throw new InvalidOperationException("Unexpected UTC read."),
            () =>
            {
                calls++;
                return 2500;
            },
            1000);

        Assert.Equal(TimeSpan.FromSeconds(1.5), provider.GetElapsedTime(1000));
        Assert.Equal(1, calls);
    }

    /// <summary>The UTC-only constructor uses nondecreasing system stopwatch timestamps and frequency.</summary>
    [Fact]
    public void DefaultTimestampUsesStopwatch()
    {
        var provider = new DelegateTimeProvider(() => throw new InvalidOperationException("Unexpected UTC read."));
        var before = Stopwatch.GetTimestamp();

        var first = provider.GetTimestamp();
        var second = provider.GetTimestamp();

        var after = Stopwatch.GetTimestamp();
        Assert.InRange(first, before, after);
        Assert.InRange(second, first, after);
        Assert.Equal(Stopwatch.Frequency, provider.TimestampFrequency);
    }

    /// <summary>Inherited local reads preserve the UTC instant and use the base system time zone.</summary>
    [Fact]
    public void GetLocalNowUsesBaseLocalTimeZone()
    {
        var value = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var calls = 0;
        var provider = new DelegateTimeProvider(() =>
        {
            calls++;
            return value;
        });
        var expected = TimeZoneInfo.ConvertTime(provider.GetUtcNow(), provider.LocalTimeZone);

        var actual = provider.GetLocalNow();

        Assert.Equal(expected.UtcDateTime, actual.UtcDateTime);
        Assert.Equal(TimeProvider.System.LocalTimeZone, provider.LocalTimeZone);
        Assert.Equal(2, calls);
    }

    /// <summary>A derived helper passes a delegate that observes its mutable state.</summary>
    [Fact]
    public void DerivedProviderObservesMutableState()
    {
        var start = DateTimeOffset.UnixEpoch.AddDays(20);
        var provider = new StatefulTimeProvider(start);

        Assert.Equal(0, provider.Calls);
        Assert.Equal(start, provider.GetUtcNow());
        Assert.Equal(1, provider.Calls);
        provider.Start = start.AddHours(1);
        Assert.Equal(start.AddHours(1).AddSeconds(1), provider.GetUtcNow());
        Assert.Equal(2, provider.Calls);
    }

    private sealed class StatefulTimeProvider : DelegateTimeProvider
    {
        private readonly ClockState state;

        internal StatefulTimeProvider(DateTimeOffset start)
            : this(new ClockState { Start = start })
        {
        }

        private StatefulTimeProvider(ClockState state)
            : base(() => state.Start.AddSeconds(state.Calls++))
        {
            this.state = state;
        }

        internal int Calls => state.Calls;

        internal DateTimeOffset Start
        {
            get => state.Start;
            set => state.Start = value;
        }

        private sealed class ClockState
        {
            internal DateTimeOffset Start { get; set; }

            internal int Calls { get; set; }
        }
    }
}
