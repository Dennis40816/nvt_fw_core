// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Integration;

internal sealed class ReplicaReadSlot(
    IUpdateSourceRegistry replica,
    TimeSpan timeout,
    TimeProvider timeProvider)
{
    private readonly Lock _gate = new();
    // Guard: _gate protects the complete physical-read lifecycle.
    private ReadLifecycle? _current;

    internal Task<UpdateSourceRegistryLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<UpdateSourceRegistryLoadResult> read;
        lock (_gate)
        {
            RetireCompletedUnderLock();
            _current ??= new ReadLifecycle(Task.Run(
                async () => await replica.LoadAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CancellationToken.None), Abandoned: false);
            if (_current.Abandoned)
            {
                return Task.FromResult(new UpdateSourceRegistryLoadResult(
                    null,
                    UpdateSourceRegistryLoadIssue.RegistryTimedOut));
            }
            read = _current.Read;
        }
        return AwaitCurrentAsync(read, cancellationToken);
    }

    private async Task<UpdateSourceRegistryLoadResult> AwaitCurrentAsync(
        Task<UpdateSourceRegistryLoadResult> read,
        CancellationToken cancellationToken)
    {
        try
        {
            UpdateSourceRegistryLoadResult result = await read.WaitAsync(
                timeout,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_current is { Abandoned: true } current && ReferenceEquals(current.Read, read))
                {
                    return new(null, UpdateSourceRegistryLoadIssue.RegistryTimedOut);
                }
                RetireUnderLock(read);
            }
            return result;
        }
        catch (TimeoutException)
        {
            Abandon(read);
            return new(null, UpdateSourceRegistryLoadIssue.RegistryTimedOut);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Timeouts have the ordered handler above. Every remaining adapter failure is
        // reported through the typed unavailable outcome without faulting other replicas.
        catch (Exception exception) when (exception is not TimeoutException)
        {
            lock (_gate)
            {
                RetireUnderLock(read);
            }
            return new(null, UpdateSourceRegistryLoadIssue.RegistryUnavailable);
        }
    }

    private void Abandon(Task<UpdateSourceRegistryLoadResult> read)
    {
        lock (_gate)
        {
            if (_current is not { } current || !ReferenceEquals(current.Read, read) || current.Abandoned)
            {
                return;
            }
            _current = current with { Abandoned = true };
            _ = read.ContinueWith(
                _ => RetireAbandoned(read),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    private void RetireAbandoned(Task<UpdateSourceRegistryLoadResult> read)
    {
        lock (_gate)
        {
            RetireUnderLock(read);
        }
    }

    private void RetireCompletedUnderLock()
    {
        if (_current?.Read is { IsCompleted: true } completed)
        {
            RetireUnderLock(completed);
        }
    }

    private void RetireUnderLock(Task<UpdateSourceRegistryLoadResult> read)
    {
        if (!ReferenceEquals(_current?.Read, read) || !read.IsCompleted)
        {
            return;
        }
        _ = read.Exception;
        _current = null;
    }

    private sealed record ReadLifecycle(Task<UpdateSourceRegistryLoadResult> Read, bool Abandoned);
}
