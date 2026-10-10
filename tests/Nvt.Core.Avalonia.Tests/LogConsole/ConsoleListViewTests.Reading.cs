// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed partial class ConsoleListViewTests
{
    /// <summary>A restore queued behind delivery cannot undo Pause while host acceptance is delayed.</summary>
    [Fact]
    public Task DeferredPauseConsumesRestoreQueuedByHostExpansion() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i, "first\nsecond\nthird\nfourth"))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            var inputCount = 0;
            content.OnRead = () =>
            {
                Assert.True(IsHostMeasuring(view));
                inputCount++;
                Scroll(view).Offset = new(0, 805);
            };
            view.ViewState = view.ViewState with { ExpandedIds = [new(1)] };
            view.Dispatcher.Post(() =>
            {
                Assert.Equal(1, inputCount);
                Assert.Empty(requests);
                view.ViewState = view.ViewState with { ExpandedIds = [new(1), new(40)] };
            }, DispatcherPriority.Loaded);
            Flush(window);
            var request = Assert.Single(requests);
            var paused = Assert.IsType<ConsoleFollow.Paused>(request.Follow);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
            Assert.Equal(925, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.PixelOffset, -Host(view).Children
                .Single(row => RowIdentity(row) == paused.Anchor.RowId).Bounds.Y);
            view.ViewState = request;
            Flush(window);
            Assert.Equal(925, Scroll(view).Offset.Y);
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>A following projection resolves an evicted pending position without a supplied successor.</summary>
    [Fact]
    public async Task DeferredFollowingScrollResolvesNearestSurvivingSuccessorWithRealProjector()
    {
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var store = new LogStore(maxEntries: 100);
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        Assert.Equal(1, store.Add(new LogWrite(LogLevel.Info, "test", content)));
        Assert.True(store.AddBatch(store.Generation, Enumerable.Range(2, 99)
            .Select(i => new LogWrite(LogLevel.Info, "test", new InMemoryLogTextContent($"Message {i}")))));
        using var snapshot = await store.CaptureLatestAsync(safety.Token);
        using var projection = ConsoleProjector.Project(snapshot, new(), new());
        Assert.True(store.AddBatch(store.Generation, Enumerable.Range(101, 60)
            .Select(i => new LogWrite(LogLevel.Info, "test", new InMemoryLogTextContent($"Message {i}")))));
        using var evicted = await store.CaptureLatestAsync(safety.Token);
        using var replacement = ConsoleProjector.Project(evicted, new(), new());
        await RunAsync(() =>
        {
            var view = new ConsoleListView { Projection = projection };
            var window = Window(view);
            try
            {
                Scroll(view).Offset = new(0, 0);
                var requests = new List<ConsoleViewState>();
                view.ViewStateRequested += (_, state) => requests.Add(state);
                BeginDeferredMeasureScroll(view, content, paused: false);
                Assert.Empty(requests);
                Assert.Equal(60, replacement.EvictedCount);
                Assert.Null(replacement.ResolvedAnchorId);
                view.Projection = replacement;
                Flush(window);
                var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
                Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
                Assert.Equal(0, Scroll(view).Offset.Y);
                Assert.Equal(new ConsoleRowId(61), FirstVisibleRow(view));
                Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
            }
            finally { window.Close(); }
        });
    }

    /// <summary>An explicit host resume supersedes scroll intent that has not been delivered.</summary>
    [Fact]
    public Task HostResumeReplacesDeferredScrollRequest() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content);
            view.ViewState = view.ViewState.Resume();
            Flush(window);
            Assert.Empty(requests);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Explicit host positioning consumes pending intent instead of requesting another pause.</summary>
    [Fact]
    public Task HostExplicitAnchorReplacesDeferredScrollRequest() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content);
            view.ViewState = view.ViewState.Pause(projection, new(61), pixelOffset: 5);
            Flush(window);
            Assert.Empty(requests);
            Assert.Equal(1265, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(61), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Following input cannot replace a pending user's row during projection trimming.</summary>
    [Fact]
    public Task DeferredFollowingScrollPreservesPositionAcrossProjectionReplacement() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        using var trimmed = Many(80, 21);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content, paused: false);
            view.Projection = trimmed;
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(405, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Host expansion preserves pending reading before the host has accepted Pause.</summary>
    [Fact]
    public Task DeferredFollowingScrollPreservesPositionAcrossHostExpansion() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i, "first\nsecond\nthird\nfourth"))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content, paused: false);
            view.ViewState = view.ViewState with { ExpandedIds = [new(1), new(40)] };
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(925, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Resource rebuilding preserves pending reading while the caller still says Following.</summary>
    [Fact]
    public Task DeferredFollowingScrollPreservesPositionAcrossResourceInvalidation() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content, paused: false);
            view.Resources["Nvt.Console.List.RowHeight"] = 21d;
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(845, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Geometry reaching the end cannot turn pending scroll-away intent into Resume.</summary>
    [Fact]
    public Task DeferredScrollViewportGrowthDoesNotRequestResume() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content);
            window.Height = 1500;
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Eviction remapping cannot bypass pending scroll delivery and resume following work.</summary>
    [Fact]
    public Task DeferredFollowingScrollEvictionRequestsVisibleSuccessor() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        using var trimmed = Project(Enumerable.Range(61, 40).Select(i => Row(i)), successor: new(61));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content, paused: false);
            view.Projection = trimmed;
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(0, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(61), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>A derived eviction remap cannot replace the user's queued Resume.</summary>
    [Fact]
    public Task DeferredResumeSurvivesProjectionEviction() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        using var trimmed = Project(Enumerable.Range(91, 10).Select(i => Row(i)), successor: new(91));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content);
            Scroll(view).Offset = new(0, Scroll(view).Extent.Height);
            view.Projection = trimmed;
            Flush(window);
            Assert.IsType<ConsoleFollow.Following>(Assert.Single(requests).Follow);
            Assert.Equal(0, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(91), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>An empty screen supplies no reading row for a pending Pause request.</summary>
    [Fact]
    public Task DeferredPauseIsDiscardedWhenProjectionBecomesEmpty() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        using var empty = Project([]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content);
            view.Projection = empty;
            Flush(window);
            Assert.Empty(requests);
        }
        finally { window.Close(); }
    });

    /// <summary>Returning to the end supersedes an already-issued pause even before the host accepts it.</summary>
    [Fact]
    public Task ScrollToEndSupersedesUnacceptedPauseRequest() => RunAsync(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            Scroll(view).Offset = new(0, 405);
            Flush(window);
            Scroll(view).Offset = new(0, Scroll(view).Extent.Height);
            Flush(window);
            Assert.Equal(2, requests.Count);
            Assert.IsType<ConsoleFollow.Following>(requests[1].Follow);
            view.ViewState = requests[0];
            Flush(window);
            view.ViewState = requests[1];
            Flush(window);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>An uninitialized external row order is an explicit restore, not a comparer failure.</summary>
    [Fact]
    public Task UninitializedRowOrderRestoresExplicitPosition() => RunAsync(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView
        {
            Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(1))
        };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            Scroll(view).Offset = new(0, 405);
            Flush(window);
            Scroll(view).Offset = new(0, 805);
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(requests[0].Follow);
            view.ViewState = requests[0] with
            {
                Follow = new ConsoleFollow.Paused(
                paused.Anchor with { RowOrder = default }, paused.PausedAt)
            };
            Flush(window);
            Assert.Equal(405, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(21), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

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
        await RunAsync(() =>
        {
            var id = initial.Rows[0].Id;
            var view = new ConsoleListView { Projection = initial, ViewState = new() { ExpandedIds = [id] } };
            view.ViewState = view.ViewState.Pause(initial, id, pixelOffset: 25);
            var window = Window(view, height: 40);
            try
            {
                var container = Container(view, id.Value);
                var before = (ConsoleReadingAnchor)Host(view).CaptureAnchor()!;
                view.Projection = replacement;
                Assert.Same(replacement.Rows[0], container.Row);
                initial.Dispose();
                Assert.Throws<ObjectDisposedException>(() => initial.Rows[0].TextContent.Read(0, new char[1]));
                Assert.Equal(before.TextOffset, container.MessageText.TextOffsetAt(before.PixelOffset));
                // No dispatcher pump or layout between either replacement and disposal.
                view.Projection = latest;
                Assert.Same(latest.Rows[0], container.Row);
                replacement.Dispose();
                Flush(window);
                Assert.Same(container, Container(view, id.Value));
                var after = (ConsoleReadingAnchor)Host(view).CaptureAnchor()!;
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
    public Task CoalescedTrimThenAppendPreservesOriginalPausedAnchor(bool resize) => RunAsync(() =>
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
            window.Width = resize ? 640 : window.Width;
            Flush(window);
            var anchor = (ConsoleReadingAnchor)Host(view).CaptureAnchor()!;
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
    public Task UserScrollAwayWhileProjectionWorkIsQueuedRequestsPause() => RunAsync(() =>
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
            Assert.NotNull(view.AttachmentSession!.PendingOperation);
            window.MouseWheel(new Point(600, 120), new Vector(0, 1));
            Flush(window);
            Assert.Contains(requests, state => state.Follow is ConsoleFollow.Paused);
            var pause = Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            Assert.True(Scroll(view).Offset.Y < Scroll(view).Extent.Height - Scroll(view).Viewport.Height);
            Assert.Equal(pause.Anchor.RowId, ((ConsoleReadingAnchor)Host(view).CaptureAnchor()!).RowId);
        }
        finally { window.Close(); }
    });

    /// <summary>The attachment lifetime guards repeated disposal and queued work.</summary>
    [Fact]
    public Task SessionDetachCanBeCalledTwiceConsecutively() => RunAsync(() =>
    {
        using var projection = Many(30);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.TimeMode = ConsoleTimeMode.Relative;
            var session = Assert.IsType<ConsoleListView.Session>(view.AttachmentSession);
            session.Detach();
            session.Detach();
            Assert.Null(session.PendingOperation);
            Flush(window);
        }
        finally { window.Close(); }
    });

    private static void AssertReadingPosition(ConsoleListView view, ConsoleFollow.Paused original)
    {
        var paused = Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
        var visible = (ConsoleReadingAnchor)Host(view).CaptureAnchor()!;
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
    public Task PausedUserScrollUpdatesAnchorBeforeExpansion(string member) => RunAsync(() =>
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
            Click(window, (Control)Element(row, member)); Flush(window);
            Assert.Contains(new ConsoleRowId(51), view.ViewState.ExpandedIds);
            AssertReadingPosition(view, original);
            Assert.Same(row, Container(view, 51));
            Click(window, (Control)Element(row, member)); Flush(window);
            Assert.Empty(view.ViewState.ExpandedIds);
            view.ViewState = view.ViewState with { IsExpanded = false }; Flush(window);
            view.ViewState = view.ViewState with { IsExpanded = true }; Flush(window);
            AssertReadingPosition(view, original);
            Assert.Equal([51L], view.ViewState.Selection);
        }
        finally { window.Close(); }
    });

    /// <summary>Reattachment uses the coordinates accepted after pausing rather than the pause-time row.</summary>
    [Fact]
    public Task PausedUserScrollUpdatesAnchorBeforeReattachment() => RunAsync(() =>
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
    public Task PausedUserScrollReplacesPendingRestoreAnchor() => RunAsync(() =>
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
            Assert.NotNull(view.AttachmentSession!.PendingOperation);
            Scroll(view).Offset = new(0, 1005); Flush(window);
            AssertReadingPosition(view, original);
        }
        finally { window.Close(); }
    });

    /// <summary>Captured input cancels at the first excursion outside the row even if it returns before release.</summary>
    [Theory]
    [InlineData("_message")]
    [InlineData("_arrow")]
    public Task PointerExcursionOutsideRowCancelsExpansion(string member) => RunAsync(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Element(row, member);
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
    public Task PointerReleaseAfterCaptureLossDoesNotToggle(string member) => RunAsync(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Element(row, member);
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
    public Task DetachingRowReleasesPointerCaptureAndCancelsExpansion(string member) => RunAsync(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Element(row, member);
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
    public Task PointerActivationTogglesExpansionAndDragDoesNot(string member, double movement, bool toggles) => RunAsync(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => { requests.Add(state); view.ViewState = state; };
            var row = Container(view, 1);
            var target = (Control)Element(row, member);
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
            AssertSecondActivationCollapsesRow(window, view, member, toggles, requests);
        }
        finally { window.Close(); }
    });

    /// <summary>The visible button takes its full count from the newest projection and real activation resumes.</summary>
    [Fact]
    public Task JumpButtonDisplaysProjectionNewMessageCountAndPointerRequestsResume() => RunAsync(() =>
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
            Assert.Equal(string.Format(view.TimeOptions.Culture,
                Resource<string>(view, "Nvt.Console.List.JumpToLatestMany"), 17), jump.Content);
            view.Projection = latest;
            Assert.Equal(string.Format(view.TimeOptions.Culture,
                Resource<string>(view, "Nvt.Console.List.JumpToLatestMany"), 23), jump.Content);
            Flush(window);
            Click(window, jump); Flush(window);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
            Assert.False(jump.IsVisible);
        }
        finally { window.Close(); }
    });

    private static void AssertSecondActivationCollapsesRow(Window window, ConsoleListView view,
        string member, bool toggles, List<ConsoleViewState> requests)
    {
        if (!toggles) return;
        Assert.Equal(60, Container(view, 1).Bounds.Height);
        Click(window, (Control)Element(Container(view, 1), member)); Flush(window);
        Assert.Equal(2, requests.Count);
        Assert.Empty(view.ViewState.ExpandedIds);
        Assert.Equal(20, Container(view, 1).Bounds.Height);
    }
}
