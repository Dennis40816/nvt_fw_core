// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Nvt.Core.Avalonia.Icons;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Verifies console state and view contracts.</summary>
public sealed class ConsoleViewTests
{
    /// <summary>Both themes and shapes render the same responsive console geometry and accessible actions.</summary>
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
        var window = ConsoleTestView.Create(surface, dark, shape, width, 144);
        try
        {
            var header = Assert.IsType<ConsoleHeader>(surface.Children[0]);
            var toolbar = Assert.IsType<ConsoleToolbar>(surface.Children[1]);
            Assert.True(((ConsoleEmptyState)surface.Children[2]).IsVisible);
            Assert.Equal(32, header.Bounds.Height);
            Assert.Equal(40, toolbar.Bounds.Height);
            Assert.Equal(new Thickness(0), toolbar.FindControl<Border>("ZoomGroup")!.BorderThickness);
            var compact = width < 700;
            Assert.True(toolbar.FindControl<Border>("SearchHost")!.IsVisible);
            Assert.False(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            Assert.Equal(compact, toolbar.FindControl<TextBlock>("SourceIcon")!.IsVisible);
            Assert.Equal(!compact, toolbar.FindControl<StackPanel>("SourceText")!.IsVisible);
            Assert.Equal(!compact, toolbar.FindControl<Button>("ZoomOut")!.IsVisible);
            Assert.Equal(!compact, toolbar.FindControl<Button>("ZoomIn")!.IsVisible);
            Assert.Equal(!compact, toolbar.FindControl<Button>("CopyVisible")!.IsVisible);
            Assert.True(toolbar.FindControl<Button>("Jump")!.IsVisible);
            Assert.All(surface.GetVisualDescendants().OfType<Button>(), button =>
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), button.Name);
                Assert.NotNull(ControlAutomationPeer.CreatePeerForElement(button));
            });
            using var frame = new RenderTargetBitmap(new PixelSize(width, 144), new Vector(96, 96));
            frame.Render(window);
        }
        finally { window.Close(); }
    }

    /// <summary>The compact tier starts at 700 DIP and drops zoom steps, copy-all and the Sources text.</summary>
    [AvaloniaTheory]
    [InlineData(640, true)]
    [InlineData(699, true)]
    [InlineData(700, false)]
    [InlineData(790, false)]
    [InlineData(1280, false)]
    public void WidthTiersSwitchCompactActionsAtTheNarrowBreakpoint(double width, bool compact)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, width: width, height: 40);
        try
        {
            Assert.Equal(compact, toolbar.FindControl<TextBlock>("SourceIcon")!.IsVisible);
            Assert.Equal(compact, !toolbar.FindControl<Button>("ZoomOut")!.IsVisible);
            Assert.Equal(compact, !toolbar.FindControl<Button>("ZoomIn")!.IsVisible);
            Assert.Equal(compact, !toolbar.FindControl<Button>("CopyVisible")!.IsVisible);
            Assert.True(toolbar.FindControl<Button>("ZoomValue")!.IsVisible);
            Assert.True(toolbar.FindControl<Button>("CopySelected")!.IsVisible);
            Assert.True(toolbar.FindControl<Button>("ClearConsole")!.IsVisible);
            window.Width = compact ? 1200 : 640;
            ConsoleTestView.Pump(window);
            Assert.Equal(!compact, toolbar.FindControl<TextBlock>("SourceIcon")!.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>An open level row takes the row at 790 DIP, and shares it with the actions at 1280 DIP.</summary>
    [AvaloniaTheory]
    [InlineData(790, false)]
    [InlineData(640, false)]
    [InlineData(1179, false)]
    [InlineData(1180, true)]
    [InlineData(1280, true)]
    public void OpenLevelRowHidesSearchAndActionsUnlessWide(double width, bool shared)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, width: width, height: 40);
        try
        {
            ConsoleTestView.OpenLevels(window, toolbar);
            Assert.True(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            Assert.Equal(shared, toolbar.FindControl<Border>("SearchHost")!.IsVisible);
            Assert.Equal(shared, toolbar.FindControl<StackPanel>("Actions")!.IsVisible);
            Assert.True(toolbar.FindControl<Button>("Jump")!.IsVisible);
            // The open row never pushes the search field or the actions past the toolbar edge.
            var row = toolbar.FindControl<Grid>("Row")!;
            var edge = row.TranslatePoint(new Point(row.Bounds.Width, 0), toolbar)!.Value.X;
            Assert.All(toolbar.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible), button =>
            {
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), toolbar)!.Value.X;
                Assert.True(right <= edge + 0.5, button.Name);
            });
            ConsoleTestView.OpenLevels(window, toolbar);
            Assert.False(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            Assert.True(toolbar.FindControl<Border>("SearchHost")!.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>A level tooltip keeps its raw count when that level is excluded from visible rows.</summary>
    [AvaloniaFact]
    public void LevelTooltipUsesRawCountAndToggleUpdatesController()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Error, "app", "failure");
        fixture.Fence();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 40);
        try
        {
            ConsoleTestView.OpenLevels(window, toolbar);
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

    /// <summary>Typing and visible actions share controller filters across both responsive rows.</summary>
    [AvaloniaFact]
    public void SearchClearOnlyMatchesDedupeAndResetShareControllerState()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "other");
        fixture.Fence();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface);
        try
        {
            var header = (ConsoleHeader)surface.Children[0];
            var toolbar = (ConsoleToolbar)surface.Children[1];
            var search = toolbar.FindControl<TextBox>("SearchBox")!;
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
            var onlyMatches = toolbar.FindControl<ToggleButton>("OnlyMatches")!;
            Assert.Equal(NvtIcons.FilterAlt, Assert.IsType<TextBlock>(onlyMatches.Content).Text);
            ConsoleTestView.Click(window, onlyMatches);
            Assert.Equal(NvtIcons.FilterAltOff, Assert.IsType<TextBlock>(onlyMatches.Content).Text);
            var display = header.FindControl<Button>("Display")!;
            var menu = Assert.IsType<MenuFlyout>(display.Flyout);
            menu.ShowAt(display);
            ConsoleTestView.Pump(window);
            menu.Items.OfType<MenuItem>().Last().Command!.Execute(null);
            menu.Hide();
            Assert.False(controller.Filter.OnlyMatches);
            Assert.True(controller.Filter.Deduplicate);
            ConsoleTestView.Click(window, toolbar.FindControl<Button>("ClearSearch")!);
            ConsoleTestView.Pump(window);
            Assert.Empty(controller.Filter.SearchText);
            controller.SetSelectedSources(["idle"]);
            ConsoleTestView.Pump(window);
            var reset = surface.Children[2].GetVisualDescendants().OfType<Button>().Single();
            ConsoleTestView.Click(window, reset);
            ConsoleTestView.Pump(window);
            Assert.True(controller.Filter.OnlyMatches);
            Assert.False(controller.Filter.Deduplicate);
        }
        finally { window.Close(); }
    }

    /// <summary>The source menu includes idle declarations and preserves multi-selection until reset.</summary>
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

    /// <summary>The More menu invokes all host commands and independent inclusion options.</summary>
    [AvaloniaFact]
    public void ExportBindingsAndIncludeFlagsWorkInMoreMenu()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var calls = 0;
        var header = new ConsoleHeader
        {
            Controller = controller,
            CopySelectedCommand = new RelayCommand(() => calls++),
            CopyVisibleCommand = new RelayCommand(() => calls++),
            SaveLogCommand = new RelayCommand(() => calls++)
        };
        var window = ConsoleTestView.Create(header, height: 40);
        try
        {
            InvokeExportMenu(header, "More", window);
            Assert.Equal(3, calls);
            // Both options start on, and the menu toggles each once.
            Assert.False(controller.ExportOptions.IncludeTime);
            Assert.False(controller.ExportOptions.IncludeLevel);
        }
        finally { window.Close(); }
    }
    /// <summary>The toolbar's copy-all button runs the command that the host supplies.</summary>
    [AvaloniaFact]
    public void ToolbarCopyVisibleButtonRunsHostCommand()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var calls = 0;
        var toolbar = new ConsoleToolbar { Controller = controller, CopyVisibleCommand = new RelayCommand(() => calls++) };
        var window = ConsoleTestView.Create(toolbar, height: 40);
        try
        {
            ConsoleTestView.Click(window, toolbar.FindControl<Button>("CopyVisible")!);
            Assert.Equal(1, calls);
        }
        finally { window.Close(); }
    }
    /// <summary>Time choices and the duplicate toggle in the Display menu use controller commands and checked state.</summary>
    [AvaloniaFact]
    public void DisplayMenuReflectsControllerState()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var header = new ConsoleHeader { Controller = controller };
        var window = ConsoleTestView.Create(header, height: 40);
        try
        {
            AssertTimeMenu(header, "Display", controller, window);
        }
        finally { window.Close(); }
    }

    private static void InvokeExportMenu(ConsoleHeader header, string name, Window window)
    {
        var button = header.FindControl<Button>(name)!;
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        menu.ShowAt(button);
        ConsoleTestView.Pump(window);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.All(items.Take(3), item => { Assert.NotNull(item.Command); item.Command!.Execute(null); });
        items.Single(item => Equals(item.Header, "Include time")).Command!.Execute(null);
        items.Single(item => Equals(item.Header, "Include level")).Command!.Execute(null);
        menu.Hide();
    }
    private static void AssertTimeMenu(ConsoleHeader header, string name, ConsoleController controller, Window window)
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
