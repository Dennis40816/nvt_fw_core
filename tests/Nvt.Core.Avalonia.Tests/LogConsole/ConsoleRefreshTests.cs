// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CA1707 // Owner-required Method_Scenario_Expected test names.

using System.ComponentModel;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks refresh containment, scheduling, and lease retirement.</summary>
public sealed class ConsoleRefreshTests
{
    /// <summary>Invalid relative formatting is rejected before any event or mode switch.</summary>
    [AvaloniaFact]
    public void Constructor_MalformedRelativeTemplate_RejectsOptions()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var error = Assert.Throws<ArgumentException>(() => new ConsoleController(fixture.Store, [],
            new ConsoleProjectionOptions { RelativeTimeTemplate = "{" }));
        Assert.Equal("options", error.ParamName);
        Assert.Contains("RelativeTimeTemplate", error.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(error.InnerException);
    }

    /// <summary>A failed reader leaves borrowed output readable and the next valid refresh clears its error.</summary>
    [AvaloniaFact]
    public void Drain_SearchReaderFailure_PreservesProjectionAndRecovers()
    {
        using var fixture = new ConsoleTestStore();
        var previousContent = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", previousContent));
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        var content = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Warn, "app", content));
        fixture.Fence();
        content.ThrowOnRead = true;
        var notifications = new List<string?>();
        controller.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        controller.SetSearchText("text");
        Assert.Null(Record.Exception(() => ConsoleTestView.Pump()));
        Assert.Same(previous, controller.Projection);
        Assert.Equal("text", Assert.Single(previous.Rows).GetFirstLine().Text);
        Assert.IsType<IOException>(controller.RefreshError);
        Assert.Contains(nameof(ConsoleController.RefreshError), notifications);
        Assert.True(controller.ResetFiltersCommand.CanExecute(null));
        content.ThrowOnRead = false;
        controller.SetSearchText("tex");
        ConsoleTestView.Pump();
        Assert.Null(controller.RefreshError);
        Assert.Equal(2, controller.Projection.RowCount);
        Assert.Equal(2, notifications.Count(name => name == nameof(ConsoleController.RefreshError)));
        controller.ClearCommand.Execute(null);
        fixture.Fence();
        ConsoleTestView.Pump();
        fixture.Until(() => content.Disposed && previousContent.Disposed);
    }

    /// <summary>A failure after projection construction releases the fresh row leases.</summary>
    [AvaloniaFact]
    public void Drain_RemapFailure_DisposesFreshLeases()
    {
        using var fixture = new ConsoleTestStore();
        var content = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        var state = controller.ViewState;
        controller.RequestViewState(state with { ExpandedIds = null! });
        ConsoleTestView.Pump();
        Assert.Same(previous, controller.Projection);
        Assert.IsType<ArgumentNullException>(controller.RefreshError);
        controller.RequestViewState(state);
        controller.ClearCommand.Execute(null);
        fixture.Fence();
        ConsoleTestView.Pump();
        fixture.Until(() => content.Disposed);
        Assert.Null(controller.RefreshError);
    }

    /// <summary>A throwing error observer cannot prevent replacement notification or retire displayed output early.</summary>
    [AvaloniaFact]
    public async Task Drain_ThrowingRefreshErrorSubscriberOnRecovery_AnnouncesReplacementBeforeRetirement()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "text");
        fixture.Fence();
        using var controller = fixture.Controller();
        var displayed = controller.Projection;
        var previous = displayed;
        var previousContent = Assert.Single(previous.Rows).TextContent;
        var state = controller.ViewState;
        controller.RequestViewState(state with { ExpandedIds = null! });
        ConsoleTestView.Pump();
        Assert.IsType<ArgumentNullException>(controller.RefreshError);
        controller.RequestViewState(state);
        var notifications = new List<string?>();
        controller.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.Projection), () =>
        {
            Assert.Same(previous, displayed);
            var buffer = new char[4];
            previousContent.Read(0, buffer);
            Assert.Equal("text", new string(buffer));
            Assert.Null(controller.RefreshError);
            displayed = controller.Projection;
        });
        var observerError = new InvalidOperationException("observer");
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.RefreshError),
            () => throw observerError);
        var pendingRefresh = PendingRefresh(controller);
        ConsoleTestView.Pump();
        Assert.True(pendingRefresh.IsCompleted);
        Assert.Same(observerError, await Assert.ThrowsAsync<InvalidOperationException>(() => pendingRefresh));
        Assert.Equal([nameof(ConsoleController.ViewState), nameof(ConsoleController.Projection),
            nameof(ConsoleController.RefreshError)], notifications);
        Assert.Same(controller.Projection, displayed);
        Assert.NotSame(previous, displayed);
        ConsoleTestView.Pump();
        Assert.Throws<ObjectDisposedException>(() => previousContent.Read(0, new char[4]));
        Assert.Equal("text", Assert.Single(displayed.Rows).GetFirstLine().Text);
    }

    /// <summary>A throwing failure observer leaves the previous output owned and fresh leases released for recovery.</summary>
    [AvaloniaFact]
    public async Task Drain_ThrowingRefreshErrorSubscriberOnFailure_PreservesProjectionAndRecovers()
    {
        using var fixture = new ConsoleTestStore();
        var previousContent = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", previousContent));
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        var state = controller.ViewState;
        var content = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Warn, "app", content));
        fixture.Fence();
        controller.RequestViewState(state with { ExpandedIds = null! });
        var observerError = new InvalidOperationException("observer");
        PropertyChangedEventHandler observer = ConsoleTestView.OnProperty(nameof(ConsoleController.RefreshError),
            () => throw observerError);
        controller.PropertyChanged += observer;
        var pendingRefresh = PendingRefresh(controller);
        ConsoleTestView.Pump();
        Assert.True(pendingRefresh.IsCompleted);
        Assert.Same(observerError, await Assert.ThrowsAsync<InvalidOperationException>(() => pendingRefresh));
        Assert.Same(previous, controller.Projection);
        Assert.IsType<ArgumentNullException>(controller.RefreshError);
        Assert.True(controller.ResetFiltersCommand.CanExecute(null));
        controller.PropertyChanged -= observer;
        fixture.Store.Clear();
        fixture.Fence();
        fixture.Until(() => content.Disposed);
        Assert.False(previousContent.Disposed);
        Assert.Equal("text", Assert.Single(previous.Rows).GetFirstLine().Text);
        controller.RequestViewState(state);
        ConsoleTestView.Pump();
        Assert.Null(controller.RefreshError);
        Assert.NotSame(previous, controller.Projection);
        Assert.True(controller.Projection.IsEmpty);
        fixture.Until(() => previousContent.Disposed);
    }

    /// <summary>Throwing filter observers cannot prevent the already requested refresh.</summary>
    [AvaloniaFact]
    public void SetSearchText_ThrowingFilterSubscriber_RefreshesProjection()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "visible");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.Filter),
            () => throw new InvalidOperationException("observer"));
        Assert.Throws<InvalidOperationException>(() => controller.SetSearchText("missing"));
        ConsoleTestView.Pump();
        Assert.True(controller.Projection.IsEmpty);
    }

    /// <summary>View state refresh is queued before invoking observers.</summary>
    [AvaloniaFact]
    public void RequestViewState_ThrowingSubscriber_RefreshesProjection()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        PropertyChangedEventHandler observer = ConsoleTestView.OnProperty(nameof(ConsoleController.ViewState),
            () => throw new InvalidOperationException("observer"));
        controller.PropertyChanged += observer;
        Assert.Throws<InvalidOperationException>(() => controller.RequestViewState(controller.ViewState with { IsExpanded = false }));
        controller.PropertyChanged -= observer;
        ConsoleTestView.Pump();
        Assert.NotSame(previous, controller.Projection);
    }

    /// <summary>Paused row reordering carries every lease to the copy that Clear later retires.</summary>
    [AvaloniaFact]
    public void Clear_PausedDedupeChange_ReleasesTransferredContent()
    {
        using var fixture = new ConsoleTestStore();
        var content = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.Pause();
        ConsoleTestView.Pump();
        controller.ToggleDedupeCommand.Execute(null);
        ConsoleTestView.Pump();
        Assert.True(controller.Projection.Deduplicate);
        controller.ClearCommand.Execute(null);
        fixture.Fence();
        ConsoleTestView.Pump();
        fixture.Until(() => content.Disposed);
        Assert.True(content.Disposed);
    }

    // InvokeAsync faults its operation task; observing it needs no production API or blocking wait.
    private static Task PendingRefresh(ConsoleController controller)
        => ((DispatcherOperation)typeof(ConsoleController)
            .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!).GetTask();
}
