// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// Pixel-scrolling recycling items host. Projection rows are borrowed, never cloned or owned.
internal sealed class ConsoleItemsHost : Panel, ILogicalScrollable
{
    // UI thread only. This source identity survives every projection and every scroll.
    internal sealed class RowSource : IReadOnlyList<ConsoleRow>
    {
        internal ConsoleProjection? Projection { get; set; }
        public int Count => Projection?.Rows.Length ?? 0;
        public ConsoleRow this[int index] => Projection!.Rows[index];
        public IEnumerator<ConsoleRow> GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // All host state, including the source, caches, pool and scroll geometry, is UI-thread only.
    private ConsoleListView? _view;
    private readonly Dictionary<ConsoleRowId, ConsoleRowPresenter> _realized = [];
    private readonly Stack<ConsoleRowPresenter> _pool = [];
    private readonly Dictionary<ConsoleRowId, HeightMeasurement> _heights = [];
    private double[] _tops = [0];
    private Size _viewport;
    private Vector _offset;
    internal RowSource ItemsSource { get; } = new();
    internal event Action<ConsoleRowId>? ToggleRequested;

    public ConsoleItemsHost() { ClipToBounds = true; }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public bool IsLogicalScrollEnabled => true;
    private double RowHeight => UiResourceResolver.GetDouble(this, "Nvt.Console.List.RowHeight");
    public Size ScrollSize => new(0, RowHeight);
    public Size PageScrollSize => new(0, Math.Max(RowHeight, Viewport.Height - RowHeight));
    public Size Extent => new(Viewport.Width, _tops[^1]);
    public Size Viewport => _viewport;
    public Vector Offset
    {
        get => _offset;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            var next = new Vector(0, Math.Clamp(value.Y, 0, Math.Max(0, Extent.Height - Viewport.Height)));
            if (next == _offset) return;
            _offset = next;
            InvalidateMeasure();
            RaiseScrollInvalidated(EventArgs.Empty);
        }
    }
    public event EventHandler? ScrollInvalidated;
    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);
    public bool BringIntoView(Control target, Rect targetRect) => false;
    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;
    internal bool AtEnd => Extent.Height - Viewport.Height - Offset.Y <= 1;
    internal void ScrollToEnd() => SetScrollOffset(new(0, Math.Max(0, Extent.Height - Viewport.Height)));
    private void SetScrollOffset(Vector offset)
    {
        if (_view is { } view) view.ScrollProgrammatically(() => Offset = offset);
        else Offset = offset;
    }

    internal void Synchronize(ConsoleListView view)
    {
        Dispatcher.UIThread.VerifyAccess();
        _view = view;
        ItemsSource.Projection = view.Projection;
        var retained = ItemsSource.Select(row => row.Id).ToHashSet();
        foreach (var id in _heights.Keys.Where(id => !retained.Contains(id)).ToArray()) _heights.Remove(id);
        foreach (var id in _realized.Keys.Where(id => !retained.Contains(id)).ToArray()) Recycle(id);
        // Rebind borrowed content before returning: the caller may immediately dispose the old leases.
        foreach (var row in ItemsSource)
            if (_realized.TryGetValue(row.Id, out var container))
                container.Configure(row, view, id => ToggleRequested?.Invoke(id));
        RebuildHeights();
        InvalidateMeasure();
        RaiseScrollInvalidated(EventArgs.Empty);
    }

    private double MessageWidth => ConsoleRowPresenter.GetMessageWidth((Control?)_view ?? this, Viewport.Width,
        _view?.TimeMode != ConsoleTimeMode.Hidden);

    private void RebuildHeights()
    {
        _tops = new double[ItemsSource.Count + 1];
        for (var i = 0; i < ItemsSource.Count; i++)
        {
            var row = ItemsSource[i];
            var height = RowHeight;
            if (_view!.ViewState.ExpandedIds.Contains(row.Id))
                height = _heights.TryGetValue(row.Id, out var measured) && measured.Version == row.TextVersion
                    && measured.Width == MessageWidth ? measured.Height
                    : ConsoleTextPresenter.Estimate(row.TextContent.Length, MessageWidth, _view);
            _tops[i + 1] = _tops[i] + height;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Dispatcher.UIThread.VerifyAccess();
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Bounds.Width;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : Bounds.Height;
        var viewport = new Size(Math.Max(0, width), Math.Max(0, height));
        var wasAtEnd = AtEnd;
        var widthChanged = _viewport.Width != viewport.Width;
        var reading = widthChanged && _view?.ViewState.Follow is ConsoleFollow.Paused
            ? _view.PendingReadingAnchor ?? CaptureAnchor() : null;
        var viewportChanged = _viewport != viewport;
        _viewport = viewport;
        if (widthChanged)
        {
            _heights.Clear();
            RebuildHeights();
            if (reading is not null) RestoreAnchor(reading, ItemsSource.Projection?.ResolvedAnchorId);
        }
        if (viewportChanged) RaiseScrollInvalidated(EventArgs.Empty);
        if (_view is null || ItemsSource.Count == 0 || height <= 0) { ClearRealized(); return viewport; }
        for (var pass = 0; pass < 3; pass++)
        {
            var first = Math.Max(0, FindRow(Offset.Y) - 1);
            var last = Math.Min(ItemsSource.Count - 1, FindRow(Offset.Y + height) + 1);
            var needed = new HashSet<ConsoleRowId>();
            var changed = false;
            for (var i = first; i <= last; i++)
            {
                var row = ItemsSource[i];
                needed.Add(row.Id);
                if (!_realized.TryGetValue(row.Id, out var container))
                {
                    container = _pool.TryPop(out var recycled) ? recycled : new ConsoleRowPresenter();
                    _realized.Add(row.Id, container);
                    Children.Add(container);
                }
                container.Configure(row, _view, id => ToggleRequested?.Invoke(id));
                container.Measure(new(width, double.PositiveInfinity));
                if (_view.ViewState.ExpandedIds.Contains(row.Id)
                    && Math.Abs(container.DesiredSize.Height - (_tops[i + 1] - _tops[i])) > .01)
                {
                    _heights[row.Id] = new(row.TextVersion, MessageWidth, container.DesiredSize.Height);
                    changed = true;
                }
            }
            foreach (var id in _realized.Keys.Where(id => !needed.Contains(id)).ToArray()) Recycle(id);
            while (_pool.Count > Math.Max(2, _realized.Count)) _pool.Pop();
            if (!changed) break;
            RebuildHeights();
            if (wasAtEnd && _view.ViewState.Follow is ConsoleFollow.Following) ScrollToEnd();
            RaiseScrollInvalidated(EventArgs.Empty);
        }
        if (reading is not null) RestoreAnchor(reading, ItemsSource.Projection?.ResolvedAnchorId);
        else if (wasAtEnd && _view.ViewState.Follow is ConsoleFollow.Following) ScrollToEnd();
        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = Math.Max(0, FindRow(Offset.Y) - 1); i < ItemsSource.Count; i++)
        {
            if (_tops[i] > Offset.Y + finalSize.Height + RowHeight) break;
            if (!_realized.TryGetValue(ItemsSource[i].Id, out var container)) continue;
            var y = _tops[i] - Offset.Y;
            container.SetVisibleSlice(Math.Max(0, -y), Math.Min(_tops[i + 1] - _tops[i], finalSize.Height - y));
            container.Arrange(new Rect(0, y, finalSize.Width, _tops[i + 1] - _tops[i]));
        }
        return finalSize;
    }

    private int FindRow(double y)
    {
        var index = Array.BinarySearch(_tops, y);
        if (index < 0) index = ~index - 1;
        return Math.Clamp(index, 0, Math.Max(0, ItemsSource.Count - 1));
    }

    internal ConsoleReadingAnchor? CaptureAnchor()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ItemsSource.Count == 0 || ItemsSource.Projection is not { } projection) return null;
        var index = FindRow(Offset.Y);
        var row = ItemsSource[index];
        var pixel = Offset.Y - _tops[index];
        var text = _realized.TryGetValue(row.Id, out var container) ? container.TextOffsetAt(pixel) : 0;
        return new(row.Id, row.LastSequence, text, pixel, projection.Generation, projection.LastSequence, []);
    }

    internal ConsoleReadingAnchor? RestoreAnchor(ConsoleReadingAnchor anchor, ConsoleRowId? successor)
    {
        Dispatcher.UIThread.VerifyAccess();
        var index = -1;
        for (var i = 0; i < ItemsSource.Count; i++) if (ItemsSource[i].Id == anchor.RowId) { index = i; break; }
        var same = index >= 0;
        if (!same && successor is { } id)
            for (var i = 0; i < ItemsSource.Count; i++) if (ItemsSource[i].Id == id) { index = i; break; }
        if (index < 0) return null;
        var pixel = same ? Math.Min(anchor.PixelOffset, Math.Max(0, _tops[index + 1] - _tops[index] - 1)) : 0;
        if (same && anchor.TextOffset > 0 && _realized.TryGetValue(ItemsSource[index].Id, out var container))
            pixel = container.PixelOffsetAt(anchor.TextOffset, pixel);
        SetScrollOffset(new(0, _tops[index] + pixel));
        return anchor with { RowId = ItemsSource[index].Id, Sequence = ItemsSource[index].LastSequence,
            TextOffset = same ? anchor.TextOffset : 0, PixelOffset = pixel };
    }

    internal void Release()
    {
        Dispatcher.UIThread.VerifyAccess();
        ClearRealized();
        _pool.Clear();
        _heights.Clear();
        _tops = [0];
        _view = null;
        ItemsSource.Projection = null;
    }

    private void ClearRealized() { foreach (var id in _realized.Keys.ToArray()) Recycle(id); }
    private void Recycle(ConsoleRowId id)
    {
        var container = _realized[id];
        _realized.Remove(id);
        Children.Remove(container);
        container.Release();
        _pool.Push(container);
    }

    private sealed record HeightMeasurement(long Version, double Width, double Height);
}
