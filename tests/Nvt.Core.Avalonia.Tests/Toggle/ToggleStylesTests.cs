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
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Checks shipped toggle states, contrast, input, and exact geometry.</summary>
public sealed class ToggleStylesTests(ITestOutputHelper output)
{
    /// <summary>Resolves every visible color through tokens and measures all state contrast requirements.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StatesUseTokensAndMeetContrast(bool dark, bool square)
    {
        double textMinimum = double.MaxValue, disabledMinimum = double.MaxValue;
        double ringMinimum = double.MaxValue, knobMinimum = double.MaxValue;
        foreach (string role in Roles)
        {
            ToggleButton button = Sample(role, new ToggleState("Rest"));
            var label = new TextBlock { Text = "Overview" };
            if (!IsSwitch(role)) button.Content = label;
            Control child = role == "toggleSegment" ? Group((StateToggleButton)button, Button(role, "Details")) : button;
            Control scope = child;
            Window host = Create(scope, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Assert.Equal(13, button.FontSize);
                if (child is Border group)
                {
                    Assert.Equal(new CornerRadius(square ? 6 : 999), group.CornerRadius);
                    Assert.Equal(48, group.Bounds.Height);
                }
                Assert.True(button.TryFindResource("NfcUiFontFamily", button.ActualThemeVariant, out object? fontRole));
                Assert.Equal(Assert.IsType<FontFamily>(fontRole), button.FontFamily);
                Assert.Equal(Color.Parse(dark ? "#5FA5FA" : "#1557E9"), ResourceColor(button, "NfcAccentBrush"));
                foreach (ToggleState state in States.Concat([
                    new ToggleState("Checked focus", Checked: true, Focus: true),
                    new ToggleState("Checked pressed", Checked: true, Pressed: true)]))
                {
                    SetState(button, state);
                    Flush(host);
                    (string background, string foreground) = Expected(role, state);
                    Assert.Equal(ResourceColor(button, background), ColorOf(button.Background));
                    Assert.Equal(ResourceColor(button, background), ColorOf(Part(button, "ToggleBody").Background));
                    Assert.Equal(ResourceColor(button, foreground), ColorOf(button.Foreground));
                    Assert.Equal(1, button.Opacity);
                    Assert.Null(button.FocusAdorner);
                    double scale = state.Pressed && !state.Disabled && button is not ToggleSwitch ? 0.98 : 1;
                    Assert.Equal(new Matrix(scale, 0, 0, scale, 0, 0), button.RenderTransform?.Value ?? Matrix.Identity);
                    if (!IsSwitch(role))
                    {
                        Assert.Equal(ColorOf(button.Foreground), ColorOf(label.Foreground));
                        if (state.Checked && !state.Disabled) Assert.Equal(Color.Parse("#FFFFFF"), ColorOf(label.Foreground));
                        Color surface = background == "Nvt.Toggle.TransparentBrush" ? ResourceColor(button, role == "toggleTab" ? "NfcSurfaceBrush" : "NfcSelectionSurfaceBrush") : ColorOf(button.Background);
                        double contrast = Contrast(ColorOf(label.Foreground), surface);
                        Assert.True(contrast >= (state.Disabled ? 3 : 4.5), $"{dark}/{role}/{state.Name}: text {contrast:F3}");
                        if (state.Disabled) disabledMinimum = Math.Min(disabledMinimum, contrast);
                        else textMinimum = Math.Min(textMinimum, contrast);
                    }
                    else
                    {
                        Ellipse knob = Assert.Single(button.GetVisualDescendants().OfType<Ellipse>());
                        Assert.Equal(Color.Parse("#FFFFFF"), ColorOf(knob.Fill));
                        double contrast = Contrast(ColorOf(knob.Fill), ColorOf(button.Background));
                        Assert.True(contrast >= (state.Checked && !state.Disabled ? 4.5 : 3), $"{dark}/{role}/{state.Name}: knob {contrast:F3}");
                        knobMinimum = Math.Min(knobMinimum, contrast);
                        VerifyKnob(button, state.Checked);
                    }
                    Border ring = Part(button, "ToggleFocusRing");
                    Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                    Assert.Equal(ResourceColor(button, "Nvt.Focus.RingBrush"), ColorOf(ring.BorderBrush));
                    foreach (string surface in new[] { "NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSelectionSurfaceBrush" })
                    {
                        double contrast = Contrast(ColorOf(ring.BorderBrush), ResourceColor(button, surface));
                        Assert.True(contrast >= 3, $"{dark}/{role}: ring {contrast:F3}");
                        ringMinimum = Math.Min(ringMinimum, contrast);
                    }
                }
                foreach (ToggleState state in States.Where(state => state.Disabled))
                {
                    SetState(button, state with { Hover = true, Pressed = true, Focus = true });
                    Flush(host);
                    (string background, string foreground) = Expected(role, state);
                    Assert.Equal(ResourceColor(button, background), ColorOf(button.Background));
                    Assert.Equal(ResourceColor(button, foreground), ColorOf(button.Foreground));
                    Assert.False(Part(button, "ToggleFocusRing").IsVisible);
                }
            }
            finally { host.Close(); }
        }
        output.WriteLine($"Shipped/{(dark ? "dark" : "light")}/{(square ? "square" : "pill")}: text {textMinimum:F3}, disabled {disabledMinimum:F3}, ring {ringMinimum:F3}, knob {knobMinimum:F3}");
    }

