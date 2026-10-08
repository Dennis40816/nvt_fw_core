// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;

namespace Nvt.Core.Launcher.Transport;

internal readonly record struct ManagedProcessTerminationResult(bool IsExitConfirmed, int? ExitCode)
{
    internal static ManagedProcessTerminationResult Unconfirmed => new(false, null);
}

internal interface IManagedProcessTermination
{
    ManagedProcessTerminationResult ConfirmExited(Process process);
}

internal interface IManagedProcessTerminationOperations
{
    bool HasExited(Process process);
    void Kill(Process process);
    bool WaitForExit(Process process, TimeSpan timeout);
    int GetExitCode(Process process);
}

internal sealed class ManagedProcessTermination : IManagedProcessTermination
{
    internal static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly IManagedProcessTerminationOperations _operations;
    private readonly TimeSpan _waitTimeout;

    internal static ManagedProcessTermination Instance { get; } = new(new ProcessTerminationOperations());

    internal ManagedProcessTermination(
        IManagedProcessTerminationOperations operations,
        TimeSpan? waitTimeout = null)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _waitTimeout = waitTimeout ?? DefaultWaitTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_waitTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            _waitTimeout,
            TimeSpan.FromMilliseconds(int.MaxValue));
    }

    public ManagedProcessTerminationResult ConfirmExited(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (_operations.HasExited(process))
            {
                return new(true, _operations.GetExitCode(process));
            }
        }
        catch (Exception exception) when (IsTerminationUncertainty(exception))
        {
            return ManagedProcessTerminationResult.Unconfirmed;
        }

        try
        {
            _operations.Kill(process);
        }
        catch (Exception exception) when (IsTerminationUncertainty(exception))
        {
            return ManagedProcessTerminationResult.Unconfirmed;
        }

        try
        {
            return _operations.WaitForExit(process, _waitTimeout) && _operations.HasExited(process)
                ? new(true, _operations.GetExitCode(process))
                : ManagedProcessTerminationResult.Unconfirmed;
        }
        catch (Exception exception) when (IsTerminationUncertainty(exception))
        {
            return ManagedProcessTerminationResult.Unconfirmed;
        }
    }

    private static bool IsTerminationUncertainty(Exception exception)
    {
        return exception is AggregateException or InvalidOperationException or Win32Exception;
    }

    private sealed class ProcessTerminationOperations : IManagedProcessTerminationOperations
    {
        public bool HasExited(Process process)
        {
            return process.HasExited;
        }

        public void Kill(Process process)
        {
            process.Kill(entireProcessTree: true);
        }

        public bool WaitForExit(Process process, TimeSpan timeout)
        {
            return process.WaitForExit(checked((int)Math.Ceiling(timeout.TotalMilliseconds)));
        }

        public int GetExitCode(Process process)
        {
            return process.ExitCode;
        }
    }
}
