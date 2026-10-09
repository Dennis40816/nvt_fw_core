// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;

namespace Nvt.Core.Processes;

public sealed partial class SystemExternalProcessRunner
{
    private enum TerminalSignal
    {
        NaturalExit,
        ExitObservationFailed,
        Timeout,
        Cancellation,
    }

    /// <summary>
    /// Custody of one invocation: the process handle, both stream readers, the exit observation and the single
    /// termination work item. Work that has not settled when the deadline ends is detached, never awaited again by
    /// the host; this custody keeps ownership of it and disposes every handle only after it settles, observing any
    /// late fault. Nothing a background task throws reaches the caller.
    /// </summary>
#pragma warning disable CA1001 // Release() owns disposal and defers it until detached work settles; a synchronous
    // Dispose would break that custody contract.
    private sealed class Invocation(Process process, ExternalProcessRunnerSeams seams)
#pragma warning restore CA1001
    {
        private readonly CancellationTokenSource _readerStop = new();
        private readonly CancellationTokenSource _exitObservationStop = new();
        private readonly TaskCompletionSource _cancellationSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private readonly List<Task> _background = [];
        private Task<BoundedProcessOutput> _stdout = Task.FromResult(new BoundedProcessOutput(string.Empty, false));
        private Task<BoundedProcessOutput> _stderr = Task.FromResult(new BoundedProcessOutput(string.Empty, false));
        // Until the observation starts, the exit counts as unobserved, so Release still requests termination.
        private Task _exit = new TaskCompletionSource().Task;
        private Task<bool>? _termination;
        private int _released;
        private int _finished;

        internal async Task<ExternalProcessResult> ExecuteAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            _stdout = Track(seams.Drain(process.StandardOutput, _readerStop.Token));
            _stderr = Track(seams.Drain(process.StandardError, _readerStop.Token));
            _exit = Track(seams.ObserveExit(process, _exitObservationStop.Token));

            // The callback only signals. It never touches the process, so Cancel() returns at
            // once on the canceller's thread, and disposing the registration never waits for a termination.
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                _cancellationSignal);
            Notify(ExternalProcessRunnerPhase.Started);

            TerminalSignal signal = await WaitForTerminalSignalAsync(timeout, cancellationToken).ConfigureAwait(false);
            CleanupSchedule schedule = seams.Timing.Schedule(seams.Time.GetTimestamp(), seams.Time.TimestampFrequency);
            Task streams = Settled(_stdout, _stderr);

            bool heldAfterExit = false;
            if (signal == TerminalSignal.NaturalExit)
            {
                await WaitUntilAsync(
                        Task.WhenAny(streams, _cancellationSignal.Task),
                        schedule.HeldOutputGraceAt)
                    .ConfigureAwait(false);
                if (!streams.IsCompleted)
                {
                    heldAfterExit = !_cancellationSignal.Task.IsCompleted;
                    if (heldAfterExit)
                    {
                        Notify(ExternalProcessRunnerPhase.OutputHeldAfterExit);
                    }

                    // Stop descendants reachable through the tree walk; the exited child's open handle keeps its
                    // process ID from being reused.
                    StartTermination();
                }
            }
            else
            {
                StartTermination();
            }

            await WaitUntilAsync(Settled(TerminalWork(streams)), schedule.ReaderStopAt).ConfigureAwait(false);
            Task readersStopping = Task.CompletedTask;
            if (!streams.IsCompleted)
            {
                readersStopping = Track(_readerStop.CancelAsync());
                Notify(ExternalProcessRunnerPhase.ReaderStopRequested);
            }

            // A reader joins its own cancellation callback before completing; also join the stop dispatch
            // within the same deadline so a just-finishing CancelAsync cannot cause a spurious Detached phase.
            await WaitUntilAsync(Settled([.. TerminalWork(streams), readersStopping]), schedule.DeadlineAt).ConfigureAwait(false);

