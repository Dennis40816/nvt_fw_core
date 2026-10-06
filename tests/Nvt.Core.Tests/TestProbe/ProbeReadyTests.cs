// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Xunit;

namespace Nvt.Core.Tests.TestProbe;

[Collection(nameof(ProbeEnvironmentGroup))]
public sealed class ProbeReadyTests
{
    [Theory]
    [InlineData("ready", true)]
    [InlineData("ready", false)]
    [InlineData("ready-wrong-identity", true)]
    [InlineData("ready-wrong-identity", false)]
    [InlineData("ready-partial", true)]
    [InlineData("ready-partial", false)]
    [InlineData("invalid-utf8", true)]
    [InlineData("invalid-utf8", false)]
    [InlineData("oversized", true)]
    [InlineData("oversized", false)]
    [InlineData("ready-tree-root", true)]
    [InlineData("ready-tree-root", false)]
    public async Task ReadyModesWriteExactFramesAndPreludeFiles(string mode, bool application)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "READY pipes require Windows.");
        await using var workspace = new ProbeWorkspace();
        using var ready = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        string processMarker = workspace.PathFor("process.pid");
        string identityMarker = workspace.PathFor("identity.txt");
        string argsPath = workspace.PathFor("args.txt");
        string treeMarker = workspace.PathFor("tree.pid");
        workspace.WatchTree(treeMarker);
        string[] payload = ["--state-path", "state value", "", "quote\"inside", "trailing\\"];
        var clock = Stopwatch.StartNew();
        var process = workspace.Start(payload, info =>
        {
            info.Environment["CORE_TEST_PROBE_MODE"] = mode;
            info.Environment[application ? "CORE_TEST_APP_READY_HANDLE" : "CORE_TEST_LAUNCHER_READY_HANDLE"] = ready.GetClientHandleAsString();
            info.Environment["CORE_TEST_LAUNCHER_EXPECTED_READY"] = "READY:role:abc0";
            if (application)
            {
                info.Environment["CORE_TEST_APP_EXPECTED_VERSION"] = "3.20";
            }
            info.Environment["CORE_TEST_PROBE_APP_VERSION"] = "5.0";
            info.Environment["CORE_TEST_PROBE_APP_ADMISSION"] = "admission \u2713";
            info.Environment["CORE_TEST_PROBE_APP_MANIFEST"] = "manifest";
            info.Environment["CORE_TEST_PROBE_PROCESS_MARKER"] = processMarker;
            info.Environment["CORE_TEST_PROBE_IDENTITY_MARKER"] = identityMarker;
            info.Environment["CORE_TEST_PROBE_ARGS_PATH"] = argsPath;
            info.Environment["CORE_TEST_PROBE_TREE_MARKER"] = treeMarker;
            info.Environment["CORE_TEST_BOOTSTRAP_IDENTITY"] = "1|helper|4|abcd";
        });
        ready.DisposeLocalCopyOfClientHandle();
        byte[] bytes = await ProbeWorkspace.ReadAsync(ready);
        await ProbeWorkspace.ExitAsync(process, 0);
        string expected = application ? "READY:3.20" : "READY:role:abc0:5.0:YWRtaXNzaW9uIOKckw==:manifest";
        if (mode == "ready-wrong-identity")
        {
            expected = application ? "READY:3.21" : "READY:role:abc1:5.0:YWRtaXNzaW9uIOKckw==:manifest";
        }
        Assert.Equal(mode switch
        {
            "invalid-utf8" => new byte[] { 0xC3, 0x28, 0x0A },
            "oversized" => Encoding.UTF8.GetBytes(new string('X', 256) + "\n"),
            "ready-partial" => Encoding.UTF8.GetBytes(expected),
            _ => Encoding.UTF8.GetBytes(expected + "\n"),
        }, bytes);
        await ProbeWorkspace.FileAsync(processMarker, process.Id.ToString(CultureInfo.InvariantCulture));
        await ProbeWorkspace.FileAsync(identityMarker, "1|helper|4|abcd" + Environment.NewLine);
        await ProbeWorkspace.FileAsync(argsPath, string.Join(Environment.NewLine, payload) + Environment.NewLine);
        if (mode == "ready-tree-root")
        {
            await ProbeWorkspace.FileAsync(treeMarker + ".root", process.Id.ToString(CultureInfo.InvariantCulture));
            var child = await workspace.ChildAsync(treeMarker + ".child");
            Assert.False(child.HasExited);
            await ProbeWorkspace.KillAsync(child);
        }
        else
        {
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(200));
            Assert.False(File.Exists(treeMarker));
        }
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    [Theory]
    [InlineData("ready-partial", "partial-drop", "4", "READY:")]
    [InlineData("ready-partial", "partial-drop", "100", "")]
    [InlineData("oversized", "oversize-chars", "4097", null)]
    [InlineData("ready-wrong-identity", "partial-drop", "0", "READY:1.00\n")]
    public async Task ReadyOptionsUseArgumentsAndKeepExactFraming(string mode, string option, string value, string? expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "READY pipes require Windows.");
        await using var workspace = new ProbeWorkspace();
        using var ready = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        string identityMarker = workspace.PathFor("identity.txt");
        var process = workspace.Start(["--mode", mode, "--" + option, value, "--identity-marker", identityMarker], info =>
        {
            info.Environment["CORE_TEST_APP_READY_HANDLE"] = ready.GetClientHandleAsString();
            info.Environment["CORE_TEST_APP_EXPECTED_VERSION"] = "1.01";
            info.Environment["CORE_TEST_PROBE_OVERSIZE_CHARS"] = "256";
        });
        ready.DisposeLocalCopyOfClientHandle();
        byte[] bytes = await ProbeWorkspace.ReadAsync(ready);
        await ProbeWorkspace.ExitAsync(process, 0);
        Assert.Equal(Encoding.UTF8.GetBytes(expected ?? new string('X', 4097) + "\n"), bytes);
        await ProbeWorkspace.FileAsync(identityMarker, "<null>" + Environment.NewLine);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    [Theory]
    [InlineData("ready", "CORE_TEST_LAUNCHER_EXPECTED_READY")]
    [InlineData("ready-partial", "CORE_TEST_LAUNCHER_EXPECTED_READY")]
    public async Task MissingProtocolInputsReturnUsageCode(string mode, string input)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "READY pipes require Windows.");
        await using var workspace = new ProbeWorkspace();
        var process = workspace.Start(["--mode", mode]);
        await ProbeWorkspace.ExitAsync(process, 64);
        Assert.Contains(input, await ProbeWorkspace.ReadAsync(process.StandardError), StringComparison.Ordinal);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
    }
}