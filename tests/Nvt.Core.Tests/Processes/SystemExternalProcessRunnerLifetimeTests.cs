// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
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
[Collection(ProcessSerialCollection.Name)]
public sealed partial class SystemExternalProcessRunnerLifetimeTests
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
