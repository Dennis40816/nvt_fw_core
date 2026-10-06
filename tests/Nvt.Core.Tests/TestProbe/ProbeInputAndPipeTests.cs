// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Xunit;

namespace Nvt.Core.Tests.TestProbe;

[CollectionDefinition(nameof(ProbeEnvironmentGroup), DisableParallelization = true)]
public sealed class ProbeEnvironmentGroup;

[Collection(nameof(ProbeEnvironmentGroup))]
public sealed class ProbeInputTests
{
    [Theory]
    [InlineData(new string[] { }, "mode")]
    [InlineData(new[] { "--mode", "unknown" }, "mode")]
    [InlineData(new[] { "--mode" }, "mode")]
    [InlineData(new[] { "--mode", "arguments-environment" }, "marker")]
    [InlineData(new[] { "--mode", "arguments-environment", "--marker", "unused" }, "text")]
    [InlineData(new[] { "--mode", "exit", "--exit-code", "invalid" }, "exit-code")]
    public async Task InvalidInputsReturnUsageCodeAndOneErrorLine(string[] arguments, string input)
    {
        await using var workspace = new ProbeWorkspace();
        var process = workspace.Start(arguments);
        await ProbeWorkspace.ExitAsync(process, 64);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        string error = await ProbeWorkspace.ReadAsync(process.StandardError);
        Assert.Contains(input, error, StringComparison.Ordinal);
        Assert.Single(error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Empty(Directory.GetFiles(workspace.Root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(17)]
    public async Task ExitReturnsRequestedCodeWithoutOutput(int code)
    {
        await using var workspace = new ProbeWorkspace();
        var process = workspace.Start(["--mode", "exit", "--exit-code", code.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        await ProbeWorkspace.ExitAsync(process, code);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
        Assert.Empty(Directory.GetFiles(workspace.Root));
    }

    [Fact]
    public async Task EnvironmentInputsPreserveUnicodeAndArgumentInputsWin()
    {
        string[] keys = ["CORE_TEST_PROBE_MODE", "CORE_TEST_PROBE_MARKER", "CORE_TEST_PROBE_TEXT"];
        string?[] previous = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        await using var workspace = new ProbeWorkspace();
        string marker = workspace.PathFor("arguments.txt");
        string[] payload = ["first value", "quote\"inside", "trailing\\", "", "--unknown", "value"];
        try
        {
            Environment.SetEnvironmentVariable(keys[0], "arguments-environment");
            Environment.SetEnvironmentVariable(keys[1], workspace.PathFor("unused.txt"));
            Environment.SetEnvironmentVariable(keys[2], "\u74B0\u5883 \u503C \u2713");
            var process = workspace.Start(["--marker", marker, .. payload], info =>
            {
                foreach (string key in keys)
                {
                    info.Environment[key] = Environment.GetEnvironmentVariable(key);
                }
            });
            await ProbeWorkspace.ExitAsync(process, 0);
            await ProbeWorkspace.FileAsync(marker, string.Join(Environment.NewLine,
                ["\u74B0\u5883 \u503C \u2713", workspace.Root, .. payload]) + Environment.NewLine);
            Assert.Equal([marker], Directory.GetFiles(workspace.Root));
            Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
            Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));

            var overrideProcess = workspace.Start(["--mode", "exit", "--exit-code", "9"], info =>
            {
                info.Environment[keys[0]] = "unknown";
                info.Environment["CORE_TEST_PROBE_EXIT_CODE"] = "3";
            });
            await ProbeWorkspace.ExitAsync(overrideProcess, 9);
        }
        finally
        {
            for (int index = 0; index < keys.Length; index++)
            {
                Environment.SetEnvironmentVariable(keys[index], previous[index]);
            }
        }
    }

    [Fact]
    public async Task RenamedApphostRunsFromAnotherFolder()
    {
        await using var workspace = new ProbeWorkspace();
        workspace.CopyAndRename();
        string marker = workspace.PathFor("arguments.txt");
        var process = workspace.Start(["--mode", "arguments-environment", "--marker", marker,
            "--text", "synthetic", "payload"]);
        await ProbeWorkspace.ExitAsync(process, 0);
        await ProbeWorkspace.FileAsync(marker, string.Join(Environment.NewLine, "synthetic", workspace.Root, "payload") + Environment.NewLine);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    [Fact]
    public async Task HandleModesReportUnsupportedSystems()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "This check requires a non-Windows system.");
        await using var workspace = new ProbeWorkspace();
        foreach (string mode in new[] { "ambient-pipe", "contained-isolation", "ready", "ready-wrong-identity",
            "ready-partial", "invalid-utf8", "oversized", "ready-tree-root", "tree-root-exit", "tree-root-wait",
            "orphan-chain-root", "detached-descendant-root" })
        {
            var process = workspace.Start(["--mode", mode]);
            await ProbeWorkspace.ExitAsync(process, 64);
            Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
            Assert.Contains("Windows", await ProbeWorkspace.ReadAsync(process.StandardError), StringComparison.Ordinal);
        }
        var jobProcess = workspace.Start(["--mode", "exit"], info => info.Environment["CORE_TEST_LIFETIME_JOB"] = "unused");
        await ProbeWorkspace.ExitAsync(jobProcess, 64);
    }
}

[Collection(nameof(ProbeEnvironmentGroup))]
public sealed class ProbePipeTests
{
    [Theory]
    [InlineData("ambient-pipe")]
    [InlineData("contained-isolation")]
    public async Task PipeModesWriteExactUtf8Bytes(string mode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Inherited pipe handles require Windows.");
        await using var workspace = new ProbeWorkspace();
        using var allowed = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var cross = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        string marker = workspace.PathFor("started.txt");
        string[] arguments = mode == "ambient-pipe"
            ? ["--mode", mode, "--marker", marker, "--ambient-handle", allowed.GetClientHandleAsString()]
            : ["--mode", mode, "--payload", "payload \u2713", "--allowed-handle", allowed.GetClientHandleAsString(),
                "--cross-handle", cross.GetClientHandleAsString()];
        var process = workspace.Start(arguments, info => info.Environment["CORE_TEST_LIFETIME_JOB"] = "missing-job");
        allowed.DisposeLocalCopyOfClientHandle();
        cross.DisposeLocalCopyOfClientHandle();
        await ProbeWorkspace.ExitAsync(process, 0);
        Assert.Equal(Encoding.UTF8.GetBytes(mode == "ambient-pipe" ? "leaked" : "payload \u2713"), await ProbeWorkspace.ReadAsync(allowed));
        Assert.Equal(mode == "ambient-pipe" ? [] : Encoding.UTF8.GetBytes("cross:payload \u2713"), await ProbeWorkspace.ReadAsync(cross));
        if (mode == "ambient-pipe")
        {
            await ProbeWorkspace.FileAsync(marker, "started");
            Assert.Equal([marker], Directory.GetFiles(workspace.Root));
        }
        else
        {
            Assert.Empty(Directory.GetFiles(workspace.Root));
        }
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    [Theory]
    [InlineData("ambient-pipe", "-1")]
    [InlineData("ambient-pipe", "0")]
    [InlineData("ambient-pipe", "invalid")]
    [InlineData("contained-isolation", "invalid")]
    [InlineData("contained-isolation", "0")]
    public async Task UnusableOptionalPipesDoNotProduceErrors(string mode, string handle)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Inherited pipe handles require Windows.");
        await using var workspace = new ProbeWorkspace();
        string marker = workspace.PathFor("started.txt");
        var process = workspace.Start(mode == "ambient-pipe"
            ? ["--mode", mode, "--marker", marker, "--ambient-handle", handle]
            : ["--mode", mode, "--payload", "payload", "--allowed-handle", handle]);
        await ProbeWorkspace.ExitAsync(process, 0);
        if (mode == "ambient-pipe")
        {
            await ProbeWorkspace.FileAsync(marker, "started");
        }
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 7)]
    public async Task PipeHoldingDescendantKeepsBothRedirectedStreamsOpen(bool renamed, int code)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Descendant handle inheritance requires Windows.");
        await using var workspace = new ProbeWorkspace();
        if (renamed)
        {
            workspace.CopyAndRename();
        }
        string marker = workspace.PathFor("leaf marker.pid");
        workspace.WatchTree(marker);
        var process = workspace.Start(["--mode", "tree-root-exit", "--tree-marker", marker,
            "--stdout-text", "root line \u2713", "--exit-code", code.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var child = await workspace.ChildAsync(marker);
        await ProbeWorkspace.ExitAsync(process, code);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(child.HasExited);
        Assert.False(output.IsCompleted);
        Assert.False(error.IsCompleted);
        await ProbeWorkspace.KillAsync(child);
        Assert.Equal("root line \u2713" + Environment.NewLine, await output.WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
        Assert.Empty(await error.WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
        await ProbeWorkspace.FileAsync(marker, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task DetachedDescendantLeavesBothRedirectedStreamsAtEndOfStream()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Detached descendants require Windows.");
        await using var workspace = new ProbeWorkspace();
        string marker = workspace.PathFor("leaf.pid");
        workspace.WatchTree(marker);
        var process = workspace.Start(["--mode", "detached-descendant-root", "--tree-marker", marker]);
        var child = await workspace.ChildAsync(marker);
        await ProbeWorkspace.ExitAsync(process, 0);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
        Assert.False(child.HasExited);
        await ProbeWorkspace.FileAsync(marker, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task OrphanLeafKeepsStreamsOpenAfterRootAndMiddleExit()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Orphan descendants require Windows.");
        await using var workspace = new ProbeWorkspace();
        string marker = workspace.PathFor("leaf.pid");
        workspace.WatchTree(marker);
        var process = workspace.Start(["--mode", "orphan-chain-root", "--tree-marker", marker]);
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await ProbeWorkspace.MarkerAsync(marker + ".ready");
        var leaf = await workspace.ChildAsync(marker);
        await ProbeWorkspace.KillAsync(process);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(leaf.HasExited);
        Assert.False(output.IsCompleted);
        Assert.False(error.IsCompleted);
        await ProbeWorkspace.KillAsync(leaf);
        Assert.Empty(await output.WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
        Assert.Empty(await error.WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
    }
}