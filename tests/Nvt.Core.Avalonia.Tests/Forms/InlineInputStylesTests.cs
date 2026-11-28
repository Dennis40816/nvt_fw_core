// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Verifies the opt-in role, state precedence, native editing, and stable geometry.</summary>
public sealed class InlineInputStylesTests
{
    /// <summary>Checks combinations of supported hover, press, open, focus, disabled, read-only and either error source.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StateMatrixUsesInlineTokens(bool dark, bool square)
    {
        foreach (string kind in InlineInputTestHost.Kinds)
        {
            TemplatedControl control = InlineInputTestHost.Sample(kind);
            Window host = InlineInputTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { control } }, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                FormsTestHost.Show(host);
                for (int bits = 0; bits < 512; bits++)
                {
                    var state = new FormState("Matrix", Hover: (bits & 1) != 0, Pressed: control is ComboBox && (bits & 2) != 0,
                        Open: control is ComboBox && (bits & 4) != 0, Focus: (bits & 8) != 0, Disabled: (bits & 16) != 0,
                        ReadOnly: (bits & 32) != 0, Error: (bits & 64) != 0);
                    bool keyboardFocus = (bits & 256) == 0;
                    InlineInputTestHost.State(control, state, nativeError: (bits & 128) != 0, keyboardFocus: keyboardFocus);
                    Flush(host);
                    AssertState(control, state, keyboardFocus);
                }
            }
            finally { host.Close(); }
        }
    }

    private static void AssertState(TemplatedControl control, FormState state, bool keyboardFocus)
    {
        string fill = state.Disabled ? "DisabledFillBrush" : state.ReadOnly ? "ReadOnlyFillBrush"
            : state.Focus ? "EditingFillBrush" : state.Hover || state.Pressed || state.Open ? "HoverFillBrush" : "FillBrush";
        string border = state.Error ? "ErrorBorderBrush" : state.Disabled || state.ReadOnly ? "BorderBrush"
            : state.Focus ? "EditingBorderBrush" : "BorderBrush";
        string text = state.Disabled ? "DisabledTextBrush" : state.ReadOnly ? "ReadOnlyTextBrush" : "TextBrush";
        Border body = FormsTestHost.Body(control);
        Assert.Equal(ResourceColor(control, "Nvt.Input.Inline." + fill), ColorOf(body.Background));
        Assert.Equal(ResourceColor(control, "Nvt.Input.Inline." + border), ColorOf(body.BorderBrush));
        Assert.Equal(ResourceColor(control, "Nvt.Input.Inline." + text), ColorOf(control.Foreground));
        Assert.Equal(new Thickness(1), body.BorderThickness);
        Assert.Equal(new Thickness(8, 0), control.Padding);
        Assert.Equal(new CornerRadius(6), body.CornerRadius);
        Assert.True(FormsTestHost.Ring(control).IsVisible == (state.Focus && keyboardFocus && !state.Disabled && !state.ReadOnly), $"{control.GetType().Name}: {state}; {string.Join(",", control.Classes)}");
        Assert.Null(control.Transitions);
        Control target = InlineInputTestHost.Target(control);
        if (target != control)
        {
            Assert.Equal(ColorOf(control.Foreground), ColorOf(((TextBox)target).Foreground));
            TextPresenter presenter = Part<TextPresenter>(target, "PART_TextPresenter");
            Assert.Equal(ColorOf(control.Foreground), ColorOf(presenter.Foreground));
            Assert.Equal(1, Nvt.Core.Avalonia.Tests.Theme.ShownColor.ElementOpacity(presenter));
            foreach (Border inner in target.GetVisualDescendants().OfType<Border>().Where(part => part.Name is "FormBody" or "PART_BorderElement"))
            {
                Assert.True(ColorOf(inner.Background).A == 0, $"{control.GetType().Name}/{inner.Name}: {state}; fill {inner.Background}");
                Assert.Equal(0, ColorOf(inner.BorderBrush).A);
            }
        }
    }

    /// <summary>Uses pointer focus and real Tab navigation to check no text motion and exactly one unclipped ring.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeFocusKeepsTextAndOneFullRowRing(bool dark, bool square)
    {
        foreach (string kind in InlineInputTestHost.Kinds)
        {
            var before = new Grid { Focusable = true, Height = 20 };
            TemplatedControl control = InlineInputTestHost.Sample(kind);
            Window host = InlineInputTestHost.Create(new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { before, control } }, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                FormsTestHost.Show(host);
                Control target = InlineInputTestHost.Target(control);
                Control presenter = kind == "combobox" ? Part<ContentPresenter>(control, "SelectionPresenter")
                    : Part<TextPresenter>(target, "PART_TextPresenter");
                Point? restPosition = presenter.TranslatePoint(default, control);
                Rect restBounds = presenter.Bounds;
                Assert.True(target.Focus(NavigationMethod.Pointer));
                Flush(host);
                Assert.Equal(0, ColorOf(FormsTestHost.Body(control).Background).A);
                Assert.Equal(ResourceColor(control, "NfcBorderBrush"), ColorOf(control.BorderBrush));
                Assert.False(FormsTestHost.Ring(control).IsVisible);
                Assert.Equal(restPosition, presenter.TranslatePoint(default, control));
                Assert.Equal(restBounds, presenter.Bounds);
                Assert.True(before.Focus());
                KeyStroke(host, Key.Tab);
                Assert.True(target.IsFocused);
                Border ring = FormsTestHost.Ring(control);
                Assert.True(ring.IsVisible);
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.Equal(new Thickness(-4), ring.Margin);
                Assert.Equal(new CornerRadius(10), ring.CornerRadius);
                Assert.Equal(new Point(-4, -4), ring.TranslatePoint(default, control));
                Assert.Equal(control.Bounds.Width + 8, ring.Bounds.Width);
                Assert.Equal(control.Bounds.Height + 8, ring.Bounds.Height);
                Assert.Equal(32, control.Bounds.Height);
                Assert.Single(control.GetVisualDescendants().OfType<Border>(), part => part.Name is "FormFocusRing" or "SpinnerFocusRing" && part.IsEffectivelyVisible);
                foreach (Visual ancestor in ring.GetVisualAncestors().TakeWhile(ancestor => ancestor != host))
                    Assert.False(ancestor.ClipToBounds);
                Assert.Equal(restPosition, presenter.TranslatePoint(default, control));
                Assert.Equal(restBounds, presenter.Bounds);
                if (kind == "editable")
                {
                    host.KeyTextInput("Custom");
                    Assert.Equal("Custom", ((ComboBox)control).Text);
                }
                DataValidationErrors.SetError(control, new InvalidOperationException("Invalid value"));
                Flush(host);
                Assert.Equal(ResourceColor(control, "NfcDangerBorderBrush"), ColorOf(control.BorderBrush));
                Assert.Equal(new Point(-4, -4), ring.TranslatePoint(default, FormsTestHost.Body(control)));
                Assert.Equal(FormsTestHost.Body(control).Bounds.Height + 8, ring.Bounds.Height);
                control.IsEnabled = false;
                Flush(host);
                Assert.False(ring.IsVisible);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Loading the opt-in sheet leaves every unclassified control's existing property values unchanged.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnclassifiedControlsKeepExistingValues(bool dark)
    {
        foreach (string kind in InlineInputTestHost.Kinds)
        {
            TemplatedControl control = InlineInputTestHost.Sample(kind);
            control.Classes.Remove("inlineInput");
            Window host = FormsTestHost.Create(new StackPanel { Children = { control } }, dark);
            try
            {
                FormsTestHost.Show(host);
                foreach (FormState state in FormsTestHost.States.Concat(FormsTestHost.ReadOnlyStates))
                {
                    FormsTestHost.SetState(control, state);
                    Flush(host);
                    var expected = Snapshot(control);
                    InlineInputTestHost.Include(host);
                    Flush(host);
                    Assert.Equal(expected, Snapshot(control));
                    host.Styles.RemoveAt(host.Styles.Count - 1);
                }
            }
            finally { host.Close(); }
        }
    }

    private static (Color Fill, Color Border, Color Text, Thickness Padding, Thickness Frame, CornerRadius Radius, Rect Bounds, bool Ring) Snapshot(TemplatedControl control)
        => (ColorOf(control.Background), ColorOf(control.BorderBrush), ColorOf(control.Foreground), control.Padding,
            control.BorderThickness, control.CornerRadius, control.Bounds, FormsTestHost.Ring(control).IsVisible);
}
