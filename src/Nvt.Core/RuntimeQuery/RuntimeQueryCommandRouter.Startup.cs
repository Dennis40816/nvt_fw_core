// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

public sealed partial class RuntimeQueryCommandRouter
{
    /// <summary>Takes only registered startup options and the enabled guard's confirmation flag. Runs no handlers.</summary>
    public RuntimeQueryStartupParseResult ParseStartupArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (_commands is null || !_commands.Values.Any(command => command.StartupPhase != RuntimeQueryStartupPhase.None))
        {
            return new(Array.Empty<RuntimeQueryStartupCall>(), Array.AsReadOnly(arguments.ToArray()),
                Array.Empty<RuntimeQueryStartupIssue>());
        }
        var pending = new List<(RuntimeQueryStartupCall Call, bool GrammarValid)>();
        var remaining = new List<string>();
        var issues = new List<RuntimeQueryStartupIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var confirmed = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                remaining.Add(token);
                continue;
            }

            var separator = token.IndexOf('=');
            var name = separator < 0 ? token[2..] : token[2..separator];
            var option = $"--{name}";
            var isConfirmation = _requireConfirmation && string.Equals(name, "confirm", StringComparison.Ordinal);
            RuntimeQueryCommand? command = null;
            if (!isConfirmation && (_commands is null || !_commands.TryGetValue(name, out command) ||
                command.StartupPhase == RuntimeQueryStartupPhase.None))
            {
                remaining.Add(token);
                continue;
            }

            var grammarValid = seen.Add(name);
            if (!grammarValid)
            {
                issues.Add(new(option, $"{option} is given more than once."));
            }

            IReadOnlyDictionary<string, string>? args = null;
            if (isConfirmation || command!.StartupValueKey is null)
            {
                if (separator >= 0)
                {
                    grammarValid = false;
                    issues.Add(new(option, $"{option} does not take a value."));
                }
                else if (isConfirmation)
                {
                    confirmed = true;
                }
            }
            else
            {
                string? value = null;
                if (separator >= 0)
                {
                    value = token[(separator + 1)..];
                }
                else if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = arguments[++index];
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    grammarValid = false;
                    issues.Add(new(option, $"{option} requires a value."));
                }
                else
                {
                    args = new Dictionary<string, string>(StringComparer.Ordinal) { [command.StartupValueKey] = value };
                }
            }

            if (!isConfirmation)
            {
                pending.Add((new(command!, args), grammarValid));
            }
        }

        var calls = new List<RuntimeQueryStartupCall>();
        foreach (var (call, grammarValid) in pending)
        {
            calls.Add(call with { Confirmed = confirmed });
            var option = $"--{call.Command.Name}";
            if (_requireConfirmation && call.Command.Risk == RuntimeQueryCommandRisk.WritesData && !confirmed)
            {
                issues.Add(new(option, $"{option} writes files or changes data. Add --confirm to use it."));
            }

            if (grammarValid && call.Command.StartupValidator?.Invoke(call.Args) is { Ok: false } failure)
            {
                issues.Add(new(option, failure.Error!.Message));
            }
        }

        return new(calls.AsReadOnly(), remaining.AsReadOnly(), issues.AsReadOnly());
    }

    /// <summary>Runs one startup phase in command-line order and stops after the first failed response.</summary>
    /// <remarks>The tool checks parse issues and cross-option rules before calling this method.</remarks>
    public async Task<IReadOnlyList<RuntimeQueryStartupCallResult>> ExecuteStartupPhaseAsync(
        IReadOnlyList<RuntimeQueryStartupCall> calls, RuntimeQueryStartupPhase phase)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var results = new List<RuntimeQueryStartupCallResult>();
        foreach (var call in calls)
        {
            if (call.Phase != phase || phase == RuntimeQueryStartupPhase.None)
            {
                continue;
            }

            var response = await RouteCoreAsync(call.Command.Name, call.Args, isStartup: true, startupConfirmed: call.Confirmed);
            results.Add(new(call, response));
            if (!response.Ok)
            {
                break;
            }
        }

        return results.AsReadOnly();
    }
}
