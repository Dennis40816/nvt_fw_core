// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;

namespace Nvt.Core.RuntimeQuery;

/// <summary>JSON formatting shared by the runtime query transport and its callers.</summary>
public static class RuntimeQueryProtocol
{
    /// <summary>Read-only compact camel-case JSON defaults, including null properties.</summary>
    public static JsonSerializerOptions CompactJsonOptions { get; } = CreateDefaults(pretty: false);

    /// <summary>Read-only indented camel-case JSON defaults, including null properties.</summary>
    public static JsonSerializerOptions PrettyJsonOptions { get; } = CreateDefaults(pretty: true);

    /// <summary>Returns an independent mutable copy of the compact transport defaults.</summary>
    public static JsonSerializerOptions CreateCompactJsonOptions() => new(CompactJsonOptions);

    /// <summary>Returns an independent mutable copy of the pretty-output defaults.</summary>
    public static JsonSerializerOptions CreatePrettyJsonOptions() => new(PrettyJsonOptions);

    private static JsonSerializerOptions CreateDefaults(bool pretty)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = pretty
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>A single runtime query request.</summary>
/// <param name="Version">The caller's protocol version.</param>
/// <param name="Command">The command interpreted by the caller's handler.</param>
/// <param name="Args">Optional command arguments.</param>
public sealed record RuntimeQueryRequest(
    string Version,
    string Command,
    IReadOnlyDictionary<string, string>? Args);

/// <summary>A caller-defined error code and message.</summary>
/// <param name="Code">The error code.</param>
/// <param name="Message">The error message.</param>
public sealed record RuntimeQueryError(string Code, string Message);

/// <summary>The response envelope, with explicit null data or error properties.</summary>
/// <param name="Ok">Whether the request succeeded.</param>
/// <param name="Data">The caller's response data.</param>
/// <param name="Error">The caller's error.</param>
public sealed record RuntimeQueryResponseEnvelope(bool Ok, object? Data, RuntimeQueryError? Error)
{
    /// <summary>Creates a successful response.</summary>
    public static RuntimeQueryResponseEnvelope Success(object? data)
    {
        return new RuntimeQueryResponseEnvelope(Ok: true, Data: data, Error: null);
    }

    /// <summary>Creates a failed response using caller-supplied text.</summary>
    public static RuntimeQueryResponseEnvelope Failure(string code, string message)
    {
        return new RuntimeQueryResponseEnvelope(
            Ok: false, Data: null, Error: new RuntimeQueryError(code, message));
    }
}
