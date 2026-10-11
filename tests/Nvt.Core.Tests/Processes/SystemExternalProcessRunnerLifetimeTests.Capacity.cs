// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.Processes;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Processes;

public sealed partial class SystemExternalProcessRunnerLifetimeTests
{
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
}
