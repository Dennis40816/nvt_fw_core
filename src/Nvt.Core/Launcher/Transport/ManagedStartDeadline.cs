// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Transport;

/// <summary>Bounds a managed start, including its synchronous contained-launch preparation.</summary>
internal sealed class ManagedStartDeadline
{
    private static TimeSpan CleanupWaitTimeout =>
        2 * ManagedProcessTermination.DefaultWaitTimeout;
    private readonly CancellationTokenSource _deadline;
    // Guard: Interlocked writes and Volatile reads. The worker owns creation and cleanup.
    private int _creationStarted;

    internal ManagedStartDeadline(
        TimeSpan readyDeadline,
        CancellationToken cancellationToken,
        CancellationToken testDeadlineSignal = default)
    {
        _deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, testDeadlineSignal);
        _deadline.CancelAfter(readyDeadline);
    }

    internal CancellationToken Token => _deadline.Token;

    // Called inside the global process-start gate, after the final repository validation.
    internal bool TryBeginCreation()
    {
        if (Token.IsCancellationRequested)
        {
            return false;
        }
        _ = Interlocked.Exchange(ref _creationStarted, 1);
        return !Token.IsCancellationRequested;
    }

    internal async Task<TResult> RunAsync<TResult>(
        Func<Task<TResult>> start,
        Func<TResult> terminationUnconfirmed,
        CancellationToken cancellationToken)
    {
        Task<TResult> worker = Task.Run(start);
        try
        {
            return await worker.WaitAsync(Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Once native creation began, its worker owns the terminal result.
            // READY may have been accepted just before cancellation; discarding
            // that result would leave a live accepted child without a state commit.
            if (Volatile.Read(ref _creationStarted) != 0)
            {
                try
                {
                    return await worker.WaitAsync(
                            CleanupWaitTimeout,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return terminationUnconfirmed();
                }
            }
            throw;
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested)
        {
            // Before creation the worker may still own its lease and pipe. After
            // creation it may need both bounded termination steps. In either case,
            // fallback is safe only after the worker has completed its cleanup.
            try
            {
                return await worker.WaitAsync(
                        CleanupWaitTimeout,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return terminationUnconfirmed();
            }
        }
        finally
        {
            if (worker.IsCompleted)
            {
                _deadline.Dispose();
            }
            else
            {
                _ = worker.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        _deadline.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}
