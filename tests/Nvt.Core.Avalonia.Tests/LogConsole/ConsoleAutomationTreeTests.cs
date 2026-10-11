// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleAutomationTreeTests
{
    [AvaloniaFact]
    public void List_AttachedView_ReportsResourceNameAndMultipleSelection()
    {
        using var scene = new ConsoleInteractionScene();
        var peer = List(scene.View);
        Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
        Assert.Equal("Console messages", peer.GetName());
        Assert.True(peer.CanSelectMultiple); Assert.False(peer.IsSelectionRequired);
    }
    [AvaloniaFact]
    public void LiveSetting_ListAndRow_RemainsOff()
    {
        using var scene = new ConsoleInteractionScene();
        Assert.Equal(AutomationLiveSetting.Off, List(scene.View).GetLiveSetting());
        Assert.Equal(AutomationLiveSetting.Off, Peer(scene.Row(1)).GetLiveSetting());
    }
    [AvaloniaFact]
    public void List_HostNameOverride_UsesResourceFallbackPath()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Window.Resources["Nvt.Console.List.Name"] = "主控台訊息";
        Assert.Equal("主控台訊息", List(scene.View).GetName());
    }
    [AvaloniaFact]
    public void Children_VirtualizedList_ContainsOnlyRealizedRows()
    {
        using var scene = new ConsoleScrollScene(1000);
        var children = List(scene.View).GetChildren()!;
        Assert.Equal(scene.Host.Children.Select(row => Peer((ConsoleRowPresenter)row)).OrderBy(peer => peer.PositionInSet), children);
        Assert.InRange(children.Count, 1, 18);
        Assert.All(children, peer => Assert.Same(List(scene.View), peer.GetParent()));
    }
    [AvaloniaFact]
    public void Children_Scroll_ReplacesRealizedPeerInputs()
    {
        using var scene = new ConsoleScrollScene(1000);
        var list = List(scene.View); var before = list.GetChildren()!.Cast<ConsoleRowAutomationPeer>().Select(peer => peer.PositionInSet).ToArray();
        scene.Host.Offset = new(0, 400);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.DoesNotContain(list.GetChildren()!.Cast<ConsoleRowAutomationPeer>(), peer => before.Contains(peer.PositionInSet));
        Assert.Equal(scene.Host.Children.Count, list.GetChildren()!.Count);
    }
    [AvaloniaFact]
    public void Selection_VirtualizedList_ContainsOnlySelectedRealizedRows()
    {
        using var scene = new ConsoleScrollScene(1000);
        scene.View.ViewState = scene.View.ViewState with { Selection = [1, 999, 1000] };
        var selected = List(scene.View).GetSelection();
        Assert.Equal([999, 1000], selected.Cast<ConsoleRowAutomationPeer>().Select(peer => peer.PositionInSet));
    }
    [AvaloniaFact]
    public void Position_RepeatedQuery_DoesNotVisitProjectionRows()
    {
        using var scene = new ConsoleScrollScene(1000);
        var peer = Assert.IsType<ConsoleRowAutomationPeer>(List(scene.View).GetChildren()![0]);
        var before = scene.Host.RowVisits;
        Assert.All(Enumerable.Range(0, 100), _ => Assert.InRange(peer.PositionInSet, 1, 1000));
        Assert.Equal(before, scene.Host.RowVisits);
    }
    [AvaloniaFact]
    public void ScrollProvider_Percent_UsesHostViewport()
    {
        using var scene = new ConsoleScrollScene(1000);
        var scroll = List(scene.View).GetProvider<IScrollProvider>()!;
        scroll.SetScrollPercent(-1, 25);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal(25, scroll.VerticalScrollPercent, 6);
        Assert.Equal(100 * scene.Host.Viewport.Height / scene.Host.Extent.Height, scroll.VerticalViewSize, 6);
    }
}
