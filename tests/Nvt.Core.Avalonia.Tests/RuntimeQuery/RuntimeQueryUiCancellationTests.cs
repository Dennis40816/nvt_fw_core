// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.Avalonia.Threading;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

/// <summary>Checks UI queue cancellation and cooperative execution using the headless dispatcher.</summary>
[Collection("RuntimeQuery")]
public sealed class RuntimeQueryUiCancellationTests
{
    /// <summary>Already cancelled requests never call the dispatcher error mapper or the handler.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledRequestNeverStartsUiWork(bool dispatcherAvailable)
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = RuntimeQueryUiThread.Wrap((_, _, _) => throw new InvalidOperationException("Work must not start."),
            (_, _) => throw new InvalidOperationException("Cancellation must not map to a dispatcher failure."));
        try
        {
            RuntimeQueryTestValues.DispatcherField.SetValue(null, dispatcherAvailable ? Dispatcher.UIThread : null);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler(null, "1", cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        finally
        {
            RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
        }
    }

    /// <summary>A token cancelled while work waits in the UI queue prevents the inner handler from running.</summary>
    [AvaloniaFact]
    public async Task CancelledQueuedUiWorkNeverStartsHandler()
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        using var cancellation = new CancellationTokenSource();
        using var queued = new ManualResetEventSlim();
        var calls = 0;
        var handler = RuntimeQueryUiThread.Wrap((_, _, _) =>
        {
            calls++;
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        }, RuntimeQueryTestValues.NfhError);
        try
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
            var execution = Task.Run(() =>
            {
                var pending = handler(null, "1", cancellation.Token);
                queued.Set();
                return pending;
            }, TestContext.Current.CancellationToken);
            // This test body runs on the UI thread. It blocks the queue until the worker has enqueued dispatch, and it cancels before its first await.
            Assert.True(queued.Wait(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken));
            cancellation.Cancel();
            OperationCanceledException canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(0, calls);
        }
        finally
        {
            RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
        }
    }

    /// <summary>Started UI handlers that ignore cancellation return their original response after cleanup.</summary>
    [AvaloniaFact]
    public async Task StartedUiHandlerIgnoringTokenFinishesNormally()
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = RuntimeQueryResponseEnvelope.Success(new object());
        var handler = RuntimeQueryUiThread.Wrap(async (_, _, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            entered.SetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            return expected;
        }, RuntimeQueryTestValues.NfhError);
        try
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
            var execution = handler(null, "1", cancellation.Token);
            await entered.Task.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            cancellation.Cancel();
            release.SetResult();
            Assert.Same(expected, await execution.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult();
            RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
        }
    }

    /// <summary>Real IPC shutdown cancels queued UI dispatch before a command handler begins.</summary>
    [AvaloniaFact]
    public async Task IpcCancellationDuringQueuedUiWorkNeverStartsCommand()
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        using var permitQueue = new ManualResetEventSlim();
        using var queued = new ManualResetEventSlim();
        using var stopCompleted = new ManualResetEventSlim();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var router = new RuntimeQueryCommandRouter([RuntimeQueryCommand.FromArgs("probe", RuntimeQueryCommandRisk.ReadOnly,
            (_, _) => { calls++; return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null)); })], false);
        var wrapped = RuntimeQueryUiThread.Wrap(router.ExecuteAsync, RuntimeQueryTestValues.NfhError);
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = new RuntimeQueryIpcServer(pipeName, "1", 5000, 100, RuntimeQueryTestValues.NfhError,
            (kind, _) => { if (kind == RuntimeQueryDiagnostic.Stopped) stopped.TrySetResult(); },
            (request, version, token) =>
            {
                if (!permitQueue.Wait(RuntimeQueryTestValues.WaitBound, timeout.Token)) throw new TimeoutException();
                var pending = wrapped(request, version, token);
                queued.Set();
                return pending;
            });
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
            server.Start();
            await client.ConnectAsync(1500, timeout.Token);
            await client.WriteAsync(Encoding.UTF8.GetBytes("{\"version\":\"1\",\"command\":\"probe\",\"args\":null}\n"), timeout.Token);
            permitQueue.Set();
            Assert.True(queued.Wait(RuntimeQueryTestValues.WaitBound, timeout.Token));
            var stop = Task.Run(async () =>
            {
                await server.DisposeAsync();
                stopCompleted.Set();
            }, timeout.Token);
            // Hold the UI queue until bounded shutdown has signalled cancellation.
            Assert.True(stopCompleted.Wait(RuntimeQueryTestValues.WaitBound, timeout.Token));
            await stop;
            await stopped.Task.WaitAsync(timeout.Token);
            Assert.Equal(0, calls);
        }
        finally
        {
            permitQueue.Set();
            RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
        }
    }
}
