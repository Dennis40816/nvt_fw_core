// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace Nvt.Core.Tests.TestProbe;

[Collection(nameof(ProbeEnvironmentGroup))]
public sealed class ProbeLifetimeTests
{
    private static readonly string[] PortableWaitingModes = ["silent-wait", "tree-grandchild", "hold-lock"];
    private static readonly string[] WindowsWaitingModes = ["tree-root-wait", "orphan-chain-root"];

    // Each waiting mode lasts 30 seconds, so one test runs its modes at the same time.
    [Fact]
    public Task PortableWaitingModesWriteTheirMarkersAndReturnZeroAfterThirtySeconds() =>
        Task.WhenAll(PortableWaitingModes.Select(WaitingModeAsync));

    [Fact]
    public Task WindowsWaitingModesWriteTheirMarkersAndReturnZeroAfterThirtySeconds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Descendant handle inheritance requires Windows.");
        return Task.WhenAll(WindowsWaitingModes.Select(WaitingModeAsync));
    }

    private static async Task WaitingModeAsync(string mode)
    {
        await using var workspace = new ProbeWorkspace();
        string marker = workspace.PathFor("leaf.pid");
        string lockPath = workspace.PathFor("lock.dat");
        string lockReady = workspace.PathFor("lock.ready");
        workspace.WatchTree(marker);
        string[] arguments = mode == "hold-lock"
            ? ["--mode", mode, "--lock-path", lockPath, "--lock-ready", lockReady]
            : mode == "silent-wait" ? ["--mode", mode] : ["--mode", mode, "--tree-marker", marker];
        var clock = Stopwatch.StartNew();
        var process = workspace.Start(arguments, info =>
        {
            if (mode == "tree-grandchild")
            {
                info.Environment["CORE_TEST_LIFETIME_JOB"] = "missing-job";
            }
        });
        if (mode == "hold-lock")
        {
            Assert.Equal("STARTED", await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
            await ProbeWorkspace.MarkerAsync(lockReady);
            Assert.Equal("LOCK_HELD", await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
            await ProbeWorkspace.FileAsync(lockReady, "ready");
            Assert.Throws<IOException>(() => { using var contender = File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        }
        else if (mode != "silent-wait")
        {
            await ProbeWorkspace.MarkerAsync(marker);
            string pid = await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken);
            Assert.True(int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out int id));
            Assert.Equal(id.ToString(CultureInfo.InvariantCulture), pid);
            if (mode == "tree-grandchild")
            {
                Assert.Equal(process.Id, id);
            }
            else
            {
                var child = await workspace.ChildAsync(marker);
                Assert.False(child.HasExited);
                Assert.NotEqual(process.Id, child.Id);
            }
            if (mode == "orphan-chain-root")
            {
                await ProbeWorkspace.MarkerAsync(marker + ".ready");
                await ProbeWorkspace.FileAsync(marker + ".ready", "ready");
                int middleId = int.Parse(await File.ReadAllTextAsync(marker + ".middle", TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
                try
                {
                    using var middle = Process.GetProcessById(middleId);
                    Assert.True(middle.HasExited);
                }
                catch (ArgumentException)
                {
                    // The process table no longer contains the exited middle process.
                }
            }
        }
        await ProbeWorkspace.ExitAsync(process, 0, 40);
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(30));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
        if (mode == "hold-lock")
        {
            using var released = File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(0, released.Length);
        }
        if (mode == "silent-wait")
        {
            Assert.Empty(Directory.GetFiles(workspace.Root));
        }
    }

    [Fact]
    public async Task ContendedLockReturnsOneAndWritesOnlyStarted()
    {
        await using var workspace = new ProbeWorkspace();
        string path = workspace.PathFor("lock.dat");
        string ready = workspace.PathFor("lock.ready");
        using var held = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var process = workspace.Start(["--mode", "hold-lock", "--lock-path", path, "--lock-ready", ready]);
        await ProbeWorkspace.ExitAsync(process, 1);
        Assert.Equal("STARTED" + Environment.NewLine, await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.NotEmpty(await ProbeWorkspace.ReadAsync(process.StandardError));
        Assert.False(File.Exists(ready));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedJobJoinReturnsTwentyFourAndOneErrorLine(bool invalidHandle)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named Jobs require Windows.");
        await using var workspace = new ProbeWorkspace();
        using var job = new ProbeJob();
        var process = workspace.Start(["--mode", "exit"], info =>
        {
            info.Environment["CORE_TEST_LIFETIME_JOB"] = invalidHandle ? job.Name : "missing-" + Guid.NewGuid().ToString("N");
            if (invalidHandle)
            {
                info.Environment["CORE_TEST_LIFETIME_HANDLE"] = "123456789";
            }
        });
        await ProbeWorkspace.ExitAsync(process, 24);
        string error = await ProbeWorkspace.ReadAsync(process.StandardError);
        Assert.Contains("Job join", error, StringComparison.Ordinal);
        Assert.Single(error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(Directory.GetFiles(workspace.Root));
    }

    [Fact]
    public async Task TerminatingJobEndsRootAndInheritedDescendant()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named Jobs require Windows.");
        await using var workspace = new ProbeWorkspace();
        using var job = new ProbeJob();
        string marker = workspace.PathFor("leaf.pid");
        workspace.WatchTree(marker);
        var root = workspace.Start(["--mode", "tree-root-wait", "--tree-marker", marker], info =>
        {
            info.Environment["CORE_TEST_LIFETIME_JOB"] = job.Name;
            info.Environment["CORE_TEST_LIFETIME_CONTEXT"] = "synthetic";
            info.Environment["CORE_TEST_LIFETIME_STATE_PATH"] = "synthetic";
            info.Environment["CORE_TEST_LIFETIME_KIND"] = "synthetic";
        });
        var child = await workspace.ChildAsync(marker);
        Assert.True(job.Contains(root));
        Assert.True(job.Contains(child));
        job.Terminate(42);
        await ProbeWorkspace.ExitAsync(root, 42);
        await ProbeWorkspace.ExitAsync(child, 42);
        Assert.Empty(await ProbeWorkspace.ReadAsync(root.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(root.StandardError));
    }
}