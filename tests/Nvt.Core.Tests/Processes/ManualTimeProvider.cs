// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Processes;

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance"/>. Timers fire on the advancing thread, in due
/// order, after the clock reaches their due time. Nothing here reads real time.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Guard: _gate protects _now, _timers and _waiter.
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _now;
    private PendingWaiter? _waiter;

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
        return Origin + TimeSpan.FromTicks(GetTimestamp());
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        _ = timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that became due, earliest first.</summary>
    internal void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        long target;
        lock (_gate)
        {
            target = _now + delta.Ticks;
        }

        while (true)
        {
            ManualTimer? due;
            lock (_gate)
            {
                due = _timers.Where(timer => timer.DueAt <= target).OrderBy(timer => timer.DueAt).FirstOrDefault();
                if (due is null)
                {
                    _now = target;
                    return;
                }

                _now = Math.Max(_now, due.DueAt);
                due.Fired();
            }

            due.Invoke();
        }
    }

    /// <summary>Completes when at least <paramref name="count"/> timers are waiting to fire within <paramref name="within"/>.</summary>
    /// <remarks>Timers further away, such as an execution timeout, are not counted.</remarks>
    internal Task WhenPendingAsync(int count, TimeSpan within, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (CountPending(within) >= count)
            {
                return Task.CompletedTask;
            }

            _waiter = new PendingWaiter(count, within);
            return _waiter.Source.Task.WaitAsync(cancellationToken);
        }
    }

    private int CountPending(TimeSpan within)
    {
        return _timers.Count(timer => timer.DueAt <= _now + within.Ticks);
    }

    private void Changed()
    {
        // Guard: called with _gate held.
        if (_waiter is { } waiter && CountPending(waiter.Within) >= waiter.Count)
        {
            _waiter = null;
            _ = waiter.Source.TrySetResult();
        }
    }

    private sealed record PendingWaiter(int Count, TimeSpan Within)
    {
        internal TaskCompletionSource Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private long _period;
        private bool _registered;

        internal long DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                _ = owner._timers.Remove(this);
                _registered = false;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = owner._now + dueTime.Ticks;
                    _period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                    owner._timers.Add(this);
                    _registered = true;
                    owner.Changed();
                }

                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                _ = owner._timers.Remove(this);
                _registered = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        // Guard: called with the owner's gate held.
        internal void Fired()
        {
            _ = owner._timers.Remove(this);
            if (_period > 0)
            {
                DueAt += _period;
                owner._timers.Add(this);
            }
        }

        internal void Invoke()
        {
            if (_registered || _period > 0)
            {
                callback(state);
            }
        }
    }
}
