// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Theme;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Checks proposal colors, accessibility, geometry and isolation from Fluent controls.</summary>
public sealed class ToggleStylesTests(ITestOutputHelper output)
{
    /// <summary>Resolves every state through tokens and measures the displayed text and ring contrast.</summary>
    [AvaloniaTheory]
    [InlineData("toggleSegment")]
    [InlineData("toggleTab")]
    [InlineData("toggleIcon")]
    public void StatesResolveTokensAndMeetContrast(string role)
    {
        double minimumText = double.MaxValue, minimumDisabled = double.MaxValue, minimumRing = double.MaxValue;
        foreach (bool dark in new[] { false, true })
        for (int tool = 0; tool < 3; tool++)
        {
            var label = new TextBlock { Text = role == "toggleIcon" ? "+" : "Option" };
            label.Classes.Add("nvtIcon");
            var button = ToggleTestHost.Button(role);
            button.Content = label;
            Control content = role == "toggleSegment" ? ToggleTestHost.Group(button, ToggleTestHost.Button(role)) : button;
            Window host = ToggleTestHost.Create(content, dark, tool);
            try
            {
                ToggleTestHost.Show(host);
                foreach (ToggleState state in ToggleTestHost.States)
                {
                    button.SetState(state);
                    ToggleTestHost.Flush(host);
                    (string background, string border, string foreground) = Expected(role, state);
                    AssertColor(button, background, button.Background);
                    AssertColor(button, border, button.BorderBrush);
                    AssertColor(button, foreground, button.Foreground);
                    Border body = ToggleTestHost.Part(button, "ToggleBody");
                    AssertColor(button, background, body.Background);
                    AssertColor(button, border, body.BorderBrush);
                    AssertColor(button, foreground, label.Foreground);
                    Assert.Equal(1, button.Opacity);
                    Assert.Null(button.FocusAdorner);
                    if (role == "toggleSegment") Assert.Equal(state.Focus && !state.Disabled ? 2 : state.Checked ? 1 : 0, button.ZIndex);
                    Assert.Equal(32, button.Bounds.Height);
                    Assert.Equal(32, button.Height);
                    Color surface = Composite(ToggleTestHost.ColorOf(body.Background), ToggleTestHost.ColorOf(host.Background));
                    double textContrast = Contrast(Composite(ToggleTestHost.ColorOf(label.Foreground), surface), surface);
                    double required = state.Disabled ? 3 : 4.5;
                    Assert.True(textContrast >= required, $"{role}, {state.Name}, {dark}, Tool {tool}: text {textContrast:F3}");
                    if (state.Disabled) minimumDisabled = Math.Min(minimumDisabled, textContrast);
                    else minimumText = Math.Min(minimumText, textContrast);
                    if (role == "toggleTab")
                    {
                        Assert.Equal(Colors.Transparent, ToggleTestHost.ColorOf(body.Background));
                        Assert.Equal(new Thickness(0, 0, 0, state.Checked ? 2 : 0), button.BorderThickness);
                        Assert.Equal(new Thickness(0), body.BorderThickness);
                        Border underline = ToggleTestHost.Part(button, "ToggleTabUnderline");
                        Assert.Equal(state.Checked ? 2 : 0, underline.Height);
                        Assert.Equal(new CornerRadius(0), underline.CornerRadius);
                        AssertColor(button, border, underline.Background);
                    }
                    Border ring = ToggleTestHost.Part(button, "ToggleFocusRing");
                    Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                    AssertColor(button, "Nvt.Focus.RingBrush", ring.BorderBrush);
                    double ringContrast = Contrast(ToggleTestHost.ColorOf(ring.BorderBrush), ToggleTestHost.ColorOf(host.Background));
                    Assert.True(ringContrast >= 3, $"{role}, {dark}: ring {ringContrast:F3}");
                    // A segment ring also crosses its neutral group surface beside the shared edge.
                    if (content is Border group)
                        Assert.True(Contrast(ToggleTestHost.ColorOf(ring.BorderBrush), ToggleTestHost.ColorOf(group.Background)) >= 3);
                    minimumRing = Math.Min(minimumRing, ringContrast);
                }
                foreach (ToggleState disabled in ToggleTestHost.States.Where(state => state.Disabled))
                {
                    button.SetState(disabled with { Hover = true, Pressed = true, Focus = true });
                    (string background, string border, string foreground) = Expected(role, disabled);
                    AssertColor(button, background, button.Background);
                    AssertColor(button, border, button.BorderBrush);
                    AssertColor(button, foreground, button.Foreground);
                    Assert.False(ToggleTestHost.Part(button, "ToggleFocusRing").IsVisible);
                }
            }
            finally { host.Close(); }
        }
        output.WriteLine($"{role}: text >= {minimumText:F3}:1, disabled >= {minimumDisabled:F3}:1, ring >= {minimumRing:F3}:1");
    }

