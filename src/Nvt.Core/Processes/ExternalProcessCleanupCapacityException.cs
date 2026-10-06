// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>
/// The runner refused to start a new external process because the capacity of invocations that are running
/// or still cleaning up is full. The refusal happens before any process starts.
/// </summary>
public sealed class ExternalProcessCleanupCapacityException : Exception
{
    /// <summary>Creates a capacity refusal with the count observed by the atomic reservation.</summary>
    /// <param name="inUseInvocations">The running or still-cleaning-up count when the reservation was refused.</param>
    /// <param name="limit">The invocation limit that was reached.</param>
    public ExternalProcessCleanupCapacityException(int inUseInvocations, int limit)
        : base(
            $"The external process runner is at its limit of {limit} invocations that are running or still cleaning up " +
            $"({inUseInvocations} in use when the reservation was refused); a new run is refused. Restart the application.")
    {
        InUseInvocations = inUseInvocations;
        Limit = limit;
    }

    /// <summary>
    /// In-use count observed when the atomic reservation refused, at or above <see cref="Limit"/>.
    /// It is not re-read later.
    /// </summary>
    public int InUseInvocations { get; }

    /// <summary>The fixed limit that was reached.</summary>
    public int Limit { get; }
}
