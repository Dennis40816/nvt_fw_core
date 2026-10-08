// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Nvt.Core.Avalonia.Theme;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Checks the shipped styles' runtime shape contract and literal white selection colors.</summary>
public sealed class ToggleShapeTests(ITestOutputHelper output)
{
    /// <summary>Updates every attached role from one theme root while preserving switch geometry and focus gaps.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeShapeChangesAttachedRolesAndPreservesSwitchTracks(bool dark)
    {
        var segment = (StateToggleButton)Sample("toggleSegment", new ToggleState("Checked", Checked: true));
        var group = Group(segment);
        ToggleButton[] buttons = [segment, .. Roles.Skip(1).Select(role => Sample(role, new ToggleState("Checked", Checked: true)))];
        var row = new StackPanel { Spacing = 12, Margin = new Thickness(16), Children = { group } };
        foreach (ToggleButton button in buttons.Skip(1)) row.Children.Add(button);
        Window host = Create(row, dark, height: 420);
        try
        {
            Show(host);
            // Default comes from ThemeTokens, before any explicit theme setting exists.
            Assert.Equal(new CornerRadius(999), group.CornerRadius);
            Assert.All(buttons, button => Assert.Equal(new CornerRadius(999), button.CornerRadius));
            var templates = buttons.Select(button => button.Template).ToArray();
            foreach (bool nextDark in new[] { dark, !dark, dark })
            foreach (bool square in new[] { true, false, true, false })
            {
                host.RequestedThemeVariant = nextDark ? ThemeVariant.Dark : ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Flush(host);
                Assert.Equal(new CornerRadius(square ? 6 : 999), group.CornerRadius);
                for (int index = 0; index < buttons.Length; index++)
                {
                    ToggleButton button = buttons[index];
                    bool isSwitch = button is ToggleSwitch || button.Classes.Contains("toggleSwitch");
                    Assert.Same(templates[index], button.Template);
                    Assert.True(button.GetDiagnostic(TemplatedControl.CornerRadiusProperty).Priority is BindingPriority.Style or BindingPriority.StyleTrigger);
                    Assert.Equal(new CornerRadius(isSwitch || !square ? 999 : 6), button.CornerRadius);
                    foreach (ToggleState state in States)
                    {
                        SetState(button, state);
                        Flush(host);
                        Border body = Part(button, "ToggleBody");
                        Assert.Equal(button.CornerRadius, body.CornerRadius);
                        Border ring = Part(button, "ToggleFocusRing");
                        Assert.Equal(new CornerRadius(isSwitch || !square ? 999 : 10), ring.CornerRadius);
                        if (state.Focus)
                        {
                            Assert.True(ring.IsVisible);
                            Assert.Equal(body.Bounds.Width + 8, ring.Bounds.Width);
                            Assert.Equal(body.Bounds.Height + 8, ring.Bounds.Height);
                        }
                        if (isSwitch)
                        {
                            Assert.True(body.CornerRadius.TopLeft >= body.Bounds.Height / 2);
                            Ellipse knob = Assert.Single(button.GetVisualDescendants().OfType<Ellipse>());
                            if (state.Checked) Assert.Equal(Color.Parse("#FFFFFF"), ColorOf(knob.Fill));
                        }
                        else if (state.Checked && !state.Disabled)
                        {
                            Assert.Equal(Color.Parse("#FFFFFF"), ColorOf(button.Foreground));
                        }
                    }
                }
            }
            Assert.Single(host.Resources.MergedDictionaries);
        }
        finally { host.Close(); }
    }

