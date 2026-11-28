// Copyright (c) 2026 Dennis Liu. All rights reserved.

using global::Avalonia.Controls.Documents;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;
using static Nvt.Core.Avalonia.Tests.Theme.SharedRoleHost;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Checks disclosure, rail and plain flyout roles on attached, rendered controls.</summary>
[Collection(nameof(RestFillIsolation))]
public sealed class SharedUiRolesTests
{
    private static readonly bool[] _both = [false, true];
    private static readonly double[] _widths = [320, 640];
    private static readonly double[] _scales = [1, 1.25, 1.5, 2];
    private static readonly string[] _tools = ["NFH", "NFU", "NFC"];
    private static readonly string[] _surfaces = ["NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush"];

    /// <summary>Exercises both themes, shapes and rest-fill modes independently.</summary>
    public static IEnumerable<object[]> Configurations()
    {
        foreach (bool dark in _both)
            foreach (bool square in _both)
                foreach (bool none in _both)
                    yield return [dark, square, none];
    }

    /// <summary>Leaves the header and content flush, with the existing six DIP gap in either direction.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Configurations))]
    public void FlatDisclosureHasNoContainerOrContentInset(bool dark, bool square, bool none)
    {
        Expander down = Disclosure(), up = Disclosure(up: true);
        Window host = Create(Column(down, up), dark, square, none);
        try
        {
            ListMenuTestHost.Show(host);
            Assert.All(new[] { down, up }, expander => AssertDisclosureGeometry(expander, square));
        }
        finally { host.Close(); }
    }

    private static void AssertDisclosureGeometry(Expander expander, bool square)
    {
        Border container = Part<Border>(expander, "ExpanderContainer");
        ToggleButton header = Part<ToggleButton>(expander, "ExpanderHeader");
        Border content = Part<Border>(expander, "ExpanderContent");
        ContentPresenter label = Part<ContentPresenter>(header, "ExpanderHeaderPresenter");
        Assert.Equal(new Thickness(0), container.Padding);
        Assert.Equal(new Thickness(0), container.BorderThickness);
        Assert.Equal(0, ColorOf(container.Background).A);
        Assert.Equal(32, header.Bounds.Height);
        Assert.Equal(new Thickness(12, 0), header.Padding);
        Assert.Equal(new CornerRadius(square ? 6 : 999), header.CornerRadius);
        Assert.Equal(new Thickness(12, 0), content.Padding);
        Assert.Equal(new Thickness(0), content.BorderThickness);
        Assert.Equal(12, label.TranslatePoint(default, expander)!.Value.X);
        Assert.Equal(12, ((Control)expander.Content!).TranslatePoint(default, expander)!.Value.X);
        Assert.Equal(6, expander.ExpandDirection == ExpandDirection.Down
            ? content.Bounds.Top - header.Bounds.Bottom : header.Bounds.Top - content.Bounds.Bottom);
        Assert.False(container.ClipToBounds);
        Assert.False(header.ClipToBounds);
        Assert.True(Part<Control>(header, "ExpanderChevron").TranslatePoint(default, header)!.Value.X > header.Bounds.Width / 2);
    }

    /// <summary>Uses transparent rest and shared overlays, with disabled taking priority over pointer states.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Configurations))]
    public void FlatDisclosureResolvesStateColors(bool dark, bool square, bool none)
    {
        Expander expander = Disclosure();
        Window host = Create(expander, dark, square, none);
        try
        {
            ListMenuTestHost.Show(host);
            ToggleButton header = Part<ToggleButton>(expander, "ExpanderHeader");
            Assert.All(States, state =>
            {
                expander.IsEnabled = !state.Disabled;
                ListMenuTestHost.SetState(header, state);
                ListMenuTestHost.Flush(host);
                string fill = state.Disabled ? "Nvt.Interaction.RestBrush"
                    : state.Pressed ? "Nvt.Interaction.PressedBrush"
                    : state.Hover ? "Nvt.Interaction.HoverBrush" : "Nvt.Interaction.RestBrush";
                string text = state.Disabled ? "NfcTextDisabledBrush"
                    : state.Pressed ? "Nvt.Controls.ExpanderPressedForegroundBrush" : "NfcTextBrush";
                Assert.Equal(ResourceColor(header, fill), ColorOf(header.Background));
                Assert.Equal(ResourceColor(header, text), ColorOf(header.Foreground));
                Assert.Equal(ColorOf(header.Foreground), ColorOf(Part<ContentPresenter>(header, "ExpanderHeaderPresenter").Foreground));
                Assert.Equal(state.Focus && !state.Disabled, Part<Border>(header, "ExpanderFocusRing").IsVisible);
                AssertTextContrast(header, header.Background, header.Foreground, state.Disabled);
            });
        }
        finally { host.Close(); }
    }

