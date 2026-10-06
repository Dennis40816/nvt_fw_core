// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>Checks requests and routes command names to the caller's handlers.</summary>
public sealed class RuntimeQueryCommandRouter
{
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>> _handlers;
    private readonly Dictionary<string, RuntimeQueryCommandRisk>? _risks;
    private readonly bool _requireConfirmation;

    /// <summary>Creates a router from the caller's completed handler table.</summary>
    public RuntimeQueryCommandRouter(
        IReadOnlyDictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>> handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        RegisteredCommands = Array.AsReadOnly(handlers.Keys.ToArray());
    }

    /// <summary>Creates an ordinal handler table from commands in registration order.</summary>
    /// <param name="commands">Commands with trimmed, lowercase invariant names and nonnull handlers.</param>
    /// <param name="requireConfirmation">Whether commands that write files or change data require confirmation.</param>
    public RuntimeQueryCommandRouter(IReadOnlyList<RuntimeQueryCommand> commands, bool requireConfirmation)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var handlers = new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>(StringComparer.Ordinal);
        var risks = new Dictionary<string, RuntimeQueryCommandRisk>(StringComparer.Ordinal);
        var names = new string[commands.Count];
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(command.Name);
            ArgumentNullException.ThrowIfNull(command.Handler);
            if (!string.Equals(command.Name, command.Name.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Command names must be trimmed and lowercase invariant.", nameof(commands));
            }

            handlers.Add(command.Name, command.Handler);
            risks.Add(command.Name, command.Risk);
            names[index] = command.Name;
        }

        _handlers = handlers;
        _risks = risks;
        _requireConfirmation = requireConfirmation;
        RegisteredCommands = Array.AsReadOnly(names);
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

    /// <summary>Normalizes the command name, applies the enabled confirmation guard, then calls the handler.</summary>
    public Task<RuntimeQueryResponseEnvelope> RouteAsync(string? commandText, IReadOnlyDictionary<string, string>? args)
    {
        var command = commandText?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(command) || !_handlers.TryGetValue(command, out var handler))
        {
            return Task.FromResult(RuntimeQueryResponseEnvelope.Failure(
                code: "UNKNOWN_COMMAND",
                message: $"Unknown query command '{commandText}'."));
        }

        if (_requireConfirmation)
        {
            if (_risks![command] == RuntimeQueryCommandRisk.WritesData)
            {
                var hasConfirmation = RuntimeQueryArgumentParser.TryGetBoolArg(args, "confirm", out var confirmed, out var error);
                if (error is not null)
                {
                    return Task.FromResult(error);
                }

                if (!hasConfirmation || !confirmed)
                {
                    return Task.FromResult(RuntimeQueryResponseEnvelope.Failure(
                        code: "CONFIRMATION_REQUIRED",
                        message: $"Command '{command}' writes files or changes data. Add --confirm to run it."));
                }
            }

            if (args is not null)
            {
                var handlerArgs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var argument in args)
                {
                    if (!string.Equals(argument.Key, "confirm", StringComparison.Ordinal))
                    {
                        handlerArgs.Add(argument.Key, argument.Value);
                    }
                }

                args = handlerArgs.Count == 0 ? null : handlerArgs;
            }
        }

        return handler(args);
    }
}
