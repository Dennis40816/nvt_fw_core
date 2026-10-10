// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed partial class ConsoleListViewTests
{
    /// <summary>Append and direct scrolling share one layout cycle; the user pause wins.</summary>
    [Fact]
    public Task ScrolledAppendInSameLayoutCycleRequestsPauseWithoutFollowing() => Run(() =>
    {
        using var initial = Many(100);
        using var appended = Many(110);
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            view.Projection = appended;
            Scroll(view).Offset = new(0, 400);
            Flush(window);
            Assert.Contains(requests, state => state.Follow is ConsoleFollow.Paused);
            Assert.Equal(400, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>A measured height correction does not hide user scrolling in the same cycle.</summary>
    [Fact]
    public Task ScrolledHeightCorrectionInSameLayoutCycleRequestsPauseWithoutFollowing() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i =>
            Row(i, i == 20 ? "first\nsecond\nthird" : "Message")));
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(20)] } };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var extent = Scroll(view).Extent.Height;
            Scroll(view).Offset = new(0, 400);
            Flush(window);
            Assert.True(Scroll(view).Extent.Height > extent);
            Assert.Contains(requests, state => state.Follow is ConsoleFollow.Paused);
            Assert.True(Scroll(view).Offset.Y < Scroll(view).Extent.Height - Scroll(view).Viewport.Height);
        }
        finally { window.Close(); }
    });

    /// <summary>Correcting an expanded overscan row preserves the visible row inset.</summary>
    [Fact]
    public Task MeasureOverrideOverscanExpandedRowCorrectionPreservesFirstVisibleRowPosition() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i =>
            Row(i, i == 40 ? "first\nsecond\nthird" : "Message")));
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(41), pixelOffset: 5) };
        var window = Window(view);
        try
        {
            var before = Container(view, 41).Bounds.Y;
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 1405); Flush(window);
            view.ViewState = view.ViewState with { ExpandedIds = [new(40)] }; Flush(window);
            Assert.DoesNotContain(Host(view).Children, row => Id(row).Value == 40);
            Scroll(view).Offset = new(0, 805);
            Flush(window);
            Assert.Equal(60, Container(view, 40).Bounds.Height);
            Assert.Equal(before, Container(view, 41).Bounds.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>A paused user scroll requests fresh coordinates before overscan correction can consume its offset.</summary>
    [Fact]
    public Task ScrolledPausedHeightCorrectionInSameLayoutCycleRequestsUpdatedAnchor() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i =>
            Row(i, i == 40 ? "first\nsecond\nthird" : "Message")));
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState { ExpandedIds = [new(40)] }.Pause(projection, new(71), pixelOffset: 5) };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            Scroll(view).Offset = new(0, 805);
            Flush(window);
            Assert.Contains(requests, state => state.Follow is ConsoleFollow.Paused paused
                && paused.Anchor.RowId == new ConsoleRowId(41));
            Assert.Equal(new ConsoleRowId(41), Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow).Anchor.RowId);
            Assert.Equal(-5, Container(view, 41).Bounds.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Programmatic scrolling during height correction preserves following.</summary>
    [Fact]
    public Task MeasureOverrideFollowingHeightCorrectionDoesNotRequestPause() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i =>
            Row(i, i == 95 ? "first\nsecond\nthird" : "Message")));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            view.ViewState = view.ViewState with { ExpandedIds = [new(95)] };
            Flush(window);
            Assert.Equal(60, Container(view, 95).Bounds.Height);
            Assert.Empty(requests);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>An unrelated state edit retains the live position while acceptance is deferred.</summary>
    [Fact]
    public Task ViewStateUnrelatedChangeWithStaleAnchorPreservesLiveOffset() => Run(() =>
    {
        using var projection = Many(100);
        var stale = new ConsoleViewState().Pause(projection, new(21));
        var view = new ConsoleListView { Projection = projection, ViewState = stale };
        var window = Window(view);
        try
        {
            ConsoleViewState? requested = null;
            view.ViewStateRequested += (_, state) => requested = state;
            Scroll(view).Offset = new(0, 805); Flush(window);
            Assert.Equal(new ConsoleRowId(41), Assert.IsType<ConsoleFollow.Paused>(requested!.Follow).Anchor.RowId);
            view.ViewState = stale with { ExpandedIds = [new(90)] };
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>A caller explicitly changing Follow can navigate to a different anchor.</summary>
    [Fact]
    public Task ViewStateExplicitFollowAnchorChangeRestoresRequestedPosition() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.ViewState = view.ViewState.Pause(projection, new(41), pixelOffset: 5);
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Replacing the template drops all borrowed rows from its old host.</summary>
    [Fact]
    public Task OnApplyTemplateReplacedHostReleasesBorrowedRows() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var host = Host(view);
            var row = host.Children[0];
            var source = Member(host, "ItemsSource")!;
            var template = view.Template;
            Assert.Same(projection, Member(source, "Projection"));
            view.Template = null;
            view.ApplyTemplate();
            view.Template = template;
            view.ApplyTemplate();
            Flush(window);
            Assert.NotSame(host, Host(view));
            Assert.Null(Member(source, "Projection"));
            Assert.Empty(host.Children);
            Assert.Null(Member(row, "_input"));
        }
        finally { window.Close(); }
    });
}
