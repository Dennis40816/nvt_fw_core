// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;

namespace Nvt.Core.LinkedProbe.Recovery;

internal static class SetupMaterializerMode
{
    [LinkedProbeMode("setup-materializer")]
    internal static Task<int> RunAsync(ProbeContext context) => RecoveryModes.RunAsync(context, "RunSetupAsync");
}

internal static class RecoveryExecutorMode
{
    [LinkedProbeMode("recovery-executor")]
    internal static Task<int> RunAsync(ProbeContext context) => RecoveryModes.RunAsync(context, "RunExecutorAsync");
}

internal static class RecoveryModes
{
    internal static Task<int> RunAsync(ProbeContext context, string entry)
    {
        // Reuse the owning tests' fixtures and codecs, just as the repository crash probe does.
        // The fixed entry points avoid copying fixture topology into the shared child project.
        string state = context.Inputs.Required("state-path");
        string boundary = context.Inputs.Required("boundary");
        string marker = context.Inputs.Required("marker");
        string assemblyPath = context.Inputs.Required("pause");
        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        Type fixture = assembly.GetType("Nvt.Core.Tests.Launcher.Recovery.RecoveryHardStopChild", throwOnError: true)!;
        var run = fixture.GetMethod(entry)!.CreateDelegate<Func<string, string, string, Task<int>>>();
        return run(state, boundary, marker);
    }
}
