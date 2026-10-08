// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Exercises the shared terminal-result boundary used by both managed process adapters.</summary>
public sealed class ManagedStartDeadlineTests
{
    /// <summary>A timed-out pre-creation start releases its lease before fallback starts.</summary>
    [Fact]
    public async Task PreCreationTimeoutReleasesLeaseBeforeImmediateRollback()
    {
        using var expiry = new CancellationTokenSource();
        using var lease = new SemaphoreSlim(1, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new ManagedStartDeadline(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken, expiry.Token);
        Task<ManagedProcessStartResult> start = deadline.RunAsync<ManagedProcessStartResult>(async () =>
        {
            Assert.True(await lease.WaitAsync(0, TestContext.Current.CancellationToken));
            try
            {
                entered.SetResult();
                await release.Task;
                deadline.Token.ThrowIfCancellationRequested();
                return new(ManagedProcessStartOutcome.Ready, null);
            }
            catch (OperationCanceledException)
            {
                return new(ManagedProcessStartOutcome.ReadyTimeout, null);
            }
            finally
            {
                _ = lease.Release();
            }
        },
        static () => new(ManagedProcessStartOutcome.TerminationUnconfirmed, null),
        TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            expiry.Cancel();
            // The fallback must never run while the first worker still owns the lease.
            Assert.False(start.IsCompleted);
            release.SetResult();
            ManagedProcessStartResult candidate = await start.WaitAsync(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Equal(ManagedProcessStartOutcome.ReadyTimeout, candidate.Outcome);
            ManagedProcessStartOutcome rollback = await lease.WaitAsync(0, TestContext.Current.CancellationToken)
                ? ManagedProcessStartOutcome.Ready
                : ManagedProcessStartOutcome.TerminationUnconfirmed;
            Assert.Equal(ManagedProcessStartOutcome.Ready, rollback);
            if (rollback == ManagedProcessStartOutcome.Ready)
            {
                _ = lease.Release();
            }
        }
        finally
        {
            _ = release.TrySetResult();
        }
    }

    /// <summary>Cancellation cannot hide an accepted child from its caller.</summary>
    [Fact]
    public async Task CallerCancellationDoesNotDiscardAlreadyAcceptedReady()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new ManagedStartDeadline(TimeSpan.FromSeconds(5), caller.Token);
        Task<ManagedProcessStartResult> start = deadline.RunAsync<ManagedProcessStartResult>(async () =>
        {
            Assert.True(deadline.TryBeginCreation());
            entered.SetResult();
            await release.Task;
            return new(ManagedProcessStartOutcome.Ready, null);
        },
        static () => new(ManagedProcessStartOutcome.TerminationUnconfirmed, null),
        caller.Token);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            caller.Cancel();
            release.SetResult();

            ManagedProcessStartResult result = await start.WaitAsync(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Equal(ManagedProcessStartOutcome.Ready, result.Outcome);
        }
        finally
        {
            _ = release.TrySetResult();
            _ = await start.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A cancelled worker still propagates cancellation after it owns cleanup.</summary>
    [Fact]
    public async Task CallerCancellationPreservesWorkerCancellationAfterCreation()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new ManagedStartDeadline(TimeSpan.FromSeconds(5), caller.Token);
        Task<ManagedProcessStartResult> start = deadline.RunAsync<ManagedProcessStartResult>(async () =>
        {
            Assert.True(deadline.TryBeginCreation());
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, caller.Token);
            return new(ManagedProcessStartOutcome.Ready, null);
        },
        static () => new(ManagedProcessStartOutcome.TerminationUnconfirmed, null),
        caller.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        caller.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await start.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    /// <summary>An unfinished native creation yields an existing fail-closed outcome.</summary>
    [Fact]
    public async Task CallerCancellationCannotWaitForeverForUnfinishedCreation()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new ManagedStartDeadline(TimeSpan.FromSeconds(10), caller.Token);
        Task<ManagedProcessStartResult> start = deadline.RunAsync<ManagedProcessStartResult>(async () =>
        {
            try
            {
                Assert.True(deadline.TryBeginCreation());
                entered.SetResult();
                await release.Task;
                return new(ManagedProcessStartOutcome.ReadyTimeout, null);
            }
            finally
            {
                workerExited.SetResult();
            }
        },
        static () => new(ManagedProcessStartOutcome.TerminationUnconfirmed, null),
        caller.Token);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            caller.Cancel();

            ManagedProcessStartResult result = await start.WaitAsync(
                (2 * ManagedProcessTermination.DefaultWaitTimeout) + TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            Assert.Equal(ManagedProcessStartOutcome.TerminationUnconfirmed, result.Outcome);
        }
        finally
        {
            _ = release.TrySetResult();
            await workerExited.Task.WaitAsync(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
    }
}
