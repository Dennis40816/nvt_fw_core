// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Characterizes NFH's generic action roles with the Core palettes and keyboard ring.</summary>
public sealed class ActionRoleStylesTests
{
    /// <summary>Checks both control types and palettes, including the source's checked-state cascade.</summary>
    [AvaloniaTheory]
    [InlineData("actionTextButton", "actionNeutral")]
    [InlineData("actionTextButton", "actionPrimary")]
    [InlineData("actionTextButton", "actionDanger")]
    [InlineData("actionTextButton", "actionGhost")]
    [InlineData("actionIconButton", "actionNeutral")]
    [InlineData("actionIconButton", "actionPrimary")]
    [InlineData("actionIconButton", "actionDanger")]
    [InlineData("actionIconButton", "actionGhost")]
    [InlineData("actionChip", "actionNeutral")]
    [InlineData("actionChip", "actionPrimary")]
    [InlineData("actionChip", "actionDanger")]
    [InlineData("actionChip", "actionGhost")]
    [InlineData("actionChip", "chipAction")]
    [InlineData("actionChip", "chipAction active")]
    public void ActionStatesResolveMappedColors(string primitive, string role)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool toggle in new[] { false, true })
        {
            var text = new TextBlock { Text = "Action" };
            Button button = CreateButton(primitive, role, toggle, text);
            Window host = CreateHost(button, dark);
            try
            {
                Show(host);
                foreach (string state in new[] { "rest", "hover", "pressed", "disabled", "checked", "checkedHover", "checkedPressed" })
                {
                    if (!toggle && state.StartsWith("checked", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    SetState(button, state);
                    (string background, string border, string foreground) = ExpectedColors(primitive, role, state);
                    AssertBrush(button, background, button.Background);
                    AssertBrush(button, border, button.BorderBrush);
                    AssertBrush(button, foreground, button.Foreground);
                    Border frame = Frame(button);
                    AssertBrush(button, background, frame.Background);
                    AssertBrush(button, border, frame.BorderBrush);
                    AssertBrush(button, foreground, text.Foreground);
                    Assert.Equal(role.StartsWith("chipAction", StringComparison.Ordinal) && state == "disabled" ? 0.56 : 1, button.Opacity);
                }
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Preserves the literal NFH dimensions, pill geometry, clipping and owned templates.</summary>
    [AvaloniaTheory]
    [InlineData("actionTextButton", "10,6", 0, 6)]
    [InlineData("actionIconButton", "0", 0, 999)]
    [InlineData("actionChip", "8,4", 1.5, 999)]
    public void PrimitivesKeepLayoutAndTextContracts(string primitive, string padding, double border, double radius)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool toggle in new[] { false, true })
        {
            var text = new TextBlock { Text = "A long action label" };
            Button button = CreateButton(primitive, "actionNeutral", toggle, text);
            Window host = CreateHost(button, dark);
            try
            {
                Show(host);
                Assert.Equal(30, button.MinHeight);
                Assert.Equal(Thickness.Parse(padding), button.Padding);
                Assert.Equal(new Thickness(border), button.BorderThickness);
                Assert.Equal(new CornerRadius(radius), button.CornerRadius);
                Assert.Equal(button.Padding, Frame(button).Padding);
                Assert.Equal(button.CornerRadius, Frame(button).CornerRadius);
                Assert.True(button.ClipToBounds);
                Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                Assert.Null(button.Theme);
                Assert.Null(button.FocusAdorner);
                ContentPresenter presenter = Assert.Single(button.GetVisualDescendants().OfType<ContentPresenter>());
                Assert.Null(presenter.Name);
                Assert.Same(text, presenter.Content);
                Assert.Same(Frame(button), presenter.Parent);
                if (primitive == "actionIconButton")
                {
                    Assert.Equal(30, button.Width);
                    Assert.Equal(30, button.Height);
                    Assert.Equal(30, button.MinWidth);
                    Assert.Equal(new Size(30, 30), button.Bounds.Size);
                }
                else
                {
                    Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
                    Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                    Assert.Equal(1, text.MaxLines);
                }
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Checks passive status colors, modifiers and layout without inventing interactive states.</summary>
    [AvaloniaTheory]
    [InlineData("", "NfcBorderBrush", "NfcTextBrush")]
    [InlineData("warning", "NfcWarningTextBrush", "NfcWarningTextBrush")]
    [InlineData("danger", "NfcDangerTextBrush", "NfcDangerTextBrush")]
    [InlineData("success", "NfcSuccessTextBrush", "NfcSuccessTextBrush")]
    public void StatusChipsKeepPassiveColorsAndLayout(string modifier, string border, string foreground)
    {
        foreach (bool dark in new[] { false, true })
        {
            var text = new TextBlock { Text = "Status" };
            var selectable = new SelectableTextBlock { Text = "Status" };
            var chip = new Border { Child = new StackPanel { Children = { text, selectable } } };
            chip.Classes.Add("chipStatus");
            if (modifier.Length != 0)
            {
                chip.Classes.Add(modifier);
            }

            Window host = CreateHost(chip, dark);
            try
            {
                Show(host);
                AssertBrush(chip, "NfcSurfaceSubtleBrush", chip.Background);
                AssertBrush(chip, border, chip.BorderBrush);
                AssertBrush(chip, foreground, text.Foreground);
                AssertBrush(chip, foreground, selectable.Foreground);
                Assert.Equal(new Thickness(1.5), chip.BorderThickness);
                Assert.Equal(new Thickness(8, 4), chip.Padding);
                Assert.Equal(new CornerRadius(999), chip.CornerRadius);
                Assert.Equal(240, chip.MaxWidth);
                Assert.True(chip.ClipToBounds);
                Assert.False(chip.Focusable);
                Assert.Equal(FontWeight.SemiBold, text.FontWeight);
                Assert.Equal(FontWeight.SemiBold, selectable.FontWeight);
                Assert.Equal(TextTrimming.CharacterEllipsis, selectable.TextTrimming);
                Assert.Equal(1, selectable.MaxLines);
                chip.IsEnabled = false;
                AssertBrush(chip, border, chip.BorderBrush);
                AssertBrush(chip, foreground, text.Foreground);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Uses real Tab traversal and pointer focus and inspects the live focus adorner.</summary>
    [AvaloniaTheory]
    [InlineData("actionTextButton", "actionNeutral", "Nvt.Focus.RingBrush", 8)]
    [InlineData("actionTextButton", "actionPrimary", "Nvt.Focus.RingBrush", 8)]
    [InlineData("actionTextButton", "actionDanger", "Nvt.Focus.DangerRingBrush", 8)]
    [InlineData("actionTextButton", "actionGhost", "Nvt.Focus.RingBrush", 8)]
    [InlineData("actionIconButton", "actionNeutral", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionIconButton", "actionPrimary", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionIconButton", "actionDanger", "Nvt.Focus.DangerRingBrush", 999)]
    [InlineData("actionIconButton", "actionGhost", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionChip", "chipAction", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionChip", "chipAction active", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionChip", "actionDanger", "Nvt.Focus.RingBrush", 999)]
    public void KeyboardFocusShowsOnlyTheRoleRing(string primitive, string role, string ringKey, double radius)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool toggle in new[] { false, true })
        {
            var before = new Grid { Focusable = true, Height = 20 };
            Button button = CreateButton(primitive, role, toggle, new TextBlock { Text = "Action" });
            Window host = CreateHost(new StackPanel { Children = { before, button } }, dark);
            try
            {
                Show(host);
                AdornerLayer.GetAdornerLayer(button)!.DefaultFocusAdorner =
                    new FuncTemplate<Control>(() => new Border());
                Assert.Null(button.FocusAdorner);
                IBrush? background = button.Background;
                IBrush? foreground = button.Foreground;
                IBrush? border = button.BorderBrush;
                Assert.True(button.Focus(NavigationMethod.Pointer));
                Assert.DoesNotContain(":focus-visible", button.Classes);
                Assert.Null(button.FocusAdorner);
                Assert.Empty(Rings(button));
                Assert.True(before.Focus());
                host.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                host.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Assert.Same(button, host.FocusManager!.GetFocusedElement());
                Assert.Contains(":focus-visible", button.Classes);
                Assert.NotNull(button.FocusAdorner);
                Border ring = Assert.Single(Rings(button));
                AssertBrush(button, ringKey, ring.BorderBrush);
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.Equal(new Thickness(-4), ring.Margin);
                Assert.Equal(new CornerRadius(radius), ring.CornerRadius);
                Assert.False(ring.IsHitTestVisible);
                Assert.False(AdornerLayer.GetIsClipEnabled(ring));
                Assert.Equal(button.Bounds.Width + 8, ring.Bounds.Width, 5);
                Assert.Equal(button.Bounds.Height + 8, ring.Bounds.Height, 5);
                Assert.Same(background, button.Background);
                Assert.Same(foreground, button.Foreground);
                Assert.Same(border, button.BorderBrush);
                Assert.True(before.Focus(NavigationMethod.Pointer));
                Assert.Null(button.FocusAdorner);
                Assert.Empty(Rings(button));
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Ports the generic NFH 4.5:1 check for explicit interactive background and text pairs.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InteractiveTextPairsMeetNfhMinimumContrast(bool dark)
    {
        XNamespace ns = ThemeContractTests.Presentation;
        var failures = new List<string>();
        foreach (XElement style in ThemeContractTests.ReadExtracted("ActionRoleStyles").Descendants(ns + "Style"))
        {
            string selector = style.Attribute("Selector")!.Value;
            if (!selector.Contains(":pointerover", StringComparison.Ordinal) &&
                !selector.Contains(":pressed", StringComparison.Ordinal) &&
                !selector.Contains(":checked", StringComparison.Ordinal))
            {
                continue;
            }

            string? background = ResourceSetter(style, "Background");
            string? foreground = ResourceSetter(style, "Foreground");
            if (background != null && foreground != null)
            {
                CheckContrast(background, foreground, dark, selector, failures);
            }
        }

        CheckContrast("NfcSurfaceBrush", "NfcTextBrush", dark, "neutral rest", failures);
        CheckContrast("NfcAccentSurfaceBrush", "NfcAccentStrongBrush", dark, "neutral and inverse hover", failures);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Pins the disabled-neutral text contrast. It is below NFH's 4.5:1 text check in both palettes;
    /// the Core palette is kept and the shortfall is documented in Theme.md.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 2.564)]
    [InlineData(true, 3.728)]
    public void DisabledNeutralTextContrastIsDocumentedException(bool dark, double expectedContrast)
    {
        Window host = CreateHost(new Grid(), dark);
        try
        {
            Show(host);
            double contrast = Contrast(BrushColor(host, "NfcTextDisabledBrush"), BrushColor(host, "NfcSurfaceBrush"));
            Assert.Equal(expectedContrast, contrast, 3);
            Assert.True(contrast < 4.5);
        }
        finally { host.Close(); }
    }

    /// <summary>Pins the owner-selected focus colors and width in both palettes.</summary>
    [AvaloniaTheory]
    [InlineData(false, 2.426644, 2.564869)]
    [InlineData(true, 7.131214, 6.746902)]
    public void FocusTokensKeepOwnerColorsAndWindowContrast(bool dark, double blueContrast, double redContrast)
    {
        Window host = CreateHost(new Grid(), dark);
        try
        {
            Show(host);
            Assert.Equal(Color.Parse("#4DA3FF"), BrushColor(host, "Nvt.Focus.RingBrush"));
            Assert.Equal(Color.Parse("#FF6B6B"), BrushColor(host, "Nvt.Focus.DangerRingBrush"));
            Assert.True(host.TryFindResource("Nvt.Focus.RingThickness", host.ActualThemeVariant, out object? width));
            Assert.Equal(new Thickness(2), Assert.IsType<Thickness>(width));
            Color background = BrushColor(host, "NfcAppBackgroundBrush");
            Assert.Equal(blueContrast, Contrast(BrushColor(host, "Nvt.Focus.RingBrush"), background), 5);
            Assert.Equal(redContrast, Contrast(BrushColor(host, "Nvt.Focus.DangerRingBrush"), background), 5);
        }
        finally { host.Close(); }
    }

    private static Window CreateHost(Control content, bool dark)
    {
        var tokens = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
        var styles = new Uri("avares://Nvt.Core.Avalonia/Theme/ActionRoleStyles.axaml");
        var host = new Window
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Width = 400,
            Height = 180,
            Content = new VisualLayerManager
            {
                EnableAdornerLayer = true,
                Child = new Border { Padding = new Thickness(12), Child = content },
            },
        };
        host.Resources.MergedDictionaries.Add(new ResourceInclude(tokens) { Source = tokens });
        host.Styles.Add(new StyleInclude(styles) { Source = styles });
        return host;
    }

    private static void Show(Window host)
    {
        host.Show();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }

    private static Button CreateButton(string primitive, string role, bool toggle, Control content)
    {
        Button button = toggle ? new StateToggleButton() : new StateButton();
        button.Content = content;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Classes.Add(primitive);
        foreach (string name in role.Split(' '))
        {
            button.Classes.Add(name);
        }

        return button;
    }

    private static void SetState(Button button, string state)
    {
        button.IsEnabled = state != "disabled";
        if (button is StateToggleButton toggle)
        {
            toggle.IsChecked = state.StartsWith("checked", StringComparison.Ordinal);
            toggle.SetState(":pointerover", state is "hover" or "checkedHover" or "pressed" or "checkedPressed");
            toggle.SetState(":pressed", state is "pressed" or "checkedPressed");
        }
        else
        {
            var ordinary = (StateButton)button;
            ordinary.SetState(":pointerover", state is "hover" or "pressed");
            ordinary.SetState(":pressed", state == "pressed");
        }
    }

    private static (string Background, string Border, string Foreground) ExpectedColors(string primitive, string role, string state)
    {
        bool chip = role.StartsWith("chipAction", StringComparison.Ordinal);
        bool icon = primitive == "actionIconButton";
        bool text = primitive == "actionTextButton";
        bool selected = state.StartsWith("checked", StringComparison.Ordinal);
        bool hover = state is "hover" or "checkedHover";
        bool pressed = state is "pressed" or "checkedPressed";
        // NFH's later active setter wins over the earlier hover, pressed and disabled colors.
        if (role == "chipAction active") return ("NfcSelectionSurfaceBrush", "NfcAccentBorderBrush", "NfcTextStrongBrush");
        if (icon)
        {
            if (state == "disabled") return ("Transparent", "Transparent", "NfcTextDisabledBrush");
            if (selected && (hover || pressed)) return ("NfcAccentSurfaceBrush", "Transparent", "NfcAccentStrongBrush");
            if (selected) return ("NfcSelectionSurfaceBrush", "Transparent", "NfcTextBrush");
            if (hover || pressed) return (pressed ? "NfcSecondaryActionPressedBrush" : "NfcAccentSurfaceBrush", "Transparent", "NfcAccentStrongBrush");
            return ("Transparent", "Transparent", "NfcTextBrush");
        }

        if (state == "disabled")
        {
            return (chip ? "NfcSurfaceSubtleBrush" : role == "actionDanger" ? "NfcDangerSurfaceBrush" :
                role == "actionGhost" ? "Transparent" : "NfcSurfaceBrush",
                role == "actionDanger" ? "NfcDangerBorderBrush" : role == "actionGhost" ? "Transparent" : "NfcBorderBrush",
                text && role == "actionNeutral" ? "NfcTextBrush" : "NfcTextDisabledBrush");
        }

        if (selected && (!text || role == "actionNeutral" || (!hover && !pressed)))
        {
            return role == "actionDanger" ? ("NfcCriticalSurfaceBrush", "NfcDangerBorderBrush", "NfcDangerTextBrush") :
                ("NfcSelectionSurfaceBrush", role == "actionPrimary" ? "NfcAccentBorderLightBrush" : "NfcAccentBorderBrush", "NfcTextStrongBrush");
        }

        if (chip && selected) return ("NfcSelectionSurfaceBrush", "NfcAccentBorderBrush", "NfcTextStrongBrush");
        if (hover || pressed)
        {
            if (text || role == "actionNeutral") return (pressed ? "NfcSecondaryActionPressedBrush" : "NfcAccentSurfaceBrush",
                pressed ? "NfcAccentBorderStrongBrush" : "NfcAccentBorderBrush", role == "actionDanger" ? "NfcDangerTextBrush" : "NfcAccentStrongBrush");
            if (chip) return (pressed ? "NfcSecondaryActionPressedBrush" : "NfcAccentSurfaceBrush", "NfcBorderBrush", "NfcAccentStrongBrush");
            if (role == "actionPrimary") return (pressed ? "NfcAccentSurfaceBrush" : "NfcAccentSurfaceSubtleBrush", "NfcAccentBorderLightBrush", "NfcAccentStrongBrush");
            if (role == "actionDanger") return (pressed ? "NfcCriticalSurfaceBrush" : "NfcDangerSurfaceMutedBrush", "NfcDangerBorderBrush", "NfcDangerTextBrush");
            return (pressed ? "NfcSecondaryActionPressedBrush" : "NfcAccentSurfaceBrush", pressed ? "NfcSecondaryActionPressedBrush" : "NfcAccentSurfaceBrush", "NfcAccentStrongBrush");
        }

        return role switch
        {
            "actionPrimary" => ("NfcAccentSurfaceBrush", "NfcAccentBorderLightBrush", "NfcAccentStrongBrush"),
            "actionDanger" => ("NfcDangerSurfaceBrush", "NfcDangerBorderBrush", "NfcDangerTextBrush"),
            "actionGhost" => ("Transparent", "Transparent", "NfcTextSecondaryBrush"),
            "chipAction" => ("NfcSurfaceSubtleBrush", "NfcBorderBrush", "NfcTextBrush"),
            _ => ("NfcSurfaceBrush", "NfcBorderBrush", "NfcTextBrush"),
        };
    }

    private static Border Frame(Button button) => Assert.Single(button.GetVisualChildren().OfType<Border>());

    private static IEnumerable<Border> Rings(Button button) =>
        AdornerLayer.GetAdornerLayer(button)!.Children.OfType<Border>()
            .Where(ring => AdornerLayer.GetAdornedElement(ring) == button);

    private static void AssertBrush(Control owner, string key, IBrush? actual)
    {
        Color expected = key == "Transparent" ? Colors.Transparent : BrushColor(owner, key);
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }

    private static Color BrushColor(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out object? brush), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    private static string? ResourceSetter(XElement style, string property)
    {
        string? value = style.Elements(ThemeContractTests.Presentation + "Setter")
            .SingleOrDefault(setter => setter.Attribute("Property")!.Value == property)?.Attribute("Value")?.Value;
        const string prefix = "{DynamicResource ";
        return value?.StartsWith(prefix, StringComparison.Ordinal) == true ? value[prefix.Length..^1] : null;
    }

    private static void CheckContrast(string background, string foreground, bool dark, string context, List<string> failures)
    {
        XElement theme = ThemeContractTests.ReadBaseline("ThemeTokens").Root!
            .Element(ThemeContractTests.Presentation + "ResourceDictionary.ThemeDictionaries")!.Elements()
            .Single(element => element.Attribute(ThemeContractTests.Xaml + "Key")!.Value == (dark ? "Dark" : "Light"));
        Color GetColor(string key) => Color.Parse(theme.Elements()
            .Single(element => element.Attribute(ThemeContractTests.Xaml + "Key")!.Value == key).Attribute("Color")!.Value);
        double ratio = Contrast(GetColor(background), GetColor(foreground));
        if (ratio < 4.5)
        {
            failures.Add($"{(dark ? "Dark" : "Light")}: {context}, {background}/{foreground} = {ratio:F4}:1, below 4.5:1.");
        }
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private sealed class StateButton : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);

        internal void SetState(string state, bool enabled) => PseudoClasses.Set(state, enabled);
    }

    private sealed class StateToggleButton : ToggleButton
    {
        protected override Type StyleKeyOverride => typeof(ToggleButton);

        internal void SetState(string state, bool enabled) => PseudoClasses.Set(state, enabled);
    }
}
