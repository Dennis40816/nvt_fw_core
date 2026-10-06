// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.Processes;

/// <summary>Production behavior plus the narrow seams tests use to inject faults, blocking, ordering and limits.</summary>
internal sealed record ExternalProcessRunnerSeams(
    ExternalProcessCleanupTiming Timing,
    Action<Process> TerminateTree,
    Func<TextReader, CancellationToken, Task<BoundedProcessOutput>> Drain,
    Func<Process, CancellationToken, Task> ObserveExit,
    Action<ExternalProcessRunnerPhase>? Observe,
    ExternalProcessCapacity Capacity,
    Action<IDisposable> DisposeResource)
{
    internal static ExternalProcessRunnerSeams Production { get; } = new(
        ExternalProcessCleanupTiming.Default,
        static process => process.Kill(entireProcessTree: true),
        BoundedProcessOutputReader.DrainProcessStreamAsync,
        static (process, cancellationToken) => process.WaitForExitAsync(cancellationToken),
        Observe: null,
        new ExternalProcessCapacity(ExternalProcessCapacity.DefaultLimit),
        static resource => resource.Dispose());
}
