// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.SourceFileNavigation;

/// <summary>
/// Starts an executable or opens a source file with the default application.
/// </summary>
public static class SourceFileNavigator
{
    /// <summary>
    /// Attempts to start an executable without shell execution.
    /// </summary>
    /// <param name="executable">The executable name or path passed to the process starter.</param>
    /// <param name="arguments">Arguments added individually, in order, to the process argument list.</param>
    /// <param name="error">The handled exception's message on failure; otherwise <see langword="null"/>.</param>
    /// <returns>Whether the process starter returned normally, even if it returned no process.</returns>
    /// <remarks>
    /// Only <see cref="InvalidOperationException"/> and <see cref="System.ComponentModel.Win32Exception"/>
    /// are converted to failure results. Other exceptions propagate to the caller.
    /// </remarks>
    public static bool TryStart(string executable, IReadOnlyList<string> arguments, out string? error) =>
        TryStart(executable, arguments, Process.Start, out error);

    /// <summary>
    /// Attempts to open a file using shell execution and its default application.
    /// </summary>
    /// <param name="path">The file path passed unchanged to the process starter.</param>
    /// <returns>
    /// An outcome identifying <c>default application</c>, with <c>ExactLine</c> always false.
    /// A normal return from the process starter counts as success even if it returned no process.
    /// </returns>
    /// <remarks>
    /// Path validation, normalization and existence checks belong to the caller.
    /// Only <see cref="InvalidOperationException"/> and <see cref="System.ComponentModel.Win32Exception"/>
    /// are converted to failure results. Other exceptions propagate to the caller.
    /// </remarks>
    public static SourceFileOpenResult OpenDefault(string path) => OpenDefault(path, Process.Start);

    internal static bool TryStart(
        string executable,
        IReadOnlyList<string> arguments,
        Func<ProcessStartInfo, Process?> startProcess,
        out string? error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            startProcess(startInfo);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static SourceFileOpenResult OpenDefault(string path, Func<ProcessStartInfo, Process?> startProcess)
    {
        try
        {
            startProcess(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            return new SourceFileOpenResult(true, false, "default application");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new SourceFileOpenResult(false, false, "default application", exception.Message);
        }
    }
}
