// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Shell;
using Xunit;

namespace Nvt.Core.Tests.Shell;

/// <summary>Characterizes activation failures, retry, and exception ordering.</summary>
public sealed partial class NavigationHistoryTests
{
    /// <summary>A failed forward or back activation keeps source inputs and leaves history retryable.</summary>
    /// <param name="isBack">Whether the failed completion is back navigation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedActivationKeepsSourceInputsAndRestoresRetryableHistory(bool isBack)
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        if (isBack)
        {
            host.History.CompleteNavigation("B", isBack: false);
        }
        host.Events.Clear();
        string source = host.SelectedPage;
        string target = isBack ? host.History.BackTarget : "B";
        bool hasSelectedInputs = true;
        bool failActivation = true;
        int clearCount = 0;
        string? clearedPage = null;
        var activationFailure = new InvalidOperationException("Injected destination activation failure.");
        host.BeforeActivation = _ =>
        {
            if (failActivation)
            {
                throw activationFailure;
            }
        };
        void ClearSource()
        {
            host.Events.Add($"clear:{source}");
            clearCount++;
            clearedPage = source;
            hasSelectedInputs = false;
        }

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.History.CompleteNavigation(target, isBack, ClearSource));

        Assert.Same(activationFailure, failure);
        Assert.Equal("Injected destination activation failure.", failure.Message);
        Assert.Equal(source, host.SelectedPage);
        Assert.True(hasSelectedInputs);
        Assert.Equal(0, clearCount);
        Assert.Null(clearedPage);
        Assert.True(host.History.CanGoBack);
        Assert.Equal(isBack ? "A" : "Home", host.History.BackTarget);

        failActivation = false;
        host.History.CompleteNavigation(target, isBack, ClearSource);

        Assert.Equal(target, host.SelectedPage);
        Assert.Equal(1, clearCount);
        Assert.Equal(source, clearedPage);
        Assert.False(hasSelectedInputs);

        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.Equal(isBack ? "Home" : "A", host.SelectedPage);
        Assert.Equal(!isBack, host.History.CanGoBack);
        AssertTrace(host, isBack ? """
            select:B
            activate:A:B:True:Home
            select:B
            state:B:True:A
            select:B
            activate:A:B:True:Home
            refresh:A:True:Home
            clear:B
            select:A
            activate:Home:A:False:-
            refresh:Home:False:-
            """ : """
            select:A
            select:A
            activate:B:A:True:A
            select:A
            state:A:True:Home
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            clear:A
            select:B
            activate:A:B:True:Home
            refresh:A:True:Home
            """);
    }

    /// <summary>A source-clear failure after destination activation restores source page and retryable history.</summary>
    [Fact]
    public void PostActivationSourceClearFailureRollsBackDestinationAndHistory()
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        host.Events.Clear();
        bool hasSelectedInputs = true;
        bool failClear = true;
        int successfulClearCount = 0;
        var clearFailure = new InvalidOperationException("Injected source-clear failure.");
        void ClearSource()
        {
            host.Events.Add("clear:A");
            if (failClear)
            {
                throw clearFailure;
            }
            successfulClearCount++;
            hasSelectedInputs = false;
        }

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.History.CompleteNavigation("B", isBack: false, ClearSource));

        Assert.Same(clearFailure, failure);
        Assert.Equal("Injected source-clear failure.", failure.Message);
        Assert.Equal("A", host.SelectedPage);
        Assert.True(hasSelectedInputs);
        Assert.Equal(0, successfulClearCount);
        Assert.Equal("Home", host.History.BackTarget);

        failClear = false;
        host.History.CompleteNavigation("B", isBack: false, ClearSource);

        Assert.Equal("B", host.SelectedPage);
        Assert.False(hasSelectedInputs);
        Assert.Equal(1, successfulClearCount);
        Assert.Equal("A", host.History.BackTarget);
        AssertTrace(host, """
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            clear:A
            select:B
            activate:A:B:True:A
            refresh:A:True:A
            state:A:True:Home
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            clear:A
            """);
    }

    /// <summary>A back completion action failure reactivates the source before restoring the removed entry.</summary>
    [Fact]
    public void PostActivationBackFailureRestoresSourceBeforeHistory()
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        host.Events.Clear();
        var completionFailure = new InvalidOperationException("Injected completion failure.");

        var failure = Assert.Throws<InvalidOperationException>(() => host.History.CompleteNavigation(
            host.History.BackTarget, isBack: true, () =>
            {
                host.Events.Add("after:Home");
                throw completionFailure;
            }));

        Assert.Same(completionFailure, failure);
        Assert.Equal("A", host.SelectedPage);
        Assert.True(host.History.CanGoBack);
        Assert.Equal("Home", host.History.BackTarget);
        AssertTrace(host, """
            select:A
            activate:Home:A:False:-
            refresh:Home:False:-
            after:Home
            select:Home
            activate:A:Home:False:-
            refresh:A:False:-
            state:A:True:Home
            """);
    }

    /// <summary>Activation that fails after changing selection rolls back without calling the completion action.</summary>
    [Fact]
    public void FailedActivationAfterSelectionReactivatesSourceAndRestoresHistory()
    {
        var host = new HistoryHost();
        var activationFailure = new InvalidOperationException("Injected refresh failure.");
        host.OnRefresh = page =>
        {
            if (page == "A")
            {
                throw activationFailure;
            }
        };

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.History.CompleteNavigation("A", isBack: false, () => host.Events.Add("after:A")));

        Assert.Same(activationFailure, failure);
        Assert.Equal("Home", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => host.History.BackTarget);
        AssertTrace(host, """
            select:Home
            select:Home
            activate:A:Home:True:Home
            refresh:A:True:Home
            select:A
            activate:Home:A:True:Home
            refresh:Home:True:Home
            state:Home:False:-
            """);
    }

    /// <summary>A failed equal-target activation refreshes restored state without unnecessary source reactivation.</summary>
    [Fact]
    public void FailedEqualTargetActivationRestoresStateWithoutReactivation()
    {
        var host = new HistoryHost();
        var activationFailure = new InvalidOperationException("Injected equal-target failure.");
        host.BeforeActivation = _ => throw activationFailure;

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.History.CompleteNavigation("Home", isBack: false));

        Assert.Same(activationFailure, failure);
        Assert.False(host.History.CanGoBack);
        AssertTrace(host, """
            select:Home
            select:Home
            activate:Home:Home:False:-
            select:Home
            state:Home:False:-
            """);
    }

    /// <summary>Rollback compares page values, so replacing selection with an equal instance does not reactivate.</summary>
    [Fact]
    public void RollbackUsesDefaultPageEqualityInsteadOfReferenceIdentity()
    {
        var home = new PageId("Home");
        var selectedPage = home;
        var target = new PageId("Home");
        var events = new List<string>();
        var activationFailure = new InvalidOperationException("Injected equal-value failure.");
        var history = new NavigationHistory<PageId>(home, () =>
        {
            events.Add("select");
            return selectedPage;
        }, page =>
        {
            events.Add("activate");
            selectedPage = page;
            throw activationFailure;
        }, () => events.Add("state"));

        var failure = Assert.Throws<InvalidOperationException>(
            () => history.CompleteNavigation(target, isBack: false));

        Assert.Same(activationFailure, failure);
        Assert.Same(target, selectedPage);
        Assert.False(history.CanGoBack);
        Assert.Equal("select\nselect\nactivate\nselect\nstate", string.Join("\n", events));
    }

    /// <summary>Rollback activation failure replaces the original failure before history restoration or state refresh.</summary>
    /// <param name="rollbackChangesSelection">Whether rollback fails before or after publishing its selection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollbackActivationFailureInterruptsHistoryRestoration(bool rollbackChangesSelection)
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        host.Events.Clear();
        var originalFailure = new InvalidOperationException("Injected completion failure.");
        var rollbackFailure = new InvalidOperationException("Injected rollback activation failure.");
        host.BeforeActivation = page =>
        {
            if (page == "A" && !rollbackChangesSelection)
            {
                throw rollbackFailure;
            }
        };
        host.OnRefresh = page =>
        {
            if (page == "A" && rollbackChangesSelection)
            {
                throw rollbackFailure;
            }
        };

        var failure = Assert.Throws<InvalidOperationException>(() => host.History.CompleteNavigation(
            "B", isBack: false, () =>
            {
                host.Events.Add("after:B");
                throw originalFailure;
            }));

        Assert.Same(rollbackFailure, failure);
        Assert.Equal(rollbackChangesSelection ? "A" : "B", host.SelectedPage);
        Assert.True(host.History.CanGoBack);
        Assert.Equal("A", host.History.BackTarget);
        AssertTrace(host, rollbackChangesSelection ? """
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            after:B
            select:B
            activate:A:B:True:A
            refresh:A:True:A
            """ : """
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            after:B
            select:B
            activate:A:B:True:A
            """);
    }

    /// <summary>A failed restoration refresh propagates after history has already been restored.</summary>
    [Fact]
    public void StateChangedFailureReplacesOriginalFailureAfterHistoryRestoration()
    {
        var host = new HistoryHost();
        var originalFailure = new InvalidOperationException("Injected activation failure.");
        var refreshFailure = new InvalidOperationException("Injected state refresh failure.");
        host.BeforeActivation = _ => throw originalFailure;
        host.OnStateChanged = () => throw refreshFailure;

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.History.CompleteNavigation("A", isBack: false));

        Assert.Same(refreshFailure, failure);
        Assert.Equal("Home", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
        AssertTrace(host, """
            select:Home
            select:Home
            activate:A:Home:True:Home
            select:Home
            state:Home:False:-
            """);
    }

    /// <summary>Source capture and the second forward selection read both occur outside activation rollback.</summary>
    /// <param name="failingRead">The selection read that throws.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectionReadFailureBeforeActivationLeavesHistoryUntouched(int failingRead)
    {
        var host = new HistoryHost();
        var selectionFailure = new InvalidOperationException("Injected selection read failure.");
        int reads = 0;
        host.SelectionReader = page => ++reads == failingRead ? throw selectionFailure : page;

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.History.CompleteNavigation("A", isBack: false, () => host.Events.Add("after:A")));

        Assert.Same(selectionFailure, failure);
        Assert.False(host.History.CanGoBack);
        AssertTrace(host, failingRead == 1 ? "select:Home" : "select:Home\nselect:Home");
    }

    /// <summary>A failed rollback selection read replaces the activation failure and leaves changed history intact.</summary>
    [Fact]
    public void RollbackSelectionReadFailureInterruptsHistoryRestoration()
    {
        var host = new HistoryHost();
        var originalFailure = new InvalidOperationException("Injected activation failure.");
        var selectionFailure = new InvalidOperationException("Injected rollback selection read failure.");
        int reads = 0;
        host.SelectionReader = page => ++reads == 3 ? throw selectionFailure : page;
        host.BeforeActivation = _ => throw originalFailure;

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.History.CompleteNavigation("A", isBack: false));

        Assert.Same(selectionFailure, failure);
        Assert.Equal("Home", host.SelectedPage);
        Assert.True(host.History.CanGoBack);
        Assert.Equal("Home", host.History.BackTarget);
        AssertTrace(host, """
            select:Home
            select:Home
            activate:A:Home:True:Home
            select:Home
            """);
    }
}