            ExternalProcessCleanup cleanup = Classify(heldAfterExit);
            if (cancellationToken.IsCancellationRequested)
            {
                Notify(ExternalProcessRunnerPhase.Returning);
                throw new OperationCanceledException(
                    $"The external process run was canceled; observed cleanup: {cleanup}.",
                    innerException: null,
                    cancellationToken);
            }

            var result = new ExternalProcessResult(
                signal == TerminalSignal.NaturalExit ? process.ExitCode : -1,
                signal == TerminalSignal.Timeout,
                TextOf(_stdout),
                TextOf(_stderr))
            {
                Cleanup = cleanup,
            };
            Notify(ExternalProcessRunnerPhase.Returning);
            return result;
        }

        /// <summary>Always runs from the runner's finally block; returns without waiting for detached work.</summary>
        internal void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            if (!_exit.IsCompletedSuccessfully)
            {
                // Reached only when the terminal phase did not already own a termination (for example an
                // unexpected fault before the terminal signal); the request runs detached.
                StartTermination();
            }

            _ = Track(_readerStop.CancelAsync());
            _ = Track(_exitObservationStop.CancelAsync());
            Task[] outstanding;
            lock (_gate)
            {
                outstanding = [.. _background];
            }

            Task settled = Settled(outstanding);
            if (settled.IsCompleted)
            {
                Finish(outstanding);
                return;
            }

            // The run is returning while cleanup work is still running. The reservation taken at start stays held
            // and is returned only when the work settles, its handles are disposed, and the final
            // signal is published.
            Notify(ExternalProcessRunnerPhase.Detached);
            Task finishing = settled.ContinueWith(
                _ => Finish(outstanding),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            // Finish never throws for a disposal failure, but the detached continuation is still observed so that no
            // fault on this path (for example from the test observer) can surface as an unobserved exception.
            _ = finishing.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Runs once when every background task has settled. It observes every background fault, disposes each handle
        /// independently (a failing disposal never skips the rest), always returns the capacity slot, and only then
        /// publishes exactly one final signal: <see cref="ExternalProcessRunnerPhase.ResourcesReleased"/> when every
        /// disposal succeeded, otherwise <see cref="ExternalProcessRunnerPhase.ResourcesReleaseFailed"/>. The slot is
        /// returned even after a disposal failure because no cleanup work is still running under it.
        /// </summary>
        private void Finish(Task[] outstanding)
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0)
            {
                return;
            }

            bool disposalFailed;
            try
            {
                disposalFailed = DisposeResources(outstanding);
            }
            finally
            {
                seams.Capacity.Release();
            }

            Notify(disposalFailed
                ? ExternalProcessRunnerPhase.ResourcesReleaseFailed
                : ExternalProcessRunnerPhase.ResourcesReleased);
        }

        private async Task<TerminalSignal> WaitForTerminalSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Task first;
            using (var timeoutStop = new CancellationTokenSource())
            {
                first = await Task.WhenAny(
                        _exit,
                        Task.Delay(timeout, seams.Time, timeoutStop.Token),
                        _cancellationSignal.Task)
                    .ConfigureAwait(false);
                timeoutStop.Cancel();
            }

            // Caller cancellation observed at this point wins over an exit or timeout that completed together.
            TerminalSignal signal = cancellationToken.IsCancellationRequested
                ? TerminalSignal.Cancellation
                : first == _exit
                    ? _exit.IsCompletedSuccessfully ? TerminalSignal.NaturalExit : TerminalSignal.ExitObservationFailed
                    : TerminalSignal.Timeout;
            Notify(signal switch
            {
                TerminalSignal.Cancellation => ExternalProcessRunnerPhase.CancellationSignaled,
                TerminalSignal.Timeout => ExternalProcessRunnerPhase.TimeoutSignaled,
                TerminalSignal.NaturalExit or TerminalSignal.ExitObservationFailed or _ =>
                    ExternalProcessRunnerPhase.ExitSignaled,
            });
            return signal;
        }

        private void StartTermination()
        {
            if (_termination is not null)
            {
                return;
            }

            Action<Process> terminateTree = seams.TerminateTree;
            _termination = Track(seams.ScheduleTermination(() => TryTerminate(terminateTree, process)));
            Notify(ExternalProcessRunnerPhase.TerminationStarted);
        }

        private static bool TryTerminate(Action<Process> terminateTree, Process target)
        {
            try
            {
                terminateTree(target);
                return true;
            }
            catch (Exception exception) when (
                exception is AggregateException or Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Same uncertainty classification as the managed-application termination owner.
                return false;
            }
        }

        /// <summary>Priority: TerminationUnconfirmed, then OutputStreamHeldOpen, then OutputReadFailed, then Complete.</summary>
        private ExternalProcessCleanup Classify(bool heldAfterExit)
        {
            bool terminationSucceeded = _termination is null ||
                (_termination.IsCompletedSuccessfully && _termination.Result);
            return !terminationSucceeded || !_exit.IsCompletedSuccessfully
                ? ExternalProcessCleanup.TerminationUnconfirmed
                : heldAfterExit || EndNotObserved(_stdout) || EndNotObserved(_stderr)
                    ? ExternalProcessCleanup.OutputStreamHeldOpen
                    : ReadFailed(_stdout) || ReadFailed(_stderr)
                        ? ExternalProcessCleanup.OutputReadFailed
                        : ExternalProcessCleanup.Complete;
        }

        private static bool ReadFailed(Task<BoundedProcessOutput> reader)
        {
            return reader.IsFaulted || reader.IsCanceled;
        }

        private static bool EndNotObserved(Task<BoundedProcessOutput> reader)
        {
            return !ReadFailed(reader) &&
                !(reader.IsCompletedSuccessfully && reader.Result.ReachedEndOfStream);
        }

        private static string TextOf(Task<BoundedProcessOutput> reader)
        {
            return reader.IsCompletedSuccessfully ? reader.Result.Text : string.Empty;
        }

        private Task[] TerminalWork(Task streams)
        {
            return _termination is null ? [_exit, streams] : [_termination, _exit, streams];
        }

        /// <summary>Returns true when at least one disposal threw; every disposal is attempted either way.</summary>
        private bool DisposeResources(Task[] outstanding)
        {
            foreach (Task task in outstanding)
            {
                // Observes late faults so a detached task can never surface as an unobserved exception.
                _ = task.Exception;
            }

            bool failed = false;
            failed |= !TryDispose(() => process.StandardOutput);
            failed |= !TryDispose(() => process.StandardError);
            failed |= !TryDispose(() => process);
            failed |= !TryDispose(() => _readerStop);
            failed |= !TryDispose(() => _exitObservationStop);
            return failed;
        }

        private bool TryDispose(Func<IDisposable> resource)
        {
            try
            {
                seams.DisposeResource(resource());
                return true;
            }
#pragma warning disable CA1031 // A disposal fault is observed and reported through the failure signal; letting it
            // escape would skip the remaining disposals and the capacity return.
            catch (Exception)
#pragma warning restore CA1031
            {
                return false;
            }
        }

        private T Track<T>(T task)
            where T : Task
        {
            lock (_gate)
            {
                _background.Add(task);
            }

            return task;
        }

        private void Notify(ExternalProcessRunnerPhase phase)
        {
            seams.Observe?.Invoke(phase);
        }

        /// <summary>A task that completes when every input completes and never faults.</summary>
        private static Task Settled(params Task[] tasks)
        {
            return Task.WhenAll(tasks).ContinueWith(
                static completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task WaitUntilAsync(Task work, long untilTimestamp)
        {
            TimeSpan remaining = seams.Time.GetElapsedTime(seams.Time.GetTimestamp(), untilTimestamp);
            if (work.IsCompleted || remaining <= TimeSpan.Zero)
            {
                return;
            }

            using var delayStop = new CancellationTokenSource();
            _ = await Task.WhenAny(work, Task.Delay(remaining, seams.Time, delayStop.Token)).ConfigureAwait(false);
            delayStop.Cancel();
        }
    }
}
