// Copyright (c) 2026 Dennis Liu. All rights reserved.


using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Characterizes exception delivery in the repository's headless dispatcher.</summary>
public sealed class ConsoleDispatcherTests
{
    /// <summary>InvokeAsync captures a delegate failure in the returned task.</summary>
    [AvaloniaFact]
    public async Task InvokeAsyncThrowingDelegateFaultsReturnedTask()
    {
        var error = new InvalidOperationException("delegate");
        var task = Dispatcher.UIThread.InvokeAsync((Action)(() => throw error), DispatcherPriority.Background).GetTask();
        ConsoleTestView.Pump();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken)));
    }

    /// <summary>A captured InvokeAsync failure does not also enter the unhandled event.</summary>
    [AvaloniaFact]
    public async Task InvokeAsyncThrowingDelegateDoesNotRaiseUnhandledException()
    {
        using var failures = new ConsoleDispatcherExceptionScope();
        var task = Dispatcher.UIThread.InvokeAsync((Action)(() => throw new InvalidOperationException("delegate")),
            DispatcherPriority.Background).GetTask();
        ConsoleTestView.Pump();
        _ = await Record.ExceptionAsync(() => task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        Assert.Empty(failures.Errors);
    }

    /// <summary>Post sends a delegate failure to the handled unhandled event once.</summary>
    [AvaloniaFact]
    public void PostThrowingDelegateRaisesUnhandledExceptionOnce()
    {
        using var failures = new ConsoleDispatcherExceptionScope();
        var error = new InvalidOperationException("delegate");
        Dispatcher.UIThread.Post(() => throw error);
        ConsoleTestView.Pump();
        Assert.Same(error, Assert.Single(failures.Errors));
    }
}