    /// <summary>Retains native keyboard expansion and disables both color and chevron motion on request.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlatDisclosureKeepsKeyboardFocusAndReducedMotion(bool up)
    {
        Expander expander = Disclosure(up);
        Window host = Create(expander, false, false, false);
        host.Classes.Remove("reducedMotion");
        try
        {
            ListMenuTestHost.Show(host, snapshot: false);
            ToggleButton header = Part<ToggleButton>(expander, "ExpanderHeader");
            Control chevron = Part<Control>(header, "ExpanderChevron");
            Assert.NotEmpty(header.Transitions!);
            Assert.NotEmpty(chevron.Transitions!);
            Assert.True(header.Focus(NavigationMethod.Tab));
            KeyStroke(host, Key.Space);
            Assert.False(expander.IsExpanded);
            KeyStroke(host, Key.Space);
            Assert.True(expander.IsExpanded);
            Assert.True(Part<Border>(header, "ExpanderFocusRing").IsVisible);
            expander.Classes.Add("reducedMotion");
            ListMenuTestHost.Flush(host);
            Assert.Null(header.Transitions);
            Assert.Null(chevron.Transitions);
            expander.Classes.Remove("reducedMotion");
            host.Classes.Add("reducedMotion");
            ListMenuTestHost.Flush(host);
            Assert.Null(header.Transitions);
            Assert.Null(chevron.Transitions);
        }
        finally { host.Close(); }
    }

    /// <summary>Changes only rail layout while keeping every flat and marked color state, including open flyouts.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Configurations))]
    public void RailKeepsFlatStateColorsAndMarkedFrame(bool dark, bool square, bool none)
    {
        Button[] rails = [Rail(), Rail(toggle: true), Rail(marked: true), Rail(toggle: true, marked: true)];
        Button[] peers = [Rail(), Rail(toggle: true), Rail(marked: true), Rail(toggle: true, marked: true)];
        Array.ForEach(peers, button => button.Classes.Remove("rail"));
        Window host = Create(Column([.. rails, .. peers]), dark, square, none);
        try
        {
            ListMenuTestHost.Show(host);
            Assert.All(States, state => AssertRailState(host, rails, peers, state));
        }
        finally { host.Close(); }
    }

    private static void AssertRailState(Window host, Button[] rails, Button[] peers, ItemState state)
    {
        Assert.All(_both, flyout =>
        {
            for (int index = 0; index < rails.Length; index++)
            {
                Button rail = rails[index], peer = peers[index];
                SetState(rail, state, flyout);
                SetState(peer, state, flyout);
                ListMenuTestHost.Flush(host);
                Color fill = index >= 2 && state.Selected && state.Pressed && !state.Disabled
                    ? ResourceColor(rail, "Nvt.Interaction.PressedFillBrush") : ColorOf(peer.Background);
                Assert.Equal(fill, ColorOf(rail.Background));
                Assert.Equal(ColorOf(peer.Foreground), ColorOf(rail.Foreground));
                Assert.Equal(ColorOf(peer.BorderBrush), ColorOf(rail.BorderBrush));
                Assert.Equal(new Size(56, 52), rail.Bounds.Size);
                Assert.Equal(new Thickness(index >= 2 ? 2 : 0), rail.BorderThickness);
                Assert.Equal(new CornerRadius(0), rail.CornerRadius);
                var panel = Assert.IsType<StackPanel>(rail.Content);
                var label = Assert.IsType<TextBlock>(panel.Children[1]);
                var icon = Assert.IsType<PathIcon>(panel.Children[0]);
                Assert.Equal(ColorOf(rail.Foreground), ColorOf(label.Foreground));
                Assert.Equal(ColorOf(rail.Foreground), ColorOf(icon.Foreground));
                Assert.Equal(11, label.FontSize);
                Assert.Equal(1, label.MaxLines);
                Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
                Assert.Equal(TextAlignment.Center, label.TextAlignment);
                Assert.Equal(28, icon.TranslatePoint(new Point(8, 0), rail)!.Value.X);
                AssertTextContrast(rail, rail.Background, rail.Foreground, state.Disabled);
            }
        });
    }

