// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleLifecycleTests
{
    [AvaloniaFact]
    public void Detach_AttachedScene_ReleasesSubscriptionAndPendingPosts()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        scene.Controller.SetSearchText("example");
        var session = scene.View.AttachmentSession!;
        Assert.True(scene.PendingOperationCount > 0);

        scene.Detach();

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
        Assert.Equal(0, scene.PendingOperationCount);
        Assert.Null(session.PendingOperation);
        Assert.Null(session.DeferredOperation);
        Assert.Null(scene.View.AttachmentSession);
    }

    [AvaloniaFact]
    public void Attach_StoreChangedWhileDetached_ShowsCurrentProjectionWithOneSubscription()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        scene.Detach();
        scene.Publish("published while hidden");
        Assert.Equal(0, scene.PendingOperationCount);

        scene.Attach();

        Assert.Equal(1, store.Store.ChangedSubscriberCount);
        Assert.Equal(5, scene.View.ItemsHost!.ItemsSource.Count);
        Assert.Same(scene.Controller.Projection, scene.View.Projection);
        Assert.Equal(5, scene.Controller.Projection.EventCount);
        Assert.False(scene.EmptyState!.IsVisible);
    }

    [AvaloniaFact]
    public void Attach_StoreClearedWhileDetached_ShowsEmptyState()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        scene.Detach();
        store.Store.Clear();
        store.Fence();

        scene.Attach();

        Assert.Empty(scene.View.Projection!.Rows);
        Assert.True(scene.EmptyState!.IsVisible);
    }

    [AvaloniaFact]
    public void Attach_TwentyPageSwitches_KeepsSubscriptionAndPendingPostCounts()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        var expected = (store.Store.ChangedSubscriberCount, scene.PendingOperationCount);

        var observations = SwitchPages(scene, store);

        Assert.All(observations, observed => Assert.Equal(expected, observed));
        Assert.Equal(1, expected.ChangedSubscriberCount);
    }

    [AvaloniaFact]
    public void Attach_TwentyPageSwitches_ShowsLatestProjectionEachTime()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);

        var current = SwitchPagesAndCheckCurrent(scene);

        Assert.All(current, check => Assert.Equal((true, true, true), check));
    }

    [AvaloniaFact]
    public void Attach_StoreDisposedBeforeFirstAttach_ReportsRefreshError()
    {
        using var store = new ConsoleTestStore();
        using var controller = store.Controller();
        store.Store.Dispose();
        var toolbar = new ConsoleToolbar { Controller = controller };

        var window = ConsoleInteractionWindow.Create(toolbar);
        window.Close();

        Assert.IsType<ObjectDisposedException>(controller.RefreshError);
    }

    [AvaloniaFact]
    public void Detach_OneOfSeveralControls_KeepsSubscriptionAndRefreshes()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        var before = scene.Controller.Projection.LastSequence;
        var host = (Panel)scene.Toolbar!.Parent!;

        host.Children.Remove(scene.Toolbar!);
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Publish("https://example.test/after-toolbar");
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(1, store.Store.ChangedSubscriberCount);
        Assert.True(scene.Controller.Projection.LastSequence > before);
        Assert.Same(scene.Controller.Projection, scene.View.Projection);
    }

    [AvaloniaFact]
    public void Detach_LastOfSeveralControls_ReleasesSubscription()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        var host = (Panel)scene.Toolbar!.Parent!;

        host.Children.Remove(scene.Toolbar!);
        host.Children.Remove(scene.Header!);
        host.Children.Remove(scene.EmptyState!);
        host.Children.Remove(scene.View);
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
        Assert.Equal(0, scene.Controller.PendingOperationCount);
    }

    [AvaloniaFact]
    public void Attach_ControllerReplacedWhileAttached_MovesObservationToNewController()
    {
        using var store = new ConsoleTestStore();
        using var second = store.Controller();
        using var scene = new ConsoleInteractionScene(store: store);

        scene.View.Controller = second;
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(1, store.Store.ChangedSubscriberCount);
        Assert.Equal(0, scene.Controller.PendingOperationCount);
    }

    [AvaloniaFact]
    public void Detach_ControllerReplacedWhileAttached_ReleasesNewController()
    {
        using var store = new ConsoleTestStore();
        using var second = store.Controller();
        using var scene = new ConsoleInteractionScene(store: store);
        scene.View.Controller = second;
        ConsoleInteractionWindow.Pump(scene.Window);

        scene.Detach();

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
    }

    [AvaloniaFact]
    public void Attach_ControllerClearedWhileAttached_ReleasesSubscription()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(store: store);

        scene.View.Controller = null;
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
    }

    [AvaloniaFact]
    public void Attach_ViewWithoutController_LeavesStoreUnobserved()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(store: store);
        scene.View.Controller = null;
        ConsoleInteractionWindow.Pump(scene.Window);

        scene.Detach();
        scene.Attach();

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
    }

    [AvaloniaFact]
    public void Dispose_DetachedController_IgnoresLaterPublish()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        scene.Detach();
        var before = scene.Controller.Projection.Version;
        scene.Controller.Dispose();
        scene.Controller.Dispose();

        scene.Publish("https://example.test/after-dispose");
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(before, scene.Controller.Projection.Version);
        Assert.Equal(0, store.Store.ChangedSubscriberCount);
        Assert.Equal(0, scene.Controller.PendingOperationCount);
        Assert.False(scene.Controller.ClearCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Attach_StoreDisposedWhileDetached_LeavesControllerDetached()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        var before = scene.Controller.Projection;
        scene.Detach();
        store.Store.Dispose();

        scene.Attach();

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
        Assert.Equal(0, scene.Controller.PendingOperationCount);
        Assert.Same(before, scene.View.Projection);
        Assert.Equal(before.RowCount, scene.View.ItemsHost!.ItemsSource.Count);
    }

    [AvaloniaFact]
    public void Detach_UnreferencedViews_AllowsCollectionWithLiveController()
    {
        using var store = new ConsoleTestStore();
        using var controller = store.Controller();
        var detached = CreateDetachedScene(store, controller);

        CollectViews();

        Assert.False(detached.List.IsAlive);
        Assert.False(detached.Toolbar.IsAlive);
        Assert.False(detached.Header.IsAlive);
        Assert.False(detached.EmptyState.IsAlive);
        Assert.Equal(0, store.Store.ChangedSubscriberCount);
        Assert.True(controller.ClearCommand.CanExecute(null));
        GC.KeepAlive(controller);
    }

    [AvaloniaFact]
    public void Dispose_AttachedController_ReleasesSubscriptionAndPendingPosts()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        scene.Controller.SetSearchText("example");

        scene.Controller.Dispose();

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
        Assert.Equal(0, scene.Controller.PendingOperationCount);
        Assert.False(scene.Controller.ClearCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Dispose_RepeatedCall_IsHarmless()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(composed: true, store: store);
        scene.Controller.Dispose();

        scene.Controller.Dispose();
        scene.Publish("store remains owned by host");
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Equal(0, store.Store.ChangedSubscriberCount);
        Assert.Equal(0, scene.Controller.PendingOperationCount);
        using var snapshot = store.Store.CaptureSnapshot();
        Assert.Equal(5, snapshot.EventCount);
    }

    [AvaloniaFact]
    public void Attach_FollowingState_ContinuesFollowing()
    {
        using var store = new ConsoleTestStore();
        using var scene = new ConsoleInteractionScene(count: 40, composed: true, store: store);
        scene.Detach();
        scene.Publish("latest");

        scene.Attach();

        Assert.IsType<ConsoleFollow.Following>(scene.Controller.ViewState.Follow);
        Assert.True(scene.View.ItemsHost!.AtEnd);
    }

    [AvaloniaFact]
    public void Attach_PausedState_RestoresAcceptedAnchor()
    {
        using var scene = new ConsoleInteractionScene(count: 40, composed: true);
        scene.Controller.Pause(new(2), pixelOffset: 5);
        ConsoleInteractionWindow.Pump(scene.Window);
        var paused = Assert.IsType<ConsoleFollow.Paused>(scene.Controller.ViewState.Follow);
        scene.Detach();

        scene.Attach();

        var current = Assert.IsType<ConsoleFollow.Paused>(scene.Controller.ViewState.Follow);
        Assert.Equal(paused.PausedAt, current.PausedAt);
        Assert.Equal(paused.Anchor with { RowOrder = current.Anchor.RowOrder }, current.Anchor);
        Assert.Equal(paused.Anchor.RowOrder.ToArray(), current.Anchor.RowOrder.ToArray());
        var restored = scene.View.ItemsHost!.CaptureAnchor();
        Assert.Equal(paused.Anchor.RowId, restored!.RowId);
        Assert.Equal(paused.Anchor.PixelOffset, restored.PixelOffset);
    }

    [AvaloniaFact]
    public void Attach_ExpandedState_PreservesExpansion()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { ExpandedIds = [new(1)] });
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Detach();

        scene.Attach();

        Assert.Equal([new ConsoleRowId(1)], scene.Controller.ViewState.ExpandedIds);
        Assert.True(scene.Row(1).MessageText.Bounds.Height > 20);
    }

    [AvaloniaFact]
    public void Attach_SelectedState_PreservesSelection()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1, 3] });
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Detach();

        scene.Attach();

        Assert.Equal([1L, 3L], scene.Controller.ViewState.Selection.Order());
        Assert.Same(scene.Controller.ViewState, scene.View.ViewState);
    }

    [AvaloniaFact]
    public void Detach_HoveredLink_ClearsHoverAndTooltip()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        var message = scene.Row(1).MessageText;
        scene.Window.MouseMove(scene.MessagePoint(1));
        Assert.NotNull(message.HoveredTarget);

        scene.Detach();

        Assert.Null(message.HoveredTarget);
        Assert.Null(ToolTip.GetTip(message));
    }

    [AvaloniaFact]
    public void Detach_PressedLink_ReleasesPointerCapture()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        IPointer? pointer = null;
        scene.View.PointerPressed += (_, args) => pointer = args.Pointer;
        scene.Window.MouseDown(scene.MessagePoint(1), MouseButton.Left, RawInputModifiers.Control);
        Assert.NotNull(pointer!.Captured);

        scene.Detach();

        Assert.Null(pointer.Captured);
    }

    [AvaloniaFact]
    public void Attach_PressedLink_DoesNotActivateStaleGesture()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Window.MouseDown(scene.MessagePoint(1), MouseButton.Left, RawInputModifiers.Control);
        scene.Detach();

        scene.Attach();
        scene.Window.MouseUp(scene.MessagePoint(1), MouseButton.Left, RawInputModifiers.Control);

        Assert.Empty(scene.Adapters.Opened);
        Assert.Empty(scene.Controller.ViewState.Selection);
    }

    [AvaloniaFact]
    public void Attach_DraggedLink_DoesNotActivateStaleGesture()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        var point = scene.MessagePoint(1);
        scene.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
        scene.Window.MouseMove(point + new Vector(8, 0), RawInputModifiers.Control);
        scene.Detach();

        scene.Attach();
        scene.Window.MouseUp(scene.MessagePoint(1), MouseButton.Left, RawInputModifiers.Control);

        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void Detach_DeferredUserScroll_AbortsPendingIntent()
    {
        using var content = new ConsoleMeasureContent("first\nsecond\nthird\nfourth");
        using var scene = new ConsoleInteractionScene(count: 40, content: content, composed: true);
        BeginDeferredScroll(scene, content);
        var session = scene.View.AttachmentSession!;
        var operation = session.DeferredOperation!;

        scene.Detach();
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.Null(session.DeferredOperation);
        Assert.False(session.UserScrollPending);
        Assert.Equal(global::Avalonia.Threading.DispatcherOperationStatus.Aborted, operation.Status);
    }

    [AvaloniaFact]
    public void Detach_SharedStore_DoesNotStopOtherSceneUpdating()
    {
        using var store = new ConsoleTestStore();
        using var hidden = new ConsoleInteractionScene(composed: true, store: store);
        using var visible = new ConsoleInteractionScene(count: 0, composed: true, store: store);
        var hiddenVersion = hidden.Controller.Projection.Version;

        hidden.Detach();
        visible.Publish("other scene stays live");
        ConsoleInteractionWindow.Pump(visible.Window);

        Assert.Equal(1, store.Store.ChangedSubscriberCount);
        Assert.Equal(hiddenVersion, hidden.Controller.Projection.Version);
        Assert.Equal(5, visible.View.ItemsHost!.ItemsSource.Count);
        Assert.Equal(0, hidden.PendingOperationCount);
    }

    private static (int Subscribers, int Posts)[] SwitchPages(ConsoleInteractionScene scene, ConsoleTestStore store)
    {
        var observations = new (int, int)[20];
        for (var index = 0; index < observations.Length; index++)
        {
            scene.Controller.SetSearchText(index % 2 == 0 ? "example" : "");
            scene.Detach();
            Assert.Equal(0, store.Store.ChangedSubscriberCount);
            Assert.Equal(0, scene.PendingOperationCount);
            scene.Publish("https://example.test/new");
            scene.Attach();
            observations[index] = (store.Store.ChangedSubscriberCount, scene.PendingOperationCount);
        }
        return observations;
    }

    private static (bool SameProjection, bool Advanced, bool ItemsMatch)[] SwitchPagesAndCheckCurrent(ConsoleInteractionScene scene)
    {
        var current = new (bool, bool, bool)[20];
        var lastSequence = scene.Controller.Projection.LastSequence;
        for (var index = 0; index < current.Length; index++)
        {
            scene.Controller.SetSearchText(index % 2 == 0 ? "example" : "");
            scene.Detach();
            scene.Publish($"https://example.test/new/{index}");
            scene.Attach();
            var projection = scene.Controller.Projection;
            current[index] = (ReferenceEquals(projection, scene.View.Projection),
                projection.LastSequence > lastSequence,
                scene.View.ItemsHost!.ItemsSource.Count == projection.RowCount);
            lastSequence = projection.LastSequence;
        }
        return current;
    }

    private static void BeginDeferredScroll(ConsoleInteractionScene scene, ConsoleMeasureContent content)
    {
        var host = scene.View.ItemsHost!;
        host.Offset = default;
        scene.Controller.RequestViewState(scene.Controller.ViewState with { ExpandedIds = [new(1)] });
        content.OnRead = () => host.Offset = new(0, 405);
        host.Measure(host.Viewport);
        Assert.True(scene.View.UserScrollPending);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DetachedScene CreateDetachedScene(ConsoleTestStore store, ConsoleController controller)
    {
        using var scene = new ConsoleInteractionScene(composed: true, store: store, controller: controller);
        scene.Controller.SetSearchText("example");
        scene.Detach();
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Window.Close();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        ConsoleTestView.Pump();
        return new(new(scene.View), new(scene.Toolbar!), new(scene.Header!), new(scene.EmptyState!));
    }

    private static void CollectViews()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            ConsoleTestView.Pump();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed record DetachedScene(WeakReference List, WeakReference Toolbar, WeakReference Header, WeakReference EmptyState);
}
