// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Transport cases ported from NFH and additional wire characterizations.</summary>
[Collection(RuntimeQueryPipeTestGroup.Name)]
public sealed class RuntimeQueryIpcTests
{
    /// <summary>Ports stopping a host that has not started.</summary>
    [Fact]
    public async Task DisposeAsyncWhenNotStartedCompletes()
    {
        var server = RuntimeQueryTestValues.CreateServer(RuntimeQueryTestValues.NewPipeName());
        await server.DisposeAsync();
        await server.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(server.Start);
    }

    /// <summary>Ports bounded shutdown while waiting for a connection.</summary>
    [Fact]
    public async Task DisposeAsyncWhenStartedCompletesWithinTimeout()
    {
        var events = new ConcurrentQueue<RuntimeQueryDiagnostic>();
        await using var server = RuntimeQueryTestValues.CreateServer(
            RuntimeQueryTestValues.NewPipeName(), diagnostic: (kind, _) => events.Enqueue(kind));
        server.Start();
        server.Start();
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(RuntimeQueryTestValues.ShutdownBoundMs), TestContext.Current.CancellationToken);
        Assert.Collection(events,
            kind => Assert.Equal(RuntimeQueryDiagnostic.Started, kind),
            kind => Assert.Equal(RuntimeQueryDiagnostic.Stopped, kind));
    }

    /// <summary>Ports bounded shutdown with a connected client that sends no request.</summary>
    [Fact]
    public async Task DisposeAsyncWhenClientConnectedWithoutRequestCompletesWithinTimeout()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.Start();
        await client.ConnectAsync(RuntimeQueryTestValues.ClientTimeoutMs, TestContext.Current.CancellationToken);
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(RuntimeQueryTestValues.ShutdownBoundMs), TestContext.Current.CancellationToken);
    }

    /// <summary>A handler that ignores cancellation cannot hold the pipe run loop open.</summary>
    [Fact]
    public async Task DisposeAsyncWhenHandlerIgnoresCancellationCompletesWithinTimeout()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<RuntimeQueryResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName, (_, _, token) =>
        {
            entered.SetResult(token);
            return release.Task;
        });
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.Start();
        await client.ConnectAsync(RuntimeQueryTestValues.ClientTimeoutMs, TestContext.Current.CancellationToken);
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"version\":\"1\",\"command\":\"probe\",\"args\":null}\n"), TestContext.Current.CancellationToken);
        var handlerToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        try
        {
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(RuntimeQueryTestValues.ShutdownBoundMs), TestContext.Current.CancellationToken);
            Assert.True(handlerToken.IsCancellationRequested);
        }
        finally
        {
            release.TrySetResult(RuntimeQueryResponseEnvelope.Success(null));
        }
    }

    /// <summary>Even a synchronously blocked handler cannot prevent bounded disposal or keep the pipe open.</summary>
    [Fact]
    public async Task DisposeAsyncWhenHandlerBlocksSynchronouslyReportsBoundAndClosesPipe()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var testToken = TestContext.Current.CancellationToken;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOut = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new RuntimeQueryIpcServer(pipeName, "1", 5000, 100, RuntimeQueryTestValues.NfhError,
            (kind, ex) =>
            {
                if (kind == RuntimeQueryDiagnostic.ShutdownTimedOut)
                {
                    timedOut.TrySetResult(ex!);
                }
            },
            (_, _, _) =>
            {
                entered.SetResult();
                try
                {
                    release.Wait(testToken);
                    return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
                }
                finally
                {
                    exited.SetResult();
                }
            });
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.Start();
        await client.ConnectAsync(RuntimeQueryTestValues.ClientTimeoutMs, testToken);
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"version\":\"1\",\"command\":\"probe\",\"args\":null}\n"), testToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), testToken);
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(RuntimeQueryTestValues.ShutdownBoundMs), testToken);
            Assert.IsType<TimeoutException>(await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(3), testToken));
            await client.DisposeAsync();
            await using var replacement = RuntimeQueryTestValues.CreatePeer(pipeName);
        }
        finally
        {
            release.Set();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(3), testToken);
        }
    }

    /// <summary>Ports handler failure without its original Avalonia or product fixture.</summary>
    [Fact]
    public async Task SendRequestWhenHandlerThrowsReturnsIpcErrorEnvelope()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var exception = new InvalidOperationException("NullValue must be in [0,65535].");
        Exception? reportedException = null;
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName,
            (_, _, _) => throw exception,
            diagnostic: (kind, ex) =>
            {
                if (kind == RuntimeQueryDiagnostic.HandlerFailed)
                {
                    reportedException = ex;
                }
            });
        server.Start();
        var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("1", "probe", null), RuntimeQueryTestValues.ClientTimeoutMs, RuntimeQueryTestValues.NfhError));
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("IPC_ERROR", "NullValue must be in [0,65535]."), response);
        Assert.Same(exception, reportedException);
    }

    /// <summary>The frozen catch order treats handler JSON exceptions as invalid JSON.</summary>
    [Fact]
    public async Task HandlerJsonExceptionRetainsInvalidJsonClassification()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName, (_, _, _) => throw new JsonException("synthetic JSON failure"));
        server.Start();
        var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("1", "probe", null), RuntimeQueryTestValues.ClientTimeoutMs, RuntimeQueryTestValues.NfhError));
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("INVALID_JSON", "synthetic JSON failure"), response);
    }

    /// <summary>Incoming UTF-8 and outgoing framing are checked against literal wire bytes.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("\uFEFF")]
    public async Task ServerReadsUtf8AndWritesExactCompactResponseFrame(string preamble)
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName, (request, _, _) =>
        {
            Assert.NotNull(request);
            Assert.Equal("probe", request.Command);
            Assert.NotNull(request.Args);
            Assert.Equal("測試", request.Args["CaseKey"]);
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(new { DisplayName = "測試", Note = (string?)null }));
        });
        server.Start();
        var bytes = await SendRawAsync(pipeName,
            preamble + "{\"version\":\"1\",\"command\":\"probe\",\"args\":{\"CaseKey\":\"測試\"}}\n");
        var expected = """{"ok":true,"data":{"displayName":"\u6E2C\u8A66","note":null},"error":null}""" + Environment.NewLine;
        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
    }

    /// <summary>Empty and whitespace lines return the caller's exact frozen error frame.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData(" \t\r\n")]
    public async Task EmptyRequestsReturnExactFailureFrame(string request)
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName, (_, _, _) => throw new InvalidOperationException("handler must not run"));
        server.Start();
        var bytes = await SendRawAsync(pipeName, request);
        Assert.Equal(Encoding.UTF8.GetBytes(
            "{\"ok\":false,\"data\":null,\"error\":{\"code\":\"INVALID_REQUEST\",\"message\":\"Empty request payload.\"}}" + Environment.NewLine), bytes);
    }

    /// <summary>Malformed JSON retains serializer exception details.</summary>
    [Theory]
    [InlineData("{]")]
    [InlineData("{\"version\":42}")]
    public async Task MalformedRequestsReturnFrozenJsonExceptionMessage(string request)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RuntimeQueryRequest>(request, RuntimeQueryProtocol.CompactJsonOptions));
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName);
        server.Start();
        var bytes = await SendRawAsync(pipeName, request + "\n");
        var response = JsonSerializer.Deserialize<RuntimeQueryResponseEnvelope>(bytes, RuntimeQueryProtocol.CompactJsonOptions);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("INVALID_JSON", exception.Message), response);
    }

    /// <summary>Valid JSON of the wrong shape forwards a serializer message that names the Core request type.</summary>
    [Theory]
    [InlineData("42")]
    [InlineData("[]")]
    public async Task WrongShapeRequestsNameTheCoreRequestType(string request)
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName);
        server.Start();
        var bytes = await SendRawAsync(pipeName, request + "\n");
        var response = JsonSerializer.Deserialize<RuntimeQueryResponseEnvelope>(bytes, RuntimeQueryProtocol.CompactJsonOptions);

        var error = Assert.IsType<RuntimeQueryError>(response?.Error);
        Assert.Equal("INVALID_JSON", error.Code);
        // The frozen source named FreeformHelper.UI.Services.RuntimeQueryRequest here.
        Assert.Contains("Nvt.Core.RuntimeQuery.RuntimeQueryRequest", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Null and mismatched versions retain NFH's existing request validation text.</summary>
    [Theory]
    [InlineData("null", "INVALID_REQUEST", "Request is null.")]
    [InlineData("{\"version\":\"2\",\"command\":\"probe\",\"args\":null}", "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'.")]
    [InlineData("{}", "UNSUPPORTED_VERSION", "Unsupported request version ''. Expected '1'.")]
    public async Task InvalidEnvelopesRetainCallerSuppliedErrors(string request, string code, string message)
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName);
        server.Start();
        var bytes = await SendRawAsync(pipeName, request + "\n");
        var response = JsonSerializer.Deserialize<RuntimeQueryResponseEnvelope>(bytes, RuntimeQueryProtocol.CompactJsonOptions);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure(code, message), response);
    }

    /// <summary>A caller-supplied read timeout returns the exact error and releases the connection.</summary>
    [Fact]
    public async Task ConnectedIdleClientReceivesExactReadTimeoutFrame()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName, requestReadTimeoutMs: 100);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        server.Start();
        await client.ConnectAsync(RuntimeQueryTestValues.ClientTimeoutMs, timeout.Token);
        var bytes = await RuntimeQueryTestValues.ReadFrameAsync(client, timeout.Token);
        Assert.Equal(Encoding.UTF8.GetBytes(
            "{\"ok\":false,\"data\":null,\"error\":{\"code\":\"IPC_REQUEST_TIMEOUT\",\"message\":\"Runtime query client connected but did not send a request before the server read timeout.\"}}" + Environment.NewLine), bytes);
        await client.DisposeAsync();
        var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("1", "probe", null), RuntimeQueryTestValues.ClientTimeoutMs, RuntimeQueryTestValues.NfhError));
        Assert.True(response.Ok);
    }

    /// <summary>Identity, version and error text are supplied entirely by a synthetic caller.</summary>
    [Fact]
    public async Task ServerUsesCallerVersionAndErrorCallback()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = new RuntimeQueryIpcServer(pipeName, "synthetic-v7", 200, 250,
            (failure, detail) => new RuntimeQueryError(failure.ToString(), detail ?? "synthetic error"), null,
            (request, version, _) => Task.FromResult(request?.Version == version
                ? RuntimeQueryResponseEnvelope.Success(null)
                : RuntimeQueryResponseEnvelope.Failure("SYNTHETIC_VERSION", $"Expected '{version}'.")));
        server.Start();
        var success = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("synthetic-v7", "probe", null), RuntimeQueryTestValues.ClientTimeoutMs, RuntimeQueryTestValues.NfhError));
        Assert.True(success.Ok);
        var rejected = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("SYNTHETIC-V7", "probe", null), RuntimeQueryTestValues.ClientTimeoutMs, RuntimeQueryTestValues.NfhError));
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("SYNTHETIC_VERSION", "Expected 'synthetic-v7'."), rejected);
        var empty = await SendRawAsync(pipeName, "\n");
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("EmptyRequest", "synthetic error"),
            JsonSerializer.Deserialize<RuntimeQueryResponseEnvelope>(empty, RuntimeQueryProtocol.CompactJsonOptions));
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
    }

    /// <summary>The JSON null literal reaches the handler, preserving application validation and dispatch order.</summary>
    [Fact]
    public async Task NullRequestIsPassedToCallerHandler()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = new RuntimeQueryIpcServer(pipeName, "synthetic-v7", 5000, 1500,
            RuntimeQueryTestValues.NfhError, null, (request, version, _) =>
            {
                Assert.Null(request);
                Assert.Equal("synthetic-v7", version);
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            });
        server.Start();
        var bytes = await SendRawAsync(pipeName, "null\n");
        Assert.Equal(Encoding.UTF8.GetBytes("{\"ok\":true,\"data\":null,\"error\":null}" + Environment.NewLine), bytes);
    }

    private static async Task<byte[]> SendRawAsync(string pipeName, string request)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(RuntimeQueryTestValues.ClientTimeoutMs, timeout.Token);
        await client.WriteAsync(Encoding.UTF8.GetBytes(request), timeout.Token);
        using var response = new MemoryStream();
        await client.CopyToAsync(response, timeout.Token);
        return response.ToArray();
    }
}
