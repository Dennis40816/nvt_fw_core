// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Rendering;
using Nvt.Core.Avalonia.Icons;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal sealed class ConsoleRowPresenter : Panel, ICustomHitTest
{
    // UI thread only. Release drops the gesture capture, borrowed content, callbacks and display caches.
    private RowInput? _input;
    private PointerGesture? _gesture;
    private ConsoleRow? _row => _input?.Row;
    private Action<ConsoleRowId>? _toggle => _input?.Toggle;
    private bool _expanded => _input is { } input && input.View.ViewState.ExpandedIds.Contains(input.Row.Id);
    private bool _timeVisible => _input?.View.TimeMode != ConsoleTimeMode.Hidden;
    private readonly TextBlock _time = Label("MonoCaption", "NfcTextMutedBrush");
    private readonly TextBlock _icon = Label("Icon", "NfcTextMutedBrush");
    private readonly TextBlock _level = Label("Body", "NfcTextBrush");
    private readonly ConsoleTextPresenter _source = new("MonoCaption", "NfcTextSecondaryBrush", ConsoleSearchArea.Source);
    private readonly ConsoleTextPresenter _message = new("Body", "NfcTextBrush", ConsoleSearchArea.Message);
    private readonly TextBlock _count = Label("Numbers", "NfcTextSecondaryBrush");
    private readonly TextBlock _arrow = Label("Icon", "NfcTextSecondaryBrush");
    private double Geometry(string name) => Dimension(this, name);
    private double RowHeight => Geometry("RowHeight");
    private Thickness RowPadding => UiResourceResolver.GetThickness(this, "Nvt.Console.List.RowPadding", default);
    private double MessageStart => RowPadding.Left + (_timeVisible ? Geometry("TimeWidth") : 0)
        + Geometry("LevelWidth") + Geometry("SourceWidth");
    private double MessageWidth(double width) => GetMessageWidth(this, width, _timeVisible);
    internal static double GetMessageWidth(Control owner, double width, bool timeVisible)
    {
        var padding = UiResourceResolver.GetThickness(owner, "Nvt.Console.List.RowPadding", default);
        return Math.Max(1, width - padding.Left - padding.Right - (timeVisible ? Dimension(owner, "TimeWidth") : 0)
            - Dimension(owner, "LevelWidth") - Dimension(owner, "SourceWidth")
            - Dimension(owner, "RepeatWidth") - Dimension(owner, "ArrowWidth"));
    }
    private static double Dimension(Control owner, string name)
    {
        var key = name switch
        {
            "IconWidth" => "Nvt.Font.Icon.Size",
            "IconGap" => "NfcSpace4",
            _ => $"Nvt.Console.List.{name}",
        };
        var value = UiResourceResolver.GetDouble(owner, key, double.NaN);
        return double.IsFinite(value) ? value
            : throw new InvalidOperationException($"Console list requires geometry resource '{name}'.");
    }
    internal ConsoleRowId? RowId => _row?.Id;
    internal ConsoleRow? Row => _row;
    internal TextBlock TimeLabel => _time;
    internal TextBlock IconLabel => _icon;
    internal TextBlock LevelLabel => _level;
    internal ConsoleTextPresenter SourceText => _source;
    internal ConsoleTextPresenter MessageText => _message;
    internal TextBlock CountLabel => _count;
    internal TextBlock ArrowLabel => _arrow;

    internal ConsoleRowPresenter()
    {
        ClipToBounds = true;
        Children.AddRange([_time, _icon, _level, _source, _message, _count, _arrow]);
    }

    private static TextBlock Label(string role, string brush)
    {
        var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, ClipToBounds = true };
        if (role == "Icon") label.Classes.Add("nvtIcon");
        label.Bind(TextBlock.LineHeightProperty, new DynamicResourceExtension("Nvt.Console.List.RowHeight"));
        label.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension($"Nvt.Font.{role}.Family"));
        label.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension($"Nvt.Font.{role}.Size"));
        label.Bind(TextBlock.FontWeightProperty, new DynamicResourceExtension($"Nvt.Font.{role}.Weight"));
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(brush));
        return label;
    }

    internal void Configure(ConsoleRow row, ConsoleListView view, Action<ConsoleRowId> toggle)
    {
        if (row.Level is LogLevel.Error or LogLevel.Fatal) Bind(BackgroundProperty, new DynamicResourceExtension("NfcDangerSurfaceBrush"));
        else ClearValue(BackgroundProperty);
        _input = new(row, view, toggle);
        _time.Text = ConsoleTimeFormatter.Format(row.Timestamp, view.Projection!.TimeBase, view.TimeMode,
            view.TimeOptions.RelativeTimeTemplate, view.TimeOptions.Culture, view.TimeOptions.AbsoluteTimeZone);
        var levelKey = $"Nvt.Console.List.Level.{row.Level}";
        _level.Text = ConsoleListText.Format(view, levelKey);
        var (glyph, brush) = row.Level switch
        {
            LogLevel.Trace => (NvtIcons.MoreHoriz, "NfcTextMutedBrush"),
            LogLevel.Debug => (NvtIcons.BugReport, "NfcTextSecondaryBrush"),
            LogLevel.Info => (NvtIcons.Info, "NfcInfoTextBrush"),
            LogLevel.Warn => (NvtIcons.Warning, "NfcWarningTextBrush"),
            LogLevel.Error => (NvtIcons.Error, "NfcDangerTextBrush"),
            _ => (NvtIcons.Cancel, "NfcDangerTextStrongBrush"),
        };
        _icon.Text = glyph;
        _icon.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(brush));
        _level.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(brush));
        _count.Text = row.Count > 1 ? $"×{row.Count}" : string.Empty;
        _arrow.Text = _expanded ? NvtIcons.ExpandLess : NvtIcons.ChevronRight;
        _source.Configure(row, false);
        _message.Configure(row, _expanded);
        ToolTip.SetTip(_source, row.SourceId);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Recycled rows can still be in Avalonia's layout queue after losing their resource parent.
        if (_input is null) return default;
        _message.Measure(new(MessageWidth(availableSize.Width), double.PositiveInfinity));
        _source.Measure(new(Geometry("SourceWidth"), RowHeight));
        _time.Measure(new(Geometry("TimeWidth"), RowHeight));
        _icon.Measure(new(Geometry("IconWidth"), RowHeight));
        _level.Measure(new(Geometry("LevelWidth") - Geometry("IconWidth") - Geometry("IconGap"), RowHeight));
        _count.Measure(new(Geometry("RepeatWidth"), RowHeight));
        _arrow.Measure(new(Geometry("ArrowWidth"), RowHeight));
        _arrow.IsVisible = _message.Truncated;
        return new(availableSize.Width, _expanded ? Math.Max(RowHeight, _message.DesiredSize.Height) : RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_input is null) return finalSize;
        var x = RowPadding.Left;
        _time.IsVisible = _timeVisible;
        if (_timeVisible) { _time.Arrange(new(x, 0, Geometry("TimeWidth"), RowHeight)); x += Geometry("TimeWidth"); }
        _icon.Arrange(new(x, 0, Geometry("IconWidth"), RowHeight));
        var levelStart = Geometry("IconWidth") + Geometry("IconGap");
        _level.Arrange(new(x + levelStart, 0, Geometry("LevelWidth") - levelStart, RowHeight)); x += Geometry("LevelWidth");
        _source.Arrange(new(x, 0, Geometry("SourceWidth"), RowHeight)); x += Geometry("SourceWidth");
        var width = MessageWidth(finalSize.Width);
        _message.Arrange(new(x, 0, width, finalSize.Height)); x += width;
        _count.Arrange(new(x, 0, Geometry("RepeatWidth"), RowHeight)); x += Geometry("RepeatWidth");
        _arrow.Arrange(new(x + (Geometry("ArrowWidth") - Geometry("IconWidth")) / 2, 0, Geometry("IconWidth"), RowHeight));
        return finalSize;
    }

    internal void SetVisibleSlice(double start, double end) => _message.SetVisibleSlice(start, end);
    internal int TextOffsetAt(double pixel) => _message.TextOffsetAt(pixel);
    internal double PixelOffsetAt(int text, double fallback) => _message.PixelOffsetAt(text, fallback);

    bool ICustomHitTest.HitTest(Point point)
    {
        var local = TopLevel.GetTopLevel(this)?.TranslatePoint(point, this);
        return _input is not null && local is { } position && new Rect(Bounds.Size).Contains(position);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && IsToggleTarget(e.GetPosition(this)))
        {
            CancelGesture();
            e.Pointer.Capture(this);
            _gesture = new(e.Pointer, e.GetPosition(this));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_gesture is { } gesture && gesture.Pointer == e.Pointer
            && Distance(e.GetPosition(this), gesture.Start) > Geometry("DragThreshold")) CancelGesture();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var gesture = _gesture;
        CancelGesture();
        if (gesture is not null && gesture.Pointer == e.Pointer && e.InitialPressMouseButton == MouseButton.Left
            && Distance(e.GetPosition(this), gesture.Start) <= Geometry("DragThreshold") && _row is { } row
            && IsToggleTarget(e.GetPosition(this)) && _message.Truncated) _toggle?.Invoke(row.Id);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_gesture?.Pointer == e.Pointer) _gesture = null;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGesture();
        base.OnDetachedFromVisualTree(e);
    }

    private void CancelGesture()
    {
        var gesture = _gesture;
        _gesture = null;
        if (gesture?.Pointer.Captured == this) gesture.Pointer.Capture(null);
    }

    private bool IsToggleTarget(Point point) => point.X >= MessageStart
        && (point.X < _message.Bounds.Right || point.X >= _arrow.Bounds.X
            && point.X < _arrow.Bounds.Right + (Geometry("ArrowWidth") - Geometry("IconWidth")) / 2);

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private sealed record RowInput(ConsoleRow Row, ConsoleListView View, Action<ConsoleRowId> Toggle);
    private sealed record PointerGesture(IPointer Pointer, Point Start);

    internal void Release()
    {
        CancelGesture();
        _input = null;
        _source.Release();
        _message.Release();
        ToolTip.SetTip(_source, null);
    }
}
