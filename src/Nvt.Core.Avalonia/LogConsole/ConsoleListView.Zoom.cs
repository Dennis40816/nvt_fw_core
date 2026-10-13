// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Nvt.Core.Avalonia.Theme;

namespace Nvt.Core.Avalonia.LogConsole;

public sealed partial class ConsoleListView
{
    // Row metrics that follow the zoom. Padding, the drag threshold and the toolbar keep their size.
    private static readonly string[] _zoomedResources =
    [
        "Nvt.Console.List.RowHeight", "Nvt.Console.List.TimeWidth", "Nvt.Console.List.LevelWidth",
        "Nvt.Console.List.SourceWidth", "Nvt.Console.List.RepeatWidth", "Nvt.Console.List.ArrowWidth",
        "Nvt.Font.Body.Size", "Nvt.Font.MonoCaption.Size", "Nvt.Font.Numbers.Size", "Nvt.Font.Icon.Size",
    ];

    /// <summary>The smallest zoom factor, 80 percent.</summary>
    public const double MinimumZoom = 0.8;
    /// <summary>The largest zoom factor, 300 percent.</summary>
    public const double MaximumZoom = 3.0;
    /// <summary>The distance between two zoom stops, 10 percent of the normal size.</summary>
    public const double ZoomStep = 0.1;

    /// <summary>Defines <see cref="Zoom"/>.</summary>
    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<ConsoleListView, double>(
        nameof(Zoom), 1.0, coerce: CoerceZoom, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Gets or sets the size factor of the rows. 1.0 is the normal size.</summary>
    /// <remarks>
    /// A value outside <see cref="MinimumZoom"/> and <see cref="MaximumZoom"/> is clamped, and a value that is not a number becomes 1.0.
    /// The view scales the row height, the column widths and the text of the rows. It does not scale the toolbar, the header, the jump button or the retention notice.
    /// A host persists the value by handling <see cref="ZoomChanged"/> and sets it again when it creates the view.
    /// While the view has focus, Ctrl and plus, Ctrl and minus, and Ctrl and 0 change it. Ctrl and the mouse wheel over the view change it with or without focus.
    /// </remarks>
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    /// <summary>Occurs after <see cref="Zoom"/> changed, with the new value. The view raises it on the UI thread.</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>Moves to the next zoom stop above the current value, or stays at <see cref="MaximumZoom"/>.</summary>
    public void ZoomIn() => Zoom = Math.Round((Math.Floor(Zoom / ZoomStep + 1e-9) + 1) * ZoomStep, 2);

    /// <summary>Moves to the next zoom stop below the current value, or stays at <see cref="MinimumZoom"/>.</summary>
    public void ZoomOut() => Zoom = Math.Round((Math.Ceiling(Zoom / ZoomStep - 1e-9) - 1) * ZoomStep, 2);

    /// <summary>Sets <see cref="Zoom"/> to 1.0.</summary>
    public void ResetZoom() => Zoom = 1.0;

    private static double CoerceZoom(AvaloniaObject owner, double value)
        => double.IsNaN(value) ? 1.0 : Math.Clamp(value, MinimumZoom, MaximumZoom);

    // The scaled values sit in the resources of the scroll viewer, so the rows and the height index find them
    // through the same lookups that they use for the theme. A view that was never zoomed keeps no dictionary.
    // After a zoom back to 1.0 the dictionary holds the unscaled values: a dictionary that was replaced by an
    // empty one notified the rows before the lookup left the old values.
    internal void RefreshZoomResources()
    {
        if (_parts?.Scroll is not { } scroll) return;
        var zoom = Zoom;
        if (zoom == 1.0 && scroll.Resources.Count == 0) return;
        var resources = new ResourceDictionary();
        foreach (var key in _zoomedResources)
        {
            var value = UiResourceResolver.GetDouble(this, key, double.NaN);
            if (double.IsFinite(value)) resources[key] = zoom == 1.0 ? value : Math.Round(value * zoom, 2);
        }
        if (SameValues(scroll.Resources, resources)) return;
        scroll.Resources = resources;
    }

    // A theme notification that changes none of the zoomed values must not rebuild the heights again.
    private static bool SameValues(IResourceDictionary current, ResourceDictionary next)
    {
        if (current.Count != next.Count) return false;
        foreach (var pair in next)
            if (!current.TryGetResource(pair.Key, null, out var value) || !Equals(value, pair.Value)) return false;
        return true;
    }

    private void ZoomUpdated(double previous, double zoom)
    {
        _parts?.Host.ScaleReadingInset(previous > 0 ? zoom / previous : 1);
        RefreshZoomResources();
        _parts?.Host.ScaleReadingInset(1);
        ZoomChanged?.Invoke(this, zoom);
    }
}
