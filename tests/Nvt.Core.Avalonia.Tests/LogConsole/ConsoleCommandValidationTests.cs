// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleCommandValidationTests
{
    [AvaloniaFact]
    public void RetainedCopyRejectsSourceFilterBeforeRefresh()
    {
        using var scene = new ConsoleInteractionScene();
        var command = scene.Controller.RowCommands.Create(ConsoleRowAction.CopyMessage, new(1));
        Assert.True(command.CanExecute(null));
        scene.Controller.SetSelectedSources(["dxf"]);
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        Assert.Empty(scene.Adapters.Copied);
        Assert.Contains(scene.Controller.Projection.Rows, row => row.Id == new ConsoleRowId(1));
    }

    [AvaloniaFact]
    public void RetainedOpenRejectsSearchFilterBeforeRefresh()
    {
        using var scene = new ConsoleInteractionScene();
        var command = scene.Controller.RowCommands.Create(ConsoleRowAction.OpenLink, new(1), new(LinkKind.Url, "https://example.test/1"));
        Assert.True(command.CanExecute(null));
        scene.Controller.SetSearchText("missing");
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        Assert.Empty(scene.Adapters.Opened);
        Assert.NotEmpty(scene.Controller.Projection.Rows);
    }

    [AvaloniaFact]
    public void RetainedCopyRejectsLevelFilterBeforeRefresh()
    {
        using var scene = new ConsoleInteractionScene();
        var command = scene.Controller.RowCommands.Create(ConsoleRowAction.CopyMessage, new(1));
        Assert.True(command.CanExecute(null));
        scene.Controller.ToggleLevelCommand.Execute(LogLevel.Info);
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        Assert.Empty(scene.Adapters.Copied);
        Assert.NotEmpty(scene.Controller.Projection.Rows);
    }

    [AvaloniaFact]
    public void RetainedRowCopyRejectsClearBeforeRefresh()
    {
        using var scene = new ConsoleInteractionScene();
        var command = scene.Controller.RowCommands.Create(ConsoleRowAction.CopyMessage, new(1));
        Assert.True(command.CanExecute(null));
        scene.Controller.ClearCommand.Execute(null);
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        Assert.Empty(scene.Adapters.Copied);
        Assert.NotEmpty(scene.Controller.Projection.Rows);
    }

    [AvaloniaFact]
    public void RetainedSelectionCopyRejectsSourceFilterBeforeRefresh()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        var command = scene.Controller.CopySelectionCommand;
        Assert.True(command.CanExecute(null));
        scene.Controller.SetSelectedSources(["dxf"]);
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        Assert.Empty(scene.Adapters.Copied);
        Assert.NotEmpty(scene.Controller.Projection.Rows);
    }

    [AvaloniaFact]
    public void RetainedSelectionCopyRejectsClearBeforeRefresh()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        var command = scene.Controller.CopySelectionCommand;
        Assert.True(command.CanExecute(null));
        scene.Controller.ClearCommand.Execute(null);
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        Assert.Empty(scene.Adapters.Copied);
        Assert.NotEmpty(scene.Controller.Projection.Rows);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void CopyRejectsClearDuringContentRead(int action)
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(clipboard: adapters);
        var command = controller.RowCommands.Create((ConsoleRowAction)action, new(1));
        content.OnRead = fixture.Store.Clear;
        command.Execute(null);
        Assert.Empty(adapters.Copied);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void CopyRejectsFilterChangeDuringContentRead(int action)
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(clipboard: adapters);
        var command = controller.RowCommands.Create((ConsoleRowAction)action, new(1));
        content.OnRead = () => controller.SetSelectedSources(["dxf"]);
        command.Execute(null);
        Assert.Empty(adapters.Copied);
    }

    [AvaloniaTheory]
    [InlineData(0, "message")]
    [InlineData(1, "12:00:00.000 [Info] [app] message")]
    public void CopyDeliversOnceWhenContentReadKeepsRowValid(int action, string expected)
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(clipboard: adapters);
        controller.RowCommands.Create((ConsoleRowAction)action, new(1)).Execute(null);
        Assert.Equal(expected, Assert.Single(adapters.Copied));
    }

}
