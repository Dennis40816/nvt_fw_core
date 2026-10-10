// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.TestSupport;

namespace Nvt.Core.Tests.Processes;

/// <summary>Uses the complete shared probe output without creating a child harness.</summary>
internal static class ProcessProbe
{
    internal static readonly TimeSpan FixtureBound = TimeSpan.FromSeconds(30);

    internal static string Executable { get; } = Path.Combine(AppContext.BaseDirectory, "probe",
        OperatingSystem.IsWindows() ? "Nvt.Core.TestProbe.exe" : "Nvt.Core.TestProbe");

    internal static ProcessStartInfo Create(string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string key in startInfo.Environment.Keys
                     .Where(static key => key.StartsWith("CORE_TEST_", StringComparison.Ordinal)).ToArray())
        {
            _ = startInfo.Environment.Remove(key);
        }
        startInfo.Environment["CORE_TEST_PROBE_MODE"] = mode;
        return startInfo;
    }

    internal static string Write(TestWorkspace workspace, string relativePath, byte[] bytes)
    {
        string path = workspace.GetPath(relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestFiles.WriteAllBytes(path, bytes);
        return path;
    }

    internal static string CopyAndRename(TestWorkspace workspace, string name)
    {
        string source = Path.GetDirectoryName(Executable)!;
        string target = workspace.GetPath("complete probe");
        _ = Directory.CreateDirectory(target);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            _ = Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
        string renamed = Path.Combine(target, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Move(Path.Combine(target, Path.GetFileName(Executable)), renamed);
        return renamed;
    }
}
