// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Nvt.Core.Tests.LinkedProbe;

internal sealed class LinkedProbeWorkspace : IAsyncDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
    private readonly List<Process> _processes = [];
    internal string Root { get; } = Directory.CreateTempSubdirectory("core-linked-probe-").FullName;
    internal string Executable { get; private set; } = Path.Combine(AppContext.BaseDirectory, "linked-probe",
        OperatingSystem.IsWindows() ? "Nvt.Core.LinkedProbe.exe" : "Nvt.Core.LinkedProbe");

    internal Assembly ChildAssembly => Assembly.LoadFrom(
        Path.Combine(Path.GetDirectoryName(Executable)!, "Nvt.Core.LinkedProbe.dll"));

    internal string PathFor(string name) => Path.Combine(Root, name);

    internal Process Start(string[] args, Action<ProcessStartInfo>? configure = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = Executable,
            WorkingDirectory = Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        RemoveProbeVariables(info.Environment);
        foreach (string argument in args)
        {
            info.ArgumentList.Add(argument);
        }
        configure?.Invoke(info);
        var process = new Process { StartInfo = info };
        _processes.Add(process);
        Assert.True(process.Start());
        return process;
    }

    /// <summary>Removes inherited probe inputs. Windows variable names ignore case, so any casing is removed.</summary>
    /// <param name="environment">The child environment to clean.</param>
    internal static void RemoveProbeVariables(IDictionary<string, string?> environment)
    {
        foreach (string key in environment.Keys.Where(key =>
            key.StartsWith("CORE_LINKED_PROBE_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("CORE_TEST_", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            environment.Remove(key);
        }
    }

    internal void CopyAndRename()
    {
        string source = Path.GetDirectoryName(Executable)!;
        string target = PathFor("renamed output");
        Directory.CreateDirectory(target);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
        string copied = Path.Combine(target, Path.GetFileName(Executable));
        Executable = Path.Combine(target, OperatingSystem.IsWindows() ? "worker helper.exe" : "worker helper");
        File.Move(copied, Executable);
    }

    internal static async Task ExitAsync(Process process, int expected)
    {
        await process.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(expected, process.ExitCode);
    }

    internal static async Task<string> ReadAsync(StreamReader reader) =>
        await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Bound, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        foreach (Process process in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                        // The child exited between the observation and the kill.
                    }
                }
                await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(Bound, TestContext.Current.CancellationToken);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            finally
            {
                process.Dispose();
            }
        }
        string resolved = Path.GetFullPath(Root);
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temp, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The test folder must remain inside the temporary directory.");
        }
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(resolved, recursive: true);
                break;
            }
            catch (Exception error) when (attempt < 4 && error is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Linked probe cleanup failed.", errors);
        }
    }
}
