// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Tests.Choice;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

internal static class ConsoleVisualEvidenceSupport
{
    internal static double Minimum(bool dark, bool square, string group)
    {
        var ratios = new List<double>();
        foreach (var level in Enum.GetValues<LogLevel>())
            foreach (var state in new[] { "normal", "hover", "selected", "selected-hover", "expanded" })
                ratios.AddRange(Ratios(dark, square, group, level, state));
        return ratios.Min();
    }
    private static double[] Ratios(bool dark, bool square, string group, LogLevel level, string state)
    {
        using var content = new InMemoryLogTextContent("https://example.test/\nsecond line");
        using var projection = Project(Row(content, level));
        var view = new ConsoleListView { Projection = projection };
        var window = ConsoleInteractionWindow.Create(view);
        try
        {
            window.RequestedThemeVariant = dark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
            ThemeShapes.SetShape(window.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            view.ViewState = view.ViewState with
            {
                Selection = state.StartsWith("selected", StringComparison.Ordinal) ? [1] : [],
                ExpandedIds = string.Equals(state, "expanded", StringComparison.Ordinal) ? [new(1)] : [],
            };
            ConsoleInteractionWindow.Pump(window);
            var row = Assert.IsType<ConsoleRowPresenter>(Assert.Single(ConsoleInteractionWindow.Host(view).Children));
            row.Focus(NavigationMethod.Directional);
            if (state.Contains("hover", StringComparison.Ordinal))
                window.MouseMove(row.MessageText.TranslatePoint(new Point(8, 10), window)!.Value);
            ConsoleInteractionWindow.Pump(window);
            var surface = ChoiceTestHost.ColorOf(view.Background);
            var background = row.Background is { } tint ? Composite(tint, surface) : surface;
            if (row.Row is { } current && ConsoleRowSelection.Contains(current, view.ViewState))
                background = Composite(Resource<IBrush>(row, "NfcSelectionSurfaceBrush"), background);
            return Brushes(row, group).Select(brush => ChoiceTestHost.Contrast(Composite(brush, background), background)).ToArray();
        }
        finally { window.Close(); }
    }
    private static IBrush[] Brushes(ConsoleRowPresenter row, string group) => group switch
    {
        "text" => [row.TimeLabel.Foreground!, row.LevelLabel.Foreground!, row.SourceText.CurrentLayout!.Foreground,
            row.MessageText.CurrentLayout!.Foreground, row.CountLabel.Foreground!, row.ArrowLabel.Foreground!,
            Resource<IBrush>(row, "NfcAccentBrush")],
        "icon" => [row.IconLabel.Foreground!],
        "focus" => [Ring(row).BorderBrush!],
        "disabled" => [Resource<IBrush>(row, "NfcTextDisabledBrush")],
        _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };
    private static Color Composite(IBrush brush, Color background)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        var alpha = solid.Color.A / 255d * solid.Opacity;
        byte Channel(byte foreground, byte behind) => (byte)Math.Round(foreground * alpha + behind * (1 - alpha), MidpointRounding.AwayFromZero);
        return Color.FromRgb(Channel(solid.Color.R, background.R), Channel(solid.Color.G, background.G), Channel(solid.Color.B, background.B));
    }
    internal static void AssertRingInside(ConsoleScrollScene scene, bool last)
    {
        scene.Host.Offset = last ? new(0, scene.Host.Extent.Height) : default;
        ConsoleInteractionWindow.Pump(scene.Window);
        var rows = scene.Host.Children.OfType<ConsoleRowPresenter>().Where(row => row.Bounds.Y >= 0 && row.Bounds.Bottom <= scene.Host.Viewport.Height);
        var row = last ? rows.MaxBy(item => item.Bounds.Y)! : rows.MinBy(item => item.Bounds.Y)!;
        row.Focus(NavigationMethod.Directional);
        ConsoleInteractionWindow.Pump(scene.Window);
        var ring = Ring(row);
        var origin = ring.TranslatePoint(default, scene.Host)!.Value;
        Assert.True(new Rect(scene.Host.Viewport).Contains(new Rect(origin, ring.Bounds.Size)), $"Ring {origin}/{ring.Bounds.Size}; viewport {scene.Host.Viewport}");
    }
}
