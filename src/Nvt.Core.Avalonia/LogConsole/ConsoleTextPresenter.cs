// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// Virtualizes bounded text segments inside a single row. No text child visual is created per line.
internal sealed class ConsoleTextPresenter(string role, string foregroundKey, ConsoleSearchArea area) : Control
{
    private const int ReadLimit = 2048;
    // UI thread only. The sole invalidation point is the layout key (row, expansion, width, typeface).
    private RenderInput? _input;
    private ConsoleRow? _row => _input?.Row;
    private bool _expanded => _input?.Expanded ?? false;
    private LayoutKey? _key;
    private PresentedText? _presentation;
    private readonly List<Segment> _segments = [];
    private readonly Dictionary<int, TextLayout> _visible = [];
    private readonly record struct VisibleRange(double Start, double End);
    private VisibleRange? _slice;
    private LinkTarget? _hoveredTarget;
    internal LinkTarget? HoveredTarget => _hoveredTarget;
    private double LineHeight => UiResourceResolver.GetDouble(this, "Nvt.Console.List.RowHeight");
    internal IReadOnlyList<Segment> Segments => _segments;
    internal IReadOnlyDictionary<int, TextLayout> VisibleLayouts => _visible;
    internal LayoutKey? CurrentLayout => _key;
    // Plain bounded values from the painting layout; getters never read retained content.
    internal PresentedText? Presentation => area == ConsoleSearchArea.Message ? _presentation : null;
    internal bool Truncated
    {
        get
        {
            if (area == ConsoleSearchArea.Message || _row is null) return _presentation?.Truncated ?? false;
            using var untrimmed = new TextLayout(_row.SourceId, Typeface, FontSize, Foreground, lineHeight: LineHeight);
            return untrimmed.WidthIncludingTrailingWhitespace > (_key?.Width ?? Bounds.Width);
        }
    }
    internal Typeface Typeface => new(Resource<FontFamily>($"Nvt.Font.{role}.Family"),
        weight: Resource<FontWeight>($"Nvt.Font.{role}.Weight"));
    internal double FontSize => Resource<double>($"Nvt.Font.{role}.Size");
    private IBrush Foreground => Resource<IBrush>(foregroundKey);
    private T Resource<T>(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is T result
        ? result : throw new InvalidOperationException($"Console list requires Core resource '{key}'.");

    internal static double Estimate(int length, double width, Control owner)
    {
        var size = UiResourceResolver.GetDouble(owner, "Nvt.Font.Body.Size");
        var height = UiResourceResolver.GetDouble(owner, "Nvt.Console.List.RowHeight");
        return Math.Max(height, Math.Ceiling(length / Math.Max(1, width / Math.Max(1, size))) * height);
    }

    internal void Configure(ConsoleRow row, bool expanded, ConsoleLinkIndex? links = null)
    {
        if (ReferenceEquals(_row, row) && _expanded == expanded && ReferenceEquals(_input?.Links, links)) return;
        if (!SamePublication(row, expanded)) _presentation = null;
        _input = new(row, expanded, links);
        InvalidateMeasure();
        InvalidateVisual();
    }

    // A projection rebind to an equal row (same id, text version, length and source) keeps the published values.
    private bool SamePublication(ConsoleRow row, bool expanded) => _row is { } current && _expanded == expanded
        && current.Id.Equals(row.Id) && current.TextVersion == row.TextVersion
        && current.TextContent.Length == row.TextContent.Length && string.Equals(current.SourceId, row.SourceId, StringComparison.Ordinal);

    internal void SetHoveredTarget(LinkTarget? target)
    {
        if (_hoveredTarget == target) return;
        _hoveredTarget = target;
        InvalidateVisual();
    }

    internal ConsoleLinkSpan? HitLink(Point point)
    {
        if (_input?.Links is not { } links || _key is null || _presentation is null || _segments.Count == 0
            || !new Rect(Bounds.Size).Contains(point)) return null;
        var segment = _segments[SegmentAt(point.Y)];
        var hit = VisibleLayout(SegmentAt(point.Y)).HitTestPoint(new(point.X, point.Y - segment.Y));
        return hit.IsInside ? links.HitTest(segment.Offset + hit.TextPosition) : null;
    }

    private TextLayout Layout(string text, double width, bool expanded, IBrush? brush = null) =>
        new(text, Typeface, FontSize, brush ?? Foreground, textWrapping: expanded ? TextWrapping.Wrap : TextWrapping.NoWrap,
            textTrimming: expanded ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            maxWidth: Math.Max(1, width), lineHeight: LineHeight, maxLines: expanded ? 0 : 1);

    private string Read(int offset, int length)
    {
        if (length == 0) return string.Empty;
        return string.Create(length, (Content: _row!.TextContent, Offset: offset),
            static (chars, state) => state.Content.Read(state.Offset, chars));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Max(1, availableSize.Width);
        if (_row is null) return new(width, LineHeight);
        var key = new LayoutKey(_row.Id, _row.TextVersion, _row.TextContent.Length, _row.SourceId,
            _expanded, width, LineHeight, Typeface, FontSize, ActualThemeVariant, Foreground);
        if (_key != key || _presentation is null)
        {
            ClearLayouts();
            _segments.Clear();
            _key = key;
            var preview = area == ConsoleSearchArea.Source ? new ConsoleFirstLine(_row.SourceId, false) : ConsoleRowName.FirstLine(_row);
            var truncated = preview.HasMoreContent;
            if (area == ConsoleSearchArea.Message && !truncated)
            {
                using var untrimmed = new TextLayout(preview.Text, Typeface, FontSize, Foreground, lineHeight: LineHeight);
                truncated = untrimmed.WidthIncludingTrailingWhitespace > width;
            }
            var text = preview.Text;
            if (area == ConsoleSearchArea.Source || !_expanded)
            {
                _segments.Add(new(0, preview.Text.Length, 0, LineHeight));
                _visible[0] = Layout(preview.Text, width, false);
            }
            else text = MeasureExpanded(width);
            _presentation = new(text, truncated);
        }
        return new(width, _segments[^1].Y + _segments[^1].Height);
    }

    private string MeasureExpanded(double width)
    {
        // Reuse the paint scan's consumed characters for the copy-bound publication, without another read path.
        var row = _row!;
        var published = new StringBuilder(Math.Min(row.TextContent.Length, ConsoleCopyText.MaximumCharacters));
        var offset = 0;
        var y = 0d;
        var capped = false;
        while (offset < row.TextContent.Length)
        {
            var count = Math.Min(ReadLimit, row.TextContent.Length - offset);
            var text = Read(offset, count);
            if (offset + count < row.TextContent.Length && count > 1
                && (char.IsHighSurrogate(text[^1]) || text[^1] == '\r')) text = text[..--count];
            using var layout = Layout(text, width, true);
            var lines = layout.TextLines.Count;
            var take = offset + count < row.TextContent.Length && lines > 1 ? lines - 1 : lines;
            var consumed = Math.Clamp(layout.TextLines.Take(take).Sum(line => line.Length), 1, count);
            var height = Math.Max(LineHeight, take * LineHeight);
            _segments.Add(new(offset, consumed, y, height));
            if (!capped)
            {
                var publish = Math.Min(consumed, ConsoleCopyText.MaximumCharacters - published.Length);
                if (publish < consumed)
                {
                    // The text is contiguous: once the bound cuts it, no later segment may add characters.
                    capped = true;
                    if (publish > 0 && char.IsHighSurrogate(text[publish - 1])) publish--;
                }
                published.Append(text.AsSpan(0, publish));
            }
            offset += consumed;
            y += height;
        }
        if (_segments.Count == 0) _segments.Add(new(0, 0, 0, LineHeight));
        return published.ToString();
    }

    internal void SetVisibleSlice(double start, double end)
    {
        var range = new VisibleRange(start, end);
        if (_slice == range) return;
        _slice = range;
        InvalidateVisual();
    }

    private int SegmentAt(double y)
    {
        var low = 0;
        var high = _segments.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_segments[middle].Y + _segments[middle].Height <= y) low = middle + 1;
            else high = middle;
        }
        return Math.Min(low, Math.Max(0, _segments.Count - 1));
    }

    private TextLayout VisibleLayout(int index)
    {
        if (_visible.TryGetValue(index, out var cached)) return cached;
        var segment = _segments[index];
        var text = area == ConsoleSearchArea.Source ? _row!.SourceId
            : _expanded ? Read(segment.Offset, segment.Length) : _presentation!.Text;
        return _visible[index] = Layout(text, _key!.Width, _expanded && area == ConsoleSearchArea.Message);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_row is null || _key is null || _presentation is null || _segments.Count == 0) return;
        var slice = _slice ?? new VisibleRange(0, LineHeight);
        var needed = new HashSet<int>();
        for (var i = SegmentAt(slice.Start); i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.Y >= slice.End) break;
            needed.Add(i);
            var layout = VisibleLayout(i);
            using var clip = context.PushClip(new Rect(0, segment.Y, Bounds.Width, segment.Height));
            var hits = _row.SearchHits.Where(hit => hit.Area == area && hit.Start < segment.Offset + segment.Length
                && hit.Start + hit.Length > segment.Offset).SelectMany(hit =>
                    layout.HitTestTextRange(Math.Max(0, hit.Start - segment.Offset),
                        Math.Min(segment.Length, hit.Start + hit.Length - segment.Offset) - Math.Max(0, hit.Start - segment.Offset)))
                .ToArray();
            // Paint layers: search background, ordinary text, link decoration, then search foreground.
            foreach (var hit in hits) context.FillRectangle(Resource<IBrush>("NfcWarningSurfaceBrush"), hit.Translate(new(0, segment.Y)));
            layout.Draw(context, new(0, segment.Y));
            DrawLinks(context, layout, segment);
            if (hits.Length > 0)
            {
                var text = area == ConsoleSearchArea.Source ? _row.SourceId
                    : _expanded ? Read(segment.Offset, segment.Length) : _presentation.Text;
                using var highlight = Layout(text, _key.Width, _expanded && area == ConsoleSearchArea.Message,
                    Resource<IBrush>("NfcWarningTextStrongBrush"));
                foreach (var hit in hits)
                {
                    using var hitClip = context.PushClip(hit.Translate(new(0, segment.Y)));
                    highlight.Draw(context, new(0, segment.Y));
                }
            }
        }
        foreach (var i in _visible.Keys.Where(i => !needed.Contains(i)).ToArray())
        {
            _visible[i].Dispose();
            _visible.Remove(i);
        }
    }

    private void DrawLinks(DrawingContext context, TextLayout layout, Segment segment)
    {
        if (_input?.Links is not { } links) return;
        foreach (var link in links.Spans.Where(link => link.Start < segment.Offset + segment.Length
            && link.Start + link.Length > segment.Offset))
        {
            var start = Math.Max(0, link.Start - segment.Offset);
            var length = Math.Min(segment.Length, link.Start + link.Length - segment.Offset) - start;
            var brush = Resource<IBrush>(_hoveredTarget == link.Target ? "NfcAccentBrush" : "NfcBorderSoftBrush");
            foreach (var rect in layout.HitTestTextRange(start, length))
            {
                var underline = rect.Translate(new(0, segment.Y));
                if (_hoveredTarget == link.Target)
                {
                    var text = _expanded ? Read(segment.Offset, segment.Length) : _presentation!.Text;
                    using var hover = Layout(text, _key!.Width, _expanded, brush);
                    using var hoverClip = context.PushClip(underline);
                    hover.Draw(context, new(0, segment.Y));
                }
                context.DrawLine(new Pen(brush), underline.BottomLeft, underline.BottomRight);
            }
        }
    }

    internal int TextOffsetAt(double pixel)
    {
        if (!_expanded || _segments.Count == 0) return 0;
        var index = SegmentAt(pixel);
        var segment = _segments[index];
        // Segments of the previous row stay until the next layout; never read past the current content.
        if (segment.Offset + segment.Length > _row!.TextContent.Length) return 0;
        using var layout = Layout(Read(segment.Offset, segment.Length), _key!.Width, true);
        var line = Math.Clamp((int)((pixel - segment.Y) / LineHeight), 0, layout.TextLines.Count - 1);
        return segment.Offset + layout.TextLines.Take(line).Sum(part => part.Length);
    }

    internal double PixelOffsetAt(int text, double fallback)
    {
        if (!_expanded || _key is null) return fallback;
        var segment = _segments.LastOrDefault(part => part.Offset <= text);
        if (segment is null || _row is null || segment.Offset + segment.Length > _row.TextContent.Length) return fallback;
        using var layout = Layout(Read(segment.Offset, segment.Length), _key.Width, true);
        var offset = segment.Offset;
        var line = 0;
        foreach (var part in layout.TextLines)
        {
            if (text < offset + part.Length) break;
            offset += part.Length;
            line++;
        }
        return segment.Y + line * LineHeight + fallback % LineHeight;
    }

    internal void Release() { _input = null; _key = null; _presentation = null; _slice = null; _hoveredTarget = null; _segments.Clear(); ClearLayouts(); }
    internal void ClearLayouts() { foreach (var layout in _visible.Values) layout.Dispose(); _visible.Clear(); }
    internal sealed record Segment(int Offset, int Length, double Y, double Height);
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += ResourcesUpdated;
        ResourcesChanged += ResourcesUpdated;
        InvalidateMeasure();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= ResourcesUpdated;
        ResourcesChanged -= ResourcesUpdated;
        base.OnDetachedFromVisualTree(e);
    }

    private void ResourcesUpdated(object? sender, EventArgs e) { InvalidateMeasure(); InvalidateVisual(); }
    private void ResourcesUpdated(object? sender, ResourcesChangedEventArgs e) { InvalidateMeasure(); InvalidateVisual(); }

    private sealed record RenderInput(ConsoleRow Row, bool Expanded, ConsoleLinkIndex? Links);
    internal sealed record PresentedText(string Text, bool Truncated);
    internal sealed record LayoutKey(ConsoleRowId RowId, long TextVersion, int Length, string SourceId, bool Expanded,
        double Width, double LineHeight, Typeface Typeface, double FontSize, ThemeVariant? ThemeVariant, IBrush Foreground);
}