    /// <summary>Checks the actual pin glyph and solid selected fills in both shapes and both themes.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SelectedPinUsesLiteralWhiteAndAccessibleAccent(bool dark, bool square)
    {
        ToggleButton icon = ToggleStylesRenderer.Icon(new ToggleState("Checked", Checked: true));
        Window host = Create(icon, dark);
        try
        {
            Show(host);
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            foreach (ToggleState state in States.Where(state => state.Checked && !state.Disabled))
            {
                SetState(icon, state);
                Flush(host);
                var glyph = Assert.IsType<global::Avalonia.Controls.Shapes.Path>(icon.Content);
                Assert.Equal(Color.Parse("#FFFFFF"), ColorOf(glyph.Stroke));
                Assert.Equal(Color.Parse(dark ? state.Hover || state.Pressed ? "#0E3C9E" : "#1148BE"
                    : state.Hover || state.Pressed ? "#1148BE" : "#1557E9"), ColorOf(icon.Background));
                Assert.Equal(new CornerRadius(square ? 6 : 999), Part(icon, "ToggleBody").CornerRadius);
                Assert.Empty(icon.GetVisualDescendants().OfType<CheckBox>());
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Checks literal danger fills, white contrast, neutral precedence, and standard focus for every role.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DangerUsesAccessibleRedAndStandardFocus(bool dark, bool square)
    {
        ToggleState[] states =
        [
            .. States,
            new("Checked focus", Checked: true, Focus: true),
            new("Checked pressed without hover", Checked: true, Pressed: true),
            new("Disabled interactions", Disabled: true, Hover: true, Pressed: true, Focus: true),
            new("Disabled checked interactions", Checked: true, Disabled: true, Hover: true, Pressed: true, Focus: true),
        ];
        foreach (string role in Roles)
        {
            ToggleButton button = role == "toggleIcon" ? ToggleStylesRenderer.Icon(states[0])
                : Sample(role, states[0], "Danger");
            var label = new TextBlock { Text = "Danger" };
            bool isSwitch = role is "toggleSwitch" or "nativeSwitch";
            if (!isSwitch && role != "toggleIcon") button.Content = label;
            Control child = role == "toggleSegment" ? Group((StateToggleButton)button) : button;
            Window host = Create(child, dark);
            try
            {
                Show(host);
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                foreach (ToggleState state in states)
                {
                    button.Classes.Remove("danger");
                    SetState(button, state);
                    Flush(host);
                    Color normalFill = ColorOf(button.Background), normalLabel = ColorOf(button.Foreground);
                    button.Classes.Add("danger");
                    Flush(host);
                    Color fill = ColorOf(Part(button, "ToggleBody").Background);
                    Assert.Equal(fill, ColorOf(button.Background));
                    if (state.Checked && !state.Disabled)
                    {
                        string token = state.Pressed ? "Nvt.Toggle.DangerFillPressedBrush"
                            : state.Hover ? "Nvt.Toggle.DangerFillPointerOverBrush" : "Nvt.Toggle.DangerFillBrush";
                        Color literalFill = Color.Parse(state.Hover || state.Pressed ? "#861B2C" : "#A82035");
                        Color white = Color.Parse("#FFFFFF");
                        Assert.Equal(literalFill, ResourceColor(button, token));
                        Assert.Equal(literalFill, fill);
                        Assert.NotEqual(normalFill, fill);
                        Assert.Equal(white, ColorOf(button.Foreground));
                        Color visibleWhite = isSwitch ? ColorOf(Assert.Single(button.GetVisualDescendants().OfType<Ellipse>()).Fill)
                            : role == "toggleIcon" ? ColorOf(Assert.IsType<global::Avalonia.Controls.Shapes.Path>(button.Content).Stroke)
                            : ColorOf(label.Foreground);
                        Assert.Equal(white, visibleWhite);
                        double contrast = ToggleStylesTests.Contrast(white, literalFill);
                        Assert.True(contrast >= 4.5, $"{dark}/{square}/{role}/{state.Name}: white {contrast:F3}");
                        output.WriteLine($"{(dark ? "dark" : "light")}/{(square ? "square" : "pill")}/{role}/{state.Name}: {token}, {literalFill}, white {contrast:F3}:1");
                    }
                    else
                    {
                        Assert.Equal(normalFill, fill);
                        Assert.Equal(normalLabel, ColorOf(button.Foreground));
                    }
                    Border ring = Part(button, "ToggleFocusRing");
                    Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                    Assert.Equal(ResourceColor(button, "Nvt.Focus.RingBrush"), ColorOf(ring.BorderBrush));
                    Assert.Null(button.FocusAdorner);
                }
            }
            finally { host.Close(); }
        }
    }
}
