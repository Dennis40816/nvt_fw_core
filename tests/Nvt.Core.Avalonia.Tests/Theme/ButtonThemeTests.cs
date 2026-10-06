// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Animation;
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

/// <summary>Characterizes the shared action roles with the Core palettes and keyboard ring.</summary>
public sealed class ButtonThemeTests
{
    /// <summary>Checks both control types and palettes, including disabled priority over the selected-state cascade.</summary>
    [AvaloniaTheory]
    [InlineData("", "actionNeutral")]
    [InlineData("", "actionPrimary")]
    [InlineData("", "actionDanger")]
    [InlineData("", "actionGhost")]
    [InlineData("actionIconButton", "")]
    [InlineData("actionIconButton", "actionNeutral")]
    [InlineData("actionIconButton", "actionPrimary")]
    [InlineData("actionIconButton", "actionDanger")]
    [InlineData("actionIconButton", "actionGhost")]
    [InlineData("", "chipAction")]
    [InlineData("", "chipAction active")]
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
                Color restText = Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground).Color;
                foreach (string state in new[] { "rest", "hover", "pressed", "disabled", "disabledHover", "disabledPressed", "checked", "checkedHover", "checkedPressed", "disabledChecked", "disabledCheckedHover", "disabledCheckedPressed" })
                {
                    if (!toggle && state.Contains("Checked", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    SetState(button, state);
                    (string background, string border, string foreground) = ExpectedColors(role, state, dark);
                    AssertBrush(button, background, button.Background);
                    AssertBrush(button, border, button.BorderBrush);
                    AssertBrush(button, foreground, button.Foreground);
                    Border frame = Frame(button);
                    AssertBrush(button, background, frame.Background);
                    AssertBrush(button, border, frame.BorderBrush);
                    AssertBrush(button, foreground, text.Foreground);
                    Assert.Equal(1, button.Opacity);
                    if (!button.IsEnabled) Assert.NotEqual(restText, Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground).Color);
                    Assert.Equal(new Thickness(1), button.BorderThickness);
                    Border overlay = Assert.Single(button.GetVisualDescendants().OfType<Border>(), b => b.Name == "PrimaryPressedBorder");
                    Assert.Equal(role == "actionPrimary" && state.Contains("pressed", StringComparison.OrdinalIgnoreCase) && button.IsEnabled, overlay.IsVisible);
                    Assert.Equal(new Thickness(2), overlay.BorderThickness);
                }
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Preserves the shared dimensions, pill geometry, clipping and owned templates.</summary>
    [AvaloniaTheory]
    [InlineData("actionNeutral", "14,0", 1, 999)]
    [InlineData("actionPrimary", "14,0", 1, 999)]
    [InlineData("actionDanger", "14,0", 1, 999)]
    [InlineData("actionGhost", "14,0", 1, 999)]
    [InlineData("chipAction", "14,0", 1, 999)]
    [InlineData("actionIconButton", "0", 1, 999)]
    public void RolesKeepLayoutAndTextContracts(string primitive, string padding, double border, double radius)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool toggle in new[] { false, true })
        {
            var text = new TextBlock { Text = "A long action label" };
            Button button = CreateButton("", primitive, toggle, text);
            Window host = CreateHost(button, dark);
            try
            {
                Show(host);
                Assert.Equal(32, button.MinHeight);
                Assert.Equal(32, button.Height);
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
                    Assert.Equal(32, button.Width);
                    Assert.Equal(32, button.Height);
                    Assert.Equal(32, button.MinWidth);
                    Assert.Equal(new Size(32, 32), button.Bounds.Size);
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
    [InlineData("", "NfcSurfaceSubtleBrush", "NfcBorderMutedBrush", "NfcTextSecondaryBrush")]
    [InlineData("warning", "NfcWarningSurfaceBrush", "NfcWarningBorderBrush", "NfcWarningTextBrush")]
    [InlineData("danger", "NfcDangerSurfaceBrush", "NfcDangerBorderBrush", "NfcDangerTextBrush")]
    [InlineData("success", "NfcSuccessSurfaceBrush", "NfcSuccessBorderBrush", "NfcSuccessTextBrush")]
    public void StatusChipsKeepPassiveColorsAndLayout(string modifier, string background, string border, string foreground)
    {
        foreach (bool dark in new[] { false, true })
        {
            var text = new TextBlock { Text = "Status" };
            var selectable = new SelectableTextBlock { Text = "Status" };
            var chip = new Border { Child = new Grid { Children = { text, selectable } } };
            chip.Classes.Add("chipStatus");
            if (modifier.Length != 0)
            {
                chip.Classes.Add(modifier);
            }

            Window host = CreateHost(chip, dark);
            try
            {
                Show(host);
                AssertBrush(chip, background, chip.Background);
                AssertBrush(chip, border, chip.BorderBrush);
                AssertBrush(chip, foreground, text.Foreground);
                AssertBrush(chip, foreground, selectable.Foreground);
                Assert.Equal(new Thickness(1), chip.BorderThickness);
                Assert.Equal(new Thickness(14, 0), chip.Padding);
                Assert.Equal(new CornerRadius(999), chip.CornerRadius);
                Assert.Equal(32, chip.Height);
                Assert.Equal(1, chip.Opacity);
                Assert.True(chip.ClipToBounds);
                Assert.False(chip.Focusable);
                Assert.Equal(TextTrimming.CharacterEllipsis, selectable.TextTrimming);
                Assert.Equal(1, selectable.MaxLines);
                Assert.False(chip.Focus());
                chip.IsEnabled = false;
                Assert.Equal(1, chip.Opacity);
                AssertBrush(chip, border, chip.BorderBrush);
                AssertBrush(chip, foreground, text.Foreground);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Uses real Tab traversal and pointer focus and inspects the live focus adorner.</summary>
    [AvaloniaTheory]
    [InlineData("", "actionNeutral", "Nvt.Focus.RingBrush", 999)]
    [InlineData("", "actionPrimary", "Nvt.Focus.RingBrush", 999)]
    [InlineData("", "actionDanger", "Nvt.Focus.DangerRingBrush", 999)]
    [InlineData("", "actionGhost", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionIconButton", "", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionIconButton", "actionNeutral", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionIconButton", "actionPrimary", "Nvt.Focus.RingBrush", 999)]
    [InlineData("actionIconButton", "actionDanger", "Nvt.Focus.DangerRingBrush", 999)]
    [InlineData("actionIconButton", "actionGhost", "Nvt.Focus.RingBrush", 999)]
    [InlineData("", "chipAction", "Nvt.Focus.RingBrush", 999)]
    [InlineData("", "chipAction active", "Nvt.Focus.RingBrush", 999)]
    [InlineData("", "chipAction actionDanger", "Nvt.Focus.RingBrush", 999)]
    public void KeyboardFocusShowsOnlyTheRoleRing(string primitive, string role, string ringKey, double radius)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool toggle in new[] { false, true })
        foreach (string state in new[] { "rest", "hover", "checked", "checkedHover" })
        {
            if (!toggle && state.StartsWith("checked", StringComparison.Ordinal)) continue;
            var before = new Grid { Focusable = true, Height = 20 };
            Button button = CreateButton(primitive, role, toggle, new TextBlock { Text = "Action" });
            Window host = CreateHost(new StackPanel { Children = { before, button } }, dark);
            try
            {
                Show(host);
                AdornerLayer.GetAdornerLayer(button)!.DefaultFocusAdorner =
                    new FuncTemplate<Control>(() => new Border());
                Assert.Null(button.FocusAdorner);
                SetState(button, state);
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

    /// <summary>Checks compiled brushes, sizes, fonts and glyphs against every frozen token value.</summary>
    [AvaloniaFact]
    public void CompiledTokensMatchEveryFrozenValueInBothThemes()
    {
        XElement root = ThemeContractTests.ReadBaseline("ThemeTokens").Root!;
        XElement[] common = [.. root.Elements().Where(element => element.Attribute(ThemeContractTests.Xaml + "Key") != null)];
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            XElement theme = Assert.Single(root.Descendants(ThemeContractTests.Presentation + "ResourceDictionary"),
                element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == variant.Key.ToString());
            foreach (XElement token in common.Concat(theme.Elements()))
            {
                string key = token.Attribute(ThemeContractTests.Xaml + "Key")!.Value;
                Assert.True(Application.Current!.TryGetResource(key, variant, out object? actual), key);
                string value = token.Attribute("Color")?.Value ?? token.Value;
                switch (token.Name.LocalName)
                {
                    case "SolidColorBrush":
                        Assert.Equal(Color.Parse(value), Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
                        break;
                    case "Double":
                        Assert.Equal(double.Parse(value, CultureInfo.InvariantCulture), Assert.IsType<double>(actual));
                        break;
                    case "CornerRadius":
                        Assert.Equal(CornerRadius.Parse(value), Assert.IsType<CornerRadius>(actual));
                        break;
                    case "FontFamily":
                        Assert.Equal(new FontFamily(value), Assert.IsType<FontFamily>(actual));
                        break;
                    case "StreamGeometry":
                        Assert.Equal(StreamGeometry.Parse(value).Bounds, Assert.IsType<StreamGeometry>(actual).Bounds);
                        break;
                    case "Thickness":
                        Assert.Equal(Thickness.Parse(value), Assert.IsType<Thickness>(actual));
                        break;
                    default:
                        Assert.Fail($"Uncharacterized token type: {token.Name.LocalName}");
                        break;
                }
            }
        }
    }

    /// <summary>A bare button receives no setter, template or implicit theme from the shared file.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ButtonsWithoutRolesReceiveNoStyles(bool toggle)
    {
        foreach (bool dark in new[] { false, true })
        {
            Button button = toggle ? new ToggleButton() : new Button();
            var initial = (button.Background, button.BorderBrush, button.Foreground, button.Padding, button.Height, button.Transitions, button.FocusAdorner, button.Template);
            Window host = CreateHost(button, dark);
            try
            {
                Show(host);
                Assert.Null(button.Theme);
                Assert.Equal(initial, (button.Background, button.BorderBrush, button.Foreground, button.Padding, button.Height, button.Transitions, button.FocusAdorner, button.Template));
            }
            finally { host.Close(); }
        }
    }

    /// <summary>String content generated by the presenter uses the same one-line ellipsis as explicit text controls.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringContentUsesSingleLineEllipsis(bool toggle)
    {
        foreach (bool dark in new[] { false, true })
        foreach (string role in new[] { "actionPrimary", "actionNeutral", "actionDanger", "actionGhost", "actionIconButton", "chipAction" })
        {
            Button button = CreateButton("", role, toggle, new TextBlock());
            button.Content = "Save 儲存 with a long label";
            if (role != "actionIconButton") button.Width = 100;
            Window host = CreateHost(button, dark);
            try
            {
                Show(host);
                TextBlock label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
                Assert.Equal(1, label.MaxLines);
                Assert.True(button.ClipToBounds);
                Assert.True(Assert.Single(label.TextLayout.TextLines).HasCollapsed);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Motion lasts 120 ms for colors; pressed feedback and both reduced-motion scopes are immediate.</summary>
    [AvaloniaTheory]
    [InlineData("actionPrimary")]
    [InlineData("actionNeutral")]
    [InlineData("actionDanger")]
    [InlineData("actionGhost")]
    [InlineData("actionIconButton")]
    [InlineData("chipAction")]
    public void ColorTransitionsRespectPressedAndReducedMotion(string role)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool toggle in new[] { false, true })
        {
            Button button = CreateButton("", role, toggle, new TextBlock { Text = "Action" });
            Window host = CreateHost(button, dark);
            host.Classes.Remove("reducedMotion");
            try
            {
                Show(host);
                Assert.NotNull(button.Transitions);
                Assert.Equal(3, button.Transitions.Count);
                Assert.All(button.Transitions, transition => Assert.Equal(TimeSpan.FromMilliseconds(120), Assert.IsType<BrushTransition>(transition).Duration));
                SetState(button, "pressed");
                Assert.Null(button.Transitions);
                AssertBrush(button, ExpectedColors(role, "pressed", dark).Background, button.Background);
                SetState(button, "rest");
                Assert.NotNull(button.Transitions);
                host.Classes.Add("reducedMotion");
                Assert.Null(button.Transitions);
                host.Classes.Remove("reducedMotion");
                button.Classes.Add("reducedMotion");
                Assert.Null(button.Transitions);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>The ring follows each rectangular corner and live radius changes with its four-pixel outset.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusRingTracksRectangularControlCorners(bool toggle)
    {
        foreach (bool dark in new[] { false, true })
        {
            Button button = CreateButton("", "actionNeutral", toggle, new TextBlock { Text = "Action" });
            button.CornerRadius = new CornerRadius(6, 8, 0, 3);
            Window host = CreateHost(button, dark);
            try
            {
                Show(host);
                Assert.True(button.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Border ring = Assert.Single(Rings(button));
                Assert.Equal(new CornerRadius(10, 12, 4, 7), ring.CornerRadius);
                button.CornerRadius = new CornerRadius(8);
                Assert.Equal(new CornerRadius(12), ring.CornerRadius);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Adoption overrides update existing role colors, geometry and keyboard rings at application scope.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationAdoptionOverridesUpdateButtonsAndFocus(bool dark)
    {
        var overrides = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["NfcSurfaceBrush"] = new SolidColorBrush(Color.Parse("#3C4D5E")),
            ["NfcAccentBrush"] = new SolidColorBrush(Color.Parse("#7F3FBF")),
            ["NfcBorderBrush"] = new SolidColorBrush(Color.Parse("#AD7C31")),
            ["NfcTextBrush"] = new SolidColorBrush(Color.Parse("#BCD1E2")),
            ["NfcDangerSurfaceBrush"] = new SolidColorBrush(Color.Parse("#563D42")),
            ["NfcDangerBorderBrush"] = new SolidColorBrush(Color.Parse("#DE8E91")),
            ["NfcDangerTextBrush"] = new SolidColorBrush(Color.Parse("#FFE8EB")),
            ["NfcControlHeight"] = 44d,
            ["NfcPillCornerRadius"] = new CornerRadius(7, 9, 11, 13),
            ["Nvt.Button.PrimaryLabelBrush"] = new SolidColorBrush(Color.Parse("#FFE7C0")),
            ["Nvt.Focus.RingBrush"] = new SolidColorBrush(Color.Parse("#24BABA")),
            ["Nvt.Focus.DangerRingBrush"] = new SolidColorBrush(Color.Parse("#FF9078")),
            ["Nvt.Focus.RingThickness"] = new Thickness(3),
        };
        IResourceDictionary resources = Application.Current!.Resources;
        var previous = overrides.Keys.Where(resources.ContainsKey).ToDictionary(key => key, key => resources[key], StringComparer.Ordinal);
        foreach (bool toggle in new[] { false, true })
        {
            var before = new Grid { Focusable = true, Height = 20 };
            Button primary = CreateButton("", "actionPrimary", toggle, new TextBlock { Text = "Primary" });
            Button neutral = CreateButton("", "actionNeutral", toggle, new TextBlock { Text = "Neutral" });
            Button danger = CreateButton("", "actionDanger", toggle, new TextBlock { Text = "Danger" });
            Window host = CreateHost(new StackPanel { Children = { before, primary, neutral, danger } }, dark);
            host.Resources.MergedDictionaries.Clear();
            try
            {
                Show(host);
                Assert.Equal(32, primary.Height);
                Assert.NotEqual(((SolidColorBrush)overrides["Nvt.Button.PrimaryLabelBrush"]).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(primary.Foreground).Color);
                foreach ((string key, object value) in overrides)
                    resources[key] = value;
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();

                Assert.True(before.Focus());
                var roles = new[]
                {
                    (primary, "NfcAccentBrush", "NfcAccentBrush", "Nvt.Button.PrimaryLabelBrush", "Nvt.Focus.RingBrush"),
                    (neutral, "NfcSurfaceBrush", "NfcBorderBrush", "NfcTextBrush", "Nvt.Focus.RingBrush"),
                    (danger, "NfcDangerSurfaceBrush", "NfcDangerBorderBrush", "NfcDangerTextBrush", "Nvt.Focus.DangerRingBrush"),
                };
                foreach ((Button button, string background, string border, string foreground, string ringKey) in roles)
                {
                    Assert.Equal(((SolidColorBrush)overrides[background]).Color, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
                    Assert.Equal(((SolidColorBrush)overrides[border]).Color, Assert.IsAssignableFrom<ISolidColorBrush>(button.BorderBrush).Color);
                    Assert.Equal(((SolidColorBrush)overrides[foreground]).Color, Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground).Color);
                    Assert.Equal(44, button.Height);
                    Assert.Equal(44, button.MinHeight);
                    Assert.Equal(new CornerRadius(7, 9, 11, 13), button.CornerRadius);

                    host.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    host.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    Dispatcher.UIThread.RunJobs();
                    host.UpdateLayout();
                    Assert.Same(button, host.FocusManager!.GetFocusedElement());
                    Border ring = Assert.Single(Rings(button));
                    Assert.Equal(((SolidColorBrush)overrides[ringKey]).Color, Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
                    Assert.Equal(new Thickness(3), ring.BorderThickness);
                    Assert.Equal(new CornerRadius(11, 13, 15, 17), ring.CornerRadius);
                }
            }
            finally
            {
                host.Close();
                foreach (string key in overrides.Keys)
                {
                    if (previous.TryGetValue(key, out object? value)) resources[key] = value;
                    else resources.Remove(key);
                }
            }
        }
    }

    /// <summary>Application resources replace all seven accent keys and update existing buttons in both themes.</summary>
    [AvaloniaTheory]
    [InlineData(false, "#2967A9", "#225489", "#F0F4F9", "#F8FAFC")]
    [InlineData(true, "#53A6FF", "#86C0FF", "#192941", "#152134")]
    public void ApplicationAccentOverridesUpdateButtonColors(bool dark, string accent, string strong, string surface, string subtle)
    {
        var overrides = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["NfcAccentBrush"] = Color.Parse(accent),
            ["NfcAccentStrongBrush"] = Color.Parse(strong),
            ["NfcAccentSurfaceBrush"] = Color.Parse(surface),
            ["NfcAccentSurfaceSubtleBrush"] = Color.Parse(subtle),
            ["NfcAccentBorderBrush"] = Color.Parse(accent),
            ["NfcAccentBorderStrongBrush"] = Color.Parse(strong),
            ["NfcAccentBorderLightBrush"] = Color.Parse(accent),
        };
        IResourceDictionary resources = Application.Current!.Resources;
        var previous = overrides.Keys.Where(resources.ContainsKey).ToDictionary(key => key, key => resources[key], StringComparer.Ordinal);
        foreach (bool toggle in new[] { false, true })
        {
            Button primary = CreateButton("", "actionPrimary", toggle, new TextBlock { Text = "Primary" });
            Button selected = CreateButton("", "chipAction active", toggle, new TextBlock { Text = "Selected" });
            Window host = CreateHost(new StackPanel { Children = { primary, selected } }, dark);
            host.Resources.MergedDictionaries.Clear();
            try
            {
                Show(host);
                Color originalAccent = Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color;
                IBrush? originalLabel = primary.Foreground;
                Color originalFocus = BrushColor(primary, "Nvt.Focus.RingBrush");
                foreach ((string key, Color color) in overrides)
                    resources[key] = new SolidColorBrush(color);
                Dispatcher.UIThread.RunJobs();

                Assert.NotEqual(originalAccent, Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color);
                foreach ((string key, Color color) in overrides)
                    Assert.Equal(color, BrushColor(primary, key));
                foreach (string state in new[] { "rest", "hover", "pressed" })
                {
                    SetState(primary, state);
                    SetState(selected, state);
                    Dispatcher.UIThread.RunJobs();
                    Color filled = overrides[state == "rest" ? "NfcAccentBrush" : "NfcAccentStrongBrush"];
                    Assert.Equal(filled, Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color);
                    Assert.Equal(filled, Assert.IsAssignableFrom<ISolidColorBrush>(primary.BorderBrush).Color);
                    Assert.Same(originalLabel, primary.Foreground);
                    Color selectedBackground = state == "pressed" ? BrushColor(selected, "NfcSecondaryActionPressedBrush")
                        : overrides[state == "rest" ? "NfcAccentSurfaceBrush" : "NfcAccentSurfaceSubtleBrush"];
                    Assert.Equal(selectedBackground, Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background).Color);
                    Assert.Equal(overrides[state == "rest" ? "NfcAccentBorderBrush" : "NfcAccentBorderStrongBrush"],
                        Assert.IsAssignableFrom<ISolidColorBrush>(selected.BorderBrush).Color);
                    Assert.Equal(overrides["NfcAccentStrongBrush"], Assert.IsAssignableFrom<ISolidColorBrush>(selected.Foreground).Color);
                }
                Assert.Equal(originalFocus, BrushColor(primary, "Nvt.Focus.RingBrush"));
            }
            finally
            {
                host.Close();
                foreach (string key in overrides.Keys)
                {
                    if (previous.TryGetValue(key, out object? value)) resources[key] = value;
                    else resources.Remove(key);
                }
            }
        }
    }

    private static Window CreateHost(Control content, bool dark)
    {
        var tokens = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
        var styles = new Uri("avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml");
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
        host.Classes.Add("reducedMotion");
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
        if (primitive.Length != 0) button.Classes.Add(primitive);
        foreach (string name in role.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            button.Classes.Add(name);
        }

        return button;
    }

    private static void SetState(Button button, string state)
    {
        button.IsEnabled = !state.StartsWith("disabled", StringComparison.Ordinal);
        if (button is StateToggleButton toggle)
        {
            toggle.IsChecked = state.Contains("checked", StringComparison.OrdinalIgnoreCase);
            toggle.SetState(":pointerover", state.Contains("hover", StringComparison.OrdinalIgnoreCase) || state.Contains("pressed", StringComparison.OrdinalIgnoreCase));
            toggle.SetState(":pressed", state.Contains("pressed", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var ordinary = (StateButton)button;
            ordinary.SetState(":pointerover", state.Contains("hover", StringComparison.OrdinalIgnoreCase) || state.Contains("pressed", StringComparison.OrdinalIgnoreCase));
            ordinary.SetState(":pressed", state.Contains("pressed", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static (string Background, string Border, string Foreground) ExpectedColors(string role, string state, bool dark)
    {
        bool selected = state.Contains("checked", StringComparison.OrdinalIgnoreCase) || role == "chipAction active";
        bool hover = state.Contains("hover", StringComparison.OrdinalIgnoreCase);
        bool pressed = state.Contains("pressed", StringComparison.OrdinalIgnoreCase);
        if (state.StartsWith("disabled", StringComparison.Ordinal))
            return ("NfcSurfaceSubtleBrush", "NfcBorderMutedBrush", "NfcTextDisabledBrush");
        if (role == "actionDanger")
        {
            if (pressed) return ("NfcCriticalSurfaceBrush", "NfcCriticalBorderBrush", "NfcDangerTextStrongBrush");
            if (selected) return ("NfcDangerSurfaceMutedBrush", "NfcDangerBorderStrongBrush", "NfcDangerTextStrongBrush");
            if (hover) return ("NfcDangerSurfaceMutedBrush", "NfcDangerBorderStrongBrush", "NfcDangerTextBrush");
            return ("NfcDangerSurfaceBrush", "NfcDangerBorderBrush", "NfcDangerTextBrush");
        }
        if (selected) return (pressed ? "NfcSecondaryActionPressedBrush" : hover ? "NfcAccentSurfaceSubtleBrush" : "NfcAccentSurfaceBrush",
            hover || pressed ? "NfcAccentBorderStrongBrush" : "NfcAccentBorderBrush", "NfcAccentStrongBrush");
        if (role == "actionPrimary") return (hover || pressed ? "NfcAccentStrongBrush" : "NfcAccentBrush",
            hover || pressed ? "NfcAccentStrongBrush" : "NfcAccentBrush", dark ? "NfcAppBackgroundBrush" : "NfcSurfaceBrush");
        if (role == "actionGhost") return (pressed ? "NfcSecondaryActionPressedBrush" : hover ? "NfcSelectionSurfaceBrush" : "Transparent",
            "Transparent", hover || pressed ? "NfcTextBrush" : "NfcTextSecondaryBrush");
        return (pressed ? "NfcSecondaryActionPressedBrush" : hover ? "NfcSelectionSurfaceBrush" : "NfcSurfaceBrush",
            "NfcBorderBrush", "NfcTextBrush");
    }

    private static Border Frame(Button button) => Assert.Single(button.GetVisualDescendants().OfType<Border>(), border => border.Name == "RoleBorder");

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
