// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Frozen client framing, timeout budget and failure envelopes.</summary>
public sealed class RuntimeQueryIpcClientTests
{
    /// <summary>The client emits exact compact UTF-8 bytes and accepts the frozen UTF-8 preamble behavior.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("\uFEFF")]
    public async Task SendRequestWritesExactRequestFrame(string responsePreamble)
    {
        var request = new RuntimeQueryRequest("synthetic-v1", "probe", new Dictionary<string, string> { ["CaseKey"] = "測試" });
        var result = await SendWithPeerAsync(responsePreamble + "{\"ok\":true,\"data\":null,\"error\":null}\n", request);
        Assert.Equal(Encoding.UTF8.GetBytes(
            """{"version":"synthetic-v1","command":"probe","args":{"CaseKey":"\u6E2C\u8A66"}}""" + Environment.NewLine), result.RequestBytes);
        Assert.Equal(RuntimeQueryResponseEnvelope.Success(null), result.Response);
    }

    /// <summary>A request with null arguments retains the exact wire property order and framing.</summary>
    [Fact]
    public async Task SendRequestWithNullArgumentsWritesExactRequestFrame()
    {
        var result = await SendWithPeerAsync("{\"ok\":true,\"data\":null,\"error\":null}\n");
        Assert.Equal(Encoding.UTF8.GetBytes(
            "{\"version\":\"1\",\"command\":\"probe\",\"args\":null}" + Environment.NewLine), result.RequestBytes);
    }

