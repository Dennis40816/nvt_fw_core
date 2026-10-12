// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

// Availability reads the published projection and captures nothing; Execute validates against a fresh capture.
public sealed class ConsoleCommandAvailabilityTests
{
    [AvaloniaFact]
    public void CopySelectionCanExecuteDoesNotCaptureCommandProjection()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        ConsoleInteractionWindow.Pump(scene.Window);
        var captures = scene.Controller.CommandProjectionCaptures;

        var available = scene.Controller.CopySelectionCommand.CanExecute(null);

        Assert.True(available);
        Assert.Equal(captures, scene.Controller.CommandProjectionCaptures);
    }

    [AvaloniaFact]
    public void RowMenuBuildDoesNotCaptureCommandProjection()
    {
        using var scene = new ConsoleInteractionScene();
        var captures = scene.Controller.CommandProjectionCaptures;

        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);

        Assert.NotEmpty(menu.Items);
        Assert.Equal(captures, scene.Controller.CommandProjectionCaptures);
    }

    [AvaloniaFact]
    public void RowMenuItemCanExecuteDoesNotCaptureCommandProjection()
    {
        using var scene = new ConsoleInteractionScene();
        var menu = ConsoleMenuBuilder.Row(scene.View, scene.Controller.Projection.Rows[0], null);
        var captures = scene.Controller.CommandProjectionCaptures;

        var available = menu.Items.OfType<MenuItem>().Select(item => item.Command!.CanExecute(null)).ToArray();

        Assert.Contains(true, available);
        Assert.Equal(captures, scene.Controller.CommandProjectionCaptures);
    }

    [AvaloniaFact]
    public void RefreshWithBoundCopyCommandDoesNotCaptureCommandProjection()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        ConsoleInteractionWindow.Pump(scene.Window);
        var command = scene.Controller.CopySelectionCommand;
        var queries = 0;
        command.CanExecuteChanged += (_, _) =>
        {
            queries++;
            command.CanExecute(null);
        };
        var captures = scene.Controller.CommandProjectionCaptures;

        scene.Controller.SetSearchText("example");
        ConsoleInteractionWindow.Pump(scene.Window);

        Assert.True(queries > 0);
        Assert.Equal(captures, scene.Controller.CommandProjectionCaptures);
    }

    [AvaloniaFact]
    public void CopySelectionExecuteCapturesProjectionForFreshValidation()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        ConsoleInteractionWindow.Pump(scene.Window);
        var captures = scene.Controller.CommandProjectionCaptures;

        scene.Controller.CopySelectionCommand.Execute(null);

        Assert.True(scene.Controller.CommandProjectionCaptures > captures);
        Assert.Single(scene.Adapters.Copied);
    }

    [AvaloniaFact]
    public void CopyAvailabilityIsFalseAfterDispose()
    {
        using var scene = new ConsoleInteractionScene();
        scene.Controller.RequestViewState(scene.Controller.ViewState with { Selection = [1] });
        ConsoleInteractionWindow.Pump(scene.Window);
        var command = scene.Controller.RowCommands.Create(ConsoleRowAction.CopyMessage, new(1));

        scene.Controller.Dispose();

        Assert.False(command.CanExecute(null));
        Assert.False(scene.Controller.CopySelectionCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void LinksAfterDisposeHaveNoSpansAndDoNotThrow()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        var row = scene.Controller.Projection.Rows[0];
        Assert.NotEmpty(scene.Controller.GetLinks(row).Spans);

        scene.Controller.Dispose();

        Assert.Empty(scene.Controller.GetLinks(row).Spans);
    }
}
