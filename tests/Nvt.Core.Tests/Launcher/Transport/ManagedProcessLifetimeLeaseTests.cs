// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Processes;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Exercises parent-owned exclusive lifetime leases, Job observation, and real descendant cleanup.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class ManagedProcessLifetimeLeaseTests
{
    private static ManagedLifetimeProtocol Protocol { get; } =
        new(TransportFixture.Names, @"Local\CoreFixture.ManagedTree");

    /// <summary>One exact lifetime path has one exclusive writer, with non-inheritable retained custody.</summary>
    [Fact]
    public void ExactLifetimeLeaseIsExclusiveAndNonInheritable()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string state = workspace.GetPath("state.json");
        using ManagedProcessLifetimeLease first = Acquire(state);
        Assert.Equal(ManagedProcessLifetimeLeaseAcquisitionOutcome.Busy,
            ManagedProcessLifetimeLease.Acquire(Protocol, state, ManagedProcessLifetimeKind.Application, out var second));
        Assert.Null(second);
        Assert.Equal(0u, WindowsPipeHandles.Flags(first.InheritedHandle) & 1u);
        var info = new ProcessStartInfo();
        first.ApplyInheritedContext(info);
        Assert.Equal("v1", info.Environment[TransportFixture.Names.LifetimeContext]);
        Assert.Equal(Path.GetFullPath(state), info.Environment[TransportFixture.Names.LifetimeStatePath]);
        Assert.Equal(nameof(ManagedProcessLifetimeKind.Application), info.Environment[TransportFixture.Names.LifetimeKind]);
        Assert.Equal(first.JobName, info.Environment[TransportFixture.Names.LifetimeJob]);
        Assert.Equal(first.InheritedHandle, info.Environment[TransportFixture.Names.LifetimeHandle]);
    }

    /// <summary>Protocol roles retain independent exact lifetime paths and distinct Job identities.</summary>
    [Fact]
    public void LifetimeRolesHaveIndependentAuthority()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string state = workspace.GetPath("state.json");
        using ManagedProcessLifetimeLease application = Acquire(state);
        using ManagedProcessLifetimeLease launcher = ManagedProcessLifetimeLease.TryAcquire(
            Protocol, state, ManagedProcessLifetimeKind.Launcher)!;
        using ManagedProcessLifetimeLease bootstrap = ManagedProcessLifetimeLease.TryAcquire(
            Protocol, state, ManagedProcessLifetimeKind.Bootstrap)!;
        Assert.NotNull(launcher);
        Assert.NotNull(bootstrap);
        Assert.NotEqual(application.JobName, launcher.JobName);
        Assert.NotEqual(application.JobName, bootstrap.JobName);
        Assert.Contains(".Invocation.", bootstrap.JobName, StringComparison.Ordinal);
    }

    /// <summary>Explicit Job termination empties both a real root and its descendant before reporting success.</summary>
    [Fact]
    public async Task JobTerminationConfirmsActualRootAndDescendantExit()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("child.txt");
        using ManagedProcessLifetimeLease lifetime = Acquire(workspace.GetPath("state.json"));
        using Process root = StartTree(lifetime, marker, "tree-root-wait");
        int childId = 0;
        try
        {
            childId = await ReadMarkerAsync(marker);
            Assert.True(IsRunning(root.Id));
            Assert.True(IsRunning(childId));
            Assert.Equal(ManagedProcessLifetimeStatus.Active, ManagedProcessLifetimeLease.GetStatus(
                Protocol, workspace.GetPath("state.json"), ManagedProcessLifetimeKind.Application));
            using Process child = Process.GetProcessById(childId);
            Assert.True(lifetime.TerminateTreeAndConfirmEmpty(TimeSpan.FromSeconds(5)));
            await root.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(child.WaitForExit(5_000));
            Assert.False(IsRunning(childId));
        }
        finally { Terminate(root.Id); Terminate(childId); }
    }

    /// <summary>Closing the final parent Job handle after root exit enforces kill-on-close for its remaining descendant.</summary>
    [Fact]
    public async Task JobClosureTerminatesRemainingDescendant()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("child.txt");
        using ManagedProcessLifetimeLease lifetime = Acquire(workspace.GetPath("state.json"));
        using Process root = StartTree(lifetime, marker, "tree-root-exit");
        int childId = 0;
        try
        {
            childId = await ReadMarkerAsync(marker);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await root.WaitForExitAsync(deadline.Token);
            using Process child = Process.GetProcessById(childId);
            Assert.False(child.HasExited);
            lifetime.Dispose();
            Assert.True(child.WaitForExit(5_000));
            Assert.False(IsRunning(childId));
        }
        finally { Terminate(root.Id); Terminate(childId); }
    }

    /// <summary>Every positive Job termination wait is required before touching native authority.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonpositiveJobTerminationWaitIsRejected(long ticks)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using ManagedProcessLifetimeLease lifetime = Acquire(workspace.GetPath("state.json"));
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            lifetime.TerminateTreeAndConfirmEmpty(TimeSpan.FromTicks(ticks)));
        Assert.Equal("timeout", exception.ParamName);
    }

    /// <summary>The smallest positive wait is accepted without requiring completion inside that scheduling window.</summary>
    [Fact]
    public void SmallestPositiveJobTerminationWaitIsAccepted()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using ManagedProcessLifetimeLease lifetime = Acquire(workspace.GetPath("state.json"));
        Assert.Null(Record.Exception(() =>
        {
            _ = lifetime.TerminateTreeAndConfirmEmpty(TimeSpan.FromTicks(1));
        }));
    }

    /// <summary>The lifetime allowlist excludes an unrelated ambient inheritable pipe.</summary>
    [Fact]
    public async Task LifetimeLaunchDeniesAmbientInheritablePipe()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using ManagedProcessLifetimeLease lifetime = Acquire(workspace.GetPath("state.json"));
        using var ambient = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        string marker = workspace.GetPath("started.txt");
        ProcessStartInfo info = ProcessProbe.Create("ambient-pipe");
        info.Environment["CORE_TEST_PROBE_MARKER"] = marker;
        info.Environment["CORE_TEST_PROBE_AMBIENT_HANDLE"] = ambient.GetClientHandleAsString();
        lifetime.ApplyInheritedContext(info);
        using Process process = ProcessLaunchGate.StartContained(info,
            [new ProcessInheritedHandle(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue)],
            static () => true) ?? throw new InvalidOperationException("The ambient probe did not start.");
        ambient.DisposeLocalCopyOfClientHandle();
        using var reader = new StreamReader(ambient, Encoding.UTF8, leaveOpen: true);
        Assert.Empty(await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
        await process.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("started", await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
    }

    /// <summary>The exact declared pipe is usable while a different physical handle remains excluded.</summary>
    [Fact]
    public async Task LifetimeLaunchAllowsExactPipeAndDeniesCrossHandle()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using ManagedProcessLifetimeLease lifetime = Acquire(workspace.GetPath("state.json"));
        using var allowed = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        using var cross = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        ProcessStartInfo info = ProcessProbe.Create("contained-isolation");
        info.Environment["CORE_TEST_PROBE_PAYLOAD"] = "exact";
        info.Environment["CORE_TEST_PROBE_CROSS_HANDLE"] = cross.GetClientHandleAsString();
        lifetime.ApplyInheritedContext(info);
        using Process process = ProcessLaunchGate.StartContained(info,
            [
                new ProcessInheritedHandle(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue),
                ProcessInheritedHandle.Parse("CORE_TEST_PROBE_ALLOWED_HANDLE", allowed.GetClientHandleAsString()),
            ], static () => true) ?? throw new InvalidOperationException("The isolation probe did not start.");
        allowed.DisposeLocalCopyOfClientHandle();
        cross.DisposeLocalCopyOfClientHandle();
        using var allowedReader = new StreamReader(allowed, Encoding.UTF8, leaveOpen: true);
        using var crossReader = new StreamReader(cross, Encoding.UTF8, leaveOpen: true);
        Task<string> allowedRead = allowedReader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        Task<string> crossRead = crossReader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("exact", await allowedRead);
        Assert.Empty(await crossRead);
    }

    private static ManagedProcessLifetimeLease Acquire(string state) =>
        ManagedProcessLifetimeLease.TryAcquire(Protocol, state, ManagedProcessLifetimeKind.Application)
        ?? throw new InvalidOperationException("The Windows lifetime lease was not acquired.");

    private static Process StartTree(ManagedProcessLifetimeLease lifetime, string marker, string mode)
    {
        ProcessStartInfo info = ProcessProbe.Create(mode);
        info.Environment["CORE_TEST_PROBE_TREE_MARKER"] = marker;
        lifetime.ApplyInheritedContext(info);
        return ProcessLaunchGate.StartContained(info,
            new[] { new ProcessInheritedHandle(TransportFixture.Names.LifetimeHandle, lifetime.InheritedHandleValue) },
            static () => true) ?? throw new InvalidOperationException("The contained tree probe did not start.");
    }

    private static async Task<int> ReadMarkerAsync(string marker)
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (File.Exists(marker) && int.TryParse(await File.ReadAllTextAsync(marker,
                    TestContext.Current.CancellationToken), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    return id;
                }
            }
            catch (IOException) { }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        throw new InvalidOperationException("Process marker was not readable before its deadline.");
    }

    private static bool IsRunning(int id)
    {
        try { using Process process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static void Terminate(int id)
    {
        if (id == 0) { return; }
        try
        {
            using Process process = Process.GetProcessById(id);
            if (!process.HasExited) { process.Kill(entireProcessTree: true); _ = process.WaitForExit(5_000); }
        }
        catch (ArgumentException) { }
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows lifetime leases and Job objects."); }
    }
}
