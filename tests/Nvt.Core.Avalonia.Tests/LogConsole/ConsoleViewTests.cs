// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Verifies console state and view contracts.</summary>
public sealed class ConsoleViewTests
{
    /// <summary>Header Toolbar And Empty State Render For Themes Shapes And Layouts.</summary>
    [AvaloniaTheory]
    [InlineData(false, ThemeShape.Pill, 1200)]
    [InlineData(true, ThemeShape.Pill, 1200)]
    [InlineData(false, ThemeShape.Square, 1200)]
    [InlineData(true, ThemeShape.Square, 1200)]
    [InlineData(false, ThemeShape.Pill, 640)]
    [InlineData(true, ThemeShape.Pill, 640)]
    [InlineData(false, ThemeShape.Square, 640)]
    [InlineData(true, ThemeShape.Square, 640)]
    public void HeaderToolbarAndEmptyStateRenderForThemesShapesAndLayouts(bool dark, ThemeShape shape, int width)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface, dark, shape, width, width <= 960 ? 184 : 144);
        try
        {
            var header = Assert.IsType<ConsoleHeader>(surface.Children[0]);
            var toolbar = Assert.IsType<ConsoleToolbar>(surface.Children[1]);
            Assert.True(((ConsoleEmptyState)surface.Children[2]).IsVisible);
            Assert.Equal(48, header.Bounds.Height);
            Assert.Equal(width <= 960 ? 88 : 48, toolbar.Bounds.Height);
            Assert.Equal(width <= 960, toolbar.FindControl<Grid>("NarrowSearchRow")!.IsVisible);
            Assert.Equal(width > 960, header.FindControl<Grid>("WideSearch")!.IsVisible);
            foreach (var button in surface.GetVisualDescendants().OfType<Button>())
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), button.Name);
                Assert.NotNull(ControlAutomationPeer.CreatePeerForElement(button));
            }
            using var frame = new RenderTargetBitmap(new PixelSize(width, width <= 960 ? 184 : 144), new Vector(96, 96));
            frame.Render(window);
            var evidence = Environment.GetEnvironmentVariable("NVT_CONSOLE_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                frame.Save(Path.Combine(evidence, $"{(dark ? "dark" : "light")}-{shape.ToString().ToLowerInvariant()}-{width}-header-toolbar-empty.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    /// <summary>Breakpoint Moves Search Display Export And Clear.</summary>
    [AvaloniaTheory]
    [InlineData(960, true)]
    [InlineData(961, false)]
    [InlineData(840, true)]
    public void BreakpointMovesSearchDisplayExportAndClear(double width, bool narrow)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface, width: width, height: 200);
        try
        {
            var header = (ConsoleHeader)surface.Children[0];
            var toolbar = (ConsoleToolbar)surface.Children[1];
            Assert.Equal(narrow, header.FindControl<StackPanel>("NarrowActions")!.IsVisible);
            Assert.Equal(!narrow, header.FindControl<StackPanel>("WideActions")!.IsVisible);
            Assert.Equal(!narrow, toolbar.FindControl<ToggleButton>("Dedupe")!.IsVisible);
            Assert.Equal(narrow, toolbar.FindControl<Grid>("NarrowSearchRow")!.IsVisible);
            window.Width = narrow ? 1200 : 640;
            ConsoleTestView.Pump(window);
            Assert.Equal(narrow ? 48 : 88, toolbar.Bounds.Height);
        }
        finally { window.Close(); }
    }

    /// <summary>Level Tooltip Uses Raw Count And Toggle Updates Controller.</summary>
    [AvaloniaFact]
    public void LevelTooltipUsesRawCountAndToggleUpdatesController()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Error, "app", "failure");
        fixture.Fence();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 48);
        try
        {
            var error = toolbar.FindControl<ToggleButton>("LevelError")!;
            Assert.Equal("Error · 1", ToolTip.GetTip(error));
            Assert.Equal("Error · 1", AutomationProperties.GetName(error));
            Assert.True(error.IsChecked);
            ConsoleTestView.Click(window, error);
            ConsoleTestView.Pump(window);
            Assert.DoesNotContain(LogLevel.Error, controller.Filter.EnabledLevels);
            Assert.False(error.IsChecked);
            Assert.True(controller.Projection.IsEmpty);
            Assert.Equal("Error · 1", ToolTip.GetTip(error));
            var fatal = toolbar.FindControl<ToggleButton>("LevelFatal")!;
            Assert.Equal("Fatal · 0", ToolTip.GetTip(fatal));
            Assert.True(fatal.IsEnabled);
        }
        finally { window.Close(); }
    }

    /// <summary>Search Clear Only Matches Dedupe And Reset Share Controller State.</summary>
    [AvaloniaFact]
    public void SearchClearOnlyMatchesDedupeAndResetShareControllerState()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface);
        try
        {
            var header = (ConsoleHeader)surface.Children[0];
            var toolbar = (ConsoleToolbar)surface.Children[1];
            var search = header.FindControl<TextBox>("SearchBox")!;
            Assert.NotEmpty(search.GetVisualDescendants());
            search.Focus();
            window.KeyTextInput("needle");
            ConsoleTestView.Pump(window);
            Assert.Equal("needle", controller.Filter.SearchText);
            search.CaretIndex = 3;
            window.KeyTextInput("X");
            ConsoleTestView.Pump(window);
            Assert.Equal("neeXdle", controller.Filter.SearchText);
            search.SelectAll();
            window.KeyTextInput("needle");
            ConsoleTestView.Pump(window);
            Assert.Equal("needle", controller.Filter.SearchText);
            ConsoleTestView.Click(window, header.FindControl<ToggleButton>("OnlyMatches")!);
            ConsoleTestView.Click(window, toolbar.FindControl<ToggleButton>("Dedupe")!);
            Assert.False(controller.Filter.OnlyMatches);
            Assert.True(controller.Filter.Deduplicate);
            ConsoleTestView.Click(window, header.FindControl<Button>("ClearSearch")!);
            ConsoleTestView.Pump(window);
            Assert.Empty(controller.Filter.SearchText);
            var reset = surface.Children[2].GetVisualDescendants().OfType<Button>().Single();
            ConsoleTestView.Click(window, reset);
            ConsoleTestView.Pump(window);
            Assert.True(controller.Filter.OnlyMatches);
            Assert.False(controller.Filter.Deduplicate);
        }
        finally { window.Close(); }
    }

    /// <summary>Source Menu Includes Injected Zero Counts And Supports Multiple Selection.</summary>
    [AvaloniaFact]
    public void SourceMenuIncludesInjectedZeroCountsAndSupportsMultipleSelection()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "event");
        fixture.Store.Add(LogLevel.Warn, "dxf", "event");
        fixture.Fence();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 400);
        try
        {
            var button = toolbar.FindControl<Button>("Sources")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            menu.ShowAt(button);
            ConsoleTestView.Pump(window);
            var items = menu.Items.Cast<MenuItem>().ToArray();
            Assert.Collection(items,
                item => Assert.Equal("All sources · 2", item.Header),
                item => Assert.Equal("Application · 1", item.Header),
                item => Assert.Equal("Drawing · 1", item.Header),
                item => Assert.Equal("Idle · 0", item.Header));
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(item))));
            Assert.All(items, item => Assert.True(item.IsChecked));
            ConsoleTestView.ClickMenu(window, items[1]);
            Assert.False(items[1].IsChecked);
            Assert.True(items[2].IsChecked);
            Assert.True(items[3].IsChecked);
            Assert.False(items[0].IsChecked);
            Assert.Equal(2, controller.Filter.SelectedSources.Count);
            Assert.Equal("dxf", Assert.Single(controller.Projection.Rows).SourceId);
            menu.ShowAt(button);
            ConsoleTestView.Pump(window);
            items = menu.Items.Cast<MenuItem>().ToArray();
            ConsoleTestView.ClickMenu(window, items[2]);
            Assert.Equal(["idle"], controller.Filter.SelectedSources);
            Assert.True(controller.Projection.IsEmpty);
            Assert.False(items[2].IsChecked);
            Assert.True(items[3].IsChecked);
            Assert.False(items[3].IsEffectivelyEnabled);
            menu.ShowAt(button);
            ConsoleTestView.Pump(window);
            items = menu.Items.Cast<MenuItem>().ToArray();
            ConsoleTestView.ClickMenu(window, items[2]);
            Assert.Equal(2, controller.Filter.SelectedSources.Count);
            Assert.Equal("dxf", Assert.Single(controller.Projection.Rows).SourceId);
            menu.ShowAt(button);
            ConsoleTestView.Pump(window);
            items = menu.Items.Cast<MenuItem>().ToArray();
            ConsoleTestView.ClickMenu(window, items[0]);
            Assert.All(items, item => Assert.True(item.IsChecked));
            Assert.Empty(controller.Filter.SelectedSources);
            Assert.Equal(2, controller.Projection.RowCount);
            menu.Hide();
        }
        finally { window.Close(); }
    }

    /// <summary>Export Bindings And Include Flags Work In Wide And More Menus.</summary>
    [AvaloniaFact]
    public void ExportBindingsAndIncludeFlagsWorkInWideAndMoreMenus()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var calls = 0;
        var header = new ConsoleHeader { Controller = controller, CopySelectedCommand = new RelayCommand(() => calls++),
            CopyVisibleCommand = new RelayCommand(() => calls++), SaveLogCommand = new RelayCommand(() => calls++) };
        var window = ConsoleTestView.Create(header, height: 48);
        try
        {
            foreach (var name in new[] { "Export", "More" })
            {
                var button = header.FindControl<Button>(name)!;
                var menu = Assert.IsType<MenuFlyout>(button.Flyout);
                menu.ShowAt(button);
                ConsoleTestView.Pump(window);
                var items = menu.Items.OfType<MenuItem>().ToArray();
                foreach (var item in items.Take(3)) { Assert.NotNull(item.Command); item.Command!.Execute(null); }
                items.Single(item => Equals(item.Header, "Include time")).Command!.Execute(null);
                items.Single(item => Equals(item.Header, "Include level")).Command!.Execute(null);
                menu.Hide();
            }
            Assert.Equal(6, calls);
            Assert.True(controller.ExportOptions.IncludeTime);
            Assert.True(controller.ExportOptions.IncludeLevel);
        }
        finally { window.Close(); }
    }
    /// <summary>Time choices and narrow display grouping use controller commands and checked state.</summary>
    [AvaloniaFact]
    public void TimeAndNarrowDisplayMenusReflectControllerState()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var header = new ConsoleHeader { Controller = controller };
        var window = ConsoleTestView.Create(header, height: 48);
        try
        {
            foreach (var name in new[] { "Time", "Display" })
            {
                var button = header.FindControl<Button>(name)!;
                var menu = Assert.IsType<MenuFlyout>(button.Flyout);
                menu.ShowAt(button);
                ConsoleTestView.Pump(window);
                var choices = menu.Items.OfType<MenuItem>().Take(3).ToArray();
                choices[1].Command!.Execute(choices[1].CommandParameter);
                ConsoleTestView.Pump(window);
                Assert.Equal(ConsoleTimeMode.Relative, controller.Filter.TimeMode);
                Assert.True(choices[1].IsChecked);
                Assert.False(choices[0].IsChecked);
                choices[2].Command!.Execute(choices[2].CommandParameter);
                ConsoleTestView.Pump(window);
                Assert.Equal(ConsoleTimeMode.Hidden, controller.Filter.TimeMode);
                Assert.True(choices[2].IsChecked);
                Assert.All(choices, choice => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(choice))));
                menu.Hide();
            }
        }
        finally { window.Close(); }
    }

}
