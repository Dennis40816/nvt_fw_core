// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.TestProbe;

/// <summary>Signals a probe wait, including a callback that outlives the wait handle.</summary>
internal static class ReleaseSignal
{
    /// <summary>Tolerates a release event after the wait handle has been disposed.</summary>
    /// <param name="released">The wait handle captured by the release callback.</param>
    internal static void Set(ManualResetEvent released)
    {
        try
        {
            _ = released.Set();
        }
        catch (ObjectDisposedException)
        {
            // The wait already ended, so there is nothing left to release.
        }
    }
}
