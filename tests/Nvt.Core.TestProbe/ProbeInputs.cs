// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;

namespace Nvt.Core.TestProbe;

internal sealed class ProbeInputs
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "mode", "marker", "text", "ambient-handle", "allowed-handle", "cross-handle", "payload",
        "tree-marker", "stdout-text", "exit-code", "app-version", "app-admission", "app-manifest",
        "args-path", "process-marker", "identity-marker", "oversize-chars", "partial-drop", "lock-path", "lock-ready",
    };
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    internal ProbeInputs(string[] args)
    {
        var payload = new List<string>();
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument.StartsWith("--", StringComparison.Ordinal) && Names.Contains(argument[2..]))
            {
                if (++index == args.Length)
                {
                    throw new ProbeInputException("Missing input: " + argument[2..]);
                }
                _values[argument[2..]] = args[index];
            }
            else
            {
                payload.Add(argument);
            }
        }
        Payload = payload.ToArray();
    }

    internal string[] Payload { get; }

    internal string? Optional(string name) => _values.TryGetValue(name, out string? value)
        ? value
        : Environment.GetEnvironmentVariable("CORE_TEST_PROBE_" + name.Replace('-', '_').ToUpperInvariant());

    internal string Required(string name) => Optional(name) ??
        throw new ProbeInputException("Missing input: " + name);

    internal int Integer(string name, int defaultValue, int minimum = int.MinValue)
    {
        string? value = Optional(name);
        if (value is null)
        {
            return defaultValue;
        }
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < minimum)
        {
            throw new ProbeInputException("Invalid input: " + name);
        }
        return number;
    }

    internal static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name) ??
        throw new ProbeInputException("Missing input: " + name);
}

internal sealed class ProbeInputException(string message) : Exception(message);
