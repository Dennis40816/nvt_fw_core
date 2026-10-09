// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed partial class ConsoleListViewTests
{
    private static void Click(Window window, Control target)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    /// <summary>Real projector leases are replaced before the caller releases the preceding projection.</summary>
    [Fact]
    public async Task ProjectionReplacementRebindsLeasesBeforeCoalescedPausedUpdates()
    {
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var store = new LogStore();
        store.Add(LogLevel.Info, "test", "first\nsecond\nthird\nfourth\nfifth");
        using var snapshot = await store.CaptureLatestAsync(safety.Token);
        using var initial = ConsoleProjector.Project(snapshot, new(), new());
        using var replacement = ConsoleProjector.Project(snapshot, new(), new());
        using var latest = ConsoleProjector.Project(snapshot, new(), new());
        await Run(() =>
        {
            var id = initial.Rows[0].Id;
            var view = new ConsoleListView { Projection = initial, ViewState = new() { ExpandedIds = [id] } };
            view.ViewState = view.ViewState.Pause(initial, id, pixelOffset: 25);
            var window = Window(view, height: 40);
            try
            {
                var container = Container(view, id.Value);
                var before = (ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!;
                view.Projection = replacement;
                Assert.Same(replacement.Rows[0], Member(Member(container, "_input")!, "Row"));
                initial.Dispose();
                Assert.Throws<ObjectDisposedException>(() => initial.Rows[0].TextContent.Read(0, new char[1]));
                Assert.Equal(before.TextOffset, Call(Member(container, "_message")!, "TextOffsetAt", before.PixelOffset));
                // No dispatcher pump or layout between either replacement and disposal.
                view.Projection = latest;
                Assert.Same(latest.Rows[0], Member(Member(container, "_input")!, "Row"));
                replacement.Dispose();
                Flush(window);
                Assert.Same(container, Container(view, id.Value));
                var after = (ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!;
                Assert.Equal(before.RowId, after.RowId);
                Assert.Equal(before.TextOffset, after.TextOffset);
                Assert.Equal(before.PixelOffset, after.PixelOffset);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>A front trim and append before layout preserve row 31 and its five-DIP inset.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CoalescedTrimThenAppendPreservesOriginalPausedAnchor(bool resize) => Run(() =>
    {
        using var initial = Many(100);
        using var trimmed = Many(90, 11);
        using var appended = Many(100, 11);
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 605); Flush(window);
            var container = Container(view, 31);
            Assert.Equal(-5, container.Bounds.Y);
            view.Projection = trimmed;
            view.Projection = appended;
            if (resize) window.Width = 640;
            Flush(window);
            var anchor = (ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!;
            Assert.Equal(new ConsoleRowId(31), anchor.RowId);
            Assert.Equal(5, anchor.PixelOffset);
            Assert.Equal(405, Scroll(view).Offset.Y);
            Assert.Same(container, Container(view, 31));
            Assert.Equal(-5, container.Bounds.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Wheel input delivered during queued follow work is accepted as a pause intent.</summary>
    [Fact]
    public Task UserScrollAwayWhileProjectionWorkIsQueuedRequestsPause() => Run(() =>
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
            Assert.NotNull(Member(Member(view, "_session")!, "_pending"));
            window.MouseWheel(new Point(600, 120), new Vector(0, 1));
            Flush(window);
            Assert.Contains(requests, state => state.Follow is ConsoleFollow.Paused);
            var pause = Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            Assert.True(Scroll(view).Offset.Y < Scroll(view).Extent.Height - Scroll(view).Viewport.Height);
            Assert.Equal(pause.Anchor.RowId, ((ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!).RowId);
        }
        finally { window.Close(); }
    });

    /// <summary>The attachment lifetime guards repeated disposal and queued work.</summary>
    [Fact]
    public Task SessionDisposeCanBeCalledTwiceConsecutively() => Run(() =>
    {
        using var projection = Many(30);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.TimeMode = ConsoleTimeMode.Relative;
            var session = Assert.IsAssignableFrom<IDisposable>(Member(view, "_session"));
            session.Dispose();
            session.Dispose();
            Assert.Null(Member(session, "_pending"));
            Flush(window);
        }
        finally { window.Close(); }
    });

    private static void AssertReadingPosition(ConsoleListView view, ConsoleFollow.Paused original)
    {
        var paused = Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
        var visible = (ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!;
        Assert.Equal(1005, Scroll(view).Offset.Y);
        Assert.Equal(new ConsoleRowId(51), paused.Anchor.RowId);
        Assert.Equal(51, paused.Anchor.Sequence);
        Assert.Equal(5, paused.Anchor.PixelOffset);
        Assert.Equal(visible.RowId, paused.Anchor.RowId);
        Assert.Equal(visible.TextOffset, paused.Anchor.TextOffset);
        Assert.Equal(visible.PixelOffset, paused.Anchor.PixelOffset);
        Assert.Equal(original.PausedAt, paused.PausedAt);
        Assert.Equal(original.Anchor.Generation, paused.Anchor.Generation);
        Assert.Equal(original.Anchor.ThroughSequence, paused.Anchor.ThroughSequence);
        Assert.Equal(original.Anchor.RowOrder, paused.Anchor.RowOrder);
    }

    /// <summary>Paused scrolling changes only reading coordinates; either toggle target keeps the latest position.</summary>
    [Theory]
    [InlineData("_message")]
    [InlineData("_arrow")]
    public Task PausedUserScrollUpdatesAnchorBeforeExpansion(string member) => Run(() =>
    {
        using var initial = Project(Enumerable.Range(1, 100).Select(i => Row(i, "first\nsecond\nthird")));
        using var appended = Project(Enumerable.Range(1, 110).Select(i => Row(i, "first\nsecond\nthird")));
        var view = new ConsoleListView { Projection = initial, ViewState = new() { Selection = [1] } };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 605); Flush(window);
            var original = Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            Assert.Equal(new ConsoleRowId(31), original.Anchor.RowId);
            view.Projection = appended; Flush(window);
            Scroll(view).Offset = new(0, 1005); Flush(window);
            AssertReadingPosition(view, original);
            var row = Container(view, 51);
            Click(window, (Control)Member(row, member)!); Flush(window);
            Assert.Contains(new ConsoleRowId(51), view.ViewState.ExpandedIds);
            AssertReadingPosition(view, original);
            Assert.Same(row, Container(view, 51));
            Click(window, (Control)Member(row, member)!); Flush(window);
            Assert.Empty(view.ViewState.ExpandedIds);
            view.ViewState = view.ViewState with { IsExpanded = false }; Flush(window);
            view.ViewState = view.ViewState with { IsExpanded = true }; Flush(window);
            AssertReadingPosition(view, original);
            Assert.Equal([1L], view.ViewState.Selection);
        }
        finally { window.Close(); }
    });

    /// <summary>Reattachment uses the coordinates accepted after pausing rather than the pause-time row.</summary>
    [Fact]
    public Task PausedUserScrollUpdatesAnchorBeforeReattachment() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 605); Flush(window);
            var original = Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            Assert.Equal(new ConsoleRowId(31), original.Anchor.RowId);
            Scroll(view).Offset = new(0, 1005); Flush(window);
            AssertReadingPosition(view, original);
            var accepted = view.ViewState;
            window.Content = null; Flush(window);
            window.Content = view; Flush(window);
            Assert.Same(accepted, view.ViewState);
            AssertReadingPosition(view, original);
        }
        finally { window.Close(); }
    });

    /// <summary>A user scroll supersedes an already queued restoration instead of being undone by it.</summary>
    [Fact]
    public Task PausedUserScrollReplacesPendingRestoreAnchor() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 605); Flush(window);
            var original = Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            view.TimeMode = ConsoleTimeMode.Relative;
            Assert.NotNull(Member(Member(view, "_session")!, "_pending"));
            Scroll(view).Offset = new(0, 1005); Flush(window);
            AssertReadingPosition(view, original);
        }
        finally { window.Close(); }
    });

    /// <summary>Captured input cancels at the first excursion outside the row even if it returns before release.</summary>
    [Theory]
    [InlineData("_message")]
    [InlineData("_arrow")]
    public Task PointerExcursionOutsideRowCancelsExpansion(string member) => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Member(row, member)!;
            IPointer? pointer = null;
            row.PointerPressed += (_, e) => pointer = e.Pointer;
            var start = target.TranslatePoint(new Point(target.Bounds.Width / 2, 10), window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            Assert.Same(row, pointer!.Captured);
            var outside = start + new Vector(0, 40);
            Assert.False(new Rect(row.Bounds.Size).Contains(window.TranslatePoint(outside, row)!.Value));
            window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
            Assert.Null(pointer.Captured);
            window.MouseMove(start, RawInputModifiers.LeftMouseButton);
            window.MouseUp(start, MouseButton.Left); Flush(window);
            Assert.Empty(requests);
            Assert.Empty(view.ViewState.ExpandedIds);
            Assert.Null(pointer.Captured);
        }
        finally { window.Close(); }
    });

    /// <summary>Losing capture cancels the pending activation before a release at the original press point.</summary>
    [Theory]
    [InlineData("_message")]
    [InlineData("_arrow")]
    public Task PointerReleaseAfterCaptureLossDoesNotToggle(string member) => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Member(row, member)!;
            IPointer? pointer = null;
            row.PointerPressed += (_, e) => pointer = e.Pointer;
            var start = target.TranslatePoint(new Point(target.Bounds.Width / 2, 10), window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            Assert.Same(row, pointer!.Captured);
            pointer.Capture(null);
            window.MouseUp(start, MouseButton.Left); Flush(window);
            Assert.Empty(requests);
            Assert.Empty(view.ViewState.ExpandedIds);
            Assert.Null(pointer.Captured);
        }
        finally { window.Close(); }
    });

    /// <summary>Detach releases a live capture and a later release cannot activate a recycled row.</summary>
    [Theory]
    [InlineData("_message")]
    [InlineData("_arrow")]
    public Task DetachingRowReleasesPointerCaptureAndCancelsExpansion(string member) => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Member(row, member)!;
            IPointer? pointer = null;
            row.PointerPressed += (_, e) => pointer = e.Pointer;
            var start = target.TranslatePoint(new Point(target.Bounds.Width / 2, 10), window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            Assert.Same(row, pointer!.Captured);
            window.Content = null; Flush(window);
            Assert.Null(pointer.Captured);
            window.Content = view; Flush(window);
            window.MouseUp(start, MouseButton.Left); Flush(window);
            Assert.Empty(requests);
            Assert.Empty(view.ViewState.ExpandedIds);
            Assert.Null(pointer.Captured);
        }
        finally { window.Close(); }
    });

    /// <summary>Message and arrow input toggles at the threshold; longer movement cancels even after returning.</summary>
    [Theory]
    [InlineData("_message", 0, true)]
    [InlineData("_arrow", 0, true)]
    [InlineData("_message", 4, true)]
    [InlineData("_arrow", 4, true)]
    [InlineData("_message", 4.01, false)]
    [InlineData("_arrow", 4.01, false)]
    public Task PointerActivationTogglesExpansionAndDragDoesNot(string member, double movement, bool toggles) => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Member(row, member)!;
            IPointer? pointer = null;
            row.PointerPressed += (_, e) => pointer = e.Pointer;
            var start = target.TranslatePoint(new Point(target.Bounds.Width / 2, 10), window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            Assert.Same(row, pointer!.Captured);
            window.MouseMove(start + new Vector(movement, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(start, RawInputModifiers.LeftMouseButton);
            window.MouseUp(start, MouseButton.Left);
            Assert.Null(pointer.Captured);
            Flush(window);
            Assert.Equal(toggles ? 1 : 0, requests.Count);
            Assert.Equal(toggles, view.ViewState.ExpandedIds.Contains(new(1)));
            if (toggles)
            {
                Assert.Equal(60, Container(view, 1).Bounds.Height);
                Click(window, (Control)Member(Container(view, 1), member)!); Flush(window);
                Assert.Equal(2, requests.Count);
                Assert.Empty(view.ViewState.ExpandedIds);
                Assert.Equal(20, Container(view, 1).Bounds.Height);
            }
        }
        finally { window.Close(); }
    });

    /// <summary>The visible button takes its full count from the newest projection and real activation resumes.</summary>
    [Fact]
    public Task JumpButtonDisplaysProjectionNewMessageCountAndPointerRequestsResume() => Run(() =>
    {
        using var initial = Many(100);
        using var next = Project(Enumerable.Range(1, 100).Select(i => Row(i)), newCount: 17);
        using var latest = Project(Enumerable.Range(1, 100).Select(i => Row(i)), newCount: 23);
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 400); Flush(window);
            var jump = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Jump");
            view.Projection = next; Flush(window);
            Assert.True(jump.IsVisible);
            Assert.Equal("Jump to latest (17 new messages)", jump.Content);
            view.Projection = latest;
            Assert.Equal("Jump to latest (23 new messages)", jump.Content);
            Flush(window);
            Click(window, jump); Flush(window);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
            Assert.False(jump.IsVisible);
        }
        finally { window.Close(); }
    });
}
