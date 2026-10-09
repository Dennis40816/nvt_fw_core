// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Processes;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>
/// Exercises admission boundaries while real exited processes retain unsettled reader custody. The child exits are real
/// events. Every cleanup wait uses a manual clock and the termination work item runs inline, so the result does not
/// depend on how fast the machine or the thread pool is.
/// </summary>
public sealed class SystemExternalProcessRunnerLifetimeBoundaryTests
{
    private static readonly string[] ExitArguments = ["--mode", "exit"];
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(40);
    private static readonly ExternalProcessCleanupTiming Fast = new(
        TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(500));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>Below the limit another run is admitted; at the limit the next is refused until late readers settle.</summary>
    [Theory]
    [InlineData(1, "complete")]
    [InlineData(2, "complete")]
    [InlineData(3, "complete")]
    [InlineData(8, "complete")]
    [InlineData(1, "fault")]
    [InlineData(2, "fault")]
    [InlineData(3, "fault")]
    [InlineData(8, "fault")]
    [InlineData(1, "cancel")]
    [InlineData(2, "cancel")]
    [InlineData(3, "cancel")]
    [InlineData(8, "cancel")]
    public async Task DetachedReadersRetainExactCapacityUntilLateSettlement(int limit, string settlement)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        var time = new ManualTimeProvider(ProcessTestClock.Start);
        var capacity = new ExternalProcessCapacity(limit);
        var invocations = new List<RetainedReaders>();
        var request = new ExternalProcessStartInfo(
            ProcessProbe.Executable, workspace.RootPath, ExitArguments, TimeSpan.FromSeconds(30));
        try
        {
            for (int count = 0; count < limit; count++)
            {
                Assert.Equal(count, capacity.InUse);
                var readers = new TaskCompletionSource<BoundedProcessOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
                var detached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
                {
                    Timing = Fast,
                    Capacity = capacity,
                    Time = time,
                    // Inline, so the termination result is settled when the cleanup deadline is read.
                    ScheduleTermination = static termination => Task.FromResult(termination()),
                    // The real root exit is observed by the production seam; no live descendant needs termination.
                    TerminateTree = static _ => { },
                    Drain = (_, _) => readers.Task,
                    Observe = phase =>
                    {
                        if (phase == ExternalProcessRunnerPhase.Detached)
                        {
                            _ = detached.TrySetResult();
                        }
                        else if (phase == ExternalProcessRunnerPhase.ResourcesReleased)
                        {
                            _ = released.TrySetResult();
                        }
                    },
                });
                Task<ExternalProcessResult> run = runner.RunAsync(request, TestToken).AsTask();
                invocations.Add(new RetainedReaders(run, readers, detached.Task, released.Task));
                Assert.Equal(count + 1, capacity.InUse);
            }

            // Every run saw its child exit and now waits on a held-output timer of the manual clock. Move the clock past
            // the whole cleanup deadline in one step; no real time decides the outcome.
            await time.WhenPendingAsync(limit, Fast.Deadline, TestToken).WaitAsync(Watchdog, TestToken);
            time.Advance(Fast.Deadline);

            foreach (RetainedReaders invocation in invocations)
            {
                ExternalProcessResult result = await invocation.Run.WaitAsync(Watchdog, TestToken);
                await invocation.Detached.WaitAsync(Watchdog, TestToken);
                Assert.Equal(0, result.ExitCode);
                Assert.False(result.TimedOut);
                Assert.Equal(ExternalProcessCleanup.OutputStreamHeldOpen, result.Cleanup);
                Assert.False(invocation.Released.IsCompleted);
            }
            Assert.Equal(limit, capacity.InUse);

            var next = new SystemExternalProcessRunner(
                ExternalProcessRunnerSeams.Production with { Capacity = capacity, Time = time });
            ExternalProcessCleanupCapacityException refused = await Assert.ThrowsAsync<ExternalProcessCleanupCapacityException>(
                () => next.RunAsync(request, TestToken).AsTask().WaitAsync(Watchdog, TestToken));
            Assert.Equal(limit, refused.Limit);
            Assert.Equal(limit, refused.InUseInvocations);
            Assert.Equal(limit, capacity.InUse);

            RetainedReaders first = invocations[0];
            Settle(first.Readers, settlement);
            await first.Released.WaitAsync(Watchdog, TestToken);
            Assert.Equal(limit - 1, capacity.InUse);

            ExternalProcessResult accepted = await next.RunAsync(request, TestToken).AsTask().WaitAsync(Watchdog, TestToken);
            Assert.Equal(0, accepted.ExitCode);
            Assert.Equal(ExternalProcessCleanup.Complete, accepted.Cleanup);
            Assert.Equal(limit - 1, capacity.InUse);

            foreach (RetainedReaders invocation in invocations.Skip(1))
            {
                Settle(invocation.Readers, settlement);
                await invocation.Released.WaitAsync(Watchdog, TestToken);
            }
            Assert.Equal(0, capacity.InUse);
        }
        finally
        {
            foreach (RetainedReaders invocation in invocations)
            {
                _ = invocation.Readers.TrySetResult(new BoundedProcessOutput(string.Empty, true));
            }
            foreach (RetainedReaders invocation in invocations)
            {
                _ = await invocation.Run.WaitAsync(Watchdog, TestToken);
                await invocation.Released.WaitAsync(Watchdog, TestToken);
            }
        }
    }

    private static void Settle(TaskCompletionSource<BoundedProcessOutput> readers, string settlement)
    {
        if (settlement == "fault")
        {
            readers.SetException(new IOException("late reader fault"));
        }
        else if (settlement == "cancel")
        {
            readers.SetCanceled(TestToken);
        }
        else
        {
            readers.SetResult(new BoundedProcessOutput(string.Empty, true));
        }
    }

    private sealed record RetainedReaders(
        Task<ExternalProcessResult> Run,
        TaskCompletionSource<BoundedProcessOutput> Readers,
        Task Detached,
        Task Released);
}
