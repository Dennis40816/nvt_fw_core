// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Choice;
using Nvt.Core.Avalonia.Tests.Forms;
using Xunit;
using static Nvt.Core.Avalonia.Tests.ListMenu.ListMenuTestHost;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Pins the hover, pressed and checked fills, the link underline and the zero-alpha rest fill.</summary>
public sealed class InteractionContrastTests(ITestOutputHelper output)
{
    private const double TextFloor = 4.5;
    private const double GraphicFloor = 3;
    // A fill that shows a state needs this much separation from the surface it sits on. The light toolbar
    // surface is limited to 1.2: a darker hover fill drops the accent link text on it below 4.5:1.
    private const double DarkFillFloor = 1.25;
    private const double LightFillFloor = 1.2;

    private static readonly string[] _parents = ["NfcSurfaceBrush", "NfcSurfaceSubtleBrush"];
    // Translucent state layers: they are composited over the surface before any ratio is taken.
    private static readonly string[] _layers = ["Nvt.Interaction.HoverBrush", "Nvt.Interaction.PressedBrush"];
    // Opaque composites of the layers for roles whose rest background is an opaque surface.
    private static readonly string[] _roleFills = ["Nvt.Interaction.HoverFillBrush", "Nvt.Interaction.PressedFillBrush"];
    private static readonly string[] _fills = ["NfcSelectionSurfaceBrush", "Nvt.Controls.SelectedBrush"];
    // Text that stays on a state fill. Muted text and the accent link swap to the main text colour on hover.
    private static readonly string[] _textOnLayers = ["NfcTextBrush", "NfcTextSecondaryBrush"];
    private static readonly string[] _textOnFills = ["NfcTextBrush", "NfcTextSecondaryBrush", "NfcTextMutedBrush", "NfcAccentStrongBrush"];

    private static Color Resolved(Window host, string fill, string parent)
    {
        Color color = ChoiceTestHost.ResourceColor(host, fill);
        return color.A == 255 ? color : Nvt.Core.Avalonia.Tests.Toggle.ToggleStylesTests.Flatten(color, ChoiceTestHost.ResourceColor(host, parent));
    }

