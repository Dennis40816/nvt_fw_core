// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.LinkedProbe;

internal static class SelfCheck
{
    [LinkedProbeMode("linked-self-check")]
    internal static async Task<int> RunAsync(ProbeContext context)
    {
        string marker = context.Inputs.Required("marker");
        Assembly core = typeof(LauncherProtocolNames).Assembly;
        string name = core.GetName().Name ?? throw new InvalidOperationException("Core assembly name is missing.");
        string version = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            throw new InvalidOperationException("Core informational version is missing.");
        LauncherProtocolNames names = context.ProtocolNames;
        await File.WriteAllLinesAsync(marker,
        [
            name,
            version,
            names.ApplicationReadyHandle,
            names.ExpectedApplicationVersion,
            names.LauncherReadyHandle,
            names.ExpectedLauncherReady,
            names.BootstrapAdmissionHandle,
            names.BootstrapStartContext,
            names.BootstrapStartHandle,
            names.LifetimeContext,
            names.LifetimeHandle,
            names.LifetimeJob,
            names.LifetimeStatePath,
            names.LifetimeKind,
            names.BootstrapIdentity,
            Path.GetDirectoryName(core.Location) ?? string.Empty,
        ], context.CancellationToken);
        return ProbeExitCodes.Success;
    }
}