    /// <summary>Uses real Tab and pointer focus, preserving one ring with a two-pixel exterior gap.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void KeyboardAndPointerInputWorkForEveryRole(bool dark, bool square)
    {
        foreach (string role in Roles.Append("toggleSoft"))
        foreach (bool selected in new[] { false, true })
        {
            var before = new Grid { Focusable = true, Height = 20 };
            ToggleButton button = Sample(role, new ToggleState("Rest", Checked: selected));
            Control control = role == "toggleSegment" ? Group((StateToggleButton)button, Button(role)) : button;
            var stack = new StackPanel { Margin = new Thickness(16), Spacing = 16, Children = { before, control } };
            Window host = Create(stack, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Assert.True(button.Focus(NavigationMethod.Pointer));
                Assert.False(Part(button, "ToggleFocusRing").IsVisible);
                Assert.True(before.Focus());
                host.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                host.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Flush(host);
                Assert.Same(button, host.FocusManager!.GetFocusedElement());
                Border ring = Part(button, "ToggleFocusRing");
                Assert.True(ring.IsVisible);
                Assert.Null(button.FocusAdorner);
                Assert.Single(button.GetVisualDescendants().OfType<Border>(), border => border.Name == "ToggleFocusRing");
                Assert.Equal(new Thickness(-4), ring.Margin);
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.False(ring.IsHitTestVisible);
                Border body = Part(button, "ToggleBody");
                Assert.Equal(body.Bounds.Width + 8, ring.Bounds.Width);
                Assert.Equal(body.Bounds.Height + 8, ring.Bounds.Height);
                Point bodyPoint = body.TranslatePoint(default, button)!.Value;
                Point ringPoint = ring.TranslatePoint(default, button)!.Value;
                Assert.Equal(4, bodyPoint.X - ringPoint.X);
                Assert.Equal(4, bodyPoint.Y - ringPoint.Y);
                Assert.Equal(new CornerRadius(IsSwitch(role) || !square ? 999 : 10), ring.CornerRadius);
                button.Classes.Add("danger");
                Assert.Equal(ResourceColor(button, "Nvt.Focus.RingBrush"), ColorOf(ring.BorderBrush));
                foreach (string surface in new[] { "NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSelectionSurfaceBrush" })
                    Assert.True(Contrast(ColorOf(ring.BorderBrush), ResourceColor(button, surface)) >= 3);
                host.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                host.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Flush(host);
                Assert.Equal(!selected, button.IsChecked);
                if (IsSwitch(role)) VerifyKnob(button, !selected);
                Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), host)!.Value;
                host.MouseDown(center, MouseButton.Left);
                host.MouseUp(center, MouseButton.Left);
                Flush(host);
                Assert.Equal(selected, button.IsChecked);
                if (IsSwitch(role)) VerifyKnob(button, selected);
                button.IsEnabled = false;
                Assert.False(ring.IsVisible);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Checks real render scaling, resizing, and switch inset parity without exporting additional images.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeometrySurvivesResizingAndDpi(bool dark, bool square)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var segment = (StateToggleButton)Sample("toggleSegment", new ToggleState("Rest"));
        segment.Width = 100;
        var peer = (StateToggleButton)Sample("toggleSegment", new ToggleState("Checked", Checked: true));
        peer.Width = 100;
        row.Children.Add(Group(segment, peer));
        foreach (string role in new[] { "toggleTab", "toggleIcon", "toggleSwitch", "nativeSwitch" })
            row.Children.Add(Sample(role, new ToggleState("Checked", Checked: true)));
        Window host = Create(row, dark, width: 720, height: 160);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            Show(host);
            Restore(row);
            foreach (double width in new[] { 640d, 720d, 960d })
            foreach (double scaling in new[] { 1d, 1.25, 1.5, 2 })
            {
                host.Width = width;
                host.SetRenderScaling(scaling);
                Flush(host);
                ToggleStylesRenderer.CheckBounds(host);
                foreach (ToggleButton button in row.GetVisualDescendants().OfType<ToggleButton>())
                {
                    string role = button is ToggleSwitch ? "nativeSwitch" : button.Classes.First();
                    Assert.Equal(40, button.Bounds.Height);
                    Assert.Equal(0, button.BorderThickness.Left);
                    if (IsSwitch(role)) VerifyKnob(button, true);
                }
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal((int)(width * scaling), frame.PixelSize.Width);
                Assert.Equal((int)(160 * scaling), frame.PixelSize.Height);
            }
        }
        finally { host.Close(); }
    }

    internal static bool IsSwitch(string role) => role is "toggleSwitch" or "nativeSwitch";

    private static (string Background, string Foreground) Expected(string role, ToggleState state)
    {
        if (IsSwitch(role))
        {
            string track = state.Disabled ? state.Checked ? "NfcTextDisabledBrush" : "NfcBorderBrush"
                : state.Checked ? state.Pressed ? "Nvt.Toggle.SwitchPressedBrush"
                    : state.Hover ? "Nvt.Toggle.SwitchPointerOverBrush" : "Nvt.Toggle.SwitchOnBrush"
                : state.Pressed ? "NfcBorderBrush" : state.Hover ? "NfcTextDisabledBrush" : "NfcBorderBrush";
            return (track, "NfcTextSecondaryBrush");
        }
        if (state.Disabled) return (state.Checked ? "NfcSelectionSurfaceBrush" : "NfcSurfaceSubtleBrush", "NfcTextDisabledBrush");
        if (state.Checked) return (state.Pressed ? "Nvt.Toggle.SelectedPressedBrush"
            : state.Hover ? "Nvt.Toggle.SelectedPointerOverBrush" : "Nvt.Toggle.SelectedBrush", "Nvt.Toggle.SelectedLabelBrush");
        return (state.Pressed ? "NfcSecondaryActionPressedBrush"
            : state.Hover ? role == "toggleSegment" ? "NfcSurfaceSubtleBrush" : "NfcSelectionSurfaceBrush"
            : role is "toggleSegment" or "toggleTab" ? "Nvt.Toggle.TransparentBrush" : "NfcSurfaceSubtleBrush",
            state.Pressed ? "NfcTextStrongBrush" : state.Hover ? "NfcTextBrush" : "NfcTextSecondaryBrush");
    }

    private static void VerifyKnob(ToggleButton button, bool selected)
    {
        Ellipse knob = Assert.Single(button.GetVisualDescendants().OfType<Ellipse>());
        Border track = Part(button, "ToggleBody");
        Assert.True(track.CornerRadius.TopLeft >= track.Bounds.Height / 2);
        Assert.Equal(new CornerRadius(track.CornerRadius.TopLeft), track.CornerRadius);
        if (selected) Assert.Equal(Color.Parse("#FFFFFF"), ColorOf(knob.Fill));
        Point point = knob.TranslatePoint(default, track)!.Value;
        Assert.Equal(3, point.Y);
        Assert.Equal(3, track.Bounds.Height - point.Y - knob.Bounds.Height);
        Assert.Equal(3, selected ? track.Bounds.Width - point.X - knob.Bounds.Width : point.X);
        Assert.Equal(track.Bounds.Height - 6, knob.Bounds.Width);
        Assert.Equal(knob.Bounds.Width, knob.Bounds.Height);
        Assert.Equal(new Size(52, 28), track.Bounds.Size);
        Assert.Equal(new Size(22, 22), knob.Bounds.Size);
        Grid moving = Assert.Single(button.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "PART_MovingKnobs");
        Assert.Equal(new Size(22, 22), moving.Bounds.Size);
        Assert.Equal(selected ? 24 : 0, Canvas.GetLeft(moving));
        Assert.Equal(selected ? 27 : 3, point.X);
    }

    internal static double Contrast(Color a, Color b)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
