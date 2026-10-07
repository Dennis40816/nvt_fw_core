// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;

namespace Nvt.Core.Processes;

/// <summary>Default process runner that uses ProcessStartInfo.ArgumentList with shell execution disabled.</summary>
/// <remarks>
/// The runner is the single owner of an invocation's terminal phase: the caller's cancellation callback
/// only signals, one termination work item per invocation performs the tree kill, and every host wait after the
/// terminal signal shares one total cleanup deadline. A run returns within that deadline even when OS termination
/// never completes; the detached work then keeps its resources until it settles. A process-wide capacity bounds the
/// invocations that are running or still cleaning up so resources cannot accumulate without limit.
/// </remarks>
public sealed partial class SystemExternalProcessRunner : IExternalProcessRunner
{
    private readonly ExternalProcessRunnerSeams _seams;

    /// <summary>Creates the production runner, sharing the process-wide invocation capacity.</summary>
    public SystemExternalProcessRunner()
        : this(ExternalProcessRunnerSeams.Production)
    {
    }

    /// <summary>Fault-injection and ordering seam for tests; production uses <see cref="ExternalProcessRunnerSeams.Production"/>.</summary>
    internal SystemExternalProcessRunner(ExternalProcessRunnerSeams seams)
    {
        ArgumentNullException.ThrowIfNull(seams);
        seams.Timing.Validate();
        _seams = seams;
    }

    /// <inheritdoc />
    /// <exception cref="ExternalProcessCleanupCapacityException">
    /// The capacity of invocations that are running or still cleaning up is full; no process is started.
    /// </exception>
    /// <exception cref="ExternalProcessStartFailedException">
    /// The operating system refused to start the approved external process; no process is started.
    /// </exception>
    public async ValueTask<ExternalProcessResult> RunAsync(
        ExternalProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        cancellationToken.ThrowIfCancellationRequested();

        // Atomically reserve one slot before starting a process, so the invocations that are
        // running or still cleaning up never exceed the limit, even under concurrency. The reservation is released
        // exactly once: on a start failure here, or by the invocation when its work settles (inline or detached).
        // The refusal reports the in-use count the atomic reservation observed, not a later re-read.
        if (!_seams.Capacity.TryReserve(out int observedInUse))
        {
            throw new ExternalProcessCleanupCapacityException(observedInUse, _seams.Capacity.Limit);
        }

        Process? process;
        try
        {
#pragma warning disable CA2000 // The invocation custody owns the process and releases it when its work settles.
            process = ProcessLaunchGate.Start(CreateProcessStartInfo(startInfo));
#pragma warning restore CA2000
        }
        catch (Win32Exception exception)
        {
            // Translate an OS launch refusal to the stable typed start-failure signal.
            _seams.Capacity.Release();
            throw new ExternalProcessStartFailedException(exception);
        }
        catch
        {
            _seams.Capacity.Release();
            throw;
        }

        if (process is null)
        {
            _seams.Capacity.Release();
            throw new InvalidOperationException("External process did not start.");
        }

        // Ownership of the reservation passes to the invocation, which releases it exactly once.
        var invocation = new Invocation(process, _seams);
        try
        {
            return await invocation.ExecuteAsync(startInfo.Timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            invocation.Release();
        }
    }

    internal static ProcessStartInfo CreateProcessStartInfo(ExternalProcessStartInfo startInfo)
    {
        var result = new ProcessStartInfo(startInfo.ExecutablePath)
        {
            WorkingDirectory = startInfo.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in startInfo.Arguments)
        {
            result.ArgumentList.Add(argument);
        }

        return result;
    }
}
