// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleStateFenceTests
{
    [AvaloniaFact]
    public void SearchRefreshPublishesAfterExportTimeChange()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        using var controller = fixture.Controller();
        var reads = content.ReadCount;
        controller.SetSearchText("missing");
        content.OnRead = () => controller.ToggleExportTimeCommand.Execute(null);
        ConsoleTestView.Pump();
        Assert.Empty(controller.Projection.Rows);
        Assert.Equal(2, content.ReadCount - reads);
    }

    [AvaloniaFact]
    public void SearchRefreshPublishesAfterExportLevelChange()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        using var controller = fixture.Controller();
        var reads = content.ReadCount;
        controller.SetSearchText("missing");
        content.OnRead = () => controller.ToggleExportLevelCommand.Execute(null);
        ConsoleTestView.Pump();
        Assert.Empty(controller.Projection.Rows);
        Assert.Equal(2, content.ReadCount - reads);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void SearchRefreshBoundsRepeatedExportChanges(int option)
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        var command = new[] { controller.ToggleExportTimeCommand, controller.ToggleExportLevelCommand }[option];
        Action? onRead = null;
        onRead = () => { command.Execute(null); content.OnRead = onRead; };
        var reads = content.ReadCount;
        content.OnRead = onRead;
        controller.SetSearchText("missing");
        ConsoleTestView.Pump();
        ConsoleTestView.Pump();
        Assert.Equal(2, content.ReadCount - reads);
        Assert.Same(previous, controller.Projection);
        Assert.Null(controller.RefreshError);
    }

    [AvaloniaFact]
    public void LinkCanExecuteCancelsShutdownDuringAvailability()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "https://example.test/1");
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(opener: adapters);
        var command = controller.RowCommands.Create(ConsoleRowAction.OpenLink, new(1),
            new(LinkKind.Url, "https://example.test/1"));
        adapters.OnAvailability = () => { controller.Dispose(); fixture.Store.Dispose(); };
        Assert.False(command.CanExecute(null));
        Assert.Empty(adapters.Opened);
    }

    [AvaloniaFact]
    public void LinkExecutionCancelsShutdownDuringAvailability()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "https://example.test/1");
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(opener: adapters);
        var command = controller.RowCommands.Create(ConsoleRowAction.OpenLink, new(1),
            new(LinkKind.Url, "https://example.test/1"));
        adapters.OnAvailability = () => adapters.OnAvailability =
            () => { controller.Dispose(); fixture.Store.Dispose(); };
        Assert.Null(Record.Exception(() => command.Execute(null)));
        Assert.Empty(adapters.Opened);
    }

    [AvaloniaFact]
    public void CopyCanExecuteCancelsShutdownDuringSearchRead()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(clipboard: adapters);
        var command = controller.RowCommands.Create(ConsoleRowAction.CopyMessage, new(1));
        controller.SetSearchText("message");
        content.OnRead = () => { controller.Dispose(); fixture.Store.Dispose(); };
        Assert.False(command.CanExecute(null));
        Assert.Empty(adapters.Copied);
    }

    [AvaloniaFact]
    public void SearchRefreshCancelsShutdownDuringContentRead()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleMeasureContent("message");
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(clipboard: adapters, opener: adapters);
        controller.SetSearchText("message");
        content.OnRead = () => { controller.Dispose(); fixture.Store.Dispose(); };
        Assert.Null(Record.Exception(() => ConsoleTestView.Pump()));
        Assert.Null(controller.RefreshError);
        Assert.Empty(adapters.Copied);
        Assert.Empty(adapters.Opened);
    }

    [AvaloniaFact]
    public void LinkCanExecuteCancelsStoreDisposalDuringAvailability()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "https://example.test/1");
        fixture.Fence();
        var adapters = new ConsoleInteractionAdapters();
        using var controller = fixture.Controller(opener: adapters);
        var command = controller.RowCommands.Create(ConsoleRowAction.OpenLink, new(1),
            new(LinkKind.Url, "https://example.test/1"));
        adapters.OnAvailability = fixture.Store.Dispose;
        Assert.False(command.CanExecute(null));
        Assert.Empty(adapters.Opened);
    }
}