    /// <summary>Preserves an unclipped exterior ring at narrow window widths and fractional render scales.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Configurations))]
    public void RoleFocusSurvivesWindowScalingAndDpi(bool dark, bool square, bool none)
    {
        Button rail = Rail(marked: true, caption: "Long caption with ellipsis");
        Expander expander = Disclosure();
        var presenter = new FlyoutPresenter { Content = Column(rail, expander), HorizontalAlignment = HorizontalAlignment.Left };
        Window host = Create(presenter, dark, square, none);
        try
        {
            ListMenuTestHost.Show(host);
            Assert.All(_widths, width =>
                Assert.All(_scales, scale =>
                {
                    host.Width = width;
                    host.SetRenderScaling(scale);
                    Assert.True(rail.Focus(NavigationMethod.Tab));
                    ListMenuTestHost.Flush(host);
                    AdornerLayer layer = AdornerLayer.GetAdornerLayer(rail)!;
                    Border ring = Assert.Single(layer.Children.OfType<Border>(),
                        border => AdornerLayer.GetAdornedElement(border) == rail);
                    Assert.Equal(ResourceColor(rail, "Nvt.Focus.RingBrush"), ColorOf(ring.BorderBrush));
                    Assert.Equal(new Thickness(2), ring.BorderThickness);
                    Assert.False(AdornerLayer.GetIsClipEnabled(ring));
                    Assert.True(ring.IsVisible);
                    AssertRingInsideSurface(rail, presenter, new Thickness(-4));
                    ToggleButton header = Part<ToggleButton>(expander, "ExpanderHeader");
                    Assert.True(header.Focus(NavigationMethod.Tab));
                    ListMenuTestHost.Flush(host);
                    Border headerRing = Part<Border>(header, "ExpanderFocusRing");
                    Assert.True(headerRing.IsVisible);
                    Assert.Equal(new Thickness(-4), headerRing.Margin);
                    AssertRingInsideSurface(header, presenter, headerRing.Margin);
                    Assert.Equal(56, rail.Bounds.Width);
                    Assert.Equal(52, rail.Bounds.Height);
                    Assert.Equal(32, header.Bounds.Height);
                    using var frame = host.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.Equal((int)(width * scale), frame.PixelSize.Width);
                }));
        }
        finally { host.Close(); }
    }

    private static void AssertRingInsideSurface(Control control, FlyoutPresenter presenter, Thickness margin)
    {
        ScrollContentPresenter viewport = Assert.Single(presenter.GetVisualDescendants().OfType<ScrollContentPresenter>());
        // The scroll viewport clips, so the whole ring must be inside it.
        Point origin = control.TranslatePoint(default, viewport)!.Value;
        Assert.True(origin.X + margin.Left >= 0 && origin.Y + margin.Top >= 0);
        Assert.True(origin.X + control.Bounds.Width - margin.Right <= viewport.Bounds.Width);
        Assert.True(origin.Y + control.Bounds.Height - margin.Bottom <= viewport.Bounds.Height);
    }

