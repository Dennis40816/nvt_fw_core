// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

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
    // _sync protects connection state and the run-loop and disposal tasks.
    private ConnectionState? _activeConnection;
    private Task? _runLoopTask;
    private Task? _disposeTask;

    private sealed record ConnectionState(NamedPipeServerStream Pipe, bool RequestRead);

    internal NamedPipeServerStream? ActivePipe
    {
        get
        {
            lock (_sync)
            {
                return _activeConnection?.Pipe;
            }
        }
    }

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

    /// <summary>Stops accepting requests and lets an already-read request finish within the supplied shutdown bound.</summary>
    public ValueTask DisposeAsync()
    {
        var startedAt = Stopwatch.GetTimestamp();
        lock (_sync)
        {
            _disposeTask ??= StopAsync(startedAt);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task StopAsync(long startedAt)
    {
        // DisposeAsync holds _sync. Only an already-read request can keep its pipe open.
        var cancellationTask = Task.CompletedTask;
        if (_activeConnection is not { RequestRead: true })
        {
            cancellationTask = _lifecycleCts.CancelAsync();
            _activeConnection?.Pipe.Dispose();
        }
        var stopTask = _runLoopTask is null ? cancellationTask : Task.WhenAll(cancellationTask, _runLoopTask);
        try
        {
            var remaining = TimeSpan.FromMilliseconds(_shutdownTimeoutMs) - Stopwatch.GetElapsedTime(startedAt);
            await stopTask.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero).ConfigureAwait(false);
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
            try
            {
                // Finish the cancellation callbacks before the source is disposed, or late handlers never see it.
                await _lifecycleCts.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception cancelEx)
            {
                _diagnostic?.Invoke(RuntimeQueryDiagnostic.ShutdownFailed, cancelEx);
            }

            lock (_sync)
            {
                _activeConnection?.Pipe.Dispose();
            }
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
            NamedPipeServerStream createdPipe;
            try
            {
                lock (_sync)
                {
                    if (_disposeTask is not null)
                    {
                        break;
                    }

                    createdPipe = CreatePipe();
                    _activeConnection = new ConnectionState(createdPipe, RequestRead: false);
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostic?.Invoke(RuntimeQueryDiagnostic.PipeCreationFailed, ex);
                break;
            }

            try
            {
                await using var pipe = createdPipe;
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
            finally
            {
                lock (_sync)
                {
                    _activeConnection = null;
                }
            }
        }

        _diagnostic?.Invoke(RuntimeQueryDiagnostic.Stopped, null);
    }

    private NamedPipeServerStream CreatePipe()
    {
        if (OperatingSystem.IsWindows())
        {
            // Read the process token rather than an impersonated thread token.
            using var identity = WindowsIdentity.RunImpersonated(
                SafeAccessTokenHandle.InvalidHandle, WindowsIdentity.GetCurrent);
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(
                identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
            // Match the CurrentUserOnly client's token-owner check, including elevated tokens.
            security.SetOwner(identity.Owner!);

            // .NET rejects CurrentUserOnly with explicit security. Keep the NETWORK denial.
            return NamedPipeServerStreamAcl.Create(
                _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, security);
        }

        return new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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

        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _activeConnection = new ConnectionState(pipe, RequestRead: true);
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
            cancellationToken.ThrowIfCancellationRequested();
            response = await _handler(request, _protocolVersion, cancellationToken).ConfigureAwait(false);
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
