// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.Avalonia.Threading;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

public sealed partial class RuntimeQueryGenericCommandsTests
{
    /// <summary>The frozen command-line parser sends a valueless name as true, which is an unknown page.</summary>
    [AvaloniaFact]
    public async Task PageWithValuelessCommandLineNameReturnsUnknownPage()
    {
        var navigation = new TestNavigation();
        var router = Router(Options(navigation: navigation));
        var parser = typeof(RuntimeQueryCommandLine).GetMethod("TryParseCommandLine", BindingFlags.Static | BindingFlags.NonPublic)!;
        string[] commandLine = ["query", "page", "--name"];
        object?[] parameters = [commandLine, router.RegisteredCommands, null, null, null, null, null];
        Assert.True((bool)parser.Invoke(null, parameters)!);
        Assert.Null(parameters[6]);
        var command = Assert.IsType<string>(parameters[2]);
        Assert.Equal("page", command);
        var args = Assert.IsType<Dictionary<string, string>>(parameters[3]);
        Assert.Equal(new KeyValuePair<string, string>("name", "true"), Assert.Single(args));
        AssertFailure(await router.RouteAsync(command, args, TestContext.Current.CancellationToken),
            "UNKNOWN_PAGE", "Unknown page 'true'. Valid pages: zeta, alpha.");
        Assert.Equal("zeta", navigation.CurrentPage);
        Assert.Equal(0, navigation.SwitchCalls);
    }

    /// <summary>A posted tool close awaits host stop while the real pipe finishes the exit response.</summary>
    [AvaloniaFact]
    public async Task ExitResponseSurvivesToolCloseStoppingHost()
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var responseProduced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeStarted = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RuntimeQueryHost? host = null;
        var options = Options() with
        {
            Close = async () =>
            {
                try
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    var stopTask = host!.StopAsync();
                    closeStarted.SetResult(stopTask);
                    await stopTask;
                    closeFinished.SetResult();
                }
                catch (Exception exception)
                {
                    closeFinished.TrySetException(exception);
                }
            }
        };
        var router = Router(options);
        var handler = RuntimeQueryUiThread.Wrap(async (request, version, token) =>
        {
            var response = await router.ExecuteAsync(request, version, token);
            responseProduced.SetResult();
            await releaseResponse.Task.WaitAsync(token);
            return response;
        }, RuntimeQueryTestValues.NfhError);
        host = new RuntimeQueryHost(() => RuntimeQueryTestValues.CreateServer(pipeName, handler));
        try
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
            host.Start();
            var clientTask = Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
                new RuntimeQueryRequest(RuntimeQueryTestValues.Version, "exit", null), 5000, RuntimeQueryTestValues.NfhError));
            await responseProduced.Task.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            var stopTask = await closeStarted.Task.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            Assert.False(stopTask.IsCompleted);
            Assert.False(closeFinished.Task.IsCompleted);
            Assert.False(clientTask.IsCompleted);
            releaseResponse.SetResult();
            AssertSuccess(await clientTask.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken),
                """{"closing":true}""");
            await closeFinished.Task.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            Assert.True(stopTask.IsCompletedSuccessfully);
        }
        finally
        {
            releaseResponse.TrySetResult();
            try
            {
                if (closeStarted.Task.IsCompletedSuccessfully)
                {
                    var stopTask = await closeStarted.Task;
                    await stopTask.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
                }
                await host.StopAsync().WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            }
            finally
            {
                RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
            }
        }
    }
}
