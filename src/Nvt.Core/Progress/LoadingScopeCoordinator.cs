// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Progress;

/// <summary>
/// Coordinates loading visibility scopes with an explicit lifecycle contract:
/// show on first enter, keep visible for at least a minimum duration, and hide
/// only after an optional UI frame-yield boundary.
/// </summary>
/// <remarks>
/// A lock guards only the scope count. The visibility callbacks run outside it, so callers must serialize
/// <see cref="Begin"/> and <see cref="EndAsync"/>, for example by calling them on the UI thread.
/// </remarks>
public sealed class LoadingScopeCoordinator
{
    private readonly Func<Task> _yieldFrameAsync;
    private readonly TimeSpan _minimumVisibleDuration;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private int _scopeCount;
    private DateTimeOffset _visibleSinceUtc;

    /// <summary>Creates a coordinator for one loading surface.</summary>
    /// <param name="yieldFrameAsync">Yields to the UI so a visibility change can render.</param>
    /// <param name="minimumVisibleDuration">The minimum time the surface stays visible. A negative value means zero.</param>
    /// <param name="timeProvider">The clock and timer source. Null means <see cref="TimeProvider.System"/>.</param>
    public LoadingScopeCoordinator(
        Func<Task> yieldFrameAsync,
        TimeSpan minimumVisibleDuration,
        TimeProvider? timeProvider = null)
    {
        _yieldFrameAsync = yieldFrameAsync ?? throw new ArgumentNullException(nameof(yieldFrameAsync));
        _minimumVisibleDuration = minimumVisibleDuration < TimeSpan.Zero
            ? TimeSpan.Zero
            : minimumVisibleDuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Runs an action inside one loading scope: begin, yield a frame, run, then end.</summary>
    /// <param name="action">The work to run while the surface is visible.</param>
    /// <param name="setVisible">Shows or hides the surface.</param>
    /// <returns>A task that completes after the scope ends.</returns>
    public async Task RunAsync(Func<Task> action, Action<bool> setVisible)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(setVisible);

        Begin(setVisible);
        try
        {
            await _yieldFrameAsync();
            await action();
        }
        finally
        {
            await EndAsync(setVisible);
        }
    }

    /// <summary>Enters a scope. The first open scope shows the surface.</summary>
    /// <param name="setVisible">Shows or hides the surface.</param>
    public void Begin(Action<bool> setVisible)
    {
        ArgumentNullException.ThrowIfNull(setVisible);

        var shouldShow = false;
        lock (_sync)
        {
            _scopeCount++;
            if (_scopeCount == 1)
            {
                _visibleSinceUtc = _timeProvider.GetUtcNow();
                shouldShow = true;
            }
        }

        if (shouldShow)
        {
            setVisible(true);
        }
    }

    /// <summary>
    /// Leaves a scope. When the last scope ends, waits for the rest of the minimum visible time, yields a
    /// frame, and hides the surface unless a scope is open at that moment.
    /// </summary>
    /// <remarks>
    /// The check after the wait looks only at the current scope count. If a newer scope began and also
    /// ended during the wait, this call still hides at its own time, before the newer scope's minimum
    /// visible time has passed. The newer call then hides again when its own wait ends.
    /// </remarks>
    /// <param name="setVisible">Shows or hides the surface.</param>
    /// <returns>A task that completes after the surface is hidden or the hide is skipped.</returns>
    public async Task EndAsync(Action<bool> setVisible)
    {
        ArgumentNullException.ThrowIfNull(setVisible);

        DateTimeOffset visibleSinceUtc = default;
        var shouldHide = false;
        lock (_sync)
        {
            if (_scopeCount <= 0)
            {
                return;
            }

            _scopeCount--;
            if (_scopeCount == 0)
            {
                visibleSinceUtc = _visibleSinceUtc;
                shouldHide = true;
            }
        }

        if (!shouldHide)
        {
            return;
        }

        var elapsed = _timeProvider.GetUtcNow() - visibleSinceUtc;
        var remaining = _minimumVisibleDuration - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, _timeProvider);
        }

        await _yieldFrameAsync();

        lock (_sync)
        {
            if (_scopeCount > 0)
            {
                return;
            }
        }

        setVisible(false);
        await _yieldFrameAsync();
    }
}
