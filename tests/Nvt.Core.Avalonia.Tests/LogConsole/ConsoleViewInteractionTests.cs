// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Exercises visible controls, real menu clicks, automation, and menu ownership.</summary>
public sealed class ConsoleViewInteractionTests
{
    /// <summary>The empty message follows projection replacements and its real reset button restores rows.</summary>
    [AvaloniaFact]
    public void EmptyStateShowsAndHidesWithProjectionAndResetClick()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var empty = (ConsoleEmptyState)surface.Children[2];
        var window = ConsoleTestView.Create(surface);
        try
        {
            Assert.True(empty.IsVisible);
            fixture.Store.Add(LogLevel.Info, "app", "visible");
            fixture.Fence();
            ConsoleTestView.Pump(window);
            Assert.False(empty.IsVisible);
            controller.SetSearchText("missing");
            ConsoleTestView.Pump(window);
            Assert.True(empty.IsVisible);
            var summary = empty.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text?.StartsWith("No matching", StringComparison.Ordinal) == true);
            Assert.Contains("missing", summary.Text, StringComparison.Ordinal);
            ConsoleTestView.Click(window, empty.GetVisualDescendants().OfType<Button>().Single());
            Assert.False(empty.IsVisible);
            Assert.False(controller.Projection.IsEmpty);
            fixture.Store.Clear();
            fixture.Fence();
            ConsoleTestView.Pump(window);
            Assert.True(empty.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Narrow controls route keyboard search, Only matches, Display dedupe, and More clear.</summary>
    [AvaloniaFact]
    public void NarrowSearchOnlyMatchesDisplayDedupeAndMoreClearUseVisibleControls()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "needle");
        fixture.Store.Add(LogLevel.Info, "app", "needle");
        fixture.Store.Add(LogLevel.Warn, "dxf", "other");
        fixture.Fence();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var header = (ConsoleHeader)surface.Children[0];
        var toolbar = (ConsoleToolbar)surface.Children[1];
        var window = ConsoleTestView.Create(surface, width: 640, height: 400);
        try
        {
            var search = toolbar.FindControl<TextBox>("SearchBox")!;
            Assert.True(search.IsEffectivelyVisible);
            Assert.False(header.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
            Assert.Equal("Search console", ControlAutomationPeer.CreatePeerForElement(search)!.GetName());
            Assert.True(search.Focus());
            window.KeyTextInput("needle");
            ConsoleTestView.Pump(window);
            Assert.Equal("needle", controller.Filter.SearchText);
            Assert.Equal(2, controller.Projection.RowCount);
            var matches = toolbar.FindControl<ToggleButton>("OnlyMatches")!;
            AssertToggle(matches, "Only matches", true);
            ConsoleTestView.Click(window, matches);
            AssertToggle(matches, "Only matches", false);
            Assert.False(controller.Filter.OnlyMatches);
            Assert.Equal(3, controller.Projection.RowCount);
            ConsoleTestView.Click(window, toolbar.FindControl<Button>("ClearSearch")!);
            Assert.Empty(controller.Filter.SearchText);
            var display = OpenMenu(window, header.FindControl<Button>("Display")!);
            var dedupe = display.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetName(item) == "Dedupe");
            Assert.False(dedupe.IsChecked);
            ConsoleTestView.ClickMenu(window, dedupe);
            Assert.True(controller.Filter.Deduplicate);
            display = OpenMenu(window, header.FindControl<Button>("Display")!);
            dedupe = display.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetName(item) == "Dedupe");
            Assert.True(dedupe.IsChecked);
            Assert.Equal("Dedupe ×1", dedupe.Header);
            Assert.Equal(2, controller.Projection.RowCount);
            display.Hide();
            var more = OpenMenu(window, header.FindControl<Button>("More")!);
            var clear = more.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Clear console"));
            Assert.Equal("Clear console", ControlAutomationPeer.CreatePeerForElement(clear)!.GetName());
            var generation = fixture.Store.Generation;
            ConsoleTestView.ClickMenu(window, clear);
            Assert.True(fixture.Store.Generation > generation);
            fixture.Fence();
            ConsoleTestView.Pump(window);
            Assert.Equal(0, controller.Projection.EventCount);
            Assert.True(((ConsoleEmptyState)surface.Children[2]).IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Every level and wide toggle exposes the same name and checked state that its click changes.</summary>
    [AvaloniaTheory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warn)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Fatal)]
    public void LevelAndFilterToggleAutomationNamesAndStatesFollowClicks(LogLevel level)
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(level, "app", "event");
        fixture.Fence();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var header = (ConsoleHeader)surface.Children[0];
        var toolbar = (ConsoleToolbar)surface.Children[1];
        var window = ConsoleTestView.Create(surface);
        try
        {
            var button = toolbar.FindControl<ToggleButton>("Level" + level)!;
            var name = level + " · 1";
            Assert.Equal(name, ToolTip.GetTip(button));
            AssertToggle(button, name, true);
            ConsoleTestView.Click(window, button);
            AssertToggle(button, name, false);
            Assert.DoesNotContain(level, controller.Filter.EnabledLevels);
            ConsoleTestView.Click(window, button);
            AssertToggle(button, name, true);
            Assert.Contains(level, controller.Filter.EnabledLevels);
            var matches = header.FindControl<ToggleButton>("OnlyMatches")!;
            AssertToggle(matches, "Only matches", true);
            ConsoleTestView.Click(window, matches);
            AssertToggle(matches, "Only matches", false);
            var dedupe = toolbar.FindControl<ToggleButton>("Dedupe")!;
            AssertToggle(dedupe, "Dedupe", false);
            ConsoleTestView.Click(window, dedupe);
            AssertToggle(dedupe, "Dedupe", true);
        }
        finally { window.Close(); }
    }

    /// <summary>Replacing a controller closes the popup, invalidates retained actions, and removes old ownership.</summary>
    [AvaloniaFact]
    public void ControllerReplacementHidesClearsSourceMenuAndReleasesOldController()
    {
        using var fixture = new ConsoleTestStore();
        using var replacement = fixture.Controller();
        var toolbar = new ConsoleToolbar();
        var window = ConsoleTestView.Create(toolbar, height: 400);
        try
        {
            var (weak, item, command) = ReplaceController(fixture, toolbar, replacement, window);
            Assert.Same(replacement, toolbar.Controller);
            AssertReleasedMenu(toolbar, item, command);
            var current = OpenMenu(window, toolbar.FindControl<Button>("Sources")!);
            ConsoleTestView.ClickMenu(window, current.Items.OfType<MenuItem>().ElementAt(1));
            Assert.Equal(["dxf", "idle"], replacement.Filter.SelectedSources.Order(StringComparer.Ordinal));
            current.Hide();
            Collect();
            Assert.False(weak.TryGetTarget(out _));
        }
        finally { window.Close(); }
    }

    /// <summary>Detaching closes the popup and invalidates actions before the host releases its controller.</summary>
    [AvaloniaFact]
    public void DetachHidesClearsSourceMenuAndReleasesOldController()
    {
        using var fixture = new ConsoleTestStore();
        var toolbar = new ConsoleToolbar();
        var window = ConsoleTestView.Create(toolbar, height: 400);
        try
        {
            var (weak, item, command) = DetachController(fixture, toolbar, window);
            AssertReleasedMenu(toolbar, item, command);
            Collect();
            Assert.False(weak.TryGetTarget(out _));
        }
        finally { window.Close(); }
    }

    /// <summary>A catalog with one source keeps that checkbox checked and disables its exclusion.</summary>
    [AvaloniaFact]
    public void SingleSourceMenuDisablesExcludingTheLastCheckedSource()
    {
        using var fixture = new ConsoleTestStore();
        using var registration = fixture.Controller();
        using var controller = new ConsoleController(fixture.Store, [new("app", "Application")]);
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 400);
        try
        {
            var menu = OpenMenu(window, toolbar.FindControl<Button>("Sources")!);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(2, items.Length);
            Assert.All(items, item => Assert.True(item.IsChecked));
            Assert.False(items[1].IsEffectivelyEnabled);
            Assert.False(items[1].Command!.CanExecute(null));
            items[1].Command!.Execute(null);
            Assert.Empty(controller.Filter.SelectedSources);
            Assert.True(items[1].IsChecked);
            menu.Hide();
        }
        finally { window.Close(); }
    }

    private static void AssertToggle(ToggleButton button, string name, bool isChecked)
    {
        Assert.Equal(isChecked, button.IsChecked);
        var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
        Assert.Equal(name, peer.GetName());
        Assert.True(peer.IsEnabled());
        var provider = Assert.IsAssignableFrom<IToggleProvider>(peer);
        Assert.Equal(isChecked ? ToggleState.On : ToggleState.Off, provider.ToggleState);
    }

    private static MenuFlyout OpenMenu(Window window, Button button)
    {
        ConsoleTestView.Pump(window);
        Assert.True(button.IsEffectivelyVisible);
        ConsoleTestView.Click(window, button);
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        Assert.True(menu.IsOpen);
        return menu;
    }

    private static void AssertReleasedMenu(ConsoleToolbar toolbar, MenuItem item, ICommand command)
    {
        var menu = Assert.IsType<MenuFlyout>(toolbar.FindControl<Button>("Sources")!.Flyout);
        Assert.False(menu.IsOpen);
        Assert.Empty(menu.Items);
        Assert.Null(item.Command);
        Assert.False(item.IsEnabled);
        Assert.False(command.CanExecute(null));
        command.Execute(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<ConsoleController>, MenuItem, ICommand) ReplaceController(
        ConsoleTestStore fixture, ConsoleToolbar toolbar, ConsoleController replacement, Window window)
    {
        using var previous = fixture.Controller();
        toolbar.Controller = previous;
        var menu = OpenMenu(window, toolbar.FindControl<Button>("Sources")!);
        var item = menu.Items.OfType<MenuItem>().ElementAt(1);
        var command = item.Command!;
        toolbar.Controller = replacement;
        AssertReleasedMenu(toolbar, item, command);
        Assert.Empty(previous.Filter.SelectedSources);
        Assert.Empty(replacement.Filter.SelectedSources);
        return (new WeakReference<ConsoleController>(previous), item, command);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<ConsoleController>, MenuItem, ICommand) DetachController(
        ConsoleTestStore fixture, ConsoleToolbar toolbar, Window window)
    {
        using var previous = fixture.Controller();
        toolbar.Controller = previous;
        var menu = OpenMenu(window, toolbar.FindControl<Button>("Sources")!);
        var item = menu.Items.OfType<MenuItem>().ElementAt(1);
        var command = item.Command!;
        window.Content = null;
        ConsoleTestView.Pump(window);
        AssertReleasedMenu(toolbar, item, command);
        Assert.Empty(previous.Filter.SelectedSources);
        // The host owns Controller; detach clears menu ownership, then the host releases its reference.
        toolbar.Controller = null;
        ConsoleTestView.Pump(window);
        return (new WeakReference<ConsoleController>(previous), item, command);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
