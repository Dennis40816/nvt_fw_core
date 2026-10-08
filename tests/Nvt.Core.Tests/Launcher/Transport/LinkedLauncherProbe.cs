// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Processes;
using Nvt.Core.Tests.LinkedProbe;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

internal static class LinkedLauncherProbe
{
    internal static ManagedLifetimeProtocol Protocol { get; } = new(TransportFixture.Names, @"Local\CoreFixture.ManagedTree");

    internal static ProcessStartInfo Create(LinkedProbeWorkspace workspace, string mode, string? state = null, string? marker = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = workspace.Executable,
            WorkingDirectory = workspace.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        LinkedProbeWorkspace.RemoveProbeVariables(info.Environment);
        info.ArgumentList.Add("--mode");
        info.ArgumentList.Add(mode);
        if (state is not null) { info.ArgumentList.Add("--state-path"); info.ArgumentList.Add(state); }
        if (marker is not null) { info.ArgumentList.Add("--marker"); info.ArgumentList.Add(marker); }
        return info;
    }

    internal static Process Start(ProcessStartInfo info, IReadOnlyList<ProcessInheritedHandle> handles) =>
        ProcessLaunchGate.StartContained(info, handles, static () => true)
        ?? throw new InvalidOperationException("Core-linked Launcher probe did not start.");

    internal static ManagedProcessLifetimeLease Acquire(string state, ManagedProcessLifetimeKind kind) =>
        ManagedProcessLifetimeLease.TryAcquire(Protocol, state, kind)
        ?? throw new InvalidOperationException("Windows lifetime authority was not acquired.");

    internal static void ApplyKind(ProcessStartInfo info, ManagedProcessLifetimeKind kind)
    {
        info.Environment[TransportFixture.Names.LifetimeKind] = kind.ToString();
        if (kind == ManagedProcessLifetimeKind.Application)
        {
            info.Environment[TransportFixture.Names.ExpectedApplicationVersion] = TransportFixture.Version.ToString();
        }
        if (kind == ManagedProcessLifetimeKind.Launcher)
        {
            info.Environment[TransportFixture.Names.ExpectedLauncherReady] = "advertised";
        }
    }

    internal static async Task<string> MarkerAsync(string path)
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    string value = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
                    if (value.Length > 0) { return value; }
                }
            }
            catch (IOException) { }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        throw new InvalidOperationException("Child evidence did not arrive before its deadline.");
    }

    internal static void Stop(Process process)
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); Assert.True(process.WaitForExit(5_000)); }
    }

    internal static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows contained creation, inherited file handles, and named Jobs."); }
    }
}
