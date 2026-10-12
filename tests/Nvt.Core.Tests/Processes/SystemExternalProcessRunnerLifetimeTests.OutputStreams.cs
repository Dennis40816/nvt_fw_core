// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using Nvt.Core.Processes;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Processes;

public sealed partial class SystemExternalProcessRunnerLifetimeTests
{
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
}
