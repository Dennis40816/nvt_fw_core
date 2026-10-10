// Copyright (c) 2026 Dennis Liu. All rights reserved.


using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks host resources and active input routing through real controls.</summary>
public sealed class ConsoleResourceTests
{
    /// <summary>One resource change moves both search rows together without duplication.</summary>
    [AvaloniaFact]
    public void LayoutHostChangesBreakpointMovesBothSearchRowsTogether()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var header = (ConsoleHeader)surface.Children[0];
        var toolbar = (ConsoleToolbar)surface.Children[1];
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            Assert.True(header.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
            Assert.False(toolbar.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
            window.Resources["Nvt.Console.NarrowBreakpoint"] = 1300d;
            ConsoleTestView.Pump(window);
            Assert.False(header.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
            Assert.True(toolbar.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
            window.Resources["Nvt.Console.NarrowBreakpoint"] = 1000d;
            ConsoleTestView.Pump(window);
            Assert.True(header.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
            Assert.False(toolbar.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Host strings replace both responsive rows and empty surface copy.</summary>
    [AvaloniaFact]
    public void ResourcesHostOverridesKeysUpdatesHeaderToolbarAndEmptyState()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var header = (ConsoleHeader)surface.Children[0];
        var toolbar = (ConsoleToolbar)surface.Children[1];
        var empty = (ConsoleEmptyState)surface.Children[2];
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            window.Resources["Nvt.Console.OnlyMatches"] = "只顯示符合項目";
            window.Resources["Nvt.Console.Empty.NoEvents"] = "尚無事件";
            window.Resources["Nvt.Console.Level.Info"] = "資訊";
            window.Resources["Nvt.Console.Sources.All"] = "所有來源";
            ConsoleTestView.Pump(window);
            Assert.Equal("只顯示符合項目", ((TextBlock)header.FindControl<ToggleButton>("OnlyMatches")!.Content!).Text);
            Assert.Equal("只顯示符合項目", ((TextBlock)toolbar.FindControl<ToggleButton>("OnlyMatches")!.Content!).Text);
            Assert.Equal("尚無事件", empty.FindControl<TextBlock>("EmptyMessage")!.Text);
            Assert.Equal("資訊 · 0", AutomationProperties.GetName(toolbar.FindControl<ToggleButton>("LevelInfo")!));
            Assert.Equal("所有來源", toolbar.FindControl<TextBlock>("SourceLabel")!.Text);
        }
        finally { window.Close(); }
    }

    /// <summary>A height token change resizes both outer rows including the narrow second row.</summary>
    [AvaloniaFact]
    public void LayoutControlHeightChangesResizesHeaderAndToolbar()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var header = (ConsoleHeader)surface.Children[0];
        var toolbar = (ConsoleToolbar)surface.Children[1];
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            window.Resources["NfcControlHeight"] = 40d;
            ConsoleTestView.Pump(window);
            Assert.Equal(56, header.Bounds.Height);
            Assert.Equal(56, toolbar.Bounds.Height);
            window.Width = 640;
            ConsoleTestView.Pump(window);
            Assert.Equal(104, toolbar.Bounds.Height);
        }
        finally { window.Close(); }
    }

    /// <summary>Time modes map through resource labels rather than enum identifiers.</summary>
    [AvaloniaTheory]
    [InlineData(ConsoleTimeMode.Absolute, "Absolute time")]
    [InlineData(ConsoleTimeMode.Relative, "Relative time")]
    [InlineData(ConsoleTimeMode.Hidden, "Hidden time")]
    public void TimeButtonSelectedModeShowsResourceLabel(ConsoleTimeMode mode, string expected)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var header = (ConsoleHeader)surface.Children[0];
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            controller.SetTimeModeCommand.Execute(mode);
            ConsoleTestView.Pump(window);
            Assert.Equal(expected, header.FindControl<TextBlock>("TimeLabel")!.Text);
            Assert.NotEqual(mode.ToString(), header.FindControl<TextBlock>("TimeLabel")!.Text);
        }
        finally { window.Close(); }
    }

    /// <summary>An empty store has its own copy and no reset action.</summary>
    [AvaloniaFact]
    public void EmptyStateNoStoredEventsShowsNoEventsAndHidesReset()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var empty = (ConsoleEmptyState)surface.Children[2];
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            Assert.Equal("No events yet", empty.FindControl<TextBlock>("EmptyMessage")!.Text);
            Assert.False(empty.FindControl<Button>("ResetFilters")!.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>A filtered empty result explains its filter and offers reset.</summary>
    [AvaloniaFact]
    public void EmptyStateFilteredEventsShowsSummaryAndReset()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var empty = (ConsoleEmptyState)surface.Children[2];
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            fixture.Store.Add(LogLevel.Info, "app", "visible");
            fixture.Fence();
            controller.SetSearchText("missing");
            ConsoleTestView.Pump(window);
            Assert.Contains("missing", empty.FindControl<TextBlock>("EmptyMessage")!.Text, StringComparison.Ordinal);
            Assert.True(empty.FindControl<Button>("ResetFilters")!.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Typing into a still attached view after disposal never invokes the controller.</summary>
    [AvaloniaTheory]
    [InlineData(1200, 0)]
    [InlineData(640, 1)]
    public void SearchTextControllerDisposedIgnoresInputAndDisablesClear(int width, int row)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface, width: width, height: 300);
        try
        {
            var view = (UserControl)surface.Children[row];
            var search = view.FindControl<TextBox>("SearchBox")!;
            var clear = view.FindControl<Button>("ClearSearch")!;
            Assert.True(clear.IsEffectivelyEnabled);
            controller.Dispose();
            Assert.True(search.Focus());
            window.KeyTextInput("after disposal");
            Assert.Null(Record.Exception(() => ConsoleTestView.Pump(window)));
            Assert.Empty(controller.Filter.SearchText);
            Assert.False(clear.IsEffectivelyEnabled);
            Assert.False(clear.Command!.CanExecute(null));
            Assert.Null(Record.Exception(() => clear.Command!.Execute(null)));
        }
        finally { window.Close(); }
    }

    /// <summary>Popup labels follow host templates while the source menu stays open.</summary>
    [AvaloniaFact]
    public void SourcesHostOverridesTemplatesUpdatesOpenMenu()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 300);
        try
        {
            var button = toolbar.FindControl<Button>("Sources")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            menu.ShowAt(button);
            ConsoleTestView.Pump(window);
            Assert.Equal("All sources · 0", ((MenuItem)menu.Items[0]!).Header);
            window.Resources["Nvt.Console.Sources.All"] = "所有來源";
            window.Resources["Nvt.Console.Count"] = "{0}：{1}";
            ConsoleTestView.Pump(window);
            Assert.Equal("所有來源：0", ((MenuItem)menu.Items[0]!).Header);
            Assert.Equal("Application：0", ((MenuItem)menu.Items[1]!).Header);
            Assert.True(menu.IsOpen);
            menu.Hide();
        }
        finally { window.Close(); }
    }

    /// <summary>One minimum width resource applies to each console surface.</summary>
    [AvaloniaFact]
    public void LayoutHostChangesMinimumWidthUpdatesAllSurfaces()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            window.Resources["Nvt.Console.MinimumWidth"] = 700d;
            ConsoleTestView.Pump(window);
            Assert.Equal(700, surface.Children[0].MinWidth);
            Assert.Equal(700, surface.Children[1].MinWidth);
            Assert.Equal(700, surface.Children[2].MinWidth);
        }
        finally { window.Close(); }
    }

}
