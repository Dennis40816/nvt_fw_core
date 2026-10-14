// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleAutomationFocusTests
{
    [AvaloniaFact]
    public void Tab_ComposedConsole_VisitsHeaderToolbarListAndNext()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        var next = AddNext(scene);
        scene.Header!.FindControl<Button>("Display")!.Focus(NavigationMethod.Tab);
        var order = TabOrder(scene, next);
        Assert.Equal(["header", "toolbar", "list", "next"], order.Distinct());
        Assert.Single(order, entry => string.Equals(entry, "list", StringComparison.Ordinal));
        Assert.All(ConsoleInteractionWindow.Host(scene.View).Children, row => Assert.False(row.IsTabStop));
    }
    [AvaloniaFact]
    public void ShiftTab_ListEntry_ReturnsToLastToolbarControl()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.View.Focus(NavigationMethod.Tab);
        scene.SendKey(Key.Tab, RawInputModifiers.Shift);
        Assert.Same(scene.Toolbar!.FindControl<Button>("Jump"), scene.Window.FocusManager!.GetFocusedElement());
    }
    [AvaloniaFact]
    public void Focus_RowNavigation_ReportsThatRowsPeer()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.Down);
        var focused = Assert.IsAssignableFrom<Control>(scene.Window.FocusManager!.GetFocusedElement());
        Assert.Same(Peer(scene.Row(2)), ControlAutomationPeer.CreatePeerForElement(focused));
        Assert.True(Peer(scene.Row(2)).HasKeyboardFocus());
    }
    [AvaloniaFact]
    public void Focus_RecycledRow_ReturnsToListPeer()
    {
        using var scene = new ConsoleInteractionScene(count: 100);
        scene.View.Focus(NavigationMethod.Directional);
        var old = Assert.IsAssignableFrom<Control>(scene.Window.FocusManager!.GetFocusedElement());
        var oldPeer = ControlAutomationPeer.CreatePeerForElement(old)!;
        ConsoleInteractionWindow.Host(scene.View).Offset = new(0, 400);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.False(oldPeer.HasKeyboardFocus());
        var focused = Assert.IsAssignableFrom<Control>(scene.Window.FocusManager!.GetFocusedElement());
        Assert.Same(scene.View, focused);
        Assert.Same(List(scene.View), ControlAutomationPeer.CreatePeerForElement(focused));
    }
    private static Button AddNext(ConsoleInteractionScene scene)
    {
        var grid = Assert.IsType<Grid>(scene.Window.Content);
        grid.RowDefinitions.Add(new(GridLength.Auto));
        var next = new Button { Content = "Next" };
        Grid.SetRow(next, 3); grid.Children.Add(next);
        ConsoleInteractionWindow.Pump(scene.Window);
        return next;
    }
    private static List<string> TabOrder(ConsoleInteractionScene scene, Button next)
    {
        var result = new List<string>();
        for (var index = 0; index < 40; index++)
        {
            var focused = Assert.IsAssignableFrom<Control>(scene.Window.FocusManager!.GetFocusedElement());
            var ancestry = focused.GetVisualAncestors().Prepend(focused).ToArray();
            result.Add(focused == next ? "next" : ancestry.Contains(scene.Header!) ? "header"
                : ancestry.Contains(scene.Toolbar!) ? "toolbar" : ancestry.Contains(scene.View) ? "list" : "other");
            if (focused == next) return result;
            scene.SendKey(Key.Tab);
        }
        throw new InvalidOperationException("Tab navigation did not reach the next control.");
    }
}
