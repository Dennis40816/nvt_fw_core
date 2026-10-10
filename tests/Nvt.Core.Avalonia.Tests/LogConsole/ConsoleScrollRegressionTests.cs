// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.LogConsole;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleScrollRegressionTests
{
    [AvaloniaFact]
    public void FollowingScrollToEndDuringMeasureSurvivesHeightCorrection()
    {
        using var content = new ConsoleMeasureContent(string.Concat(Enumerable.Repeat("line\n", 1000)));
        using var scene = new ConsoleScrollScene(1, content);
        scene.View.ViewState = new() { ExpandedIds = [new(1)] };
        scene.Host.ScrollToEnd();
        content.OnRead = () => ScrollToEndDuringMeasure(scene);
        scene.Host.Measure(scene.Host.Viewport);
        Assert.True(scene.Host.AtEnd);
        Assert.IsType<ConsoleFollow.Following>(scene.View.ViewState.Follow);
    }

    [AvaloniaFact]
    public void ThrowingDeferredDeliveryClearsPendingUserIntent()
    {
        using var content = new ConsoleMeasureContent("first\nsecond\nthird\nfourth");
        using var scene = new ConsoleScrollScene(100, content);
        BeginDeferredScroll(scene, content);
        var session = scene.View.AttachmentSession!;
        var delivery = session.DeferredOperation!;
        session.PendingOperation?.Abort();
        delivery.Priority = DispatcherPriority.Send;
        scene.View.ScrollProgrammatically(() => scene.Host.Offset = default);
        scene.View.Resources["Nvt.Font.Body.Size"] = 14d;
        content.OnRead = () => throw new InvalidOperationException("Injected delivery failure");
        scene.Window.Dispatcher.RunJobs();
        Assert.Throws<InvalidOperationException>(() => TaskBlock.UntilComplete(delivery.GetTask()));
        Assert.False(scene.View.UserScrollPending);
    }

    [AvaloniaFact]
    public void ResumeRetiresIgnoredPauseRequests()
    {
        using var scene = new ConsoleScrollScene(100);
        var requests = new List<ConsoleViewState>();
        scene.View.ViewStateRequested += (_, state) => requests.Add(state);
        scene.Host.Offset = new(0, 40);
        scene.Host.Offset = new(0, scene.Host.Extent.Height);
        scene.View.ScrollProgrammatically(() => scene.Host.Offset = new(0, 40));
        scene.Host.Offset = new(0, scene.Host.Extent.Height);
        Assert.Equal(2, requests.Count);
    }

    private static void ScrollToEndDuringMeasure(ConsoleScrollScene scene)
    {
        Assert.True(scene.Host.IsMeasuring);
        scene.Host.Offset = default;
        scene.Host.Offset = new(0, scene.Host.Extent.Height);
    }

    private static void BeginDeferredScroll(ConsoleScrollScene scene, ConsoleMeasureContent content)
    {
        scene.Host.Offset = default;
        scene.View.ViewState = new ConsoleViewState { ExpandedIds = [new(1)] }.Pause(scene.Projection, new(1));
        content.OnRead = () => scene.Host.Offset = new(0, 805);
        scene.Host.Measure(scene.Host.Viewport);
        Assert.True(scene.View.UserScrollPending);
    }
}
