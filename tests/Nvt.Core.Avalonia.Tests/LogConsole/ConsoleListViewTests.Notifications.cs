// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed partial class ConsoleListViewTests
{
    /// <summary>Host and scroll-viewer notifications report one user intent.</summary>
    [Fact]
    public Task UserScrollRaisesExactlyOneRequest() => RunAsync(() =>
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
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(new ConsoleRowId(21), paused.Anchor.RowId);
            Assert.Equal(5, paused.Anchor.PixelOffset);
        }
        finally { window.Close(); }
    });

    /// <summary>A taller viewport reaching the end does not express a resume intent.</summary>
    [Fact]
    public Task ViewportGrowthAfterUserScrollDoesNotRequestResume() => RunAsync(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, Scroll(view).Extent.Height - Scroll(view).Viewport.Height - 40);
            Flush(window);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            window.Height += 100;
            Flush(window);
            Assert.Empty(requests);
            Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
        }
        finally { window.Close(); }
    });

    /// <summary>Geometry before queued restoration cannot substitute pre-restoration coordinates.</summary>
    [Fact]
    public Task GeometryNotificationPreservesPendingProjectionAnchor() => RunAsync(() =>
    {
        using var initial = Many(100);
        using var trimmed = Many(90, 11);
        var view = new ConsoleListView
        {
            Projection = initial,
            ViewState = new ConsoleViewState().Pause(initial, new(11))
        };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 605);
            Flush(window);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            view.Projection = trimmed;
            Scroll(view).RaiseScrollInvalidated(EventArgs.Empty);
            Flush(window);
            Assert.Empty(requests);
            Assert.Equal(405, Scroll(view).Offset.Y);
            Assert.Equal(-5, Container(view, 31).Bounds.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Refreshing resources after a user scroll does not request reading state.</summary>
    [Fact]
    public Task ResourceInvalidationAfterUserScrollDoesNotRequestState() => RunAsync(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 405);
            Flush(window);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            view.Resources["Nvt.Console.List.Level.Info"] = "Information";
            Flush(window);
            Assert.Empty(requests);
            Assert.Equal(405, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Deferred in-order acceptance never restores the earlier of two user positions.</summary>
    [Fact]
    public Task DeferredScrollRequestsAcceptedInOrderPreserveLatestPosition() => RunAsync(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView
        {
            Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(11))
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
            Assert.Equal(2, requests.Count);
            view.ViewState = requests[0];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            view.ViewState = requests[1];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            Assert.Equal(-5, Container(view, 41).Bounds.Y);
            Assert.Equal(new ConsoleRowId(41), Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow).Anchor.RowId);
        }
        finally { window.Close(); }
    });

    /// <summary>Each acceptance retires only its prefix of the queued user positions.</summary>
    [Fact]
    public Task ThreeScrollRequestsAcceptedInOrderPreserveLatestPosition() => RunAsync(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView
        {
            Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(11))
        };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            Scroll(view).Offset = new(0, 405);
            Flush(window);
            Scroll(view).Offset = new(0, 605);
            Flush(window);
            Scroll(view).Offset = new(0, 805);
            Flush(window);
            Assert.Equal(3, requests.Count);
            view.ViewState = requests[0];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            view.ViewState = requests[1];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            view.ViewState = requests[2];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Discarded history is treated as an explicit host reading position.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public Task ScrollRequestsBeyondHistoryLimitRestoreExplicitAnchor(int requestIndex) => RunAsync(() =>
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
            QueueScrollRequests(view, window, 40);
            Assert.Equal(40, requests.Count);
            view.ViewState = requests[requestIndex];
            Flush(window);
            Assert.Equal(205 + requestIndex * 20, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(11 + requestIndex), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>The oldest of the most recent anchors remains recognizable at the cap.</summary>
    [Fact]
    public Task OldestRetainedScrollRequestPreservesLatestPosition() => RunAsync(() =>
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
            QueueScrollRequests(view, window, 40);
            Assert.Equal(40, requests.Count);
            view.ViewState = requests[8];
            Flush(window);
            Assert.Equal(985, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(50), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>A caller may copy the frozen row order without changing anchor identity.</summary>
    [Fact]
    public Task CopiedEqualRowOrderPreservesLatestPosition() => RunAsync(() =>
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
            var order = paused.Anchor.RowOrder.ToArray().ToImmutableArray();
            Assert.False(order == paused.Anchor.RowOrder);
            view.ViewState = requests[0] with
            {
                Follow = new ConsoleFollow.Paused(
                paused.Anchor with { RowOrder = order }, paused.PausedAt)
            };
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>A different row order makes the host anchor an explicit restore.</summary>
    [Fact]
    public Task ChangedRowOrderRestoresExplicitPosition() => RunAsync(() =>
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
                paused.Anchor with { RowOrder = paused.Anchor.RowOrder.Reverse().ToImmutableArray() }, paused.PausedAt)
            };
            Flush(window);
            Assert.Equal(405, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(21), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Explicit host navigation retires even the later issued anchors.</summary>
    [Fact]
    public Task UnrequestedFollowRetiresAllIssuedAnchors() => RunAsync(() =>
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
            view.ViewState = view.ViewState.Pause(projection, new(11));
            Flush(window);
            view.ViewState = requests[1];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Expansion with the same Follow leaves later issued positions recognizable.</summary>
    [Fact]
    public Task ExpansionChangePreservesLaterIssuedAnchors() => RunAsync(() =>
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
            Scroll(view).Offset = new(0, 605);
            Flush(window);
            Scroll(view).Offset = new(0, 805);
            Flush(window);
            view.ViewState = requests[0];
            Flush(window);
            view.ViewState = view.ViewState with { ExpandedIds = [new(41)] };
            Flush(window);
            view.ViewState = requests[1];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>A projection refresh between two user scrolls keeps the earlier request valid for a later in-order acceptance.</summary>
    [Fact]
    public Task ProjectionRefreshBetweenDeferredRequestsKeepsLatestPosition() => RunAsync(() =>
    {
        using var projection = Many(100);
        using var refreshed = Many(100);
        var view = new ConsoleListView
        {
            Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(11))
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
            view.Projection = refreshed;
            Flush(window);
            view.ViewState = requests[0];
            Flush(window);
            Assert.Equal(805, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Estimate growth for an offscreen measured row keeps Following at the end.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HeightInvalidationGrowingExtentKeepsFollowingAtBottom(bool themeChange) => RunAsync(() =>
    {
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1, new string('i', 3000))));
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            Assert.DoesNotContain(Host(view).Children, row => RowIdentity(row) == new ConsoleRowId(1));
            var extent = Scroll(view).Extent.Height;
            InvalidateListResources(view, window, themeChange);
            Flush(window);
            Assert.True(Scroll(view).Extent.Height > extent);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
        }
        finally { window.Close(); }
    });

    /// <summary>A host may synchronously reproject when a measure-originated request is delivered.</summary>
    [Fact]
    public Task ScrollRequestDuringMeasureIsDeliveredAfterMeasure() => RunAsync(() =>
    {
        using var smaller = Many(3);
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var deliveryPhases = new List<bool>();
            view.ViewStateRequested += (_, state) =>
            {
                deliveryPhases.Add(IsHostMeasuring(view));
                view.ViewState = state;
                view.Projection = smaller;
            };
            content.OnRead = () => Scroll(view).Offset = new(0, 405);
            view.ViewState = new ConsoleViewState { ExpandedIds = [new(1)] }.Pause(projection, new(1));
            Flush(window);
            Assert.False(Assert.Single(deliveryPhases));
            Assert.Same(smaller, view.Projection);
        }
        finally { window.Close(); }
    });

    /// <summary>Multiple measure-originated requests coalesce to the latest reading coordinates.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ScrollRequestsDuringMeasureCoalesceToLatestAnchor(bool resize) => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            content.OnRead = () =>
            {
                Scroll(view).Offset = new(0, 405);
                Scroll(view).Offset = new(0, 805);
                Assert.Empty(requests);
            };
            view.ViewState = new ConsoleViewState { ExpandedIds = [new(1)] }.Pause(projection, new(1));
            ResizeForMeasure(window, resize);
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(new ConsoleRowId(41), paused.Anchor.RowId);
            Assert.Equal(5, paused.Anchor.PixelOffset);
            Assert.Equal(2060, Scroll(view).Extent.Height);
            Assert.Equal(865, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            Assert.Equal(-5, Container(view, 41).Bounds.Y);
            view.ViewState = requests[0];
            Flush(window);
            Assert.Equal(865, Scroll(view).Offset.Y);
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Deferred scroll delivery uses host expansion changes made after measuring.</summary>
    [Fact]
    public Task DeferredScrollRequestPreservesHostExpansionBeforeDelivery() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            Scroll(view).Offset = new(0, 0);
            requests.Clear();
            var inputCount = 0;
            content.OnRead = () =>
            {
                Assert.True(IsHostMeasuring(view));
                inputCount++;
                Scroll(view).Offset = new(0, 805);
            };
            view.ViewState = new ConsoleViewState { ExpandedIds = [new(1)] }.Pause(projection, new(1));
            Host(view).Measure(Scroll(view).Viewport);
            Assert.Equal(1, inputCount);
            Assert.Empty(requests);
            view.ViewState = view.ViewState with { ExpandedIds = [new(1), new(41)] };
            var expansion = view.ViewState.ExpandedIds;
            Flush(window);
            var request = Assert.Single(requests);
            Assert.Same(expansion, request.ExpandedIds);
            view.ViewState = request;
            Flush(window);
            Assert.Same(expansion, view.ViewState.ExpandedIds);
        }
        finally { window.Close(); }
    });

    /// <summary>Queued following work cannot undo scroll-away intent awaiting delivery.</summary>
    [Fact]
    public Task ScrollDuringFollowingMeasurePreservesLatestPosition() => RunAsync(() =>
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
            content.OnRead = () => Scroll(view).Offset = new(0, 805);
            view.ViewState = view.ViewState with { ExpandedIds = [new(1)] };
            Host(view).Measure(Scroll(view).Viewport);
            Assert.Empty(requests);
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(865, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Projection replacement before delivery remaps the request to the live screen.</summary>
    [Fact]
    public Task DeferredScrollRequestUsesReplacementProjection() => RunAsync(() =>
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
            BeginDeferredMeasureScroll(view, content);
            Assert.Empty(requests);
            view.Projection = trimmed;
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(405, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>A resume command supersedes pending scroll intent immediately.</summary>
    [Fact]
    public Task JumpToLatestReplacesDeferredScrollRequest() => RunAsync(() =>
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
            Assert.Empty(requests);
            view.JumpToLatest();
            Assert.IsType<ConsoleFollow.Following>(Assert.Single(requests).Follow);
            view.ViewState = requests[0];
            Flush(window);
            Assert.Single(requests);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>A toggle replaces pending scroll intent with one immediate combined request.</summary>
    [Fact]
    public Task ToggleReplacesDeferredScrollRequest() => RunAsync(() =>
    {
        using var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i, "first\nsecond"))
            .Prepend(Row(1) with { TextContent = content }));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Scroll(view).Offset = new(0, 0);
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            BeginDeferredMeasureScroll(view, content);
            Host(view).Arrange(new Rect(Scroll(view).Viewport));
            Assert.Empty(requests);
            var arrow = Container(view, 41).GetVisualChildren().OfType<TextBlock>().Last();
            var point = arrow.TranslatePoint(new Point(arrow.Bounds.Width / 2, arrow.Bounds.Height / 2), window)!.Value;
            using var pointer = new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true);
            arrow.RaiseEvent(new PointerPressedEventArgs(arrow, pointer, window, point, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, 1));
            arrow.RaiseEvent(new PointerReleasedEventArgs(arrow, pointer, window, point, 0,
                new PointerPointProperties(), KeyModifiers.None, MouseButton.Left));
            var request = Assert.Single(requests);
            Assert.Contains(new ConsoleRowId(41), request.ExpandedIds);
            Assert.Equal(new ConsoleRowId(41), Assert.IsType<ConsoleFollow.Paused>(request.Follow).Anchor.RowId);
            view.ViewState = request;
            Flush(window);
            Assert.Single(requests);
            Assert.Equal(new ConsoleRowId(41), FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    /// <summary>Removing the projection leaves no meaningful scroll state to deliver.</summary>
    [Fact]
    public Task DeferredScrollRequestIsDiscardedWhenProjectionClears() => RunAsync(() =>
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
            view.Projection = null;
            Flush(window);
            Assert.Empty(requests);
        }
        finally { window.Close(); }
    });

    /// <summary>Detachment cancels pending user scroll delivery with the attachment lifetime.</summary>
    [Fact]
    public Task DeferredScrollRequestIsCancelledOnDetach() => RunAsync(() =>
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
            window.Content = null;
            Flush(window);
            Assert.Empty(requests);
        }
        finally { window.Close(); }
    });

    /// <summary>Delivery evaluates reaching the end after the original measure-time scroll.</summary>
    [Fact]
    public Task DeferredScrollToEndRequestsResume() => RunAsync(() =>
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
            Scroll(view).Offset = new(0, Scroll(view).Extent.Height);
            Assert.Empty(requests);
            Flush(window);
            Assert.IsType<ConsoleFollow.Following>(Assert.Single(requests).Follow);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Scroll-away before delivery supersedes a transient arrival at the end.</summary>
    [Fact]
    public Task DeferredScrollAwayReplacesPendingResume() => RunAsync(() =>
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
            Scroll(view).Offset = new(0, Scroll(view).Extent.Height);
            Scroll(view).Offset = new(0, 605);
            Assert.Empty(requests);
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(605, Scroll(view).Offset.Y);
            Assert.Equal(new ConsoleRowId(28), FirstVisibleRow(view));
            Assert.Equal(paused.Anchor.RowId, FirstVisibleRow(view));
        }
        finally { window.Close(); }
    });

    private static void BeginDeferredMeasureScroll(ConsoleListView view, MeasureCallbackContent content, bool paused = true)
    {
        var inputCount = 0;
        content.OnRead = () =>
        {
            Assert.True(IsHostMeasuring(view));
            inputCount++;
            Scroll(view).Offset = new(0, 805);
        };
        var state = new ConsoleViewState { ExpandedIds = [new(1)] };
        view.ViewState = paused ? state.Pause(view.Projection!, new(1)) : state;
        Host(view).Measure(Scroll(view).Viewport);
        Assert.Equal(1, inputCount);
    }

    private static void ResizeForMeasure(Window window, bool resize)
    {
        if (resize) window.Width = 960;
    }

    private static ConsoleRowId FirstVisibleRow(ConsoleListView view) => RowIdentity(Host(view).Children
        .Where(row => row.Bounds.Bottom > 0).OrderBy(row => row.Bounds.Y).First());

    private static void QueueScrollRequests(ConsoleListView view, Window window, int count)
    {
        for (var index = 0; index < count; index++)
        {
            Scroll(view).Offset = new(0, 205 + index * 20);
            Flush(window);
        }
    }

    private static ConsoleRowId RowIdentity(Control row) => ((ConsoleRowPresenter)row).RowId!.Value;

    private static bool IsHostMeasuring(ConsoleListView view) => Host(view).IsMeasuring;

    private static void InvalidateListResources(ConsoleListView view, Window window, bool themeChange)
    {
        if (themeChange) window.RequestedThemeVariant = ThemeVariant.Dark;
        else view.Resources["Nvt.Font.Body.Size"] = 14d;
    }

    // UI thread only. The one-shot callback injects input at an actual text read during measure.
    private sealed class MeasureCallbackContent(string text) : ILogTextContent
    {
        private Action? _onRead;
        internal Action? OnRead { set => _onRead = value; }
        public int Length => text.Length;
        public int ResidentCharacterCount => text.Length;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            var callback = _onRead;
            _onRead = null;
            callback?.Invoke();
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose() { }
    }
}
