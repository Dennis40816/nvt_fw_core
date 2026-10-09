// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Nvt.Core.RuntimeQuery;
using Nvt.Core.Avalonia.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

public sealed partial class RuntimeQueryGenericCommandsTests
{
    /// <summary>Cancelled invocations stop every generic command before adapters or effects run.</summary>
    [AvaloniaTheory]
    [InlineData("help")]
    [InlineData("ping")]
    [InlineData("focus")]
    [InlineData("page")]
    [InlineData("screenshot")]
    [InlineData("exit")]
    public async Task CancelledGenericHandlerNeverStartsWork(string name)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = Options() with
        {
            GetMainWindow = () => throw new InvalidOperationException("Window lookup must not run."),
            GetCommands = () => throw new InvalidOperationException("Help lookup must not run."),
            DecideExit = () => throw new InvalidOperationException("Exit decision must not run."),
            CaptureScreenshot = (_, _) => throw new InvalidOperationException("Capture must not run.")
        };
        var command = RuntimeQueryGenericCommands.Create(options).Single(command => command.Name == name);
        var exception = await Record.ExceptionAsync(() => command.Handler(RuntimeQueryInvocation.Runtime, null, cancellation.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    /// <summary>Custom capture receives the invocation token and cancels its own pending frame wait.</summary>
    [AvaloniaFact]
    public async Task ScreenshotCaptureObservesCancellationAfterStarting()
    {
        using var workspace = new ScreenshotWorkspace();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options() with
        {
            CaptureScreenshot = async (_, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return RuntimeQueryScreenshotResult.Success(1, 1, 1);
            }
        };
        var execution = Router(options).RouteAsync("screenshot", Arg("path", workspace.PathFor("capture.png")), cancellation.Token);
        await entered.Task.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
    }

    /// <summary>Cancellation after a custom capture commits its file preserves cleanup, response data and the committed file.</summary>
    [AvaloniaFact]
    public async Task ScreenshotCommittedCaptureIgnoresCancellationDuringCleanup()
    {
        using var workspace = new ScreenshotWorkspace();
        using var cancellation = new CancellationTokenSource();
        var path = workspace.PathFor("capture.png");
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var options = Options() with
        {
            CaptureScreenshot = async (destination, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                File.WriteAllBytes(destination, [1, 2, 3]);
                committed.SetResult();
                await release.Task.WaitAsync(TestContext.Current.CancellationToken);
                cleaned = true;
                return RuntimeQueryScreenshotResult.Success(7, 11, 3);
            }
        };
        var execution = Router(options).RouteAsync("screenshot", Arg("path", path), cancellation.Token);
        try
        {
            await committed.Task.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            cancellation.Cancel();
            Assert.False(execution.IsCompleted);
            release.SetResult();
            var response = await execution.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            Assert.True(response.Ok);
            Assert.True(cleaned);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(workspace.DirectoryPath));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    /// <summary>Cancellation during default rendering stops the file write and leaves no temporary files.</summary>
    [AvaloniaFact]
    public async Task ScreenshotDefaultCaptureCancelsBeforeFileCommit()
    {
        using var workspace = new ScreenshotWorkspace();
        using var cancellation = new CancellationTokenSource();
        var window = CaptureWindow();
        try
        {
            window.Show();
            window.Content = new CancellingCaptureContent(cancellation.Cancel);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Router(Options(window)).RouteAsync("screenshot", Arg("path", workspace.PathFor("capture.png")), cancellation.Token));
            Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Cancellation after the close decision preserves the closing response and queued close action.</summary>
    [AvaloniaFact]
    public async Task ExitApprovedCloseRunsAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var closed = 0;
        var options = Options() with
        {
            DecideExitRequest = _ => { cancellation.Cancel(); return RuntimeQueryExitResult.Closing; },
            Close = () => closed++
        };
        AssertSuccess(await Router(options).RouteAsync("exit", Arg("confirm", "true"), cancellation.Token), """{"closing":true}""");
        Assert.Equal(0, closed);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, closed);
    }

    private sealed class CancellingCaptureContent(Action cancel) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            cancel();
        }
    }
}
