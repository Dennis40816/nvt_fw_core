// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.Persistence;

/// <summary>Serializes local-state saves and lets a newer immutable snapshot supersede queued work.</summary>
public sealed class LatestSnapshotPersistenceCoordinator<TSnapshot>
{
    private readonly object _gate = new();
    private readonly Func<TSnapshot, TSnapshot> _capture;
    private readonly Func<TSnapshot, CancellationToken, Task> _saveAsync;
    private readonly Action<Exception?, long>? _saveCompleted;
    private CancellationTokenSource? _latestCancellation;
    private Task _tail = Task.CompletedTask;
    private TSnapshot? _latestSnapshot;
    private bool _hasLatestSnapshot;
    private bool _isCompleted;
    private long _generation;

    /// <summary>Creates a coordinator for host-supplied snapshot capture, persistence, and outcomes.</summary>
    /// <param name="saveAsync">Persists one captured snapshot.</param>
    /// <param name="capture">Copies a snapshot into an immutable value before it is queued.</param>
    /// <param name="saveCompleted">
    /// Observes every save that finished (<see langword="null"/>) or failed (the exception) together with the
    /// request generation <see cref="Queue"/>/<see cref="TryRetry"/> assigned it, in queue order on a background
    /// thread; a save superseded by a newer snapshot reports nothing, even when its write ignored the
    /// cancellation and finished. The generation lets a caller that applies this outcome later (for example after
    /// an <c>await</c> back to a UI thread) re-check with <see cref="IsCurrentGeneration"/> whether a newer
    /// snapshot was queued in the meantime and, if so, discard the now-stale outcome instead of applying it.
    /// </param>
    public LatestSnapshotPersistenceCoordinator(
        Func<TSnapshot, CancellationToken, Task> saveAsync,
        Func<TSnapshot, TSnapshot> capture,
        Action<Exception?, long>? saveCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        ArgumentNullException.ThrowIfNull(capture);
        _saveAsync = saveAsync;
        _capture = capture;
        _saveCompleted = saveCompleted;
    }

    /// <summary>Captures a snapshot and queues it, superseding earlier save requests.</summary>
    public void Queue(TSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TSnapshot capturedSnapshot = _capture(snapshot);
        ArgumentNullException.ThrowIfNull(capturedSnapshot);

        lock (_gate)
        {
            if (_isCompleted)
            {
                throw new InvalidOperationException("Local-state persistence is already completing.");
            }

            QueueCaptured(capturedSnapshot);
        }
    }

    /// <summary>Queues the latest captured snapshot again; false once completing or before any snapshot.</summary>
    public bool TryRetry()
    {
        lock (_gate)
        {
            if (_isCompleted || !_hasLatestSnapshot)
            {
                return false;
            }

            QueueCaptured(_latestSnapshot!);
            return true;
        }
    }

    /// <summary>Returns the current save tail, including its completion observer.</summary>
    public Task WaitForIdleAsync()
    {
        lock (_gate)
        {
            return _tail;
        }
    }

    /// <summary>Seals admission and returns the current save tail without cancelling it.</summary>
    public Task CompleteAsync()
    {
        lock (_gate)
        {
            _isCompleted = true;
            return _tail;
        }
    }

    /// <summary>Reopens admission after a failed close without replacing the serial save tail.</summary>
    public void Reopen()
    {
        lock (_gate)
        {
            _isCompleted = false;
        }
    }

    /// <summary>Gets the last recorded save failure; later success does not clear it.</summary>
    public Exception? LastFailure
    {
        get
        {
            lock (_gate)
            {
                return field;
            }
        }

        private set;
    }

    private void QueueCaptured(TSnapshot capturedSnapshot)
    {
        var cancellation = new CancellationTokenSource();
        _latestCancellation?.Cancel();
        _latestCancellation = cancellation;
        _latestSnapshot = capturedSnapshot;
        _hasLatestSnapshot = true;
        long generation = ++_generation;
        Task predecessor = _tail;
        _tail = Task.Run(() => PersistAfterAsync(predecessor, capturedSnapshot, cancellation, generation));
    }

    /// <summary>
    /// True while <paramref name="generation"/> is still the most recently queued request: no <see cref="Queue"/>
    /// or <see cref="TryRetry"/> call has run since it was assigned. A caller applying a deferred save outcome
    /// (posted, for example, to a UI thread) calls this again at the point it actually applies the outcome, since
    /// a newer snapshot can have been queued after the coordinator itself reported this one as latest but before
    /// the deferred apply ran; when it returns <see langword="false"/> the outcome is stale and must be discarded.
    /// </summary>
    public bool IsCurrentGeneration(long generation)
    {
        lock (_gate)
        {
            return generation == _generation;
        }
    }

    private async Task PersistAfterAsync(
        Task predecessor,
        TSnapshot snapshot,
        CancellationTokenSource cancellation,
        long generation)
    {
        bool isTerminal = false;
        bool isLatest = false;
        Exception? failure = null;
        try
        {
            await ObserveCompletionAsync(predecessor).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            await _saveAsync(snapshot, cancellation.Token).ConfigureAwait(false);
            isTerminal = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            failure = exception;
            isTerminal = true;
        }
        finally
        {
            lock (_gate)
            {
                // Queue replaces the latest cancellation under this gate, so the save still owns it only when no
                // newer snapshot superseded it, even if its write ignored the cancellation and finished anyway.
                isLatest = ReferenceEquals(_latestCancellation, cancellation);
                if (isLatest)
                {
                    _latestCancellation = null;
                }
            }

            cancellation.Dispose();
        }

        if (isTerminal && isLatest)
        {
            ReportSaveCompleted(failure, generation);
        }
    }

    private async Task ObserveCompletionAsync(Task predecessor)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
    }

    private void RecordFailure(Exception exception)
    {
        lock (_gate)
        {
            LastFailure = exception;
        }
    }

    private void ReportSaveCompleted(Exception? failure, long generation)
    {
        if (_saveCompleted is null)
        {
            return;
        }

        try
        {
            _saveCompleted(failure, generation);
        }
        catch (Exception exception)
        {
            // A failing observer must not poison later saves.
            Trace.TraceError("Local-state save observer failed: {0}", exception);
        }
    }
}
