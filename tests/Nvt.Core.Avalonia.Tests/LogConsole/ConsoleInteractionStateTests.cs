// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Tests.Choice;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Covers the open-flyout state, the menu height limit and the keyboard landing of the console menus.</summary>
public sealed class ConsoleInteractionStateTests
{
    private const int SourceCount = 16;

    private static ConsoleTestStore StoreWithSources()
    {
        var store = new ConsoleTestStore(200);
        for (int index = 0; index < SourceCount; index++)
            store.Store.Add(LogLevel.Info, "source-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "event " + index);
        store.Fence();
        return store;
    }

    private static void Settle(Window window) => ConsoleTestView.Pump(window);

    /// <summary>The fill the styles resolve to. A background fade animates above it, so this is the end of the fade.</summary>
    private static Color Target(TemplatedControl control) => ChoiceTestHost.ColorOf(control.GetBaseValue(TemplatedControl.BackgroundProperty).Value);

    /// <summary>The colour the renderer paints just inside the left edge of a control, where the body fill shows and no content draws.</summary>
    private static Color Painted(Window window, Control control)
    {
        Settle(window);
        var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(window);
        var bytes = new byte[size.Width * size.Height * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { pinned.Free(); }
        Point at = control.TranslatePoint(new Point(3, control.Bounds.Height / 2), window)!.Value;
        int offset = ((int)at.Y * size.Width + (int)at.X) * 4;
        return Color.FromArgb(bytes[offset + 3], bytes[offset + 2], bytes[offset + 1], bytes[offset]);
    }

    private static Point Center(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>Opening the Display menu keeps the active fill on that button after the pointer leaves it, and the neighbouring button follows its own state.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenFlyoutMarksOnlyItsOwnButtonAsActive(bool dark)
    {
        using var fixture = StoreWithSources();
        using var controller = fixture.Controller();
        var header = new ConsoleHeader { Controller = controller };
        Window window = ConsoleTestView.Create(new StackPanel { Children = { header } }, dark: dark, height: 400);
        try
        {
            var display = header.FindControl<Button>("Display")!;
            var more = header.FindControl<Button>("More")!;
            // Reduced motion removes the background fade, so the painted pixel is the fill itself and not a frame of the fade.
            display.Classes.Add("reducedMotion");
            more.Classes.Add("reducedMotion");
            Color active = ChoiceTestHost.ResourceColor(display, "NfcSelectionSurfaceBrush");
            window.MouseMove(new Point(2, 2));
            Color displayRest = Painted(window, display);
            Color moreRest = Painted(window, more);
            Assert.NotEqual(active, displayRest);
            Assert.NotEqual(active, moreRest);

            // The neighbouring button paints the hover fill while the pointer is over it and the menu is closed.
            window.MouseMove(Center(window, more));
            Settle(window);
            Assert.Contains(":pointerover", more.Classes);
            Assert.Equal(active, Painted(window, more));
            Assert.Equal(displayRest, Painted(window, display));

            // Opening the menu moves the pointer to the flyout: Display is no longer hovered but keeps the one active fill.
            window.MouseMove(Center(window, display));
            ConsoleTestView.Click(window, display);
            var menu = Assert.IsType<MenuFlyout>(display.Flyout);
            Assert.True(menu.IsOpen);
            Settle(window);
            Assert.Contains(":flyout-open", display.Classes);
            Assert.DoesNotContain(":flyout-open", more.Classes);
            Assert.DoesNotContain(":pointerover", display.Classes);
            Assert.Equal(active, Painted(window, display));
            Assert.Equal(moreRest, Painted(window, more));

            // The open flyout takes the pointer, so moving over the neighbouring button neither hovers it nor changes Display.
            window.MouseMove(Center(window, more));
            Settle(window);
            Assert.DoesNotContain(":pointerover", more.Classes);
            Assert.Equal(active, Painted(window, display));
            Assert.Equal(moreRest, Painted(window, more));
            menu.Hide();
            Settle(window);
            Assert.Equal(displayRest, Painted(window, display));
        }
        finally { window.Close(); }
    }

    /// <summary>A menu with many items stays within the popup height token and scrolls the rest.</summary>
    [AvaloniaFact]
    public void SourcesMenuIsLimitedToThePopupHeightToken()
    {
        using var fixture = StoreWithSources();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        Window window = ConsoleTestView.Create(toolbar, dark: true, height: 900);
        try
        {
            var sources = toolbar.FindControl<Button>("Sources")!;
            ConsoleTestView.Click(window, sources);
            var menu = Assert.IsType<MenuFlyout>(sources.Flyout);
            var first = menu.Items.Cast<MenuItem>().First();
            var presenter = Assert.IsType<MenuFlyoutPresenter>(first.FindAncestorOfType<MenuFlyoutPresenter>());
            double limit = (double)window.FindResource("Nvt.Menu.PopupMaxHeight")!;
            Assert.True(menu.Items.Count > SourceCount);
            Assert.True(presenter.Bounds.Height <= limit, $"presenter {presenter.Bounds.Height} > {limit}");
            var scroll = Assert.Single(presenter.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            menu.Hide();
        }
        finally { window.Close(); }
    }

    /// <summary>Opening the menu with the keyboard moves focus into the menu, not onto a scroll arrow.</summary>
    [AvaloniaFact]
    public void KeyboardOpenDoesNotFocusAScrollArrow()
    {
        using var fixture = StoreWithSources();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        Window window = ConsoleTestView.Create(toolbar, dark: true, height: 900);
        try
        {
            var sources = toolbar.FindControl<Button>("Sources")!;
            sources.Focus(NavigationMethod.Tab);
            ConsoleTestView.Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            ConsoleTestView.Pump(window);
            var menu = Assert.IsType<MenuFlyout>(sources.Flyout);
            Assert.True(menu.IsOpen);
            var focused = Assert.IsAssignableFrom<MenuItem>(window.FocusManager?.GetFocusedElement());
            Assert.True(focused.IsEnabled);
            Assert.Contains(focused, menu.Items.Cast<MenuItem>());
            menu.Hide();
        }
        finally { window.Close(); }
    }

    /// <summary>Opening the menu with the mouse shows no keyboard focus ring on any item.</summary>
    [AvaloniaFact]
    public void MouseOpenShowsNoFocusRing()
    {
        using var fixture = StoreWithSources();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        Window window = ConsoleTestView.Create(toolbar, dark: true, height: 900);
        try
        {
            var sources = toolbar.FindControl<Button>("Sources")!;
            ConsoleTestView.Click(window, sources);
            var menu = Assert.IsType<MenuFlyout>(sources.Flyout);
            var items = menu.Items.Cast<MenuItem>().ToArray();
            window.MouseMove(items[1].TranslatePoint(new Point(10, 10), window)!.Value);
            ConsoleTestView.Pump(window);
            foreach (MenuItem item in items)
            {
                Border ring = Assert.Single(item.GetVisualDescendants().OfType<Border>(), part => part.Name == "MenuFocusRing");
                Assert.False(ring.IsVisible, $"{item.Header} shows a focus ring");
            }
            menu.Hide();
        }
        finally { window.Close(); }
    }

    /// <summary>Checked source rows show only the check mark. They do not carry a fill.</summary>
    [AvaloniaFact]
    public void CheckedSourceRowsCarryNoFill()
    {
        using var fixture = StoreWithSources();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        Window window = ConsoleTestView.Create(toolbar, dark: true, height: 900);
        try
        {
            var sources = toolbar.FindControl<Button>("Sources")!;
            ConsoleTestView.Click(window, sources);
            var menu = Assert.IsType<MenuFlyout>(sources.Flyout);
            window.MouseMove(new Point(2, 2));
            ConsoleTestView.Pump(window);
            foreach (MenuItem item in menu.Items.Cast<MenuItem>())
            {
                Assert.True(item.IsChecked);
                Border body = Assert.Single(item.GetVisualDescendants().OfType<Border>(), part => part.Name == "PART_LayoutRoot");
                Assert.True(ChoiceTestHost.ColorOf(body.Background).A == 0, $"{item.Header}: {string.Join(' ', item.Classes)} {body.Background}");
            }
            menu.Hide();
        }
        finally { window.Close(); }
    }

    /// <summary>The checked Only matches button rests on the zero-alpha hover colour, so its fade never passes through white.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedOnlyMatchesRestsOnTheZeroAlphaHoverColour(bool dark)
    {
        using var fixture = StoreWithSources();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        Window window = ConsoleTestView.Create(toolbar, dark: dark, height: 400);
        try
        {
            var flag = toolbar.FindControl<ToggleButton>("OnlyMatches")!;
            flag.IsChecked = true;
            Settle(window);
            Color rest = Target(flag);
            Color hover = ChoiceTestHost.ResourceColor(flag, "NfcSelectionSurfaceBrush");
            Assert.Equal(0, rest.A);
            Assert.Equal((hover.R, hover.G, hover.B), (rest.R, rest.G, rest.B));
            window.MouseMove(flag.TranslatePoint(new Point(flag.Bounds.Width / 2, flag.Bounds.Height / 2), window)!.Value);
            Settle(window);
            Assert.Equal(hover, Target(flag));
        }
        finally { window.Close(); }
    }
}
