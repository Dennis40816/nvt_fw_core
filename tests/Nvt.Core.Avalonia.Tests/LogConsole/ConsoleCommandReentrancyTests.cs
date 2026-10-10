// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleCommandReentrancyTests
{
    [AvaloniaFact]
    public void SelectionCopyRejectsSourceFilterDuringFormatting()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        content.OnRead = () => scene.Controller.SetSelectedSources(["dxf"]);
        scene.Controller.CopySelectionCommand.Execute(null);
        Assert.Empty(scene.Adapters.Copied);
    }

    [AvaloniaFact]
    public void SelectionCopyRejectsSelectionClearDuringFormatting()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        content.OnRead = () => scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [] });
        scene.Controller.CopySelectionCommand.Execute(null);
        Assert.Empty(scene.Adapters.Copied);
    }

    [AvaloniaFact]
    public void RowCopyRejectsFilterChangeDuringValidation()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.SetSearchText("message");
        // CanExecute, execution capture, formatting, then the final validation capture.
        content.OnRead = () => content.OnRead = () => content.OnRead = () =>
            content.OnRead = () => scene.Controller.SetSelectedSources(["dxf"]);
        scene.Controller.RowCommands.Create(ConsoleRowAction.CopyRow, new(1)).Execute(null);
        Assert.Empty(scene.Adapters.Copied);
        Assert.Equal(["dxf"], scene.Controller.Filter.SelectedSources);
    }

    [AvaloniaFact]
    public void MessageCopyRejectsSelectionChangeDuringFormatting()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        content.OnRead = () => scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        scene.Controller.RowCommands.Create(ConsoleRowAction.CopyMessage, new(1)).Execute(null);
        Assert.Empty(scene.Adapters.Copied);
    }

    [AvaloniaFact]
    public void SourceCopyRejectsFilterChangeDuringValidation()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.SetSearchText("message");
        // Source formatting reads no content: the third search read is final validation.
        content.OnRead = () => content.OnRead = () =>
            content.OnRead = () => scene.Controller.SetSelectedSources(["dxf"]);
        scene.Controller.RowCommands.Create(ConsoleRowAction.CopySource, new(1)).Execute(null);
        Assert.Empty(scene.Adapters.Copied);
        Assert.Equal(["dxf"], scene.Controller.Filter.SelectedSources);
    }

    [AvaloniaFact]
    public void RowCopyRejectsRestoredFilterDuringFormatting()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        content.OnRead = () =>
        {
            scene.Controller.SetSelectedSources(["dxf"]);
            scene.Controller.SetSelectedSources([]);
        };
        scene.Controller.RowCommands.Create(ConsoleRowAction.CopyRow, new(1)).Execute(null);
        Assert.Empty(scene.Adapters.Copied);
    }

    [AvaloniaFact]
    public void RowCopyRejectsHistoryPublicationDuringFormatting()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(clipboard: adapters);
        content.OnRead = () => { fixture.Store.Add(LogLevel.Info, "dxf", "later"); fixture.Fence(); };
        controller.RowCommands.Create(ConsoleRowAction.CopyRow, new(1)).Execute(null);
        Assert.Empty(adapters.Copied);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RowMenuCopyRejectsViewStateChangeDuringValidation(int item)
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.SetSearchText("message");
        ConsoleInteractionWindow.Pump(scene.Window);
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        content.OnRead = () => content.OnRead = () =>
            scene.Controller.RequestViewState(scene.Controller.ViewState with { ExpandedIds = [new(1)] });
        menu.Items.OfType<MenuItem>().ElementAt(item).Command!.Execute(null);
        Assert.Empty(scene.Adapters.Copied);
    }

    [AvaloniaFact]
    public void SingleLinkRejectsViewStateChangeDuringAvailability()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Adapters.OnAvailability = () => scene.Adapters.OnAvailability = () =>
            scene.Controller.RequestViewState(scene.Controller.ViewState with { ExpandedIds = [new(1)] });
        scene.Key(Key.Enter, RawInputModifiers.Control);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void RowMenuOpenRejectsSourceFilterDuringAvailability()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        scene.Adapters.OnAvailability = () => scene.Adapters.OnAvailability = () => scene.Controller.SetSelectedSources(["dxf"]);
        menu.Items.OfType<MenuItem>().Last().Command!.Execute(null);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void LinkChoiceRejectsSearchChangeDuringAvailability()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "https://example.test/first https://example.test/second");
        scene.Key(Key.Enter, RawInputModifiers.Control);
        var menu = Assert.IsType<MenuFlyout>(scene.View.RowInteraction!.OpenMenu);
        scene.Adapters.OnAvailability = () => scene.Adapters.OnAvailability = () => scene.Controller.SetSearchText("missing");
        menu.Items.OfType<MenuItem>().First().Command!.Execute(null);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void PointerLinkRejectsViewStateChangeDuringAvailability()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Adapters.OnAvailability = () => scene.Adapters.OnAvailability = () =>
            scene.Controller.RequestViewState(scene.Controller.ViewState with { ExpandedIds = [new(1)] });
        scene.Click(1, RawInputModifiers.Control);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void RowMenuLinkChoiceRejectsSearchChangeDuringAvailability()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "https://example.test/first https://example.test/second");
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        var choices = menu.Items.OfType<MenuItem>().Last();
        scene.Adapters.OnAvailability = () => scene.Adapters.OnAvailability = () => scene.Controller.SetSearchText("missing");
        choices.Items.OfType<MenuItem>().First().Command!.Execute(null);
        Assert.Empty(scene.Adapters.Opened);
    }

    [AvaloniaFact]
    public void OpenLinkRejectsSelectionChangeDuringContentRead()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("https://example.test/1");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(opener: adapters);
        content.OnRead = () => controller.RequestViewState(controller.ViewState with { Selection = [1] });
        controller.RowCommands.Create(ConsoleRowAction.OpenLink, new(1), new(LinkKind.Url, "https://example.test/1")).Execute(null);
        Assert.Empty(adapters.Opened);
    }

    [AvaloniaTheory]
    [InlineData(0, "message")]
    [InlineData(1, "12:00:00.000 [Info] [app] message")]
    [InlineData(2, "app")]
    public void CopyWithSearchDeliversExactlyOnceWithoutStateChange(int action, string expected)
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.SetSearchText("message");
        scene.Controller.RowCommands.Create((ConsoleRowAction)action, new(1)).Execute(null);
        Assert.Equal(expected, Assert.Single(scene.Adapters.Copied));
    }

    [AvaloniaFact]
    public void SelectionCopyDeliversExactlyOnceWithoutStateChange()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        scene.Controller.CopySelectionCommand.Execute(null);
        Assert.Equal("12:00:00.000 [Info] [app] message", Assert.Single(scene.Adapters.Copied));
    }

    [AvaloniaFact]
    public void LinkChoiceOpensExactlyOnceWithoutStateChange()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "https://example.test/first https://example.test/second");
        scene.Key(Key.Enter, RawInputModifiers.Control);
        var menu = Assert.IsType<MenuFlyout>(scene.View.RowInteraction!.OpenMenu);
        menu.Items.OfType<MenuItem>().Last().Command!.Execute(null);
        Assert.Equal(new(LinkKind.Url, "https://example.test/second"), Assert.Single(scene.Adapters.Opened));
    }
}
