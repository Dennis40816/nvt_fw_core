// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.Progress;
using Xunit;

namespace Nvt.Core.Tests.Progress;

/// <summary>Ports the frozen loading-scope tests and characterizes nesting, timing and frame yields.</summary>
public sealed class LoadingScopeCoordinatorTests
{
    private static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly bool[] ShownOnly = [true];
    private static readonly bool[] ShownThenHidden = [true, false];
    private static readonly bool[] ShownTwice = [true, true];
    private static readonly bool[] ShownTwiceThenHidden = [true, true, false];
    private static readonly bool[] ShownTwiceThenHiddenTwice = [true, true, false, false];
    private static readonly bool[] HiddenThenShown = [false, true];
    private static readonly string[] RunOrder = ["show", "yield", "action", "yield", "hide", "yield"];

    /// <summary>Ported: the surface shows before the action and hides after it.</summary>
    [Fact]
    public async Task RunAsyncShowsBeforeActionAndHidesAfterAction()
    {
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(
            yieldFrameAsync: static () => Task.CompletedTask,
            minimumVisibleDuration: TimeSpan.Zero);

        await coordinator.RunAsync(
            action: async () =>
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            },
            setVisible: states.Add);

        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>
    /// Ported: the surface stays visible for the minimum duration. The source measured a Stopwatch against the
    /// wall clock; this version uses the manual clock, so a system clock change cannot skip the wait.
    /// </summary>
    [Fact]
    public async Task RunAsyncRespectsMinimumVisibleDuration()
    {
        var time = new ManualTimeProvider();
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(
            yieldFrameAsync: static () => Task.CompletedTask,
            minimumVisibleDuration: TimeSpan.FromMilliseconds(60),
            timeProvider: time);

        Task run = coordinator.RunAsync(
            action: static () => Task.CompletedTask,
            setVisible: states.Add);

        Assert.Equal(TimeSpan.FromMilliseconds(60), Assert.Single(time.RequestedDueTimes));
        Assert.False(run.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(60));
        await run.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>Without a time provider the coordinator waits on the system clock and still shows and hides.</summary>
    [Fact]
    public async Task DefaultClockRunShowsAndHides()
    {
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, TimeSpan.FromMilliseconds(20));

        await coordinator.RunAsync(static () => Task.CompletedTask, states.Add)
            .WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>The hide waits on the injected clock for the rest of the minimum visible time.</summary>
    [Fact]
    public async Task EndAsyncWaitsForTheRestOfTheMinimumOnTheInjectedClock()
    {
        var time = new ManualTimeProvider();
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, Minimum, time);

        coordinator.Begin(states.Add);
        time.Advance(TimeSpan.FromMilliseconds(50));
        Task end = coordinator.EndAsync(states.Add);

        Assert.Equal(TimeSpan.FromMilliseconds(70), Assert.Single(time.RequestedDueTimes));
        Assert.False(end.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(69));
        Assert.False(end.IsCompleted);
        Assert.Equal(ShownOnly, states);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await end.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>When the minimum has already passed, the hide does not start a timer.</summary>
    [Fact]
    public async Task ElapsedMinimumHidesWithoutWaiting()
    {
        var time = new ManualTimeProvider();
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, Minimum, time);

        coordinator.Begin(states.Add);
        time.Advance(Minimum);
        await coordinator.EndAsync(states.Add);

        Assert.Empty(time.RequestedDueTimes);
        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>A negative minimum counts as zero.</summary>
    [Fact]
    public async Task NegativeMinimumMeansNoWait()
    {
        var time = new ManualTimeProvider();
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, TimeSpan.FromSeconds(-1), time);

        coordinator.Begin(states.Add);
        await coordinator.EndAsync(states.Add);

        Assert.Empty(time.RequestedDueTimes);
        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>Nested scopes show once and hide only after the last scope ends.</summary>
    [Fact]
    public async Task NestedScopesShowOnceAndHideAfterTheLastEnd()
    {
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, TimeSpan.Zero);

        coordinator.Begin(states.Add);
        coordinator.Begin(states.Add);
        Assert.Equal(ShownOnly, states);

        await coordinator.EndAsync(states.Add);
        Assert.Equal(ShownOnly, states);

        await coordinator.EndAsync(states.Add);
        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>An end without an open scope neither hides nor yields.</summary>
    [Fact]
    public async Task EndWithoutAnOpenScopeDoesNothing()
    {
        var states = new List<bool>();
        var yields = 0;
        var coordinator = new LoadingScopeCoordinator(
            () =>
            {
                yields++;
                return Task.CompletedTask;
            },
            TimeSpan.Zero);

        await coordinator.EndAsync(states.Add);

        Assert.Empty(states);
        Assert.Equal(0, yields);
    }

    /// <summary>
    /// A scope that begins during the minimum wait cancels that hide. It shows again and restarts the
    /// minimum visible time.
    /// </summary>
    [Fact]
    public async Task BeginDuringTheMinimumWaitKeepsTheSurfaceVisible()
    {
        var time = new ManualTimeProvider();
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, Minimum, time);

        coordinator.Begin(states.Add);
        Task firstEnd = coordinator.EndAsync(states.Add);
        time.Advance(TimeSpan.FromMilliseconds(100));
        coordinator.Begin(states.Add);
        time.Advance(TimeSpan.FromMilliseconds(20));
        await firstEnd.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(ShownTwice, states);

        Task secondEnd = coordinator.EndAsync(states.Add);
        Assert.Equal(TimeSpan.FromMilliseconds(100), time.RequestedDueTimes[^1]);
        time.Advance(TimeSpan.FromMilliseconds(100));
        await secondEnd.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(ShownTwiceThenHidden, states);
    }

