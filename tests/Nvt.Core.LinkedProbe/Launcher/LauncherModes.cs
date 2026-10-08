// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Processes;

namespace Nvt.Core.LinkedProbe.Launcher;

internal static class LauncherModes
{
    internal const string JobNamePrefix = @"Local\CoreFixture.ManagedTree";
    private static readonly byte[] StartBytes = "START\n"u8.ToArray();

    internal static ProductDescriptor Descriptor(ProbeContext context) => new(
        "Fixture", "test-runtime", "test-registry", "Fixture.exe", "launcher/Fixture.Launcher.exe",
        "Fixture.Bootstrap.exe", static version => $"Fixture-v{version}-test-runtime", context.ProtocolNames);

    private static class BootstrapReadyMode
    {
        [LinkedProbeMode("bootstrap-ready")]
        internal static Task<int> RunAsync(ProbeContext context) => LauncherModes.RunAsync(context, "bootstrap-ready");
    }

    private static class BootstrapIdentityChainMode
    {
        [LinkedProbeMode("bootstrap-identity-chain-root")]
        internal static Task<int> RunAsync(ProbeContext context) => LauncherModes.RunAsync(context, "bootstrap-identity-chain-root");
    }

    private static class IdentityContextMode
    {
        [LinkedProbeMode("identity-context-child")]
        internal static Task<int> RunAsync(ProbeContext context) => LauncherModes.RunAsync(context, "identity-context-child");
    }

    private static class LauncherIdentityMode
    {
        [LinkedProbeMode("launcher-identity-observation")]
        internal static Task<int> RunAsync(ProbeContext context) => LauncherModes.RunAsync(context, "launcher-identity-observation");
    }

    private static class BootstrapExitMode
    {
        [LinkedProbeMode("bootstrap-exit-22")]
        internal static Task<int> RunAsync(ProbeContext context) => LauncherModes.RunAsync(context, "bootstrap-exit-22");
    }

    private static class BootstrapDelayedExitMode
    {
        [LinkedProbeMode("bootstrap-eof-before-exit-18")]
        internal static Task<int> RunAsync(ProbeContext context) => LauncherModes.RunAsync(context, "bootstrap-eof-before-exit-18");
    }

    private static class LifetimeCaptureMode
    {
        [LinkedProbeMode("lifetime-capture")]
        internal static Task<int> RunAsync(ProbeContext context) => LauncherModes.RunAsync(context, "lifetime-capture");
    }

