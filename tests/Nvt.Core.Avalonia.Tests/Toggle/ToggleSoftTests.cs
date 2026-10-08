// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Checks soft tonal colors, inherited content, runtime resources, shapes, and disabled input.</summary>
public sealed class ToggleSoftTests(ITestOutputHelper output)
{
    internal static readonly (string Soft, string Core)[] Tokens =
    [
        ("SoftCheckedBrush", "NfcAccentSurfaceBrush"),
        ("SoftCheckedForegroundBrush", "NfcAccentStrongBrush"),
        ("SoftPointerOverBrush", "NfcSelectionSurfaceBrush"),
        ("SoftPressedBrush", "NfcSecondaryActionPressedBrush"),
        ("SoftForegroundBrush", "NfcTextSecondaryBrush"),
        ("SoftPointerOverForegroundBrush", "NfcTextBrush"),
        ("SoftPressedForegroundBrush", "NfcTextStrongBrush"),
        ("SoftDisabledForegroundBrush", "NfcTextDisabledBrush"),
        ("SoftDisabledCheckedBrush", "NfcSelectionSurfaceBrush"),
    ];

    /// <summary>Checks text and icon colors, state precedence, and contrast on both themes and shapes.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatesUseTonalTokensAndAccessibleInheritedContent(bool icon)
    {
        ToggleButton button = SampleContent(new ToggleState("Rest"), icon);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { button } };
        Window host = Create(row, false);
        try
        {
            Show(host);
            foreach (bool dark in new[] { false, true, false })
            foreach (ThemeShape shape in new[] { ThemeShape.Pill, ThemeShape.Square })
            {
                host.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, shape);
                Flush(host);
                foreach ((string soft, string core) in Tokens)
                    Assert.Equal(ResourceColor(button, core), ResourceColor(button, "Nvt.Toggle." + soft));
                Color rest = ResourceColor(button, "Nvt.Toggle.TransparentBrush");
                Assert.Equal(0, rest.A);
                Assert.NotEqual(rest, ResourceColor(button, "Nvt.Toggle.SoftCheckedBrush"));
                foreach (ToggleState state in States.Concat(
                [
                    new ToggleState("Disabled feedback", Hover: true, Pressed: true, Disabled: true, Focus: true),
                    new ToggleState("Disabled checked feedback", Checked: true, Hover: true, Pressed: true, Disabled: true, Focus: true),
                    new ToggleState("Checked focus", Checked: true, Focus: true),
                ]))
                {
                    SetState(button, state);
                    Flush(host);
                    (string background, string foreground) = Expected(state);
                    Assert.Equal(ResourceColor(button, background), ColorOf(button.Background));
                    Assert.Equal(ResourceColor(button, foreground), ColorOf(button.Foreground));
                    Assert.Equal(ColorOf(button.Background), ColorOf(Part(button, "ToggleBody").Background));
                    Assert.Equal(32, button.Bounds.Height);
                    Assert.Equal(new Thickness(12, 0), button.Padding);
                    Assert.Equal(new Thickness(0), button.BorderThickness);
                    Assert.Equal(0, ColorOf(button.BorderBrush).A);
                    Assert.Equal(1, button.Opacity);
                    Assert.Equal(state.Pressed && !state.Disabled ? Matrix.CreateScale(.98, .98) : Matrix.Identity,
                        button.RenderTransform?.Value ?? Matrix.Identity);
                    Assert.False(button.ClipToBounds);
                    Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 6), button.CornerRadius);
                    Border ring = Part(button, "ToggleFocusRing");
                    Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                    Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 10), ring.CornerRadius);
                    TextBlock[] content = button.GetVisualDescendants().OfType<TextBlock>().ToArray();
                    Assert.Equal(icon ? 2 : 1, content.Length);
                    foreach (TextBlock text in content)
                    {
                        Assert.Equal(ColorOf(button.Foreground), ColorOf(text.Foreground));
                        if (state.Disabled) continue;
                        foreach (string surface in new[] { "NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush" })
                        {
                            Color fill = ColorOf(button.Background);
                            Color backdrop = fill.A == 0 ? ResourceColor(button, surface) : fill;
                            double ratio = ToggleStylesTests.Contrast(ColorOf(text.Foreground), backdrop);
                            Assert.True(ratio >= 4.5, $"{dark}/{shape}/{state.Name}/{text.Text}: {ratio:F3}:1");
                            if (shape == ThemeShape.Pill && surface == "NfcSurfaceSubtleBrush")
                                output.WriteLine($"{(dark ? "Dark" : "Light")} {state.Name}: {ratio:F3}:1");
                        }
                    }
                }
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Updates attached body and focus corners through the existing shape setting without replacing templates.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeShapeSwitchUpdatesBodyAndFocusRing(bool dark)
    {
        ToggleButton button = SampleContent(new ToggleState("Checked", Checked: true), true);
        Window host = Create(button, dark);
        try
        {
            Show(host);
            var template = button.Template;
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill, ThemeShape.Square })
            {
                ThemeShapes.SetShape(host.Resources, shape);
                Assert.True(button.Focus(NavigationMethod.Tab));
                Flush(host);
                Assert.Same(template, button.Template);
                Border body = Part(button, "ToggleBody");
                Border ring = Part(button, "ToggleFocusRing");
                Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 6), body.CornerRadius);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 10), ring.CornerRadius);
                Assert.Equal(ResourceColor(button, "Nvt.Focus.RingBrush"), ColorOf(ring.BorderBrush));
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.Equal(new Thickness(-4), ring.Margin);
                Assert.True(ring.IsVisible);
                Assert.False(ring.IsHitTestVisible);
                Assert.Equal(body.Bounds.Width + 8, ring.Bounds.Width);
                Assert.Equal(body.Bounds.Height + 8, ring.Bounds.Height);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Keeps the checked state visible while disabled and still distinguishable from the unchecked state.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledCheckedStaysDistinctFromDisabledUnchecked(bool dark)
    {
        ToggleButton button = SampleContent(new ToggleState("Disabled checked", Checked: true, Disabled: true), true);
        Window host = Create(button, dark);
        try
        {
            Show(host);
            Flush(host);
            Color checkedFill = ColorOf(Part(button, "ToggleBody").Background);
            Assert.NotEqual(0, checkedFill.A);
            Assert.Equal(ResourceColor(button, "NfcSelectionSurfaceBrush"), checkedFill);
            double textRatio = ToggleStylesTests.Contrast(ColorOf(button.Foreground), checkedFill);
            Assert.True(textRatio >= 3, $"{(dark ? "Dark" : "Light")} disabled checked text: {textRatio:F3}:1");
            output.WriteLine($"{(dark ? "Dark" : "Light")} disabled checked text: {textRatio:F3}:1");
            SetState(button, new ToggleState("Disabled", Disabled: true));
            Flush(host);
            Assert.Equal(0, ColorOf(Part(button, "ToggleBody").Background).A);
            Assert.NotEqual(checkedFill, ColorOf(Part(button, "ToggleBody").Background));
        }
        finally { host.Close(); }
    }

    /// <summary>Requires the focus ring to reach 3:1 against the page surfaces and the checked tint.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusRingContrastsWithSurfacesAndCheckedTint(bool dark)
    {
        ToggleButton button = SampleContent(new ToggleState("Checked", Checked: true), false);
        Window host = Create(button, dark);
        try
        {
            Show(host);
            Flush(host);
            Color ring = ResourceColor(button, "Nvt.Focus.RingBrush");
            foreach (string surface in new[] { "NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush", "Nvt.Toggle.SoftCheckedBrush" })
            {
                double ratio = ToggleStylesTests.Contrast(ring, ResourceColor(button, surface));
                Assert.True(ratio >= 3, $"{(dark ? "Dark" : "Light")} ring on {surface}: {ratio:F3}:1");
                output.WriteLine($"{(dark ? "Dark" : "Light")} ring on {surface}: {ratio:F3}:1");
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Replaces one soft palette dictionary and updates every state on an attached control.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DictionaryReplacementUpdatesAllSoftStates(bool dark)
    {
        ToggleButton button = SampleContent(new ToggleState("Rest"), true);
        Window host = Create(button, dark);
        try
        {
            Show(host);
            ResourceDictionary first = Palette(40);
            host.Resources.MergedDictionaries.Add(first);
            Verify(first);
            ResourceDictionary second = Palette(100);
            host.Resources.MergedDictionaries[0] = second;
            Verify(second);
        }
        finally { host.Close(); }

        void Verify(ResourceDictionary palette)
        {
            foreach (ToggleState state in States)
            {
                SetState(button, state);
                Flush(host);
                (string background, string foreground) = Expected(state);
                Assert.Same(palette[background], button.Background);
                Assert.Same(palette[foreground], button.Foreground);
                Assert.Equal(ColorOf(button.Foreground), ColorOf(Assert.Single(
                    button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Matches only").Foreground));
                Assert.Same(palette["Nvt.Focus.RingBrush"], Part(button, "ToggleFocusRing").BorderBrush);
            }
        }

        static ResourceDictionary Palette(byte offset)
        {
            var palette = new ResourceDictionary();
            for (int index = 0; index < Tokens.Length; index++)
                palette["Nvt.Toggle." + Tokens[index].Soft] = new SolidColorBrush(Color.FromRgb((byte)(offset + index), 80, 160));
            palette["Nvt.Toggle.TransparentBrush"] = new SolidColorBrush(Colors.Transparent);
            palette["Nvt.Focus.RingBrush"] = new SolidColorBrush(Color.FromRgb(offset, 160, 80));
            return palette;
        }
    }

    /// <summary>Ignores real pointer and keyboard input while disabled and skips the disabled control during Tab navigation.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledButtonDoesNotToggleOrReceiveTabFocus(bool selected)
    {
        var before = new Grid { Focusable = true, Height = 20 };
        var after = new Grid { Focusable = true, Height = 20 };
        ToggleButton button = SampleContent(new ToggleState("Rest", Checked: selected), true);
        Window host = Create(new StackPanel { Spacing = 16, Children = { before, button, after } }, false);
        try
        {
            Show(host);
            Assert.True(button.Focus(NavigationMethod.Tab));
            button.IsEnabled = false;
            Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, 16), host)!.Value;
            host.MouseMove(center);
            host.MouseDown(center, MouseButton.Left);
            host.MouseUp(center, MouseButton.Left);
            host.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            host.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Flush(host);
            Assert.Equal(selected, button.IsChecked);
            Assert.False(Part(button, "ToggleFocusRing").IsVisible);
            Assert.True(before.Focus());
            host.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            host.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Flush(host);
            Assert.Same(after, host.FocusManager!.GetFocusedElement());
            Assert.Equal(selected, button.IsChecked);
        }
        finally { host.Close(); }
    }

    internal static ToggleButton SampleContent(ToggleState state, bool icon, string label = "Matches only", string glyph = "\ue152")
    {
        ToggleButton button = Sample("toggleSoft", state, label);
        if (!icon) return button;
        var fonts = new Uri("avares://Nvt.Core.Fonts/FontRoles.axaml");
        button.Resources.MergedDictionaries.Add(new ResourceInclude(fonts) { Source = fonts });
        var symbol = new TextBlock { Text = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        symbol.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("Nvt.Font.Icon.Family"));
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { symbol, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
        };
        return button;
    }

    private static (string Background, string Foreground) Expected(ToggleState state)
    {
        if (state.Disabled) return (state.Checked ? "Nvt.Toggle.SoftDisabledCheckedBrush" : "Nvt.Toggle.TransparentBrush", "Nvt.Toggle.SoftDisabledForegroundBrush");
        string background = state.Checked ? state.Hover || state.Pressed ? "SoftPointerOverBrush" : "SoftCheckedBrush"
            : state.Pressed ? "SoftPressedBrush" : state.Hover ? "SoftPointerOverBrush" : "TransparentBrush";
        string foreground = state.Checked ? "SoftCheckedForegroundBrush"
            : state.Pressed ? "SoftPressedForegroundBrush" : state.Hover ? "SoftPointerOverForegroundBrush" : "SoftForegroundBrush";
        return ("Nvt.Toggle." + background, "Nvt.Toggle." + foreground);
    }
}
