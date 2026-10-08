// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Characterizes native timer bounds and deterministic cancellation around the creation boundary.</summary>
public sealed class ManagedStartDeadlineBoundaryTests
{
    /// <summary>The linked timer preserves the BCL inclusive maximum and its reserved infinite sentinel.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4294967293)]
    [InlineData(4294967294)]
    public async Task TimerAcceptsExactBoundary(long milliseconds)
    {
        var deadline = new ManagedStartDeadline(TimeSpan.FromMilliseconds(milliseconds),
            TestContext.Current.CancellationToken);
        Assert.Equal(7, await deadline.RunAsync(
            static () => Task.FromResult(7), static () => -1, TestContext.Current.CancellationToken));
    }

    /// <summary>Values below the infinite sentinel or above the timer maximum are rejected.</summary>
    [Theory]
    [InlineData(-2)]
    [InlineData(4294967295)]
    public void TimerRejectsOutsideBoundary(long milliseconds)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ManagedStartDeadline(TimeSpan.FromMilliseconds(milliseconds),
                TestContext.Current.CancellationToken));
    }

    /// <summary>Cancellation before creation propagates while an unfinished worker retains its cleanup ownership.</summary>
    [Fact]
    public async Task CallerCancellationBeforeCreationDoesNotWaitForUnfinishedWorker()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new ManagedStartDeadline(TimeSpan.FromSeconds(10), caller.Token);
        Task<int> result = deadline.RunAsync(async () =>
        {
            try { entered.SetResult(); await release.Task; return 7; }
            finally { exited.SetResult(); }
        }, static () => -1, caller.Token);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            caller.Cancel();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await result.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.False(exited.Task.IsCompleted);
        }
        finally
        {
            _ = release.TrySetResult();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>An expired final gate cannot claim creation.</summary>
    [Fact]
    public async Task CancelledDeadlineCannotBeginCreation()
    {
        using var expiry = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new ManagedStartDeadline(TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken, expiry.Token);
        Task<bool> result = deadline.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return deadline.TryBeginCreation();
        }, static () => true, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            expiry.Cancel();
            release.SetResult();
            Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }
        finally { _ = release.TrySetResult(); }
    }
}
