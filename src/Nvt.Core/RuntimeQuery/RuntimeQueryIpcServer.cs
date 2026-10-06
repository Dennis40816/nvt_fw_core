// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Nvt.Core.RuntimeQuery;

/// <summary>Serves one JSON request per named-pipe connection.</summary>
public sealed class RuntimeQueryIpcServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly string _protocolVersion;
    private readonly int _requestReadTimeoutMs;
    private readonly int _shutdownTimeoutMs;
    private readonly Func<RuntimeQueryFailure, string?, RuntimeQueryError> _error;
    private readonly Action<RuntimeQueryDiagnostic, Exception?>? _diagnostic;
    private readonly Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>> _handler;
    private readonly CancellationTokenSource _lifecycleCts = new();
    private readonly object _sync = new();
    private NamedPipeServerStream? _activePipe;
    private Task? _runLoopTask;
    private Task? _disposeTask;

    /// <summary>Creates a server with caller-owned identity, limits, errors, diagnostics and handling.</summary>
    /// <param name="pipeName">The local named-pipe name.</param>
    /// <param name="protocolVersion">The caller's protocol version, passed to the handler.</param>
    /// <param name="requestReadTimeoutMs">The positive timeout for reading a connected client's request.</param>
    /// <param name="shutdownTimeoutMs">The positive maximum wait for the run loop during disposal.</param>
    /// <param name="error">Maps a failure and its optional detail to caller-owned error text.</param>
    /// <param name="diagnostic">Receives diagnostic events and exceptions, or null to omit diagnostics.</param>
    /// <param name="handler">Receives the deserialized request and configured version; cancellation signals shutdown.</param>
    public RuntimeQueryIpcServer(
        string pipeName,
        string protocolVersion,
        int requestReadTimeoutMs,
        int shutdownTimeoutMs,
        Func<RuntimeQueryFailure, string?, RuntimeQueryError> error,
        Action<RuntimeQueryDiagnostic, Exception?>? diagnostic,
        Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(protocolVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestReadTimeoutMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shutdownTimeoutMs);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(handler);
        _pipeName = pipeName;
        _protocolVersion = protocolVersion;
        _requestReadTimeoutMs = requestReadTimeoutMs;
        _shutdownTimeoutMs = shutdownTimeoutMs;
        _error = error;
        _diagnostic = diagnostic;
        _handler = handler;
    }

    /// <summary>Starts the server once. A disposed server cannot be restarted.</summary>
    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_runLoopTask is not null)
            {
                return;
            }

            var cancellationToken = _lifecycleCts.Token;
            _runLoopTask = Task.Run(() => RunLoopAsync(cancellationToken));
        }

        _diagnostic?.Invoke(RuntimeQueryDiagnostic.Started, null);
    }

    /// <summary>Cancels connection, read, handler and write waits within the supplied shutdown bound.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposeTask ??= StopAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task StopAsync()
    {
        var cancellationTask = _lifecycleCts.CancelAsync();
        Volatile.Read(ref _activePipe)?.Dispose();
        var stopTask = _runLoopTask is null ? cancellationTask : Task.WhenAll(cancellationTask, _runLoopTask);
        try
        {
            await stopTask.WaitAsync(TimeSpan.FromMilliseconds(_shutdownTimeoutMs)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation ends the run loop during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Ignore disposal races during shutdown.
        }
        catch (TimeoutException ex)
        {
            _diagnostic?.Invoke(RuntimeQueryDiagnostic.ShutdownTimedOut, ex);
        }
        catch (Exception ex)
        {
            _diagnostic?.Invoke(RuntimeQueryDiagnostic.ShutdownFailed, ex);
        }

        _lifecycleCts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            Volatile.Write(ref _activePipe, pipe);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostic?.Invoke(RuntimeQueryDiagnostic.ConnectionFailed, ex);
                continue;
            }

            try
            {
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostic?.Invoke(RuntimeQueryDiagnostic.RequestFailed, ex);
            }
        }

        _diagnostic?.Invoke(RuntimeQueryDiagnostic.Stopped, null);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(
            pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeoutCts.CancelAfter(_requestReadTimeoutMs);

        string? requestJson;
        try
        {
            requestJson = await reader.ReadLineAsync(readTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteResponseAsync(writer, Failure(RuntimeQueryFailure.RequestTimeout), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(requestJson))
        {
            await WriteResponseAsync(writer, Failure(RuntimeQueryFailure.EmptyRequest), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        RuntimeQueryResponseEnvelope response;
        try
        {
            var request = JsonSerializer.Deserialize<RuntimeQueryRequest>(requestJson, RuntimeQueryProtocol.CompactJsonOptions);
            response = await _handler(request, _protocolVersion, cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            response = Failure(RuntimeQueryFailure.InvalidJson, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _diagnostic?.Invoke(RuntimeQueryDiagnostic.HandlerFailed, ex);
            response = Failure(RuntimeQueryFailure.HandlerError, ex.Message);
        }

        await WriteResponseAsync(writer, response, cancellationToken).ConfigureAwait(false);
    }

    private RuntimeQueryResponseEnvelope Failure(RuntimeQueryFailure failure, string? detail = null)
    {
        return new RuntimeQueryResponseEnvelope(Ok: false, Data: null, Error: _error(failure, detail));
    }

    private static async Task WriteResponseAsync(
        StreamWriter writer, RuntimeQueryResponseEnvelope response, CancellationToken cancellationToken)
    {
        var responseJson = JsonSerializer.Serialize(response, RuntimeQueryProtocol.CompactJsonOptions);
        await writer.WriteLineAsync(responseJson).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