    /// <summary>Retains Fluent's scroll settings and size limits while adopting Core surface and typography.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Configurations))]
    public void FlyoutUsesCoreSurfaceAndKeepsFluentLayout(bool dark, bool square, bool none)
    {
        var label = new TextBlock { Text = "Panel settings" };
        var presenter = new FlyoutPresenter { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        Window host = ListMenuTestHost.Create(presenter, dark, core: false);
        try
        {
            ListMenuTestHost.Show(host);
            Assert.Equal((96d, 456d, 40d, 758d), (presenter.MinWidth, presenter.MaxWidth, presenter.MinHeight, presenter.MaxHeight));
            ListMenuTestHost.Include(host, "MenuStyles");
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            ThemeRestFills.SetRestFill(host.Resources, none ? ThemeRestFill.None : ThemeRestFill.Soft);
            ListMenuTestHost.Flush(host);
            Border surface = presenter.GetVisualDescendants().OfType<Border>().First();
            Assert.Equal(ResourceColor(presenter, "NfcSurfaceBrush"), ColorOf(surface.Background));
            Assert.Equal(ResourceColor(presenter, "NfcBorderBrush"), ColorOf(surface.BorderBrush));
            Assert.Equal(new Thickness(1), surface.BorderThickness);
            Assert.Equal(new CornerRadius(square ? 6 : 8), surface.CornerRadius);
            Assert.Equal(new Thickness(12), presenter.Padding);
            // 1 DIP border plus 12 DIP padding.
            Assert.Equal(new Point(13, 13), label.TranslatePoint(default, presenter));
            Assert.Equal(ResourceColor(presenter, "NfcTextBrush"), ColorOf(label.Foreground));
            Assert.Equal(13, label.FontSize);
            Assert.True(presenter.TryFindResource("NfcUiFontFamily", presenter.ActualThemeVariant, out object? font));
            Assert.Equal(font, label.FontFamily);
            Assert.Equal((96d, 456d, 40d, 758d), (presenter.MinWidth, presenter.MaxWidth, presenter.MinHeight, presenter.MaxHeight));
            AssertTextContrast(presenter, surface.Background, label.Foreground, false);
        }
        finally { host.Close(); }
    }

    /// <summary>Scrolls to both ends of oversized content without adding any focusable presenter or scrollbar.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlyoutLongContentRemainsReachableWithoutExtraTabStops(bool dark)
    {
        var first = new Button { Content = "First", Classes = { "actionNeutral" }, HorizontalAlignment = HorizontalAlignment.Left };
        var last = new Button { Content = "Last", Classes = { "actionNeutral" }, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = Column(first, new Border { Width = 700, Height = 1100 }, last);
        var presenter = new FlyoutPresenter { Content = panel, HorizontalAlignment = HorizontalAlignment.Left };
        Window host = Create(presenter, dark, false, false);
        try
        {
            ListMenuTestHost.Show(host);
            ScrollViewer scroll = Assert.Single(presenter.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.False(presenter.Focusable);
            Assert.False(scroll.Focusable);
            Assert.All(scroll.GetVisualDescendants().OfType<ScrollBar>(), bar => Assert.False(bar.Focusable));
            Assert.True(first.Focus(NavigationMethod.Tab));
            KeyStroke(host, Key.Tab);
            Assert.True(last.IsFocused);
            ListMenuTestHost.Flush(host);
            Assert.True(scroll.Offset.Y > 0);
            AssertRingInsideSurface(last, presenter, default); // The button ring is an unclipped adorner.
            scroll.Offset = new Vector(scroll.Extent.Width, scroll.Extent.Height);
            ListMenuTestHost.Flush(host);
            Assert.True(scroll.Offset.X > 0);
            Assert.True(first.Focus(NavigationMethod.Tab));
            ListMenuTestHost.Flush(host);
            AssertRingInsideSurface(first, presenter, default);
        }
        finally { host.Close(); }
    }

    /// <summary>Uses only application accent overrides; attached marked and unmarked rails follow later replacements.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RailsFollowSevenApplicationAccentOverrides(bool dark)
    {
        using var scope = new AccentSelectionScope("NFH", dark);
        Button marked = Rail(marked: true), plain = Rail(toggle: true);
        Window host = scope.Show(Surface(Column(marked, plain)));
        Assert.All(_tools, tool =>
        {
            foreach (var entry in AccentSelectionScope.Palette(tool, dark))
                Application.Current!.Resources[entry.Key] = new SolidColorBrush(entry.Value);
            Assert.All(States.Where(state => state.Selected), state =>
            {
                SetState(marked, state);
                SetState(plain, state);
                ListMenuTestHost.Flush(host);
                Assert.Equal(ResourceColor(marked, state.Disabled ? "NfcTextDisabledBrush" : "NfcAccentBrush"), ColorOf(marked.BorderBrush));
                Assert.Equal(ResourceColor(marked, state.Disabled ? "NfcTextDisabledBrush" : "NfcAccentStrongBrush"), ColorOf(marked.Foreground));
                Assert.Equal(ColorOf(marked.Foreground), ColorOf(plain.Foreground));
                AssertTextContrast(marked, marked.Background, marked.Foreground, state.Disabled);
                AssertTextContrast(plain, plain.Background, plain.Foreground, state.Disabled);
            });
        });
    }

    /// <summary>Checks style lookup through a real plain flyout's popup rather than only an inline presenter.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlainFlyoutResolvesCoreSurfaceThroughPopup(bool dark)
    {
        var label = new TextBlock { Text = "Panel settings" };
        Button item = Rail(marked: true);
        item.Classes.Add("active");
        var banner = new TextBlock { Text = "Colored container" };
        var container = new Border { Child = banner };
        TextElement.SetForeground(container, Brushes.Magenta);
        var selectable = new SelectableTextBlock { Text = "Selectable text" };
        var selectableContainer = new Border { Child = selectable };
        TextElement.SetForeground(selectableContainer, Brushes.Magenta);
        var unavailable = new Button { Content = "Unavailable", IsEnabled = false };
        var flyout = new Flyout { Content = Column(label, item, container, selectableContainer, unavailable) };
        var owner = new Button { Content = "Open panel", Classes = { "actionGhost", "flat", "rail" }, Flyout = flyout };
        Window host = Create(owner, dark, false, false);
        try
        {
            ListMenuTestHost.Show(host);
            flyout.ShowAt(owner);
            ListMenuTestHost.Flush(host);
            Assert.True(flyout.IsOpen);
            FlyoutPresenter presenter = Assert.Single(label.GetVisualAncestors().OfType<FlyoutPresenter>());
            Assert.Equal(ResourceColor(presenter, "NfcSurfaceBrush"), ColorOf(presenter.Background));
            Assert.Equal(ResourceColor(presenter, "NfcTextBrush"), ColorOf(label.Foreground));
            // Text inside a role button's flyout keeps the color of its own container.
            Assert.Equal(Colors.Magenta, ColorOf(banner.Foreground));
            Assert.Equal(Colors.Magenta, ColorOf(selectable.Foreground));
            // A button without a role keeps the disabled label color of its own template.
            ContentPresenter disabledContent = unavailable.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(ColorOf(disabledContent.Foreground), ColorOf(unavailable.GetVisualDescendants().OfType<TextBlock>().First().Foreground));
            Assert.Equal(ResourceColor(item, "NfcAccentStrongBrush"), ColorOf(Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(item.Content).Children[1]).Foreground));
            Assert.Equal(new Thickness(12), presenter.Padding);
            Assert.Equal(13, label.FontSize);
            AssertTextContrast(presenter, presenter.Background, label.Foreground, false);
            Assert.Contains(":flyout-open", owner.Classes);
            flyout.Hide();
            Assert.False(flyout.IsOpen);
        }
        finally { flyout.Hide(); host.Close(); }
    }

    private static void AssertTextContrast(Control owner, IBrush? fill, IBrush? text, bool disabled)
    {
        Assert.All(_surfaces, key =>
        {
            Color background = ShownColor.Over(fill, ResourceColor(owner, key));
            double ratio = ShownColor.Contrast(ShownColor.Over(text, background), background);
            double minimum = disabled && owner.ActualThemeVariant != global::Avalonia.Styling.ThemeVariant.Dark ? 3 : 4.5;
            Assert.True(ratio >= minimum, $"{owner.GetType().Name}/{owner.ActualThemeVariant}/{key}: {ratio:F6}:1");
        });
    }
}
