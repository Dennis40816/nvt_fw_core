// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit;

namespace Nvt.Core.Tests.TestProbe;

internal sealed class ProbeWorkspace : IAsyncDisposable
{
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
    private readonly List<Process> _processes = [];
    private readonly HashSet<string> _pidMarkers = new(StringComparer.Ordinal);
    internal string Root { get; } = Directory.CreateTempSubdirectory("core-probe-").FullName;
    internal string Executable { get; private set; } = Path.Combine(AppContext.BaseDirectory, "probe",
        OperatingSystem.IsWindows() ? "Nvt.Core.TestProbe.exe" : "Nvt.Core.TestProbe");

    internal string PathFor(string name) => Path.Combine(Root, name);

    internal void WatchTree(string marker)
    {
        foreach (string suffix in new[] { "", ".root", ".child", ".middle" })
        {
            _pidMarkers.Add(marker + suffix);
        }
    }

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
        foreach (string key in info.Environment.Keys.Where(key => key.StartsWith("CORE_TEST_", StringComparison.Ordinal)).ToArray())
        {
            info.Environment.Remove(key);
        }
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

    internal static async Task ExitAsync(Process process, int expected, int seconds = 5)
    {
        await process.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(seconds), TestContext.Current.CancellationToken);
        Assert.Equal(expected, process.ExitCode);
    }

    internal static async Task<string> ReadAsync(StreamReader reader) =>
        await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Bound, TestContext.Current.CancellationToken);

    internal static async Task<byte[]> ReadAsync(Stream stream)
    {
        using var bytes = new MemoryStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Bound);
        await stream.CopyToAsync(bytes, timeout.Token).WaitAsync(Bound, TestContext.Current.CancellationToken);
        return bytes.ToArray();
    }

    internal static async Task<string> MarkerAsync(string path)
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                string text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
                if (text.Length != 0)
                {
                    return text;
                }
            }
            catch (IOException)
            {
                // The child can still be completing its marker write.
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("The child did not write its marker within five seconds.");
    }

    internal async Task<Process> ChildAsync(string marker)
    {
        string text = await MarkerAsync(marker);
        int pid = int.Parse(text, CultureInfo.InvariantCulture);
        var child = Process.GetProcessById(pid);
        _processes.Add(child);
        return child;
    }

    internal static async Task FileAsync(string path, string expected) =>
        Assert.Equal(Encoding.UTF8.GetBytes(expected), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

    internal static async Task KillAsync(Process process)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited between the observation and the kill.
            }
        }
        await process.WaitForExitAsync(CancellationToken.None).WaitAsync(Bound, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        foreach (var process in _processes)
        {
            try
            {
                await KillAsync(process);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        // An exited middle process can leave a leaf outside the root's current process tree.
        foreach (string marker in _pidMarkers)
        {
            try
            {
                if (File.Exists(marker) && int.TryParse(await File.ReadAllTextAsync(marker, CancellationToken.None),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                {
                    using var child = Process.GetProcessById(pid);
                    await KillAsync(child);
                }
            }
            catch (ArgumentException)
            {
                // The recorded process has already exited.
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        foreach (var process in _processes)
        {
            process.Dispose();
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
                await Task.Delay(25, CancellationToken.None);
            }
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Probe cleanup failed.", errors);
        }
    }
}