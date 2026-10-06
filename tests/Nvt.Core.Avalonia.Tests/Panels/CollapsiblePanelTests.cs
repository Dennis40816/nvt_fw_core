// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Panels;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Nvt.Core.Avalonia.Tests.Panels;

/// <summary>Characterizes the frozen NFH panel behavior using synthetic content.</summary>
public sealed class CollapsiblePanelTests
{
    /// <summary>Pins all five property defaults before initialization.</summary>
    [AvaloniaFact]
    public void DefaultsMatchFrozenPanel()
    {
        var panel = new CollapsiblePanel();
        Assert.Equal(string.Empty, panel.Title);
        Assert.True(panel.IsExpanded);
        Assert.True(panel.DefaultExpanded);
        Assert.True(panel.IsCollapsible);
        Assert.Null(panel.HeaderRight);
        Assert.False(panel.IsSet(CollapsiblePanel.IsExpandedProperty));
    }

    /// <summary>Initialization applies the default only when expansion has not been set.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializationUsesDefaultWhenExpansionIsUnset(bool defaultExpanded)
    {
        var panel = new CollapsiblePanel { DefaultExpanded = defaultExpanded };
        Assert.True(panel.IsExpanded);
        Window host = PanelsTestHost.Create(panel);
        try
        {
            Assert.True(panel.IsInitialized);
            Assert.Equal(defaultExpanded, panel.IsExpanded);
            panel.DefaultExpanded = !defaultExpanded;
            Assert.Equal(defaultExpanded, panel.IsExpanded);
        }
        finally { host.Close(); }
    }

    /// <summary>Even an explicitly set value equal to the property default wins at initialization.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InitializationPreservesExplicitExpansion(bool defaultExpanded, bool explicitExpanded)
    {
        var panel = new CollapsiblePanel { DefaultExpanded = defaultExpanded, IsExpanded = explicitExpanded };
        Assert.True(panel.IsSet(CollapsiblePanel.IsExpandedProperty));
        Window host = PanelsTestHost.Create(panel);
        try
        {
            Assert.True(panel.IsInitialized);
            Assert.Equal(explicitExpanded, panel.IsExpanded);
        }
        finally { host.Close(); }
    }

    /// <summary>Clearing an explicit value before initialization restores use of the default.</summary>
    [AvaloniaFact]
    public void InitializationUsesDefaultAfterExplicitExpansionIsCleared()
    {
        var panel = new CollapsiblePanel { DefaultExpanded = false, IsExpanded = true };
        panel.ClearValue(CollapsiblePanel.IsExpandedProperty);
        Window host = PanelsTestHost.Create(panel);
        try { Assert.False(panel.IsExpanded); }
        finally { host.Close(); }
    }

    /// <summary>A non-collapsible panel remains expanded regardless of initialization and assignment order.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonCollapsiblePanelCannotInitializeOrBeAssignedCollapsed(bool setCollapsibleFirst)
    {
        var panel = new CollapsiblePanel { DefaultExpanded = false };
        if (setCollapsibleFirst)
        {
            panel.IsCollapsible = false;
            panel.IsExpanded = false;
        }
        else
        {
            panel.IsExpanded = false;
            panel.IsCollapsible = false;
        }

        Window host = PanelsTestHost.Create(panel);
        try
        {
            Assert.True(panel.IsInitialized);
            Assert.True(panel.IsExpanded);
            panel.IsExpanded = false;
            Assert.True(panel.IsExpanded);
        }
        finally { host.Close(); }
    }

    /// <summary>Space on the actual header toggles both ways, updating body visibility and vector direction.</summary>
    [AvaloniaFact]
    public void HeaderTogglesBodyAndChevronInBothDirections()
    {
        var content = new TextBlock { Text = "Synthetic body" };
        var panel = new CollapsiblePanel { Title = "Synthetic panel", Content = content, DefaultExpanded = false };
        Window host = PanelsTestHost.Create(panel);
        try
        {
            ToggleButton header = PanelsTestHost.Find<ToggleButton>(panel, "panelBlockHeader");
            Border body = PanelsTestHost.Find<Border>(panel, "panelBlockContent");
            ShapePath chevron = PanelsTestHost.Find<ShapePath>(panel, "panelBlockChevron");
            Assert.False(panel.IsExpanded);
            Assert.False(header.IsChecked);
            Assert.False(body.IsVisible);
            Assert.False(body.IsEffectivelyVisible);
            Geometry collapsedChevron = Assert.IsAssignableFrom<Geometry>(chevron.Data);
            Assert.True(header.Focus());

            host.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            host.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            host.UpdateLayout();
            Assert.True(panel.IsExpanded);
            Assert.True(header.IsChecked);
            Assert.True(body.IsVisible);
            Assert.True(content.IsEffectivelyVisible);
            Assert.NotSame(collapsedChevron, chevron.Data);
            Assert.True(chevron.Data!.FillContains(new Point(2, 5)));
            Assert.False(chevron.Data.FillContains(new Point(2, 1)));

            host.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            host.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.False(panel.IsExpanded);
            Assert.False(header.IsChecked);
            Assert.False(body.IsVisible);
            Assert.False(content.IsEffectivelyVisible);
            Assert.True(chevron.Data!.FillContains(new Point(2, 1)));
            Assert.False(chevron.Data.FillContains(new Point(2, 5)));

            panel.IsExpanded = true;
            Assert.True(header.IsChecked);
            Assert.True(body.IsVisible);
        }
        finally { host.Close(); }
    }

