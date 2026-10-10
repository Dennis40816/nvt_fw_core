// Copyright (c) 2026 Dennis Liu. All rights reserved.


using System.ComponentModel;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks refresh containment, scheduling, and lease retirement.</summary>
public sealed class ConsoleRefreshTests
{
    /// <summary>Invalid relative formatting is rejected before any event or mode switch.</summary>
    [AvaloniaFact]
    public void ConstructorMalformedRelativeTemplateRejectsOptions()
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
    public void DrainSearchReaderFailurePreservesProjectionAndRecovers()
    {
        using var fixture = new ConsoleTestStore();
        using var previousContent = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", previousContent));
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        using var content = new ConsoleControllerTests.TrackedContent();
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
    public void DrainRemapFailureDisposesFreshLeases()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleControllerTests.TrackedContent();
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
    public void DrainThrowingRefreshErrorSubscriberOnRecoveryAnnouncesReplacementBeforeRetirement()
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
        using var failures = new ConsoleDispatcherExceptionScope();
        ConsoleTestView.Pump();
        Assert.Same(observerError, Assert.Single(failures.Errors));
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
    public void DrainThrowingRefreshErrorSubscriberOnFailurePreservesProjectionAndRecovers()
    {
        using var fixture = new ConsoleTestStore();
        using var previousContent = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", previousContent));
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        var state = controller.ViewState;
        using var content = new ConsoleControllerTests.TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Warn, "app", content));
        fixture.Fence();
        controller.RequestViewState(state with { ExpandedIds = null! });
        var observerError = new InvalidOperationException("observer");
        PropertyChangedEventHandler observer = ConsoleTestView.OnProperty(nameof(ConsoleController.RefreshError),
            () => throw observerError);
        controller.PropertyChanged += observer;
        using var failures = new ConsoleDispatcherExceptionScope();
        ConsoleTestView.Pump();
        Assert.Same(observerError, Assert.Single(failures.Errors));
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
    public void SetSearchTextThrowingFilterSubscriberRefreshesProjection()
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
    public void RequestViewStateThrowingSubscriberRefreshesProjection()
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
    public void ClearPausedDedupeChangeReleasesTransferredContent()
    {
        using var fixture = new ConsoleTestStore();
        using var content = new ConsoleControllerTests.TrackedContent();
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

    /// <summary>All subscribers of the same refresh notification contribute to one visible failure.</summary>
    [AvaloniaFact]
    public void DrainTwoThrowingSubscribersReportsBothFailuresOnce()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        using var failures = new ConsoleDispatcherExceptionScope();
        var first = new InvalidOperationException("first observer");
        var second = new IOException("second observer");
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.Projection), () => throw first);
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.Projection), () => throw second);
        controller.SetSearchText("refresh");
        ConsoleTestView.Pump();
        ConsoleTestView.Pump();
        var failure = Assert.IsType<AggregateException>(Assert.Single(failures.Errors));
        Assert.Equal([first, second], failure.InnerExceptions);
    }

    /// <summary>A lone observer failure retains its original stack and is reported only once.</summary>
    [AvaloniaTheory]
    [InlineData(nameof(ConsoleController.ViewState))]
    [InlineData(nameof(ConsoleController.Projection))]
    public void DrainThrowingSubscriberReportsFailureOnce(string propertyName)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        using var failures = new ConsoleDispatcherExceptionScope();
        var observerError = new InvalidOperationException("observer");
        controller.PropertyChanged += ConsoleTestView.OnProperty(propertyName, () => throw observerError);
        controller.SetSearchText("refresh");
        ConsoleTestView.Pump();
        ConsoleTestView.Pump();
        var failure = Assert.Single(failures.Errors);
        Assert.Same(observerError, failure);
        Assert.Contains(nameof(DrainThrowingSubscriberReportsFailureOnce), failure.StackTrace, StringComparison.Ordinal);
    }

    /// <summary>Even throwing observers on every property allow later subscribers to receive the full sequence.</summary>
    [AvaloniaFact]
    public void DrainThrowingSubscribersCompletesNotificationSequence()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var state = controller.ViewState;
        controller.RequestViewState(state with { ExpandedIds = null! });
        ConsoleTestView.Pump();
        controller.RequestViewState(state);
        using var failures = new ConsoleDispatcherExceptionScope();
        var notifications = new List<string?>();
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.ViewState),
            () => throw new InvalidOperationException("view state observer"));
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.Projection),
            () => throw new InvalidOperationException("projection observer"));
        controller.PropertyChanged += ConsoleTestView.OnProperty(nameof(ConsoleController.RefreshError),
            () => throw new InvalidOperationException("error observer"));
        controller.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        ConsoleTestView.Pump();
        Assert.Equal([nameof(ConsoleController.ViewState), nameof(ConsoleController.Projection),
            nameof(ConsoleController.RefreshError)], notifications);
    }

    /// <summary>The constructor validates the same string argument that relative-time projection formats.</summary>
    [AvaloniaFact]
    public void ConstructorStringRelativeTemplateAcceptsOptions()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller(new ConsoleProjectionOptions { RelativeTimeTemplate = "{0:X}" });
        Assert.Equal("{0:X}", controller.Options.RelativeTimeTemplate);
    }
}
