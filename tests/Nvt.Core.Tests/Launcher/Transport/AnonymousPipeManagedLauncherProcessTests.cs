// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.LinkedProbe;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Tests exact parent-side launcher admission, ambient identity denial, and accepted Job lifetime.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class AnonymousPipeManagedLauncherProcessTests
{
    private const string JobNamePrefix = @"Local\CoreFixture.ManagedTree";

    /// <summary>The exact identity and custom state path reach the candidate launcher.</summary>
    [Fact]
    public async Task ExactReadyCarriesCustomStatePathToCandidate()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Launcher");
        string statePath = workspace.GetPath("state/custom state.json");
        string arguments = workspace.GetPath("arguments.txt");
        using var environment = ProbeEnvironment("ready", arguments);
        using BootstrapAdmissionSignal admission = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        using var process = Create(admission);
        using var lease = Lease(executable);
        LauncherProcessStartResult result = await process.StartUntilReadyAsync(workspace.RootPath, statePath,
            TransportFixture.Launcher(), lease, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(LauncherProcessStartOutcome.Ready, result.Outcome);
        Assert.Equal(new[] { "--managed-root", Path.GetFullPath(workspace.RootPath), "--state-path", Path.GetFullPath(statePath) },
            await File.ReadAllLinesAsync(arguments, TestContext.Current.CancellationToken));
        Assert.Equal(new ManagedVersionAdmission(TransportFixture.Version, "fixture-admission", new string('a', 64)),
            result.ReadyAdmission);
    }

    /// <summary>Accepted outer READY releases cleanup authority so the real descendant survives parent disposal.</summary>
    [Fact]
    public async Task AcceptedOuterReadyKeepsChildAndGrandchildAlive()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Launcher");
        string marker = workspace.GetPath("accepted-tree");
        int rootId = 0;
        int childId = 0;
        try
        {
            using var environment = ProbeEnvironment("ready-tree-root", treeMarker: marker);
            using BootstrapAdmissionSignal admission = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
            using (var process = Create(admission))
            using (var lease = Lease(executable))
            {
                LauncherProcessStartResult result = await process.StartUntilReadyAsync(workspace.RootPath,
                    workspace.GetPath("state/custom state.json"), TransportFixture.Launcher(), lease,
                    TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                rootId = await ReadProcessMarkerAsync(marker + ".root");
                childId = await ReadProcessMarkerAsync(marker + ".child");
                Assert.Equal(LauncherProcessStartOutcome.Ready, result.Outcome);
            }
            long exitDeadline = Environment.TickCount64 + 5_000;
            while (IsRunning(rootId) && Environment.TickCount64 < exitDeadline)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.False(IsRunning(rootId));
            using Process descendant = Process.GetProcessById(childId);
            Assert.False(descendant.HasExited);
        }
        finally
        {
            Terminate(rootId);
            Terminate(childId);
        }
    }

    /// <summary>The real shared child cannot authenticate a different prefix or truncated partial READY.</summary>
    [Theory]
    [InlineData("ready-wrong-identity", 0, LauncherProcessStartOutcome.InvalidReadySignal)]
    [InlineData("ready-partial", 0, LauncherProcessStartOutcome.Ready)]
    [InlineData("ready-partial", 1, LauncherProcessStartOutcome.InvalidReadySignal)]
    [InlineData("invalid-utf8", 0, LauncherProcessStartOutcome.StartFailed)]
    public async Task ExactReadyWireAndFrozenFailureMappingArePreserved(
        string mode, int drop, LauncherProcessStartOutcome expected)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Launcher");
        using var environment = ProbeEnvironment(mode);
        using var partial = new ProtocolEnvironmentScope(
            ("CORE_TEST_PROBE_PARTIAL_DROP", drop.ToString(CultureInfo.InvariantCulture)));
        using BootstrapAdmissionSignal admission = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        using var process = Create(admission);
        using var lease = Lease(executable);
        LauncherProcessStartResult result = await process.StartUntilReadyAsync(workspace.RootPath,
            workspace.GetPath("state.json"), TransportFixture.Launcher(), lease,
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
    }

    /// <summary>Real outer READY input tests the exact 4,096-character ceiling and both adjacent values.</summary>
    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    public async Task LauncherReadyCharacterBoundaryIsExact(int characters)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Launcher");
        using var environment = ProbeEnvironment("oversized");
        using var size = new ProtocolEnvironmentScope(
            ("CORE_TEST_PROBE_OVERSIZE_CHARS", characters.ToString(CultureInfo.InvariantCulture)));
        using BootstrapAdmissionSignal admission = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        using var process = Create(admission);
        using var lease = Lease(executable);
        LauncherProcessStartResult result = await process.StartUntilReadyAsync(workspace.RootPath,
            workspace.GetPath("state.json"), TransportFixture.Launcher(), lease,
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(LauncherProcessStartOutcome.InvalidReadySignal, result.Outcome);
    }

    /// <summary>A Core-linked child captures lifetime, observes only explicit identity, and clears it before READY.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedLauncherPropagatesOnlyExplicitBootstrapIdentity(bool explicitIdentity)
    {
        RequireWindows();
        ManagedImmutableBootstrapIdentity identity = ManagedImmutableBootstrapIdentity.Create(
            TransportFixture.Descriptor, TransportFixture.Descriptor.BootstrapExecutableFileName, 67890, new string('b', 64));
        string[] observation = await ObserveIdentityAsync(explicitIdentity ? identity : null, "1|forged");
        Assert.Equal("Captured", observation[0]);
        Assert.Equal(explicitIdentity ? $"1|{identity.FileName}|67890|{identity.Sha256}" : "<null>", observation[1]);
        Assert.Equal("<null>", observation[2]);
        Assert.Equal(explicitIdentity ? identity.FileName : "<null>", observation[3]);
        Assert.Equal(explicitIdentity ? "67890" : "<null>", observation[4]);
        Assert.Equal(explicitIdentity ? identity.Sha256 : "<null>", observation[5]);
    }

    /// <summary>A managed Launcher strips ambient Bootstrap identity that was not explicitly admitted.</summary>
    [Fact]
    public async Task ManagedLauncherDoesNotLaunderAmbientBootstrapIdentity()
    {
        RequireWindows();
        string[] observation = await ObserveIdentityAsync(null,
            $"1|{TransportFixture.Descriptor.BootstrapExecutableFileName}|12345|{new string('a', 64)}");
        Assert.Equal("Captured", observation[0]);
        Assert.Equal("<null>", observation[1]);
        Assert.Equal("<null>", observation[2]);
        Assert.Equal("<null>", observation[3]);
    }

    /// <summary>A real managed child clears malformed ambient identity without losing its lifetime or READY.</summary>
    [Fact]
    public async Task ManagedLauncherClearsMalformedIdentityAndStillReachesReady()
    {
        RequireWindows();
        string[] observation = await ObserveIdentityAsync(null, "1|malformed");
        Assert.Equal("Captured", observation[0]);
        Assert.Equal("<null>", observation[1]);
        Assert.Equal("<null>", observation[2]);
        Assert.Equal("<null>", observation[3]);
    }

    private static async Task<string[]> ObserveIdentityAsync(ManagedImmutableBootstrapIdentity? identity, string ambient)
    {
        await using var workspace = new LinkedProbeWorkspace();
        string marker = workspace.PathFor("identity.txt");
        using var environment = new ProtocolEnvironmentScope(
            ("CORE_LINKED_PROBE_MODE", "launcher-identity-observation"), ("CORE_LINKED_PROBE_MARKER", marker),
            (TransportFixture.Names.BootstrapIdentity, ambient), (TransportFixture.Names.ExpectedApplicationVersion, null));
        using BootstrapAdmissionSignal admission = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        using var process = Create(admission, identity: identity);
        using var lease = Lease(workspace.Executable);
        LauncherProcessStartResult result = await process.StartUntilReadyAsync(workspace.Root, workspace.PathFor("state.json"),
            TransportFixture.Launcher(), lease, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(LauncherProcessStartOutcome.Ready, result.Outcome);
        string[] observation = await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken);
        Assert.Equal(6, observation.Length);
        return observation;
    }

    /// <summary>The first admitted attempt emits exactly ADMITTED bytes and a later attempt emits none.</summary>
    [Fact]
    public async Task CandidateReadyTimeoutThenLkgReadyDoesNotReemitAdmission()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Launcher");
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var inherited = new ProtocolEnvironmentScope((TransportFixture.Names.BootstrapAdmissionHandle, duplicate));
        using BootstrapAdmissionSignal admission = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        pipe.DisposeLocalCopyOfClientHandle();
        using var expiry = new CancellationTokenSource();
        using var candidate = Create(admission, hooks: new(DeadlineSignal: expiry.Token));
        using var fallback = Create(admission);
        using var lease = Lease(executable);
        using var environment = ProbeEnvironment("silent-wait");
        Task<LauncherProcessStartResult> candidateStart = candidate.StartUntilReadyAsync(workspace.RootPath,
            workspace.GetPath("state.json"), TransportFixture.Launcher(), lease,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).AsTask();
        using var reader = new StreamReader(pipe);
        using var receiptDeadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        receiptDeadline.CancelAfter(TimeSpan.FromSeconds(10));
        string? admitted = await reader.ReadLineAsync(receiptDeadline.Token);
        expiry.Cancel();
        LauncherProcessStartResult failed = await candidateStart;
        using var ready = ProbeEnvironment("ready");
        LauncherProcessStartResult result = await fallback.StartUntilReadyAsync(workspace.RootPath,
            workspace.GetPath("state.json"), TransportFixture.Launcher(), lease,
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(LauncherProcessStartOutcome.ReadyTimeout, failed.Outcome);
        Assert.Equal(LauncherProcessStartOutcome.Ready, result.Outcome);
        Assert.Equal("ADMITTED", admitted);
        Assert.Null(await reader.ReadLineAsync(receiptDeadline.Token));
    }

    /// <summary>The exact application admission is not inferred from a later root exit code.</summary>
    [Fact]
    public async Task NestedUnconfirmedTerminationExitRemainsTyped()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string executable = ProcessProbe.CopyAndRename(workspace, "Fixture.Launcher");
        using var environment = ProbeEnvironment("exit");
        using var exit = new ProtocolEnvironmentScope(("CORE_TEST_PROBE_EXIT_CODE", "17"));
        using BootstrapAdmissionSignal admission = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        using var process = Create(admission);
        using var lease = Lease(executable);
        LauncherProcessStartResult result = await process.StartUntilReadyAsync(workspace.RootPath,
            workspace.GetPath("state.json"), TransportFixture.Launcher(), lease,
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(LauncherProcessStartOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.Equal(17, result.ExitCode);
    }

    /// <summary>Each product ceiling is positive, and validation precedes inherited handle capture.</summary>
    [Theory]
    [InlineData(-1, 128, "maximumReadyLineCharacters")]
    [InlineData(0, 128, "maximumReadyLineCharacters")]
    [InlineData(4096, -1, "maximumBootstrapIdentityCharacters")]
    [InlineData(4096, 0, "maximumBootstrapIdentityCharacters")]
    public void NonpositiveCeilingsAreRejected(int ready, int identity, string parameter)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnonymousPipeManagedLauncherProcess(TransportFixture.Descriptor, ready, identity, JobNamePrefix));
        Assert.Equal(parameter, exception.ParamName);
    }

    /// <summary>The explicit READY boundary and adjacent positive parameters are accepted without defaults.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4095, 127)]
    [InlineData(4096, 128)]
    [InlineData(4097, 129)]
    public void PositiveExplicitCeilingsAreAccepted(int ready, int identity)
    {
        using var environment = new ProtocolEnvironmentScope((TransportFixture.Names.BootstrapAdmissionHandle, null));
        using var process = new AnonymousPipeManagedLauncherProcess(TransportFixture.Descriptor, ready, identity, JobNamePrefix);
        Assert.NotNull(process);
    }

    private static AnonymousPipeManagedLauncherProcess Create(BootstrapAdmissionSignal admission,
        ManagedProcessStartHooks? hooks = null, ManagedImmutableBootstrapIdentity? identity = null)
    {
        ManagedProcessStartHooks effective = (hooks ?? new()) with
        {
            BeforeStartValidation = info =>
            {
                info.CreateNoWindow = true;
                hooks?.BeforeStartValidation?.Invoke(info);
            },
        };
        return new(TransportFixture.Descriptor, 4096, 128, JobNamePrefix,
            ManagedProcessTermination.Instance, admission, effective, identity);
    }

    private static ProtocolEnvironmentScope ProbeEnvironment(string mode, string? arguments = null,
        string? treeMarker = null, string? identityMarker = null) =>
        new(("CORE_TEST_PROBE_MODE", mode), (TransportFixture.Names.ExpectedApplicationVersion, null),
            ("CORE_TEST_PROBE_APP_VERSION", "1.2.3"), ("CORE_TEST_PROBE_APP_ADMISSION", "fixture-admission"),
            ("CORE_TEST_PROBE_APP_MANIFEST", new string('a', 64)), ("CORE_TEST_PROBE_ARGS_PATH", arguments),
            ("CORE_TEST_PROBE_TREE_MARKER", treeMarker), ("CORE_TEST_PROBE_IDENTITY_MARKER", identityMarker));

    private static ProbeLease Lease(string executable) => new(executable, Path.GetDirectoryName(executable)!);

    private static async Task<int> ReadProcessMarkerAsync(string marker)
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

    private static bool IsRunning(int processId)
    {
        try { using Process process = Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static void Terminate(int processId)
    {
        if (processId == 0) { return; }
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.HasExited) { process.Kill(entireProcessTree: true); _ = process.WaitForExit(5_000); }
        }
        catch (ArgumentException) { }
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows contained creation and Job supervision."); }
    }

    private sealed record ProbeLease(string ExecutablePath, string WorkingDirectory) : IManagedExecutableLaunchLease
    {
        public bool TryValidateForStart() => true;
        public void Dispose() { }
    }
}
