// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Progress;
using Xunit;

namespace Nvt.Core.Tests.Progress;

/// <summary>Ports all seven frozen NFU job tests and characterizes cancellation and observer races.</summary>
public sealed class BackgroundJobServiceTests
{
    /// <summary>Work runs on the thread pool, publishes progress, and rejects a second active job.</summary>
    [Fact]
    public async Task RunsOnABackgroundThreadReportsProgressAndAllowsOnlyOneActiveJob()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var callerContext = new SynchronizationContext();
        SynchronizationContext? operationContext = callerContext;
        var started = Signal();
        var release = Signal();
        var updates = new List<BackgroundJobSnapshot<TestProgress, TestResult>>();
        var initial = new TestProgress(0, 0, "Preparing");
        var reported = new TestProgress(4, 10, "Processing");
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(callerContext);
        BackgroundJobHandle<TestProgress, TestResult> handle;
        try
        {
            handle = service.Start(async (_, progress) =>
            {
                operationContext = SynchronizationContext.Current;
                progress.Report(reported);
                started.SetResult();
                await release.Task;
                return Result("first");
            }, initial, new InlineProgress<BackgroundJobSnapshot<TestProgress, TestResult>>(updates.Add));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(service.IsActive);
        Assert.Null(operationContext);
        Assert.Equal("Processing", service.Snapshot.Progress?.StepText);
        var rejected = Assert.Throws<InvalidOperationException>(
            () => service.Start((_, _) => Task.FromResult(Result("second"))));
        Assert.Equal("A background job is already active.", rejected.Message);

        release.SetResult();
        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundJobStatus.Succeeded, completed.Status);
        Assert.Equal("first", completed.Result?.Value);
        Assert.False(service.IsActive);
        Assert.Contains(updates, item => item.Status == BackgroundJobStatus.Running && item.Progress?.Completed == 4);
        Assert.Equal(BackgroundJobStatus.Succeeded, updates[^1].Status);
        Assert.Equal(1, handle.JobId);
        Assert.Equal(
            [BackgroundJobStatus.Running, BackgroundJobStatus.Running, BackgroundJobStatus.Succeeded],
            updates.Select(item => item.Status));
        Assert.All(updates, item => Assert.Equal(handle.JobId, item.JobId));
        Assert.Same(initial, updates[0].Progress);
        Assert.Same(reported, updates[1].Progress);
        Assert.Same(reported, completed.Progress);
        Assert.Same(completed, service.Snapshot);
        Assert.Null(completed.Error);
    }

    /// <summary>Cancellation remains idempotent while a cooperative operation waits for permission to finish.</summary>
    [Fact]
    public async Task CancelIsIdempotentAndWaitsForACooperativeJobToFinish()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var started = Signal();
        var release = Signal();
        var handle = service.Start(async (cancellationToken, _) =>
        {
            started.SetResult();
            await release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return Result("unreachable");
        });
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Cancel());
        Assert.False(service.Cancel());
        Assert.True(service.IsActive);
        Assert.Equal(BackgroundJobStatus.Cancelling, service.Snapshot.Status);
        Assert.False(handle.Completion.IsCompleted);

        release.SetResult();
        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
        Assert.Null(completed.Result);
        Assert.False(service.IsActive);
        Assert.False(service.Cancel());
    }

    /// <summary>A cancellation callback reads state from another thread while Cancel is still executing.</summary>
    [Fact]
    public async Task CancellationCallbacksCanReadServiceStateWithoutRunningUnderTheServiceLock()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var started = Signal();
        var release = Signal();
        BackgroundJobSnapshot<TestProgress, TestResult>? callbackSnapshot = null;
        var handle = service.Start(async (cancellationToken, _) =>
        {
            using var registration = cancellationToken.Register(() =>
            {
                callbackSnapshot = ReadSnapshotOnAnotherThread(service);
            });
            started.SetResult();
            await release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return Result("unreachable");
        });
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Cancel());
        release.SetResult();
        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundJobStatus.Cancelling, callbackSnapshot?.Status);
        Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
    }

    /// <summary>A result returned by an operation that ignores cancellation is suppressed.</summary>
    [Fact]
    public async Task AResultReturnedAfterCancellationIsSuppressed()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var started = Signal();
        var release = Signal();
        var handle = service.Start(async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            return Result("stale");
        });
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Cancel());
        release.SetResult();
        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
        Assert.Null(completed.Result);
        Assert.Equal(BackgroundJobStatus.Cancelled, service.Snapshot.Status);
    }

    /// <summary>The old reporter cannot change the next job or notify the old observer.</summary>
    [Fact]
    public async Task LateProgressFromAnOldJobCannotReplaceTheNewJobSnapshot()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        IProgress<TestProgress>? oldProgress = null;
        var oldUpdates = new List<BackgroundJobSnapshot<TestProgress, TestResult>>();
        var first = service.Start((_, progress) =>
        {
            oldProgress = progress;
            return Task.FromResult(Result("first"));
        }, observer: new InlineProgress<BackgroundJobSnapshot<TestProgress, TestResult>>(oldUpdates.Add));
        await first.Completion.WaitAsync(TestContext.Current.CancellationToken);
        var oldSnapshot = service.Snapshot;
        oldProgress!.Report(new TestProgress(98, 100, "Late progress"));
        Assert.Same(oldSnapshot, service.Snapshot);

        var secondStarted = Signal();
        var secondRelease = Signal();
        var second = service.Start(async (cancellationToken, _) =>
        {
            secondStarted.SetResult();
            await secondRelease.Task.WaitAsync(cancellationToken);
            return Result("second");
        });
        await secondStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var newSnapshot = service.Snapshot;

        oldProgress.Report(new TestProgress(99, 100, "Stale completion"));

        Assert.Equal(second.JobId, service.Snapshot.JobId);
        Assert.NotEqual("Stale completion", service.Snapshot.Progress?.StepText);
        Assert.Same(newSnapshot, service.Snapshot);
        Assert.Equal(2, oldUpdates.Count);
        Assert.Equal(first.JobId + 1, second.JobId);
        secondRelease.SetResult();
        Assert.Equal(BackgroundJobStatus.Succeeded,
            (await second.Completion.WaitAsync(TestContext.Current.CancellationToken)).Status);
    }

    /// <summary>A synchronous operation failure is observable and permits a later job.</summary>
    [Fact]
    public async Task FailureIsObservableAndDoesNotBlockTheNextJob()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var error = new IOException("synthetic failure");
        var failure = service.Start((_, _) => throw error);

        var failed = await failure.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundJobStatus.Failed, failed.Status);
        Assert.IsType<IOException>(failed.Error);
        Assert.Same(error, failed.Error);
        Assert.Null(failed.Result);
        Assert.False(service.IsActive);

        var next = service.Start((_, _) => Task.FromResult(Result("recovered")));
        var succeeded = await next.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(next.JobId > failure.JobId);
        Assert.Equal(BackgroundJobStatus.Succeeded, succeeded.Status);
    }

    /// <summary>Observer failures at initial, progress, and terminal publication do not fail the job.</summary>
    [Fact]
    public async Task ObserverFailuresDoNotFailTheExport()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        int observations = 0;
        var observer = new InlineProgress<BackgroundJobSnapshot<TestProgress, TestResult>>(_ =>
        {
            Interlocked.Increment(ref observations);
            throw new InvalidOperationException("synthetic observer failure");
        });

        var handle = service.Start((_, progress) =>
        {
            progress.Report(new TestProgress(1, 1, "Done"));
            return Task.FromResult(Result("safe"));
        }, observer: observer);

        Assert.Equal(BackgroundJobStatus.Succeeded,
            (await handle.Completion.WaitAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(3, observations);
    }

    /// <summary>The initial state is idle, and a null operation does not consume a job identity.</summary>
    [Fact]
    public async Task IdleAndDefaultProgressPreserveNullSemantics()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var idle = service.Snapshot;
        Assert.Equal(new BackgroundJobSnapshot<TestProgress, TestResult>(0, BackgroundJobStatus.Idle), idle);
        Assert.False(service.IsActive);
        Assert.False(service.Cancel());
        var invalid = Assert.Throws<ArgumentNullException>(() => service.Start(null!));
        Assert.Equal("operation", invalid.ParamName);
        Assert.Same(idle, service.Snapshot);
        var updates = new List<BackgroundJobSnapshot<TestProgress, TestResult>>();
        var result = Result("value");

        var handle = service.Start((_, _) => Task.FromResult(result),
            observer: new InlineProgress<BackgroundJobSnapshot<TestProgress, TestResult>>(updates.Add));
        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, handle.JobId);
        Assert.Equal(
            [new BackgroundJobSnapshot<TestProgress, TestResult>(1, BackgroundJobStatus.Running),
             new BackgroundJobSnapshot<TestProgress, TestResult>(1, BackgroundJobStatus.Succeeded, Result: result)],
            updates);
        Assert.Same(result, completed.Result);
        Assert.Null(completed.Progress);
    }

    /// <summary>All enum states use NFU's active-state rule.</summary>
    [Theory]
    [InlineData(BackgroundJobStatus.Idle, false)]
    [InlineData(BackgroundJobStatus.Running, true)]
    [InlineData(BackgroundJobStatus.Cancelling, true)]
    [InlineData(BackgroundJobStatus.Succeeded, false)]
    [InlineData(BackgroundJobStatus.Cancelled, false)]
    [InlineData(BackgroundJobStatus.Failed, false)]
    public void SnapshotIsActiveOnlyWhileRunningOrCancelling(BackgroundJobStatus status, bool expected)
    {
        Assert.Equal(expected, new BackgroundJobSnapshot<TestProgress, TestResult>(1, status).IsActive);
    }

    /// <summary>Progress remains accepted during cancellation and stops after the terminal snapshot.</summary>
    [Fact]
    public async Task ProgressReportedDuringCancellingIsPublishedInOrder()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var started = Signal();
        var release = Signal();
        IProgress<TestProgress>? reporter = null;
        var updates = new List<BackgroundJobSnapshot<TestProgress, TestResult>>();
        var handle = service.Start(async (_, progress) =>
        {
            reporter = progress;
            started.SetResult();
            await release.Task;
            return Result("suppressed");
        }, observer: new InlineProgress<BackgroundJobSnapshot<TestProgress, TestResult>>(updates.Add));
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(service.Cancel());
        Assert.Throws<InvalidOperationException>(() => service.Start((_, _) => Task.FromResult(Result("blocked"))));
        var progress = new TestProgress(3, 5, "Finishing");

        reporter!.Report(progress);

        Assert.Equal(BackgroundJobStatus.Cancelling, service.Snapshot.Status);
        Assert.Same(progress, service.Snapshot.Progress);
        release.SetResult();
        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [new BackgroundJobSnapshot<TestProgress, TestResult>(1, BackgroundJobStatus.Running),
             new BackgroundJobSnapshot<TestProgress, TestResult>(1, BackgroundJobStatus.Cancelling),
             new BackgroundJobSnapshot<TestProgress, TestResult>(1, BackgroundJobStatus.Cancelling, progress),
             new BackgroundJobSnapshot<TestProgress, TestResult>(1, BackgroundJobStatus.Cancelled, progress)],
            updates);
        reporter.Report(new TestProgress(5, 5, "Late"));
        Assert.Same(completed, service.Snapshot);
        Assert.Equal(4, updates.Count);
        Assert.Null(completed.Result);
        Assert.Null(completed.Error);
    }

    /// <summary>An unrelated cancelled token fails the job unless the job's own cancellation was requested.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedOperationCancellationUsesTheJobCancellationState(bool cancelJob)
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        var error = new OperationCanceledException("synthetic cancellation", unrelated.Token);
        var started = Signal();
        var release = Signal();
        CancellationToken jobToken = default;
        var handle = service.Start(async (token, _) =>
        {
            jobToken = token;
            started.SetResult();
            await release.Task;
            throw error;
        });
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(unrelated.Token, jobToken);
        if (cancelJob) Assert.True(service.Cancel());
        release.SetResult();

        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(cancelJob ? BackgroundJobStatus.Cancelled : BackgroundJobStatus.Failed, completed.Status);
        Assert.Same(cancelJob ? null : error, completed.Error);
        Assert.Null(completed.Result);
        Assert.False(service.IsActive);
    }

    /// <summary>A throwing cancellation callback permits completion before or after Cancel exits and preserves later jobs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingCancelCallbackAndCompletionPreserveCancellationAndRecovery(bool completeDuringCallback)
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var started = Signal();
        var callbackStarted = Signal();
        var releaseCallback = Signal();
        var releaseOperation = Signal();
        var releaseNext = Signal();
        var error = new InvalidOperationException("synthetic callback failure");
        CancellationTokenRegistration registration = default;
        var handle = service.Start(async (token, _) =>
        {
            registration = token.Register(() =>
            {
                callbackStarted.SetResult();
                WaitForCallbackGate(releaseCallback.Task);
                throw error;
            });
            started.SetResult();
            await releaseOperation.Task;
            return Result("suppressed");
        });
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var cancel = Task.Run(() => Record.Exception(() => { _ = service.Cancel(); }));
        BackgroundJobHandle<TestProgress, TestResult>? next = null;
        try
        {
            await callbackStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(service.Cancel());
            if (completeDuringCallback)
            {
                releaseOperation.SetResult();
                var completedDuringCallback = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
                Assert.Equal(BackgroundJobStatus.Cancelled, completedDuringCallback.Status);
                Assert.False(cancel.IsCompleted);
                next = service.Start(async (_, _) =>
                {
                    await releaseNext.Task;
                    return Result("recovered");
                });
                Assert.Equal(next.JobId, service.Snapshot.JobId);
            }

            releaseCallback.SetResult();
            var callbackFailure = Assert.IsType<AggregateException>(
                await cancel.WaitAsync(TestContext.Current.CancellationToken));
            Assert.Same(error, Assert.Single(callbackFailure.InnerExceptions));
            if (!completeDuringCallback)
            {
                Assert.Equal(BackgroundJobStatus.Cancelling, service.Snapshot.Status);
                Assert.True(service.IsActive);
                releaseOperation.SetResult();
            }

            var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
            Assert.Null(completed.Result);
            Assert.Null(completed.Error);
            next ??= service.Start((_, _) => Task.FromResult(Result("recovered")));
            releaseNext.SetResult();
            var recovered = await next.Completion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(handle.JobId + 1, next.JobId);
            Assert.Equal(BackgroundJobStatus.Succeeded, recovered.Status);
            Assert.Same(recovered, service.Snapshot);
        }
        finally
        {
            releaseCallback.TrySetResult();
            releaseOperation.TrySetResult();
            releaseNext.TrySetResult();
            await cancel.WaitAsync(TestContext.Current.CancellationToken);
            await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
            if (next is not null) await next.Completion.WaitAsync(TestContext.Current.CancellationToken);
            registration.Dispose();
        }
    }

    /// <summary>An observer reads state from another thread, cancels reentrantly, and starts the next job on completion.</summary>
    [Fact]
    public async Task ObserverCanReadCancelAndStartTheNextJobReentrantly()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var releaseNext = Signal();
        var updates = new List<BackgroundJobSnapshot<TestProgress, TestResult>>();
        var readBack = new List<BackgroundJobSnapshot<TestProgress, TestResult>>();
        BackgroundJobHandle<TestProgress, TestResult>? next = null;
        bool? cancelAccepted = null;
        bool operationRanWithCancelledToken = false;
        var observer = new InlineProgress<BackgroundJobSnapshot<TestProgress, TestResult>>(update =>
        {
            updates.Add(update);
            readBack.Add(ReadSnapshotOnAnotherThread(service));
            if (update.Status == BackgroundJobStatus.Running)
            {
                cancelAccepted = service.Cancel();
            }
            else if (update.Status == BackgroundJobStatus.Cancelled)
            {
                next = service.Start(async (_, _) =>
                {
                    await releaseNext.Task;
                    return Result("next");
                });
            }
        });
        var first = service.Start((token, _) =>
        {
            operationRanWithCancelledToken = token.IsCancellationRequested;
            return Task.FromResult(Result("suppressed"));
        }, observer: observer);
        try
        {
            var completed = await first.Completion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(cancelAccepted);
            Assert.True(operationRanWithCancelledToken);
            Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
            Assert.Equal(
                [BackgroundJobStatus.Running, BackgroundJobStatus.Cancelling, BackgroundJobStatus.Cancelled],
                updates.Select(item => item.Status));
            Assert.Equal(updates, readBack);
            Assert.NotNull(next);
            Assert.Equal(first.JobId + 1, next.JobId);
            Assert.Equal(next.JobId, service.Snapshot.JobId);
            Assert.True(service.IsActive);
            releaseNext.SetResult();
            Assert.Equal(BackgroundJobStatus.Succeeded,
                (await next.Completion.WaitAsync(TestContext.Current.CancellationToken)).Status);
        }
        finally
        {
            releaseNext.TrySetResult();
            if (next is not null) await next.Completion.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A cancelling observer can let completion finish before the token cancellation call starts.</summary>
    [Fact]
    public async Task CompletionDuringTheCancellingObserverSuppressesResultsBeforeTokenCancellation()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var started = Signal();
        var release = Signal();
        var completedObserved = Signal();
        bool? tokenCancelledAtReturn = null;
        var observer = new InlineProgress<BackgroundJobSnapshot<TestProgress, TestResult>>(update =>
        {
            if (update.Status == BackgroundJobStatus.Cancelling)
            {
                release.SetResult();
                WaitForCallbackGate(completedObserved.Task);
            }
            else if (update.Status == BackgroundJobStatus.Cancelled)
            {
                completedObserved.SetResult();
            }
        });
        var handle = service.Start(async (token, _) =>
        {
            started.SetResult();
            await release.Task;
            tokenCancelledAtReturn = token.IsCancellationRequested;
            return Result("suppressed");
        }, observer: observer);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Cancel());
        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(tokenCancelledAtReturn);
        Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
        Assert.Null(completed.Result);
        Assert.Null(completed.Error);
        Assert.False(service.Cancel());
    }

    /// <summary>Cancellation suppresses an operation failure just as it suppresses a returned result.</summary>
    [Fact]
    public async Task FailureAfterCancellationIsSuppressed()
    {
        var service = new BackgroundJobService<TestProgress, TestResult>();
        var started = Signal();
        var release = Signal();
        var handle = service.Start(async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            throw new IOException("synthetic failure after cancellation");
        });
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(service.Cancel());
        release.SetResult();

        var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
        Assert.Null(completed.Result);
        Assert.Null(completed.Error);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TestResult Result(string value) => new(value);

    // These callbacks are synchronous. Another thread must acquire the service lock before they return.
    private static BackgroundJobSnapshot<TestProgress, TestResult> ReadSnapshotOnAnotherThread(
        BackgroundJobService<TestProgress, TestResult> service) =>
        Task.Run(() => service.Snapshot).WaitAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    // A synchronous cancellation callback cannot await its gate.
    private static void WaitForCallbackGate(Task gate) =>
        gate.WaitAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    private sealed record TestProgress(int Completed, int Total, string StepText);

    private sealed record TestResult(string Value);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}