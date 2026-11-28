// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Checks opt-in integration with units, motion preferences, popup rows and spinner visibility.</summary>
public sealed class InlineInputBehaviorTests
{
    /// <summary>Retains ordinary transitions, supports local/inherited reduced motion, and resolves the unit role.</summary>
    [AvaloniaFact]
    public void UnitsAndReducedMotionKeepTheirRoles()
    {
        var unit = new TextBlock { Text = "mm", Classes = { "inlineInputUnit" } };
        var panel = new StackPanel { Children = { unit } };
        foreach (string kind in InlineInputTestHost.Kinds)
        {
            TemplatedControl control = InlineInputTestHost.Sample(kind);
            control.Classes.Remove("reducedMotion");
            panel.Children.Add(control);
        }
        Window host = InlineInputTestHost.Create(panel);
        try
        {
            host.Show();
            Flush(host);
            foreach (bool dark in new[] { false, true })
            {
                host.RequestedThemeVariant = dark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
                Assert.Equal(ResourceColor(unit, "NfcTextSecondaryBrush"), ColorOf(unit.Foreground));
            }
            foreach (TemplatedControl control in panel.Children.OfType<TemplatedControl>())
            {
                Assert.NotNull(control.Transitions);
                Assert.Equal(3, control.Transitions.Count);
                control.Classes.Add("reducedMotion");
                Assert.Null(control.Transitions);
                control.Classes.Remove("reducedMotion");
                Assert.NotNull(control.Transitions);
            }
            panel.Classes.Add("reducedMotion");
            foreach (TemplatedControl control in panel.Children.OfType<TemplatedControl>())
                Assert.Null(control.Transitions);
        }
        finally { host.Close(); }
    }

    /// <summary>The real popup opens with the hover fill, while its item container keeps all pre-opt-in values.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenPopupKeepsExistingRows(bool dark)
    {
        var combo = (ComboBox)InlineInputTestHost.Sample("combobox");
        combo.Classes.Remove("inlineInput");
        Window host = InlineInputTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { combo } }, dark);
        try
        {
            FormsTestHost.Show(host);
            combo.IsDropDownOpen = true;
            Flush(host);
            Assert.True(Part<Popup>(combo, "PART_Popup").IsOpen);
            ComboBoxItem row = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(0));
            var expected = (ColorOf(row.Background), ColorOf(row.Foreground), row.Padding, row.CornerRadius, row.Template);
            combo.Classes.Add("inlineInput");
            Flush(host);
            Assert.Equal(ResourceColor(combo, "Nvt.Input.Inline.HoverFillBrush"), ColorOf(combo.Background));
            Assert.Equal(0, ColorOf(combo.BorderBrush).A);
            Assert.Equal(expected, (ColorOf(row.Background), ColorOf(row.Foreground), row.Padding, row.CornerRadius, row.Template));
            KeyStroke(host, Key.Escape);
            Assert.False(combo.IsDropDownOpen);
        }
        finally { host.Close(); }
    }

    /// <summary>Changing spinner placement or hiding it preserves an unclipped full-row focus ring.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SpinnerFeedbackAndRingFollowVisibility(bool dark, bool square)
    {
        var numeric = (NumericUpDown)InlineInputTestHost.Sample("numericupdown");
        Window host = InlineInputTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { numeric } }, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            Assert.True(InlineInputTestHost.Target(numeric).Focus(NavigationMethod.Tab));
            foreach (Location location in new[] { Location.Left, Location.Right })
                foreach (bool visible in new[] { true, false })
                {
                    numeric.ButtonSpinnerLocation = location;
                    numeric.ShowButtonSpinner = visible;
                    Flush(host);
                    Border ring = FormsTestHost.Ring(numeric);
                    Assert.True(ring.IsVisible);
                    Assert.Equal(new Point(-4, -4), ring.TranslatePoint(default, numeric));
                    Assert.Equal(numeric.Bounds.Width + 8, ring.Bounds.Width);
                    Assert.Equal(numeric.Bounds.Height + 8, ring.Bounds.Height);
                }
            numeric.ShowButtonSpinner = true;
            RepeatButton increase = Part<RepeatButton>(numeric, "PART_IncreaseButton");
            ((IPseudoClasses)increase.Classes).Set(":pointerover", true);
            Flush(host);
            Assert.Equal(ResourceColor(numeric, "Nvt.Input.Inline.HoverFillBrush"), ColorOf(increase.Background));
            Assert.Equal(new CornerRadius(6), increase.CornerRadius);
            ((IPseudoClasses)increase.Classes).Set(":pressed", true);
            Flush(host);
            Assert.Equal(ResourceColor(numeric, "Nvt.Input.Inline.HoverFillBrush"), ColorOf(increase.Background));
            numeric.IsReadOnly = true;
            Flush(host);
            Assert.Equal(0, ColorOf(increase.Background).A);
            Assert.Equal(ResourceColor(numeric, "NfcTextMutedBrush"), ColorOf(increase.Foreground));
        }
        finally { host.Close(); }
    }
}
