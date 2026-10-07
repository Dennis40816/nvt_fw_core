// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>Internal ordering points of one invocation, published to the test observer seam.</summary>
internal enum ExternalProcessRunnerPhase
{
    /// <summary>The process started and both drains and the exit observation are running.</summary>
    Started,

    /// <summary>The terminal signal is a natural exit, or the exit observation failed.</summary>
    ExitSignaled,

    /// <summary>The terminal signal is the execution timeout.</summary>
    TimeoutSignaled,

    /// <summary>The terminal signal is caller cancellation.</summary>
    CancellationSignaled,

    /// <summary>After a natural exit the held-output grace elapsed with a stream still open.</summary>
    OutputHeldAfterExit,

    /// <summary>The single termination work item was started.</summary>
    TerminationStarted,

    /// <summary>The readers were asked to stop.</summary>
    ReaderStopRequested,

    /// <summary>The terminal decision is made; the result is returned or cancellation is thrown next.</summary>
    Returning,

    /// <summary>The run returned while cleanup work is still running; the invocation keeps its original slot.</summary>
    Detached,

    /// <summary>
    /// Final success signal: every background task settled, every handle was disposed without error, and the slot was
    /// returned, in that order.
    /// </summary>
    ResourcesReleased,

    /// <summary>
    /// Final failure signal: every background task settled and the slot was returned, but disposing at least one handle
    /// threw. The disposal faults were observed; <see cref="ResourcesReleased"/> is not published.
    /// </summary>
    ResourcesReleaseFailed,
}
