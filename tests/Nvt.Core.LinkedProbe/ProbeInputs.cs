// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.LinkedProbe;

internal sealed class ProbeInputs
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "mode", "protocol-prefix", "marker", "state-path", "boundary", "pause",
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
        : Environment.GetEnvironmentVariable("CORE_LINKED_PROBE_" + name.Replace('-', '_').ToUpperInvariant());

    internal string Required(string name) => Optional(name) ??
        throw new ProbeInputException("Missing input: " + name);
}

internal sealed class ProbeInputException(string message) : Exception(message);
