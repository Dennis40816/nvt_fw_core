// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Shell;
using Xunit;

namespace Nvt.Core.Tests.Shell;

/// <summary>Characterizes generic navigation history with synthetic page identities and complete callback traces.</summary>
public sealed partial class NavigationHistoryTests
{
    /// <summary>Construction seeds the supplied Home entry without reading selection or invoking callbacks.</summary>
    [Fact]
    public void ConstructorSeedsHomeWithoutInvokingCallbacks()
    {
        var host = new HistoryHost("Start");

        Assert.Equal("Start", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => host.History.BackTarget);
        Assert.Empty(host.Events);

        host.History.CompleteNavigation("A", isBack: false);

        Assert.Equal("Start", host.History.BackTarget);
        AssertTrace(host, """
            select:Start
            select:Start
            activate:A:Start:True:Start
            refresh:A:True:Start
            """);
    }

    /// <summary>Null callbacks are rejected in selected-page, activation, then state-refresh order.</summary>
    /// <param name="firstMissing">The first missing callback in constructor parameter order.</param>
    /// <param name="parameterName">The expected exception parameter name.</param>
    [Theory]
    [InlineData(0, "selectedPage")]
    [InlineData(1, "activate")]
    [InlineData(2, "stateChanged")]
    public void ConstructorRejectsNullCallbacksInParameterOrder(int firstMissing, string parameterName)
    {
        var failure = Assert.Throws<ArgumentNullException>(() => new NavigationHistory<string>(
            "Home",
            firstMissing == 0 ? null! : static () => "Home",
            firstMissing <= 1 ? null! : static _ => { },
            null!));

        Assert.Equal(parameterName, failure.ParamName);
    }

    /// <summary>Forward and back completion cross the one-entry boundary and activate pages in order.</summary>
    [Fact]
    public void ForwardAndBackCompletionPreserveHistoryAndCallbackOrder()
    {
        var host = new HistoryHost();

        Assert.False(host.History.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => host.History.BackTarget);

        host.History.CompleteNavigation("A", isBack: false);
        Assert.True(host.History.CanGoBack);
        Assert.Equal("Home", host.History.BackTarget);

        host.History.CompleteNavigation("B", isBack: false, () => host.Events.Add("after:B"));
        Assert.True(host.History.CanGoBack);
        Assert.Equal("A", host.History.BackTarget);

        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.Equal("A", host.SelectedPage);
        Assert.True(host.History.CanGoBack);
        Assert.Equal("Home", host.History.BackTarget);

        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.Equal("Home", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => host.History.BackTarget);
        AssertTrace(host, """
            select:Home
            select:Home
            activate:A:Home:True:Home
            refresh:A:True:Home
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            after:B
            select:B
            activate:A:B:True:Home
            refresh:A:True:Home
            select:A
            activate:Home:A:False:-
            refresh:Home:False:-
            """);
    }

    /// <summary>An equal target still activates and runs the completion action without adding history.</summary>
    [Fact]
    public void EqualTargetCompletionActivatesWithoutAddingHistory()
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        host.Events.Clear();

        host.History.CompleteNavigation("A", isBack: false, () => host.Events.Add("after:A"));