    /// <summary>Overlaps each shared stroke once and rounds only the first and last exterior corners.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SegmentGroupsShareEdges(int count)
    {
        foreach (bool dark in new[] { false, true })
        {
            var buttons = Enumerable.Range(0, count).Select(_ => ToggleTestHost.Button("toggleSegment", "One")).ToArray();
            foreach (var button in buttons) button.Width = 64;
            Border group = ToggleTestHost.Group(buttons);
            Window host = ToggleTestHost.Create(group, dark);
            try
            {
                ToggleTestHost.Show(host);
                Assert.Equal(new CornerRadius(8), group.CornerRadius);
                Assert.Equal(new Thickness(1), group.BorderThickness);
                Assert.Equal(32, group.Bounds.Height);
                Assert.Equal(64 * count - (count - 1), group.Bounds.Width);
                var panel = Assert.IsType<StackPanel>(group.Child);
                Assert.Equal(Orientation.Horizontal, panel.Orientation);
                Assert.Equal(0, panel.Spacing);
                Assert.Equal(new Thickness(-1), panel.Margin);
                Assert.False(group.ClipToBounds);
                for (int index = 0; index < count; index++)
                {
                    var button = buttons[index];
                    Assert.Equal(new Thickness(1), button.BorderThickness);
                    Assert.Equal(index == 0 ? new Thickness(0) : new Thickness(-1, 0, 0, 0), button.Margin);
                    Assert.Equal(count == 1 ? new CornerRadius(8) : index == 0 ? new CornerRadius(8, 0, 0, 8)
                        : index == count - 1 ? new CornerRadius(0, 8, 8, 0) : new CornerRadius(0), button.CornerRadius);
                    Assert.Equal(button.CornerRadius, ToggleTestHost.Part(button, "ToggleBody").CornerRadius);
                    if (index != 0) Assert.Equal(buttons[index - 1].Bounds.Right - 1, button.Bounds.Left);
                }
                group.CornerRadius = new CornerRadius(6);
                if (count == 1)
                {
                    Assert.Equal(new CornerRadius(6), buttons[0].CornerRadius);
                    continue;
                }

                Assert.Equal(new CornerRadius(6, 0, 0, 6), buttons[0].CornerRadius);
                Assert.Equal(new CornerRadius(0, 6, 6, 0), buttons[^1].CornerRadius);
                panel.Children.RemoveAt(0);
                Assert.Equal(new Thickness(0), buttons[1].Margin);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Uses real Tab navigation, suppresses pointer rings, and follows live corners without a focus adorner.</summary>
    [AvaloniaTheory]
    [InlineData("toggleSegment")]
    [InlineData("toggleTab")]
    [InlineData("toggleIcon")]
    public void KeyboardRingFollowsCornersAndDanger(string role)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool selected in new[] { false, true })
        {
            var before = new Grid { Focusable = true, Height = 20 };
            var button = ToggleTestHost.Button(role);
            Control content = role == "toggleSegment" ? ToggleTestHost.Group(button, ToggleTestHost.Button(role)) : button;
            Window host = ToggleTestHost.Create(new StackPanel { Margin = new Thickness(12), Children = { before, content } }, dark);
            try
            {
                ToggleTestHost.Show(host);
                button.IsChecked = selected;
                var colors = (button.Background, button.BorderBrush, button.Foreground);
                Assert.True(button.Focus(NavigationMethod.Pointer));
                Assert.False(ToggleTestHost.Part(button, "ToggleFocusRing").IsVisible);
                Assert.True(before.Focus());
                host.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                host.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                ToggleTestHost.Flush(host);
                Assert.Same(button, host.FocusManager!.GetFocusedElement());
                Border ring = ToggleTestHost.Part(button, "ToggleFocusRing");
                Assert.True(ring.IsVisible);
                Assert.Null(button.FocusAdorner);
                Assert.Equal(new Thickness(-4), ring.Margin);
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.False(ring.IsHitTestVisible);
                Assert.Equal(button.Bounds.Width + 8, ring.Bounds.Width);
                Assert.Equal(40, ring.Bounds.Height);
                Assert.Equal(colors, (button.Background, button.BorderBrush, button.Foreground));
                Assert.Equal(role == "toggleSegment" ? new CornerRadius(12, 4, 4, 12) : new CornerRadius(10), ring.CornerRadius);
                if (role == "toggleIcon") Assert.Equal(new Size(32, 32), button.Bounds.Size);
                button.CornerRadius = new CornerRadius(8);
                Assert.Equal(new CornerRadius(12), ring.CornerRadius);
                button.Classes.Add("danger");
                AssertColor(button, "Nvt.Focus.DangerRingBrush", ring.BorderBrush);
                Assert.True(Contrast(ToggleTestHost.ColorOf(ring.BorderBrush), ToggleTestHost.ColorOf(host.Background)) >= 3);
                button.IsEnabled = false;
                Assert.False(ring.IsVisible);
                Assert.True(before.Focus(NavigationMethod.Pointer));
                Assert.False(ring.IsVisible);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Updates an existing selected control when resources and the requested theme change.</summary>
    [AvaloniaTheory]
    [InlineData("toggleSegment")]
    [InlineData("toggleTab")]
    [InlineData("toggleIcon")]
    public void TokensRemainDynamic(string role)
    {
        var button = ToggleTestHost.Button(role);
        Window host = ToggleTestHost.Create(button, false);
        try
        {
            ToggleTestHost.Show(host);
            button.SetState(new ToggleState("Selected focus", Checked: true, Focus: true));
            Color light = ToggleTestHost.ColorOf(button.Foreground);
            host.RequestedThemeVariant = ThemeVariant.Dark;
            ToggleTestHost.Flush(host);
            Assert.NotEqual(light, ToggleTestHost.ColorOf(button.Foreground));
            (string background, string border, string foreground) = Expected(role, new ToggleState("Selected", Checked: true));
            foreach (string key in new[] { background, border, foreground, "Nvt.Focus.RingBrush" }.Where(key => key != "Transparent").Distinct())
                host.Resources[key] = new SolidColorBrush(Color.Parse("#A05BB5"));
            ToggleTestHost.Flush(host);
            AssertColor(button, background, button.Background);
            AssertColor(button, border, button.BorderBrush);
            AssertColor(button, foreground, button.Foreground);
            AssertColor(button, "Nvt.Focus.RingBrush", ToggleTestHost.Part(button, "ToggleFocusRing").BorderBrush);
        }
        finally { host.Close(); }
    }

    /// <summary>A plain ToggleButton keeps its Fluent template and every state after the proposal loads.</summary>
    [AvaloniaFact]
    public void PlainToggleKeepsFluentAndSelectorsStayScoped()
    {
        foreach (var style in ThemeContractTests.ReadExtracted("ToggleStyles").Descendants(ThemeContractTests.Presentation + "Style"))
        foreach (string branch in style.Attribute("Selector")!.Value.Split(','))
            Assert.Matches(@"^\s*(?:ToggleButton\.(?:toggleSegment|toggleTab|toggleIcon)|Border\.toggleSegmentGroup)(?:[\s:.]|$)", branch);
        foreach (bool dark in new[] { false, true })
        {
            var button = ToggleTestHost.Button("unrelated");
            Window host = ToggleTestHost.Create(button, dark, proposal: false);
            try
            {
                ToggleTestHost.Show(host);
                var template = button.Template;
                Assert.NotNull(template);
                var snapshots = new List<(IBrush?, IBrush?, IBrush?, Thickness, double, object?, object?)>();
                foreach (ToggleState state in ToggleTestHost.States)
                {
                    button.SetState(state);
                    snapshots.Add((button.Background, button.BorderBrush, button.Foreground, button.Padding, button.Height, button.Theme, button.FocusAdorner));
                }
                ToggleTestHost.Include(host, "avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml");
                for (int index = 0; index < ToggleTestHost.States.Length; index++)
                {
                    button.SetState(ToggleTestHost.States[index]);
                    Assert.Equal(snapshots[index], (button.Background, button.BorderBrush, button.Foreground, button.Padding, button.Height, button.Theme, button.FocusAdorner));
                    Assert.Same(template, button.Template);
                    Assert.DoesNotContain(button.GetVisualDescendants().OfType<Border>(), border => border.Name == "ToggleBody");
                }
            }
            finally { host.Close(); }
        }
    }

    private static (string Background, string Border, string Foreground) Expected(string role, ToggleState state)
    {
        if (role == "toggleTab")
        {
            if (state.Disabled) return ("Transparent", state.Checked ? "NfcBorderMutedBrush" : "Transparent", "NfcTextDisabledBrush");
            if (state.Checked) return ("Transparent", state.Hover || state.Pressed ? "NfcAccentBorderStrongBrush" : "NfcAccentBorderBrush", "NfcTextStrongBrush");
            return ("Transparent", "Transparent", state.Pressed ? "NfcTextStrongBrush" : state.Hover ? "NfcTextBrush" : "NfcTextSecondaryBrush");
        }
        if (state.Disabled) return (state.Checked ? "NfcSelectionSurfaceBrush" : "NfcSurfaceSubtleBrush", "NfcBorderMutedBrush", "NfcTextDisabledBrush");
        if (state.Checked) return (state.Pressed ? "NfcSecondaryActionPressedBrush" : state.Hover ? "NfcAccentSurfaceSubtleBrush" : "NfcAccentSurfaceBrush",
            state.Hover || state.Pressed ? "NfcAccentBorderStrongBrush" : "NfcAccentBorderBrush", "NfcAccentStrongBrush");
        return (state.Pressed ? "NfcSecondaryActionPressedBrush" : state.Hover ? "NfcSelectionSurfaceBrush" : "NfcSurfaceBrush", "NfcBorderBrush",
            state.Pressed ? "NfcTextStrongBrush" : state.Hover ? "NfcTextBrush" : "NfcTextSecondaryBrush");
    }

    private static void AssertColor(Control owner, string key, IBrush? actual) =>
        Assert.Equal(ToggleTestHost.ResourceColor(owner, key), ToggleTestHost.ColorOf(actual));

    private static Color Composite(Color foreground, Color background)
    {
        double alpha = foreground.A / 255d;
        return Color.FromRgb((byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
