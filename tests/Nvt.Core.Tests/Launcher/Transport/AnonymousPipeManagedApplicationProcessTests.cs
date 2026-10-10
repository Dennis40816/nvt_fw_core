// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Exercises parent-side READY, physical deadlines, final custody, and Job cleanup with the shared BCL probe.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class AnonymousPipeManagedApplicationProcessTests
{
    private const string JobNamePrefix = @"Local\CoreFixture.ManagedTree";

    /// <summary>The exact signal succeeds and the original lifetime observation completes within ten seconds.</summary>
    [Fact]
    public async Task ExactReadySignalSucceeds()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        fixture.RequireLifetimeCustodyCapability();
        ManagedProcessStartResult result = await fixture.RunAsync("ready", cancellationToken: TestContext.Current.CancellationToken);
        await WaitForApplicationLifetimeExitAsync(fixture.StatePath);
        Assert.Equal(ManagedProcessStartOutcome.Ready, result.Outcome);
    }

    /// <summary>Malformed UTF-8 is mapped to the frozen invalid-signal outcome.</summary>
    [Fact]
    public async Task InvalidUtf8ReadySignalIsRejected()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        ManagedProcessStartResult result = await fixture.RunAsync("invalid-utf8", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.InvalidReadySignal, result.Outcome);
    }

    /// <summary>Oversized input is rejected and retains the source lifetime observation.</summary>
    [Fact]
    public async Task OversizedReadySignalIsRejected()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        fixture.RequireLifetimeCustodyCapability();
        ManagedProcessStartResult result = await fixture.RunAsync("oversized", cancellationToken: TestContext.Current.CancellationToken);
        await WaitForApplicationLifetimeExitAsync(fixture.StatePath);
        Assert.Equal(ManagedProcessStartOutcome.InvalidReadySignal, result.Outcome);
    }

    /// <summary>Wrong identity and truncated EOF never authenticate a managed application.</summary>
    [Theory]
    [InlineData("ready-wrong-identity", 0, ManagedProcessStartOutcome.InvalidReadySignal)]
    [InlineData("ready-partial", 0, ManagedProcessStartOutcome.Ready)]
    [InlineData("ready-partial", 1, ManagedProcessStartOutcome.InvalidReadySignal)]
    [InlineData("exit", 0, ManagedProcessStartOutcome.ExitedBeforeReady)]
    public async Task ReadyIdentityAndPartialEofAreExact(string mode, int drop, ManagedProcessStartOutcome expected)
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        using var environment = new ProtocolEnvironmentScope(
            ("CORE_TEST_PROBE_PARTIAL_DROP", drop.ToString(CultureInfo.InvariantCulture)),
            ("CORE_TEST_PROBE_EXIT_CODE", "7"));
        ManagedProcessStartResult result = await fixture.RunAsync(mode, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
        if (mode == "exit")
        {
            Assert.Equal(7, result.ExitCode);
        }
    }

    /// <summary>The shared real child exercises the exact 128-character boundary and both adjacent values.</summary>
    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    public async Task ReadyLineBoundaryCannotAuthenticateOversizedIdentity(int characters)
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        using var environment = new ProtocolEnvironmentScope(
            ("CORE_TEST_PROBE_OVERSIZE_CHARS", characters.ToString(CultureInfo.InvariantCulture)));
        ManagedProcessStartResult result = await fixture.RunAsync("oversized", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.InvalidReadySignal, result.Outcome);
    }

    /// <summary>A failed final tree validation starts no process.</summary>
    [Fact]
    public async Task InvalidatedTreeLeaseFailsBeforeApplicationProcessStart()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        bool created = false;
        var hooks = new ManagedProcessStartHooks(AfterProcessCreation: () => created = true);
        ManagedProcessStartResult result = await fixture.RunAsync("ready", hooks, validForStart: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.StartFailed, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.False(created);
    }

    /// <summary>A synchronous post-create failure owns cleanup of the actual child.</summary>
    [Fact]
    public async Task PostCreateFailureTerminatesActualChild()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        var hooks = new ManagedProcessStartHooks(AfterProcessCreation: static () =>
            throw new InvalidOperationException("Injected post-create failure."));
        ManagedProcessStartResult result = await fixture.RunAsync("silent-wait", hooks, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.StartFailed, result.Outcome);
        Assert.NotNull(result.ExitCode);
    }

    /// <summary>The exact custom managed root and state path are arguments; handshake handles remain in the environment.</summary>
    [Fact]
    public async Task ExactCustomStatePathReachesManagedApplication()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        string arguments = fixture.PathFor("arguments.txt");
        using var environment = new ProtocolEnvironmentScope(("CORE_TEST_PROBE_ARGS_PATH", arguments));
        ManagedProcessStartResult result = await fixture.RunAsync("ready", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.Ready, result.Outcome);
        Assert.Equal(
            new[] { "--managed-root", Path.GetFullPath(fixture.Root), "--state-path", Path.GetFullPath(fixture.StatePath) },
            await File.ReadAllLinesAsync(arguments, TestContext.Current.CancellationToken));
    }

    /// <summary>Expiry triggered after actual creation cleans the Job before returning a timeout.</summary>
    [Fact]
    public async Task ReadyTimeoutFailsBoundedly()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        using var expiry = new CancellationTokenSource();
        var hooks = new ManagedProcessStartHooks(AfterProcessCreation: expiry.Cancel, DeadlineSignal: expiry.Token);
        ManagedProcessStartResult result = await fixture.RunAsync("silent-wait", hooks, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.ReadyTimeout, result.Outcome);
        Assert.NotNull(result.ExitCode);
    }

    /// <summary>Caller cancellation after actual creation propagates only through the worker that owns cleanup.</summary>
    [Fact]
    public async Task CallerCancellationPropagatesAfterCreation()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var hooks = new ManagedProcessStartHooks(AfterProcessCreation: caller.Cancel);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.RunAsync("silent-wait", hooks, cancellationToken: caller.Token));
    }

    /// <summary>Cancellation before lifetime acquisition starts no child.</summary>
    [Fact]
    public async Task CallerCancellationBeforeLifetimeAcquisitionStartsNoChild()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.Cancel();
        bool created = false;
        var hooks = new ManagedProcessStartHooks(AfterProcessCreation: () => created = true);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.RunAsync("ready", hooks, cancellationToken: caller.Token));
        Assert.False(created);
    }

    /// <summary>Expiry inside the final custody gate prevents native creation and preserves the timeout outcome.</summary>
    [Fact]
    public async Task ExpiryDuringFinalValidationPreventsCreation()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        using var expiry = new CancellationTokenSource();
        bool created = false;
        var hooks = new ManagedProcessStartHooks(
            BeforeStartValidation: _ => expiry.Cancel(),
            AfterProcessCreation: () => created = true,
            DeadlineSignal: expiry.Token);
        ManagedProcessStartResult result = await fixture.RunAsync("ready", hooks, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.ReadyTimeout, result.Outcome);
        Assert.False(created);
    }

    /// <summary>An uncertain kill remains unconfirmed even after Job cleanup can observe an empty tree.</summary>
    [Fact]
    public async Task KillFailureReturnsUnconfirmedTermination()
    {
        RequireWindows();
        using var fixture = ApplicationProbe.Create();
        using var expiry = new CancellationTokenSource();
        var hooks = new ManagedProcessStartHooks(AfterProcessCreation: expiry.Cancel, DeadlineSignal: expiry.Token);
        var termination = new ManagedProcessTermination(new FailingTerminationOperations());
        ManagedProcessStartResult result = await fixture.RunAsync("silent-wait", hooks, termination: termination, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ManagedProcessStartOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.Null(result.ExitCode);
    }

    /// <summary>A READY ceiling is always a positive explicit product parameter.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonpositiveReadyCeilingIsRejected(int maximum)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnonymousPipeManagedApplicationProcess(TransportFixture.Descriptor, maximum,
                Path.Combine(Path.GetTempPath(), "unused-state.json"), JobNamePrefix));
        Assert.Equal("maximumReadyLineCharacters", exception.ParamName);
    }

    /// <summary>Every positive READY ceiling is accepted without widening the caller's chosen value.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    public void ExplicitReadyCeilingIsAccepted(int maximum)
    {
        _ = new AnonymousPipeManagedApplicationProcess(TransportFixture.Descriptor, maximum,
            Path.Combine(Path.GetTempPath(), "unused-state.json"), JobNamePrefix);
    }

    /// <summary>The supervised start rejects zero and negative physical budgets before a lifetime can be acquired.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task NonpositiveReadyDeadlineIsRejected(long ticks)
    {
        using var lease = new ProbeLease("unused.exe", Path.GetTempPath(), true);
        var process = new AnonymousPipeManagedApplicationProcess(TransportFixture.Descriptor, 128,
            Path.Combine(Path.GetTempPath(), "unused-state.json"), JobNamePrefix);
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await process.StartUntilReadyAsync(Path.GetTempPath(), TransportFixture.Version, lease,
                TimeSpan.FromTicks(ticks), TestContext.Current.CancellationToken));
        Assert.Equal("readyDeadline", exception.ParamName);
    }

    private static async Task WaitForApplicationLifetimeExitAsync(string statePath)
    {
        var protocol = new ManagedLifetimeProtocol(TransportFixture.Names, JobNamePrefix);
        long deadline = Environment.TickCount64 + 10_000;
        ManagedProcessLifetimeStatus status;
        while ((status = ManagedProcessLifetimeLease.GetStatus(protocol, statePath,
                   ManagedProcessLifetimeKind.Application)) != ManagedProcessLifetimeStatus.Exited &&
               Environment.TickCount64 < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.Equal(ManagedProcessLifetimeStatus.Exited, status);
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test requires Windows contained creation and Job supervision.");
        }
    }

    private sealed record ProbeLease(string ExecutablePath, string WorkingDirectory, bool IsValidForStart)
        : IManagedExecutableLaunchLease
    {
        public bool TryValidateForStart() => IsValidForStart;
        public void Dispose() { }
    }

    private sealed class ApplicationProbe : IDisposable
    {
        private readonly ProbeFiles _files;
        private ApplicationProbe(TestWorkspace workspace, string executable)
        {
            _files = new(workspace, executable);
        }
        internal string Root => _files.Workspace.RootPath;
        internal string StatePath => Path.Combine(Root, "state", "custom state.json");
        internal string PathFor(string path) => _files.Workspace.GetPath(path);
        internal static ApplicationProbe Create()
        {
            TestWorkspace workspace = TestWorkspace.Create();
            try { return new(workspace, ProcessProbe.CopyAndRename(workspace, "Fixture")); }
            catch { workspace.Dispose(); throw; }
        }

        internal void RequireLifetimeCustodyCapability() =>
            WindowsCustodyCapability.RequireFile(Path.GetDirectoryName(StatePath)!);

        internal async ValueTask<ManagedProcessStartResult> RunAsync(
            string mode,
            ManagedProcessStartHooks? hooks = null,
            bool validForStart = true,
            IManagedProcessTermination? termination = null,
            CancellationToken cancellationToken = default)
        {
            using var environment = new ProtocolEnvironmentScope(("CORE_TEST_PROBE_MODE", mode));
            using var lease = new ProbeLease(_files.Executable, Path.GetDirectoryName(_files.Executable)!, validForStart);
            ManagedProcessStartHooks effective = (hooks ?? new()) with
            {
                BeforeStartValidation = info =>
                {
                    info.CreateNoWindow = true;
                    hooks?.BeforeStartValidation?.Invoke(info);
                },
            };
            var process = new AnonymousPipeManagedApplicationProcess(TransportFixture.Descriptor, 128,
                StatePath, JobNamePrefix, termination ?? ManagedProcessTermination.Instance, effective);
            return await process.StartUntilReadyAsync(Root, TransportFixture.Version, lease,
                TimeSpan.FromSeconds(5), cancellationToken == default
                    ? TestContext.Current.CancellationToken : cancellationToken);
        }

        public void Dispose() => _files.Workspace.Dispose();

        private sealed record ProbeFiles(TestWorkspace Workspace, string Executable);
    }

    private sealed class FailingTerminationOperations : IManagedProcessTerminationOperations
    {
        public bool HasExited(Process process) => false;
        public void Kill(Process process)
        {
            process.Kill(entireProcessTree: true);
            throw new Win32Exception("Injected kill failure.");
        }
        public bool WaitForExit(Process process, TimeSpan timeout) =>
            throw new InvalidOperationException("A failed kill cannot be followed by a wait.");
        public int GetExitCode(Process process) => process.ExitCode;
    }
}
