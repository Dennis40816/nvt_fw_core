// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.Processes;

/// <summary>Serializes every production process start in this process.</summary>
public static class ProcessLaunchGate
{
    private static readonly object StartLock = new();

    /// <summary>Starts a normal child while excluding concurrent handle duplication.</summary>
    public static Process? Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        lock (StartLock)
        {
            return Process.Start(startInfo);
        }
    }

    /// <summary>Starts a child with exactly the declared Windows handle allow-list.</summary>
    public static Process? StartContained(
        ProcessStartInfo startInfo,
        IReadOnlyList<ProcessInheritedHandle> inheritedHandles)
    {
        return StartContained(startInfo, inheritedHandles, static () => true);
    }

    /// <summary>
    /// Starts a child only when its final custody validation succeeds while the
    /// global start gate is held, immediately before native process creation.
    /// </summary>
    public static Process? StartContained(
        ProcessStartInfo startInfo,
        IReadOnlyList<ProcessInheritedHandle> inheritedHandles,
        Func<bool> validateImmediatelyBeforeStart)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(inheritedHandles);
        ArgumentNullException.ThrowIfNull(validateImmediatelyBeforeStart);
        lock (StartLock)
        {
            return OperatingSystem.IsWindows()
                ? WindowsContainedProcessStarter.Start(
                    startInfo,
                    inheritedHandles,
                    validateImmediatelyBeforeStart)
                : validateImmediatelyBeforeStart()
                    ? Process.Start(startInfo)
                    : null;
        }
    }

    /// <summary>Clears child inheritance from one captured Windows handle.</summary>
    public static bool TryClearInheritance(IntPtr handle)
    {
        return !OperatingSystem.IsWindows() ||
            WindowsContainedProcessStarter.TryClearInheritance(handle);
    }
}

