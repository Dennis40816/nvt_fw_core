// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks cancellation across the real pipe server and router without detaching committed cleanup.</summary>
[Collection("RuntimeQuery pipes")]
public sealed class RuntimeQueryIpcCancellationTests
{
    /// <summary>A shutdown token cancelled before routing prevents command work from starting.</summary>
    [Fact]
    public async Task CancelledRequestBeforeRoutingNeverStartsCommand()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var router = new RuntimeQueryCommandRouter([RuntimeQueryCommand.FromArgs("probe", RuntimeQueryCommandRisk.ReadOnly,
            (_, _) => { calls++; return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null)); })], false);
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = new RuntimeQueryIpcServer(pipeName, "1", 5000, 100, RuntimeQueryTestValues.NfhError,
            (kind, _) => { if (kind == RuntimeQueryDiagnostic.Stopped) stopped.TrySetResult(); },
            async (request, version, token) =>
            {
                entered.SetResult(token);
                await release.Task.WaitAsync(timeout.Token);
                return await router.ExecuteAsync(request, version, token);
            });
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.Start();
        try
        {
            await SendAsync(client, timeout.Token);
            var token = await entered.Task.WaitAsync(timeout.Token);
            await server.DisposeAsync();
            Assert.True(token.IsCancellationRequested);
            release.SetResult();
            await stopped.Task.WaitAsync(timeout.Token);
            Assert.Equal(0, calls);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    /// <summary>Started work observes shutdown, finishes cleanup, and reports an unrelated failure without replacing it.</summary>
    [Fact]
    public async Task CancellationAfterWorkStartedPreservesCleanupAndFailure()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<string>();
        var expected = new InvalidOperationException("Failure after committed cleanup.");
        Exception? reported = null;
        var router = new RuntimeQueryCommandRouter([RuntimeQueryCommand.FromArgs("probe", RuntimeQueryCommandRisk.ChangesState,
            async (_, token) =>
            {
                entered.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                finally
                {
                    cleanupStarted.SetResult();
                    await release.Task.WaitAsync(timeout.Token);
                    events.Enqueue("cleanup");
                }
                return RuntimeQueryResponseEnvelope.Success(null);
            })], false);
        // Convert the handler's own cancellation into an unrelated fault after cleanup.
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = new RuntimeQueryIpcServer(pipeName, "1", 5000, 100, RuntimeQueryTestValues.NfhError,
            (kind, error) =>
            {
                if (kind == RuntimeQueryDiagnostic.HandlerFailed) reported = error;
                if (kind == RuntimeQueryDiagnostic.Stopped)
                {
                    events.Enqueue("stopped");
                    stopped.TrySetResult();
                }
            }, async (request, version, token) =>
            {
                try { return await router.ExecuteAsync(request, version, token); }
                catch (OperationCanceledException) { throw expected; }
            });
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.Start();
        try
        {
            await SendAsync(client, timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            await server.DisposeAsync();
            await cleanupStarted.Task.WaitAsync(timeout.Token);
            release.SetResult();
            await stopped.Task.WaitAsync(timeout.Token);
            string[] expectedOrder = ["cleanup", "stopped"];
            Assert.Equal(expectedOrder, events);
            Assert.Same(expected, reported);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    /// <summary>A slow cancel callback finishes before DisposeAsync returns, so late handlers see the signal.</summary>
    [Fact]
    public async Task DisposeAsyncFinishesSlowCancelCallbacksBeforeReturning()
    {
        var finished = false;
        var events = await RunBlockedHandlerAsync(token => token.Register(() =>
        {
            Thread.Sleep(200);
            Volatile.Write(ref finished, true);
        }));
        Assert.True(Volatile.Read(ref finished));
        Assert.Contains(RuntimeQueryDiagnostic.ShutdownTimedOut, events.Select(e => e.Kind));
    }

    /// <summary>A throwing cancel callback is reported as a failure and does not hide the timeout.</summary>
    [Fact]
    public async Task DisposeAsyncReportsThrowingCancelCallbackAndTimeout()
    {
        var events = await RunBlockedHandlerAsync(token => token.Register(() => throw new InvalidOperationException("callback")));
        var kinds = events.Select(e => e.Kind).ToArray();
        Assert.Contains(RuntimeQueryDiagnostic.ShutdownFailed, kinds);
        Assert.Contains(RuntimeQueryDiagnostic.ShutdownTimedOut, kinds);
    }

    /// <summary>A handler that throws the shutdown token's cancellation is a shutdown cancel, not a handler failure.</summary>
    [Fact]
    public async Task HandlerThrowingShutdownCancellationIsNotReportedAsFailure()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<RuntimeQueryDiagnostic>();
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = new RuntimeQueryIpcServer(pipeName, "1", 5000, 1500, RuntimeQueryTestValues.NfhError,
            (kind, _) => events.Enqueue(kind), async (_, _, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return RuntimeQueryResponseEnvelope.Success(null);
            });
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.Start();
        await SendAsync(client, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        await server.DisposeAsync();
        using var responseBytes = new MemoryStream();
        var readError = await Record.ExceptionAsync(() => client.CopyToAsync(responseBytes, timeout.Token));
        Assert.True(readError is null or IOException);
        Assert.Empty(responseBytes.ToArray());
        Assert.DoesNotContain(RuntimeQueryDiagnostic.HandlerFailed, events);
        Assert.Contains(RuntimeQueryDiagnostic.Stopped, events);
    }

    /// <summary>Runs a handler that never returns, registers a callback on its token, and disposes with a short bound.</summary>
    private static async Task<List<(RuntimeQueryDiagnostic Kind, Exception? Error)>> RunBlockedHandlerAsync(
        Func<CancellationToken, CancellationTokenRegistration> register)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<RuntimeQueryResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<(RuntimeQueryDiagnostic, Exception?)>();
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = new RuntimeQueryIpcServer(pipeName, "1", 5000, 100, RuntimeQueryTestValues.NfhError,
            (kind, error) => events.Enqueue((kind, error)), (_, _, token) =>
            {
                register(token);
                entered.SetResult(token);
                return release.Task;
            });
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.Start();
        try
        {
            await SendAsync(client, timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            await server.DisposeAsync();
            return [.. events];
        }
        finally
        {
            release.TrySetResult(RuntimeQueryResponseEnvelope.Success(null));
        }
    }

    private static async Task SendAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(1500, cancellationToken);
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"version\":\"1\",\"command\":\"probe\",\"args\":null}\n"), cancellationToken);
    }
}
