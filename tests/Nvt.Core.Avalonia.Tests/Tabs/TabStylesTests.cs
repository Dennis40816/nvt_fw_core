// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Forms;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Tabs;

/// <summary>Checks strip geometry, soft selection states, keyboard focus, and native content switching.</summary>
public sealed class TabStylesTests
{
    /// <summary>Switches the selected item, strip, and focus corners at runtime in both themes.</summary>
    [AvaloniaFact]
    public void TabItemCornersFollowRuntimeShape()
    {
        var first = new TabItem { Header = "Overview", Content = "Overview content" };
        var tabs = new TabControl { ItemsSource = new[] { first, new TabItem { Header = "Details", Content = "Details content" } } };
        Window host = FormsTestHost.Create(tabs);
        try
        {
            FormsTestHost.Show(host);
            object? template = first.Template;
            foreach (bool dark in new[] { false, true })
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill, ThemeShape.Square })
            {
                host.RequestedThemeVariant = dark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, shape);
                Flush(host);
                CornerRadius corner = new(shape == ThemeShape.Pill ? 999 : 6);
                Assert.Equal(corner, Part<Border>(first, "TabBody").CornerRadius);
                Assert.Equal(corner, Part<Border>(tabs, "TabStrip").CornerRadius);
                Assert.Equal(32, first.Bounds.Height);
                Assert.Same(template, first.Template);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Checks all selected and unselected states against their shared palette and focus tokens.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TabStatesAndFocusMeetContrast(bool dark, bool square)
    {
        var before = new Grid { Focusable = true, Height = 20 };
        var tab = new TabItem { Header = "Overview" };
        Window host = FormsTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { before, tab } }, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            foreach (bool selected in new[] { false, true })
            foreach (FormState state in FormsTestHost.States.Take(5))
            {
                tab.IsSelected = selected;
                tab.IsEnabled = !state.Disabled;
                var pseudo = (IPseudoClasses)tab.Classes;
                pseudo.Set(":pointerover", state.Hover);
                pseudo.Set(":pressed", state.Pressed);
                pseudo.Set(":focus-visible", state.Focus);
                Flush(host);
                string fill = state.Disabled ? selected ? "DisabledSelectedFillBrush" : "DisabledFillBrush"
                    : selected ? state.Pressed ? "SelectedPressedFillBrush" : state.Hover ? "SelectedHoverFillBrush" : "SelectedFillBrush"
                    : state.Pressed ? "PressedFillBrush" : state.Hover ? "HoverFillBrush" : "FillBrush";
                string text = state.Disabled ? "DisabledTextBrush" : selected ? "SelectedTextBrush"
                    : state.Pressed ? "PressedTextBrush" : state.Hover ? "HoverTextBrush" : "TextBrush";
                Assert.Equal(ResourceColor(tab, "Nvt.Tab." + fill), ColorOf(tab.Background));
                Assert.Equal(ResourceColor(tab, "Nvt.Tab." + text), ColorOf(tab.Foreground));
                var background = fill == "FillBrush" ? ResourceColor(tab, "Nvt.Tab.StripBrush") : ColorOf(tab.Background);
                Assert.True(Contrast(ColorOf(tab.Foreground), background) >= (state.Disabled ? 3 : 4.5));
                Assert.Equal(selected, Part<Border>(tab, "TabIndicator").IsVisible);
                Assert.Equal(state.Focus && !state.Disabled, Part<Border>(tab, "TabFocusRing").IsVisible);
                if (selected) Assert.True(Contrast(ColorOf(Part<Border>(tab, "TabIndicator").Background), background) >= 3);
            }
            tab.IsEnabled = true;
            Assert.True(tab.Focus(NavigationMethod.Pointer));
            Assert.False(Part<Border>(tab, "TabFocusRing").IsVisible);
            Assert.True(before.Focus());
            KeyStroke(host, Key.Tab);
            Assert.True(tab.IsFocused);
            Flush(host);
            Border ring = Part<Border>(tab, "TabFocusRing");
            Assert.True(ring.IsVisible);
            Assert.Equal(new Thickness(2), ring.BorderThickness);
            Assert.Equal(new Point(-4, -4), ring.TranslatePoint(default, tab));
            Assert.Equal(tab.Bounds.Width + 8, ring.Bounds.Width);
            Assert.Null(tab.FocusAdorner);
        }
        finally { host.Close(); }
    }

    /// <summary>Places the selected indicator toward content for all native strip placements.</summary>
    [AvaloniaTheory]
    [InlineData(Dock.Top)]
    [InlineData(Dock.Bottom)]
    [InlineData(Dock.Left)]
    [InlineData(Dock.Right)]
    public void IndicatorFollowsTabStripPlacement(Dock placement)
    {
        var item = new TabItem { Header = "Overview", Content = "Content" };
        var tabs = new TabControl { TabStripPlacement = placement, ItemsSource = new[] { item } };
        Window host = FormsTestHost.Create(tabs);
        try
        {
            FormsTestHost.Show(host);
            Border indicator = Part<Border>(item, "TabIndicator");
            Assert.True(indicator.IsVisible);
            if (placement is Dock.Left or Dock.Right)
            {
                Assert.Equal(2, indicator.Bounds.Width);
                Assert.Equal(16, indicator.Bounds.Height);
                Assert.Equal(placement == Dock.Left ? global::Avalonia.Layout.HorizontalAlignment.Right
                    : global::Avalonia.Layout.HorizontalAlignment.Left, indicator.HorizontalAlignment);
            }
            else
            {
                Assert.Equal(2, indicator.Bounds.Height);
                Assert.Equal(placement == Dock.Top ? global::Avalonia.Layout.VerticalAlignment.Bottom
                    : global::Avalonia.Layout.VerticalAlignment.Top, indicator.VerticalAlignment);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Retains default arrow-key selection, disabled-item skipping, and selected content hosts.</summary>
    [AvaloniaFact]
    public void ArrowKeysSwitchContentAndSkipDisabledTab()
    {
        var items = new[] { new TabItem { Header = "Overview", Content = "Overview content" },
            new TabItem { Header = "Unavailable", IsEnabled = false },
            new TabItem { Header = "Details", Content = "Details content" } };
        var tabs = new TabControl { ItemsSource = items };
        Window host = FormsTestHost.Create(tabs);
        try
        {
            FormsTestHost.Show(host);
            items[0].Focus(NavigationMethod.Tab);
            KeyStroke(host, Key.Right);
            Assert.Equal(2, tabs.SelectedIndex);
            Assert.Equal("Details content", tabs.SelectedContent);
            Assert.Contains(tabs.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Details content");
            KeyStroke(host, Key.Left);
            Assert.Equal(0, tabs.SelectedIndex);
        }
        finally { host.Close(); }
    }
}
