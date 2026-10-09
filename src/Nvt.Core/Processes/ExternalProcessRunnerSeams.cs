// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.Processes;

/// <summary>Production behavior plus the narrow seams tests use to inject faults, blocking, ordering and limits.</summary>
/// <remarks>
/// <see cref="Time"/> supplies every cleanup timestamp and delay, so a test can drive the deadlines with a manual clock
/// instead of waiting for real time. <see cref="ScheduleTermination"/> starts the single termination work item; a test
/// can run it inline, so its completion does not depend on thread-pool scheduling.
/// </remarks>
internal sealed record ExternalProcessRunnerSeams(
    ExternalProcessCleanupTiming Timing,
    Action<Process> TerminateTree,
    Func<TextReader, CancellationToken, Task<BoundedProcessOutput>> Drain,
    Func<Process, CancellationToken, Task> ObserveExit,
    Action<ExternalProcessRunnerPhase>? Observe,
    ExternalProcessCapacity Capacity,
    Action<IDisposable> DisposeResource,
    TimeProvider Time,
    Func<Func<bool>, Task<bool>> ScheduleTermination)
{
    internal static ExternalProcessRunnerSeams Production { get; } = new(
        ExternalProcessCleanupTiming.Default,
        static process => process.Kill(entireProcessTree: true),
        BoundedProcessOutputReader.DrainProcessStreamAsync,
        static (process, cancellationToken) => process.WaitForExitAsync(cancellationToken),
        Observe: null,
        new ExternalProcessCapacity(ExternalProcessCapacity.DefaultLimit),
        static resource => resource.Dispose(),
        TimeProvider.System,
        static termination => Task.Run(termination));
}
