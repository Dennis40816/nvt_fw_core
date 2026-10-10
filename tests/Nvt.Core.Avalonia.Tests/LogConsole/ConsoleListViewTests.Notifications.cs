// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
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
    public Task UserScrollRaisesExactlyOneRequest() => Run(() =>
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
    public Task ViewportGrowthAfterUserScrollDoesNotRequestResume() => Run(() =>
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
    public Task GeometryNotificationPreservesPendingProjectionAnchor() => Run(() =>
    {
        using var initial = Many(100);
        using var trimmed = Many(90, 11);
        var view = new ConsoleListView { Projection = initial,
            ViewState = new ConsoleViewState().Pause(initial, new(11)) };
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
    public Task ResourceInvalidationAfterUserScrollDoesNotRequestState() => Run(() =>
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
    public Task DeferredScrollRequestsAcceptedInOrderPreserveLatestPosition() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(11)) };
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

    /// <summary>A projection refresh between two user scrolls keeps the earlier request valid for a later in-order acceptance.</summary>
    [Fact]
    public Task ProjectionRefreshBetweenDeferredRequestsKeepsLatestPosition() => Run(() =>
    {
        using var projection = Many(100);
        using var refreshed = Many(100);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(11)) };
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
    public Task HeightInvalidationGrowingExtentKeepsFollowingAtBottom(bool themeChange) => Run(() =>
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
    public Task ScrollRequestDuringMeasureIsDeliveredAfterMeasure() => Run(() =>
    {
        using var smaller = Many(3);
        var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
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
    [Fact]
    public Task ScrollRequestsDuringMeasureCoalesceToLatestAnchor() => Run(() =>
    {
        var content = new MeasureCallbackContent("first\nsecond\nthird\nfourth");
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
            Flush(window);
            var paused = Assert.IsType<ConsoleFollow.Paused>(Assert.Single(requests).Follow);
            Assert.Equal(new ConsoleRowId(41), paused.Anchor.RowId);
            Assert.Equal(5, paused.Anchor.PixelOffset);
        }
        finally { window.Close(); }
    });

    private static ConsoleRowId RowIdentity(Control row) => (ConsoleRowId)row.GetType()
        .GetProperty("RowId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(row)!;

    private static bool IsHostMeasuring(ConsoleListView view) => (bool)Host(view).GetType()
        .GetProperty("IsMeasuring", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Host(view))!;

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
