// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleLinkHoverTests
{
    [AvaloniaFact]
    public void RepeatedLinkHoverDoesNotReadContent()
    {
        using var content = new ConsoleMeasureContent("https://example.test/needle");
        using var scene = new ConsoleInteractionScene(count: 1, content: content);
        scene.Controller.SetSearchText("needle");
        ConsoleInteractionWindow.Pump(scene.Window);
        var point = scene.MessagePoint(1);
        scene.Window.MouseMove(point);
        var reads = content.ReadCount;
        scene.Window.MouseMove(point + new Vector(1, 0));
        scene.Window.MouseMove(point + new Vector(2, 0));
        scene.Window.MouseMove(point + new Vector(3, 0));
        Assert.Equal(reads, content.ReadCount);
    }

    [AvaloniaFact]
    public void RepeatedLinkHoverDoesNotCaptureCommandProjection()
    {
        using var content = new ConsoleMeasureContent("https://example.test/needle");
        using var scene = new ConsoleInteractionScene(count: 1, content: content);
        scene.Controller.SetSearchText("needle");
        ConsoleInteractionWindow.Pump(scene.Window);
        var point = scene.MessagePoint(1);
        scene.Window.MouseMove(point);
        var captures = scene.Controller.CommandProjectionCaptures;
        scene.Window.MouseMove(point + new Vector(1, 0));
        scene.Window.MouseMove(point + new Vector(2, 0));
        scene.Window.MouseMove(point + new Vector(3, 0));
        Assert.Equal(captures, scene.Controller.CommandProjectionCaptures);
    }
}
