// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Nvt.Core.Avalonia.Icons;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleRowInteractionTests
{
    [AvaloniaFact]
    public void ControlClickOpensTheScannedUrlTarget()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1, RawInputModifiers.Control);
        Assert.Equal(new LinkTarget(LinkKind.Url, "https://example.test/1"), Assert.Single(scene.Adapters.Opened));
    }

    [AvaloniaFact]
    public void ControlClickPreservesFileLineAndColumn()
    {
        using var scene = new ConsoleInteractionScene(message: "C:\\Synthetic\\source.cs(142,18)");
        scene.Click(1, RawInputModifiers.Control);
        Assert.Equal(new LinkTarget(LinkKind.File, "C:\\Synthetic\\source.cs", 142, 18), Assert.Single(scene.Adapters.Opened));
    }

    [AvaloniaFact]
    public void RightClickOpensTheRowMenu()
    {
        using var scene = new ConsoleInteractionScene();
        var point = scene.MessagePoint(1);
        scene.Window.MouseDown(point, MouseButton.Right);
        scene.Window.MouseUp(point, MouseButton.Right);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.True(scene.View.RowInteraction!.OpenMenu?.IsOpen);
    }

    [AvaloniaFact]
    public void MultipleLinksOfferAKeyboardChoiceMenu()
    {
        using var scene = new ConsoleInteractionScene(message: "https://a.test/ https://b.test/");
        scene.Key(Key.Enter, RawInputModifiers.Control);
        Assert.Equal(2, scene.View.RowInteraction!.OpenMenu!.Items.Count);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void KeyboardFocusUsesTheCoreFocusAdorner()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.Down);
        Assert.True(scene.Row(2).IsFocused);
        Assert.NotNull(scene.Row(2).FocusAdorner);
    }

    [AvaloniaFact]
    public void PointerFocusDoesNotShowTheKeyboardRing()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        Assert.True(scene.Row(1).IsFocused);
        Assert.Null(scene.Row(1).FocusAdorner);
    }

    [AvaloniaFact]
    public void HoverShowsTheLinkTarget()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Window.MouseMove(scene.MessagePoint(1));
        ToolTip.SetIsOpen(scene.Row(1).MessageText, true);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Equal("https://example.test/1", scene.Row(1).MessageText.HoveredTarget?.Path);
        Assert.Equal("https://example.test/1", ToolTip.GetTip(scene.Row(1).MessageText));
    }

    [AvaloniaFact]
    public void UnavailableLinkCannotActivate()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Adapters.UnavailableReason = "File is unavailable";
        scene.Click(1, RawInputModifiers.Control);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void LinkDragCancelsActivation()
    {
        using var scene = new ConsoleInteractionScene();
        var point = scene.MessagePoint(1);
        scene.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
        scene.Window.MouseMove(point + new Vector(8, 0), RawInputModifiers.Control);
        scene.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void LinkReleaseOnAnotherTargetCannotActivate()
    {
        using var scene = new ConsoleInteractionScene(message: "https://a.test/ https://b.test/");
        var point = scene.MessagePoint(1);
        scene.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
        scene.Window.MouseUp(scene.MessagePoint(1, 160), MouseButton.Left, RawInputModifiers.Control);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void ControlEnterOpensTheUniqueLink()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.Enter, RawInputModifiers.Control);
        Assert.Single(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void ContextMenuContainsTheRowCommands()
    {
        using var scene = new ConsoleInteractionScene();
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        Assert.Equal(["Copy message", "Copy row", "Copy source", "Expand", "Filter by this source", "Filter by this level", "Open link"],
            menu.Items.OfType<MenuItem>().Select(item => item.Header).ToArray());
    }

    [AvaloniaFact]
    public void DisabledMenuItemExplainsItsReason()
    {
        using var scene = new ConsoleInteractionScene(clipboard: false);
        scene.View.RowInteraction!.ShowMenu(scene.Controller.Projection.Rows[0], null);
        ConsoleInteractionWindow.Pump(scene.Window);
        var menu = scene.View.RowInteraction.OpenMenu!;
        var item = menu.Items.OfType<MenuItem>().First();
        ToolTip.SetIsOpen(item, true);
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.False(item.IsEnabled);
        Assert.Equal("Clipboard is unavailable", ToolTip.GetTip(item));
        Assert.True(ToolTip.GetShowOnDisabled(item));
    }

    [AvaloniaFact]
    public void RowCopyMatchesTheExistingExportFormat()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Controller.RowCommands.Create(ConsoleRowAction.CopyRow, new(1)).Execute(null);
        Assert.Equal(ConsoleExportFormatter.FormatVisible(scene.Controller.Projection, scene.Controller.ExportOptions), Assert.Single(scene.Adapters.Copied));
    }

    [AvaloniaFact]
    public void CopyCommandEnablesItsConsumerAfterSelectionChanges()
    {
        using var scene = new ConsoleInteractionScene();
        var button = new Button { Command = scene.Controller.CopySelectionCommand };
        scene.Key(Key.A, RawInputModifiers.Control);
        Assert.True(button.IsEnabled);
    }

    [AvaloniaFact]
    public void CopyCommandObserversAreIsolated()
    {
        using var errors = new ConsoleDispatcherExceptionScope();
        using var scene = new ConsoleInteractionScene();
        var notified = 0;
        EventHandler failure = (_, _) => throw new InvalidOperationException("Injected observer failure");
        scene.Controller.CopySelectionCommand.CanExecuteChanged += failure;
        scene.Controller.CopySelectionCommand.CanExecuteChanged += (_, _) => notified++;
        scene.Key(Key.A, RawInputModifiers.Control);
        scene.Controller.CopySelectionCommand.CanExecuteChanged -= failure;
        Assert.True(notified > 0);
        Assert.NotEmpty(errors.Errors);
    }

    [AvaloniaFact]
    public void SourceCopyUsesTheStableSourceId()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RowCommands.Create(ConsoleRowAction.CopySource, new(1)).Execute(null);
        Assert.Equal("app", Assert.Single(scene.Adapters.Copied));
    }

    [AvaloniaFact]
    public void DisposedControllerCannotCopyThroughRetainedCommands()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.A, RawInputModifiers.Control);
        var command = scene.Controller.CopySelectionCommand;
        scene.Controller.Dispose();
        command.Execute(null);
        Assert.Empty(scene.Adapters.Copied);
    }

    [AvaloniaFact]
    public void ControlEndResumesFollowing()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        scene.Key(Key.End, RawInputModifiers.Control);
        Assert.IsType<ConsoleFollow.Following>(scene.Controller.ViewState.Follow);
    }

    [AvaloniaFact]
    public void RowMenuCopiesTheMessageThroughTheAdapter()
    {
        using var scene = new ConsoleInteractionScene();
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        menu.Items.OfType<MenuItem>().First().Command!.Execute(null);
        Assert.Equal("https://example.test/1\nsecond line", Assert.Single(scene.Adapters.Copied));
    }

    [AvaloniaFact]
    public void RowMenuToggleExpandsItsRow()
    {
        using var scene = new ConsoleInteractionScene();
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        menu.Items.OfType<MenuItem>().ElementAt(3).Command!.Execute(null);
        Assert.Contains(new ConsoleRowId(1), scene.Controller.ViewState.ExpandedIds);
    }

    [AvaloniaFact]
    public void RowMenuOpenUsesTheInjectedAdapter()
    {
        using var scene = new ConsoleInteractionScene();
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        menu.Items.OfType<MenuItem>().Last().Command!.Execute(null);
        Assert.Equal(new LinkTarget(LinkKind.Url, "https://example.test/1"), Assert.Single(scene.Adapters.Opened));
    }

    [AvaloniaFact]
    public void RowMenuFiltersByItsSource()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RowCommands.Create(ConsoleRowAction.FilterSource, new(1)).Execute(null);
        Assert.Equal("app", Assert.Single(scene.Controller.Filter.SelectedSources));
    }

    [AvaloniaFact]
    public void RowMenuFiltersByItsLevel()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RowCommands.Create(ConsoleRowAction.FilterLevel, new(1)).Execute(null);
        Assert.Equal(LogLevel.Info, Assert.Single(scene.Controller.Filter.EnabledLevels));
    }

    [AvaloniaFact]
    public void ClickReplacesThePreviousRowSelection()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        scene.Click(3);
        Assert.Equal([3L], scene.Controller.ViewState.Selection.ToArray());
    }

    [AvaloniaFact]
    public void ShiftClickSelectsTheInclusiveRange()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        scene.Click(3, RawInputModifiers.Shift);
        Assert.Equal([1L, 2L, 3L], scene.Controller.ViewState.Selection.Order().ToArray());
    }

    [AvaloniaFact]
    public void ControlClickTogglesAnIndependentRowSelection()
    {
        using var scene = new ConsoleInteractionScene(message: "message");
        scene.Click(1);
        scene.Click(3, RawInputModifiers.Control);
        Assert.Equal([1L, 3L], scene.Controller.ViewState.Selection.Order().ToArray());
    }

    [AvaloniaFact]
    public void ControlASelectsAllProjectedRawIdentities()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.A, RawInputModifiers.Control);
        Assert.Equal([1L, 2L, 3L, 4L], scene.Controller.ViewState.Selection.Order().ToArray());
    }

    [AvaloniaFact]
    public void CopyExcludesFilteredHiddenSelections()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.A, RawInputModifiers.Control);
        scene.Controller.SetSelectedSources(["app"]);
        ConsoleInteractionWindow.Pump(scene.Window);
        scene.Key(Key.C, RawInputModifiers.Control);
        Assert.DoesNotContain("[dxf]", Assert.Single(scene.Adapters.Copied), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void CopyIsBoundedForLargeMessages()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: new string('a', 200_000));
        scene.Key(Key.A, RawInputModifiers.Control);
        scene.Key(Key.C, RawInputModifiers.Control);
        Assert.Equal(ConsoleCopyText.MaximumCharacters, Assert.Single(scene.Adapters.Copied).Length);
    }

    [AvaloniaFact]
    public void EscapeClearsSelection()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.A, RawInputModifiers.Control);
        scene.Key(Key.Escape);
        Assert.Empty(scene.Controller.ViewState.Selection);
    }

    [AvaloniaFact]
    public void ShiftDownExtendsTheRange()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        scene.Key(Key.Down, RawInputModifiers.Shift);
        Assert.Equal([1L, 2L], scene.Controller.ViewState.Selection.Order().ToArray());
    }

    [AvaloniaTheory]
    [InlineData(Key.Down, 2)]
    [InlineData(Key.PageDown, 4)]
    [InlineData(Key.End, 4)]
    public void NavigationMovesTheFocusedRow(Key key, long expected)
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        scene.Key(key);
        Assert.Equal(expected, scene.View.RowInteraction!.FocusedEntry);
        Assert.True(scene.Row(expected).IsFocused);
    }

    [AvaloniaTheory]
    [InlineData(Key.Up, 3)]
    [InlineData(Key.PageUp, 1)]
    [InlineData(Key.Home, 1)]
    public void NavigationReturnsTowardTheFirstRow(Key key, long expected)
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(4);
        scene.Key(key);
        Assert.Equal(expected, scene.View.RowInteraction!.FocusedEntry);
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void ActivationTogglesExpansion(Key key)
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(key);
        Assert.Contains(new ConsoleRowId(1), scene.Controller.ViewState.ExpandedIds);
    }

    [AvaloniaFact]
    public void HeaderUsesAnExistingCatalogGlyph()
    {
        using var scene = new ConsoleInteractionScene();
        Assert.Equal(NvtIcons.DataObject, new ConsoleHeader().GetLogicalDescendants().OfType<TextBlock>()
            .First(label => label.Classes.Contains("nvtIcon")).Text);
    }

    [AvaloniaFact]
    public void ConflictingSourceRegistriesAreRejected()
    {
        using var store = new ConsoleTestStore();
        Assert.Throws<ArgumentException>(() => { using var rejected = store.Controller(new() { SourceRegistry = [new("other", "Other")] }); });
    }

    [AvaloniaFact]
    public void MatchingSourceRegistriesAreAccepted()
    {
        using var store = new ConsoleTestStore();
        using var controller = store.Controller(new() { SourceRegistry = [new("app", "Application"), new("dxf", "Drawing"), new("idle", "Idle")] });
        Assert.Equal(3, controller.Options.SourceRegistry.Length);
    }
    [AvaloniaFact]
    public void EscapeClearsRetainedSelectionWithNoVisibleRows()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Key(Key.A, RawInputModifiers.Control);
        scene.Controller.SetSearchText("missing");
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Empty(scene.Controller.Projection.Rows);
        Assert.NotEmpty(scene.Controller.ViewState.Selection);
        scene.Key(Key.Escape);
        Assert.Empty(scene.Controller.ViewState.Selection);
    }

    [AvaloniaFact]
    public void ControlEndResumesFollowingWithNoVisibleRows()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        scene.Controller.SetSearchText("missing");
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Empty(scene.Controller.Projection.Rows);
        Assert.IsType<ConsoleFollow.Paused>(scene.Controller.ViewState.Follow);
        scene.Key(Key.End, RawInputModifiers.Control);
        Assert.IsType<ConsoleFollow.Following>(scene.Controller.ViewState.Follow);
    }

    [AvaloniaTheory]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    public void ArrowKeysLeaveEmptyResultsUnchanged(Key key)
    {
        using var scene = new ConsoleInteractionScene();
        scene.Click(1);
        scene.Controller.SetSearchText("missing");
        ConsoleInteractionWindow.Pump(scene.Window);
        Assert.Empty(scene.Controller.Projection.Rows);
        var state = scene.Controller.ViewState;
        var focused = scene.View.RowInteraction!.FocusedEntry;
        scene.Key(key);
        Assert.Same(state, scene.Controller.ViewState);
        Assert.Equal(focused, scene.View.RowInteraction.FocusedEntry);
    }

}
