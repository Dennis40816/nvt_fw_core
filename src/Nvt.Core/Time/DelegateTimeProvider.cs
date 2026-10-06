// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.Time;

/// <summary>Adapts delegates to a <see cref="TimeProvider" /> for tests and legacy time sources.</summary>
/// <remarks>
/// Each UTC read invokes the supplied delegate once and returns its value unchanged, without caching.
/// Timers and the local time zone follow the base class, which uses the system.
/// </remarks>
public class DelegateTimeProvider : TimeProvider
{
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<long> timestamp;

    /// <summary>Creates a provider with a delegate for UTC reads and system stopwatch timestamps.</summary>
    /// <param name="utcNow">The delegate invoked once for each UTC read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utcNow" /> is null.</exception>
    public DelegateTimeProvider(Func<DateTimeOffset> utcNow)
        : this(utcNow, Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    /// <summary>Creates a provider with delegates for UTC reads and timestamps.</summary>
    /// <param name="utcNow">The delegate invoked once for each UTC read.</param>
    /// <param name="timestamp">The delegate invoked once for each timestamp read.</param>
    /// <param name="timestampFrequency">The positive number of timestamp ticks per second.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="utcNow" /> or <paramref name="timestamp" /> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timestampFrequency" /> is zero or negative.</exception>
    public DelegateTimeProvider(Func<DateTimeOffset> utcNow, Func<long> timestamp, long timestampFrequency)
    {
        ArgumentNullException.ThrowIfNull(utcNow);
        ArgumentNullException.ThrowIfNull(timestamp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);

        this.utcNow = utcNow;
        this.timestamp = timestamp;
        TimestampFrequency = timestampFrequency;
    }

    /// <summary>Gets the supplied UTC value without changing its offset.</summary>
    /// <returns>The value returned by one invocation of the UTC delegate.</returns>
    public override DateTimeOffset GetUtcNow() => utcNow();

    /// <summary>Gets the timestamp from the configured delegate or system stopwatch.</summary>
    /// <returns>The value returned by one invocation of the timestamp delegate.</returns>
    public override long GetTimestamp() => timestamp();

    /// <summary>Gets the configured number of timestamp ticks per second.</summary>
    public override long TimestampFrequency { get; }
}
