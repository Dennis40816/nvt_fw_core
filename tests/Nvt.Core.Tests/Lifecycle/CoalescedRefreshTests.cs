// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Lifecycle;
using Xunit;

namespace Nvt.Core.Tests.Lifecycle;

/// <summary>Ports the frozen refresh tests and characterizes scheduling failure and reset behavior.</summary>
public sealed class CoalescedRefreshTests
{
    /// <summary>A burst of requests queues only one callback.</summary>
    [Fact]
    public void RequestBurstBeforeTheRefreshRunsSchedulesOnce()
    {
        var scheduled = new List<Action>();
        var refreshCount = 0;
        var refresh = new CoalescedRefresh(scheduled.Add, () => refreshCount++);

        for (var i = 0; i < 25; i++)
        {
            refresh.Request();
        }

        Assert.Single(scheduled);
        scheduled[0]();
        Assert.Equal(1, refreshCount);
    }

    /// <summary>A request after a completed refresh queues another callback.</summary>
    [Fact]
    public void RequestAfterTheRefreshRanSchedulesAgain()
    {
        var scheduled = new List<Action>();
        var refreshCount = 0;
        var refresh = new CoalescedRefresh(scheduled.Add, () => refreshCount++);

        refresh.Request();
        scheduled[0]();
        refresh.Request();

        Assert.Equal(2, scheduled.Count);
        scheduled[1]();
        Assert.Equal(2, refreshCount);
    }

    /// <summary>A request during the refresh queues a callback that can also run.</summary>
    [Fact]
    public void RequestWhileTheRefreshIsRunningSchedulesAnother()
    {
        var scheduled = new List<Action>();
        CoalescedRefresh? refresh = null;
        var refreshCount = 0;
        refresh = new CoalescedRefresh(scheduled.Add, () =>
        {
            refreshCount++;
            if (refreshCount == 1)
            {
                // A change that arrives during the refresh must not be lost.
                refresh!.Request();
            }
        });

        refresh.Request();
        scheduled[0]();

        Assert.Equal(2, scheduled.Count);
        scheduled[1]();
        Assert.Equal(2, refreshCount);
    }

    /// <summary>Reset permits another request to schedule while an earlier callback is queued.</summary>
    [Fact]
    public void ResetWithAScheduledRefreshLetsTheNextRequestSchedule()
    {
        var scheduled = new List<Action>();
        var refresh = new CoalescedRefresh(scheduled.Add, static () => { });

        refresh.Request();
        refresh.Reset();
        refresh.Request();

        Assert.Equal(2, scheduled.Count);
    }

    /// <summary>Reset leaves the earlier callback runnable, including when a new callback is queued.</summary>
    [Fact]
    public void ResetLeavesTheQueuedCallbackRunnable()
    {
        var scheduled = new List<Action>();
        var refreshCount = 0;
        var refresh = new CoalescedRefresh(scheduled.Add, () => refreshCount++);

        refresh.Request();
        refresh.Reset();

        Assert.Single(scheduled);
        Assert.Equal(0, refreshCount);

        refresh.Request();
        Assert.Equal(2, scheduled.Count);

        scheduled[0]();
        Assert.Equal(1, refreshCount);
        scheduled[1]();
        Assert.Equal(2, refreshCount);
    }

    /// <summary>A scheduling exception propagates and leaves requests coalesced until reset.</summary>
    [Fact]
    public void RequestWhenSchedulerThrowsStaysCoalescedUntilReset()
    {
        var scheduled = new List<Action>();
        var scheduleCount = 0;
        var refreshCount = 0;
        var failure = new InvalidOperationException("Scheduling failed.");
        var refresh = new CoalescedRefresh(callback =>
        {
            scheduleCount++;
            if (scheduleCount == 1)
            {
                throw failure;
            }

            scheduled.Add(callback);
        }, () => refreshCount++);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(refresh.Request));
        refresh.Request();

        Assert.Equal(1, scheduleCount);
        Assert.Empty(scheduled);
        Assert.Equal(0, refreshCount);

        refresh.Reset();
        refresh.Request();

        Assert.Equal(2, scheduleCount);
        Assert.Single(scheduled)();
        Assert.Equal(1, refreshCount);
    }
}
