// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
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

    /// <summary>Opening the Display menu gives that button the active fill and leaves the neighbouring button idle.</summary>
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
            ConsoleTestView.Click(window, display);
            var menu = Assert.IsType<MenuFlyout>(display.Flyout);
            Assert.True(menu.IsOpen);
            Assert.Contains(":flyout-open", display.Classes);
            Assert.DoesNotContain(":flyout-open", more.Classes);
            Assert.False(more.IsPointerOver);
            Assert.False(display.IsPointerOver);
            menu.Hide();
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
            var focused = window.FocusManager?.GetFocusedElement();
            Assert.IsNotType<RepeatButton>(focused);
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
                Assert.True(global::Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost.ColorOf(body.Background).A == 0, $"{item.Header}: {string.Join(' ', item.Classes)} {body.Background}");
            }
            menu.Hide();
        }
        finally { window.Close(); }
    }
}
