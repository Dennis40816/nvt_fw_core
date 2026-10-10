// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Nvt.Core.Processes;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>
/// The runner's terminal-phase contract. Ordering comes from the runner's phase seam or from a blocking
/// termination/reader seam, never from a sleep. Every run is awaited under a watchdog, and every bound is measured
/// from before the triggering call (including <c>Cancel()</c> itself). Helpers live 30 s, far past each bound, so a
/// pass cannot come from a helper ending on its own.
/// </summary>
public sealed class SystemExternalProcessRunnerLifetimeTests
{
    private const int HelperLifetimeSeconds = 30;
    private static readonly string[] QuietArguments = ["--mode", "silent-wait"];
    private static readonly string[] ExitArguments = ["--mode", "exit"];
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan SchedulingMargin = TimeSpan.FromSeconds(4);
    // Cancel() must return, not block on the termination it requests. A blocking callback waits for the 60 s helper,
    // so 10 s separates the regression from a loaded CI machine without comparing a short real-time budget.
    private static readonly TimeSpan _cancelReturnBound = TimeSpan.FromSeconds(10);
    private static readonly ExternalProcessCleanupTiming Fast = new(
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(500));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>Cancel() only signals; a blocked termination cannot hold Cancel() or the run past the deadline.</summary>
    [Fact]
    public async Task CancelReturnsAtOnceAndRunEndsWithinDeadlineWhileTerminationBlocks()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var termination = new TerminationSeam();
        var phases = new PhaseRecorder();
        SystemExternalProcessRunner runner = CreateRunner(Fast, termination.Block, phases);
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task<ExternalProcessResult> run = runner.RunAsync(QuietWait(workspace.RootPath, TimeSpan.FromSeconds(60)), cancellation.Token).AsTask();
            _ = await phases.Reached(ExternalProcessRunnerPhase.Started).WaitAsync(Watchdog, TestToken);

