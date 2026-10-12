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
        Assert.DoesNotContain(list.GetChildren()!.OfType<ConsoleRowAutomationPeer>(), peer => before.Contains(peer.PositionInSet));
        Assert.Equal(scene.Host.Children.Count, list.GetChildren()!.OfType<ConsoleRowAutomationPeer>().Count());
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

    [AvaloniaFact]
    public void Children_VisibleJumpButton_InvokeRequestsResume()
    {
        using var scene = new ConsoleScrollScene(1000);
        scene.View.ViewStateRequested += (_, state) => scene.View.ViewState = state;
        scene.Pause();
        var list = List(scene.View);
        var button = Assert.Single(list.GetChildren()!, peer => peer.GetAutomationControlType() == AutomationControlType.Button);
        Assert.Same(ControlAutomationPeer.FromElement(scene.View.TemplateParts!.Jump), button);

        button.GetProvider<IInvokeProvider>()!.Invoke();
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.IsType<global::Nvt.Core.LogConsole.ConsoleFollow.Following>(scene.View.ViewState.Follow);
        Assert.False(scene.View.TemplateParts.Jump.IsVisible);
        Assert.DoesNotContain(button, list.GetChildren()!);
        Assert.True(scene.Host.AtEnd);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Children_TemplateVisibility_AppendsOnlyVisibleParts(bool jumpVisible, bool retentionVisible)
    {
        using var scene = new ConsoleInteractionScene();
        var parts = scene.View.TemplateParts!;
        parts.Jump.IsVisible = jumpVisible;
        parts.Retention.IsVisible = retentionVisible;
        var expected = new[]
        {
            jumpVisible ? ControlAutomationPeer.CreatePeerForElement(parts.Jump) : null,
            retentionVisible ? ControlAutomationPeer.CreatePeerForElement(parts.Retention) : null,
        }.OfType<AutomationPeer>();

        var children = List(scene.View).GetChildren()!;

        Assert.Equal(ConsoleInteractionWindow.Host(scene.View).Children.Count, children.OfType<ConsoleRowAutomationPeer>().Count());
        Assert.Equal(expected, children.SkipWhile(peer => peer is ConsoleRowAutomationPeer));
    }

    [AvaloniaFact]
    public void Children_HiddenRetention_RemovesPreviouslyVisibleNotice()
    {
        using var scene = new ConsoleInteractionScene();
        var parts = scene.View.TemplateParts!;
        parts.Retention.IsVisible = true;
        var list = List(scene.View);
        var notice = Assert.Single(list.GetChildren()!, peer => peer is not ConsoleRowAutomationPeer);
        Assert.Same(ControlAutomationPeer.FromElement(parts.Retention), notice);

        parts.Retention.IsVisible = false;

        Assert.DoesNotContain(notice, list.GetChildren()!);
    }

    [AvaloniaFact]
    public void Children_HiddenView_ExcludesTemplateParts()
    {
        using var scene = new ConsoleInteractionScene();
        var parts = scene.View.TemplateParts!;
        parts.Jump.IsVisible = true;
        parts.Retention.IsVisible = true;
        var list = List(scene.View);
        var jump = ControlAutomationPeer.CreatePeerForElement(parts.Jump);
        var retention = ControlAutomationPeer.CreatePeerForElement(parts.Retention);
        Assert.Contains(jump, list.GetChildren()!);
        Assert.Contains(retention, list.GetChildren()!);

        scene.View.IsVisible = false;

        Assert.DoesNotContain(jump, list.GetChildren()!);
        Assert.DoesNotContain(retention, list.GetChildren()!);
    }

    [AvaloniaFact]
    public void Children_VisibleTemplateParts_ReportListPeerAsParent()
    {
        using var scene = new ConsoleInteractionScene();
        var parts = scene.View.TemplateParts!;
        parts.Jump.IsVisible = true;
        parts.Retention.IsVisible = true;
        var list = List(scene.View);

        var children = list.GetChildren()!.Where(peer => peer is not ConsoleRowAutomationPeer).ToArray();

        Assert.Equal(2, children.Length);
        Assert.All(children, peer => Assert.Same(list, peer.GetParent()));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Children_InactiveView_DropsVisibleTemplateParts(bool dispose)
    {
        using var scene = new ConsoleInteractionScene();
        scene.View.TemplateParts!.Jump.IsVisible = true;
        scene.View.TemplateParts.Retention.IsVisible = true;
        var list = List(scene.View);
        Assert.Equal(2, list.GetChildren()!.Count(peer => peer is not ConsoleRowAutomationPeer));

        Deactivate(scene, dispose);

        Assert.Empty(list.GetChildren()!);
    }
    private static void Deactivate(ConsoleInteractionScene scene, bool dispose)
    {
        if (dispose) scene.Controller.Dispose();
        else scene.Window.Content = null;
    }

    [AvaloniaFact]
    public void Children_ScrollWithPeer_InvalidatesOncePerLayoutPass()
    {
        using var scene = new ConsoleScrollScene(1000);
        scene.Pause();
        var list = List(scene.View);
        var invalidations = new List<EventArgs>();
        list.ChildrenChanged += (_, args) => invalidations.Add(args);

        scene.Host.Offset = new(0, 400);
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Single(invalidations);
        Assert.All(list.GetChildren()!.OfType<ConsoleRowAutomationPeer>(), peer => Assert.InRange(peer.PositionInSet, 20, 36));
    }

    [AvaloniaFact]
    public void Children_PauseShowsJumpButton_InvalidatesOnceWithSameRows()
    {
        using var scene = new ConsoleInteractionScene();
        var list = List(scene.View);
        var rows = list.GetChildren()!.Count;
        var invalidations = new List<EventArgs>();
        list.ChildrenChanged += (_, args) => invalidations.Add(args);

        scene.View.ViewState = scene.View.ViewState.Pause(scene.View.Projection!, new(1));
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Single(invalidations);
        Assert.Equal(rows + 1, list.GetChildren()!.Count);
    }

    [AvaloniaFact]
    public void Children_StateWithoutVisibilityChange_DoesNotInvalidate()
    {
        using var scene = new ConsoleInteractionScene();
        var list = List(scene.View);
        list.GetChildren();
        var invalidations = new List<EventArgs>();
        list.ChildrenChanged += (_, args) => invalidations.Add(args);

        scene.View.ViewState = scene.View.ViewState with { Selection = [1] };
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Empty(invalidations);
    }

    [AvaloniaFact]
    public void Children_ScrollWithoutPeer_CreatesNoAutomationPeers()
    {
        using var scene = new ConsoleScrollScene(1000);
        scene.Pause();
        Assert.Null(ControlAutomationPeer.FromElement(scene.View));

        scene.Host.Offset = new(0, 400);
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Null(ControlAutomationPeer.FromElement(scene.View));
        Assert.All(scene.Host.Children, row => Assert.Null(ControlAutomationPeer.FromElement(row)));
    }

    [AvaloniaTheory]
    [InlineData(ScrollAmount.SmallIncrement, 1)]
    [InlineData(ScrollAmount.SmallDecrement, -1)]
    [InlineData(ScrollAmount.LargeIncrement, 1)]
    [InlineData(ScrollAmount.LargeDecrement, -1)]
    [InlineData(ScrollAmount.NoAmount, 0)]
    public void ScrollProvider_Scroll_UsesHostStepAndPage(ScrollAmount amount, int direction)
    {
        using var scene = new ConsoleScrollScene(1000);
        scene.Pause();
        var scroll = List(scene.View).GetProvider<IScrollProvider>()!;
        scroll.SetScrollPercent(-1, 50);
        ConsoleInteractionWindow.Pump(scene.Window);
        var offset = scene.Host.Offset.Y;
        var size = amount is ScrollAmount.LargeIncrement or ScrollAmount.LargeDecrement
            ? scene.Host.PageScrollSize.Height : scene.Host.ScrollSize.Height;

        scroll.Scroll(ScrollAmount.NoAmount, amount);
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(offset + direction * size, scene.Host.Offset.Y, 6);
    }

    [AvaloniaTheory]
    [InlineData(-2)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ScrollProvider_InvalidVerticalPercent_KeepsOffset(double percent)
    {
        using var scene = new ConsoleScrollScene(1000);
        var scroll = List(scene.View).GetProvider<IScrollProvider>()!;
        scroll.SetScrollPercent(-1, 50);
        ConsoleInteractionWindow.Pump(scene.Window);
        var offset = scene.Host.Offset;

        scroll.SetScrollPercent(-1, percent);

        Assert.Equal(offset, scene.Host.Offset);
    }
}
