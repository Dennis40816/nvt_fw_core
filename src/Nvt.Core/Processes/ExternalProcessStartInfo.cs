// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>Process launch request prepared by the host.</summary>
public sealed class ExternalProcessStartInfo
{
    /// <summary>Creates a process launch request with an argument list, not a shell command line.</summary>
    public ExternalProcessStartInfo(
        string executablePath,
        string workingDirectory,
        IEnumerable<string> arguments,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");
        }

        ExecutablePath = executablePath;
        WorkingDirectory = workingDirectory;
        Arguments = (string[])[.. arguments];
        Timeout = timeout;
    }

    /// <summary>Resolved executable path.</summary>
    public string ExecutablePath { get; }

    /// <summary>Directory used as the process working directory.</summary>
    public string WorkingDirectory { get; }

    /// <summary>Expanded arguments passed through ProcessStartInfo.ArgumentList.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Maximum process execution time.</summary>
    public TimeSpan Timeout { get; }
}
