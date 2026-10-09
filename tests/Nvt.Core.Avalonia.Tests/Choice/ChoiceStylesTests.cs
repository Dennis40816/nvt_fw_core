// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Choice;

/// <summary>Checks choice states, contrast, wrapping, focus, and exact hit target geometry.</summary>
public sealed class ChoiceStylesTests(ITestOutputHelper output)
{
    /// <summary>Measures visible labels, selection outlines, glyphs, and focus against all supported surfaces.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EveryStateUsesTokensAndMeetsContrast(bool dark, bool square)
    {
        double textMinimum = double.MaxValue, disabledMinimum = double.MaxValue;
        double indicatorMinimum = double.MaxValue, markMinimum = double.MaxValue, ringMinimum = double.MaxValue;
        foreach (bool radio in new[] { false, true })
        {
            ToggleButton control = Sample(radio, new("Rest"));
            var label = new TextBlock { Text = "Include archived items" };
            control.Content = label;
            Window host = Create(control, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Assert.Equal(13, control.FontSize);
                Assert.True(control.TryFindResource("NfcUiFontFamily", control.ActualThemeVariant, out object? family));
                Assert.Equal(Assert.IsType<FontFamily>(family), control.FontFamily);
                Assert.Equal(FontWeight.Normal, control.FontWeight);
                Assert.Equal(new Size(20, 20), Part<Border>(control, "ChoiceIndicator").Bounds.Size);
                foreach (bool? selected in radio ? new bool?[] { false, true } : [false, true, null])
                foreach (ChoiceState interaction in Interactions.Concat([
                    new("Pressed without hover", Pressed: true),
                    new("Disabled interactions", Disabled: true, Hover: true, Pressed: true, Focus: true)]))
                {
                    ChoiceState state = interaction with { Checked = selected };
                    SetState(control, state);
                    Flush(host);
                    (string fill, string border) = Expected(state);
                    Border indicator = Part<Border>(control, "ChoiceIndicator");
                    Assert.Equal(ResourceColor(control, fill), ColorOf(indicator.Background));
                    Assert.Equal(ResourceColor(control, border), ColorOf(indicator.BorderBrush));
                    string labelToken = state.Disabled ? "NfcTextDisabledBrush" : "Nvt.Controls.ChoiceForegroundBrush";
                    Assert.Equal(ResourceColor(control, labelToken), ColorOf(label.Foreground));
                    Assert.Equal(1, control.Opacity);
                    Assert.Null(control.FocusAdorner);
                    Assert.Equal(new CornerRadius(radio ? 999 : 6), indicator.CornerRadius);
                    Assert.Equal(!radio && selected is true, Part<global::Avalonia.Controls.Shapes.Path>(control, "ChoiceCheck").IsVisible);
                    Assert.Equal(!radio && selected is null, Part<Rectangle>(control, "ChoiceDash").IsVisible);
                    Ellipse dot = Part<Ellipse>(control, "ChoiceDot");
                    Assert.Equal(radio && selected is true, dot.IsVisible);
                    if (dot.IsVisible) Assert.Equal(new Size(10, 10), dot.Bounds.Size);
                    Border ring = Part<Border>(control, "ChoiceFocusRing");
                    Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                    foreach (string surface in new[] { "NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush", "NfcSelectionSurfaceBrush", "NfcSecondaryActionPressedBrush" })
                    {
                        Color background = ResourceColor(control, surface);
                        double textContrast = Contrast(ColorOf(label.Foreground), background);
                        Assert.True(textContrast >= (state.Disabled ? 3 : 4.5), $"{dark}/{radio}/{selected}/{state.Name}: label {textContrast:F3}");
                        if (state.Disabled) disabledMinimum = Math.Min(disabledMinimum, textContrast);
                        else textMinimum = Math.Min(textMinimum, textContrast);
                        double indicatorContrast = Contrast(ColorOf(indicator.BorderBrush), background);
                        Assert.True(indicatorContrast >= 3, $"{dark}/{radio}/{selected}/{state.Name}: indicator {indicatorContrast:F3}");
                        indicatorMinimum = Math.Min(indicatorMinimum, indicatorContrast);
                        double focusContrast = Contrast(ColorOf(ring.BorderBrush), background);
                        Assert.True(focusContrast >= 3, $"{dark}: focus {focusContrast:F3}");
                        ringMinimum = Math.Min(ringMinimum, focusContrast);
                    }
                    if (selected is not false)
                    {
                        Color white = Color.Parse("#FFFFFF");
                        Assert.Equal(white, ColorOf(dot.Fill));
                        Assert.Equal(white, ColorOf(Part<Rectangle>(control, "ChoiceDash").Fill));
                        Assert.Equal(white, ColorOf(Part<global::Avalonia.Controls.Shapes.Path>(control, "ChoiceCheck").Stroke));
                        double contrast = Contrast(white, ColorOf(indicator.Background));
                        Assert.True(contrast >= (state.Disabled ? 3 : 4.5), $"{dark}: mark {contrast:F3}");
                        markMinimum = Math.Min(markMinimum, contrast);
                    }
                }
            }
            finally { host.Close(); }
        }
        output.WriteLine($"{(dark ? "Dark" : "Light")}/{(square ? "Square" : "Pill")}: label {textMinimum:F3}, disabled {disabledMinimum:F3}, outline {indicatorMinimum:F3}, mark {markMinimum:F3}, focus {ringMinimum:F3}");
    }