    /// <summary>Disabling collapse expands a collapsed body; re-enabling retains it until the next toggle.</summary>
    [AvaloniaFact]
    public void ChangingCollapsibilityForcesExpansionAndAllowsLaterCollapse()
    {
        var panel = new CollapsiblePanel { IsExpanded = false, Content = new TextBlock { Text = "Body" } };
        Window host = PanelsTestHost.Create(panel);
        try
        {
            ToggleButton header = PanelsTestHost.Find<ToggleButton>(panel, "panelBlockHeader");
            Border body = PanelsTestHost.Find<Border>(panel, "panelBlockContent");
            Border chevronHost = PanelsTestHost.Find<Border>(panel, "panelBlockChevronHost");
            panel.IsCollapsible = false;
            Assert.True(panel.IsExpanded);
            Assert.True(header.IsChecked);
            Assert.False(header.IsEnabled);
            Assert.Equal(1, header.Opacity);
            Assert.True(body.IsVisible);
            Assert.False(chevronHost.IsVisible);
            Assert.False(header.Focus());
            panel.IsExpanded = false;
            Assert.True(panel.IsExpanded);
            Assert.True(body.IsVisible);

            panel.IsCollapsible = true;
            Assert.True(panel.IsExpanded);
            Assert.True(header.IsEnabled);
            Assert.True(chevronHost.IsVisible);
            Assert.True(header.Focus());
            host.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            host.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.False(panel.IsExpanded);
            Assert.False(body.IsVisible);
        }
        finally { host.Close(); }
    }

    /// <summary>Selectable header text neither takes focus nor blocks a pointer toggle, as in NFH.</summary>
    [AvaloniaFact]
    public void SelectableHeaderTextDoesNotBlockPointerToggle()
    {
        var right = new SelectableTextBlock { Text = "Selectable header text" };
        var panel = new CollapsiblePanel { Title = "Title", HeaderRight = right, Content = new TextBlock { Text = "Body" } };
        Window host = PanelsTestHost.Create(panel);
        try
        {
            ToggleButton header = PanelsTestHost.Find<ToggleButton>(panel, "panelBlockHeader");
            Assert.False(right.IsHitTestVisible);
            Assert.False(right.Focusable);
            Assert.False(right.Focus());
            Assert.True(panel.IsExpanded);

            Point center = right.TranslatePoint(new Point(right.Bounds.Width / 2, right.Bounds.Height / 2), host)!.Value;
            host.MouseDown(center, MouseButton.Left);
            host.MouseUp(center, MouseButton.Left);
            Assert.False(panel.IsExpanded);
            Assert.False(header.IsChecked);

            host.MouseDown(center, MouseButton.Left);
            host.MouseUp(center, MouseButton.Left);
            Assert.True(panel.IsExpanded);
            Assert.True(header.IsChecked);
        }
        finally { host.Close(); }
    }

