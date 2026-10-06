// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>Checks requests and routes command names to the caller's handlers.</summary>
public sealed class RuntimeQueryCommandRouter
{
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>> _handlers;

    /// <summary>Creates a router from the caller's completed handler table.</summary>
    public RuntimeQueryCommandRouter(
        IReadOnlyDictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>> handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        RegisteredCommands = Array.AsReadOnly(handlers.Keys.ToArray());
    }

    /// <summary>The registered names in the handler table's enumeration order at construction.</summary>
    public IReadOnlyList<string> RegisteredCommands { get; }

    /// <summary>Checks for a null request, checks the caller's version, then routes the command.</summary>
    public async Task<RuntimeQueryResponseEnvelope> ExecuteAsync(RuntimeQueryRequest? request, string expectedVersion)
    {
        if (request is null)
        {
            return RuntimeQueryResponseEnvelope.Failure(
                code: "INVALID_REQUEST",
                message: "Request is null.");
        }

        if (!string.Equals(request.Version, expectedVersion, StringComparison.Ordinal))
        {
            return RuntimeQueryResponseEnvelope.Failure(
                code: "UNSUPPORTED_VERSION",
                message: $"Unsupported request version '{request.Version}'. Expected '{expectedVersion}'.");
        }

        return await RouteAsync(request.Command, request.Args);
    }

    /// <summary>Trims and lowercases the command with invariant culture, then passes arguments unchanged.</summary>
    public Task<RuntimeQueryResponseEnvelope> RouteAsync(string? commandText, IReadOnlyDictionary<string, string>? args)
    {
        var command = commandText?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(command) || !_handlers.TryGetValue(command, out var handler))
        {
            return Task.FromResult(RuntimeQueryResponseEnvelope.Failure(
                code: "UNKNOWN_COMMAND",
                message: $"Unknown query command '{commandText}'."));
        }

        return handler(args);
    }
}
