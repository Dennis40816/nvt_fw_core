// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Nvt.Core.TestSupport;

/// <summary>Blocks the calling thread until a task completes, for the few test hooks that cannot be asynchronous.</summary>
/// <remarks>
/// A synchronous hook such as a dispatcher callback or a fixture constructor cannot await. A direct
/// <c>Task.Wait</c>, <c>Task.Result</c> or <c>GetAwaiter().GetResult()</c> is banned in tests, because it blocks inside
/// a synchronization context and wraps faults. This helper blocks through an event, rethrows the original exception,
/// and does not use the caller's synchronization context. The task must run on another thread (for example the thread
/// pool). A task that needs the blocked thread to continue never completes. Use it only where the test cannot await.
/// </remarks>
public static class TaskBlock
{
    /// <summary>Blocks until the task completes. A fault is rethrown with its original exception.</summary>
    /// <param name="task">The task to wait for.</param>
    /// <exception cref="OperationCanceledException">The task was canceled.</exception>
    public static void UntilComplete(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        using var done = new ManualResetEventSlim();
        _ = task.ContinueWith(
            static (_, state) => ((ManualResetEventSlim)state!).Set(),
            done,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        done.Wait();
        if (task.IsFaulted)
        {
            ExceptionDispatchInfo.Capture(task.Exception!.InnerException ?? task.Exception).Throw();
        }
        if (task.IsCanceled)
        {
            throw new OperationCanceledException();
        }
    }

    /// <summary>Blocks until the task completes and returns its result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="task">The task to wait for.</param>
    /// <returns>The result of the completed task.</returns>
    /// <exception cref="OperationCanceledException">The task was canceled.</exception>
    public static T UntilComplete<T>(Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var result = new StrongBox<T>();
        Task copied = task.ContinueWith(
            static (completed, state) => ((StrongBox<T>)state!).Value = completed.Result,
            result,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        UntilComplete((Task)task);
        UntilComplete(copied);
        return result.Value!;
    }
}
