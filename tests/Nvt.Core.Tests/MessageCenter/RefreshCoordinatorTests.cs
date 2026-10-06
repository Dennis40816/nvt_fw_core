// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.MessageCenter;
using Xunit;

namespace Nvt.Core.Tests.MessageCenter;

/// <summary>Characterizes asynchronous admission, token ownership, retry, and task-owned cleanup.</summary>
public sealed class RefreshCoordinatorTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    /// <summary>A missing refresh delegate is rejected at construction.</summary>
    [Fact]
    public void ConstructorRejectsNullRefresh()
    {
        var error = Assert.Throws<ArgumentNullException>(
            () => new MessageCenterRefreshCoordinator(null!));

        Assert.Equal("refresh", error.ParamName);
    }

    /// <summary>Both initial strengths forward their token and allow another request after completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialRequestCompletesAndNextRequestRuns(bool reloadSources)
    {
        using var context = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var nextToken = new CancellationTokenSource();
        var firstGate = Gate();
        var nextGate = Gate();
        var script = new RefreshScript(firstGate.Task, nextGate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);

        Task first = context.Invoke(() => coordinator.RefreshAsync(reloadSources, ownerToken.Token));
        Assert.False(first.IsCompleted);
        firstGate.SetResult();
        await context.CompleteAsync(first);
        await LogOutcomeAsync(script.Log, "first", first);

        Task next = context.Invoke(() => coordinator.RefreshAsync(!reloadSources, nextToken.Token));
        Assert.False(next.IsCompleted);
        nextGate.SetResult();
        await context.CompleteAsync(next);
        await LogOutcomeAsync(script.Log, "next", next);

        Assert.Equal(new Trace[]
        {
            new("refresh", reloadSources, Token: ownerToken.Token), new("first:succeeded"),
            new("refresh", !reloadSources, Token: nextToken.Token), new("next:succeeded"),
        }, script.Log);
    }

    /// <summary>Synchronous completion and an empty wait-then-request path leave the next request runnable.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SynchronouslyCompletedDelegateDoesNotBlockNextRequest(bool afterCurrent, bool reloadSources)
    {
        using var firstToken = new CancellationTokenSource();
        using var nextToken = new CancellationTokenSource();
        var script = new RefreshScript(Task.CompletedTask, Task.CompletedTask);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);

        Task first = Request(coordinator, afterCurrent, reloadSources, firstToken.Token);
        Assert.True(first.IsCompletedSuccessfully);
        await LogOutcomeAsync(script.Log, "first", first);
        Task next = coordinator.RefreshAsync(!reloadSources, nextToken.Token);
        Assert.True(next.IsCompletedSuccessfully);
        await LogOutcomeAsync(script.Log, "next", next);

        Assert.Equal(new Trace[]
        {
            new("refresh", reloadSources, Token: firstToken.Token), new("first:succeeded"),
            new("refresh", !reloadSources, Token: nextToken.Token), new("next:succeeded"),
        }, script.Log);
    }

    /// <summary>Ports the active full-refresh/startup join assertion and covers all compatible strengths.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompatibleRequestJoinsActiveWork(bool activeReload, bool joiningReload)
    {
        using var context = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var waiterToken = new CancellationTokenSource();
        var gate = Gate();
        var script = new RefreshScript(gate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);

        Task owner = context.Invoke(() => coordinator.RefreshAsync(activeReload, ownerToken.Token));
        Task waiter = coordinator.RefreshAsync(joiningReload, waiterToken.Token);
        Assert.False(owner.IsCompleted);
        Assert.False(waiter.IsCompleted);
        Assert.Single(script.Log);

        gate.SetResult();
        await context.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        await LogOutcomeAsync(script.Log, "waiter", waiter);

        Assert.Equal(new Trace[]
        {
            new("refresh", activeReload, Token: ownerToken.Token),
            new("owner:succeeded"), new("waiter:succeeded"),
        }, script.Log);
    }

    /// <summary>Two stronger waiters join one reload, whichever is admitted first; old cleanup preserves it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrongerWaitersShareFreshReloadAndOlderCleanupCannotEraseIt(bool reverseWaiters)
    {
        using var ownerContext = new QueuedContext();
        using var firstContext = new QueuedContext();
        using var secondContext = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var firstToken = new CancellationTokenSource();
        using var secondToken = new CancellationTokenSource();
        using var lateToken = new CancellationTokenSource();
        var observationGate = Gate();
        var reloadGate = Gate();
        var script = new RefreshScript(observationGate.Task, reloadGate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = ownerContext.Invoke(() => coordinator.RefreshAsync(false, ownerToken.Token));
        Task first = firstContext.Invoke(() => coordinator.RefreshAsync(true, firstToken.Token));
        Task second = secondContext.Invoke(() => coordinator.RefreshAsync(true, secondToken.Token));
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Single(script.Log);

        observationGate.SetResult();
        QueuedContext winnerContext = reverseWaiters ? secondContext : firstContext;
        QueuedContext joinerContext = reverseWaiters ? firstContext : secondContext;
        CancellationToken winnerToken = reverseWaiters ? secondToken.Token : firstToken.Token;
        await winnerContext.RunNextAsync();
        await joinerContext.RunNextAsync();
        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: ownerToken.Token), new("refresh", true, Token: winnerToken),
        }, script.Log);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await ownerContext.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        Task late = coordinator.RefreshAsync(false, lateToken.Token);
        Assert.False(late.IsCompleted);

        reloadGate.SetResult();
        await firstContext.CompleteAsync(first);
        await secondContext.CompleteAsync(second);
        await LogOutcomeAsync(script.Log, "first", first);
        await LogOutcomeAsync(script.Log, "second", second);
        await LogOutcomeAsync(script.Log, "late", late);

        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: ownerToken.Token), new("refresh", true, Token: winnerToken),
            new("owner:succeeded"), new("first:succeeded"),
            new("second:succeeded"), new("late:succeeded"),
        }, script.Log);
    }

    /// <summary>A compatible wait uses its own token without canceling or replacing the admitted owner.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledCompatibleWaitLeavesOwnerActive(bool activeReload)
    {
        using var context = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var waiterToken = new CancellationTokenSource();
        using var lateToken = new CancellationTokenSource();
        var gate = Gate();
        var script = new RefreshScript(gate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = context.Invoke(() => coordinator.RefreshAsync(activeReload, ownerToken.Token));
        Task waiter = coordinator.RefreshAsync(false, waiterToken.Token);

        waiterToken.Cancel();
        await LogOutcomeAsync(script.Log, "waiter", waiter);
        Assert.False(ownerToken.IsCancellationRequested);
        Assert.False(owner.IsCompleted);
        Assert.False(gate.Task.IsCompleted);
        Task late = coordinator.RefreshAsync(false, lateToken.Token);
        Assert.False(late.IsCompleted);
        gate.SetResult();
        await context.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        await LogOutcomeAsync(script.Log, "late", late);

        Assert.Equal(new Trace[]
        {
            new("refresh", activeReload, Token: ownerToken.Token),
            new("waiter:canceled", Token: waiterToken.Token),
            new("owner:succeeded"), new("late:succeeded"),
        }, script.Log);
    }

    /// <summary>After-current always requests work after the observed task, including a fresh observation after reload.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AfterCurrentWaitsThenRequestsChosenStrength(bool activeReload, bool requestedReload)
    {
        using var ownerContext = new QueuedContext();
        using var afterContext = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var afterToken = new CancellationTokenSource();
        var currentGate = Gate();
        var afterGate = Gate();
        var script = new RefreshScript(currentGate.Task, afterGate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = ownerContext.Invoke(() => coordinator.RefreshAsync(activeReload, ownerToken.Token));
        Task after = afterContext.Invoke(
            () => coordinator.RefreshAfterCurrentAsync(requestedReload, afterToken.Token));
        Assert.False(after.IsCompleted);
        Assert.Single(script.Log);

        currentGate.SetResult();
        await afterContext.RunNextAsync();
        Assert.Equal(new Trace[]
        {
            new("refresh", activeReload, Token: ownerToken.Token),
            new("refresh", requestedReload, Token: afterToken.Token),
        }, script.Log);
        Assert.False(after.IsCompleted);
        await ownerContext.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        afterGate.SetResult();
        await afterContext.CompleteAsync(after);
        await LogOutcomeAsync(script.Log, "after", after);

        Assert.Equal(new Trace[]
        {
            new("refresh", activeReload, Token: ownerToken.Token),
            new("refresh", requestedReload, Token: afterToken.Token),
            new("owner:succeeded"), new("after:succeeded"),
        }, script.Log);
    }

    /// <summary>The following request re-enters normal admission and can join a newer compatible reload.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterCurrentCanJoinCompatibleWorkAdmittedDuringItsWait(bool requestedReload)
    {
        using var ownerContext = new QueuedContext();
        using var newerContext = new QueuedContext();
        using var afterContext = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var newerToken = new CancellationTokenSource();
        using var afterToken = new CancellationTokenSource();
        var currentGate = Gate();
        var newerGate = Gate();
        var script = new RefreshScript(currentGate.Task, newerGate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = ownerContext.Invoke(() => coordinator.RefreshAsync(false, ownerToken.Token));
        Task after = afterContext.Invoke(
            () => coordinator.RefreshAfterCurrentAsync(requestedReload, afterToken.Token));

        currentGate.SetResult();
        Task newer = newerContext.Invoke(() => coordinator.RefreshAsync(true, newerToken.Token));
        await afterContext.RunNextAsync();
        Assert.False(after.IsCompleted);
        await ownerContext.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        newerGate.SetResult();
        await newerContext.CompleteAsync(newer);
        await afterContext.CompleteAsync(after);
        await LogOutcomeAsync(script.Log, "newer", newer);
        await LogOutcomeAsync(script.Log, "after", after);

        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: ownerToken.Token), new("refresh", true, Token: newerToken.Token),
            new("owner:succeeded"), new("newer:succeeded"), new("after:succeeded"),
        }, script.Log);
    }

    /// <summary>Both wait-then-request paths ignore unrelated active failure or cancellation and retain the new token.</summary>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task UnrelatedActiveFailureAllowsFreshAttempt(
        bool afterCurrent, bool requestedReload, bool cancelActive)
    {
        using var ownerContext = new QueuedContext();
        using var nextContext = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var nextToken = new CancellationTokenSource();
        var currentGate = Gate();
        var nextGate = Gate();
        var failure = new InvalidOperationException("Synthetic active failure.");
        var script = new RefreshScript(currentGate.Task, nextGate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        bool activeReload = afterCurrent;
        Task owner = ownerContext.Invoke(() => coordinator.RefreshAsync(activeReload, ownerToken.Token));
        Task next = nextContext.Invoke(
            () => Request(coordinator, afterCurrent, requestedReload, nextToken.Token));

        Fail(currentGate, cancelActive, ownerToken, failure);
        await nextContext.RunNextAsync();
        Assert.False(next.IsCompleted);
        await ownerContext.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        nextGate.SetResult();
        await nextContext.CompleteAsync(next);
        await LogOutcomeAsync(script.Log, "next", next);

        Assert.Equal(new Trace[]
        {
            new("refresh", activeReload, Token: ownerToken.Token),
            new("refresh", requestedReload, Token: nextToken.Token),
            cancelActive ? new("owner:canceled", Token: ownerToken.Token) : new("owner:faulted", Error: failure),
            new("next:succeeded"),
        }, script.Log);
    }

    /// <summary>Caller cancellation takes precedence while waiting, preserves the owner, and suppresses the fresh attempt.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CanceledWaitThenRequestDoesNotRetryUnrelatedFailure(bool afterCurrent, bool cancelActive)
    {
        using var ownerContext = new QueuedContext();
        using var waiterContext = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var waiterToken = new CancellationTokenSource();
        using var retryToken = new CancellationTokenSource();
        var gate = Gate();
        var failure = new InvalidOperationException("Synthetic active failure.");
        var script = new RefreshScript(gate.Task, Task.CompletedTask);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = ownerContext.Invoke(() => coordinator.RefreshAsync(afterCurrent, ownerToken.Token));
        Task waiter = waiterContext.Invoke(
            () => Request(coordinator, afterCurrent, !afterCurrent, waiterToken.Token));

        waiterToken.Cancel();
        await waiterContext.CompleteAsync(waiter);
        await LogOutcomeAsync(script.Log, "waiter", waiter);
        Assert.False(owner.IsCompleted);
        Assert.False(ownerToken.IsCancellationRequested);
        Fail(gate, cancelActive, ownerToken, failure);
        await ownerContext.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        Task retry = coordinator.RefreshAsync(false, retryToken.Token);
        await LogOutcomeAsync(script.Log, "retry", retry);

        Assert.Equal(new Trace[]
        {
            new("refresh", afterCurrent, Token: ownerToken.Token), new("waiter:canceled", Token: waiterToken.Token),
            cancelActive ? new("owner:canceled", Token: ownerToken.Token) : new("owner:faulted", Error: failure),
            new("refresh", false, Token: retryToken.Token), new("retry:succeeded"),
        }, script.Log);
    }

    /// <summary>Compatible joins propagate the active outcome; subsequent admission can retry either outcome.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompatibleJoinPropagatesFailureAndLaterRequestRecovers(bool cancelActive)
    {
        using var context = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var waiterToken = new CancellationTokenSource();
        using var retryToken = new CancellationTokenSource();
        var gate = Gate();
        var failure = new InvalidOperationException("Synthetic active failure.");
        var script = new RefreshScript(gate.Task, Task.CompletedTask);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = context.Invoke(() => coordinator.RefreshAsync(false, ownerToken.Token));
        Task waiter = coordinator.RefreshAsync(false, waiterToken.Token);

        Fail(gate, cancelActive, ownerToken, failure);
        await context.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        await LogOutcomeAsync(script.Log, "waiter", waiter);
        Assert.False(waiterToken.IsCancellationRequested);
        Task retry = coordinator.RefreshAsync(true, retryToken.Token);
        await LogOutcomeAsync(script.Log, "retry", retry);

        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: ownerToken.Token),
            cancelActive ? new("owner:canceled", Token: ownerToken.Token) : new("owner:faulted", Error: failure),
            cancelActive ? new("waiter:canceled", Token: ownerToken.Token) : new("waiter:faulted", Error: failure),
            new("refresh", true, Token: retryToken.Token), new("retry:succeeded"),
        }, script.Log);
    }

    /// <summary>A fresh attempt's failure propagates through either path and its cleanup permits another attempt.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FreshAttemptFailurePropagatesAndRetryRuns(bool afterCurrent, bool cancelFresh)
    {
        using var ownerContext = new QueuedContext();
        using var nextContext = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var nextToken = new CancellationTokenSource();
        using var retryToken = new CancellationTokenSource();
        var currentGate = Gate();
        var nextGate = Gate();
        var failure = new InvalidOperationException("Synthetic fresh failure.");
        var script = new RefreshScript(currentGate.Task, nextGate.Task, Task.CompletedTask);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = ownerContext.Invoke(() => coordinator.RefreshAsync(false, ownerToken.Token));
        Task next = nextContext.Invoke(() => Request(coordinator, afterCurrent, true, nextToken.Token));

        currentGate.SetResult();
        await nextContext.RunNextAsync();
        await ownerContext.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        Fail(nextGate, cancelFresh, nextToken, failure);
        await nextContext.CompleteAsync(next);
        await LogOutcomeAsync(script.Log, "next", next);
        Task retry = coordinator.RefreshAsync(false, retryToken.Token);
        await LogOutcomeAsync(script.Log, "retry", retry);

        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: ownerToken.Token), new("refresh", true, Token: nextToken.Token),
            new("owner:succeeded"),
            cancelFresh ? new("next:canceled", Token: nextToken.Token) : new("next:faulted", Error: failure),
            new("refresh", false, Token: retryToken.Token), new("retry:succeeded"),
        }, script.Log);
    }

    /// <summary>A completed raw task permits new admission before its observer runs, regardless of its outcome.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CompletedTaskDoesNotBlockAdmissionBeforeOlderCleanup(int outcome)
    {
        using var oldContext = new QueuedContext();
        using var newContext = new QueuedContext();
        using var oldToken = new CancellationTokenSource();
        using var newToken = new CancellationTokenSource();
        using var joinToken = new CancellationTokenSource();
        var oldGate = Gate();
        var newGate = Gate();
        var failure = new InvalidOperationException("Synthetic older failure.");
        var script = new RefreshScript(oldGate.Task, newGate.Task);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task old = oldContext.Invoke(() => coordinator.RefreshAsync(true, oldToken.Token));
        if (outcome == 0) oldGate.SetResult();
        else Fail(oldGate, outcome == 2, oldToken, failure);

        Task current = newContext.Invoke(() => coordinator.RefreshAsync(false, newToken.Token));
        Assert.False(old.IsCompleted);
        Assert.False(current.IsCompleted);
        await oldContext.CompleteAsync(old);
        await LogOutcomeAsync(script.Log, "old", old);
        Task join = coordinator.RefreshAsync(false, joinToken.Token);
        Assert.False(join.IsCompleted);
        newGate.SetResult();
        await newContext.CompleteAsync(current);
        await LogOutcomeAsync(script.Log, "current", current);
        await LogOutcomeAsync(script.Log, "join", join);

        Trace oldOutcome = outcome switch
        {
            0 => new("old:succeeded"),
            1 => new("old:faulted", Error: failure),
            _ => new("old:canceled", Token: oldToken.Token),
        };
        Assert.Equal(new Trace[]
        {
            new("refresh", true, Token: oldToken.Token), new("refresh", false, Token: newToken.Token),
            oldOutcome, new("current:succeeded"), new("join:succeeded"),
        }, script.Log);
    }

    /// <summary>A synchronous delegate throw is preserved and does not leave work admitted.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousDelegateFailureAllowsRecovery(bool afterCurrent)
    {
        using var firstToken = new CancellationTokenSource();
        using var retryToken = new CancellationTokenSource();
        var failure = new InvalidOperationException("Synthetic synchronous failure.");
        var log = new List<Trace>();
        Task Refresh(bool reloadSources, CancellationToken cancellationToken)
        {
            log.Add(new("refresh", reloadSources, Token: cancellationToken));
            if (log.Count == 1) throw failure;
            return Task.CompletedTask;
        }
        var coordinator = new MessageCenterRefreshCoordinator(Refresh);

        Exception? observed = await Record.ExceptionAsync(
            () => Request(coordinator, afterCurrent, false, firstToken.Token));
        Assert.Same(failure, observed);
        log.Add(new("first:failed", Error: observed));
        Task retry = coordinator.RefreshAsync(true, retryToken.Token);
        await LogOutcomeAsync(log, "retry", retry);

        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: firstToken.Token), new("first:failed", Error: failure),
            new("refresh", true, Token: retryToken.Token), new("retry:succeeded"),
        }, log);
    }

    /// <summary>Idle admission forwards even an already-canceled token; only the delegate decides new-work cancellation.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AlreadyCanceledTokenIsForwardedOnIdleAdmission(bool afterCurrent, bool reloadSources)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var script = new RefreshScript(Task.CompletedTask);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);

        Task request = Request(coordinator, afterCurrent, reloadSources, cancellation.Token);
        await LogOutcomeAsync(script.Log, "request", request);

        Assert.Equal(new Trace[]
        {
            new("refresh", reloadSources, Token: cancellation.Token), new("request:succeeded"),
        }, script.Log);
    }

    /// <summary>Cancellation after a successful wait is forwarded on fresh admission without an extra Core token check.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterWaitCompletedIsForwardedToFreshDelegate(bool afterCurrent)
    {
        using var ownerContext = new QueuedContext();
        using var nextContext = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var nextToken = new CancellationTokenSource();
        var gate = Gate();
        var script = new RefreshScript(gate.Task, Task.CompletedTask);
        var coordinator = new MessageCenterRefreshCoordinator(script.Refresh);
        Task owner = ownerContext.Invoke(() => coordinator.RefreshAsync(false, ownerToken.Token));
        Task next = nextContext.Invoke(
            () => Request(coordinator, afterCurrent, !afterCurrent, nextToken.Token));

        gate.SetResult();
        await nextContext.WaitForPostAsync();
        nextToken.Cancel();
        await nextContext.CompleteAsync(next);
        await ownerContext.CompleteAsync(owner);
        await LogOutcomeAsync(script.Log, "owner", owner);
        await LogOutcomeAsync(script.Log, "next", next);

        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: ownerToken.Token), new("refresh", !afterCurrent, Token: nextToken.Token),
            new("owner:succeeded"), new("next:succeeded"),
        }, script.Log);
    }

    /// <summary>Admission invokes the delegate inline and a stronger continuation retains the caller context.</summary>
    [Fact]
    public async Task AdmissionAndWaitThenRequestRetainCallerContext()
    {
        using var context = new QueuedContext();
        using var ownerToken = new CancellationTokenSource();
        using var nextToken = new CancellationTokenSource();
        var gate = Gate();
        var log = new List<Trace>();
        Task Refresh(bool reloadSources, CancellationToken cancellationToken)
        {
            Assert.Same(context, SynchronizationContext.Current);
            log.Add(new("refresh", reloadSources, Token: cancellationToken));
            return reloadSources ? Task.CompletedTask : gate.Task;
        }
        var coordinator = new MessageCenterRefreshCoordinator(Refresh);
        Task owner = context.Invoke(() => coordinator.RefreshAsync(false, ownerToken.Token));
        Assert.Single(log);
        Task next = context.Invoke(() => coordinator.RefreshAsync(true, nextToken.Token));
        Assert.Single(log);

        gate.SetResult();
        await context.CompleteAsync(next);
        await context.CompleteAsync(owner);
        await LogOutcomeAsync(log, "owner", owner);
        await LogOutcomeAsync(log, "next", next);

        Assert.Equal(new Trace[]
        {
            new("refresh", false, Token: ownerToken.Token), new("refresh", true, Token: nextToken.Token),
            new("owner:succeeded"), new("next:succeeded"),
        }, log);
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task Request(
        MessageCenterRefreshCoordinator coordinator, bool afterCurrent, bool reloadSources, CancellationToken token)
        => afterCurrent
            ? coordinator.RefreshAfterCurrentAsync(reloadSources, token)
            : coordinator.RefreshAsync(reloadSources, token);

    private static void Fail(
        TaskCompletionSource gate, bool cancel, CancellationTokenSource ownerToken, Exception failure)
    {
        if (cancel)
        {
            ownerToken.Cancel();
            gate.SetCanceled(ownerToken.Token);
        }
        else
        {
            gate.SetException(failure);
        }
    }

    private static async Task LogOutcomeAsync(List<Trace> log, string name, Task request)
    {
        Exception? error = await Record.ExceptionAsync(
            () => request.WaitAsync(WaitLimit, TestContext.Current.CancellationToken));
        if (error is OperationCanceledException canceled)
        {
            Assert.True(request.IsCanceled);
            log.Add(new(name + ":canceled", Token: canceled.CancellationToken));
        }
        else if (error is not null)
        {
            Assert.True(request.IsFaulted);
            log.Add(new(name + ":faulted", Error: error));
        }
        else
        {
            Assert.True(request.IsCompletedSuccessfully);
            log.Add(new(name + ":succeeded"));
        }
    }

    private sealed record Trace(
        string Event, bool? ReloadSources = null, Exception? Error = null, CancellationToken Token = default);

    private sealed class RefreshScript(params Task[] attempts)
    {
        private readonly Queue<Task> _attempts = new(attempts);

        internal List<Trace> Log { get; } = [];

        internal Task Refresh(bool reloadSources, CancellationToken cancellationToken)
        {
            Log.Add(new("refresh", reloadSources, Token: cancellationToken));
            return _attempts.Dequeue();
        }
    }

    // Each context is pumped by the test, keeping admission serialized while choosing completion order.
    private sealed class QueuedContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        private readonly SemaphoreSlim _posted = new(0);

        public override void Post(SendOrPostCallback d, object? state)
        {
            _callbacks.Enqueue((d, state));
            _posted.Release();
        }

        internal T Invoke<T>(Func<T> action)
        {
            SynchronizationContext? previous = Current;
            SetSynchronizationContext(this);
            try
            {
                return action();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        internal async Task RunNextAsync()
        {
            await _posted.WaitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
            Assert.True(_callbacks.TryDequeue(out var work));
            Invoke(() =>
            {
                work.Callback(work.State);
                return 0;
            });
        }

        internal async Task CompleteAsync(Task request)
        {
            while (!request.IsCompleted)
            {
                await RunNextAsync();
            }
        }

        internal async Task WaitForPostAsync()
        {
            await _posted.WaitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
            _posted.Release();
        }

        public void Dispose() => _posted.Dispose();
    }
}
