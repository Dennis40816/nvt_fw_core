// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

internal static class ConsoleAccessibilitySupport
{
    internal static ConsoleListAutomationPeer List(ConsoleListView view)
        => Assert.IsType<ConsoleListAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(view));
    internal static ConsoleRowAutomationPeer Peer(ConsoleRowPresenter row)
        => Assert.IsType<ConsoleRowAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(row));
    internal static ConsoleRowPresenter Configure(ConsoleInteractionScene scene, bool hidden, bool repeated, bool expanded)
    {
        if (repeated) scene.Controller.ToggleDedupeCommand.Execute(null);
        ConsoleInteractionWindow.Pump(scene.Window);
        var row = ConsoleInteractionWindow.Host(scene.View).Children.OfType<ConsoleRowPresenter>()
            .First(item => string.Equals(item.Row!.SourceId, "app", StringComparison.Ordinal));
        scene.View.TimeMode = hidden ? ConsoleTimeMode.Hidden : ConsoleTimeMode.Absolute;
        if (expanded) scene.Controller.RequestViewState(scene.Controller.ViewState with { ExpandedIds = [row.RowId!.Value] });
        ConsoleInteractionWindow.Pump(scene.Window);
        return row;
    }
    internal static void Read(AutomationPeer peer)
    {
        _ = peer.GetAcceleratorKey(); _ = peer.GetAccessKey(); _ = peer.GetAutomationControlType();
        _ = peer.GetAutomationId(); _ = peer.GetBoundingRectangle(); _ = peer.GetChildren();
        _ = peer.GetClassName(); _ = peer.GetName(); _ = peer.GetLabeledBy(); _ = peer.GetLocalizedControlType();
        _ = peer.GetHelpText(); _ = peer.GetPlaceholderText(); _ = peer.GetLandmarkType(); _ = peer.GetHeadingLevel();
        _ = peer.GetItemType(); _ = peer.GetItemStatus(); _ = peer.GetParent();
        _ = peer.HasKeyboardFocus(); _ = peer.IsContentElement();
        _ = peer.IsControlElement(); _ = peer.IsEnabled(); _ = peer.IsKeyboardFocusable(); _ = peer.IsOffscreen();
        _ = peer.GetLiveSetting(); _ = peer.ToScreen(default);
        _ = peer.GetProvider<ISelectionItemProvider>(); _ = peer.GetProvider<IExpandCollapseProvider>();
        _ = peer.GetProvider<ISelectionProvider>(); _ = peer.GetProvider<IScrollProvider>();
    }
    internal static void AssertNeutral(ConsoleRowAutomationPeer row, ConsoleListAutomationPeer list,
        ISelectionProvider selection, IScrollProvider scroll)
    {
        Read(row); Read(list);
        Assert.Empty(row.GetName()); Assert.Empty(list.GetName());
        Assert.Empty(row.GetChildren()!); Assert.Empty(list.GetChildren()!);
        Assert.False(row.IsSelected); Assert.Null(row.SelectionContainer);
        Assert.Equal(ExpandCollapseState.LeafNode, row.ExpandCollapseState);
        Assert.Equal(0, row.PositionInSet); Assert.Equal(0, row.SizeOfSet);
        Assert.Empty(selection.GetSelection()); Assert.False(selection.CanSelectMultiple);
        Assert.False(selection.IsSelectionRequired); Assert.False(scroll.VerticallyScrollable);
        Assert.Equal(-1, scroll.VerticalScrollPercent); Assert.Equal(0, scroll.VerticalViewSize);
        Assert.Null(row.GetProvider<ISelectionItemProvider>()); Assert.Null(list.GetProvider<ISelectionProvider>());
    }
    internal static Border Ring(ConsoleRowPresenter row) => Assert.Single(
        AdornerLayer.GetAdornerLayer(row)!.Children.OfType<Border>(), ring => AdornerLayer.GetAdornedElement(ring) == row);
    internal static T Resource<T>(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out var value), key);
        return Assert.IsAssignableFrom<T>(value);
    }
    internal static ConsoleProjection Project(ConsoleRow row) => new()
    {
        Version = 1,
        Generation = 1,
        LastSequence = row.LastSequence,
        CapturedAt = row.Timestamp,
        TimeBase = row.Timestamp,
        Rows = [row],
        LevelCounts = ImmutableDictionary<LogLevel, int>.Empty,
        Sources = [],
        SourceCounts = ImmutableDictionary<string, int>.Empty,
        RetainedMembership = ImmutableDictionary<ConsoleRowId, ImmutableArray<long>>.Empty,
        EventCount = row.Count,
        NewSincePauseCount = 0,
        EvictedCount = 0,
        Deduplicate = false,
    };
    internal static ConsoleRow Row(ILogTextContent content, LogLevel level = LogLevel.Info)
    {
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        return new(new(1), level, "app", content, 0, [], [1], instant, instant, 1, [], "");
    }
}