    /// <summary>A fill that shows hover, pressed or checked is visible on every surface that hosts a control.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StateFillsAreSeparatedFromTheirSurface(bool dark)
    {
        Window host = FormsTestHost.Create(new TextBlock { Text = "Contrast" }, dark);
        try
        {
            double floor = dark ? DarkFillFloor : LightFillFloor;
            foreach (string fill in _layers.Concat(_roleFills).Concat(_fills))
            {
                foreach (string parent in _parents)
                {
                    double ratio = ChoiceTestHost.Contrast(Resolved(host, fill, parent), ChoiceTestHost.ResourceColor(host, parent));
                    output.WriteLine($"{(dark ? "Dark" : "Light")} {fill} / {parent}: {ratio:F3}:1");
                    Assert.True(ratio >= floor, $"{fill} on {parent}: {ratio:F3}:1 < {floor}:1");
                }
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Text and the selected label stay readable on every state fill.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextIsReadableOnEveryStateFill(bool dark)
    {
        Window host = FormsTestHost.Create(new TextBlock { Text = "Contrast" }, dark);
        try
        {
            foreach ((string fill, string[] texts) in _layers.Concat(_roleFills).Select(layer => (layer, _textOnLayers)).Concat(_fills.Select(fill => (fill, _textOnFills))))
            {
                foreach (string parent in _parents)
                {
                    foreach (string text in texts)
                    {
                        double ratio = ChoiceTestHost.Contrast(ChoiceTestHost.ResourceColor(host, text), Resolved(host, fill, parent));
                        output.WriteLine($"{(dark ? "Dark" : "Light")} {text} on {fill} over {parent}: {ratio:F3}:1");
                        Assert.True(ratio >= TextFloor, $"{text} on {fill} over {parent}: {ratio:F3}:1 < {TextFloor}:1");
                    }
                }
            }
            foreach (string fill in new[] { "Nvt.Controls.SelectedBrush", "Nvt.Controls.SelectedPointerOverBrush", "Nvt.Controls.SelectedPressedBrush" })
            {
                double ratio = ChoiceTestHost.Contrast(ChoiceTestHost.ResourceColor(host, "Nvt.Controls.SelectedForegroundBrush"), ChoiceTestHost.ResourceColor(host, fill));
                Assert.True(ratio >= TextFloor, $"Selected label on {fill}: {ratio:F3}:1 < {TextFloor}:1");
            }
        }
        finally { host.Close(); }
    }

    /// <summary>The idle link underline is a graphic, so it keeps 3:1 on every fill a console row can have.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkUnderlineMeetsGraphicFloor(bool dark)
    {
        Window host = FormsTestHost.Create(new TextBlock { Text = "Contrast" }, dark);
        try
        {
            foreach (string background in new[] { "NfcSurfaceBrush", "NfcSurfaceSubtleBrush", "NfcAppBackgroundBrush", "NfcSelectionSurfaceBrush" })
            {
                double ratio = ChoiceTestHost.Contrast(ChoiceTestHost.ResourceColor(host, "Nvt.Console.LinkUnderlineBrush"), ChoiceTestHost.ResourceColor(host, background));
                output.WriteLine($"{(dark ? "Dark" : "Light")} underline / {background}: {ratio:F3}:1");
                Assert.True(ratio >= GraphicFloor, $"Underline on {background}: {ratio:F3}:1 < {GraphicFloor}:1");
            }
        }
        finally { host.Close(); }
    }

    /// <summary>The rest fill has the hover colour at zero alpha, so a hover transition never passes through white.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestFillIsTheHoverColourAtZeroAlpha(bool dark)
    {
        Window host = FormsTestHost.Create(new TextBlock { Text = "Contrast" }, dark);
        try
        {
            Color rest = ChoiceTestHost.ResourceColor(host, "Nvt.Interaction.RestBrush");
            Color hover = ChoiceTestHost.ResourceColor(host, "Nvt.Interaction.HoverBrush");
            Assert.Equal(0, rest.A);
            Assert.Equal((hover.R, hover.G, hover.B), (rest.R, rest.G, rest.B));
        }
        finally { host.Close(); }
    }

    /// <summary>Every control that fades in a hover fill rests on that zero-alpha colour, not on transparent white.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoverControlsRestOnTheZeroAlphaHoverColour(bool dark)
    {
        var ghost = new Button { Classes = { "actionGhost" }, Content = "Ghost" };
        var soft = new ToggleButton { Classes = { "toggleSoft" }, Content = "Soft" };
        var segment = new ToggleButton { Classes = { "toggleSegment" }, Content = "Segment" };
        var tab = new ToggleButton { Classes = { "toggleTab" }, Content = "Tab" };
        var menuItem = new MenuItem { Header = "Item" };
        var listItem = new ListBoxItem { Content = "Row" };
        var tabItem = new TabItem { Header = "Tab item" };
        var panel = new StackPanel { Children = { ghost, soft, segment, tab, menuItem, listItem, tabItem } };
        Window host = Create(panel, dark);
        try
        {
            Include(host, "ButtonStyles");
            Include(host, "ToggleStyles");
            Include(host, "TabStyles");
            Show(host);
            Color hover = ChoiceTestHost.ResourceColor(host, "Nvt.Interaction.HoverBrush");
            foreach ((string name, Control control) in new (string, Control)[]
            {
                ("actionGhost", ghost), ("toggleSoft", soft), ("toggleSegment", segment), ("toggleTab", tab),
                ("MenuItem", menuItem), ("ListBoxItem", listItem), ("TabItem", tabItem),
            })
            {
                Color rest = ChoiceTestHost.ColorOf(((TemplatedControl)control).Background);
                Assert.True(rest.A == 0, $"{name} rest alpha {rest.A}");
                Assert.True((rest.R, rest.G, rest.B) == (hover.R, hover.G, hover.B), $"{name} rest colour {rest}");
            }
        }
        finally { host.Close(); }
    }

    /// <summary>A checked soft toggle draws an outline, so a checked toggle differs from a hovered one by more than colour.</summary>
    [AvaloniaFact]
    public void CheckedSoftToggleDrawsAnOutline()
    {
        var toggle = new ToggleButton { Classes = { "toggleSoft" }, Content = "Info", IsChecked = false };
        Window host = Create(new StackPanel { Children = { toggle } }, dark: true);
        try
        {
            Include(host, "ToggleStyles");
            Show(host);
            Border outline = Assert.Single(toggle.GetVisualDescendants().OfType<Border>(), part => part.Name == "ToggleCheckedOutline");
            Assert.False(outline.IsVisible);
            toggle.IsChecked = true;
            Flush(host);
            Assert.True(outline.IsVisible);
            Assert.Equal(new Thickness(1), outline.BorderThickness);
            ((IPseudoClasses)toggle.Classes).Set(":pointerover", true);
            Flush(host);
            Assert.Equal(new Thickness(2), outline.BorderThickness);
            Assert.Equal(ChoiceTestHost.ResourceColor(toggle, "NfcAccentStrongBrush"), ChoiceTestHost.ColorOf(outline.BorderBrush));
            ((IPseudoClasses)toggle.Classes).Set(":pointerover", false);
            Flush(host);
            Assert.True(ChoiceTestHost.Contrast(ChoiceTestHost.ColorOf(outline.BorderBrush), ChoiceTestHost.ResourceColor(toggle, "NfcSurfaceSubtleBrush")) >= GraphicFloor);
        }
        finally { host.Close(); }
    }

    /// <summary>Pressed is stronger than hover on every role, so a press never looks weaker than the pointer resting on the control.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PressedIsStrongerThanHoverOnEveryRole(bool dark)
    {
        Window host = FormsTestHost.Create(new TextBlock { Text = "Contrast" }, dark);
        try
        {
            foreach ((string role, string hover, string pressed, string surface) in new[]
            {
                ("Button neutral", "Nvt.Interaction.HoverFillBrush", "Nvt.Interaction.PressedFillBrush", "NfcSurfaceBrush"),
                ("Toggle, list, menu", "Nvt.Interaction.HoverBrush", "Nvt.Interaction.PressedBrush", "NfcSurfaceBrush"),
                ("Tab", "Nvt.Tab.HoverFillBrush", "Nvt.Tab.PressedFillBrush", "Nvt.Tab.StripBrush"),
                ("Form field", "Nvt.Form.HoverFillBrush", "Nvt.Form.PressedFillBrush", "NfcSurfaceBrush"),
                ("Choice row", "NfcSelectionSurfaceBrush", "Nvt.Interaction.PressedFillBrush", "NfcSurfaceSubtleBrush"),
                ("Expander header", "NfcSelectionSurfaceBrush", "Nvt.Controls.ExpanderPressedBrush", "NfcSurfaceSubtleBrush"),
            })
            {
                Color under = ChoiceTestHost.ResourceColor(host, surface);
                double hoverRatio = ChoiceTestHost.Contrast(Toggle.ToggleStylesTests.Flatten(ChoiceTestHost.ResourceColor(host, hover), under), under);
                double pressedRatio = ChoiceTestHost.Contrast(Toggle.ToggleStylesTests.Flatten(ChoiceTestHost.ResourceColor(host, pressed), under), under);
                output.WriteLine($"{(dark ? "Dark" : "Light")} {role}: hover {hoverRatio:F3}:1, pressed {pressedRatio:F3}:1");
                Assert.True(pressedRatio > hoverRatio, $"{role}: pressed {pressedRatio:F3}:1 is not stronger than hover {hoverRatio:F3}:1");
            }
        }
        finally { host.Close(); }
    }
}