    private static async Task<int> RunAsync(ProbeContext context, string mode)
    {
        LauncherProtocolNames names = context.ProtocolNames;
        string? version = Environment.GetEnvironmentVariable(names.ExpectedApplicationVersion);
        string? expectedReady = Environment.GetEnvironmentVariable(names.ExpectedLauncherReady);
        ManagedProcessLifetimeKind kind = version is not null ? ManagedProcessLifetimeKind.Application
            : Environment.GetEnvironmentVariable(names.LifetimeKind) == nameof(ManagedProcessLifetimeKind.Bootstrap)
                ? ManagedProcessLifetimeKind.Bootstrap : ManagedProcessLifetimeKind.Launcher;
        string? state = context.Inputs.Optional("state-path");
        state = state is null ? null : Path.GetFullPath(state);

        if (mode is "bootstrap-ready" or "bootstrap-identity-chain-root")
        {
            if (!await WaitForExactStartAsync(context).ConfigureAwait(false))
            {
                return ProbeExitCodes.InvalidStartGate;
            }
        }
        if (mode == "bootstrap-identity-chain-root")
        {
            var info = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path."),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("--mode");
            info.ArgumentList.Add("identity-context-child");
            info.ArgumentList.Add("--protocol-prefix");
            info.ArgumentList.Add(context.Inputs.Optional("protocol-prefix") ?? ProtocolNames.DefaultPrefix);
            info.ArgumentList.Add("--state-path");
            info.ArgumentList.Add(state ?? throw new ProbeInputException("Missing input: state-path"));
            info.ArgumentList.Add("--marker");
            info.ArgumentList.Add(context.Inputs.Required("marker"));
            // Capture has deliberately not run yet. Only this exact lifetime handle crosses the next gate.
            using Process child = ProcessLaunchGate.StartContained(info,
                [ProcessInheritedHandle.Parse(names.LifetimeHandle,
                    Environment.GetEnvironmentVariable(names.LifetimeHandle) ?? "0")], static () => true)
                ?? throw new InvalidOperationException("Identity child did not start.");
            await child.WaitForExitAsync(context.CancellationToken).ConfigureAwait(false);
            if (child.ExitCode != 0) { return child.ExitCode; }
        }

        string? inheritedHandle = Environment.GetEnvironmentVariable(names.LifetimeHandle);
        using IInheritedManagedProcessLifetimeCapture capture = new InheritedManagedProcessLifetime(names, JobNamePrefix)
            .Capture(state, kind, state is not null || version is not null || expectedReady is not null);
        if (mode == "identity-context-child")
        {
            await WriteIdentityAsync(context, capture).ConfigureAwait(false);
            return ProbeExitCodes.Success;
        }
        if (mode == "lifetime-capture")
        {
            string marker = context.Inputs.Required("marker");
            await File.WriteAllTextAsync(marker, capture.Outcome.ToString(), context.CancellationToken).ConfigureAwait(false);
            if (capture.Outcome == InheritedManagedProcessLifetimeOutcome.Captured && context.Inputs.Optional("pause") == "dispose")
            {
                var handle = new IntPtr(long.Parse(inheritedHandle!, CultureInfo.InvariantCulture));
                if (!GetHandleInformation(handle, out uint flags)) { throw new InvalidOperationException("Captured handle is closed."); }
                await File.WriteAllLinesAsync(marker + ".context",
                    [(flags & 1u).ToString(CultureInfo.InvariantCulture),
                        Environment.GetEnvironmentVariable(names.LifetimeContext) ?? "<null>",
                        Environment.GetEnvironmentVariable(names.LifetimeHandle) ?? "<null>",
                        Environment.GetEnvironmentVariable(names.LifetimeJob) ?? "<null>",
                        Environment.GetEnvironmentVariable(names.LifetimeStatePath) ?? "<null>",
                        Environment.GetEnvironmentVariable(names.LifetimeKind) ?? "<null>"], context.CancellationToken).ConfigureAwait(false);
                if (!await WaitForFileAsync(marker + ".release", context.CancellationToken).ConfigureAwait(false))
                {
                    return ProbeExitCodes.HandshakeTimedOut;
                }
                capture.Dispose();
                await File.WriteAllTextAsync(marker + ".disposed", GetHandleInformation(handle, out _).ToString(), context.CancellationToken).ConfigureAwait(false);
                if (!await WaitForFileAsync(marker + ".exit", context.CancellationToken).ConfigureAwait(false))
                {
                    return ProbeExitCodes.HandshakeTimedOut;
                }
            }
        }
        if (capture.Outcome != InheritedManagedProcessLifetimeOutcome.Captured)
        {
            return ProbeExitCodes.LifetimeCaptureFailed;
        }
        switch (mode)
        {
            case "bootstrap-ready":
                return ImmutableBootstrapExitCodeCodec.EncodeFailure(ImmutableBootstrapExitIssue.StateUnavailable);
            case "bootstrap-exit-22":
            case "bootstrap-eof-before-exit-18":
                string marker = context.Inputs.Required("marker");
                await File.WriteAllTextAsync(marker, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), context.CancellationToken).ConfigureAwait(false);
                if (mode == "bootstrap-exit-22")
                {
                    return ImmutableBootstrapExitCodeCodec.EncodeFailure(ImmutableBootstrapExitIssue.InvalidInheritedContext);
                }
                return await WaitForFileAsync(marker + ".release", context.CancellationToken).ConfigureAwait(false)
                    ? ImmutableBootstrapExitCodeCodec.EncodeFailure(ImmutableBootstrapExitIssue.StateUnavailable)
                    : ProbeExitCodes.HandshakeTimedOut;
            case "launcher-identity-observation":
                await WriteIdentityAsync(context, capture).ConfigureAwait(false);
                string expected = expectedReady ?? throw new InvalidOperationException("Missing expected READY identity.");
                // The wire producer remains BCL-only; this mode's Core evidence is capture and identity clearing.
                string ready = expected + ":1.2.3:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture-admission")) + ":" + new string('a', 64) + "\n";
                await WritePipeAsync(names.LauncherReadyHandle, Encoding.UTF8.GetBytes(ready), context.CancellationToken).ConfigureAwait(false);
                break;
            case "bootstrap-identity-chain-root":
                await WritePipeAsync(names.BootstrapAdmissionHandle, "ADMITTED\n"u8.ToArray(), context.CancellationToken).ConfigureAwait(false);
                break;
        }
        return ProbeExitCodes.Success;
    }

    private static async Task WriteIdentityAsync(ProbeContext context, IInheritedManagedProcessLifetimeCapture capture)
    {
        string? before = Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapIdentity);
        ManagedImmutableBootstrapIdentity? identity = capture.Outcome == InheritedManagedProcessLifetimeOutcome.Captured
            ? new InheritedManagedBootstrapIdentityContext(Descriptor(context), 128).CaptureAndClear() : null;
        string? after = Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapIdentity);
        await File.WriteAllLinesAsync(context.Inputs.Required("marker"),
            [capture.Outcome.ToString(), before ?? "<null>", after ?? "<null>", identity?.FileName ?? "<null>",
                identity?.Length.ToString(CultureInfo.InvariantCulture) ?? "<null>", identity?.Sha256 ?? "<null>"],
            context.CancellationToken).ConfigureAwait(false);
    }

    private static async Task WritePipeAsync(string key, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var pipe = new AnonymousPipeClientStream(PipeDirection.Out,
            Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException("Missing inherited pipe."));
        await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<bool> WaitForFileAsync(string path, CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (!File.Exists(path) && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
        return File.Exists(path);
    }

    private static async Task<bool> WaitForExactStartAsync(ProbeContext context)
    {
        using BootstrapStartGate gate = BootstrapStartGate.Capture(context.ProtocolNames);
        if (gate.Outcome != BootstrapStartGateInheritanceOutcome.Inherited) { return false; }
        return await gate.WaitForStartAsync(StartBytes, context.CancellationToken).ConfigureAwait(false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
}
