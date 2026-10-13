// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks the toolbar's level row, hint, zoom and jump behavior through real controls.</summary>
public sealed class ConsoleToolbarTests
{
    private static readonly string[] _listButtons = ["ZoomOut", "ZoomValue", "ZoomIn", "Jump"];

    private static (ConsoleToolbar Toolbar, Window Window) Show(ConsoleController controller, double width = 1200, ConsoleListView? list = null)
    {
        var toolbar = new ConsoleToolbar { Controller = controller, List = list };
        return (toolbar, ConsoleTestView.Create(toolbar, width: width, height: 120));
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        ConsoleTestView.Pump(window);
    }

    /// <summary>The label names the filter in words and follows the controller.</summary>
    [AvaloniaFact]
    public void LabelTextAndNameFollowTheLevelFilter()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var text = toolbar.FindControl<TextBlock>("LevelsText")!;
            Assert.Equal("All levels", text.Text);
            Assert.Equal("Levels: All levels", AutomationProperties.GetName(label));
            controller.ToggleLevelCommand.Execute(LogLevel.Trace);
            ConsoleTestView.Pump(window);
            Assert.Equal("Debug and above", text.Text);
            controller.ToggleLevelCommand.Execute(LogLevel.Debug);
            controller.ToggleLevelCommand.Execute(LogLevel.Info);
            controller.ToggleLevelCommand.Execute(LogLevel.Warn);
            controller.ToggleLevelCommand.Execute(LogLevel.Error);
            ConsoleTestView.Pump(window);
            Assert.Equal("Fatal only", text.Text);
            controller.ToggleLevelCommand.Execute(LogLevel.Error);
            controller.ToggleLevelCommand.Execute(LogLevel.Fatal);
            ConsoleTestView.Pump(window);
            Assert.Equal("1 of 6 levels", text.Text);
            controller.ToggleLevelCommand.Execute(LogLevel.Error);
            ConsoleTestView.Pump(window);
            Assert.Equal("No levels", text.Text);
        }
        finally { window.Close(); }
    }

    /// <summary>The label reports expanded or collapsed and can be opened and closed by assistive technology.</summary>
    [AvaloniaFact]
    public void LabelReportsExpandStateAndAcceptsExpandAndCollapse()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var peer = ControlAutomationPeer.CreatePeerForElement(label)!;
            var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer);
            Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);
            provider.Expand();
            ConsoleTestView.Pump(window);
            Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
            Assert.True(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            provider.Collapse();
            ConsoleTestView.Pump(window);
            Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);
            Assert.False(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>A click pins the row, a second click closes it, and the chevron follows.</summary>
    [AvaloniaFact]
    public void ClickPinsTheRowAndSecondClickClosesIt()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var chevron = toolbar.FindControl<TextBlock>("LevelsChevron")!;
            var closedGlyph = chevron.Text;
            ConsoleTestView.Click(window, label);
            Assert.True(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            Assert.NotEqual(closedGlyph, chevron.Text);
            ConsoleTestView.Click(window, label);
            Assert.False(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            Assert.Equal(closedGlyph, chevron.Text);
        }
        finally { window.Close(); }
    }

    /// <summary>Down opens the row and moves into it, arrows move between items, and Escape returns to the label.</summary>
    [AvaloniaFact]
    public void KeyboardOpensNavigatesAndEscapeReturnsToTheLabel()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            Assert.True(label.Focus(NavigationMethod.Tab));
            Press(window, Key.Down);
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            Assert.True(items.IsVisible);
            var trace = toolbar.FindControl<ToggleButton>("LevelTrace")!;
            var debug = toolbar.FindControl<ToggleButton>("LevelDebug")!;
            Assert.Same(trace, window.FocusManager!.GetFocusedElement());
            Press(window, Key.Right);
            Assert.Same(debug, window.FocusManager.GetFocusedElement());
            Press(window, Key.Left);
            Assert.Same(trace, window.FocusManager.GetFocusedElement());
            Press(window, Key.Left);
            Assert.Same(label, window.FocusManager.GetFocusedElement());
            Press(window, Key.Right);
            Assert.Same(trace, window.FocusManager.GetFocusedElement());
            Press(window, Key.Escape);
            // The first Escape dismisses the focus tooltip of the item, as it does everywhere in Core.
            if (items.IsVisible) Press(window, Key.Escape);
            Assert.False(items.IsVisible);
            Assert.Same(label, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    /// <summary>A right-to-left layout opens and steps toward the end of the line with Left.</summary>
    [AvaloniaFact]
    public void RightToLeftSwapsTheArrowKeys()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            toolbar.FlowDirection = global::Avalonia.Media.FlowDirection.RightToLeft;
            ConsoleTestView.Pump(window);
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            Assert.True(label.Focus(NavigationMethod.Tab));
            Press(window, Key.Left);
            Assert.True(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            var trace = toolbar.FindControl<ToggleButton>("LevelTrace")!;
            Assert.Same(trace, window.FocusManager!.GetFocusedElement());
            Press(window, Key.Left);
            Assert.Same(toolbar.FindControl<ToggleButton>("LevelDebug"), window.FocusManager.GetFocusedElement());
            Press(window, Key.Right);
            Assert.Same(trace, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    /// <summary>A press outside the open row closes it after the release, and a press inside leaves it open.</summary>
    [AvaloniaFact]
    public void PressOutsideClosesAfterReleaseAndInsideKeepsOpen()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface, height: 144);
        try
        {
            var toolbar = (ConsoleToolbar)surface.Children[1];
            var header = (ConsoleHeader)surface.Children[0];
            ConsoleTestView.OpenLevels(window, toolbar);
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            Assert.True(items.IsVisible);
            ConsoleTestView.Click(window, toolbar.FindControl<ToggleButton>("LevelWarn")!);
            Assert.True(items.IsVisible);
            ConsoleTestView.Click(window, header.FindControl<Button>("Display")!);
            Assert.False(items.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>The hint appears only while Error or Fatal entries are hidden, and counts them.</summary>
    [AvaloniaFact]
    public void HiddenErrorHintAppearsOnlyForHiddenErrorOrFatalEntries()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Error, "app", "one");
        fixture.Store.Add(LogLevel.Error, "app", "two");
        fixture.Store.Add(LogLevel.Fatal, "app", "three");
        fixture.Store.Add(LogLevel.Warn, "app", "four");
        fixture.Fence();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var hint = toolbar.FindControl<StackPanel>("ErrorHint")!;
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            Assert.False(hint.IsVisible);
            controller.ToggleLevelCommand.Execute(LogLevel.Warn);
            ConsoleTestView.Pump(window);
            Assert.False(hint.IsVisible);
            controller.ToggleLevelCommand.Execute(LogLevel.Error);
            ConsoleTestView.Pump(window);
            Assert.True(hint.IsVisible);
            Assert.Equal("2", hint.GetLogicalDescendants().OfType<TextBlock>().Single(text => !text.Classes.Contains("nvtIcon")).Text);
            Assert.Equal("Hidden error entries: 2", ToolTip.GetTip(hint));
            Assert.Contains("hidden error entries: 2", AutomationProperties.GetName(label), StringComparison.Ordinal);
            controller.ToggleLevelCommand.Execute(LogLevel.Fatal);
            ConsoleTestView.Pump(window);
            Assert.Equal("3", hint.GetLogicalDescendants().OfType<TextBlock>().Single(text => !text.Classes.Contains("nvtIcon")).Text);
            controller.ToggleLevelCommand.Execute(LogLevel.Error);
            controller.ToggleLevelCommand.Execute(LogLevel.Fatal);
            ConsoleTestView.Pump(window);
            Assert.False(hint.IsVisible);
            Assert.DoesNotContain("hidden", AutomationProperties.GetName(label), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    /// <summary>The hint hides while the row is open, because the toggles show the same counts.</summary>
    [AvaloniaFact]
    public void HiddenErrorHintIsReplacedByTheOpenRow()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Error, "app", "one");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.ToggleLevelCommand.Execute(LogLevel.Error);
        var (toolbar, window) = Show(controller);
        try
        {
            var host = toolbar.FindControl<Border>("ErrorHintHost")!;
            Assert.True(host.IsVisible);
            ConsoleTestView.OpenLevels(window, toolbar);
            Assert.False(host.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Zoom steps, the percentage and the reset button act on the list and respect its limits.</summary>
    [AvaloniaFact]
    public void ZoomButtonsStepResetAndStopAtTheLimits()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var list = new ConsoleListView { Controller = controller };
        var (toolbar, window) = Show(controller, list: list);
        try
        {
            var text = toolbar.FindControl<TextBlock>("ZoomText")!;
            var zoomIn = toolbar.FindControl<Button>("ZoomIn")!;
            var zoomOut = toolbar.FindControl<Button>("ZoomOut")!;
            var value = toolbar.FindControl<Button>("ZoomValue")!;
            Assert.Equal("100%", text.Text);
            Assert.Equal("Zoom 100%", AutomationProperties.GetName(value));
            ConsoleTestView.Click(window, zoomIn);
            Assert.Equal(1.1, list.Zoom, 3);
            Assert.Equal("110%", text.Text);
            ConsoleTestView.Click(window, zoomOut);
            ConsoleTestView.Click(window, zoomOut);
            Assert.Equal(0.9, list.Zoom, 3);
            ConsoleTestView.Click(window, value);
            Assert.Equal(1.0, list.Zoom, 3);
            list.Zoom = ConsoleListView.MaximumZoom;
            ConsoleTestView.Pump(window);
            Assert.False(zoomIn.IsEffectivelyEnabled);
            Assert.True(zoomOut.IsEffectivelyEnabled);
            list.Zoom = ConsoleListView.MinimumZoom;
            ConsoleTestView.Pump(window);
            Assert.False(zoomOut.IsEffectivelyEnabled);
            Assert.True(zoomIn.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    /// <summary>Zoom and jump stay disabled without a list, and jump asks the list to resume.</summary>
    [AvaloniaFact]
    public void ZoomAndJumpNeedAListAndJumpRequestsResume()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            Assert.All(_listButtons,
                name => Assert.False(toolbar.FindControl<Button>(name)!.IsEffectivelyEnabled, name));
            Assert.Equal("100%", toolbar.FindControl<TextBlock>("ZoomText")!.Text);
            var list = new ConsoleListView { Controller = controller };
            var requests = 0;
            list.ViewStateRequested += (_, _) => requests++;
            toolbar.List = list;
            ConsoleTestView.Pump(window);
            var jump = toolbar.FindControl<Button>("Jump")!;
            Assert.True(jump.IsEffectivelyEnabled);
            ConsoleTestView.Click(window, jump);
            Assert.Equal(1, requests);
        }
        finally { window.Close(); }
    }

    /// <summary>Hovering the label opens the row after the delay, and moving away closes it.</summary>
    [AvaloniaFact]
    public void HoverOpensAfterTheDelayAndLeavingCloses()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            toolbar.UseClock(clock);
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            ConsoleTestView.Pump(window);
            clock.Advance(TimeSpan.FromMilliseconds(299));
            ConsoleTestView.Pump(window);
            Assert.False(items.IsVisible);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            ConsoleTestView.Pump(window);
            Assert.True(items.IsVisible);
            window.MouseMove(new Point(center.X, 110));
            ConsoleTestView.Pump(window);
            clock.Advance(TimeSpan.FromMilliseconds(499));
            ConsoleTestView.Pump(window);
            Assert.True(items.IsVisible);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            ConsoleTestView.Pump(window);
            Assert.False(items.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>A focused search box blocks the hover open, so the row never shifts the field under typing.</summary>
    [AvaloniaFact]
    public void HoverDoesNotOpenWhileTheSearchFieldHasFocus()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            toolbar.UseClock(clock);
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            Assert.True(toolbar.FindControl<TextBox>("SearchBox")!.Focus());
            var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            ConsoleTestView.Pump(window);
            clock.Advance(TimeSpan.FromSeconds(2));
            ConsoleTestView.Pump(window);
            Assert.False(items.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Crossing the label in under 300 ms toward the search field opens nothing.</summary>
    [AvaloniaFact]
    public void PointerCrossingTheLabelQuicklyOpensNothing()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            toolbar.UseClock(clock);
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var search = toolbar.FindControl<TextBox>("SearchBox")!;
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            window.MouseMove(label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value);
            ConsoleTestView.Pump(window);
            clock.Advance(TimeSpan.FromMilliseconds(200));
            window.MouseMove(search.TranslatePoint(new Point(search.Bounds.Width / 2, search.Bounds.Height / 2), window)!.Value);
            ConsoleTestView.Pump(window);
            clock.Advance(TimeSpan.FromSeconds(2));
            ConsoleTestView.Pump(window);
            Assert.False(items.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Down on a hover-opened row pins it, so the focused toggle stays when the pointer leaves.</summary>
    [AvaloniaFact]
    public void DownOnAHoverOpenedRowPinsIt()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            toolbar.UseClock(clock);
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            clock.Advance(TimeSpan.FromMilliseconds(300));
            ConsoleTestView.Pump(window);
            Assert.True(items.IsVisible);
            Assert.True(label.Focus(NavigationMethod.Directional));
            Press(window, Key.Down);
            window.MouseMove(new Point(center.X, 110));
            ConsoleTestView.Pump(window);
            clock.Advance(TimeSpan.FromSeconds(5));
            ConsoleTestView.Pump(window);
            Assert.True(items.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>A touch pointer never opens the row by hover.</summary>
    [AvaloniaFact]
    public void TouchPointerNeverOpensByHover()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            toolbar.UseClock(clock);
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            var touch = new PointerEventArgs(InputElement.PointerEnteredEvent, label, new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true),
                window, new Point(20, 20), 0, new PointerPointProperties(), KeyModifiers.None);
            label.RaiseEvent(touch);
            clock.Advance(TimeSpan.FromSeconds(2));
            ConsoleTestView.Pump(window);
            Assert.False(items.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>A mouse button held down anywhere blocks the hover open until the button is released.</summary>
    [AvaloniaFact]
    public void MouseButtonDownBlocksTheHoverOpen()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            toolbar.UseClock(clock);
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseDown(new Point(center.X, 110), MouseButton.Left);
            window.MouseMove(center);
            ConsoleTestView.Pump(window);
            clock.Advance(TimeSpan.FromSeconds(2));
            ConsoleTestView.Pump(window);
            Assert.False(items.IsVisible);
            window.MouseUp(center, MouseButton.Left);
        }
        finally { window.Close(); }
    }

    /// <summary>A press outside keeps the pinned row open until the button is released.</summary>
    [AvaloniaFact]
    public void PressOutsideClosesOnlyAfterTheRelease()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var items = toolbar.FindControl<StackPanel>("LevelItems")!;
            ConsoleTestView.OpenLevels(window, toolbar);
            Assert.True(items.IsVisible);
            var outside = new Point(20, 110);
            window.MouseDown(outside, MouseButton.Left);
            ConsoleTestView.Pump(window);
            Assert.True(items.IsVisible);
            window.MouseUp(outside, MouseButton.Left);
            ConsoleTestView.Pump(window);
            Assert.False(items.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>A control that hides while it has focus hands the focus to the level label.</summary>
    [AvaloniaFact]
    public void FocusMovesToTheLabelWhenTheFocusedControlHides()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller, width: 790);
        try
        {
            var label = toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
            var search = toolbar.FindControl<TextBox>("SearchBox")!;
            Assert.True(search.Focus());
            ConsoleTestView.Pump(window);
            var peer = ControlAutomationPeer.CreatePeerForElement(label)!;
            Assert.IsAssignableFrom<IExpandCollapseProvider>(peer).Expand();
            ConsoleTestView.Pump(window);
            Assert.False(toolbar.FindControl<Border>("SearchHost")!.IsVisible);
            Assert.Same(label, window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    /// <summary>A malformed hidden-error template keeps the built-in name and hint text.</summary>
    [AvaloniaTheory]
    [InlineData("Levels.NameHidden")]
    [InlineData("Levels.ErrorsHidden")]
    public void MalformedHiddenErrorTemplatesKeepTheBuiltInText(string key)
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Trace, "app", "t");
        fixture.Store.Add(LogLevel.Error, "app", "e");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.ToggleLevelCommand.Execute(LogLevel.Trace);
        controller.ToggleLevelCommand.Execute(LogLevel.Error);
        var (toolbar, window) = Show(controller);
        try
        {
            using var log = new ConsoleBindingLogScope();
            window.Resources["Nvt.Console." + key] = "{";
            ConsoleTestView.Pump(window);
            Assert.Equal("Levels: 4 of 6 levels, hidden error entries: 1", AutomationProperties.GetName(toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!));
            Assert.Equal("Hidden error entries: 1", ToolTip.GetTip(toolbar.FindControl<StackPanel>("ErrorHint")!));
            Assert.Empty(log.Errors);
        }
        finally { window.Close(); }
    }

    /// <summary>Malformed name templates for the label and the zoom value keep the built-in names.</summary>
    [AvaloniaFact]
    public void MalformedLevelsAndZoomNameTemplatesKeepTheBuiltInNames()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            using var log = new ConsoleBindingLogScope();
            window.Resources["Nvt.Console.Levels.Name"] = "{";
            window.Resources["Nvt.Console.Zoom.Name"] = "{99}";
            ConsoleTestView.Pump(window);
            Assert.Equal("Levels: All levels", AutomationProperties.GetName(toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!));
            Assert.Equal("Zoom 100%", AutomationProperties.GetName(toolbar.FindControl<Button>("ZoomValue")!));
            Assert.Empty(log.Errors);
        }
        finally { window.Close(); }
    }

    /// <summary>An open row with four-digit counts still fits inside the toolbar at normal and compact widths.</summary>
    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(700)]
    [InlineData(760)]
    [InlineData(1180)]
    [InlineData(1250)]
    public void OpenRowWithLargeCountsStaysInsideTheToolbar(double width)
    {
        using var fixture = new ConsoleTestStore(maxEntries: 10_000);
        foreach (var level in Enum.GetValues<LogLevel>())
            for (var index = 0; index < 1200; index++) fixture.Store.Add(level, "app", "message");
        fixture.Fence();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller, width);
        try
        {
            ConsoleTestView.OpenLevels(window, toolbar);
            Assert.True(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
            var row = toolbar.FindControl<Grid>("Row")!;
            var edge = row.TranslatePoint(new Point(row.Bounds.Width, 0), toolbar)!.Value.X;
            Assert.All(toolbar.GetVisualDescendants().OfType<ContentControl>().Where(control => control is Button or ToggleButton && control.IsEffectivelyVisible), control =>
            {
                var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), toolbar)!.Value.X;
                Assert.True(right <= edge + 0.5, control.Name);
            });
        }
        finally { window.Close(); }
    }

    /// <summary>A name that grows while the row is open makes the names give way, and the toggles keep their counts.</summary>
    [AvaloniaFact]
    public void OpenRowDropsNamesWhenTheyGrowWhileOpen()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller, 760);
        try
        {
            ConsoleTestView.OpenLevels(window, toolbar);
            var name = toolbar.FindControl<TextBlock>("LevelWarnName")!;
            Assert.True(name.IsVisible);
            toolbar.Resources["Nvt.Console.Level.Warn"] = new string('W', 120);
            ConsoleTestView.Pump(window);
            Assert.False(name.IsVisible);
            Assert.True(toolbar.FindControl<TextBlock>("LevelWarnCount")!.IsVisible);
            Assert.True(toolbar.FindControl<StackPanel>("LevelItems")!.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>The names of the open row give way when they do not fit, and the row stays inside the toolbar.</summary>
    [AvaloniaTheory]
    [InlineData(700, false)]
    [InlineData(760, true)]
    public void OpenRowShowsNamesOnlyWhenTheyFit(double width, bool namesShown)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller, width);
        try
        {
            ConsoleTestView.OpenLevels(window, toolbar);
            Assert.Equal(namesShown, toolbar.FindControl<TextBlock>("LevelWarnName")!.IsVisible);
            Assert.True(toolbar.FindControl<TextBlock>("LevelWarnCount")!.IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Copy visible stays disabled until the host supplies its command.</summary>
    [AvaloniaFact]
    public void CopyVisibleNeedsACommand()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var copy = toolbar.FindControl<Button>("CopyVisible")!;
            Assert.False(copy.IsEnabled);
            toolbar.CopyVisibleCommand = new ConsoleClearSearchCommand(controller);
            ConsoleTestView.Pump(window);
            Assert.True(copy.IsEnabled);
        }
        finally { window.Close(); }
    }

    /// <summary>Clearing the search hides the Clear button, so the focus goes back to the search field.</summary>
    [AvaloniaFact]
    public void ClearSearchReturnsFocusToTheField()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            controller.SetSearchText("layer");
            ConsoleTestView.Pump(window);
            var clear = toolbar.FindControl<Button>("ClearSearch")!;
            Assert.True(clear.IsEffectivelyVisible);
            ConsoleTestView.Click(window, clear);
            ConsoleTestView.Pump(window);
            Assert.False(clear.IsEffectivelyVisible);
            Assert.Same(toolbar.FindControl<TextBox>("SearchBox"), window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    /// <summary>The funnel shape carries the Only matches state: the tooltip names it and no disc or ring is drawn.</summary>
    [AvaloniaFact]
    public void OnlyMatchesIsAPlainFunnelWhoseTooltipNamesTheState()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var flag = toolbar.FindControl<ToggleButton>("OnlyMatches")!;
            var field = toolbar.FindControl<TextBox>("SearchBox")!;
            for (var pass = 0; pass < 2; pass++)
            {
                var on = controller.Filter.OnlyMatches;
                Assert.Equal(on, flag.IsChecked);
                Assert.Equal(on ? "Only matches: on" : "Only matches: off", ToolTip.GetTip(flag));
                Assert.Equal("Only matches", AutomationProperties.GetName(flag));
                Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(flag.Background).Color.A);
                Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(flag.BorderBrush).Color.A);
                Assert.True(flag.Bounds.Height < field.Bounds.Height);
                controller.ToggleOnlyMatchesCommand.Execute(null);
                ConsoleTestView.Pump(window);
            }
        }
        finally { window.Close(); }
    }

    /// <summary>A disabled toolbar action is a dimmed glyph without the shared disabled fill or ring.</summary>
    [AvaloniaFact]
    public void DisabledActionsDrawNoFillOrRing()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var (toolbar, window) = Show(controller);
        try
        {
            var copy = toolbar.FindControl<Button>("CopySelected")!;
            Assert.Contains(":disabled", copy.Classes);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(copy.Background).Color.A);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(copy.BorderBrush).Color.A);
        }
        finally { window.Close(); }
    }
}