    /// <summary>
    /// Characterizes the source: the hide check looks only at the open scope count. A newer scope that begins
    /// and ends during the wait does not stop the earlier hide, and its own end hides again later.
    /// </summary>
    [Fact]
    public async Task OverlappingEndHidesBeforeTheNewerScopesMinimum()
    {
        var time = new ManualTimeProvider();
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, Minimum, time);

        coordinator.Begin(states.Add);
        Task firstEnd = coordinator.EndAsync(states.Add);
        time.Advance(TimeSpan.FromMilliseconds(100));
        coordinator.Begin(states.Add);
        Task secondEnd = coordinator.EndAsync(states.Add);
        Assert.Equal(Minimum, time.RequestedDueTimes[^1]);

        time.Advance(TimeSpan.FromMilliseconds(20));
        await firstEnd.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(ShownTwiceThenHidden, states);
        Assert.False(secondEnd.IsCompleted);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await secondEnd.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(ShownTwiceThenHiddenTwice, states);
    }

    /// <summary>
    /// Characterizes the source: the callbacks run outside the lock. An end on one thread can hide the surface
    /// before a begin on another thread shows it, which leaves the surface visible with no open scope.
    /// </summary>
    [Fact]
    public async Task UnserializedCallsCanLeaveTheSurfaceVisibleWithoutAScope()
    {
        var token = TestContext.Current.CancellationToken;
        var states = new ConcurrentQueue<bool>();
        using var showEntered = new ManualResetEventSlim();
        using var releaseShow = new ManualResetEventSlim();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, TimeSpan.Zero);

        Task begin = Task.Run(
            () => coordinator.Begin(visible =>
            {
                showEntered.Set();
                Assert.True(releaseShow.Wait(WaitLimit, token));
                states.Enqueue(visible);
            }),
            token);
        Assert.True(showEntered.Wait(WaitLimit, token));

        await coordinator.EndAsync(states.Enqueue);
        releaseShow.Set();
        await begin.WaitAsync(WaitLimit, token);

        Assert.Equal(HiddenThenShown, states);
        var laterEnd = new List<bool>();
        await coordinator.EndAsync(laterEnd.Add);
        Assert.Empty(laterEnd);
    }

    /// <summary>A run yields a frame before the action, before the hide and after the hide.</summary>
    [Fact]
    public async Task RunAsyncYieldsAroundTheActionAndTheHide()
    {
        var events = new List<string>();
        var coordinator = new LoadingScopeCoordinator(
            () =>
            {
                events.Add("yield");
                return Task.CompletedTask;
            },
            TimeSpan.Zero);

        await coordinator.RunAsync(
            () =>
            {
                events.Add("action");
                return Task.CompletedTask;
            },
            visible => events.Add(visible ? "show" : "hide"));

        Assert.Equal(RunOrder, events);
    }

    /// <summary>A failing action still ends its scope, and the exception reaches the caller.</summary>
    [Fact]
    public async Task RunAsyncEndsTheScopeWhenTheActionThrows()
    {
        var states = new List<bool>();
        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(
            static () => throw new InvalidOperationException("failed"),
            states.Add));

        Assert.Equal(ShownThenHidden, states);
    }

    /// <summary>Null delegates are rejected with their parameter names. A null clock means the system clock.</summary>
    [Fact]
    public async Task NullArgumentsThrowWithTheirNames()
    {
        Assert.Equal(
            "yieldFrameAsync",
            Assert.Throws<ArgumentNullException>(() => new LoadingScopeCoordinator(null!, TimeSpan.Zero)).ParamName);

        var coordinator = new LoadingScopeCoordinator(static () => Task.CompletedTask, TimeSpan.Zero, timeProvider: null);
        Assert.Equal(
            "action",
            (await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.RunAsync(null!, static _ => { }))).ParamName);
        Assert.Equal(
            "setVisible",
            (await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.RunAsync(static () => Task.CompletedTask, null!))).ParamName);
        Assert.Equal("setVisible", Assert.Throws<ArgumentNullException>(() => coordinator.Begin(null!)).ParamName);
        Assert.Equal(
            "setVisible",
            (await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.EndAsync(null!))).ParamName);
    }
}
