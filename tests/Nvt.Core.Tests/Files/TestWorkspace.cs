// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Files;

/// <summary>Owns a unique temporary fixture for Files tests.</summary>
internal sealed class TestWorkspace : IDisposable
{
    internal TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), $"nvt-core-files-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string PathFor(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    internal string Write(string relativePath, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string path = PathFor(relativePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Deletes the fixture, retrying Windows sharing failures for at most 450 ms.</summary>
    public void Dispose()
    {
        long retryDeadline = Environment.TickCount64 + 450;
        while (Directory.Exists(Root))
        {
            try
            {
                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (IOException) when (OperatingSystem.IsWindows() && Environment.TickCount64 < retryDeadline)
            {
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && Environment.TickCount64 < retryDeadline)
            {
            }

            long remainingMilliseconds = retryDeadline - Environment.TickCount64;
            if (remainingMilliseconds > 0)
            {
                Thread.Sleep((int)Math.Min(50, remainingMilliseconds));
            }
        }
    }
}
