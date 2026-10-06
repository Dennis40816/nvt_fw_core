// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Processes;

/// <summary>A unique temporary folder for synthetic process fixtures.</summary>
internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string root)
    {
        Root = root;
    }

    internal string Root { get; }

    internal static TestWorkspace Create()
    {
        return new TestWorkspace(Directory.CreateTempSubdirectory("core-process-").FullName);
    }

    internal string PathFor(string relativePath)
    {
        return Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    internal string Write(string relativePath, byte[] bytes)
    {
        string path = PathFor(relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Deletes only this fixture's temporary folder, retrying transient Windows sharing failures.</summary>
    public void Dispose()
    {
        string resolved = Path.GetFullPath(Root);
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) +
            Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temp, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The test folder must remain inside the temporary directory.");
        }
        long retryDeadline = Environment.TickCount64 + 450;
        while (Directory.Exists(resolved))
        {
            try
            {
                Directory.Delete(resolved, recursive: true);
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
