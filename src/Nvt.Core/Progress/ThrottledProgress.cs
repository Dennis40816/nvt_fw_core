// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Progress;

/// <summary>Forwards progress at a minimum interval and drops reports between forwarded values.</summary>
/// <typeparam name="T">The progress value type.</typeparam>
public sealed class ThrottledProgress<T> : IProgress<T>
{
    private readonly IProgress<T> _target;
    private readonly TimeSpan _minimumInterval;
    private readonly Func<T, bool> _bypass;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private bool _hasForwarded;
    private long _lastTimestamp;

    /// <summary>Creates a progress reporter with a caller-selected interval and bypass condition.</summary>
    /// <param name="target">Receives each forwarded value.</param>
    /// <param name="minimumInterval">The minimum interval between reports that do not bypass the gate.</param>
    /// <param name="bypass">Returns true when a value must pass without waiting.</param>
    /// <param name="timeProvider">Measures elapsed time. A null value selects <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException">The target or bypass condition is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The interval is negative.</exception>
    public ThrottledProgress(
        IProgress<T> target,
        TimeSpan minimumInterval,
        Func<T, bool> bypass,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bypass);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumInterval, TimeSpan.Zero);

        _target = target;
        _minimumInterval = minimumInterval;
        _bypass = bypass;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Forwards the first value, bypassing values, and values whose minimum interval has elapsed.</summary>
    /// <param name="value">The progress value to report.</param>
    /// <remarks>
    /// Concurrent calls share the interval gate. The target runs outside the lock, so target calls can overlap.
    /// Dropped values are not saved or replayed.
    /// </remarks>
    public void Report(T value)
    {
        var bypass = _bypass(value);

        lock (_gate)
        {
            var timestamp = _timeProvider.GetTimestamp();
            if (!bypass && _hasForwarded &&
                _timeProvider.GetElapsedTime(_lastTimestamp, timestamp) < _minimumInterval)
            {
                return;
            }

            _hasForwarded = true;
            _lastTimestamp = timestamp;
        }

        _target.Report(value);
    }
}
