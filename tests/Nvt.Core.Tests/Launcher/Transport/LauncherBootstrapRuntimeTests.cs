// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using System.Text;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Verifies Bootstrap capture ordering, mandatory composition, typed exits, and snapshot-qualified one-use nested READY.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class LauncherBootstrapRuntimeTests
{
    private const string JobNamePrefix = @"Local\CoreFixture.ManagedTree";
    private static readonly string[] ContextKeys =
    [
        TransportFixture.Names.LifetimeContext, TransportFixture.Names.LifetimeHandle,
        TransportFixture.Names.LifetimeJob, TransportFixture.Names.LifetimeStatePath,
        TransportFixture.Names.LifetimeKind, TransportFixture.Names.BootstrapStartContext,
        TransportFixture.Names.BootstrapStartHandle, TransportFixture.Names.BootstrapAdmissionHandle,
        TransportFixture.Names.BootstrapIdentity, TransportFixture.Names.LauncherReadyHandle,
        TransportFixture.Names.ExpectedLauncherReady,
    ];
    private static readonly TraceExpectations ExpectedTrace = new(
        ["compose", "app.load"], ["lifetime"],
        ["lifetime", "compose", "app.load", "lease.dispose", "job.dispose"],
        ["writer.acquire", "app.load", "launcher.load", "writer.dispose"]);
    private sealed record TraceExpectations(string[] Legacy, string[] BeforeAuthorization, string[] Inherited, string[] Ready);

    private static ManagedVersionAdmission Admission { get; } = new(TransportFixture.Version, "fixture-admission", new string('a', 64));

    /// <summary>Legacy entry clears forged ambient identity before any adapter sees managed state.</summary>
    [Fact]
    public async Task LegacyBootstrapClearsAmbientIdentityBeforeManagedStateAccess()
    {
        using var workspace = TestWorkspace.Create();
        using var environment = EmptyContext();
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapIdentity, SerializedIdentity());
        var trace = new TraceLog();
        var runtime = Runtime((root, state) =>
        {
            Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapIdentity));
            Assert.Equal(Path.GetFullPath(workspace.RootPath), root);
            Assert.Equal(workspace.GetPath("state.json"), state);
            trace.Add("compose");
            return Services(new StateStore(new(null, VersionManagerStateLoadIssue.Invalid), trace));
        });
        Assert.Equal(10, await runtime.RunEntryAsync(workspace.RootPath, workspace.GetPath("state.json"), TestContext.Current.CancellationToken));
        Assert.Equal(ExpectedTrace.Legacy, trace.Events);
    }

    /// <summary>Every partial startup component is consumed and rejected before managed-root validation or adapter composition.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PartialStartupReturnsInvalidContextWithoutManagedStateAccess(int field)
    {
        using var workspace = TestWorkspace.Create();
        using var environment = EmptyContext();
        string key = field == 0 ? TransportFixture.Names.LifetimeContext : field == 1
            ? TransportFixture.Names.BootstrapStartContext : TransportFixture.Names.BootstrapAdmissionHandle;
        Environment.SetEnvironmentVariable(key, "v1");
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapIdentity, SerializedIdentity());
        var runtime = Runtime(static (_, _) => throw new InvalidOperationException("Managed state must not be accessed."));
        Assert.Equal(22, await runtime.RunEntryAsync(string.Empty, workspace.GetPath("state.json"), TestContext.Current.CancellationToken));
        Assert.All(ContextKeys, static name => Assert.Null(Environment.GetEnvironmentVariable(name)));
    }

    /// <summary>Identity clearing follows lifetime capture, and managed state remains inaccessible until exact START authorization.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("1|malformed")]
    [InlineData("valid")]
    public async Task IdentityIsClearedAfterLifetimeCaptureAndBeforeAuthorizedComposition(string? identity)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using var environment = EmptyContext();
        using var start = new BootstrapStartAuthorization(TransportFixture.Names);
        using var admission = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        string startHandle = DuplicateStart(start);
        string admissionHandle = WindowsPipeHandles.DuplicateClient(admission);
        Environment.SetEnvironmentVariable(TransportFixture.Names.LifetimeContext, "v1");
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapStartContext, "v1");
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapStartHandle, startHandle);
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapAdmissionHandle, admissionHandle);
        string? serialized = identity == "valid" ? SerializedIdentity() : identity;
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapIdentity, serialized);
        var trace = new TraceLog();
        var hooks = new LauncherBootstrapRuntimeHooks((state, kind, advertised) =>
        {
            Assert.Equal(workspace.GetPath("state.json"), state);
            Assert.Equal(ManagedProcessLifetimeKind.Bootstrap, kind);
            Assert.True(advertised);
            Assert.Equal(serialized, Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapIdentity));
            Assert.Equal(startHandle, Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapStartHandle));
            trace.Add("lifetime");
            Environment.SetEnvironmentVariable(TransportFixture.Names.LifetimeContext, null);
            return InheritedManagedProcessLifetimeCapture.Create(new ActionDisposable(() => trace.Add("lease.dispose")),
                new ActionDisposable(() => trace.Add("job.dispose")));
        });
        var runtime = Runtime((_, _) =>
        {
            Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapIdentity));
            trace.Add("compose");
            return Services(new StateStore(new(null, VersionManagerStateLoadIssue.Invalid), trace));
        }, hooks);
        Task<int> entry = runtime.RunEntryAsync(workspace.RootPath, workspace.GetPath("state.json"), TestContext.Current.CancellationToken).AsTask();
        Assert.False(entry.IsCompleted);
        Assert.Equal(ExpectedTrace.BeforeAuthorization, trace.Events);
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapIdentity));
        Assert.Equal(0u, WindowsPipeHandles.Flags(startHandle) & 1u);
        Assert.Equal(0u, WindowsPipeHandles.Flags(admissionHandle) & 1u);
        Assert.True(start.TryAuthorize());
        Assert.Equal(10, await entry.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(ExpectedTrace.Inherited, trace.Events);
        Assert.False(WindowsPipeHandles.IsOpen(startHandle));
        Assert.False(WindowsPipeHandles.IsOpen(admissionHandle));
        // The local seam isolates capture order; the linked child also runs native capture
        // through the public runtime, with actual handle closure and gated composition.
        await LinkedBootstrapTests.AssertRuntimeAsync(identity, authorize: true);
    }

    /// <summary>A malformed START signal returns its frozen exit reason before any managed-state access.</summary>
    [Fact]
    public async Task UnauthorizedStartReturnsExactExitBeforeComposition()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using var environment = EmptyContext();
        using var start = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        using var admission = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapStartContext, "v1");
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapStartHandle, WindowsPipeHandles.DuplicateClient(start));
        Environment.SetEnvironmentVariable(TransportFixture.Names.BootstrapAdmissionHandle, WindowsPipeHandles.DuplicateClient(admission));
        var hooks = new LauncherBootstrapRuntimeHooks(static (_, _, _) =>
            InheritedManagedProcessLifetimeCapture.Create(new ActionDisposable(static () => { }), new ActionDisposable(static () => { })));
        var runtime = Runtime(static (_, _) => throw new InvalidOperationException("Managed state must not be accessed."), hooks);
        await start.WriteAsync("STOP\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await start.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(23, await runtime.RunEntryAsync(workspace.RootPath, workspace.GetPath("state.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>Seed and writer failures preserve exact Bootstrap exit values through the real runtime composition.</summary>
    [Theory]
    [InlineData("invalid-existing", 10)]
    [InlineData("missing-seed", 10)]
    [InlineData("busy", 2)]
    [InlineData("unavailable", 18)]
    [InlineData("root-mismatch", 11)]
    [InlineData("existing-coordinator-busy", 2)]
    public async Task SeedAndCoordinatorEntryFailureMappingIsExact(string scenario, int expected)
    {
        using var workspace = TestWorkspace.Create();
        using var environment = EmptyContext();
        var trace = new TraceLog();
        VersionManagerStateLoadResult load = scenario == "invalid-existing" ? new(null, VersionManagerStateLoadIssue.Invalid)
            : scenario == "root-mismatch" ? new(AppState(workspace.GetPath("other")), VersionManagerStateLoadIssue.None)
            : scenario == "existing-coordinator-busy" ? new(AppState(workspace.RootPath), VersionManagerStateLoadIssue.None)
            : new(null, VersionManagerStateLoadIssue.Missing);
        VersionManagerWriteLeaseIssue writer = scenario is "busy" or "existing-coordinator-busy" ? VersionManagerWriteLeaseIssue.Busy
            : scenario == "unavailable" ? VersionManagerWriteLeaseIssue.Unavailable : VersionManagerWriteLeaseIssue.None;
        var runtime = Runtime((_, _) => Services(new StateStore(load, trace, writer)));
        Assert.Equal(expected, await runtime.RunEntryAsync(workspace.RootPath, workspace.GetPath("state.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>Every coordinator outcome keeps its exact completion or encoded failure exit, including undefined outcomes.</summary>
    [Theory]
    [InlineData(LauncherBootstrapOutcome.Ready, 0)]
    [InlineData(LauncherBootstrapOutcome.RolledBack, 1)]
    [InlineData(LauncherBootstrapOutcome.Busy, 2)]
    [InlineData(LauncherBootstrapOutcome.InvalidState, 10)]
    [InlineData(LauncherBootstrapOutcome.ManagedRootMismatch, 11)]
    [InlineData(LauncherBootstrapOutcome.AppMutationPending, 12)]
    [InlineData(LauncherBootstrapOutcome.DamagedLauncher, 13)]
    [InlineData(LauncherBootstrapOutcome.ProtocolMismatch, 14)]
    [InlineData(LauncherBootstrapOutcome.StartFailed, 15)]
    [InlineData(LauncherBootstrapOutcome.RollbackUnavailable, 16)]
    [InlineData(LauncherBootstrapOutcome.StateChanged, 17)]
    [InlineData(LauncherBootstrapOutcome.StateUnavailable, 18)]
    [InlineData(LauncherBootstrapOutcome.TerminationUnconfirmed, 19)]
    [InlineData((LauncherBootstrapOutcome)(-1), 99)]
    public void CoordinatorOutcomeEncodingIsExact(LauncherBootstrapOutcome outcome, int expected) =>
        Assert.Equal(expected, LauncherBootstrapRuntime.EncodeCoordinatorOutcome(outcome));

    /// <summary>Missing nested READY authority cannot compose or read state.</summary>
    [Fact]
    public async Task MissingNestedReadyContextDoesNotReadState()
    {
        using var environment = EmptyContext();
        var runtime = Runtime(static (_, _) => throw new InvalidOperationException("State must not be accessed."));
        using LauncherReadyInheritance context = runtime.CaptureNestedReadyContext();
        Assert.Equal(LauncherReadyInheritanceOutcome.NotInherited, context.Outcome);
        Assert.False(await runtime.ReportNestedReadyAsync(context, "unused", "unused", TestContext.Current.CancellationToken));
    }

    /// <summary>A malformed nested READY identity clears the environment and closes its captured native handle.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("READY-LAUNCHER:2:1.2.3:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void MalformedNestedReadyContextClosesHandle(string? expected)
    {
        RequireWindows();
        using var environment = EmptyContext();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        string handle = WindowsPipeHandles.DuplicateClient(pipe);
        Environment.SetEnvironmentVariable(TransportFixture.Names.LauncherReadyHandle, handle);
        Environment.SetEnvironmentVariable(TransportFixture.Names.ExpectedLauncherReady, expected);
        var runtime = Runtime(static (_, _) => throw new InvalidOperationException("State must not be accessed."));
        using LauncherReadyInheritance context = runtime.CaptureNestedReadyContext();
        Assert.Equal(LauncherReadyInheritanceOutcome.InvalidInheritedContext, context.Outcome);
        Assert.False(WindowsPipeHandles.IsOpen(handle));
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.LauncherReadyHandle));
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.ExpectedLauncherReady));
    }

    /// <summary>Candidate, previous LKG, and active identities can each report the exact reloaded admission only once.</summary>
    [Theory]
    [InlineData("candidate")]
    [InlineData("previous")]
    [InlineData("active")]
    public async Task SnapshotQualifiedNestedReadyHasExactBytesAndOneUse(string selected)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using var environment = EmptyContext();
        ManagedLauncherIdentity active = TransportFixture.Launcher();
        ManagedLauncherIdentity candidate = Launcher('c');
        ManagedLauncherIdentity previous = Launcher('d');
        PendingLauncherActivation pending = PendingLauncherActivation.Create(candidate, active, previous, LauncherActivationPhase.CandidateLaunchRecorded);
        LauncherBootstrapState state = LauncherBootstrapState.Create(workspace.RootPath, active, previous, pending, null);
        ManagedLauncherIdentity expected = selected == "candidate" ? candidate : selected == "previous" ? previous : active;
        var trace = new TraceLog();
        var runtime = Runtime((root, path) =>
        {
            Assert.Equal(workspace.RootPath, root);
            Assert.Equal(workspace.GetPath("state.json"), path);
            return Services(new StateStore(new(AppState(root), VersionManagerStateLoadIssue.None), trace), new LauncherStore(new(state, LauncherBootstrapStateLoadIssue.None), trace));
        });
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        string handle = WindowsPipeHandles.DuplicateClient(pipe);
        Environment.SetEnvironmentVariable(TransportFixture.Names.LauncherReadyHandle, handle);
        Environment.SetEnvironmentVariable(TransportFixture.Names.ExpectedLauncherReady, LauncherReadyProtocol.CreateExpectedPrefix(expected));
        using LauncherReadyInheritance context = runtime.CaptureNestedReadyContext();
        pipe.DisposeLocalCopyOfClientHandle();
        Assert.Equal(LauncherReadyInheritanceOutcome.Inherited, context.Outcome);
        Assert.Equal(0u, WindowsPipeHandles.Flags(handle) & 1u);
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.LauncherReadyHandle));
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.ExpectedLauncherReady));
        Assert.True(await runtime.ReportNestedReadyAsync(context, workspace.RootPath, workspace.GetPath("state.json"), TestContext.Current.CancellationToken));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        string bytes = await reader.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(LauncherReadyProtocol.Create(expected, Admission) + "\n", bytes);
        Assert.Equal(ExpectedTrace.Ready, trace.Events);
        Assert.False(await runtime.ReportNestedReadyAsync(context, workspace.RootPath, workspace.GetPath("state.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>Invalid snapshots, pending app journals, missing admission, writer failure, I/O, and cancellation cannot report READY.</summary>
    [Theory]
    [InlineData("app-invalid")]
    [InlineData("launcher-invalid")]
    [InlineData("app-root")]
    [InlineData("launcher-root")]
    [InlineData("activation")]
    [InlineData("mutation")]
    [InlineData("no-active")]
    [InlineData("different-launcher")]
    [InlineData("writer-busy")]
    [InlineData("io")]
    [InlineData("cancelled")]
    public async Task InvalidSnapshotCannotReportNestedReady(string mismatch)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using var environment = EmptyContext();
        var trace = new TraceLog();
        PendingVersionActivation? activation = mismatch == "activation"
            ? new(TransportFixture.Version, Admission.AdmissionIdentity, TransportFixture.Version, TransportFixture.Version) : null;
        PendingManagedVersionMutation? mutation = mismatch == "mutation"
            ? new(ManagedVersionMutationKind.Delete, new ManagedVersionAdmission(ManagedAppVersion.Parse("1.2.4"), "other-admission", new string('b', 64))) : null;
        VersionManagerState app = AppState(mismatch == "app-root" ? workspace.GetPath("other") : workspace.RootPath, activation, mutation, mismatch == "no-active");
        ManagedLauncherIdentity launcher = mismatch == "different-launcher" ? Launcher('f') : TransportFixture.Launcher();
        LauncherBootstrapState journal = LauncherBootstrapState.Create(mismatch == "launcher-root" ? workspace.GetPath("other") : workspace.RootPath, launcher, launcher, null, null);
        var store = new StateStore(mismatch == "app-invalid" ? new(null, VersionManagerStateLoadIssue.Invalid) : new(app, VersionManagerStateLoadIssue.None), trace,
            mismatch == "writer-busy" ? VersionManagerWriteLeaseIssue.Busy : VersionManagerWriteLeaseIssue.None, failRead: mismatch == "io");
        var runtime = Runtime((_, _) => Services(store, new LauncherStore(mismatch == "launcher-invalid"
            ? new(null, LauncherBootstrapStateLoadIssue.Invalid) : new(journal, LauncherBootstrapStateLoadIssue.None), trace)));
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        string handle = WindowsPipeHandles.DuplicateClient(pipe);
        Environment.SetEnvironmentVariable(TransportFixture.Names.LauncherReadyHandle, handle);
        Environment.SetEnvironmentVariable(TransportFixture.Names.ExpectedLauncherReady, LauncherReadyProtocol.CreateExpectedPrefix(TransportFixture.Launcher()));
        using LauncherReadyInheritance context = runtime.CaptureNestedReadyContext();
        pipe.DisposeLocalCopyOfClientHandle();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (mismatch == "cancelled") { cancellation.Cancel(); }
        Assert.False(await runtime.ReportNestedReadyAsync(context, workspace.RootPath, workspace.GetPath("state.json"), cancellation.Token));
        Assert.True(WindowsPipeHandles.IsOpen(handle));
        context.Dispose();
        Assert.False(WindowsPipeHandles.IsOpen(handle));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        Assert.Empty(await reader.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    /// <summary>Protocol line and serialized identity ceilings require explicit positive parameters.</summary>
    [Theory]
    [InlineData(-1, 128, "maximumReadyLineCharacters")]
    [InlineData(0, 128, "maximumReadyLineCharacters")]
    [InlineData(4096, -1, "maximumBootstrapIdentityCharacters")]
    [InlineData(4096, 0, "maximumBootstrapIdentityCharacters")]
    public void NonpositiveRuntimeCeilingIsRejected(int ready, int identity, string parameter)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LauncherBootstrapRuntime(
            TransportFixture.Descriptor, ready, identity, JobNamePrefix, static (_, _) => throw new InvalidOperationException()));
        Assert.Equal(parameter, exception.ParamName);
    }

    /// <summary>The smallest positive ceilings are accepted without widening either argument.</summary>
    [Fact]
    public void SmallestPositiveRuntimeCeilingsAreAccepted() =>
        _ = new LauncherBootstrapRuntime(TransportFixture.Descriptor, 1, 1, JobNamePrefix, static (_, _) => throw new InvalidOperationException());

    /// <summary>Runtime composition always requires a caller-owned adapter.</summary>
    [Fact]
    public void MissingCompositionAdapterIsRejected() =>
        Assert.Equal("compose", Assert.Throws<ArgumentNullException>(() => new LauncherBootstrapRuntime(TransportFixture.Descriptor, 4096, 128, JobNamePrefix, null!)).ParamName);

    private static LauncherBootstrapRuntime Runtime(Func<string, string, LauncherBootstrapRuntimeServices> compose, LauncherBootstrapRuntimeHooks? hooks = null) =>
        new(TransportFixture.Descriptor, 4096, 128, JobNamePrefix, compose, hooks ?? new());
    private static LauncherBootstrapRuntimeServices Services(StateStore app, LauncherStore? launcher = null) =>
        new(app, new StateStore(new(null, VersionManagerStateLoadIssue.Missing), new TraceLog()),
            launcher ?? new LauncherStore(new(null, LauncherBootstrapStateLoadIssue.Invalid), new TraceLog()), new UnusedRepositories(), new UnusedRepositories());
    private static ProtocolEnvironmentScope EmptyContext() => new(ContextKeys.Select(static key => (key, (string?)null)).ToArray());
    private static string DuplicateStart(BootstrapStartAuthorization start) => WindowsPipeHandles.Duplicate(start.InheritedHandle.Handle);
    private static string SerializedIdentity() => $"1|{TransportFixture.Descriptor.BootstrapExecutableFileName}|1|{new string('a', 64)}";
    private static ManagedLauncherIdentity Launcher(char digest) => ManagedLauncherIdentity.Create(TransportFixture.Descriptor, 200_000_000,
        TransportFixture.Version, Admission.AdmissionIdentity, Admission.ReleaseManifestSha256, TransportFixture.Version, 1,
        TransportFixture.Descriptor.LauncherExecutableRelativePath, 123, new string(digest, 64));
    private static VersionManagerState AppState(string root, PendingVersionActivation? activation = null, PendingManagedVersionMutation? mutation = null, bool noActive = false) =>
        VersionManagerState.Create(null, noActive ? null : TransportFixture.Version, noActive ? null : TransportFixture.Version,
            mutation is null ? [Admission] : [Admission, mutation.Admission], activation, null, false, mutation, root);
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows anonymous-pipe handle inheritance capture."); }
    }

    private sealed class TraceLog
    {
        private readonly object _sync = new();
        // Guard: _sync protects trace writes and immutable snapshots.
        private readonly List<string> _events = [];
        internal void Add(string value) { lock (_sync) { _events.Add(value); } }
        internal string[] Events { get { lock (_sync) { return [.. _events]; } } }
    }
    private sealed class ActionDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
    private sealed class StateStore(VersionManagerStateLoadResult result, TraceLog trace,
        VersionManagerWriteLeaseIssue writer = VersionManagerWriteLeaseIssue.None, bool failRead = false) : IVersionManagerStateStore
    {
        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            trace.Add("app.load");
            return failRead ? throw new IOException("Injected state read failure.") : ValueTask.FromResult(result);
        }
        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(LauncherBootstrapCoordinator.StartupWriterLeaseTimeout, waitTimeout);
            trace.Add("writer.acquire");
            return ValueTask.FromResult(new VersionManagerWriteLeaseResult(writer, writer == VersionManagerWriteLeaseIssue.None
                ? new ActionDisposable(() => trace.Add("writer.dispose")) : null));
        }
        public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken) => throw new InvalidOperationException("State must not be mutated by this scenario.");
    }
    private sealed class LauncherStore(LauncherBootstrapStateLoadResult result, TraceLog trace) : ILauncherBootstrapStateStore
    {
        public ValueTask<LauncherBootstrapStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            trace.Add("launcher.load");
            return ValueTask.FromResult(result);
        }
        public ValueTask<LauncherBootstrapStateSaveResult> TrySaveAsync(LauncherBootstrapState state, CancellationToken cancellationToken) => throw new InvalidOperationException("Launcher state must not be mutated by this scenario.");
    }
    private sealed class UnusedRepositories : IManagedVersionRepository, IInstalledLauncherRepository
    {
        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) => throw new InvalidOperationException("Package verification must not run.");
        public ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) => throw new InvalidOperationException("Installation must not run.");
        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string managedRoot, IReadOnlyList<ManagedVersionAdmission> admissions, ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion, ManagedAppVersion? failedActivationVersion, CancellationToken cancellationToken) => throw new InvalidOperationException("Inventory must not run.");
        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string managedRoot, ManagedVersionAdmission admission, ManagedAppVersion? activeVersion, CancellationToken cancellationToken) => throw new InvalidOperationException("Deletion must not run.");
        public ValueTask<InstalledLauncherResult> VerifyAsync(string managedRoot, ManagedVersionAdmission admission, CancellationToken cancellationToken) => throw new InvalidOperationException("Launcher verification must not run.");
        public ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(string managedRoot, ManagedVersionAdmission admission, CancellationToken cancellationToken) => throw new InvalidOperationException("Launcher acquisition must not run.");
    }
}
