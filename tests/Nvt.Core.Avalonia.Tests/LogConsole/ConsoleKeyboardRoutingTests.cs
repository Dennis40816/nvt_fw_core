// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleKeyboardRoutingTests
{
    [AvaloniaFact]
    public void ShiftF10OpensFocusedRowMenu()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Click(3);
        scene.SendKey(Key.F10, RawInputModifiers.Shift);
        var menu = Assert.IsType<MenuFlyout>(scene.View.RowInteraction!.OpenMenu);
        Assert.True(menu.IsOpen);
        menu.Items.OfType<MenuItem>().ElementAt(2).Command!.Execute(null);
        Assert.Equal("app", Assert.Single(scene.Adapters.Copied));
        Assert.Equal(3, scene.View.RowInteraction.FocusedEntry);
    }

    [AvaloniaFact]
    public void MenuKeyOpensFocusedRowMenu()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Click(2);
        scene.SendKey(Key.Apps);
        var menu = Assert.IsType<MenuFlyout>(scene.View.RowInteraction!.OpenMenu);
        Assert.True(menu.IsOpen);
        menu.Items.OfType<MenuItem>().ElementAt(2).Command!.Execute(null);
        Assert.Equal("dxf", Assert.Single(scene.Adapters.Copied));
        Assert.Equal(2, scene.View.RowInteraction.FocusedEntry);
    }

    [AvaloniaFact]
    public void EscapeClosesMenuBeforeClearingSelection()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Click(2);
        scene.SendKey(Key.Apps);
        var menu = Assert.IsType<MenuFlyout>(scene.View.RowInteraction!.OpenMenu);
        Assert.True(menu.IsOpen);
        scene.SendKey(Key.Escape);
        Assert.False(menu.IsOpen);
        Assert.Equal([2L], scene.Controller.ViewState.Selection);
    }

    [AvaloniaFact]
    public void SearchControlASelectsTextWithoutSelectingRows()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Controller.SetSearchText("example");
        ConsoleInteractionWindow.Pump(scene.Window);
        var search = scene.Header!.FindControl<TextBox>("SearchBox")!;
        Assert.True(search.Focus());
        search.SelectionStart = 0;
        search.SelectionEnd = 0;
        scene.SendKey(Key.A, RawInputModifiers.Control);
        Assert.Equal("example", search.SelectedText);
        Assert.Empty(scene.Controller.ViewState.Selection);
        Assert.NotEmpty(scene.Controller.Projection.Rows);
    }

    [AvaloniaFact]
    public async Task SearchControlCCopiesTextWithoutCopyingRows()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Click(2);
        scene.Controller.SetSearchText("example");
        ConsoleInteractionWindow.Pump(scene.Window);
        var search = scene.Header!.FindControl<TextBox>("SearchBox")!;
        Assert.True(search.Focus());
        search.SelectAll();
        var copied = new List<string>();
        var signal = new SignalWait("TextBox copy", TimeSpan.FromSeconds(60));
        search.CopyingToClipboard += (_, args) => { copied.Add(search.SelectedText); args.Handled = true; signal.Set(); };
        scene.SendKey(Key.C, RawInputModifiers.Control);
        await signal.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("example", Assert.Single(copied));
        Assert.Empty(scene.Adapters.Copied);
        Assert.Equal([2L], scene.Controller.ViewState.Selection);
        Assert.NotEmpty(scene.Controller.Projection.Rows);
    }

    [AvaloniaFact]
    public void ToolbarShortcutRunsWithListFocused()
    {
        using var scene = new ConsoleInteractionScene(composed: true);
        scene.Window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new(Key.D, KeyModifiers.Control),
            Command = scene.Controller.ToggleDedupeCommand,
        });
        scene.Click(2);
        Assert.True(scene.Row(2).IsFocused);
        Assert.False(scene.Controller.Filter.Deduplicate);
        scene.SendKey(Key.D, RawInputModifiers.Control);
        Assert.True(scene.Controller.Filter.Deduplicate);
        Assert.True(scene.Toolbar!.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("Dedupe")!.IsChecked);
    }
}
