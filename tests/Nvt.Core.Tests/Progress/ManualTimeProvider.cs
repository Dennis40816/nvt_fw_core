// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Progress;

/// <summary>
/// A test clock that moves only when the test advances it. Due timers fire synchronously inside
/// <see cref="Advance"/>. Timers are one-shot; periods are ignored because the coordinator never uses them.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gets the due times requested through <see cref="CreateTimer"/>, in order.</summary>
    public List<TimeSpan> RequestedDueTimes { get; } = [];

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        RequestedDueTimes.Add(dueTime);
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that is now due.</summary>
    /// <param name="by">How far to move the clock.</param>
    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (ManualTimer timer in _timers.ToList())
        {
            timer.FireIfDue(_now);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            return true;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            if (_due is { } due && due <= now)
            {
                _due = null;
                callback(state);
            }
        }

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
