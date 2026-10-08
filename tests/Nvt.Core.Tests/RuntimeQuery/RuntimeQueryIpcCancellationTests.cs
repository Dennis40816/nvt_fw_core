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

    private static async Task SendAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(1500, cancellationToken);
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"version\":\"1\",\"command\":\"probe\",\"args\":null}\n"), cancellationToken);
    }
}
