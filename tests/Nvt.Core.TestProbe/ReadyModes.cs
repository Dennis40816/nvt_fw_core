// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using System.Text;

namespace Nvt.Core.TestProbe;

internal static class ReadyModes
{
    internal static async Task<int> RunAsync(string mode, ProbeInputs inputs)
    {
        Probe.RequireWindows();
        string? version = Environment.GetEnvironmentVariable("CORE_TEST_APP_EXPECTED_VERSION");
        bool application = version is not null;
        string identity = application ? version! : ProbeInputs.RequiredEnvironment("CORE_TEST_LAUNCHER_EXPECTED_READY");
        string handle = ProbeInputs.RequiredEnvironment(application
            ? "CORE_TEST_APP_READY_HANDLE" : "CORE_TEST_LAUNCHER_READY_HANDLE");
        if (mode == "ready-wrong-identity")
        {
            identity = identity.Length == 0 ? "0" : identity[..^1] + (identity[^1] == '0' ? "1" : "0");
        }
        string text = application ? "READY:" + identity : string.Join(':', identity,
            inputs.Required("app-version"),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(inputs.Required("app-admission"))),
            inputs.Required("app-manifest"));
        string? processMarker = inputs.Optional("process-marker");
        if (processMarker is not null)
        {
            await Probe.WriteProcessIdAsync(processMarker);
        }
        string? identityMarker = inputs.Optional("identity-marker");
        if (identityMarker is not null)
        {
            await File.WriteAllLinesAsync(identityMarker,
                [Environment.GetEnvironmentVariable("CORE_TEST_BOOTSTRAP_IDENTITY") ?? "<null>"]);
            Environment.SetEnvironmentVariable("CORE_TEST_BOOTSTRAP_IDENTITY", null);
        }
        string? argsPath = inputs.Optional("args-path");
        if (argsPath is not null)
        {
            await File.WriteAllLinesAsync(argsPath, inputs.Payload);
        }
        if (mode == "ready-tree-root")
        {
            string marker = inputs.Required("tree-marker");
            await Probe.WriteProcessIdAsync(marker + ".root");
            if (!OperatingSystem.IsWindows())
            {
                return 64;
            }
            using var child = WindowsProcess.StartDescendant("tree-grandchild", marker + ".child", holdStandardPipes: true);
            if (!await Probe.HandshakeAsync(() => File.Exists(marker + ".child")))
            {
                return 25;
            }
        }
        int drop = mode == "ready-partial" ? inputs.Integer("partial-drop", 0, 0) : 0;
        byte[] message = mode switch
        {
            "invalid-utf8" => [0xC3, 0x28, 0x0A],
            "oversized" => Encoding.UTF8.GetBytes(new string('X', inputs.Integer("oversize-chars", 256, 1)) + "\n"),
            "ready-partial" => Encoding.UTF8.GetBytes(text[..Math.Max(0, text.Length - drop)]),
            _ => Encoding.UTF8.GetBytes(text + "\n"),
        };
        await using (var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle))
        {
            await pipe.WriteAsync(message);
            await pipe.FlushAsync();
            if (mode is not ("ready-partial" or "ready-tree-root"))
            {
                await Task.Delay(Probe.ReadyLinger);
            }
        }
        if (mode == "ready-partial")
        {
            await Task.Delay(Probe.ReadyLinger);
        }
        return 0;
    }
}