    /// <summary>Ports an accepted request for which the peer sends no response.</summary>
    [Fact]
    public async Task SendRequestWhenServerAcceptsButDoesNotRespondReturnsTimeout()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var serverTask = Task.Run(async () =>
        {
            await peer.WaitForConnectionAsync(timeout.Token);
            _ = await RuntimeQueryTestValues.ReadFrameAsync(peer, timeout.Token);
            await Task.Delay(500, timeout.Token);
        }, TestContext.Current.CancellationToken);
        var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("1", "probe", null), 100, RuntimeQueryTestValues.NfhError));
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("IPC_TIMEOUT", "Runtime query did not complete within timeout."), response);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    /// <summary>Connection time is deducted from the later request/response budget.</summary>
    [Fact]
    public async Task SendRequestUsesOneTotalTimeoutBudget()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(async () =>
        {
            await started.Task;
            await Task.Delay(900, timeout.Token);
            await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
            await peer.WaitForConnectionAsync(timeout.Token);
            connected.SetResult();
            _ = await RuntimeQueryTestValues.ReadFrameAsync(peer, timeout.Token);
            await release.Task.WaitAsync(timeout.Token);
        }, TestContext.Current.CancellationToken);
        var clientTask = Task.Run(() =>
        {
            started.SetResult();
            var watch = Stopwatch.StartNew();
            var response = RuntimeQueryIpcClient.SendRequest(pipeName,
                new RuntimeQueryRequest("1", "probe", null), 1500, RuntimeQueryTestValues.NfhError);
            return (Response: response, Elapsed: watch.Elapsed);
        });
        try
        {
            await connected.Task.WaitAsync(timeout.Token);
            var result = await clientTask.WaitAsync(timeout.Token);
            Assert.Equal(RuntimeQueryResponseEnvelope.Failure("IPC_TIMEOUT", "Runtime query did not complete within timeout."), result.Response);
            Assert.InRange(result.Elapsed.TotalMilliseconds, 1300, 2100);
        }
        finally
        {
            release.TrySetResult();
            await serverTask.WaitAsync(timeout.Token);
        }
    }

    /// <summary>An absent pipe retains the caller's frozen connection timeout text.</summary>
    [Fact]
    public void SendRequestWhenNoServerExistsReturnsInstanceNotRunning()
    {
        var response = RuntimeQueryIpcClient.SendRequest(RuntimeQueryTestValues.NewPipeName(),
            new RuntimeQueryRequest("1", "probe", null), 100, RuntimeQueryTestValues.NfhError);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("INSTANCE_NOT_RUNNING",
            "No running FreeformHelper instance responded within timeout."), response);
    }

    /// <summary>Empty lines and whitespace retain the frozen empty-response error.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData(" \t\r\n")]
    public async Task EmptyResponseReturnsExactError(string responseJson)
    {
        var result = await SendWithPeerAsync(responseJson);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("EMPTY_RESPONSE", "Runtime query returned an empty response."), result.Response);
    }

    /// <summary>A JSON null response retains the frozen invalid-response error.</summary>
    [Fact]
    public async Task NullResponseReturnsExactError()
    {
        var result = await SendWithPeerAsync("null\n");
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("INVALID_RESPONSE", "Runtime query response could not be parsed."), result.Response);
    }

    /// <summary>Malformed response JSON retains the generic client error and serializer message.</summary>
    [Fact]
    public async Task MalformedResponseReturnsExactExceptionMessage()
    {
        const string responseJson = "{]";
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuntimeQueryResponseEnvelope>(
            responseJson, RuntimeQueryProtocol.CompactJsonOptions));
        var result = await SendWithPeerAsync(responseJson + "\n");
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("IPC_ERROR", exception.Message), result.Response);
    }

    /// <summary>A response of valid JSON but the wrong shape forwards a serializer message that names the Core envelope type.</summary>
    [Theory]
    [InlineData("42")]
    [InlineData("[]")]
    public async Task WrongShapeResponseNamesTheCoreEnvelopeType(string responseJson)
    {
        var result = await SendWithPeerAsync(responseJson + "\n");

        var error = Assert.IsType<RuntimeQueryError>(result.Response.Error);
        Assert.Equal("IPC_ERROR", error.Code);
        // The frozen source named FreeformHelper.UI.Services.RuntimeQueryResponseEnvelope here.
        Assert.Contains("Nvt.Core.RuntimeQuery.RuntimeQueryResponseEnvelope", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An open connection with an unterminated response cannot complete before the timeout.</summary>
    [Fact]
    public async Task ResponseWithoutFinalNewlineTimesOutWhileConnectionRemainsOpen()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var serverTask = Task.Run(async () =>
        {
            await peer.WaitForConnectionAsync(timeout.Token);
            _ = await RuntimeQueryTestValues.ReadFrameAsync(peer, timeout.Token);
            await peer.WriteAsync(Encoding.UTF8.GetBytes("{\"ok\":true,\"data\":null,\"error\":null}"), timeout.Token);
            await Task.Delay(500, timeout.Token);
        }, TestContext.Current.CancellationToken);
        var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("1", "probe", null), 100, RuntimeQueryTestValues.NfhError));
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("IPC_TIMEOUT", "Runtime query did not complete within timeout."), response);
        await serverTask.WaitAsync(timeout.Token);
    }

    /// <summary>Broken-pipe IO messages are forwarded unchanged to the caller's error mapping.</summary>
    [Fact]
    public async Task DisconnectedPeerReturnsCallerIoErrorAndExceptionMessage()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(async () =>
        {
            await peer.WaitForConnectionAsync(timeout.Token);
            peer.Disconnect();
        }, TestContext.Current.CancellationToken);
        string? exceptionMessage = null;
        var request = new RuntimeQueryRequest("1", "probe", new Dictionary<string, string> { ["payload"] = new string('x', 1024 * 1024) });
        var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName, request, 1500, (failure, detail) =>
        {
            Assert.Equal(RuntimeQueryFailure.IoError, failure);
            exceptionMessage = detail;
            return RuntimeQueryTestValues.NfhError(failure, detail);
        }));
        Assert.NotNull(exceptionMessage);
        Assert.NotEmpty(exceptionMessage);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("IPC_IO_ERROR", exceptionMessage), response);
        await serverTask.WaitAsync(timeout.Token);
    }

    /// <summary>The client uses caller-owned error codes and messages.</summary>
    [Fact]
    public void SendRequestUsesCallerErrorCallback()
    {
        var response = RuntimeQueryIpcClient.SendRequest(RuntimeQueryTestValues.NewPipeName(),
            new RuntimeQueryRequest("synthetic-v1", "probe", null), 50,
            (failure, _) => new RuntimeQueryError(failure.ToString(), "synthetic timeout"));
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("ConnectionTimeout", "synthetic timeout"), response);
    }

    private static async Task<(RuntimeQueryResponseEnvelope Response, byte[] RequestBytes)> SendWithPeerAsync(
        string responseJson, RuntimeQueryRequest? request = null)
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(async () =>
        {
            await peer.WaitForConnectionAsync(timeout.Token);
            var requestBytes = await RuntimeQueryTestValues.ReadFrameAsync(peer, timeout.Token);
            await peer.WriteAsync(Encoding.UTF8.GetBytes(responseJson), timeout.Token);
            if (OperatingSystem.IsWindows())
            {
                peer.WaitForPipeDrain();
            }

            await peer.DisposeAsync();
            return requestBytes;
        }, TestContext.Current.CancellationToken);
        var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            request ?? new RuntimeQueryRequest("1", "probe", null), RuntimeQueryTestValues.ClientTimeoutMs, RuntimeQueryTestValues.NfhError));
        return (response, await serverTask.WaitAsync(timeout.Token));
    }
}
