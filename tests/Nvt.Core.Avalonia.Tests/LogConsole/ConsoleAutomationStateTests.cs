// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Automation;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleAutomationStateTests
{
    [AvaloniaFact]
    public void Select_UnacceptedRequest_DoesNotAssignViewState()
    {
        using var content = new InMemoryLogTextContent("message");
        using var projection = Project(Row(content));
        var view = new ConsoleListView { Projection = projection };
        var window = ConsoleInteractionWindow.Create(view);
        try
        {
            var original = view.ViewState;
            var requests = new List<ConsoleViewState>();
            view.ViewStateRequested += (_, state) => requests.Add(state);
            Assert.IsType<ConsoleRowAutomationPeer>(List(view).GetChildren()![0]).Select();
            Assert.Same(original, view.ViewState);
            Assert.Equal([1L], Assert.Single(requests).Selection);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void IsSelected_GroupedRow_RecognizesAnyMember()
    {
        using var scene = new ConsoleInteractionScene(count: 3, message: "message");
        var row = Configure(scene, true, true, false);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [3] });
        Assert.True(Peer(row).IsSelected);
        Assert.True(scene.Controller.CopySelectionCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(0, new long[] { 2 })]
    [InlineData(1, new long[] { 1, 2 })]
    [InlineData(2, new long[] { 1 })]
    public void Selection_PeerAction_MatchesPointerRequest(int value, long[] expected)
    {
        var action = (ConsoleSelectionAction)value;
        using var peerScene = new ConsoleInteractionScene(message: "message");
        using var pointerScene = new ConsoleInteractionScene(message: "message");
        PrepareSelection(peerScene, action); PrepareSelection(pointerScene, action);
        var requests = new List<ConsoleViewState>();
        peerScene.View.ViewStateRequested += (_, state) => requests.Add(state);
        Act(Peer(peerScene.Row(2)), action);
        pointerScene.Click(2, action == ConsoleSelectionAction.Replace ? RawInputModifiers.None : RawInputModifiers.Control);
        Assert.Equal(expected, peerScene.Controller.ViewState.Selection.Order());
        Assert.Equal(pointerScene.Controller.ViewState.Selection.Order(), peerScene.Controller.ViewState.Selection.Order());
        var requested = Assert.Single(requests);
        Assert.Equal(peerScene.Controller.ViewState.Selection, requested.Selection);
        Assert.Equal(peerScene.Controller.ViewState.ExpandedIds, requested.ExpandedIds);
        Assert.IsType<ConsoleFollow.Paused>(requested.Follow);
    }
    private static void PrepareSelection(ConsoleInteractionScene scene, ConsoleSelectionAction action)
        => scene.Controller.RequestViewState(scene.Controller.ViewState with
        { Selection = action == ConsoleSelectionAction.Remove ? [1, 2] : [1] });
    private static void Act(ConsoleRowAutomationPeer peer, ConsoleSelectionAction action)
    {
        switch (action)
        {
            case ConsoleSelectionAction.Replace: peer.Select(); break;
            case ConsoleSelectionAction.Add: peer.AddToSelection(); break;
            case ConsoleSelectionAction.Remove: peer.RemoveFromSelection(); break;
        }
    }

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Expansion_PeerAction_ChangesOnlyExpandedIdsAndKeepsFocus(bool expand, bool following)
    {
        using var scene = new ConsoleInteractionScene(message: "first\nsecond");
        scene.Click(1);
        PreparePeerExpansion(scene, expand, following);
        var original = scene.View.ViewState;
        var focused = scene.Window.FocusManager!.GetFocusedElement();
        var focusEntry = scene.View.RowInteraction!.FocusedEntry;
        var requests = new List<ConsoleViewState>();
        scene.View.ViewStateRequested += (_, state) => requests.Add(state);

        ChangeExpansion(Peer(scene.Row(3)), expand);
        ConsoleInteractionWindow.Pump(scene.Window);

        var requested = Assert.Single(requests);
        Assert.Same(original.Selection, requested.Selection);
        Assert.Equal([1L, 2L], requested.Selection.Order());
        Assert.Same(original.Follow, requested.Follow);
        Assert.Equal(original with { ExpandedIds = requested.ExpandedIds }, requested);
        Assert.Equal(expand, requested.ExpandedIds.Contains(new(3)));
        Assert.Equal(expand ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed, Peer(scene.Row(3)).ExpandCollapseState);
        Assert.Same(focused, scene.Window.FocusManager.GetFocusedElement());
        Assert.Equal(focusEntry, scene.View.RowInteraction.FocusedEntry);
    }
    private static void PreparePeerExpansion(ConsoleInteractionScene scene, bool expand, bool following)
    {
        var state = scene.Controller.ViewState;
        scene.Controller.RequestViewState(state with
        {
            Selection = [1, 2],
            ExpandedIds = expand ? [] : [new(3)],
            Follow = following ? state.Resume().Follow : state.Follow,
        });
        ConsoleInteractionWindow.Pump(scene.Window);
    }

    private static void ChangeExpansion(ConsoleRowAutomationPeer peer, bool expand)
    {
        if (expand) peer.Expand(); else peer.Collapse();
    }

    [AvaloniaFact]
    public void ExpandPattern_SingleShortLine_IsAbsent()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "message");
        Assert.Null(Peer(scene.Row(1)).GetProvider<IExpandCollapseProvider>());
        Assert.Equal(ExpandCollapseState.LeafNode, Peer(scene.Row(1)).ExpandCollapseState);
    }

    [AvaloniaFact]
    public void Reads_PublishedPeers_DoNotCaptureProjectionOrCallAdapters()
    {
        using var scene = new ConsoleInteractionScene();
        var list = List(scene.View); var row = Peer(scene.Row(1));
        var captures = scene.Controller.CommandProjectionCaptures;
        scene.Adapters.OnAvailability = () => throw new InvalidOperationException("Peer invoked an adapter");
        Read(row); Read(list);
        Assert.Equal(captures, scene.Controller.CommandProjectionCaptures);
    }

    [AvaloniaFact]
    public void Reads_PublishedPeers_DoNotEnterLifecycleLock()
    {
        using var scene = new ConsoleInteractionScene();
        var list = List(scene.View); var row = Peer(scene.Row(1));
        var reads = scene.Controller.LifecycleLockReads;
        Read(row); Read(list);
        _ = row.IsSelected; _ = row.ExpandCollapseState;
        _ = list.GetSelection(); _ = list.VerticalViewSize;
        AssertLifecycleReads(scene.Controller, reads);
    }

    [AvaloniaFact]
    public void LifecycleRead_LockedGetter_IsRejectedByReadGuard()
    {
        using var scene = new ConsoleInteractionScene();
        var reads = scene.Controller.LifecycleLockReads;
        _ = scene.Controller.IsActive;
        Assert.Throws<global::Xunit.Sdk.EqualException>(() => AssertLifecycleReads(scene.Controller, reads));
    }

    private static void AssertLifecycleReads(global::Nvt.Core.Avalonia.LogConsole.ConsoleController controller, int expected)
        => Assert.Equal(expected, controller.LifecycleLockReads);

    [AvaloniaFact]
    public void Patterns_PublishedPeers_DoNotCaptureProjection()
    {
        using var scene = new ConsoleInteractionScene();
        var list = List(scene.View); var row = Peer(scene.Row(1));
        var captures = scene.Controller.CommandProjectionCaptures;
        _ = row.GetProvider<ISelectionItemProvider>()!.IsSelected;
        _ = row.GetProvider<IExpandCollapseProvider>()!.ExpandCollapseState;
        _ = list.GetProvider<ISelectionProvider>()!.GetSelection();
        _ = list.GetProvider<IScrollProvider>()!.VerticalViewSize;
        Assert.Equal(captures, scene.Controller.CommandProjectionCaptures);
    }

    [AvaloniaFact]
    public void Reads_DisposedController_ReturnNeutralValues()
    {
        using var scene = new ConsoleInteractionScene();
        var list = List(scene.View); var row = Peer(scene.Row(1));
        var selection = list.GetProvider<ISelectionProvider>()!; var scroll = list.GetProvider<IScrollProvider>()!;
        scene.Controller.Dispose();
        AssertNeutral(row, list, selection, scroll);
    }

    [AvaloniaFact]
    public void Reads_DetachedView_ReturnNeutralValues()
    {
        using var scene = new ConsoleInteractionScene();
        var list = List(scene.View); var row = Peer(scene.Row(1));
        var selection = list.GetProvider<ISelectionProvider>()!; var scroll = list.GetProvider<IScrollProvider>()!;
        scene.Window.Content = null;
        AssertNeutral(row, list, selection, scroll);
    }

    [AvaloniaFact]
    public void Configure_RecycledPresenter_NotifiesEachChangedPropertyOnce()
    {
        using var scene = new ConsoleInteractionScene(count: 2);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [2], ExpandedIds = [new(2)] });
        ConsoleInteractionWindow.Pump(scene.Window);
        var presenter = scene.Row(1); var peer = Peer(presenter);
        var changes = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, args) => changes.Add(args);
        presenter.Configure(scene.Controller.Projection.Rows[1], scene.View, 1);
        presenter.Measure(new(presenter.Bounds.Width, double.PositiveInfinity));
        presenter.Arrange(new(presenter.Bounds.Position, presenter.DesiredSize));
        Assert.Equal(Peer(scene.Row(2)).GetName(), peer.GetName());
        Assert.Equal(2, peer.PositionInSet);
        Assert.Single(changes, change => change.Property == AutomationElementIdentifiers.NameProperty);
        Assert.Single(changes, change => change.Property == SelectionItemPatternIdentifiers.IsSelectedProperty);
        Assert.Single(changes, change => change.Property == ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty);
    }

    [AvaloniaFact]
    public void Configure_UnchangedInput_RaisesNoPropertyEvents()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        var presenter = scene.Row(1); var peer = Peer(presenter);
        var changes = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, args) => changes.Add(args);
        presenter.Configure(scene.Controller.Projection.Rows[0], scene.View, 0);
        Assert.Empty(changes);
    }

    [AvaloniaTheory]
    [InlineData(true, new long[] { 1, 2, 3 })]
    [InlineData(false, new long[] { 1, 2 })]
    public void Selection_AddOrRemove_KeepsKeyboardFocus(bool add, long[] expected)
    {
        using var scene = new ConsoleInteractionScene(message: "message");
        scene.Click(1);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = add ? [1, 2] : [1, 2, 3] });
        ConsoleInteractionWindow.Pump(scene.Window);
        var focused = scene.Window.FocusManager!.GetFocusedElement();
        var requests = new List<ConsoleViewState>();
        scene.View.ViewStateRequested += (_, state) => requests.Add(state);

        Act(Peer(scene.Row(3)), add ? ConsoleSelectionAction.Add : ConsoleSelectionAction.Remove);
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(expected, Assert.Single(requests).Selection.Order());
        Assert.Same(focused, scene.Window.FocusManager.GetFocusedElement());
        Assert.Equal(1L, scene.View.RowInteraction!.FocusedEntry);
    }

    [AvaloniaFact]
    public void Select_PeerAction_MovesFocusLikePointerClick()
    {
        using var scene = new ConsoleInteractionScene(message: "message");
        scene.Click(1);

        Peer(scene.Row(3)).Select();
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Same(scene.Row(3), scene.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(3L, scene.View.RowInteraction!.FocusedEntry);
        Assert.Equal([3L], scene.View.ViewState.Selection);
    }

    [AvaloniaFact]
    public void Expansion_EnterOnFocusedRow_ReplacesSelectionAndPauses()
    {
        using var scene = new ConsoleInteractionScene(message: "first\nsecond");
        scene.Click(3);
        scene.Controller.RequestViewState(scene.Controller.ViewState.Resume() with { Selection = [1, 2], ExpandedIds = [] });
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Empty(scene.View.ViewState.ExpandedIds);
        scene.SendKey(Key.Enter);

        Assert.Equal([3L], scene.View.ViewState.Selection);
        Assert.Contains(new(3), scene.View.ViewState.ExpandedIds);
        Assert.IsType<ConsoleFollow.Paused>(scene.View.ViewState.Follow);
        Assert.Same(scene.Row(3), scene.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LifecycleRead_EachLockedAccessor_IsRejectedByReadGuard(int accessor)
    {
        using var scene = new ConsoleInteractionScene();
        var reads = scene.Controller.LifecycleLockReads;

        ReadLifecycleAccessor(scene.Controller, accessor);

        Assert.Throws<global::Xunit.Sdk.EqualException>(() => AssertLifecycleReads(scene.Controller, reads));
        Assert.Equal(reads + 1, scene.Controller.LifecycleLockReads);
    }
    [AvaloniaFact]
    public void LifecycleRead_CurrentVersionCheck_CountsBothLockEntries()
    {
        using var scene = new ConsoleInteractionScene();
        var version = scene.Controller.CommandStateVersion;
        var reads = scene.Controller.LifecycleLockReads;

        _ = scene.Controller.IsCommandStateCurrent(version, scene.Controller.Projection);

        Assert.Equal(reads + 2, scene.Controller.LifecycleLockReads);
    }
    private static void ReadLifecycleAccessor(ConsoleController controller, int accessor)
    {
        switch (accessor)
        {
            case 0: _ = controller.IsActive; break;
            case 1: _ = controller.CommandStateVersion; break;
            case 2: _ = controller.IsCommandStateCurrent(-1, controller.Projection); break;
            case 3: _ = controller.IsPublishedStateCurrent; break;
        }
    }
}