        Assert.Equal("Home", host.History.BackTarget);
        AssertTrace(host, """
            select:A
            select:A
            activate:A:A:True:Home
            refresh:A:True:Home
            after:A
            """);

        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.False(host.History.CanGoBack);
    }

    /// <summary>Distinct reference identities that compare equal use the default value equality.</summary>
    [Fact]
    public void EqualReferencePageValuesStillActivateTheSuppliedTarget()
    {
        var home = new PageId("Home");
        var selectedPage = home;
        var target = new PageId("Home");
        var events = new List<string>();
        var history = new NavigationHistory<PageId>(home, () =>
        {
            events.Add("select");
            return selectedPage;
        }, page =>
        {
            events.Add("activate");
            selectedPage = page;
        }, () => events.Add("state"));

        history.CompleteNavigation(target, isBack: false, () => events.Add("after"));

        Assert.NotSame(home, target);
        Assert.Same(target, selectedPage);
        Assert.False(history.CanGoBack);
        Assert.Equal("select\nselect\nactivate\nafter", string.Join("\n", events));
    }

    /// <summary>Back completion with a single entry still activates the supplied target and keeps the Home entry.</summary>
    [Fact]
    public void BackCompletionWithOnlyHomeStillActivatesCapturedTarget()
    {
        var host = new HistoryHost();

        host.History.CompleteNavigation("A", isBack: true, () => host.Events.Add("after:A"));
        host.History.CompleteNavigation("B", isBack: true);

        Assert.Equal("B", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => host.History.BackTarget);
        AssertTrace(host, """
            select:Home
            activate:A:Home:False:-
            refresh:A:False:-
            after:A
            select:A
            activate:B:A:False:-
            refresh:B:False:-
            """);

        host.History.CompleteNavigation("C", isBack: false);
        Assert.Equal("Home", host.History.BackTarget);
    }

    /// <summary>Empty, whitespace, control, and Unicode page IDs round-trip without product validation.</summary>
    /// <param name="target">The synthetic page identity.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A\0B\r\nC")]
    [InlineData("頁面😀e\u0301")]
    public void StringPageIdentitiesRoundTripWithoutNormalization(string target)
    {
        var host = new HistoryHost();

        host.History.CompleteNavigation(target, isBack: false);
        Assert.Equal(target, host.SelectedPage);
        Assert.True(host.History.CanGoBack);
        Assert.Equal("Home", host.History.BackTarget);

        host.History.CompleteNavigation("Next", isBack: false);
        Assert.Equal(target, host.History.BackTarget);

        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.Equal(target, host.SelectedPage);
        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.Equal("Home", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
    }

    /// <summary>Numeric identities are opaque values, including zero, negatives, and type endpoints.</summary>
    /// <param name="target">The synthetic numeric page identity.</param>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NumericPageIdentitiesHaveNoPositiveLimitPolicy(int target)
    {
        var selectedPage = 42;
        var history = new NavigationHistory<int>(42, () => selectedPage, page => selectedPage = page, static () => { });

        history.CompleteNavigation(target, isBack: false);

        Assert.Equal(target, selectedPage);
        Assert.True(history.CanGoBack);
        Assert.Equal(42, history.BackTarget);
        history.CompleteNavigation(history.BackTarget, isBack: true);
        Assert.Equal(42, selectedPage);
        Assert.False(history.CanGoBack);
    }

    private static void AssertTrace(HistoryHost host, string expected)
    {
        Assert.Equal(
            expected.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            host.Events);
    }

    private sealed record PageId(string Value);

    private sealed class HistoryHost
    {
        internal HistoryHost(string home = "Home")
        {
            SelectedPage = home;
            History = new NavigationHistory<string>(home, ReadSelectedPage, Activate, StateChanged);
        }

        internal NavigationHistory<string> History { get; }

        internal string SelectedPage { get; set; }

        internal List<string> Events { get; } = [];

        internal Func<string, string>? SelectionReader { get; set; }

        internal Action<string>? BeforeActivation { get; set; }

        internal Action<string>? OnRefresh { get; set; }

        internal Action? OnStateChanged { get; set; }

        private string Snapshot => $"{SelectedPage}:{History.CanGoBack}:{(History.CanGoBack ? History.BackTarget : "-")}";

        private string ReadSelectedPage()
        {
            Events.Add($"select:{SelectedPage}");
            return SelectionReader is { } read ? read(SelectedPage) : SelectedPage;
        }

        private void Activate(string target)
        {
            Events.Add($"activate:{target}:{Snapshot}");
            BeforeActivation?.Invoke(target);
            SelectedPage = target;
            Events.Add($"refresh:{Snapshot}");
            OnRefresh?.Invoke(target);
        }

        private void StateChanged()
        {
            Events.Add($"state:{Snapshot}");
            OnStateChanged?.Invoke();
        }
    }
}
