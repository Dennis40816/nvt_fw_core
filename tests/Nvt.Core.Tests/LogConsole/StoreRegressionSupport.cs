// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

// Shared deterministic writer controls and immutable test content.
internal static class StoreRegressionSupport
{
    internal static CancellationTokenSource CreateWaitCancellation()
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(60));
        return cancellation;
    }

    internal static bool WaitUntil(Func<bool> condition, CancellationToken cancellationToken)
    {
        SpinWait.SpinUntil(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return condition();
        });
        return true;
    }

    internal static void JoinThread(Thread thread, CancellationToken cancellationToken)
        => Assert.True(WaitUntil(() => thread.Join(10), cancellationToken));

    internal static object? Field(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value);
    internal static int PendingCount(LogStore store) => ((IEnumerable)Field(store, "_pending")!).Cast<object>().Count();
    internal static long PendingCharge(LogStore store) => store.PendingUsage.Characters;
    internal static void AwaitCaptureOrFence(LogStore store, Task capture)
    {
        using var waitCancellation = CreateWaitCancellation();
        Assert.True(WaitUntil(() =>
        {
            lock (Field(store, "_gate")!)
                return capture.IsCompleted || Field(store, "_snapshotWaiters") is ICollection { Count: > 0 };
        }, waitCancellation.Token));
    }
    internal static Action Take(ConcurrentQueue<Action> callbacks)
    {
        using var waitCancellation = CreateWaitCancellation();
        Action? result = null;
        Assert.True(WaitUntil(() => callbacks.TryDequeue(out result), waitCancellation.Token));
        return result!;
    }

    internal static Task<LogSnapshot> Latest(LogStore store, CancellationToken cancellationToken)
        => store.CaptureLatestAsync(cancellationToken).AsTask();
}