    /// <summary>Header hover and pressed backgrounds use Core's secondary pair, even under a host theme that styles the presenter.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderStatesUseCoreBrushesOverHostThemeRules(bool dark)
    {
        var panel = new CollapsiblePanel { Title = "Title", Content = new TextBlock { Text = "Body" } };
        Window host = PanelsTestHost.Create(panel, window =>
        {
            window.Resources[typeof(ToggleButton)] = HostToggleTheme();
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        });
        try
        {
            ToggleButton header = PanelsTestHost.Find<ToggleButton>(panel, "panelBlockHeader");
            Border presenter = Assert.Single(header.GetVisualDescendants().OfType<Border>(),
                candidate => candidate.Name == "RoleBorder");
            Assert.True(header.IsChecked);
            AssertBrush(presenter.Background, "NfcAccentSurfaceBrush", dark);
            AssertBrush(presenter.BorderBrush, "NfcAccentBorderBrush", dark);

            Point center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), host)!.Value;
            host.MouseMove(center);
            AssertBrush(presenter.Background, "NfcAccentSurfaceSubtleBrush", dark);
            host.MouseDown(center, MouseButton.Left);
            AssertBrush(presenter.Background, "NfcSecondaryActionPressedBrush", dark);
            host.MouseUp(center, MouseButton.Left);
            Assert.False(panel.IsExpanded);
            AssertBrush(presenter.Background, "NfcSelectionSurfaceBrush", dark);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.BorderBrush).Color);
        }
        finally { host.Close(); }
    }

    /// <summary>A toggle button in the panel body keeps its own template and geometry.</summary>
    [AvaloniaFact]
    public void BodyToggleButtonKeepsItsOwnTemplate()
    {
        var bodyToggle = new ToggleButton { Content = "Body toggle" };
        var panel = new CollapsiblePanel { Content = bodyToggle };
        Window host = PanelsTestHost.Create(panel);
        try
        {
            ToggleButton header = PanelsTestHost.Find<ToggleButton>(panel, "panelBlockHeader");
            Assert.NotNull(header.Template);
            Assert.NotSame(header.Template, bodyToggle.Template);
            Assert.NotEqual(new Thickness(12, 10), bodyToggle.Padding);
            Assert.NotEqual(new Thickness(0, 0, 0, 1), bodyToggle.BorderThickness);
        }
        finally { host.Close(); }
    }

    /// <summary>Ports NFH's flat layout and no-Expander concern; header content remains visible when collapsed.</summary>
    [AvaloniaFact]
    public void FlatHeaderShowsTitleAndRightContentAboveTheBody()
    {
        var right = new TextBlock { Text = "Header action" };
        var content = new TextBlock { Text = "Body" };
        var panel = new CollapsiblePanel { Title = "Title", HeaderRight = right, Content = content };
        Window host = PanelsTestHost.Create(panel);
        try
        {
            ToggleButton header = PanelsTestHost.Find<ToggleButton>(panel, "panelBlockHeader");
            Border root = PanelsTestHost.Find<Border>(panel, "panelBlockRoot");
            Border body = PanelsTestHost.Find<Border>(panel, "panelBlockContent");
            StackPanel layout = Assert.IsType<StackPanel>(root.Child);
            Assert.Collection(layout.Children, child => Assert.Same(header, child), child => Assert.Same(body, child));
            Assert.Equal(new Thickness(1), root.BorderThickness);
            Assert.Equal(new CornerRadius(6), root.CornerRadius);
            Assert.Equal(new Thickness(0), root.Padding);
            Assert.False(root.ClipToBounds);
            Assert.Equal(new Thickness(14, 0), header.Padding);
            Assert.Equal(32, header.Height);
            Assert.Contains("actionGhost", header.Classes);
            Assert.Equal(new Thickness(1), header.BorderThickness);
            Assert.Equal(new CornerRadius(999), header.CornerRadius);
            Assert.Equal(HorizontalAlignment.Stretch, header.HorizontalContentAlignment);
            Assert.Equal(new Thickness(10), body.Padding);
            Assert.Equal(new Thickness(0, 1, 0, 0), body.BorderThickness);
            Assert.Empty(panel.GetVisualDescendants().OfType<Expander>());

            TextBlock title = Assert.Single(header.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Title");
            Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
            ContentPresenter rightPresenter = Assert.Single(header.GetVisualDescendants().OfType<ContentPresenter>(),
                presenter => ReferenceEquals(presenter.Content, right));
            Assert.Equal(1, Grid.GetColumn(rightPresenter));
            Assert.Equal(new Thickness(10, 0, 0, 0), rightPresenter.Margin);
            Assert.True(right.IsEffectivelyVisible);
            Assert.True(content.TranslatePoint(default, panel)!.Value.Y > title.TranslatePoint(default, panel)!.Value.Y);
            panel.IsExpanded = false;
            Assert.True(right.IsEffectivelyVisible);
            Assert.False(content.IsEffectivelyVisible);
        }
        finally { host.Close(); }
    }

    private static readonly string[] HostStates = [":pointerover", ":pressed", ":checked"];

    private static ControlTheme HostToggleTheme()
    {
        var theme = new ControlTheme(typeof(ToggleButton));
        foreach (string state in HostStates)
        {
            theme.Children.Add(new Style(selector => selector.Nesting().Class(state).Template()
                .OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters =
                {
                    new Setter(ContentPresenter.BackgroundProperty, Brushes.Red),
                    new Setter(ContentPresenter.BorderBrushProperty, Brushes.Red),
                },
            });
        }

        return theme;
    }

    private static void AssertBrush(IBrush? actual, string key, bool dark)
    {
        Assert.True(Application.Current!.TryGetResource(key, dark ? ThemeVariant.Dark : ThemeVariant.Light,
            out object? expected));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }
}
