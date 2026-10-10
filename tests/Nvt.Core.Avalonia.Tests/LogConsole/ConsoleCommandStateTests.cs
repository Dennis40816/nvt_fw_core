// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleCommandStateTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void CommandStateVersionAdvancesForChangedInput(int input)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var version = controller.CommandStateVersion;
        ChangeInput(input, fixture, controller)();
        Assert.True(controller.CommandStateVersion > version);
    }

    [AvaloniaFact]
    public void CommandStateVersionKeepsUnchangedInputs()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var version = controller.CommandStateVersion;
        controller.RequestViewState(controller.ViewState);
        controller.SetSearchText(controller.Filter.SearchText);
        Assert.Equal(version, controller.CommandStateVersion);
    }

    [AvaloniaFact]
    public void ProjectionCaptureRejectsStateChangeDuringSearchRead()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(content: content);
        scene.Controller.SetSearchText("message");
        content.OnRead = () => scene.Controller.SetSelectedSources(["dxf"]);
        using var projection = scene.Controller.CaptureCommandProjection();
        Assert.Null(projection);
    }

    private static Action ChangeInput(int input, ConsoleTestStore fixture, ConsoleController controller) => input switch
    {
        0 => () => controller.SetSelectedSources(["app"]),
        1 => () => controller.SetSearchText("message"),
        2 => () => controller.RequestViewState(controller.ViewState with { Selection = [1] }),
        3 => () => controller.RequestViewState(controller.ViewState with { ExpandedIds = [new(1)] }),
        4 => () => controller.Pause(),
        5 => () => controller.ToggleDedupeCommand.Execute(null),
        6 => () => controller.SetTimeModeCommand.Execute(ConsoleTimeMode.Hidden),
        7 => () => controller.ClearCommand.Execute(null),
        8 => () => PublishHistory(fixture),
        9 => () => controller.ToggleExportTimeCommand.Execute(null),
        _ => throw new ArgumentOutOfRangeException(nameof(input)),
    };

    private static void PublishHistory(ConsoleTestStore fixture)
    {
        fixture.Store.Add(LogLevel.Info, "app", "history");
        fixture.Fence();
    }
}
