// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text.Json;

namespace Nvt.Core.RuntimeQuery;

/// <summary>Parses a query command, sends one request and writes its JSON response.</summary>
public static class RuntimeQueryCommandLine
{
    /// <summary>Handles arguments whose first token is query, ignoring case.</summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <param name="pipeName">The tool's pipe name.</param>
    /// <param name="protocolVersion">The tool's protocol version.</param>
    /// <param name="supportedCommands">The supported command names in the tool's order.</param>
    /// <param name="error">Maps client failures to the tool's error codes and messages.</param>
    /// <param name="output">The output writer; tools pass Console.Out.</param>
    /// <param name="exitCode">Zero for success or unhandled arguments, one for failure, or two for invalid arguments.</param>
    /// <returns>Whether the first argument was query.</returns>
    public static bool TryHandleQueryCommand(
        string[] args,
        string pipeName,
        string protocolVersion,
        IReadOnlyList<string> supportedCommands,
        Func<RuntimeQueryFailure, string?, RuntimeQueryError> error,
        TextWriter output,
        out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], "query", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryParseCommandLine(args, supportedCommands, out var command, out var commandArgs,
            out var timeoutMs, out var prettyJson, out var parseError))
        {
            var response = RuntimeQueryResponseEnvelope.Failure("INVALID_ARGUMENTS", parseError ?? "Invalid arguments.");
            output.WriteLine(JsonSerializer.Serialize(response, RuntimeQueryProtocol.PrettyJsonOptions));
            exitCode = 2;
            return true;
        }

        var request = new RuntimeQueryRequest(
            Version: protocolVersion,
            Command: command,
            Args: commandArgs.Count == 0 ? null : commandArgs);
        var reply = RuntimeQueryIpcClient.SendRequest(pipeName, request, timeoutMs, error);
        var options = prettyJson ? RuntimeQueryProtocol.PrettyJsonOptions : RuntimeQueryProtocol.CompactJsonOptions;
        output.WriteLine(JsonSerializer.Serialize(reply, options));
        exitCode = reply.Ok ? 0 : 1;
        return true;
    }

    private static bool TryParseCommandLine(
        string[] args,
        IReadOnlyList<string> supportedCommands,
        out string command,
        out Dictionary<string, string> commandArgs,
        out int timeoutMs,
        out bool prettyJson,
        out string? error)
    {
        command = string.Empty;
        commandArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        timeoutMs = 1500;
        prettyJson = true;
        error = null;

        if (args.Length < 2)
        {
            error = $"Usage: query <{string.Join("|", supportedCommands)}> [--key value]";
            return false;
        }

        command = args[1].Trim().ToLowerInvariant();
        if (!supportedCommands.Contains(command, StringComparer.Ordinal))
        {
            error = $"Unsupported query command '{command}'. Supported: {string.Join(", ", supportedCommands)}.";
            return false;
        }

        for (var i = 2; i < args.Length; i++)
        {
            var token = args[i];
            if (string.Equals(token, "--json-compact", StringComparison.OrdinalIgnoreCase))
            {
                prettyJson = false;
                continue;
            }

            if (string.Equals(token, "--json-pretty", StringComparison.OrdinalIgnoreCase))
            {
                prettyJson = true;
                continue;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected token '{token}'. Options must start with '--'.";
                return false;
            }

            string key;
            string value;
            var equalIndex = token.IndexOf('=');
            if (equalIndex > 2)
            {
                key = token.Substring(2, equalIndex - 2);
                value = token[(equalIndex + 1)..];
            }
            else
            {
                key = token[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                else
                {
                    value = "true";
                }
            }

            if (string.Equals(key, "timeout-ms", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimeout) ||
                    parsedTimeout <= 0 || parsedTimeout > 120000)
                {
                    error = "--timeout-ms must be in [1, 120000].";
                    return false;
                }

                timeoutMs = parsedTimeout;
                continue;
            }

            commandArgs[key] = value;
        }

        return true;
    }
}
