// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Xunit;
using Xunit.Sdk;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleCommandLifetimeTests
{
    [AvaloniaFact]
    public void DetachedMenuCommandReleasesViewWhileControllerStaysActive()
    {
        using var fixture = new ConsoleTestStore();
        fixture.AddPublishedEntries(1);
        using var controller = fixture.Controller();
        var retained = DetachedMenu(controller);
        Collect();
        Assert.False(retained.View.IsAlive);
        Assert.False(retained.Command.CanExecute(null));
        Assert.True(controller.ResetFiltersCommand.CanExecute(null));
        GC.KeepAlive(retained.Command);
    }

    [AvaloniaFact]
    public void DetachedCommandsReleaseViewAndDisposedController()
    {
        var retained = ReleasedCommands((window, controller) =>
        {
            window.Content = null;
            ConsoleInteractionWindow.Pump(window);
            controller.Dispose();
        });
        Collect();
        AssertCollected(retained);
        GC.KeepAlive(retained);
    }

    [AvaloniaFact]
    public void DisposedCommandsReleaseViewControllerAndSubscribers()
    {
        var retained = ReleasedCommands((window, controller) =>
        {
            controller.Dispose();
            controller.Dispose();
            window.Content = null;
            ConsoleInteractionWindow.Pump(window);
        });
        Collect();
        AssertCollected(retained);
        GC.KeepAlive(retained);
    }

    [AvaloniaFact]
    public void CollectionPolicyRejectsUnreleasedCommandReferences()
    {
        var retained = UnreleasedCommands();
        Collect();
        Assert.ThrowsAny<XunitException>(() => AssertCollected(retained));
        GC.KeepAlive(retained);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference View, ICommand Command) DetachedMenu(ConsoleController controller)
    {
        var view = new ConsoleListView { Controller = controller, Projection = controller.Projection };
        var window = ConsoleInteractionWindow.Create(view);
        try
        {
            var command = ConsoleMenuBuilder.Row(view, controller.Projection.Rows[0], null).Items.OfType<MenuItem>().First().Command!;
            command.CanExecuteChanged += (_, _) => view.InvalidateVisual();
            window.Content = null;
            ConsoleInteractionWindow.Pump(window);
            return (new(view), command);
        }
        finally { window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetainedCommands ReleasedCommands(Action<Window, ConsoleController> release)
    {
        RetainedCommands retained;
        using (var scene = new ConsoleInteractionScene())
        {
            var view = scene.View;
            var menu = ConsoleMenuBuilder.Row(view, scene.Controller.Projection.Rows[0], null);
            var rowCommand = menu.Items.OfType<MenuItem>().ElementAt(3).Command!;
            var selectionCommand = scene.Controller.CopySelectionCommand;
            rowCommand.CanExecuteChanged += (_, _) => view.InvalidateVisual();
            selectionCommand.CanExecuteChanged += (_, _) => view.InvalidateVisual();
            retained = new(new(view), new(scene.Controller), rowCommand, selectionCommand);
            release(scene.Window, scene.Controller);
        }
        // Drain headless window teardown before checking command ownership.
        ConsoleTestView.Pump();
        return retained;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetainedCommands UnreleasedCommands()
    {
        using var scene = new ConsoleInteractionScene();
        var view = scene.View;
        var controller = scene.Controller;
        // Simulate the old lifetime: delegates and subscribers still own the detached graph.
        var rowCommand = new ConsoleCopySelectionCommand(() => controller.ResetFiltersCommand.CanExecute(null),
            () => view.Toggle(new(1)), static _ => { });
        var selectionCommand = new ConsoleCopySelectionCommand(() => controller.CopySelectionCommand.CanExecute(null),
            static () => { }, static _ => { });
        selectionCommand.CanExecuteChanged += (_, _) => view.InvalidateVisual();
        return new(new(view), new(controller), rowCommand, selectionCommand);
    }

    private static void AssertCollected(RetainedCommands retained)
    {
        Assert.False(retained.View.IsAlive);
        Assert.False(retained.Controller.IsAlive);
        Assert.False(retained.Row.CanExecute(null));
        Assert.False(retained.Selection.CanExecute(null));
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private sealed record RetainedCommands(WeakReference View, WeakReference Controller, ICommand Row, ICommand Selection);
}
