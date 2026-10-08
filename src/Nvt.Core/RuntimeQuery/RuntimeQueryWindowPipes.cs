// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Nvt.Core.RuntimeQuery;

/// <summary>Names and discovers one local pipe per tool process.</summary>
public static class RuntimeQueryWindowPipes
{
    /// <summary>Builds {baseName}.{processId} with a nonblank base name and a positive process ID.</summary>
    public static string BuildName(string baseName, int processId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        return baseName + "." + processId.ToString(CultureInfo.InvariantCulture);
    }

    internal static IReadOnlyList<(int ProcessId, DateTime StartTime, string PipeName)> DiscoverCandidates(string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        var candidates = new List<(int ProcessId, DateTime StartTime, string PipeName)>();
        if (!OperatingSystem.IsWindows())
        {
            return candidates;
        }

        var prefix = baseName + ".";
        try
        {
            foreach (var path in Directory.EnumerateFiles(@"\\.\pipe\"))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
                    processId <= 0)
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(processId);
                    var startTime = process.StartTime.ToUniversalTime();
                    if (!process.HasExited)
                    {
                        candidates.Add((processId, startTime, name));
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // A process can exit or deny access between enumeration and the start-time read.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unavailable local pipe directory yields no further candidates.
        }

        return candidates;
    }

    internal static int? SelectProcessId(
        IEnumerable<(int ProcessId, DateTime StartTime)> candidates, int? requestedProcessId = null)
    {
        (int ProcessId, DateTime StartTime)? selected = null;
        foreach (var candidate in candidates)
        {
            if (requestedProcessId is not null)
            {
                if (candidate.ProcessId == requestedProcessId)
                {
                    return candidate.ProcessId;
                }
            }
            else if (selected is null || candidate.StartTime > selected.Value.StartTime ||
                (candidate.StartTime == selected.Value.StartTime && candidate.ProcessId > selected.Value.ProcessId))
            {
                selected = candidate;
            }
        }

        return selected?.ProcessId;
    }
}