    /// <summary>Uses real Tab navigation and pointer clicks anywhere in the row with one exterior focus ring.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WholeRowAcceptsInputAndOnlyKeyboardFocusShowsRing(bool dark, bool square)
    {
        foreach (bool radio in new[] { false, true })
        foreach (bool selected in new[] { false, true })
        {
            var before = new Grid { Focusable = true, Height = 20 };
            ToggleButton control = Sample(radio, new("Rest", selected));
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { before, control } };
            Window host = Create(panel, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Assert.True(control.Focus(NavigationMethod.Pointer));
                Assert.False(Part<Border>(control, "ChoiceFocusRing").IsVisible);
                Assert.True(before.Focus());
                KeyStroke(host, Key.Tab);
                Assert.Same(control, host.FocusManager!.GetFocusedElement());
                Border ring = Part<Border>(control, "ChoiceFocusRing");
                Assert.True(ring.IsVisible);
                Assert.Null(control.FocusAdorner);
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.Equal(new Thickness(-4), ring.Margin);
                Assert.False(ring.IsHitTestVisible);
                Assert.Equal(new CornerRadius(square ? 10 : 999), ring.CornerRadius);
                Assert.Equal(control.Bounds.Width + 8, ring.Bounds.Width);
                Assert.Equal(control.Bounds.Height + 8, ring.Bounds.Height);
                Point ringPoint = ring.TranslatePoint(default, control)!.Value;
                Assert.Equal(new Point(-4, -4), ringPoint);
                Assert.Single(control.GetVisualDescendants().OfType<Border>(), part => part.Name == "ChoiceFocusRing");
                control.IsChecked = false;
                KeyStroke(host, Key.Space);
                Assert.True(control.IsChecked);
                foreach (Point point in new[] { new Point(24, 16), new Point(70, 16), new Point(control.Bounds.Width - 2, 2) })
                {
                    control.IsChecked = false;
                    Assert.True(before.Focus(NavigationMethod.Pointer));
                    Point location = control.TranslatePoint(point, host)!.Value;
                    host.MouseDown(location, MouseButton.Left);
                    host.MouseUp(location, MouseButton.Left);
                    Flush(host);
                    Assert.True(control.IsChecked);
                    Assert.False(ring.IsVisible);
                }
                control.IsEnabled = false;
                Assert.False(ring.IsVisible);
                Assert.True(control.IsChecked);
                Point center = control.TranslatePoint(new Point(60, 16), host)!.Value;
                host.MouseDown(center, MouseButton.Left);
                host.MouseUp(center, MouseButton.Left);
                Assert.True(control.IsChecked);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Pins 32 and 24 DIP rows, the eight DIP label gap, and top-aligned indicators on wrapped content.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeometryAndWrappingRemainStableDuringShapeAndThemeChanges(bool radio)
    {
        ToggleButton normal = Sample(radio, new("Checked", true), "Archive");
        ToggleButton compact = Sample(radio, new("Checked", true), "Archive");
        compact.Classes.Add("compact");
        ToggleButton wrapped = Sample(radio, new("Checked", true), "Include archived items from every collection and retain all original annotations for the next review.");
        var panel = new StackPanel { Width = 200, Spacing = 16, Margin = new Thickness(24), Children = { normal, compact, wrapped } };
        Window host = Create(panel, width: 300, height: 360);
        try
        {
            Show(host);
            var templates = new[] { normal.Template, compact.Template, wrapped.Template };
            foreach (bool dark in new[] { false, true })
            foreach (bool square in new[] { false, true, false })
            foreach (double scale in new[] { 1d, 1.25, 2 })
            {
                host.RequestedThemeVariant = dark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                host.SetRenderScaling(scale);
                Flush(host);
                Assert.Equal(32, normal.Bounds.Height);
                Assert.True(normal.Bounds.Height >= 32);
                Assert.Equal(24, compact.Bounds.Height);
                Assert.True(wrapped.Bounds.Height > 32);
                ToggleButton[] controls = [normal, compact, wrapped];
                for (int index = 0; index < controls.Length; index++)
                {
                    ToggleButton control = controls[index];
                    Assert.Same(templates[index], control.Template);
                    Border indicator = Part<Border>(control, "ChoiceIndicator");
                    var presenter = Part<global::Avalonia.Controls.Presenters.ContentPresenter>(control, "PART_ContentPresenter");
                    Assert.Equal(new Size(20, 20), indicator.Bounds.Size);
                    Point indicatorPoint = indicator.TranslatePoint(default, control)!.Value;
                    Point labelPoint = presenter.TranslatePoint(default, control)!.Value;
                    Assert.Equal(8, labelPoint.X - indicatorPoint.X - indicator.Bounds.Width);
                    Assert.Equal(indicatorPoint.Y, labelPoint.Y);
                    Assert.Equal(new CornerRadius(radio ? 999 : 6), indicator.CornerRadius);
                }
            }
        }
        finally { host.Close(); }
    }

    private static (string Fill, string Border) Expected(ChoiceState state)
    {
        if (state.Disabled) return (state.Checked is false ? "NfcSurfaceSubtleBrush" : "NfcTextDisabledBrush", "NfcTextDisabledBrush");
        if (state.Checked is not false)
            return (state.Pressed ? "Nvt.Toggle.SelectedPressedBrush" : state.Hover ? "Nvt.Toggle.SelectedPointerOverBrush" : "Nvt.Toggle.SelectedBrush",
                state.Pressed || state.Hover ? "NfcAccentBorderStrongBrush" : "NfcAccentBorderBrush");
        return (state.Pressed ? "NfcSelectionSurfaceBrush" : state.Hover ? "NfcSurfaceSubtleBrush" : "NfcSurfaceBrush",
            state.Pressed ? "NfcTextStrongBrush" : state.Hover ? "NfcTextSecondaryBrush" : "NfcBorderBrush");
    }
}
