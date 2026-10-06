// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.MessageCenter;

/// <summary>Joins compatible asynchronous refreshes and admits a reload after an active observation.</summary>
/// <remarks>
/// The host must serialize admission and continuations, normally through its caller context. Admission
/// is not thread-safe. The delegate runs in the admitting caller's context, and awaits retain that
/// context. The host owns delegate threading, cancellation, publication, and readiness policy.
/// </remarks>
public sealed class MessageCenterRefreshCoordinator
{
    private readonly Func<bool, CancellationToken, Task> _refresh;
    private Task? _activeRefresh;
    private bool _activeRefreshReloadsSources;

    /// <summary>Creates a coordinator for a host-supplied asynchronous refresh.</summary>
    /// <param name="refresh">Returns the refresh task using the admitted strength and owner's token.</param>
    /// <exception cref="ArgumentNullException">The refresh delegate is null.</exception>
    public MessageCenterRefreshCoordinator(Func<bool, CancellationToken, Task> refresh)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
    }

    /// <summary>Starts a refresh, joins compatible active work, or requests a reload after an observation.</summary>
    /// <param name="reloadSources">Whether this request requires a reload rather than an observation.</param>
    /// <param name="cancellationToken">The owner token for new work, or this caller's token for joining.</param>
    /// <returns>A task observing this request's completion, failure, or cancellation.</returns>
    /// <remarks>
    /// Only incomplete work can be joined. A joining caller's cancellation does not cancel its owner.
    /// A reload behind an observation ignores an unrelated active failure and re-enters admission for
    /// a full attempt. Compatible reload waiters can then join that attempt. Completion cleanup clears
    /// only the task it owns. Cancellation of newly admitted work is the delegate's responsibility.
    /// </remarks>
    public Task RefreshAsync(bool reloadSources, CancellationToken cancellationToken)
    {
        if (_activeRefresh is { IsCompleted: false } active)
        {
            return reloadSources && !_activeRefreshReloadsSources
                ? RefreshAfterActiveAsync(active, cancellationToken)
                : active.WaitAsync(cancellationToken);
        }

        Task refresh = _refresh(reloadSources, cancellationToken);
        _activeRefreshReloadsSources = reloadSources;
        _activeRefresh = refresh;
        return ObserveRefreshCompletionAsync(refresh);
    }

    /// <summary>Waits for current incomplete work before requesting the caller's chosen refresh strength.</summary>
    /// <param name="reloadSources">Whether the following request requires a reload or an observation.</param>
    /// <param name="cancellationToken">This caller's wait token and the owner token if new work is admitted.</param>
    /// <returns>A task observing the wait and the following refresh request.</returns>
    /// <remarks>
    /// The current task alone does not satisfy this request. Caller cancellation while waiting propagates;
    /// unrelated current failure or cancellation permits a fresh request. Following the wait, normal
    /// admission applies, so compatible work admitted in the meantime may be joined.
    /// </remarks>
    public async Task RefreshAfterCurrentAsync(bool reloadSources, CancellationToken cancellationToken)
    {
        if (_activeRefresh is { IsCompleted: false } active)
        {
            try
            {
                await active.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // The following request still owns a fresh attempt.
            }
        }

        await RefreshAsync(reloadSources, cancellationToken);
    }

    private async Task RefreshAfterActiveAsync(Task active, CancellationToken cancellationToken)
    {
        try
        {
            await active.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The stronger request still owns a fresh full attempt.
        }

        await RefreshAsync(reloadSources: true, cancellationToken);
    }

    private async Task ObserveRefreshCompletionAsync(Task refresh)
    {
        try
        {
            await refresh;
        }
        finally
        {
            if (ReferenceEquals(_activeRefresh, refresh))
            {
                _activeRefresh = null;
            }
        }
    }
}
