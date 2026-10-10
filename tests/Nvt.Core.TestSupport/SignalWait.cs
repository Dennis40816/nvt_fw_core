// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.TestSupport;

/// <summary>A one-shot signal that a test sets and waits for, with a watchdog that only prevents a hang.</summary>
/// <remarks>
/// Use it instead of <c>Task.Delay</c> or <c>Thread.Sleep</c>. The watchdog is a long limit, never an assertion:
/// a result must not depend on how fast the signal arrives. The watchdog clock is <see cref="TimeProvider.System"/>
/// unless a test passes another clock, for example a <see cref="ManualTimeProvider"/>.
/// </remarks>
public sealed class SignalWait
{
    /// <summary>The default watchdog, long enough for a loaded CI machine.</summary>
    public static readonly TimeSpan DefaultWatchdog = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _name;
    private readonly TimeSpan _watchdog;
    private readonly TimeProvider _clock;

    /// <summary>Creates an unset signal.</summary>
    /// <param name="name">Names the signal in the watchdog message.</param>
    /// <param name="watchdog">A finite, positive limit. <see langword="null"/> means <see cref="DefaultWatchdog"/>.</param>
    /// <param name="clock">The watchdog clock. <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The watchdog is not finite and positive.</exception>
    public SignalWait(string name, TimeSpan? watchdog = null, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        _watchdog = ValidateWatchdog(watchdog);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Gets whether <see cref="Set"/> has been called.</summary>
    public bool IsSet => _signal.Task.IsCompleted;

    /// <summary>Sets the signal. Waiters continue on another thread, never inside this call.</summary>
    /// <returns><see langword="true"/> for the call that set it; <see langword="false"/> when it was already set.</returns>
    public bool Set() => _signal.TrySetResult();

    /// <summary>Waits until the signal is set, the token is canceled, or the watchdog expires.</summary>
    /// <exception cref="TimeoutException">The watchdog expired. The message names the signal.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        WaitUnderWatchdogAsync(_signal.Task, _name, _watchdog, _clock, cancellationToken);

    /// <summary>Waits for a task under the watchdog. The task's own failure or cancellation passes through unchanged.</summary>
    /// <param name="task">The task to wait for.</param>
    /// <param name="name">Names the wait in the watchdog message.</param>
    /// <param name="watchdog">A finite, positive limit. <see langword="null"/> means <see cref="DefaultWatchdog"/>.</param>
    /// <param name="clock">The watchdog clock. <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="TimeoutException">The watchdog expired. The message names the wait.</exception>
    public static async Task WaitAsync(
        Task task,
        string name,
        TimeSpan? watchdog = null,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await WaitUnderWatchdogAsync(task, name, ValidateWatchdog(watchdog), clock ?? TimeProvider.System, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitUnderWatchdogAsync(Task task, string name, TimeSpan watchdog, TimeProvider clock, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(watchdog, clock, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception) when (!task.IsCompleted)
        {
            throw new TimeoutException($"Watchdog: '{name}' did not complete within {watchdog}.", exception);
        }
    }

    private static TimeSpan ValidateWatchdog(TimeSpan? watchdog)
    {
        TimeSpan value = watchdog ?? DefaultWatchdog;
        // Timeout.InfiniteTimeSpan is negative, so this also rejects "no limit".
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(watchdog));
        return value;
    }
}
