// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Nvt.Core.Avalonia.Tests.Choice;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>A BrushTransition interpolates ARGB channels. A fade between two state fills must not pass through a colour that is brighter or darker than both ends.</summary>
public sealed class StateFadeTests(ITestOutputHelper output)
{
    private const double Tolerance = 0.004;
    private static readonly string[] _files = ["ButtonStyles", "ToggleStyles", "ListStyles", "TabStyles"];
    private static readonly string[] _surfaces = ["NfcSurfaceBrush", "NfcSurfaceSubtleBrush", "NfcSelectionSurfaceBrush"];
    private static readonly string[] _states = ["rest", "hover", "pressed", "flyout"];

    private sealed record Sample(string Name, Func<Control> Create, string[]? Surfaces = null);

    private static readonly Sample[] _samples =
    [
        new("Button.actionPrimary", () => new Button { Classes = { "actionPrimary" }, Content = "Label" }),
        new("Button.actionNeutral", () => new Button { Classes = { "actionNeutral" }, Content = "Label" }),
        new("Button.actionGhost", () => new Button { Classes = { "actionGhost" }, Content = "Label" }),
        new("Button.actionGhost.flat", () => new Button { Classes = { "actionGhost", "flat" }, Content = "Label" }),
        new("Button.chipAction", () => new Button { Classes = { "chipAction" }, Content = "Label" }),
        new("Button.actionIconButton", () => new Button { Classes = { "actionIconButton" }, Content = "+" }),
        new("Button.actionGhost.actionIconButton", () => new Button { Classes = { "actionGhost", "actionIconButton" }, Content = "+" }),
        // A segment always sits in its group, which paints NfcSelectionSurfaceBrush.
        new("ToggleButton.toggleSegment", () => new ToggleButton { Classes = { "toggleSegment" }, Content = "Label" }, ["NfcSelectionSurfaceBrush"]),
        new("ToggleButton.toggleTab", () => new ToggleButton { Classes = { "toggleTab" }, Content = "Label" }),
        new("ToggleButton.toggleIcon", () => new ToggleButton { Classes = { "toggleIcon" }, Content = "+" }),
        new("ToggleButton.toggleSoft", () => new ToggleButton { Classes = { "toggleSoft" }, Content = "Label" }),
        new("ToggleButton.actionNeutral", () => new ToggleButton { Classes = { "actionNeutral" }, Content = "Label" }),
        new("ListBoxItem", () => new ListBoxItem { Content = "Label" }),
        new("TabItem", () => new TabItem { Header = "Label" }),
    ];

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoverAndPressedFadesStayBetweenTheirEndpoints(bool dark)
    {
        var failures = new List<string>();
        foreach (Sample sample in _samples)
        {
            Control control = sample.Create();
            Window host = ChoiceTestHost.Create(control, dark, styles: false, snapshot: false);
            foreach (string file in _files)
            {
                var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{file}.axaml");
                host.Styles.Add(new StyleInclude(uri) { Source = uri });
            }
            try
            {
                host.Show();
                ChoiceTestHost.Flush(host);
                control.Transitions = null;
                var fills = new Dictionary<string, Color>();
                foreach (string state in _states)
                {
                    bool flyout = state == "flyout";
                    if (flyout && control.GetType() != typeof(Button)) continue;
                    var pseudo = (IPseudoClasses)control.Classes;
                    pseudo.Set(":pointerover", state is "hover" or "pressed");
                    pseudo.Set(":pressed", state == "pressed");
                    pseudo.Set(":flyout-open", flyout);
                    ChoiceTestHost.Flush(host);
                    fills[state] = ChoiceTestHost.ColorOf(((TemplatedControl)control).Background);
                }
                foreach (string surfaceKey in sample.Surfaces ?? _surfaces)
                {
                    Color surface = ChoiceTestHost.ResourceColor(host, surfaceKey);
                    foreach (string from in fills.Keys)
                        foreach (string to in fills.Keys.Where(to => string.CompareOrdinal(from, to) < 0))
                            if (Overshoot(fills[from], fills[to], surface) is { } found)
                                failures.Add($"{(dark ? "Dark" : "Light")} {sample.Name} {from}->{to} over {surfaceKey}: {found} ({fills[from]} -> {fills[to]})");
                }
            }
            finally { host.Close(); }
        }
        foreach (string failure in failures) output.WriteLine(failure);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>Returns a description when a frame of the fade is brighter or darker than both endpoints, otherwise null.</summary>
    private static string? Overshoot(Color from, Color to, Color surface)
    {
        double a = Luminance(Flatten(from, surface)), b = Luminance(Flatten(to, surface));
        double low = Math.Min(a, b) - Tolerance, high = Math.Max(a, b) + Tolerance;
        foreach (double t in new[] { 0.25, 0.5, 0.75 })
        {
            double frame = Luminance(Flatten(Lerp(from, to, t), surface));
            if (frame < low || frame > high) return $"frame {t:F2} luminance {frame:F4} is outside {Math.Min(a, b):F4}..{Math.Max(a, b):F4}";
        }
        return null;
    }

    private static Color Lerp(Color from, Color to, double t) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * t), (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t), (byte)Math.Round(from.B + (to.B - from.B) * t));

    private static Color Flatten(Color fill, Color surface)
    {
        double alpha = fill.A / 255d;
        byte Mix(byte top, byte under) => (byte)Math.Round(top * alpha + under * (1 - alpha));
        return Color.FromRgb(Mix(fill.R, surface.R), Mix(fill.G, surface.G), Mix(fill.B, surface.B));
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            double v = value / 255d;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
