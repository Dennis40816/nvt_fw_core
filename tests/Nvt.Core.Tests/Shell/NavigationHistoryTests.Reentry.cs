// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Tests.Shell;

/// <summary>Characterizes synchronous reentry and host-captured targets.</summary>
public sealed partial class NavigationHistoryTests
{
    /// <summary>A reentrant refresh sees changed back availability before each completion action.</summary>
    [Fact]
    public void HostRefreshObservesChangedHistoryBeforeCompletionActions()
    {
        var host = new HistoryHost();
        host.OnRefresh = page =>
        {
            Assert.Equal(page != "Home", host.History.CanGoBack);
            if (page != "Home")
            {
                Assert.Equal("Home", host.History.BackTarget);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => host.History.BackTarget);
            }
            host.Events.Add($"observe:{host.History.CanGoBack}");
        };

        host.History.CompleteNavigation("A", isBack: false, () => host.Events.Add("after:A"));
        host.History.CompleteNavigation(host.History.BackTarget, isBack: true, () => host.Events.Add("after:Home"));

        AssertTrace(host, """
            select:Home
            select:Home
            activate:A:Home:True:Home
            refresh:A:True:Home
            observe:True
            after:A
            select:A
            activate:Home:A:False:-
            refresh:Home:False:-
            observe:False
            after:Home
            """);
    }

    /// <summary>Confirmation-close reentry does not replace the captured destination or the host's captured clear source.</summary>
    [Fact]
    public void CapturedBackTargetCompletesAfterConfirmationCloseReentry()
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        host.History.CompleteNavigation("B", isBack: false);
        host.Events.Clear();
        string capturedTarget = host.History.BackTarget;
        string capturedClearSource = host.SelectedPage;
        host.Events.Add($"capture:{capturedTarget}");
        host.Events.Add("confirmation:close");

        host.History.CompleteNavigation("C", isBack: false);
        Assert.Equal("B", host.History.BackTarget);
        host.History.CompleteNavigation(capturedTarget, isBack: true,
            () => host.Events.Add($"clear:{capturedClearSource}"));

        Assert.Equal("A", host.SelectedPage);
        Assert.Equal("A", host.History.BackTarget);
        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.Equal("A", host.SelectedPage);
        Assert.Equal("Home", host.History.BackTarget);
        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.False(host.History.CanGoBack);
        AssertTrace(host, """
            capture:A
            confirmation:close
            select:B
            select:B
            activate:C:B:True:B
            refresh:C:True:B
            select:C
            activate:A:C:True:A
            refresh:A:True:A
            clear:B
            select:A
            activate:A:A:True:Home
            refresh:A:True:Home
            select:A
            activate:Home:A:False:-
            refresh:Home:False:-
            """);
    }

    /// <summary>A captured back completion failure rolls back the selection read after confirmation-close reentry.</summary>
    [Fact]
    public void CapturedBackFailureRestoresSourceObservedAfterConfirmationCloseReentry()
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        host.History.CompleteNavigation("B", isBack: false);
        string capturedTarget = host.History.BackTarget;
        string capturedClearSource = host.SelectedPage;
        host.History.CompleteNavigation("C", isBack: false);
        host.Events.Clear();
        var clearFailure = new InvalidOperationException("Injected source-clear failure.");

        var failure = Assert.Throws<InvalidOperationException>(() => host.History.CompleteNavigation(
            capturedTarget, isBack: true, () =>
            {
                host.Events.Add($"clear:{capturedClearSource}");
                throw clearFailure;
            }));

        Assert.Same(clearFailure, failure);
        Assert.Equal("C", host.SelectedPage);
        Assert.Equal("B", host.History.BackTarget);
        AssertTrace(host, """
            select:C
            activate:A:C:True:A
            refresh:A:True:A
            clear:B
            select:A
            activate:C:A:True:A
            refresh:C:True:A
            state:C:True:B
            """);
    }

    /// <summary>A nested completion during host refresh finishes before the outer completion action.</summary>
    [Fact]
    public void ActivationRefreshCanCompleteNestedNavigation()
    {
        var host = new HistoryHost();
        host.OnRefresh = _ =>
        {
            host.OnRefresh = null;
            host.Events.Add("reenter:B");
            host.History.CompleteNavigation("B", isBack: false, () => host.Events.Add("after:B"));
        };

        host.History.CompleteNavigation("A", isBack: false,
            () => host.Events.Add($"after:A:selected={host.SelectedPage}"));

        Assert.Equal("B", host.SelectedPage);
        Assert.Equal("A", host.History.BackTarget);
        AssertTrace(host, """
            select:Home
            select:Home
            activate:A:Home:True:Home
            refresh:A:True:Home
            reenter:B
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            after:B
            after:A:selected=B
            """);
    }

    /// <summary>Failure of an outer completion restores its snapshot, including removal of nested history changes.</summary>
    [Fact]
    public void OuterFailureRestoresHistoryFromBeforeNestedNavigation()
    {
        var host = new HistoryHost();
        var completionFailure = new InvalidOperationException("Injected outer completion failure.");
        host.OnRefresh = _ =>
        {
            host.OnRefresh = null;
            host.Events.Add("reenter:B");
            host.History.CompleteNavigation("B", isBack: false);
        };

        var failure = Assert.Throws<InvalidOperationException>(() => host.History.CompleteNavigation(
            "A", isBack: false, () =>
            {
                host.Events.Add("after:A");
                throw completionFailure;
            }));

        Assert.Same(completionFailure, failure);
        Assert.Equal("Home", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
        Assert.Throws<InvalidOperationException>(() => host.History.BackTarget);
        AssertTrace(host, """
            select:Home
            select:Home
            activate:A:Home:True:Home
            refresh:A:True:Home
            reenter:B
            select:A
            select:A
            activate:B:A:True:A
            refresh:B:True:A
            after:A
            select:B
            activate:Home:B:True:A
            refresh:Home:True:A
            state:Home:False:-
            """);
    }

    /// <summary>The host's selected page determines forward appends even when it differs from the latest history entry.</summary>
    [Fact]
    public void ForwardAppendComparesSelectionInsteadOfLatestHistoryEntry()
    {
        var host = new HistoryHost();
        host.History.CompleteNavigation("A", isBack: false);
        host.SelectedPage = "External";
        host.Events.Clear();

        host.History.CompleteNavigation("A", isBack: false);

        Assert.Equal("A", host.History.BackTarget);
        host.History.CompleteNavigation(host.History.BackTarget, isBack: true);
        Assert.Equal("A", host.SelectedPage);
        Assert.Equal("Home", host.History.BackTarget);
        AssertTrace(host, """
            select:External
            select:External
            activate:A:External:True:A
            refresh:A:True:A
            select:A
            activate:A:A:True:Home
            refresh:A:True:Home
            """);
    }

    /// <summary>Forward completion rereads selection for the append predicate instead of reusing its captured source.</summary>
    [Fact]
    public void ForwardAppendUsesSecondSelectionRead()
    {
        var host = new HistoryHost();
        int reads = 0;
        host.SelectionReader = page =>
        {
            if (++reads == 2)
            {
                host.SelectedPage = "B";
                host.Events.Add("selection:update:B");
                return "B";
            }
            return page;
        };

        host.History.CompleteNavigation("B", isBack: false);

        Assert.Equal("B", host.SelectedPage);
        Assert.False(host.History.CanGoBack);
        AssertTrace(host, """
            select:Home
            select:Home
            selection:update:B
            activate:B:B:False:-
            refresh:B:False:-
            """);
    }
}
