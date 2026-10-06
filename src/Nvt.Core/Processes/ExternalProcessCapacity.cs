// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.Processes;

/// <summary>
/// Process-wide hard cap on external-process invocations that are running or still cleaning up. A slot is reserved
/// atomically before a process starts and held for the whole invocation; if the
/// invocation's cleanup work is still running when the run returns, the same reservation stays held until that work
/// settles. The in-use count therefore never exceeds the limit, so detached resources cannot accumulate without bound.
/// </summary>
internal sealed class ExternalProcessCapacity(int limit)
{
    /// <summary>Fixed production invocation limit.</summary>
    internal const int DefaultLimit = 8;

    private int _inUse;

    internal int Limit { get; } = limit > 0
        ? limit
        : throw new ArgumentOutOfRangeException(nameof(limit));

    /// <summary>Slots currently held by invocations that are running or still cleaning up.</summary>
    internal int InUse => Volatile.Read(ref _inUse);

    /// <summary>
    /// Atomically takes one slot when below the limit. On refusal nothing is taken and
    /// <paramref name="observedInUse"/> is the in-use count the reservation observed (at or above the limit):
    /// from the initial read when the capacity was already full, or from a failed compare-and-set.
    /// </summary>
    internal bool TryReserve(out int observedInUse)
    {
        int current = Volatile.Read(ref _inUse);
        while (current < Limit)
        {
            int seen = Interlocked.CompareExchange(ref _inUse, current + 1, current);
            if (seen == current)
            {
                observedInUse = current + 1;
                return true;
            }

            current = seen;
        }

        observedInUse = current;
        return false;
    }

    /// <summary>Returns one previously reserved slot. Called exactly once per successful reservation.</summary>
    internal void Release()
    {
        int updated = Interlocked.Decrement(ref _inUse);
        Debug.Assert(updated >= 0, "The invocation capacity was released more times than it was reserved.");
    }
}
