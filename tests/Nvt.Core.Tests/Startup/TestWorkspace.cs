// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Startup;

internal sealed class TestWorkspace : IDisposable
{
    private readonly string _rootPath = Directory.CreateTempSubdirectory("startup-trace-").FullName;

    internal string PathFor(string relative)
    {
        return Path.Combine(_rootPath, relative);
    }

    /// <summary>Deletes the temporary workspace, briefly retrying sharing failures on Windows.</summary>
    public void Dispose()
    {
        string resolvedRoot = Path.GetFullPath(_rootPath);
        string temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolvedRoot.StartsWith(temporaryRoot, comparison))
        {
            throw new InvalidOperationException("The test workspace must remain within the temporary directory.");
        }

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(resolvedRoot, recursive: true);
                return;
            }
            catch (Exception exception) when (OperatingSystem.IsWindows() && attempt < 2 &&
                exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(25);
            }
        }
    }
}
