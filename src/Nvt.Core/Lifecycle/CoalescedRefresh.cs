// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Lifecycle;

/// <summary>
/// Schedules a refresh at most once until that refresh starts running. A burst of change notifications
/// then costs one refresh instead of one per notification; the refresh is expected to read the latest
/// state itself.
/// </summary>
public sealed class CoalescedRefresh
{
    private readonly Action<Action> _schedule;
    private readonly Action _refresh;
    private int _isScheduled;

    /// <summary>Creates a refresh coordinator with caller-supplied scheduling and refresh actions.</summary>
    /// <param name="schedule">Schedules the supplied callback.</param>
    /// <param name="refresh">Reads the latest state and performs the refresh.</param>
    public CoalescedRefresh(Action<Action> schedule, Action refresh)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
    }

    /// <summary>Requests a refresh. Safe to call from any thread.</summary>
    public void Request()
    {
        if (Interlocked.Exchange(ref _isScheduled, 1) == 1)
        {
            return;
        }

        _schedule(Run);
    }

    /// <summary>
    /// Lets the next request schedule again. A refresh that is already scheduled is not cancelled; it
    /// still runs, so the refresh callback must tolerate running after its owner has let go.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref _isScheduled, 0);
    }

    private void Run()
    {
        // Cleared before refreshing: a change that arrives while the refresh runs must schedule another.
        Volatile.Write(ref _isScheduled, 0);
        _refresh();
    }
}
