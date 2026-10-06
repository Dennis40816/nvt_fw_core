// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Nvt.Core.RuntimeQuery;

/// <summary>Sends one request using a total connection, write and read timeout budget.</summary>
public static class RuntimeQueryIpcClient
{
    /// <summary>Sends a compact JSON line and reads one response line from a local named pipe.</summary>
    /// <param name="pipeName">The caller's pipe name.</param>
    /// <param name="request">The request envelope.</param>
    /// <param name="timeoutMs">The positive total timeout budget in milliseconds.</param>
    /// <param name="error">Maps failures and exception message details to caller-owned error text.</param>
    /// <returns>The response, or a caller-defined failure envelope.</returns>
    public static RuntimeQueryResponseEnvelope SendRequest(
        string pipeName,
        RuntimeQueryRequest request,
        int timeoutMs,
        Func<RuntimeQueryFailure, string?, RuntimeQueryError> error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var startedAt = Stopwatch.GetTimestamp();
            client.Connect(timeoutMs);
            using var timeoutCts = new CancellationTokenSource(GetRemainingTimeout(timeoutMs, startedAt));
            var cancellationToken = timeoutCts.Token;
            using var reader = new StreamReader(
                client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(
                client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            var requestJson = JsonSerializer.Serialize(request, RuntimeQueryProtocol.CompactJsonOptions);
            writer.WriteLineAsync(requestJson).WaitAsync(cancellationToken).GetAwaiter().GetResult();
            var responseJson = reader.ReadLineAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return Failure(error, RuntimeQueryFailure.EmptyResponse);
            }

            var response = JsonSerializer.Deserialize<RuntimeQueryResponseEnvelope>(
                responseJson, RuntimeQueryProtocol.CompactJsonOptions);
            return response ?? Failure(error, RuntimeQueryFailure.InvalidResponse);
        }
        catch (Exception ex)
        {
            return Failure(error, ex);
        }
    }

    internal static RuntimeQueryResponseEnvelope Failure(
        Func<RuntimeQueryFailure, string?, RuntimeQueryError> error, Exception exception)
    {
        return exception switch
        {
            TimeoutException => Failure(error, RuntimeQueryFailure.ConnectionTimeout),
            OperationCanceledException => Failure(error, RuntimeQueryFailure.ClientTimeout),
            IOException => Failure(error, RuntimeQueryFailure.IoError, exception.Message),
            _ => Failure(error, RuntimeQueryFailure.ClientError, exception.Message)
        };
    }

    private static int GetRemainingTimeout(int timeoutMs, long startedAt)
    {
        var elapsedMs = (int)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        return Math.Max(1, timeoutMs - elapsedMs);
    }

    private static RuntimeQueryResponseEnvelope Failure(
        Func<RuntimeQueryFailure, string?, RuntimeQueryError> error,
        RuntimeQueryFailure failure,
        string? detail = null)
    {
        return new RuntimeQueryResponseEnvelope(Ok: false, Data: null, Error: error(failure, detail));
    }
}
