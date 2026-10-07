// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>Transport failures for which the caller supplies an error code and message.</summary>
public enum RuntimeQueryFailure
{
    /// <summary>A connected client did not send a line before the read timeout.</summary>
    RequestTimeout,
    /// <summary>The request line was empty or whitespace.</summary>
    EmptyRequest,
    /// <summary>Request deserialization failed; detail is the JSON exception message.</summary>
    InvalidJson,
    /// <summary>The request handler failed; detail is the exception message.</summary>
    HandlerError,
    /// <summary>The response line was empty or whitespace.</summary>
    EmptyResponse,
    /// <summary>The response JSON was the null literal.</summary>
    InvalidResponse,
    /// <summary>The client could not connect within its timeout budget.</summary>
    ConnectionTimeout,
    /// <summary>The client's remaining request/response budget expired.</summary>
    ClientTimeout,
    /// <summary>Client pipe IO failed; detail is the exception message.</summary>
    IoError,
    /// <summary>Another client failure occurred; detail is the exception message.</summary>
    ClientError,
    /// <summary>No running UI dispatcher is available; detail is null.</summary>
    DispatcherUnavailable,
    /// <summary>No per-window server candidate matches the selection; detail is null.</summary>
    ServerNotFound
}

/// <summary>Diagnostic events delivered without a logging dependency.</summary>
public enum RuntimeQueryDiagnostic
{
    /// <summary>The server started.</summary>
    Started,
    /// <summary>The server run loop stopped.</summary>
    Stopped,
    /// <summary>Waiting for a pipe connection failed.</summary>
    ConnectionFailed,
    /// <summary>Reading or writing a request failed.</summary>
    RequestFailed,
    /// <summary>The request handler failed.</summary>
    HandlerFailed,
    /// <summary>The run loop failed during shutdown.</summary>
    ShutdownFailed,
    /// <summary>The run loop exceeded the caller's shutdown bound.</summary>
    ShutdownTimedOut,
    /// <summary>Creating a server pipe failed; the run loop ends.</summary>
    PipeCreationFailed
}
