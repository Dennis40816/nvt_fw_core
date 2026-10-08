// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Processes;
using Nvt.Core.Tests.LinkedProbe;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Exercises Core START, real runtime composition, Bootstrap failure exits, and identity handoff inside linked children.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class LinkedBootstrapTests
{
    private static readonly ImmutableBootstrapWaitBudget AdmissionBudget = new(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5_500));
    private static readonly ImmutableBootstrapWaitBudget CompletionBudget = new(TimeSpan.FromMilliseconds(44_500), TimeSpan.FromSeconds(45));
    private static readonly string[] RuntimeWaiting = ["False", "<null>", "0", "0", "<null>", "<null>", "<null>", "<null>", "<null>", "<null>", "<null>", "<null>"];
    private static readonly string[] RuntimeTrace = ["compose", "app.load"];
    private static readonly string[] ClosedHandles = ["closed", "closed"];

    /// <summary>The linked gated child captures Bootstrap lifetime only after exact START bytes and context.</summary>
    [Theory]
    [InlineData("v1", "START\n", 18)]
    [InlineData("v1", "START\nX", 18)]
    [InlineData("V1", "START\n", 26)]
    [InlineData("v1", "STOP\n", 26)]
    [InlineData("v1", "START\r\n", 26)]
    [InlineData("v1", "START", 26)]
    [InlineData("v1", "1234567\n", 26)]
    [InlineData("v1", "12345678\n", 26)]
    [InlineData("v1", "123456789\n", 26)]
    public async Task BootstrapReadyRequiresExactStartBeforeLifetimeCapture(string startContext, string bytes, int exit)
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string state = workspace.PathFor("state.json");
        using ManagedProcessLifetimeLease lifetime = LinkedLauncherProbe.Acquire(state, ManagedProcessLifetimeKind.Bootstrap);
        using var start = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "bootstrap-ready", state);
        lifetime.ApplyInheritedContext(info);
        info.Environment[TransportFixture.Names.BootstrapStartContext] = startContext;
        info.Environment[TransportFixture.Names.BootstrapStartHandle] = start.GetClientHandleAsString();
        using Process child = LinkedLauncherProbe.Start(info,
            [new(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue),
                ProcessInheritedHandle.Parse(TransportFixture.Names.BootstrapStartHandle, start.GetClientHandleAsString())]);
        start.DisposeLocalCopyOfClientHandle();
        try
        {
            if (startContext == "v1")
            {
                byte[] message = System.Text.Encoding.UTF8.GetBytes(bytes);
                int split = Math.Min(3, message.Length);
                await start.WriteAsync(message.AsMemory(0, split), TestContext.Current.CancellationToken);
                await start.FlushAsync(TestContext.Current.CancellationToken);
                await start.WriteAsync(message.AsMemory(split), TestContext.Current.CancellationToken);
                await start.FlushAsync(TestContext.Current.CancellationToken);
            }
            start.Dispose();
            await LinkedProbeWorkspace.ExitAsync(child, exit);
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Exact START authorization cannot manufacture missing Bootstrap lifetime authority.</summary>
    [Fact]
    public async Task BootstrapReadyRejectsMissingLifetimeAfterExactStart()
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        using var start = new BootstrapStartAuthorization(TransportFixture.Names);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "bootstrap-ready", workspace.PathFor("state.json"));
        info.Environment[TransportFixture.Names.LifetimeKind] = nameof(ManagedProcessLifetimeKind.Bootstrap);
        start.ApplyInheritedContext(info);
        using Process child = LinkedLauncherProbe.Start(info, [start.InheritedHandle]);
        start.DisposeLocalClientHandle();
        try
        {
            Assert.True(start.TryAuthorize());
            await LinkedProbeWorkspace.ExitAsync(child, 24);
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Identity capture clears malformed optional authority only when Core lifetime capture succeeded.</summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "1|malformed")]
    [InlineData(false, "1|malformed")]
    public async Task IdentityClearingRequiresCapturedLifetime(bool managed, string? identity)
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string state = workspace.PathFor("state.json");
        string marker = workspace.PathFor("identity.txt");
        using ManagedProcessLifetimeLease lifetime = LinkedLauncherProbe.Acquire(state, ManagedProcessLifetimeKind.Bootstrap);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "identity-context-child", state, marker);
        info.Environment[TransportFixture.Names.BootstrapIdentity] = identity;
        IReadOnlyList<ProcessInheritedHandle> handles = [];
        if (managed)
        {
            lifetime.ApplyInheritedContext(info);
            handles = [new(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue)];
        }
        using Process child = LinkedLauncherProbe.Start(info, handles);
        try
        {
            await LinkedProbeWorkspace.ExitAsync(child, 0);
            Assert.Equal(new[] { managed ? "Captured" : "InvalidInheritedContext", identity ?? "<null>",
                managed ? "<null>" : identity ?? "<null>", "<null>", "<null>", "<null>" },
                await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken));
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>A missing release file produces the frozen five-second handshake timeout after successful Bootstrap capture.</summary>
    [Fact]
    public async Task BootstrapDelayedExitRequiresReleaseWithinFiveSeconds()
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string state = workspace.PathFor("state.json");
        string marker = workspace.PathFor("exit.txt");
        using ManagedProcessLifetimeLease lifetime = LinkedLauncherProbe.Acquire(state, ManagedProcessLifetimeKind.Bootstrap);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "bootstrap-eof-before-exit-18", state, marker);
        lifetime.ApplyInheritedContext(info);
        using Process child = LinkedLauncherProbe.Start(info, [new(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue)]);
        try
        {
            Assert.Equal(child.Id.ToString(CultureInfo.InvariantCulture), await LinkedLauncherProbe.MarkerAsync(marker));
            await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(25, child.ExitCode);
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Runtime captures real managed authority and clears identity before blocking composition on START.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("1|malformed")]
    [InlineData("valid")]
    public async Task CompleteManagedRuntimeWaitsForStartBeforeComposition(string? identity) =>
        await AssertRuntimeAsync(identity, authorize: true);

    /// <summary>The real runtime rejects malformed START before synthetic state services can run.</summary>
    [Fact]
    public async Task UnauthorizedRuntimeStartReturnsExactExitBeforeComposition() =>
        await AssertRuntimeAsync("valid", authorize: false);

    internal static async Task AssertRuntimeAsync(string? identity, bool authorize)
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string state = workspace.PathFor("state.json");
        string marker = workspace.PathFor("runtime.txt");
        WindowsCustodyCapability.RequireFile(workspace.Root);
        using ManagedProcessLifetimeLease lifetime = LinkedLauncherProbe.Acquire(state, ManagedProcessLifetimeKind.Bootstrap);
        using var gate = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        using var admission = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "bootstrap-runtime", state, marker);
        lifetime.ApplyInheritedContext(info);
        info.Environment[TransportFixture.Names.BootstrapStartContext] = "v1";
        info.Environment[TransportFixture.Names.BootstrapStartHandle] = gate.GetClientHandleAsString();
        info.Environment[TransportFixture.Names.BootstrapAdmissionHandle] = admission.GetClientHandleAsString();
        info.Environment[TransportFixture.Names.BootstrapIdentity] = identity == "valid"
            ? $"1|{TransportFixture.Descriptor.BootstrapExecutableFileName}|1|{new string('a', 64)}" : identity;
        using Process child = LinkedLauncherProbe.Start(info,
            [new(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue),
                ProcessInheritedHandle.Parse(TransportFixture.Names.BootstrapStartHandle, gate.GetClientHandleAsString()),
                ProcessInheritedHandle.Parse(TransportFixture.Names.BootstrapAdmissionHandle, admission.GetClientHandleAsString())]);
        gate.DisposeLocalCopyOfClientHandle();
        admission.DisposeLocalCopyOfClientHandle();
        try
        {
            _ = await LinkedLauncherProbe.MarkerAsync(marker + ".waiting");
            Assert.Equal(RuntimeWaiting, await File.ReadAllLinesAsync(marker + ".waiting", TestContext.Current.CancellationToken));
            Assert.False(child.HasExited);
            Assert.False(File.Exists(marker));
            await gate.WriteAsync(authorize ? "START\n"u8.ToArray() : "STOP\n"u8.ToArray(), TestContext.Current.CancellationToken);
            await gate.FlushAsync(TestContext.Current.CancellationToken);
            await LinkedProbeWorkspace.ExitAsync(child, authorize ? 10 : 23);
            Assert.Equal(ClosedHandles, await File.ReadAllLinesAsync(marker + ".closed", TestContext.Current.CancellationToken));
            if (authorize) { Assert.Equal(RuntimeTrace, await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken)); }
            else { Assert.False(File.Exists(marker)); }
            lifetime.Dispose();
            Assert.Equal(ManagedProcessLifetimeStatus.Exited, ManagedProcessLifetimeLease.GetStatus(LinkedLauncherProbe.Protocol, state, ManagedProcessLifetimeKind.Bootstrap));
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Exact authority crosses the owned handoff and a real nested child, and manual entry never captures it.</summary>
    [Fact]
    public async Task BootstrapIdentityIsInheritedCapturedClearedAndRequiresManagedLifetime()
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        workspace.CopyAndRename();
        string root = Path.GetDirectoryName(workspace.Executable)!;
        string executable = Path.Combine(root, TransportFixture.Descriptor.BootstrapExecutableFileName);
        File.Move(workspace.Executable, executable);
        byte[] bytes = await File.ReadAllBytesAsync(executable, TestContext.Current.CancellationToken);
        ManagedImmutableBootstrapIdentity identity = ManagedImmutableBootstrapIdentity.Create(TransportFixture.Descriptor,
            Path.GetFileName(executable), bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        string serialized = $"1|{identity.FileName}|{identity.Length.ToString(CultureInfo.InvariantCulture)}|{identity.Sha256}";
        string marker = workspace.PathFor("managed.txt");
        string state = workspace.PathFor("state.json");
        using var environment = new ProtocolEnvironmentScope(("CORE_LINKED_PROBE_MODE", "bootstrap-identity-chain-root"),
            ("CORE_LINKED_PROBE_MARKER", marker), (TransportFixture.Names.ExpectedApplicationVersion, null));
        var handoff = new StableLauncherHandoff(TransportFixture.Descriptor, 200_000_000, 128,
            LinkedLauncherProbe.Protocol.JobNamePrefix, root, state, TimeSpan.FromMilliseconds(500));
        ImmutableBootstrapStartResult started = await handoff.StartAsync(root, identity,
            new ExecutableLease(executable, root), TestContext.Current.CancellationToken);
        using IImmutableBootstrapLaunch launch = Assert.IsAssignableFrom<IImmutableBootstrapLaunch>(started.Launch);
        ImmutableBootstrapAdmissionResult admitted = await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken);
        ImmutableBootstrapCompletionResult completed = await launch.WaitForCompletionAsync(CompletionBudget, TestContext.Current.CancellationToken);
        Assert.True(started.IsStarted);
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted, admitted.Outcome);
        Assert.Equal(ImmutableBootstrapCompletionOutcome.Ready, completed.Outcome);
        Assert.Equal(new[] { "Captured", serialized, "<null>", identity.FileName, identity.Length.ToString(CultureInfo.InvariantCulture), identity.Sha256 },
            await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken));

        string manualMarker = workspace.PathFor("manual.txt");
        ProcessStartInfo manual = LinkedLauncherProbe.Create(workspace, "identity-context-child", marker: manualMarker);
        manual.FileName = executable;
        manual.Environment[TransportFixture.Names.BootstrapIdentity] = serialized;
        using Process child = LinkedLauncherProbe.Start(manual, []);
        try
        {
            await LinkedProbeWorkspace.ExitAsync(child, 0);
            Assert.Equal(new[] { "NotInherited", serialized, serialized, "<null>", "<null>", "<null>" },
                await File.ReadAllLinesAsync(manualMarker, TestContext.Current.CancellationToken));
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Actual child-side capture and encoded failure preserve the exact pre-admission code and issue.</summary>
    [Fact]
    public async Task BootstrapRealExitTwentyTwoPreservesInvalidInheritedContext()
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string marker = workspace.PathFor("exit.txt");
        using ImmutableBootstrapProcessLaunch launch = StartFailure(workspace, "bootstrap-exit-22", marker, out Process child);
        int pid = child.Id;
        await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(pid.ToString(CultureInfo.InvariantCulture), await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
        ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken);
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, result.Outcome);
        Assert.Equal(22, result.ExitCode);
        Assert.Equal(ImmutableBootstrapExitIssue.InvalidInheritedContext, result.ExitIssue);
        Assert.True(result.HasValidShape);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    /// <summary>Admission EOF waits for the linked child's delayed StateUnavailable exit without substituting a parent code.</summary>
    [Fact]
    public async Task BootstrapPipeEofBeforeDelayedExitPreservesStateUnavailable()
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string marker = workspace.PathFor("exit.txt");
        using ImmutableBootstrapProcessLaunch launch = StartFailure(workspace, "bootstrap-eof-before-exit-18", marker, out Process child);
        int pid = child.Id;
        Assert.Equal(pid.ToString(CultureInfo.InvariantCulture), await LinkedLauncherProbe.MarkerAsync(marker));
        Task<ImmutableBootstrapAdmissionResult> pending = launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        await File.WriteAllTextAsync(marker + ".release", "exit", TestContext.Current.CancellationToken);
        ImmutableBootstrapAdmissionResult result = await pending;
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, result.Outcome);
        Assert.Equal(18, result.ExitCode);
        Assert.Equal(ImmutableBootstrapExitIssue.StateUnavailable, result.ExitIssue);
        Assert.True(result.HasValidShape);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "Successful creation transfers process, pipe, gate, and lifetime ownership into the returned receipt.")]
    private static ImmutableBootstrapProcessLaunch StartFailure(LinkedProbeWorkspace workspace, string mode, string marker, out Process child)
    {
        string state = workspace.PathFor("state.json");
        ManagedProcessLifetimeLease lifetime = LinkedLauncherProbe.Acquire(state, ManagedProcessLifetimeKind.Bootstrap);
        var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        var gate = new BootstrapStartAuthorization(TransportFixture.Names);
        try
        {
            ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, mode, state, marker);
            lifetime.ApplyInheritedContext(info);
            child = LinkedLauncherProbe.Start(info, [new(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue)]);
            pipe.DisposeLocalCopyOfClientHandle();
            return new(Task.FromResult<Process?>(child), pipe, gate, lifetime, ManagedProcessTermination.Instance, TimeSpan.FromMilliseconds(500));
        }
        catch { pipe.Dispose(); gate.Dispose(); lifetime.Dispose(); throw; }
    }

    private sealed record ExecutableLease(string ExecutablePath, string WorkingDirectory) : IManagedExecutableLaunchLease
    {
        public bool TryValidateForStart() => true;
        public void Dispose() { }
    }
}
