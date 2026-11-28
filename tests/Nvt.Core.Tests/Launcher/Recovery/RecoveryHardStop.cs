// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using System.Text.Json;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

internal static class RecoveryHardStop
{
    internal static ChildProcessFixture Start(string mode, string statePath, string boundary, string marker) =>
        ChildProcessFixture.Start(Path.Combine(AppContext.BaseDirectory, "linked-probe", "Nvt.Core.LinkedProbe.exe"),
            ["--mode", mode, "--state-path", statePath, "--boundary", boundary, "--marker", marker,
                "--pause", Assembly.GetExecutingAssembly().Location], watchdog: TimeSpan.FromSeconds(30));

    internal static async Task StopAsync(ChildProcessFixture child, string marker, string boundary)
    {
        try
        {
            await child.WaitForOutputAsync("READY:" + boundary + "\n", TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            Assert.Fail(exception.Message + "\n" + child.Output);
        }
        Assert.Equal(boundary, await SetupFixture.TextAsync(marker));
        Assert.False(child.HasExited);
        child.KillTree();
        await SignalWait.WaitAsync(child.WaitForExitAsync(), "hard-stopped recovery child",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(child.WatchdogExpired);
    }

    internal static void WriteJson<T>(string path, T value) => TestFiles.WriteAllBytes(path,
        JsonSerializer.SerializeToUtf8Bytes(value));

    internal static async Task<T> ReadJsonAsync<T>(string path) =>
        JsonSerializer.Deserialize<T>(await SetupFixture.ReadAsync(path))!;

    internal static SortedDictionary<string, string> Snapshot(string parent) =>
        new(Directory.EnumerateFileSystemEntries(parent, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("hard-stop", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(parent, path).Replace('\\', '/'),
                path => Directory.Exists(path) ? "directory" : HashFile(path)), StringComparer.Ordinal);

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
