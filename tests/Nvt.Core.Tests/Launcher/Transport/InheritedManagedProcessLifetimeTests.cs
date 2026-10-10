// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Exercises context consumption, role binding, and native lease-path rejection without joining the test host to a Job.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class InheritedManagedProcessLifetimeTests
{
    private static ManagedLifetimeProtocol Protocol { get; } =
        new(TransportFixture.Names, @"Local\CoreFixture.ManagedTree");
    private static readonly string[] LifetimeKeys =
    [
        TransportFixture.Names.LifetimeContext, TransportFixture.Names.LifetimeHandle,
        TransportFixture.Names.LifetimeJob, TransportFixture.Names.LifetimeStatePath,
        TransportFixture.Names.LifetimeKind,
    ];

    /// <summary>Absence is legacy only when no other managed context was advertised.</summary>
    [Theory]
    [InlineData(false, InheritedManagedProcessLifetimeOutcome.NotInherited)]
    [InlineData(true, InheritedManagedProcessLifetimeOutcome.InvalidInheritedContext)]
    public void AbsentLifetimeContextRequiresUnadvertisedStartup(bool advertised, InheritedManagedProcessLifetimeOutcome expected)
    {
        using var environment = EmptyLifetime();
        var entry = Entry();
        Assert.False(entry.IsContextAdvertised());
        using IInheritedManagedProcessLifetimeCapture capture = entry.Capture(null, ManagedProcessLifetimeKind.Application, advertised);
        Assert.Equal(expected, capture.Outcome);
        AssertConsumed();
    }

    /// <summary>Each of the five fields independently advertises a managed parent and is consumed on rejection.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PartialLifetimeContextIsAdvertisedConsumedAndRejected(int field)
    {
        using var environment = EmptyLifetime();
        Environment.SetEnvironmentVariable(LifetimeKeys[field], " ");
        var entry = Entry();
        Assert.True(entry.IsContextAdvertised());
        using IInheritedManagedProcessLifetimeCapture capture = entry.Capture(null, ManagedProcessLifetimeKind.Application, false);
        Assert.Equal(InheritedManagedProcessLifetimeOutcome.InvalidInheritedContext, capture.Outcome);
        AssertConsumed();
    }

    /// <summary>Both application READY fields independently advertise startup without being consumed by lifetime inspection.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EitherReadyFieldAdvertisesManagedApplicationStartup(int field)
    {
        using var environment = new ProtocolEnvironmentScope(
            (TransportFixture.Names.ApplicationReadyHandle, null), (TransportFixture.Names.ExpectedApplicationVersion, null));
        var entry = Entry();
        Assert.False(entry.IsApplicationReadyContextAdvertised());
        string key = field == 0 ? TransportFixture.Names.ApplicationReadyHandle : TransportFixture.Names.ExpectedApplicationVersion;
        Environment.SetEnvironmentVariable(key, " ");
        Assert.True(entry.IsApplicationReadyContextAdvertised());
        Assert.Equal(" ", Environment.GetEnvironmentVariable(key));
    }

    /// <summary>Malformed handle text never confers native custody.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-2")]
    [InlineData("+1")]
    [InlineData("1 ")]
    [InlineData("9223372036854775808")]
    public void InvalidHandleTextIsConsumedAndRejected(string? handle)
    {
        using var environment = EmptyLifetime();
        Environment.SetEnvironmentVariable(TransportFixture.Names.LifetimeContext, "v1");
        Environment.SetEnvironmentVariable(TransportFixture.Names.LifetimeHandle, handle);
        using IInheritedManagedProcessLifetimeCapture capture = Entry().Capture(null, ManagedProcessLifetimeKind.Application, true);
        Assert.Equal(InheritedManagedProcessLifetimeOutcome.InvalidInheritedContext, capture.Outcome);
        AssertConsumed();
    }

    /// <summary>Context version, normalized state path, ordinal role, and exact Job identity fail closed and close the captured handle.</summary>
    [Theory]
    [InlineData("context")]
    [InlineData("missing-path")]
    [InlineData("different-path")]
    [InlineData("role")]
    [InlineData("role-case")]
    [InlineData("job")]
    [InlineData("job-case")]
    public void InvalidMetadataClosesTransferredNativeHandle(string mismatch)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string state = workspace.GetPath("state.json");
        using ManagedProcessLifetimeLease lease = Acquire(state, ManagedProcessLifetimeKind.Application);
        var start = new ProcessStartInfo();
        lease.ApplyInheritedContext(start);
        string duplicate = WindowsPipeHandles.Duplicate(lease.InheritedHandleValue);
        start.Environment[TransportFixture.Names.LifetimeHandle] = duplicate;
        switch (mismatch)
        {
            case "context": start.Environment[TransportFixture.Names.LifetimeContext] = "V1"; break;
            case "missing-path": start.Environment[TransportFixture.Names.LifetimeStatePath] = null; break;
            case "different-path": start.Environment[TransportFixture.Names.LifetimeStatePath] = workspace.GetPath("other.json"); break;
            case "role": start.Environment[TransportFixture.Names.LifetimeKind] = nameof(ManagedProcessLifetimeKind.Launcher); break;
            case "role-case": start.Environment[TransportFixture.Names.LifetimeKind] = "application"; break;
            case "job": start.Environment[TransportFixture.Names.LifetimeJob] += ".extra"; break;
            case "job-case": start.Environment[TransportFixture.Names.LifetimeJob] = lease.JobName.ToUpperInvariant(); break;
        }
        using var environment = ApplyLifetime(start);
        using IInheritedManagedProcessLifetimeCapture capture = Entry().Capture(state, ManagedProcessLifetimeKind.Application, true);
        Assert.Equal(InheritedManagedProcessLifetimeOutcome.InvalidInheritedContext, capture.Outcome);
        AssertConsumed();
        Assert.False(WindowsPipeHandles.IsOpen(duplicate));
        Assert.True(WindowsPipeHandles.IsOpen(lease.InheritedHandle));
    }

    /// <summary>Correct metadata cannot launder a readable handle to a different physical file.</summary>
    [Fact]
    public void DifferentReadableFileHandleIsRejectedBeforeJobAssignment()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string state = workspace.GetPath("state.json");
        using ManagedProcessLifetimeLease lease = Acquire(state, ManagedProcessLifetimeKind.Application);
        using var arbitrary = new FileStream(workspace.GetPath("arbitrary.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        string duplicate = WindowsPipeHandles.Duplicate(arbitrary.SafeFileHandle.DangerousGetHandle());
        var start = new ProcessStartInfo();
        lease.ApplyInheritedContext(start);
        start.Environment[TransportFixture.Names.LifetimeHandle] = duplicate;
        using var environment = ApplyLifetime(start);
        using IInheritedManagedProcessLifetimeCapture capture = Entry().Capture(state, ManagedProcessLifetimeKind.Application, true);
        Assert.Equal(InheritedManagedProcessLifetimeOutcome.InvalidInheritedContext, capture.Outcome);
        Assert.False(WindowsPipeHandles.IsOpen(duplicate));
        AssertConsumed();
    }

    /// <summary>Application and launcher Jobs require their exact role identity; Bootstrap requires exactly 32 lowercase hexadecimal invocation characters.</summary>
    [Theory]
    [InlineData(ManagedProcessLifetimeKind.Application)]
    [InlineData(ManagedProcessLifetimeKind.Launcher)]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap)]
    public void ExactJobRoleAndInvocationShapeAreRequired(ManagedProcessLifetimeKind kind)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string state = workspace.GetPath("state.json");
        using ManagedProcessLifetimeLease lease = Acquire(state, kind);
        Assert.True(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, state, kind, lease.JobName));
        Assert.False(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, state, kind, lease.JobName.ToUpperInvariant()));
        Assert.False(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, workspace.GetPath("other.json"), kind, lease.JobName));
        Assert.False(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, state, kind, lease.JobName + "0"));
        Assert.False(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, state, kind, lease.JobName[..^1]));
        if (kind == ManagedProcessLifetimeKind.Bootstrap)
        {
            string prefix = lease.JobName[..^32];
            Assert.True(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, state, kind, prefix + new string('a', 32)));
            Assert.False(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, state, kind, prefix + new string('A', 32)));
            Assert.False(ManagedProcessLifetimeLease.IsExpectedJobName(Protocol, state, kind, prefix + new string('g', 32)));
        }
    }

    /// <summary>The physical native file path is matched after normalization and Windows case folding.</summary>
    [Fact]
    public void NativeLeasePathRequiresExactPhysicalFile()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string path = workspace.GetPath("lease.lock");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Assert.True(ManagedLifetimeNativePath.IsExactLeaseHandle(stream.SafeFileHandle, path));
        Assert.True(ManagedLifetimeNativePath.IsExactLeaseHandle(stream.SafeFileHandle, path.ToUpperInvariant()));
        Assert.False(ManagedLifetimeNativePath.IsExactLeaseHandle(stream.SafeFileHandle, workspace.GetPath("other.lock")));
    }

    private static InheritedManagedProcessLifetime Entry() => new(TransportFixture.Names, Protocol.JobNamePrefix);
    private static ProtocolEnvironmentScope EmptyLifetime() => new(LifetimeKeys.Select(static key => (key, (string?)null)).ToArray());
    private static ProtocolEnvironmentScope ApplyLifetime(ProcessStartInfo start) => new(LifetimeKeys.Select(key => (key, start.Environment[key])).ToArray());
    private static void AssertConsumed() => Assert.All(LifetimeKeys, static key => Assert.Null(Environment.GetEnvironmentVariable(key)));
    private static ManagedProcessLifetimeLease Acquire(string state, ManagedProcessLifetimeKind kind) =>
        ManagedProcessLifetimeLease.TryAcquire(Protocol, state, kind) ?? throw new InvalidOperationException("Lifetime lease was not acquired.");
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows file-handle path resolution and Job objects."); }
    }
}
