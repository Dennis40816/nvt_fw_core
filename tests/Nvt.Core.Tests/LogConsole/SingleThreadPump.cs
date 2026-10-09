// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Nvt.Core.Tests.LogConsole;

// One dedicated thread owns this context and executes posted continuations and writer turns.
internal sealed class SingleThreadPump : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly CancellationToken _cancellationToken;
    private readonly Thread _thread;
    private ExceptionDispatchInfo? _failure;
    private int _disposed;

    internal SingleThreadPump(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                try { callback(state); }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null);
                }
            }
        }) { IsBackground = true };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
    internal void Schedule(Action action) => Post(_ => action(), null);
    internal Task Run(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        }, null);
        return completion.Task;
    }

    internal void Join()
    {
        _queue.CompleteAdding();
        StoreRegressionSupport.JoinThread(_thread, _cancellationToken);
        Volatile.Read(ref _failure)?.Throw();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Join(); }
        finally { if (!_thread.IsAlive) _queue.Dispose(); }
    }
}
