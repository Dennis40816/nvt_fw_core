// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Progress;
using Xunit;

namespace Nvt.Core.Tests.Progress;

/// <summary>Pins cancellation status, repeated requests, and cancellation source disposal.</summary>
public sealed class BackgroundJobServiceCancellationTests
{
    /// <summary>Two cancel calls invoke one callback and dispose the source only after completion.</summary>
    [Fact]
    public async Task RepeatedCancelKeepsStatusTokenAndDisposalConsistent()
    {
        var service = new BackgroundJobService<string, string>();
        var started = Signal();
        var release = Signal();
        CancellationToken token = default;
        int callbacks = 0;
        BackgroundJobStatus? callbackStatus = null;
        var updates = new List<BackgroundJobStatus>();
        var handle = service.Start(async (jobToken, _) =>
        {
            token = jobToken;
            using var registration = jobToken.Register(() =>
            {
                Interlocked.Increment(ref callbacks);
                callbackStatus = service.Snapshot.Status;
            });
            started.SetResult();
            await release.Task;
            return "suppressed";
        }, observer: new InlineProgress<BackgroundJobSnapshot<string, string>>(update => updates.Add(update.Status)));
        try
        {
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(service.Cancel());
            Assert.False(service.Cancel());
            Assert.Equal(1, callbacks);
            Assert.Equal(BackgroundJobStatus.Cancelling, callbackStatus);
            Assert.Equal(BackgroundJobStatus.Cancelling, service.Snapshot.Status);
            Assert.True(token.IsCancellationRequested);
            Assert.True(token.WaitHandle.WaitOne(0));
            Assert.False(handle.Completion.IsCompleted);

            release.SetResult();
            var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
            Assert.Null(completed.Result);
            Assert.Same(completed, service.Snapshot);
            Assert.Throws<ObjectDisposedException>(() => token.WaitHandle);
            Assert.False(service.Cancel());
            Assert.Equal(
                [BackgroundJobStatus.Running, BackgroundJobStatus.Cancelling, BackgroundJobStatus.Cancelled], updates);
        }
        finally
        {
            release.TrySetResult();
            await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Completion before token cancellation defers disposal even when an observer starts the next job.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionWhileCancellingDefersDisposalUntilCancelReturns(bool startNextJob)
    {
        var service = new BackgroundJobService<string, string>();
        var started = Signal();
        var release = Signal();
        var releaseNext = Signal();
        CancellationToken token = default;
        BackgroundJobHandle<string, string>? next = null;
        BackgroundJobHandle<string, string>? handle = null;
        int callbacks = 0;
        bool? secondCancel = null;
        bool? tokenCancelledBeforeCompletion = null;
        bool? sourceAliveAfterCompletion = null;
        Exception? observerFailure = null;
        CancellationTokenRegistration registration = default;
        var observer = new InlineProgress<BackgroundJobSnapshot<string, string>>(update =>
        {
            if (update.Status != BackgroundJobStatus.Cancelling) return;
            // SafeReport swallows observer exceptions, so record them for assertions outside the callback.
            observerFailure = Record.Exception(() =>
            {
                secondCancel = service.Cancel();
                tokenCancelledBeforeCompletion = token.IsCancellationRequested;
                release.SetResult();
                handle!.Completion.WaitAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
                sourceAliveAfterCompletion = !token.WaitHandle.WaitOne(0);
                if (startNextJob)
                {
                    next = service.Start(async (_, _) =>
                    {
                        await releaseNext.Task;
                        return "next";
                    });
                }
            });
        });
        handle = service.Start(async (jobToken, _) =>
        {
            token = jobToken;
            registration = jobToken.Register(() => Interlocked.Increment(ref callbacks));
            started.SetResult();
            await release.Task;
            return "suppressed";
        }, observer: observer);
        try
        {
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(service.Cancel());
            Assert.Null(observerFailure);
            Assert.False(secondCancel);
            Assert.False(tokenCancelledBeforeCompletion);
            Assert.True(sourceAliveAfterCompletion);
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(1, callbacks);
            Assert.Throws<ObjectDisposedException>(() => token.WaitHandle);
            var completed = await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundJobStatus.Cancelled, completed.Status);
            Assert.Null(completed.Result);
            if (startNextJob)
            {
                Assert.NotNull(next);
                Assert.Equal(next.JobId, service.Snapshot.JobId);
                Assert.Equal(BackgroundJobStatus.Running, service.Snapshot.Status);
                releaseNext.SetResult();
                Assert.Equal(BackgroundJobStatus.Succeeded,
                    (await next.Completion.WaitAsync(TestContext.Current.CancellationToken)).Status);
            }
            else
            {
                Assert.Same(completed, service.Snapshot);
                Assert.False(service.Cancel());
            }
        }
        finally
        {
            release.TrySetResult();
            releaseNext.TrySetResult();
            await handle.Completion.WaitAsync(TestContext.Current.CancellationToken);
            if (next is not null) await next.Completion.WaitAsync(TestContext.Current.CancellationToken);
            registration.Dispose();
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
