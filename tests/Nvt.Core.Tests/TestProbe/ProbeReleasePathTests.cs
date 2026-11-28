// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Xunit;

namespace Nvt.Core.Tests.TestProbe;

/// <summary>The probe rejects a release path with a missing directory before it writes or starts anything.</summary>
[Collection(nameof(ProbeEnvironmentGroup))]
public sealed class ProbeReleasePathTests
{
    // The probe runs in the empty workspace, so this directory does not exist.
    private const string MissingRelease = "missing/release.flag";

    /// <summary>A waiting mode rejects a bad release path argument.</summary>
    /// <param name="arguments">The mode and its required inputs.</param>
    [Theory]
    [InlineData(new object[] { new[] { "--mode", "silent-wait" } })]
    [InlineData(new object[] { new[] { "--mode", "tree-grandchild", "--tree-marker", "leaf.pid" } })]
    [InlineData(new object[] { new[] { "--mode", "tree-root-wait", "--tree-marker", "leaf.pid" } })]
    [InlineData(new object[] { new[] { "--mode", "orphan-chain-root", "--tree-marker", "leaf.pid" } })]
    [InlineData(new object[] { new[] { "--mode", "hold-lock", "--lock-path", "lock.dat", "--lock-ready", "lock.ready" } })]
    public async Task AWaitingModeRejectsAMissingReleaseDirectoryFirst(string[] arguments)
    {
        await using var workspace = new ProbeWorkspace();
        await RejectedAsync(workspace, workspace.Start([.. arguments, "--release-path", MissingRelease]));
    }

    /// <summary>A mode that starts a child rejects a bad inherited value, even with a valid argument.</summary>
    /// <param name="mode">The probe mode.</param>
    [Theory]
    [InlineData("tree-root-exit")]
    [InlineData("tree-root-wait")]
    [InlineData("orphan-chain-root")]
    [InlineData("orphan-chain-exit")]
    [InlineData("detached-descendant-root")]
    [InlineData("ready-tree-root")]
    public async Task AModeThatStartsAChildRejectsABadInheritedReleasePathFirst(string mode)
    {
        await using var workspace = new ProbeWorkspace();
        // The child receives only the environment value.
        var process = workspace.Start(["--mode", mode, "--tree-marker", "leaf.pid", "--release-path", "release.flag"],
            info => info.Environment["CORE_TEST_PROBE_RELEASE_PATH"] = MissingRelease);
        await RejectedAsync(workspace, process);
    }

    private static async Task RejectedAsync(ProbeWorkspace workspace, Process process)
    {
        await ProbeWorkspace.ExitAsync(process, 64);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        string error = await ProbeWorkspace.ReadAsync(process.StandardError);
        Assert.Contains("release-path", error, StringComparison.Ordinal);
        Assert.Single(error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Empty(Directory.GetFiles(workspace.Root));
    }
}
