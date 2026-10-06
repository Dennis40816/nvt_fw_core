// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Tests.RuntimeQuery;

internal static class RuntimeQueryTestValues
{
    internal const string Version = "1";
    internal const int ReadTimeoutMs = 5000;
    internal const int ClientTimeoutMs = 1500;
    internal const int ShutdownBoundMs = 1500;

    internal static string NewPipeName() => $"nvt-core-runtime-query-{Guid.NewGuid():N}";

    internal static RuntimeQueryError NfhError(RuntimeQueryFailure failure, string? detail)
    {
        return failure switch
        {
            RuntimeQueryFailure.RequestTimeout => new("IPC_REQUEST_TIMEOUT",
                "Runtime query client connected but did not send a request before the server read timeout."),
            RuntimeQueryFailure.EmptyRequest => new("INVALID_REQUEST", "Empty request payload."),
            RuntimeQueryFailure.InvalidJson => new("INVALID_JSON", detail!),
            RuntimeQueryFailure.HandlerError or RuntimeQueryFailure.ClientError => new("IPC_ERROR", detail!),
            RuntimeQueryFailure.EmptyResponse => new("EMPTY_RESPONSE", "Runtime query returned an empty response."),
            RuntimeQueryFailure.InvalidResponse => new("INVALID_RESPONSE", "Runtime query response could not be parsed."),
            RuntimeQueryFailure.ConnectionTimeout => new("INSTANCE_NOT_RUNNING",
                "No running FreeformHelper instance responded within timeout."),
            RuntimeQueryFailure.ClientTimeout => new("IPC_TIMEOUT", "Runtime query did not complete within timeout."),
            RuntimeQueryFailure.IoError => new("IPC_IO_ERROR", detail!),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
    }

    internal static RuntimeQueryIpcServer CreateServer(
        string pipeName,
        Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>>? handler = null,
        int requestReadTimeoutMs = ReadTimeoutMs,
        Action<RuntimeQueryDiagnostic, Exception?>? diagnostic = null)
    {
        return new RuntimeQueryIpcServer(
            pipeName, Version, requestReadTimeoutMs, ShutdownBoundMs, NfhError, diagnostic,
            handler ?? ((request, version, _) => Task.FromResult(ValidateRequest(request, version))));
    }

    private static RuntimeQueryResponseEnvelope ValidateRequest(RuntimeQueryRequest? request, string version)
    {
        if (request is null)
        {
            return RuntimeQueryResponseEnvelope.Failure("INVALID_REQUEST", "Request is null.");
        }

        return string.Equals(request.Version, version, StringComparison.Ordinal)
            ? RuntimeQueryResponseEnvelope.Success(null)
            : RuntimeQueryResponseEnvelope.Failure("UNSUPPORTED_VERSION",
                $"Unsupported request version '{request.Version}'. Expected '{version}'.");
    }

    internal static NamedPipeServerStream CreatePeer(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[1];
        while (await stream.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
        {
            bytes.WriteByte(buffer[0]);
            if (buffer[0] == '\n')
            {
                break;
            }
        }

        return bytes.ToArray();
    }
}
