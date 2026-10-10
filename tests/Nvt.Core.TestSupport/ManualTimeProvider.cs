// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.TestSupport;

/// <summary>A UTC clock whose timers run synchronously, in due order, when <see cref="Advance"/> is called.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    // Guard: _gate protects the timestamp, timer schedules, advancing flag and pending waiters.
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<PendingWaiter> _waiters = [];
    private readonly DateTimeOffset _start;
    private readonly long _maximumTimestamp;
    private long _now;
    private bool _advancing;

    /// <summary>Creates a clock at <paramref name="start"/>, normalized to UTC, with timestamp zero.</summary>
    public ManualTimeProvider(DateTimeOffset start)
    {
        _start = start.ToUniversalTime();
        _maximumTimestamp = DateTimeOffset.MaxValue.UtcTicks - _start.UtcTicks;
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _start.AddTicks(_now);
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        _ = timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves time forward, firing once for every elapsed timer period at its due timestamp.</summary>
    /// <remarks>
    /// Callbacks run outside the clock lock and may read time or create, change and dispose timers.
    /// Recursive or concurrent advances throw <see cref="InvalidOperationException"/>.
    /// Equal due times follow scheduling order. A callback exception propagates and stops time at that callback's due time.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The delta is negative or exceeds the UTC date range.</exception>
    /// <exception cref="InvalidOperationException">Another advance is still executing.</exception>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        long target;
        lock (_gate)
        {
            if (_advancing)
            {
                throw new InvalidOperationException("Advance cannot run recursively or concurrently.");
            }
            if (delta.Ticks > _maximumTimestamp - _now)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "The advance exceeds the UTC date range.");
            }
            target = _now + delta.Ticks;
            _advancing = true;
        }

        try
        {
            while (true)
            {
                ManualTimer? due = null;
                lock (_gate)
                {
                    foreach (ManualTimer timer in _timers)
                    {
                        if (timer.DueAt <= target && (due is null || timer.DueAt < due.DueAt))
                        {
                            due = timer;
                        }
                    }
                    if (due is null)
                    {
                        _now = target;
                        return;
                    }
                    _now = due.DueAt;
                    due.Fired();
                }
                due.Invoke();
            }
        }
        finally
        {
            lock (_gate)
            {
                _advancing = false;
            }
        }
    }

    // Preserve the PR #147 registration signal for the representative Core process tests.
    internal Task WhenPendingAsync(int count, TimeSpan within, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (CountPending(within) >= count)
            {
                return Task.CompletedTask;
            }
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiter = new PendingWaiter(count, within, source);
            _waiters.Add(waiter);
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(static state =>
                {
                    (ManualTimeProvider owner, PendingWaiter pending, CancellationToken token) = ((ManualTimeProvider, PendingWaiter, CancellationToken))state!;
                    lock (owner._gate)
                    {
                        _ = owner._waiters.Remove(pending);
                    }
                    _ = pending.Source.TrySetCanceled(token);
                }, (this, waiter, cancellationToken));
                // Release the token registration once the waiter completes; the token may outlive the provider.
                _ = waiter.Source.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                    registration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return source.Task;
        }
    }

    private int CountPending(TimeSpan within)
    {
        long end = _now + Math.Min(within.Ticks, long.MaxValue - _now);
        return _timers.Count(timer => timer.DueAt <= end);
    }

    private void Changed()
    {
        // Guard: called with _gate held. Continuations cannot execute inside this lock.
        for (int index = _waiters.Count - 1; index >= 0; index--)
        {
            PendingWaiter waiter = _waiters[index];
            if (CountPending(waiter.Within) >= waiter.Count)
            {
                _waiters.RemoveAt(index);
                _ = waiter.Source.TrySetResult();
            }
        }
    }

    private static void ValidateTimeout(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Use a nonnegative timeout or Timeout.InfiniteTimeSpan.");
        }
    }

    private sealed record PendingWaiter(int Count, TimeSpan Within, TaskCompletionSource Source);

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private long _period;
        private bool _disposed;
        private bool _invoking;
        private TaskCompletionSource? _disposeCompletion;

        internal long DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimeout(dueTime, nameof(dueTime));
            ValidateTimeout(period, nameof(period));
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }
                if (dueTime.Ticks > long.MaxValue - owner._now)
                {
                    throw new ArgumentOutOfRangeException(nameof(dueTime), "The due timestamp exceeds the timestamp range.");
                }
                _ = owner._timers.Remove(this);
                _period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = owner._now + dueTime.Ticks;
                    owner._timers.Add(this);
                    owner.Changed();
                }
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                _disposed = true;
                _ = owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (owner._gate)
            {
                Dispose();
                if (!_invoking)
                {
                    return ValueTask.CompletedTask;
                }
                _disposeCompletion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return new ValueTask(_disposeCompletion.Task);
            }
        }

        internal void Fired()
        {
            // Guard: selection, rescheduling and claiming this callback occur under the owner's gate.
            _ = owner._timers.Remove(this);
            _invoking = true;
            if (_period > 0 && _period <= long.MaxValue - DueAt)
            {
                DueAt += _period;
                owner._timers.Add(this);
                owner.Changed();
            }
        }

        internal void Invoke()
        {
            try
            {
                callback(state);
            }
            finally
            {
                lock (owner._gate)
                {
                    _invoking = false;
                    _ = _disposeCompletion?.TrySetResult();
                }
            }
        }
    }
}
