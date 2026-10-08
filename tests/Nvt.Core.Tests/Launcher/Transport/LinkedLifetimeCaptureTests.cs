// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Processes;
using Nvt.Core.Tests.LinkedProbe;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Runs actual Core inherited lifetime capture, rejection, and disposal in an isolated child.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class LinkedLifetimeCaptureTests
{
    private static readonly string[] ClearedContext = ["0", "<null>", "<null>", "<null>", "<null>", "<null>"];

    /// <summary>Every exact role succeeds, while a missing handle, wrong path, and wrong role fail closed in Core.</summary>
    [Theory]
    [InlineData(ManagedProcessLifetimeKind.Application, "exact", 0)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap, "exact", 0)]
    [InlineData(ManagedProcessLifetimeKind.Launcher, "exact", 0)]
    [InlineData(ManagedProcessLifetimeKind.Application, "missing", 24)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap, "missing", 24)]
    [InlineData(ManagedProcessLifetimeKind.Launcher, "missing", 24)]
    [InlineData(ManagedProcessLifetimeKind.Application, "path", 24)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap, "path", 24)]
    [InlineData(ManagedProcessLifetimeKind.Launcher, "path", 24)]
    [InlineData(ManagedProcessLifetimeKind.Application, "role", 24)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap, "role", 24)]
    [InlineData(ManagedProcessLifetimeKind.Launcher, "role", 24)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap, "application-precedence", 24)]
    public async Task ManagedEntryCapturesOnlyExactPathAndRole(ManagedProcessLifetimeKind kind, string scenario, int exit)
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string state = workspace.PathFor("state.json");
        string marker = workspace.PathFor("capture.txt");
        using ManagedProcessLifetimeLease lease = LinkedLauncherProbe.Acquire(state, kind);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "lifetime-capture",
            scenario == "path" ? workspace.PathFor("other.json") : state, marker);
        lease.ApplyInheritedContext(info);
        LinkedLauncherProbe.ApplyKind(info, kind);
        IReadOnlyList<ProcessInheritedHandle> handles = [new(TransportFixture.Names.LifetimeHandle, lease.InheritedHandleValue)];
        if (scenario == "missing") { info.Environment[TransportFixture.Names.LifetimeHandle] = null; handles = []; }
        if (scenario == "role")
        {
            // Application takes precedence over LifetimeKind, and Launcher is the fallback kind.
            info.Environment[TransportFixture.Names.LifetimeKind] = nameof(ManagedProcessLifetimeKind.Application);
            if (kind == ManagedProcessLifetimeKind.Application) { info.Environment[TransportFixture.Names.ExpectedApplicationVersion] = null; }
            else { info.Environment[TransportFixture.Names.ExpectedApplicationVersion] = "1.2.3"; }
        }
        if (scenario == "application-precedence")
        {
            info.Environment[TransportFixture.Names.ExpectedApplicationVersion] = "1.2.3";
        }
        using Process child = LinkedLauncherProbe.Start(info, handles);
        try
        {
            await LinkedProbeWorkspace.ExitAsync(child, exit);
            Assert.Equal(exit == 0 ? "Captured" : "InvalidInheritedContext",
                await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Required application, Bootstrap, and Launcher capture cannot downgrade absent authority to unmanaged execution.</summary>
    [Theory]
    [InlineData(ManagedProcessLifetimeKind.Application)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap)]
    [InlineData(ManagedProcessLifetimeKind.Launcher)]
    public async Task ReadyAdvertisedProcessWithoutLifetimeExitsFailClosed(ManagedProcessLifetimeKind kind)
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string marker = workspace.PathFor("capture.txt");
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "lifetime-capture", workspace.PathFor("state.json"), marker);
        LinkedLauncherProbe.ApplyKind(info, kind);
        using Process child = LinkedLauncherProbe.Start(info, []);
        try
        {
            await LinkedProbeWorkspace.ExitAsync(child, 24);
            Assert.Equal("InvalidInheritedContext", await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Core capture clears inheritance and all five fields, then disposal releases the file while the child remains alive.</summary>
    [Theory]
    [InlineData(ManagedProcessLifetimeKind.Application)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap)]
    [InlineData(ManagedProcessLifetimeKind.Launcher)]
    public async Task CapturedLifetimeDisposalReleasesExactLeaseBeforeChildExit(ManagedProcessLifetimeKind kind)
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        WindowsCustodyCapability.RequireFile(workspace.Root);
        string state = workspace.PathFor("state.json");
        string marker = workspace.PathFor("capture.txt");
        using ManagedProcessLifetimeLease lease = LinkedLauncherProbe.Acquire(state, kind);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "lifetime-capture", state, marker);
        info.ArgumentList.Add("--pause");
        info.ArgumentList.Add("dispose");
        lease.ApplyInheritedContext(info);
        LinkedLauncherProbe.ApplyKind(info, kind);
        using Process child = LinkedLauncherProbe.Start(info, [new(TransportFixture.Names.LifetimeHandle, lease.InheritedHandleValue)]);
        try
        {
            Assert.Equal("Captured", await LinkedLauncherProbe.MarkerAsync(marker));
            _ = await LinkedLauncherProbe.MarkerAsync(marker + ".context");
            Assert.Equal(ClearedContext, await File.ReadAllLinesAsync(marker + ".context", TestContext.Current.CancellationToken));
            Assert.True(lease.TryReleaseAcceptedTree());
            lease.Dispose();
            string suffix = kind switch
            {
                ManagedProcessLifetimeKind.Application => ManagedProcessLifetimeLease.ApplicationSuffix,
                ManagedProcessLifetimeKind.Bootstrap => ManagedProcessLifetimeLease.BootstrapSuffix,
                _ => ManagedProcessLifetimeLease.LauncherSuffix,
            };
            Assert.Equal(ManagedProcessLifetimeStatus.Active, ManagedProcessLifetimeLease.GetStatus(LinkedLauncherProbe.Protocol, state, kind));
            Assert.Throws<IOException>(() => { using var blocked = new FileStream(state + suffix, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            await File.WriteAllTextAsync(marker + ".release", "dispose", TestContext.Current.CancellationToken);
            Assert.Equal("False", await LinkedLauncherProbe.MarkerAsync(marker + ".disposed"));
            Assert.False(child.HasExited);
            using (var released = new FileStream(state + suffix, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(released.CanWrite);
            }
            await File.WriteAllTextAsync(marker + ".exit", "exit", TestContext.Current.CancellationToken);
            await LinkedProbeWorkspace.ExitAsync(child, 0);
            Assert.Equal(ManagedProcessLifetimeStatus.Exited, ManagedProcessLifetimeLease.GetStatus(LinkedLauncherProbe.Protocol, state, kind));
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Core capture rejects a readable unrelated file in the real child before it can join the managed Job.</summary>
    [Fact]
    public async Task ManagedEntryRejectsDifferentReadableFileHandleWithoutReady()
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string state = workspace.PathFor("state.json");
        string marker = workspace.PathFor("capture.txt");
        using ManagedProcessLifetimeLease lease = LinkedLauncherProbe.Acquire(state, ManagedProcessLifetimeKind.Application);
        using var arbitrary = new FileStream(workspace.PathFor("unrelated.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        ProcessStartInfo info = LinkedLauncherProbe.Create(workspace, "lifetime-capture", state, marker);
        lease.ApplyInheritedContext(info);
        LinkedLauncherProbe.ApplyKind(info, ManagedProcessLifetimeKind.Application);
        using Process child = LinkedLauncherProbe.Start(info,
            [new(TransportFixture.Names.LifetimeHandle, arbitrary.SafeFileHandle.DangerousGetHandle())]);
        try
        {
            await LinkedProbeWorkspace.ExitAsync(child, 24);
            Assert.Equal("InvalidInheritedContext", await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
            Assert.True(arbitrary.CanWrite);
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }

    /// <summary>Unadvertised absence retains the distinct NotInherited outcome even though the probe requires capture to succeed.</summary>
    [Fact]
    public async Task UnadvertisedAbsenceIsNotInherited()
    {
        LinkedLauncherProbe.RequireWindows();
        await using var workspace = new LinkedProbeWorkspace();
        string marker = workspace.PathFor("capture.txt");
        using Process child = LinkedLauncherProbe.Start(LinkedLauncherProbe.Create(workspace, "lifetime-capture", marker: marker), []);
        try
        {
            await LinkedProbeWorkspace.ExitAsync(child, 24);
            Assert.Equal("NotInherited", await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
        }
        finally { LinkedLauncherProbe.Stop(child); }
    }
}
