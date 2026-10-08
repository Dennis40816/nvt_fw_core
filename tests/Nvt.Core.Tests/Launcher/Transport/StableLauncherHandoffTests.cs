// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Ports exact legacy authority and owned-lease handoff cancellation, containment, and disposal.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class StableLauncherHandoffTests
{
    private const string JobNamePrefix = @"Local\CoreFixture.ManagedTree";

    /// <summary>Constructor authority cannot derive an absolute root or state path from a relative value.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StableLauncherHandoffRejectsRelativeAuthorityPaths(bool root)
    {
        string absolute = Path.GetFullPath(Path.GetTempPath());
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Create(
            root ? "relative-root" : absolute, root ? Path.Combine(absolute, "state.json") : "relative-state.json"));
        Assert.Equal(root ? "managedRoot" : "statePath", exception.ParamName);
        Assert.StartsWith(root ? "Managed root must be an absolute path." : "State path must be an absolute path.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Cancellation is checked before any legacy process-boundary work.</summary>
    [Fact]
    public async Task StableLauncherHandoffHonorsCancellation()
    {
        using var workspace = TestWorkspace.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Create(workspace.Root, workspace.PathFor("state.json")).TryStartLauncherAsync(cancellation.Token));
    }

    /// <summary>Legacy restart fails closed without an exact inherited Bootstrap identity.</summary>
    [Fact]
    public async Task StableLauncherHandoffWithoutExpectedIdentityFailsClosed()
    {
        using var workspace = TestWorkspace.Create();
        StableLauncherStartResult result = await Create(workspace.Root, workspace.PathFor("state.json"))
            .TryStartLauncherAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new(StableLauncherStartOutcome.HandoffFailed), result);
    }

    /// <summary>A pre-cancelled owned handoff consumes its lease exactly once.</summary>
    [Fact]
    public async Task StableLauncherHandoffPreCancellationDisposesOwnedLeaseExactlyOnce()
    {
        using var workspace = TestWorkspace.Create();
        using var lease = Lease(workspace.Root);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Create(workspace.Root, workspace.PathFor("state.json")).StartAsync(workspace.Root, Identity(), lease, cancellation.Token));
        Assert.Equal(1, lease.DisposeCount);
    }

    /// <summary>Each exact path and filename predicate rejects mismatched authority before creation and consumes the owned lease once.</summary>
    [Theory]
    [InlineData("relative-root")]
    [InlineData("different-root")]
    [InlineData("working-directory")]
    [InlineData("executable")]
    [InlineData("filename")]
    [InlineData("missing-identity")]
    public async Task InvalidOwnedAuthorityDisposesLeaseExactlyOnce(string mismatch)
    {
        using var workspace = TestWorkspace.Create();
        string root = mismatch == "relative-root" ? "relative-root" : mismatch == "different-root" ? workspace.PathFor("other") : workspace.Root;
        string executable = workspace.PathFor(mismatch == "executable" ? "other.exe" : TransportFixture.Descriptor.BootstrapExecutableFileName);
        using var lease = new CountingLease(executable, mismatch == "working-directory" ? workspace.PathFor("other") : workspace.Root);
        ManagedImmutableBootstrapIdentity? identity = mismatch == "missing-identity" ? null : mismatch == "filename"
            ? ManagedImmutableBootstrapIdentity.Create(TransportFixture.CreateDescriptor("Other.exe"), "Other.exe", 1, new string('a', 64)) : Identity();
        ImmutableBootstrapStartResult result = await Create(workspace.Root, workspace.PathFor("state.json"))
            .StartAsync(root, identity!, lease, TestContext.Current.CancellationToken);
        Assert.Equal(ImmutableBootstrapStartIssue.Damaged, result.Issue);
        Assert.Null(result.Launch);
        Assert.True(result.HasValidShape);
        Assert.Equal(1, lease.DisposeCount);
    }

    /// <summary>Cancellation after owned acquisition releases the transferred lease before propagating.</summary>
    [Fact]
    public async Task CancellationAfterOwnedAcquisitionDisposesLeaseExactlyOnce()
    {
        using var workspace = TestWorkspace.Create();
        using var lease = Lease(workspace.Root);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handoff = Create(workspace.Root, workspace.PathFor("state.json"), afterExecutableAcquired: cancellation.Cancel);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await handoff.StartAsync(workspace.Root, Identity(), lease, cancellation.Token));
        Assert.Equal(1, lease.DisposeCount);
    }

    /// <summary>Final validation and post-acquisition hooks preserve typed start failure without a false exit code.</summary>
    [Fact]
    public async Task OwnedHandoffPreservesTypedStartFailure()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using var lease = Lease(workspace.Root, valid: false);
        var handoff = Create(workspace.Root, workspace.PathFor("state.json"), beforeProcessStart:
            static _ => throw new InvalidOperationException("Injected start failure."));
        ImmutableBootstrapStartResult start = await handoff.StartAsync(workspace.Root, Identity(), lease, TestContext.Current.CancellationToken);
        Assert.True(start.IsStarted);
        using IImmutableBootstrapLaunch launch = Assert.IsAssignableFrom<IImmutableBootstrapLaunch>(start.Launch);
        ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(
            new ImmutableBootstrapWaitBudget(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5_500)), TestContext.Current.CancellationToken);
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.LaunchFailed, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.Equal(ImmutableBootstrapExitIssue.StartFailed, result.ExitIssue);
        Assert.True(result.HasValidShape);
        Assert.Equal(1, lease.DisposeCount);
    }

    /// <summary>Refused final custody retains the frozen task exception and exact message rather than being reclassified.</summary>
    [Fact]
    public async Task RefusedOwnedFinalValidationPreservesInvalidDataException()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string state = workspace.PathFor("state.json");
        using var lease = Lease(workspace.Root, valid: false);
        ImmutableBootstrapStartResult start = await Create(workspace.Root, state)
            .StartAsync(workspace.Root, Identity(), lease, TestContext.Current.CancellationToken);
        Assert.True(start.IsStarted);
        using IImmutableBootstrapLaunch launch = Assert.IsAssignableFrom<IImmutableBootstrapLaunch>(start.Launch);
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await launch.WaitForAdmissionAsync(new ImmutableBootstrapWaitBudget(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5_500)),
                TestContext.Current.CancellationToken));
        Assert.Equal("Bootstrap tree changed after executable verification.", exception.Message);
        Assert.Equal(1, lease.DisposeCount);
        launch.Dispose();
        // The frozen Dispose starts background cleanup; wait for exclusive reacquisition before deleting the fixture.
        var protocol = new ManagedLifetimeProtocol(TransportFixture.Names, JobNamePrefix);
        long deadline = Environment.TickCount64 + 5_000;
        ManagedProcessLifetimeLease? released;
        while ((released = ManagedProcessLifetimeLease.TryAcquire(protocol, state,
                   Nvt.Core.Launcher.Coordination.ManagedProcessLifetimeKind.Bootstrap)) is null && Environment.TickCount64 < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        using (released) { Assert.NotNull(released); }
    }

    /// <summary>A concurrent Bootstrap lifetime is reported as typed contention and consumes the incoming lease.</summary>
    [Fact]
    public async Task OwnedHandoffReportsConcurrentBootstrapAsBusy()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string state = workspace.PathFor("state.json");
        var protocol = new ManagedLifetimeProtocol(TransportFixture.Names, JobNamePrefix);
        using ManagedProcessLifetimeLease lifetime = ManagedProcessLifetimeLease.TryAcquire(protocol, state,
            Nvt.Core.Launcher.Coordination.ManagedProcessLifetimeKind.Bootstrap)
            ?? throw new InvalidOperationException("Bootstrap lifetime was not acquired.");
        using var lease = Lease(workspace.Root);
        ImmutableBootstrapStartResult result = await Create(workspace.Root, state).StartAsync(workspace.Root, Identity(), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ImmutableBootstrapStartIssue.Busy, result.Issue);
        Assert.Null(result.Launch);
        Assert.True(result.HasValidShape);
        Assert.Equal(1, lease.DisposeCount);
    }

    /// <summary>The source Bootstrap byte ceiling is explicit and rejects zero, negative, and one above its maximum.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(200000001L)]
    public void InvalidExecutableCeilingIsRejected(long maximum)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => Create(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "state.json"), maximumExecutableBytes: maximum));
        Assert.Equal("maximumExecutableBytes", exception.ParamName);
    }

    /// <summary>The smallest ceiling and both admitted maximum-byte boundaries remain accepted.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(199999999L)]
    [InlineData(200000000L)]
    public void ExecutableCeilingBoundaryIsAccepted(long maximum)
    {
        _ = Create(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "state.json"), maximumExecutableBytes: maximum);
    }

    /// <summary>An owned lease cannot widen the caller's explicit executable ceiling.</summary>
    [Fact]
    public async Task OwnedIdentityAboveExplicitExecutableCeilingIsRejected()
    {
        using var workspace = TestWorkspace.Create();
        using var lease = Lease(workspace.Root);
        ImmutableBootstrapStartResult result = await Create(workspace.Root, workspace.PathFor("state.json"), maximumExecutableBytes: 1)
            .StartAsync(workspace.Root, Identity(2), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ImmutableBootstrapStartIssue.Damaged, result.Issue);
        Assert.Equal(1, lease.DisposeCount);
    }

    /// <summary>The serialized identity ceiling always requires a positive explicit argument.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonpositiveIdentityCeilingIsRejected(int maximum)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => Create(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "state.json"), maximumIdentityCharacters: maximum));
        Assert.Equal("maximumBootstrapIdentityCharacters", exception.ParamName);
    }

    /// <summary>The explicit cleanup observation cap preserves the frozen half-second maximum and its adjacent ticks.</summary>
    [Theory]
    [InlineData(-1L, false)]
    [InlineData(0L, false)]
    [InlineData(1L, true)]
    [InlineData(4999999L, true)]
    [InlineData(5000000L, true)]
    [InlineData(5000001L, false)]
    public void CleanupObservationBoundaryIsExact(long ticks, bool accepted)
    {
        Action create = () => _ = Create(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "state.json"), maximumCleanupObservation: TimeSpan.FromTicks(ticks));
        if (accepted) { Assert.Null(Record.Exception(create)); }
        else { Assert.Equal("maximumCleanupObservation", Assert.Throws<ArgumentOutOfRangeException>(create).ParamName); }
    }

    /// <summary>A missing legacy target fails and its exact verified replacement starts through the sole contained gate.</summary>
    [Fact]
    public async Task StableLauncherHandoffRejectsMissingAndStartsExactLauncher()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Bootstrap");
        string root = Path.GetDirectoryName(executable)!;
        WindowsCustodyCapability.RequireFile(root);
        ManagedImmutableBootstrapIdentity identity = Measure(executable);
        int pid = 0;
        var handoff = Create(root, workspace.PathFor("state.json"), identity, hasExited: process => { pid = process.Id; return false; });
        File.Move(executable, executable + ".missing");
        Assert.Equal(new(StableLauncherStartOutcome.HandoffFailed), await handoff.TryStartLauncherAsync(TestContext.Current.CancellationToken));
        File.Move(executable + ".missing", executable);
        using var environment = new ProtocolEnvironmentScope(("CORE_TEST_PROBE_MODE", "silent-wait"));
        try { Assert.Equal(new(StableLauncherStartOutcome.Started), await handoff.TryStartLauncherAsync(TestContext.Current.CancellationToken)); }
        finally { Terminate(pid); }
    }

    /// <summary>Length and digest mismatches cannot satisfy inherited restart authority.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LegacyExecutableIdentityMismatchIsRejected(int lengthDelta)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Bootstrap");
        string root = Path.GetDirectoryName(executable)!;
        WindowsCustodyCapability.RequireFile(root);
        ManagedImmutableBootstrapIdentity exact = Measure(executable);
        string digest = lengthDelta == 0 ? (exact.Sha256[0] == 'a' ? "b" : "a") + exact.Sha256[1..] : exact.Sha256;
        ManagedImmutableBootstrapIdentity wrong = ManagedImmutableBootstrapIdentity.Create(TransportFixture.Descriptor, exact.FileName, exact.Length + lengthDelta, digest);
        Assert.Equal(new(StableLauncherStartOutcome.HandoffFailed), await Create(root, workspace.PathFor("state.json"), wrong).TryStartLauncherAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Cancellation inside final verification prevents native creation after custody I/O completes.</summary>
    [Fact]
    public async Task StableLauncherHandoffCancellationDuringVerificationDoesNotLaunch()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Bootstrap");
        string root = Path.GetDirectoryName(executable)!;
        WindowsCustodyCapability.RequireFile(root);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bool verified = false;
        var handoff = Create(root, workspace.PathFor("state.json"), Measure(executable), validateLauncherForStart: lease =>
        {
            cancellation.Cancel();
            verified = lease.TryValidateForStart();
            return verified;
        });
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await handoff.TryStartLauncherAsync(cancellation.Token));
        Assert.True(verified);
    }

    /// <summary>Post-create observation failure remains a handoff failure with no false creation or exit receipt.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StableLauncherHandoffReportsPostCreationObservationFailure(bool failOnExitCode)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Bootstrap");
        string root = Path.GetDirectoryName(executable)!;
        WindowsCustodyCapability.RequireFile(root);
        int pid = 0;
        var handoff = Create(root, workspace.PathFor("state.json"), Measure(executable), hasExited: process =>
        {
            pid = process.Id;
            return failOnExitCode ? true : throw new Win32Exception(5);
        }, getExitCode: static _ => throw new Win32Exception(5));
        using var environment = new ProtocolEnvironmentScope(("CORE_TEST_PROBE_MODE", "silent-wait"));
        try { Assert.Equal(new(StableLauncherStartOutcome.HandoffFailed), await handoff.TryStartLauncherAsync(TestContext.Current.CancellationToken)); }
        finally { Terminate(pid); }
    }

    /// <summary>The public owned handoff uses real contained creation and cancels every descendant through its returned receipt.</summary>
    [Fact]
    public async Task OwnedHandoffStartsContainedTreeAndCancellationConfirmsDescendantExit()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Bootstrap");
        string root = Path.GetDirectoryName(executable)!;
        string statePath = workspace.PathFor("state.json");
        string marker = workspace.PathFor("owned-child.txt");
        var lease = new CountingLease(executable, root);
        using var environment = new ProtocolEnvironmentScope(
            ("CORE_TEST_PROBE_MODE", "tree-root-wait"), ("CORE_TEST_PROBE_TREE_MARKER", marker));
        ImmutableBootstrapStartResult start = await Create(root, statePath).StartAsync(
            root, Measure(executable), lease, TestContext.Current.CancellationToken);
        Assert.True(start.IsStarted);
        Assert.True(start.HasValidShape);
        using IImmutableBootstrapLaunch launch = Assert.IsAssignableFrom<IImmutableBootstrapLaunch>(start.Launch);
        var deadline = Stopwatch.StartNew();
        int childId = 0;
        while (deadline.Elapsed < TimeSpan.FromSeconds(5) && childId == 0)
        {
            if (File.Exists(marker))
            {
                try { _ = int.TryParse(await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken),
                    System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out childId); }
                catch (IOException) { }
            }
            if (childId == 0) { await Task.Delay(10, TestContext.Current.CancellationToken); }
        }
        Assert.NotEqual(0, childId);
        using Process child = Process.GetProcessById(childId);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        ImmutableBootstrapAdmissionResult admission = await launch.WaitForAdmissionAsync(
            new ImmutableBootstrapWaitBudget(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5_500)), cancellation.Token);
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, admission.Outcome);
        Assert.True(admission.HasValidShape);
        Assert.True(child.WaitForExit(5_000));
        Assert.Equal(1, lease.DisposeCount);
        Assert.Equal(ManagedProcessLifetimeLeaseAcquisitionOutcome.Acquired,
            ManagedProcessLifetimeLease.Acquire(new ManagedLifetimeProtocol(TransportFixture.Names, JobNamePrefix),
                statePath, ManagedProcessLifetimeKind.Bootstrap, out ManagedProcessLifetimeLease? released));
        released!.Dispose();
    }

    /// <summary>An explicit identity ceiling failure propagates its exact argument error and releases every transferred authority.</summary>
    [Fact]
    public async Task OversizedOwnedIdentityDisposesAllTransferredAuthority()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        var lease = Lease(workspace.Root);
        string statePath = workspace.PathFor("state.json");
        var handoff = Create(workspace.Root, statePath, maximumIdentityCharacters: 1);
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await handoff.StartAsync(workspace.Root, Identity(), lease, TestContext.Current.CancellationToken));
        Assert.Equal("identity", exception.ParamName);
        Assert.StartsWith("Inherited Bootstrap identity is oversized.", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, lease.DisposeCount);
        Assert.Equal(ManagedProcessLifetimeLeaseAcquisitionOutcome.Acquired,
            ManagedProcessLifetimeLease.Acquire(new ManagedLifetimeProtocol(TransportFixture.Names, JobNamePrefix),
                statePath, ManagedProcessLifetimeKind.Bootstrap, out ManagedProcessLifetimeLease? released));
        released!.Dispose();
    }

    private static StableLauncherHandoff Create(string root, string state,
        ManagedImmutableBootstrapIdentity? identity = null, Action<string>? beforeProcessStart = null,
        Action? afterExecutableAcquired = null, Func<IManagedExecutableLaunchLease, bool>? validateLauncherForStart = null,
        Func<Process, bool>? hasExited = null, Func<Process, int>? getExitCode = null,
        long maximumExecutableBytes = 200_000_000, int maximumIdentityCharacters = 128, TimeSpan? maximumCleanupObservation = null) =>
        new(TransportFixture.Descriptor, maximumExecutableBytes, maximumIdentityCharacters, JobNamePrefix,
            root, state, maximumCleanupObservation ?? TimeSpan.FromMilliseconds(500), ManagedProcessTermination.Instance,
            beforeProcessStart, afterExecutableAcquired, identity, validateLauncherForStart, hasExited, getExitCode);
    private static ManagedImmutableBootstrapIdentity Identity(long length = 1) =>
        ManagedImmutableBootstrapIdentity.Create(TransportFixture.Descriptor, TransportFixture.Descriptor.BootstrapExecutableFileName, length, new string('a', 64));
    private static ManagedImmutableBootstrapIdentity Measure(string executable)
    {
        byte[] bytes = File.ReadAllBytes(executable);
        return ManagedImmutableBootstrapIdentity.Create(TransportFixture.Descriptor, Path.GetFileName(executable), bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    private static CountingLease Lease(string root, bool valid = true) => new(Path.Combine(root, TransportFixture.Descriptor.BootstrapExecutableFileName), root, valid);
    private static void Terminate(int pid)
    {
        if (pid == 0) { return; }
        try { using Process process = Process.GetProcessById(pid); if (!process.HasExited) { process.Kill(entireProcessTree: true); Assert.True(process.WaitForExit(5_000)); } }
        catch (ArgumentException) { }
    }
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows contained creation and named Job lifetimes."); }
    }
    private sealed class CountingLease(string executable, string directory, bool valid = true) : IManagedExecutableLaunchLease
    {
        // Guard: Interlocked/Volatile count calls without changing the source's exact disposal assertion.
        private int _disposals;
        public int DisposeCount => Volatile.Read(ref _disposals);
        public string ExecutablePath => executable;
        public string WorkingDirectory => directory;
        public bool TryValidateForStart() => valid;
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }
}
