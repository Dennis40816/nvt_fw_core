// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Choice;
using Nvt.Core.Avalonia.Tests.Theme;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Verifies form geometry, error precedence, native input, and keyboard-only exterior focus.</summary>
public sealed class FormStylesTests
{
    /// <summary>Changes the attached text field corner without replacing its template.</summary>
    [AvaloniaFact]
    public void TextBoxCornersFollowRuntimeShape() => CheckShape("textbox");

    /// <summary>Changes the numeric container and spinner corners without replacing their templates.</summary>
    [AvaloniaFact]
    public void NumericUpDownCornersFollowRuntimeShape() => CheckShape("numericupdown");

    /// <summary>Changes the closed selector corner without replacing its template.</summary>
    [AvaloniaFact]
    public void ComboBoxCornersFollowRuntimeShape() => CheckShape("combobox");

    private static void CheckShape(string kind)
    {
        TemplatedControl control = FormsTestHost.Sample(kind, new("Rest"));
        Window host = FormsTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { control } });
        try
        {
            FormsTestHost.Show(host);
            object? template = control.Template;
            foreach (bool dark in new[] { false, true })
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill, ThemeShape.Square })
            {
                host.RequestedThemeVariant = dark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, shape);
                Flush(host);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 6), FormsTestHost.Body(control).CornerRadius);
                Assert.Equal(32, control.Bounds.Height);
                Assert.Same(template, control.Template);
                if (control is NumericUpDown)
                    Assert.Equal(control.CornerRadius, Part<ButtonSpinner>(control, "PART_Spinner").CornerRadius);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Checks rest, hover, pressed, disabled, read-only, and error borders in both themes and shapes.</summary>
    [AvaloniaTheory]
    [InlineData("textbox", false, false)]
    [InlineData("textbox", true, true)]
    [InlineData("textbox", false, true)]
    [InlineData("textbox", true, false)]
    [InlineData("numericupdown", false, false)]
    [InlineData("numericupdown", true, true)]
    [InlineData("numericupdown", false, true)]
    [InlineData("numericupdown", true, false)]
    [InlineData("combobox", false, false)]
    [InlineData("combobox", true, true)]
    [InlineData("combobox", false, true)]
    [InlineData("combobox", true, false)]
    public void FormStatesAndErrorClassResolveTokens(string kind, bool dark, bool square)
    {
        TemplatedControl control = FormsTestHost.Sample(kind, new("Rest"));
        Window host = FormsTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { control } }, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            foreach (FormState state in FormsTestHost.States)
            {
                FormsTestHost.SetState(control, state);
                Flush(host);
                string fill = state.Disabled ? "DisabledFillBrush" : state.ReadOnly ? "ReadOnlyFillBrush"
                    : state.Pressed ? "PressedFillBrush" : state.Hover ? "HoverFillBrush" : "FillBrush";
                string border = state.Error ? "ErrorBorderBrush" : state.Disabled ? "DisabledTextBrush"
                    : state.ReadOnly ? "BorderBrush" : state.Pressed ? "PressedBorderBrush" : state.Hover ? "HoverBorderBrush" : "BorderBrush";
                Border body = FormsTestHost.Body(control);
                if (control is NumericUpDown)
                {
                    TextBox input = Part<TextBox>(control, "PART_TextBox");
                    Assert.Equal(0, ColorOf(FormsTestHost.Body(input).Background).A);
                }
                Assert.Equal(ResourceColor(control, "Nvt.Form." + fill), ColorOf(body.Background));
                Assert.Equal(ResourceColor(control, "Nvt.Form." + border), ColorOf(body.BorderBrush));
                Assert.True(Contrast(ColorOf(body.BorderBrush), ColorOf(body.Background)) >= 3);
                Assert.True(Contrast(ColorOf(control.Foreground), ColorOf(body.Background)) >= (state.Disabled ? 3 : 4.5));
                Border ring = FormsTestHost.Ring(control);
                Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.Equal(new Thickness(-4), ring.Margin);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Uses actual Tab navigation and pointer focus to verify one ring around the full row.</summary>
    [AvaloniaTheory]
    [InlineData("textbox", false, false)]
    [InlineData("textbox", true, true)]
    [InlineData("numericupdown", false, false)]
    [InlineData("numericupdown", true, true)]
    [InlineData("combobox", false, false)]
    [InlineData("combobox", true, true)]
    public void KeyboardFocusShowsOneOuterRing(string kind, bool dark, bool square)
    {
        var before = new Grid { Focusable = true, Height = 20 };
        TemplatedControl control = FormsTestHost.Sample(kind, new("Rest"));
        Window host = FormsTestHost.Create(new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { before, control } }, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            Control target = control is NumericUpDown ? Part<TextBox>(control, "PART_TextBox") : control;
            Assert.True(target.Focus(NavigationMethod.Pointer));
            Flush(host);
            Assert.False(FormsTestHost.Ring(control).IsVisible);
            Assert.True(before.Focus());
            KeyStroke(host, Key.Tab);
            Assert.True(target.IsFocused);
            Border ring = FormsTestHost.Ring(control);
            Assert.True(ring.IsVisible);
            foreach (Visual ancestor in ring.GetVisualAncestors().TakeWhile(ancestor => ancestor != host))
                Assert.False(ancestor.ClipToBounds, $"Focus ring clipped by {ancestor.GetType().Name}");
            Assert.Equal(new Thickness(2), ring.BorderThickness);
            Assert.Equal(new CornerRadius(square ? 10 : 999), ring.CornerRadius);
            Assert.Equal(new Point(-4, -4), ring.TranslatePoint(default, control));
            Assert.Equal(control.Bounds.Width + 8, ring.Bounds.Width);
            Assert.Equal(control.Bounds.Height + 8, ring.Bounds.Height);
            Assert.Null(control.FocusAdorner);
            Assert.Single(control.GetVisualDescendants().OfType<Border>(), part => part.Name == "FormFocusRing" && part.IsEffectivelyVisible);
            control.IsEnabled = false;
            Flush(host);
            Assert.False(ring.IsVisible);
        }
        finally { host.Close(); }
    }

    /// <summary>Keeps the numeric ring aligned when the native spinner moves left or is hidden.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NumericFocusRingFollowsSpinnerPlacement(bool left, bool visible)
    {
        var numeric = new NumericUpDown { Value = 42, ShowButtonSpinner = visible,
            ButtonSpinnerLocation = left ? Location.Left : Location.Right };
        Window host = FormsTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { numeric } });
        try
        {
            FormsTestHost.Show(host);
            Part<TextBox>(numeric, "PART_TextBox").Focus(NavigationMethod.Tab);
            Flush(host);
            Border ring = FormsTestHost.Ring(numeric);
            Assert.True(ring.IsVisible);
            Assert.Equal(new Point(-4, -4), ring.TranslatePoint(default, numeric));
            Assert.Equal(numeric.Bounds.Width + 8, ring.Bounds.Width);
            Assert.Equal(numeric.Bounds.Height + 8, ring.Bounds.Height);
        }
        finally { host.Close(); }
    }

    /// <summary>Exercises text input, read-only editing, spinner buttons, and selector popup keyboard behavior.</summary>
    [AvaloniaFact]
    public void NativeEditingSpinningAndSelectionStillWork()
    {
        var text = new TextBox { Text = "" };
        var numeric = new NumericUpDown { Value = 42, Minimum = 0, Maximum = 100 };
        var combo = new ComboBox { ItemsSource = new[] { "Alpha", "Beta", "Gamma" }, SelectedIndex = 0 };
        Window host = FormsTestHost.Create(new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { text, numeric, combo } });
        try
        {
            FormsTestHost.Show(host);
            text.Focus(NavigationMethod.Tab);
            host.KeyTextInput("Sample");
            Assert.Equal("Sample", text.Text);
            text.IsReadOnly = true;
            host.KeyTextInput("Ignored");
            Assert.Equal("Sample", text.Text);
            TextBox input = Part<TextBox>(numeric, "PART_TextBox");
            input.Focus(NavigationMethod.Tab);
            KeyStroke(host, Key.Up);
            Assert.Equal(43, numeric.Value);
            KeyStroke(host, Key.Down);
            Assert.Equal(42, numeric.Value);
            RepeatButton increase = Part<RepeatButton>(numeric, "PART_IncreaseButton");
            Point center = increase.TranslatePoint(new Point(increase.Bounds.Width / 2, increase.Bounds.Height / 2), host)!.Value;
            host.MouseDown(center, MouseButton.Left);
            host.MouseUp(center, MouseButton.Left);
            Flush(host);
            Assert.Equal(43, numeric.Value);
            numeric.IsReadOnly = true;
            KeyStroke(host, Key.Up);
            Assert.Equal(43, numeric.Value);
            numeric.IsReadOnly = false;
            combo.Focus(NavigationMethod.Tab);
            KeyStroke(host, Key.Down);
            Assert.Equal(1, combo.SelectedIndex);
            combo.IsDropDownOpen = true;
            Flush(host);
            Assert.True(Part<Popup>(combo, "PART_Popup").IsOpen);
            Assert.Equal(ResourceColor(combo, "Nvt.Form.PressedFillBrush"), ColorOf(FormsTestHost.Body(combo).Background));
            KeyStroke(host, Key.Escape);
            Assert.False(combo.IsDropDownOpen);
        }
        finally { host.Close(); }
    }

    /// <summary>Keeps palette, radii, dimensions, padding, and thickness out of new style rules.</summary>
    [Fact]
    public void StylesContainNoLiteralColorsOrGeometry()
    {
        foreach (string file in new[] { "FormStyles", "TabStyles", "TextStyles" })
        {
            string source = ThemeContractTests.ReadExtracted(file).ToString();
            Assert.DoesNotMatch(@"#[0-9A-Fa-f]{6,8}\b", source);
            foreach (Match match in Regex.Matches(source, @"(?:Property=""(?<property>[^""]+)"" Value|(?<property>Width|Height|MinWidth|MinHeight|Padding|Margin|CornerRadius|BorderThickness|StrokeThickness))=""(?<value>[^""]+)"""))
                if (Regex.IsMatch(match.Groups["property"].Value, @"(?:Width|Height|Padding|Margin|Radius|Thickness)$"))
                    Assert.StartsWith("{", match.Groups["value"].Value, StringComparison.Ordinal);
        }
    }

    /// <summary>Honors the existing reduced-motion class on a control or ancestor.</summary>
    [AvaloniaFact]
    public void ReducedMotionDisablesFormAndTabTransitions()
    {
        TemplatedControl form = FormsTestHost.Sample("textbox", new("Rest"));
        var tab = new TabItem { Header = "Overview" };
        var root = new StackPanel { Children = { form, tab } };
        Window host = FormsTestHost.Create(root);
        try
        {
            host.Show();
            Flush(host);
            Assert.NotNull(form.Transitions);
            Assert.NotNull(tab.Transitions);
            root.Classes.Add("reducedMotion");
            Assert.Null(form.Transitions);
            Assert.Null(tab.Transitions);
        }
        finally { host.Close(); }
    }
}
