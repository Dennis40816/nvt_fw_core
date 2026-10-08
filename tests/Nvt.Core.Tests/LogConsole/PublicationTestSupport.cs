// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

// Deterministic queued writer controls.
internal static class PublicationTestSupport
{
    internal static Task<LogSnapshot> Latest(LogStore store, CancellationToken cancellationToken)
        => store.CaptureLatestAsync(cancellationToken).AsTask();

    internal static Action Take(ConcurrentQueue<Action> callbacks)
    {
        Action? action = null;
        Assert.True(SpinWait.SpinUntil(() => callbacks.TryDequeue(out action), TimeSpan.FromSeconds(10)));
        return action!;
    }

}