            var clock = Stopwatch.StartNew();
            TimeSpan cancelReturned = await CancelWithinAsync(cancellation, _cancelReturnBound);
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Watchdog, TestToken));
            TimeSpan runEnded = clock.Elapsed;

            Assert.True(cancelReturned < _cancelReturnBound, $"Cancel() took {cancelReturned}.");
            Assert.True(runEnded < Fast.Deadline + SchedulingMargin, $"The run took {runEnded} after Cancel().");
            Assert.Equal(1, termination.Calls);
            Assert.False(phases.HasReached(ExternalProcessRunnerPhase.ResourcesReleased), "The custody released the process while its termination still ran.");

            termination.Open();
            _ = await phases.Reached(ExternalProcessRunnerPhase.ResourcesReleased).WaitAsync(Watchdog, TestToken);
            Assert.Equal(1, termination.Calls);
        }
        finally
        {
            termination.Open();
            termination.KillCaptured();
        }
    }

    /// <summary>a blocked termination after a timeout yields a typed unconfirmed result within the deadline.</summary>
    [Fact]
    public async Task TimeoutWithBlockedTerminationReturnsUnconfirmedWithinDeadline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var termination = new TerminationSeam();
        var phases = new PhaseRecorder();
        SystemExternalProcessRunner runner = CreateRunner(Fast, termination.Block, phases);
        try
        {
            ExternalProcessResult result = await runner.RunAsync(
                    QuietWait(workspace.RootPath, TimeSpan.FromMilliseconds(300)),
                    TestToken)
                .AsTask()
                .WaitAsync(Watchdog, TestToken);

            Assert.True(result.TimedOut);
            Assert.Equal(-1, result.ExitCode);
            Assert.Equal(ExternalProcessCleanup.TerminationUnconfirmed, result.Cleanup);
            TimeSpan cleanup = phases.Between(ExternalProcessRunnerPhase.TimeoutSignaled, ExternalProcessRunnerPhase.Returning);
            Assert.True(cleanup < Fast.Deadline + SchedulingMargin, $"Cleanup took {cleanup}.");
        }
        finally
        {
            termination.Open();
            termination.KillCaptured();
        }
    }

    /// <summary>every termination uncertainty is classified, never thrown.</summary>
    [Theory]
    [InlineData("aggregate")]
    [InlineData("win32")]
    [InlineData("invalid-operation")]
    [InlineData("not-supported")]
    public async Task RefusedTerminationIsClassifiedAsUnconfirmed(string refusal)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var termination = new TerminationSeam();
        SystemExternalProcessRunner runner = CreateRunner(Fast, termination.Refuse(refusal), new PhaseRecorder());
        try
        {
            ExternalProcessResult result = await runner.RunAsync(
                    QuietWait(workspace.RootPath, TimeSpan.FromMilliseconds(300)),
                    TestToken)
                .AsTask()
                .WaitAsync(Watchdog, TestToken);

            Assert.True(result.TimedOut);
            Assert.Equal(ExternalProcessCleanup.TerminationUnconfirmed, result.Cleanup);
        }
        finally
        {
            termination.KillCaptured();
        }
    }

    /// <summary>a refused termination on cancellation ends as cancellation, and Cancel() does not throw.</summary>
    [Fact]
    public async Task RefusedTerminationOnCancellationEndsCanceled()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var termination = new TerminationSeam();
        var phases = new PhaseRecorder();
        SystemExternalProcessRunner runner = CreateRunner(Fast, termination.Refuse("aggregate"), phases);
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task<ExternalProcessResult> run = runner.RunAsync(QuietWait(workspace.RootPath, TimeSpan.FromSeconds(60)), cancellation.Token).AsTask();
            _ = await phases.Reached(ExternalProcessRunnerPhase.Started).WaitAsync(Watchdog, TestToken);

            // CancelWithinAsync throws if Cancel() blocks or faults; a refused termination must do neither.
            _ = await CancelWithinAsync(cancellation, _cancelReturnBound);
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Watchdog, TestToken));
        }
        finally
        {
            termination.KillCaptured();
        }
    }

    /// <summary>a termination request that never produces an exit is bounded and unconfirmed.</summary>
    [Fact]
    public async Task TerminationWithoutObservedExitIsBoundedAndUnconfirmed()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var termination = new TerminationSeam();
        var phases = new PhaseRecorder();
        SystemExternalProcessRunner runner = CreateRunner(Fast, termination.Ignore, phases);
        try
        {
            ExternalProcessResult result = await runner.RunAsync(
                    QuietWait(workspace.RootPath, TimeSpan.FromMilliseconds(300)),
                    TestToken)
                .AsTask()
                .WaitAsync(Watchdog, TestToken);

            Assert.True(result.TimedOut);
            Assert.Equal(ExternalProcessCleanup.TerminationUnconfirmed, result.Cleanup);
            TimeSpan cleanup = phases.Between(ExternalProcessRunnerPhase.TimeoutSignaled, ExternalProcessRunnerPhase.Returning);
            Assert.True(cleanup < Fast.Deadline + SchedulingMargin, $"Cleanup took {cleanup}.");
        }
        finally
        {
            termination.KillCaptured();
        }
    }

    /// <summary>Cancellation race: cancellation arriving right after the timeout decision still wins before return.</summary>
    [Fact]
    public async Task CancellationRightAfterTimeoutSignalEndsCanceled()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var phases = new PhaseRecorder();
        Exception? cancelFailure = null;
        phases.When(ExternalProcessRunnerPhase.TimeoutSignaled, () => cancelFailure = Record.Exception(cancellation.Cancel));
        SystemExternalProcessRunner runner = CreateRunner(Fast, KillTree, phases);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
                QuietWait(workspace.RootPath, TimeSpan.FromMilliseconds(300)),
                cancellation.Token)
            .AsTask()
            .WaitAsync(Watchdog, TestToken));
        Assert.Null(cancelFailure);
    }

    /// <summary>Cancellation race: cancellation arriving right after a natural exit still wins before return.</summary>
    [Fact]
    public async Task CancellationRightAfterExitSignalEndsCanceled()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var phases = new PhaseRecorder();
        phases.When(ExternalProcessRunnerPhase.ExitSignaled, cancellation.Cancel);
        SystemExternalProcessRunner runner = CreateRunner(Fast, KillTree, phases);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
                QuickExit(workspace.RootPath),
                cancellation.Token)
            .AsTask()
            .WaitAsync(Watchdog, TestToken));
    }

    /// <summary>Cancellation race: cancellation after the terminal decision leaves the returned result unchanged.</summary>
    [Fact]
    public async Task CancellationAfterTerminalDecisionKeepsResult()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var phases = new PhaseRecorder();
        Exception? cancelFailure = null;
        phases.When(ExternalProcessRunnerPhase.Returning, () => cancelFailure = Record.Exception(cancellation.Cancel));
        SystemExternalProcessRunner runner = CreateRunner(Fast, KillTree, phases);

        ExternalProcessResult result = await runner.RunAsync(QuickExit(workspace.RootPath), cancellation.Token)
            .AsTask()
            .WaitAsync(Watchdog, TestToken);

        Assert.Null(cancelFailure);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExternalProcessCleanup.Complete, result.Cleanup);
    }

    /// <summary>a reader that ignores the stop is detached at the deadline; its late fault is observed afterwards.</summary>
    [Fact]
    public async Task UncooperativeReaderIsDetachedAtDeadlineAndObservedLater()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var phases = new PhaseRecorder();
        var stuck = new TaskCompletionSource<BoundedProcessOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        SystemExternalProcessRunner runner = CreateRunner(Fast, KillTree, phases, FirstReader(_ => stuck.Task));

        ExternalProcessResult result = await runner.RunAsync(QuickExit(workspace.RootPath), TestToken)
            .AsTask()
            .WaitAsync(Watchdog, TestToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExternalProcessCleanup.OutputStreamHeldOpen, result.Cleanup);
        TimeSpan cleanup = phases.Between(ExternalProcessRunnerPhase.ExitSignaled, ExternalProcessRunnerPhase.Returning);
        Assert.True(cleanup < Fast.Deadline + SchedulingMargin, $"Cleanup took {cleanup}.");
        Assert.True(phases.HasReached(ExternalProcessRunnerPhase.ReaderStopRequested));
        Assert.False(phases.HasReached(ExternalProcessRunnerPhase.ResourcesReleased), "Handles were released while a reader still ran.");

        stuck.SetException(new IOException("late reader fault"));
        _ = await phases.Reached(ExternalProcessRunnerPhase.ResourcesReleased).WaitAsync(Watchdog, TestToken);
    }

    /// <summary>a reader fault without cancellation is a typed cleanup outcome, not an exception.</summary>
    [Fact]
    public async Task ReaderFaultWithoutCancellationIsOutputReadFailed()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        SystemExternalProcessRunner runner = CreateRunner(Fast, KillTree, new PhaseRecorder(), FirstReader(_ => Task.FromException<BoundedProcessOutput>(new IOException("read failed"))));

        ExternalProcessResult result = await runner.RunAsync(QuickExit(workspace.RootPath), TestToken)
            .AsTask()
            .WaitAsync(Watchdog, TestToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExternalProcessCleanup.OutputReadFailed, result.Cleanup);
    }

    /// <summary>A production drain startup fault uses the existing output-read failure classification.</summary>
    [Fact]
    public async Task ReaderStartupFaultIsOutputReadFailed()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This runner helper requires the Windows process probe.");
        }

        using var workspace = TestWorkspace.Create();
        var capacity = new ExternalProcessCapacity(1);
        var phases = new PhaseRecorder();
        SystemExternalProcessRunner runner = CreateRunner(
            Fast, KillTree, phases,
            FirstReader(stop => ExternalProcessRunnerSeams.Production.Drain(null!, stop)), capacity);

        ExternalProcessResult result = await runner.RunAsync(QuickExit(workspace.RootPath), TestToken)
            .AsTask().WaitAsync(Watchdog, TestToken);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal(ExternalProcessCleanup.OutputReadFailed, result.Cleanup);
        Assert.False(phases.HasReached(ExternalProcessRunnerPhase.Detached));
        Assert.Equal(0, capacity.InUse);
    }

    /// <summary>Cancellation priority: caller cancellation wins over a reader fault.</summary>
    [Fact]
    public async Task ReaderFaultWithCancellationEndsCanceled()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var phases = new PhaseRecorder();
        phases.When(ExternalProcessRunnerPhase.Started, cancellation.Cancel);
        SystemExternalProcessRunner runner = CreateRunner(Fast, KillTree, phases, FirstReader(_ => Task.FromException<BoundedProcessOutput>(new IOException("read failed"))));

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
                QuietWait(workspace.RootPath, TimeSpan.FromSeconds(60)),
                cancellation.Token)
            .AsTask()
            .WaitAsync(Watchdog, TestToken));
    }

    /// <summary>a failed exit observation terminates, reports unconfirmed, and yields to cancellation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitObservationFaultIsUnconfirmedOrCanceled(bool cancel)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var termination = new TerminationSeam();
        var phases = new PhaseRecorder();
        if (cancel)
        {
            phases.When(ExternalProcessRunnerPhase.Started, cancellation.Cancel);
        }

        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Timing = Fast,
            TerminateTree = termination.KillAndCapture,
            ObserveExit = static (_, _) => Task.FromException(new InvalidOperationException("exit observation failed")),
            Observe = phases.Record,
        });
        try
        {
            Task<ExternalProcessResult> run = runner.RunAsync(
                    QuietWait(workspace.RootPath, TimeSpan.FromSeconds(60)),
                    cancellation.Token)
                .AsTask();
            if (cancel)
            {
                _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Watchdog, TestToken));
                return;
            }

            ExternalProcessResult result = await run.WaitAsync(Watchdog, TestToken);
            Assert.False(result.TimedOut);
            Assert.Equal(-1, result.ExitCode);
            Assert.Equal(ExternalProcessCleanup.TerminationUnconfirmed, result.Cleanup);
            Assert.Equal(1, termination.Calls);
        }
        finally
        {
            termination.KillCaptured();
        }
    }

    /// <summary>With real processes, a background process holding stdout after a natural exit is bounded and fails closed.</summary>
    [Fact]
    public async Task HeldOutputAfterNaturalExitIsBoundedAndReported()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("held.pid");
        var phases = new PhaseRecorder();
        SystemExternalProcessRunner runner = CreateRunner(ExternalProcessCleanupTiming.Default, KillTree, phases);

        ExternalProcessResult result = await runner.RunAsync(
                ProbeTree(workspace.RootPath, "tree-root-exit", marker, TimeSpan.FromSeconds(HelperLifetimeSeconds * 2)),
                TestToken)
            .AsTask()
            .WaitAsync(Watchdog, TestToken);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExternalProcessCleanup.OutputStreamHeldOpen, result.Cleanup);
        Assert.True(phases.HasReached(ExternalProcessRunnerPhase.OutputHeldAfterExit));
        TimeSpan cleanup = phases.Between(ExternalProcessRunnerPhase.ExitSignaled, ExternalProcessRunnerPhase.Returning);
        Assert.True(cleanup < ExternalProcessCleanupTiming.Default.Deadline + SchedulingMargin, $"Cleanup took {cleanup}.");
    }

    /// <summary>With real processes, cancellation after the direct child exited, while its output is held, ends canceled in time.</summary>
    [Fact]
    public async Task CancellationDuringHeldDrainEndsCanceledWithinDeadline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("held.pid");
        using var cancellation = new CancellationTokenSource();
        var phases = new PhaseRecorder();
        TimeSpan cancelReturned = TimeSpan.MaxValue;
        phases.When(ExternalProcessRunnerPhase.ExitSignaled, () =>
        {
            var clock = Stopwatch.StartNew();
            cancellation.Cancel();
            cancelReturned = clock.Elapsed;
        });
        SystemExternalProcessRunner runner = CreateRunner(ExternalProcessCleanupTiming.Default, KillTree, phases);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
                ProbeTree(workspace.RootPath, "tree-root-exit", marker, TimeSpan.FromSeconds(HelperLifetimeSeconds * 2)),
                cancellation.Token)
            .AsTask()
            .WaitAsync(Watchdog, TestToken));

        Assert.True(cancelReturned < _cancelReturnBound, $"Cancel() took {cancelReturned}.");
        TimeSpan cleanup = phases.Between(ExternalProcessRunnerPhase.ExitSignaled, ExternalProcessRunnerPhase.Returning);
        Assert.True(cleanup < ExternalProcessCleanupTiming.Default.Deadline + SchedulingMargin, $"Cleanup took {cleanup}.");
    }

    /// <summary>With real processes, an orphan outside the tree walk holding stdout after a timeout is bounded and reported.</summary>
    [Fact]
    public async Task OrphanHoldingOutputAfterTimeoutIsBoundedAndReported()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        string pingPidFile = workspace.GetPath("orphan.pid");
        string orphanReady = pingPidFile + ".ready";
        string innerPidFile = pingPidFile + ".middle";
        // The chain is root -> middle -> leaf. The shared probe passes both redirected streams down,
        // records the leaf and middle identities, and signals ready only after the middle has exited.
        // The pipe-holder is therefore live with an exited direct parent before timeout termination.
        var time = new ManualTimeProvider(ProcessTestClock.Start);
        var phases = new PhaseRecorder(time);
        TimeSpan timeout = TimeSpan.FromSeconds(3);
        ExternalProcessCleanupTiming timing = ExternalProcessCleanupTiming.Default;
        SystemExternalProcessRunner runner = CreateRunner(timing, KillTree, phases, time: time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        int pingPid = 0;
        // The orphan and outer outlive the run by design, so they must not keep the disposable workspace open.
        Task<ExternalProcessResult> run = runner.RunAsync(ProbeTree(Path.GetTempPath(), "orphan-chain-root", pingPidFile, timeout), cancellation.Token).AsTask();
        try
        {
            pingPid = await ReadPidAsync(pingPidFile, run);
            await WaitForFileAsync(orphanReady, run);
            int innerPid = await ReadPidAsync(innerPidFile, run);

            // Real parent-exited handshake: the pipe-holder is up, and its direct parent (inner) has actually
            // exited, and both facts are confirmed before the timeout that starts termination.
            await SpinUntilAsync(() => HasExited(innerPid), Watchdog);
            Assert.False(HasExited(pingPid), "The pipe-holding orphan exited before termination could be exercised.");
            Assert.False(phases.HasReached(ExternalProcessRunnerPhase.TimeoutSignaled), "Timeout preceded the parent-exited handshake.");

            // Startup is bounded by the watchdog, but only this signal-ordered clock advance can time out the run.
            await time.WhenPendingAsync(1, timeout, TestToken).WaitAsync(Watchdog, TestToken);
            time.Advance(timeout);
            await AdvanceToReaderStopAsync(time, timing, phases);

            ExternalProcessResult result = await run.WaitAsync(Watchdog, TestToken);

            Assert.True(result.TimedOut);
            Assert.Equal(ExternalProcessCleanup.OutputStreamHeldOpen, result.Cleanup);
            TimeSpan cleanup = phases.Between(ExternalProcessRunnerPhase.TimeoutSignaled, ExternalProcessRunnerPhase.Returning);
            Assert.True(cleanup < timing.Deadline, $"Cleanup took {cleanup} of virtual time.");
        }
        finally
        {
            KillById(pingPid);
            await FinishClockedRunAsync(run, time, timing, phases, cancellation);
        }
    }

    /// <summary>A real orphan keeps the pipes open; reader stop preserves diagnostics and releases capacity.</summary>
    [Fact]
    public async Task OrphanHoldingOutputAfterExitStopsWithoutDetachingAndKeepsText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This regression exercises synchronous Windows process pipes and CancelSynchronousIo.");
        }

        using var workspace = TestWorkspace.Create();
        string pingPidFile = workspace.GetPath("stopped-orphan.pid");
        string orphanReady = pingPidFile + ".ready";
        string innerPidFile = pingPidFile + ".middle";
        var time = new ManualTimeProvider(ProcessTestClock.Start);
        var phases = new PhaseRecorder(time);
        var capacity = new ExternalProcessCapacity(1);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = new List<Task<BoundedProcessOutput>>();
        const string expected = "before-reader-stop\r\n";
        Task<BoundedProcessOutput> DrainAsync(TextReader reader, CancellationToken stop)
        {
            Task<BoundedProcessOutput> drain = readers.Count == 0
                ? DrainWithCaptureSignalAsync(reader, expected, captured, stop)
                : ExternalProcessRunnerSeams.Production.Drain(reader, stop);
            readers.Add(drain);
            return drain;
        }

        ExternalProcessCleanupTiming timing = ExternalProcessCleanupTiming.Default;
        SystemExternalProcessRunner runner = CreateRunner(
            timing, KillTree, phases, DrainAsync, capacity, time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        int pingPid = 0;
        Task<ExternalProcessResult> run = runner.RunAsync(
            ProbeTree(Path.GetTempPath(), "orphan-chain-exit", pingPidFile, TimeSpan.FromSeconds(60), "before-reader-stop"), cancellation.Token).AsTask();
        try
        {
            pingPid = await ReadPidAsync(pingPidFile, run);
            await WaitForFileAsync(orphanReady, run);
            int innerPid = await ReadPidAsync(innerPidFile, run);
            await SpinUntilAsync(() => HasExited(innerPid), Watchdog);

            // A writer's ready file does not prove the drain has captured its output. The next read does.
            await captured.Task.WaitAsync(Watchdog, TestToken);
            await AdvanceToReaderStopAsync(time, timing, phases);
            // Keep the clock inside the stop reserve until the production drains have actually returned.
            _ = await Task.WhenAll(readers).WaitAsync(Watchdog, TestToken);
            ExternalProcessResult result = await run.WaitAsync(Watchdog, TestToken);

            Assert.False(HasExited(pingPid), "The real orphan must still hold the pipes when the run returns.");
            Assert.False(result.TimedOut);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(ExternalProcessCleanup.OutputStreamHeldOpen, result.Cleanup);
            Assert.True(phases.HasReached(ExternalProcessRunnerPhase.ReaderStopRequested));
            Assert.Contains("before-reader-stop\r\n", result.StandardOutput, StringComparison.Ordinal);
            Assert.False(phases.HasReached(ExternalProcessRunnerPhase.Detached));
            Assert.True(phases.HasReached(ExternalProcessRunnerPhase.ResourcesReleased));
            Assert.Equal(0, capacity.InUse);
        }
        finally
        {
            KillById(pingPid);
            await FinishClockedRunAsync(run, time, timing, phases, cancellation);
        }
    }

    private static async Task AdvanceToReaderStopAsync(
        ManualTimeProvider time, ExternalProcessCleanupTiming timing, PhaseRecorder phases)
    {
        // A phase hook runs before the cleanup timer is registered. Wait for the timer before moving time.
        await time.WhenPendingAsync(1, timing.Deadline, TestToken).WaitAsync(Watchdog, TestToken);
        time.Advance(timing.Deadline - timing.ReaderStopReserve);
        _ = await phases.Reached(ExternalProcessRunnerPhase.ReaderStopRequested).WaitAsync(Watchdog, TestToken);
    }

    private static async Task FinishClockedRunAsync(
        Task<ExternalProcessResult> run, ManualTimeProvider time, ExternalProcessCleanupTiming timing,
        PhaseRecorder phases, CancellationTokenSource cancellation)
    {
        if (!run.IsCompleted)
        {
            cancellation.Cancel();
            _ = await Task.WhenAny(run, phases.Reached(ExternalProcessRunnerPhase.TerminationStarted)).WaitAsync(Watchdog, TestToken);
            if (!run.IsCompleted)
            {
                _ = await Task.WhenAny(run, time.WhenPendingAsync(1, timing.Deadline, TestToken)).WaitAsync(Watchdog, TestToken);
                time.Advance(timing.Deadline);
            }
        }

        try
        {
            _ = await run.WaitAsync(Watchdog, TestToken);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Emergency teardown after a failed setup/assertion still joins the invocation's custody.
        }

        _ = await phases.Reached(ExternalProcessRunnerPhase.ResourcesReleased).WaitAsync(Watchdog, TestToken);
    }

    private static async Task<BoundedProcessOutput> DrainWithCaptureSignalAsync(
        TextReader reader, string expected, TaskCompletionSource captured, CancellationToken stop)
    {
        using var observed = new CaptureSignalReader(reader, expected, captured);
        return await ExternalProcessRunnerSeams.Production.Drain(observed, stop).ConfigureAwait(false);
    }

    /// <summary>Signals on the read after the expected text, once the production drain has retained that text.</summary>
    private sealed class CaptureSignalReader(TextReader reader, string expected, TaskCompletionSource captured) : TextReader
    {
        // Only the dedicated production reader thread accesses these fields.
        private string _tail = string.Empty;
        private bool _matched;

        /// <inheritdoc/>
        public override int Read(char[] buffer, int index, int count)
        {
            if (_matched)
            {
                _ = captured.TrySetResult();
            }

            int read = reader.Read(buffer, index, count);
            string text = string.Concat(_tail, new string(buffer, index, read));
            _matched |= text.Contains(expected, StringComparison.Ordinal);
            _tail = text.Length <= expected.Length ? text : text[^expected.Length..];
            return read;
        }
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static async Task<int> ReadPidAsync(string path, Task run)
    {
        await WaitForFileAsync(path, run);
        var clock = Stopwatch.StartNew();
        string? text = null;
        while (clock.Elapsed < Watchdog)
        {
            try
            {
                text = await File.ReadAllTextAsync(path, TestToken);
                if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int processId) && processId > 0)
                {
                    return processId;
                }
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
            {
                // The probe's marker is visible before its writer closes; retry inside the watchdog.
            }

            await Task.Delay(10, TestToken);
        }

        Assert.Fail($"The probe did not publish a valid process ID: {text ?? "<null>"}");
        return 0;
    }

    /// <summary>
    /// Accepted limitation: a descendant holding no redirected stream is invisible, so the
    /// run reports Complete while that descendant still runs. Complete is an observation, not a tree-empty proof.
    /// </summary>
    [Fact]
    public async Task DescendantWithoutRedirectedStreamIsNotObserved()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("descendant.txt");
        int descendant = 0;
        try
        {
            ExternalProcessResult result = await new SystemExternalProcessRunner()
                .RunAsync(ProbeTree(Path.GetTempPath(), "detached-descendant-root", marker, TimeSpan.FromSeconds(60)), TestToken)
                .AsTask()
                .WaitAsync(Watchdog, TestToken);
            descendant = await ReadPidAsync(marker, Task.CompletedTask);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(ExternalProcessCleanup.Complete, result.Cleanup);
            using var survivor = Process.GetProcessById(descendant);
            Assert.False(survivor.HasExited);
        }
        finally
        {
            KillById(descendant);
        }
    }

    /// <summary>the schedule keeps the reader stop inside the single deadline; the reserve is not added after it.</summary>
    [Fact]
    public void CleanupScheduleKeepsReaderStopWithinTheSingleDeadline()
    {
        var timing = new ExternalProcessCleanupTiming(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1));
        const long signaledAt = 1_000_000L;
        long deadlineTicks = ExternalProcessCleanupTiming.Ticks(TimeSpan.FromSeconds(5));
        long reserveTicks = ExternalProcessCleanupTiming.Ticks(TimeSpan.FromSeconds(1));
        long graceTicks = ExternalProcessCleanupTiming.Ticks(TimeSpan.FromSeconds(2));

        CleanupSchedule schedule = timing.Schedule(signaledAt);

        Assert.Equal(signaledAt + deadlineTicks, schedule.DeadlineAt);
        Assert.Equal(signaledAt + deadlineTicks - reserveTicks, schedule.ReaderStopAt);
        Assert.Equal(signaledAt + graceTicks, schedule.HeldOutputGraceAt);
        Assert.True(schedule.ReaderStopAt < schedule.DeadlineAt, "The reader stop must fall before the deadline.");
        // The reserve is carved out of the deadline, not appended to it.
        Assert.Equal(schedule.DeadlineAt, schedule.ReaderStopAt + reserveTicks);
        Assert.True(schedule.HeldOutputGraceAt <= schedule.ReaderStopAt, "The held-output grace must fall within the deadline.");
    }

    /// <summary>detached, unsettled invocations are capped; a new run is refused, and settling frees a slot.</summary>
    [Fact]
    public async Task DetachedCleanupIsBoundedAndRefusesNewRunsAtTheLimit()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var budget = new ExternalProcessCapacity(1);
        var termination = new TerminationSeam();
        var phases = new PhaseRecorder();
        SystemExternalProcessRunner runner = CreateRunner(Fast, termination.Block, phases, budget: budget);
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task<ExternalProcessResult> first = runner.RunAsync(QuietWait(workspace.RootPath, TimeSpan.FromSeconds(60)), cancellation.Token).AsTask();
            _ = await phases.Reached(ExternalProcessRunnerPhase.Started).WaitAsync(Watchdog, TestToken);
            _ = await CancelWithinAsync(cancellation, _cancelReturnBound);
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Watchdog, TestToken));
            _ = await phases.Reached(ExternalProcessRunnerPhase.Detached).WaitAsync(Watchdog, TestToken);
            Assert.Equal(1, budget.InUse);

            // The budget is full, so a new run is refused before any process starts.
            ExternalProcessCleanupCapacityException refused = await Assert.ThrowsAsync<ExternalProcessCleanupCapacityException>(
                () => runner.RunAsync(QuickExit(workspace.RootPath), TestToken).AsTask());
            Assert.Equal(1, refused.Limit);
            Assert.Equal(1, refused.InUseInvocations);

            // Settling the detached work frees the slot; the completion signal fires only after the release, so
            // the count is already zero when the test observes it. A new run is then accepted again.
            termination.Open();
            _ = await phases.Reached(ExternalProcessRunnerPhase.ResourcesReleased).WaitAsync(Watchdog, TestToken);
            Assert.Equal(0, budget.InUse);
            ExternalProcessResult result = await runner.RunAsync(QuickExit(workspace.RootPath), TestToken)
                .AsTask()
                .WaitAsync(Watchdog, TestToken);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(ExternalProcessCleanup.Complete, result.Cleanup);
        }
        finally
        {
            termination.Open();
            termination.KillCaptured();
        }
    }

    /// <summary>
    /// the cap holds when reservations truly race. Each run is started on its own dedicated thread; every
    /// thread waits at one barrier, and all of them call <c>RunAsync</c> (whose reservation happens before its first
    /// await) at the same moment. The exit observation is held so every started run keeps its slot during the burst.
    /// Exactly the limit start a process, the rest are refused before starting one, and after the exit gate opens
    /// every started run publishes ResourcesReleased and the capacity returns to zero. Repeated for stability.
    /// </summary>
    [Fact]
    public async Task CapacityIsAHardCapUnderConcurrentStarts()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        const int limit = 3;
        const int total = 8;
        using var workspace = TestWorkspace.Create();
        for (int iteration = 0; iteration < 10; iteration++)
        {
            var capacity = new ExternalProcessCapacity(limit);
            var releaseExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var startLine = new Barrier(total + 1);
            int started = 0;
            int released = 0;

            // A started run parks in the exit observation until released, so its slot is held for the whole burst.
            // A refused run throws before ProcessLaunchGate.Start, so it never reaches this. The gate only holds the
            // observation back: after it opens, the seam still waits for the real process exit, and a cancelled
            // observation surfaces as cancellation instead of being turned into an exit.
            async Task HeldExitAsync(Process process, CancellationToken observationToken)
            {
                _ = Interlocked.Increment(ref started);
                await releaseExit.Task.WaitAsync(observationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(observationToken).ConfigureAwait(false);
            }

            void Observe(ExternalProcessRunnerPhase phase)
            {
                if (phase == ExternalProcessRunnerPhase.ResourcesReleased)
                {
                    _ = Interlocked.Increment(ref released);
                }
            }

            ExternalProcessRunnerSeams seams = ExternalProcessRunnerSeams.Production with
            {
                Timing = Fast,
                TerminateTree = KillTree,
                ObserveExit = HeldExitAsync,
                Observe = Observe,
                Capacity = capacity,
            };

            // Dedicated threads, so a busy thread pool cannot serialize the start; each blocks at the barrier and
            // then calls RunAsync synchronously, so the reservations compete at once.
            Task<ExternalProcessResult>[] runs =
            [
                .. Enumerable.Range(0, total).Select(_ => Task.Factory.StartNew(
                        () =>
                        {
                            startLine.SignalAndWait(TestToken);
                            return new SystemExternalProcessRunner(seams)
                                .RunAsync(QuickExit(workspace.RootPath), TestToken)
                                .AsTask();
                        },
                        TestToken,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default)
                    .Unwrap()),
            ];
            try
            {
                // Release all workers together once every one is ready at the line.
                startLine.SignalAndWait(TestToken);

                await SpinUntilAsync(
                    () => Volatile.Read(ref started) == limit &&
                        runs.Count(run => run.IsFaulted) == total - limit,
                    Watchdog);

                Assert.Equal(limit, Volatile.Read(ref started));
                Assert.Equal(limit, capacity.InUse);
                int refused = runs.Count(run =>
                    run.Exception is { } aggregate &&
                    aggregate.Flatten().InnerExceptions.Any(inner => inner is ExternalProcessCleanupCapacityException));
                Assert.Equal(total - limit, refused);
            }
            finally
            {
                _ = releaseExit.TrySetResult();
            }

            // Every admitted run completes as a normal, fully observed run: exit code 0 and complete cleanup.
            var admitted = new List<ExternalProcessResult>();
            foreach (Task<ExternalProcessResult> run in runs)
            {
                try
                {
                    admitted.Add(await run.WaitAsync(Watchdog, TestToken));
                }
                catch (ExternalProcessCleanupCapacityException)
                {
                    // Refused before starting a process; counted above.
                }
            }

            Assert.Equal(limit, admitted.Count);
            Assert.All(admitted, result =>
            {
                Assert.Equal(0, result.ExitCode);
                Assert.False(result.TimedOut);
                Assert.Equal(ExternalProcessCleanup.Complete, result.Cleanup);
            });

            // Every started run publishes ResourcesReleased only after returning its slot, so once all of them have
            // published, the capacity must be back to zero.
            await SpinUntilAsync(() => Volatile.Read(ref released) == limit, Watchdog);
            Assert.Equal(0, capacity.InUse);
        }
    }

    /// <summary>
    /// the reservation itself is atomic. Many dedicated threads released by one barrier call TryReserve
    /// at the same moment; exactly the limit succeed, every refusal reports an observed count at or above the limit,
    /// and returning the successful slots brings the count back to zero. A non-atomic read-check-increment would let
    /// more than the limit through. Repeated for stability.
    /// </summary>
    [Fact]
    public async Task TryReserveIsAtomicUnderContention()
    {
        const int limit = 8;
        const int contenders = 32;
        for (int round = 0; round < 200; round++)
        {
            var capacity = new ExternalProcessCapacity(limit);
            using var startLine = new Barrier(contenders);
            int succeeded = 0;
            int refusedBelowLimit = 0;
            Task[] contendersTasks =
            [
                .. Enumerable.Range(0, contenders).Select(_ => Task.Factory.StartNew(
                    () =>
                    {
                        startLine.SignalAndWait(TestToken);
                        if (capacity.TryReserve(out int observed))
                        {
                            _ = Interlocked.Increment(ref succeeded);
                        }
                        else if (observed < limit)
                        {
                            _ = Interlocked.Increment(ref refusedBelowLimit);
                        }
                    },
                    TestToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)),
            ];
            await Task.WhenAll(contendersTasks).WaitAsync(Watchdog, TestToken);

            Assert.Equal(limit, succeeded);
            Assert.Equal(limit, capacity.InUse);
            Assert.Equal(0, refusedBelowLimit);
            for (int slot = 0; slot < limit; slot++)
            {
                capacity.Release();
            }

            Assert.Equal(0, capacity.InUse);
        }
    }
    /// <summary>
    /// Inline disposal: a disposal that throws does not skip the remaining disposals, the slot is still returned,
    /// and the failure signal is published instead of ResourcesReleased.
    /// </summary>
    [Fact]
    public async Task DisposalFailureStillReturnsSlotAndSignalsFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var capacity = new ExternalProcessCapacity(1);
        var phases = new PhaseRecorder();
        var disposal = new ThrowingDisposal();
        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Timing = Fast,
            TerminateTree = KillTree,
            Observe = phases.Record,
            Capacity = capacity,
            DisposeResource = disposal.Dispose,
        });

        ExternalProcessResult result = await runner.RunAsync(QuickExit(workspace.RootPath), TestToken)
            .AsTask()
            .WaitAsync(Watchdog, TestToken);

        Assert.Equal(0, result.ExitCode);
        _ = await phases.Reached(ExternalProcessRunnerPhase.ResourcesReleaseFailed).WaitAsync(Watchdog, TestToken);
        Assert.False(phases.HasReached(ExternalProcessRunnerPhase.ResourcesReleased), "A failed disposal published the success signal.");
        Assert.Equal(5, disposal.Attempts);
        Assert.Equal(0, capacity.InUse);

        // The returned slot is usable: with a limit of one, a further run is accepted.
        ExternalProcessResult next = await runner.RunAsync(QuickExit(workspace.RootPath), TestToken)
            .AsTask()
            .WaitAsync(Watchdog, TestToken);
        Assert.Equal(0, next.ExitCode);
    }

    /// <summary>
    /// Detached disposal: when the detached continuation finishes with a disposal failure, the slot is returned,
    /// the failure signal is published, and ResourcesReleased is not.
    /// </summary>
    [Fact]
    public async Task DetachedDisposalFailureStillReturnsSlotAndSignalsFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var capacity = new ExternalProcessCapacity(1);
        var phases = new PhaseRecorder();
        var termination = new TerminationSeam();
        var disposal = new ThrowingDisposal();
        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Timing = Fast,
            TerminateTree = termination.Block,
            Observe = phases.Record,
            Capacity = capacity,
            DisposeResource = disposal.Dispose,
        });
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task<ExternalProcessResult> run = runner.RunAsync(QuietWait(workspace.RootPath, TimeSpan.FromSeconds(60)), cancellation.Token).AsTask();
            _ = await phases.Reached(ExternalProcessRunnerPhase.Started).WaitAsync(Watchdog, TestToken);
            _ = await CancelWithinAsync(cancellation, _cancelReturnBound);
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Watchdog, TestToken));
            _ = await phases.Reached(ExternalProcessRunnerPhase.Detached).WaitAsync(Watchdog, TestToken);
            Assert.Equal(1, capacity.InUse);

            termination.Open();
            _ = await phases.Reached(ExternalProcessRunnerPhase.ResourcesReleaseFailed).WaitAsync(Watchdog, TestToken);

            Assert.False(phases.HasReached(ExternalProcessRunnerPhase.ResourcesReleased), "A failed disposal published the success signal.");
            Assert.Equal(5, disposal.Attempts);
            Assert.Equal(0, capacity.InUse);
        }
        finally
        {
            termination.Open();
            termination.KillCaptured();
        }
    }

    /// <summary>Disposes each resource for real, counts attempts, and throws after disposing the process.</summary>
    private sealed class ThrowingDisposal
    {
        // Guard: Interlocked writes and Volatile reads.
        private int _attempts;

        internal int Attempts => Volatile.Read(ref _attempts);

        internal void Dispose(IDisposable resource)
        {
            _ = Interlocked.Increment(ref _attempts);
            resource.Dispose();
            if (resource is Process)
            {
                throw new IOException("Injected disposal failure.");
            }
        }
    }
    private static async Task SpinUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < timeout, "The concurrent budget burst did not settle in time.");
            await Task.Delay(20, TestToken);
        }
    }

    private static void KillTree(Process process)
    {
        process.Kill(entireProcessTree: true);
    }

    private static SystemExternalProcessRunner CreateRunner(
        ExternalProcessCleanupTiming timing,
        Action<Process> terminateTree,
        PhaseRecorder phases,
        Func<TextReader, CancellationToken, Task<BoundedProcessOutput>>? drain = null,
        ExternalProcessCapacity? budget = null,
        TimeProvider? time = null)
    {
        return new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Timing = timing,
            TerminateTree = terminateTree,
            Drain = drain ?? ExternalProcessRunnerSeams.Production.Drain,
            Observe = phases.Record,
            // Each test gets its own budget so detached work never bleeds across tests or into production's.
            Capacity = budget ?? new ExternalProcessCapacity(int.MaxValue),
            Time = time ?? ExternalProcessRunnerSeams.Production.Time,
            ScheduleTermination = time is null
                ? ExternalProcessRunnerSeams.Production.ScheduleTermination
                : static termination => Task.FromResult(termination()),
        });
    }

    /// <summary>
    /// Runs <c>Cancel()</c> on its own task and bound-waits it. A signal-only callback returns at once; a
    /// regression to a synchronous blocking callback is caught here instead of hanging the test before its
    /// watchdog. Returns the time <c>Cancel()</c> took.
    /// </summary>
    private static async Task<TimeSpan> CancelWithinAsync(CancellationTokenSource cancellation, TimeSpan bound)
    {
        // A dedicated thread keeps a starved thread pool from delaying the call the bound is meant to measure.
        Task<TimeSpan> returned = Task.Factory.StartNew(
            () =>
            {
                long started = TimeProvider.System.GetTimestamp();
                cancellation.Cancel();
                return TimeProvider.System.GetElapsedTime(started);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            // A signal-only Cancel() never throws; a fault surfaces here.
            return await returned.WaitAsync(bound, TestToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Cancel() did not return within {bound}.");
            throw;
        }
    }

    /// <summary>Replaces only standard output; standard error uses the production drain.</summary>
    private static Func<TextReader, CancellationToken, Task<BoundedProcessOutput>> FirstReader(
        Func<CancellationToken, Task<BoundedProcessOutput>> first)
    {
        int calls = 0;
        return (reader, stopToken) => Interlocked.Increment(ref calls) == 1
            ? first(stopToken)
            : ExternalProcessRunnerSeams.Production.Drain(reader, stopToken);
    }

    private static ExternalProcessStartInfo QuietWait(string root, TimeSpan timeout)
    {
        return new ExternalProcessStartInfo(
            ProcessProbe.Executable,
            root,
            QuietArguments,
            timeout);
    }

    private static ExternalProcessStartInfo QuickExit(string root)
    {
        return new ExternalProcessStartInfo(
            ProcessProbe.Executable,
            root,
            ExitArguments,
            TimeSpan.FromSeconds(HelperLifetimeSeconds));
    }

    private static ExternalProcessStartInfo ProbeTree(
        string root, string mode, string marker, TimeSpan timeout, string? stdout = null)
    {
        string[] arguments = stdout is null
            ? ["--mode", mode, "--tree-marker", marker]
            : ["--mode", mode, "--tree-marker", marker, "--stdout-text", stdout];
        return new ExternalProcessStartInfo(ProcessProbe.Executable, root, arguments, timeout);
    }

    private static async Task WaitForFileAsync(string path, Task run)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(path) && !run.IsCompleted && clock.Elapsed < Watchdog)
        {
            // Polls an external process's readiness file; ordering of the runner itself uses the phase seam.
            await Task.Delay(100, TestToken);
        }

        Assert.True(File.Exists(path), "The deny-terminate guard did not publish readiness.");
    }

    private static void KillById(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    /// <summary>Records the first time each runner phase occurs and runs optional synchronous hooks.</summary>
    private sealed class PhaseRecorder(TimeProvider? time = null)
    {
        private readonly TimeProvider _time = time ?? TimeProvider.System;
        // Guard: ConcurrentDictionary and each TaskCompletionSource provide atomic access.
        private readonly ConcurrentDictionary<ExternalProcessRunnerPhase, TaskCompletionSource<long>> _reached = new();
        // Guard: ConcurrentDictionary; a hook is consumed atomically before phase publication.
        private readonly ConcurrentDictionary<ExternalProcessRunnerPhase, Action> _hooks = new();

        internal void When(ExternalProcessRunnerPhase phase, Action hook)
        {
            _hooks[phase] = hook;
        }

        internal void Record(ExternalProcessRunnerPhase phase)
        {
            if (_hooks.TryRemove(phase, out Action? hook))
            {
                hook();
            }

            _ = Get(phase).TrySetResult(_time.GetTimestamp());
        }

        internal Task<long> Reached(ExternalProcessRunnerPhase phase)
        {
            return Get(phase).Task;
        }

        internal bool HasReached(ExternalProcessRunnerPhase phase)
        {
            return Get(phase).Task.IsCompleted;
        }

        internal TimeSpan Between(ExternalProcessRunnerPhase from, ExternalProcessRunnerPhase to)
        {
            Assert.True(HasReached(from), $"Phase {from} was not reached.");
            Assert.True(HasReached(to), $"Phase {to} was not reached.");
            return _time.GetElapsedTime(Get(from).Task.Result, Get(to).Task.Result);
        }

        private TaskCompletionSource<long> Get(ExternalProcessRunnerPhase phase)
        {
            return _reached.GetOrAdd(
                phase,
                static _ => new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    /// <summary>Termination fault injection that always keeps a way to stop the real helper afterwards.</summary>
    private sealed class TerminationSeam
    {
        // Guard: TaskCompletionSource provides atomic publication of the one-way gate.
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Guard: ConcurrentDictionary; entries are captured by termination and read by fixture cleanup.
        private readonly ConcurrentDictionary<int, bool> _processIds = new();
        // Guard: Interlocked writes and Volatile reads.
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);

        /// <summary>
        /// A slow kill: blocks until the gate opens and then performs the real tree kill. Opening the gate never
        /// stands in for a completed termination.
        /// </summary>
        internal void Block(Process process)
        {
            Capture(process);
            _open.Task.Wait();
            process.Kill(entireProcessTree: true);
        }

        /// <summary>
        /// An ineffective termination by design: it returns without terminating anything, so the runner never
        /// observes an exit and must report the termination as unconfirmed.
        /// </summary>
        internal void Ignore(Process process)
        {
            Capture(process);
        }

        internal void KillAndCapture(Process process)
        {
            Capture(process);
            process.Kill(entireProcessTree: true);
        }

        internal Action<Process> Refuse(string refusal)
        {
            return process =>
            {
                Capture(process);
                throw refusal switch
                {
                    "aggregate" => new AggregateException("Not all processes in process tree could be terminated.", new Win32Exception(5)),
                    "win32" => new Win32Exception(5),
                    "invalid-operation" => new InvalidOperationException("No process is associated with this object."),
                    _ => new NotSupportedException("Remote process."),
                };
            };
        }

        internal void Open()
        {
            _ = _open.TrySetResult();
        }

        internal void KillCaptured()
        {
            foreach (int processId in _processIds.Keys)
            {
                KillById(processId);
            }
        }

        private void Capture(Process process)
        {
            _ = Interlocked.Increment(ref _calls);
            _ = _processIds.TryAdd(process.Id, true);
        }
    }
}
