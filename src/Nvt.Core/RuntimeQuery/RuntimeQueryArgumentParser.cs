// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;

namespace Nvt.Core.RuntimeQuery;

/// <summary>Parses generic runtime query arguments with the source tool's rules and error messages.</summary>
/// <remarks>
/// Each helper returns true for a valid value.
/// It returns false with a null error when the key is missing or its value is blank.
/// It returns false with an INVALID_ARGUMENTS error when the value is invalid.
/// </remarks>
public static class RuntimeQueryArgumentParser
{
    /// <summary>Parses an invariant integer within the inclusive range.</summary>
    public static bool TryGetIntArg(
        IReadOnlyDictionary<string, string>? args,
        string key,
        int min,
        int max,
        out int value,
        out RuntimeQueryResponseEnvelope? error)
    {
        value = 0;
        error = null;

        if (args is null || !args.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = RuntimeQueryResponseEnvelope.Failure(
                code: "INVALID_ARGUMENTS",
                message: $"Argument '--{key}' must be an integer.");
            return false;
        }

        if (parsed < min || parsed > max)
        {
            error = RuntimeQueryResponseEnvelope.Failure(
                code: "INVALID_ARGUMENTS",
                message: $"Argument '--{key}' must be in [{min}, {max}].");
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Parses a comma-separated integer list within the inclusive range.</summary>
    public static bool TryGetIntListArg(
        IReadOnlyDictionary<string, string>? args,
        string key,
        int min,
        int max,
        out List<int> values,
        out RuntimeQueryResponseEnvelope? error)
    {
        values = new List<int>();
        error = null;

        if (args is null || !args.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tokens = text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .ToList();
        if (tokens.Count == 0)
        {
            error = RuntimeQueryResponseEnvelope.Failure(
                code: "INVALID_ARGUMENTS",
                message: $"Argument '--{key}' cannot be empty.");
            return false;
        }

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                error = RuntimeQueryResponseEnvelope.Failure(
                    code: "INVALID_ARGUMENTS",
                    message: $"Argument '--{key}' must be a comma-separated integer list.");
                values.Clear();
                return false;
            }

            if (parsed < min || parsed > max)
            {
                error = RuntimeQueryResponseEnvelope.Failure(
                    code: "INVALID_ARGUMENTS",
                    message: $"Argument '--{key}' values must be in [{min}, {max}].");
                values.Clear();
                return false;
            }

            values.Add(parsed);
        }

        return true;
    }

    /// <summary>Parses an invariant finite double within the inclusive range.</summary>
    public static bool TryGetDoubleArg(
        IReadOnlyDictionary<string, string>? args,
        string key,
        double min,
        double max,
        out double value,
        out RuntimeQueryResponseEnvelope? error)
    {
        value = 0.0;
        error = null;

        if (args is null || !args.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
        {
            error = RuntimeQueryResponseEnvelope.Failure(
                code: "INVALID_ARGUMENTS",
                message: $"Argument '--{key}' must be numeric.");
            return false;
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < min || parsed > max)
        {
            error = RuntimeQueryResponseEnvelope.Failure(
                code: "INVALID_ARGUMENTS",
                message: $"Argument '--{key}' must be in [{min}, {max}].");
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Reads a nonblank argument and trims surrounding whitespace.</summary>
    public static bool TryGetStringArg(
        IReadOnlyDictionary<string, string>? args,
        string key,
        out string value,
        out RuntimeQueryResponseEnvelope? error)
    {
        value = string.Empty;
        error = null;

        if (args is null || !args.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        if (value.Length == 0)
        {
            error = RuntimeQueryResponseEnvelope.Failure(
                code: "INVALID_ARGUMENTS",
                message: $"Argument '--{key}' cannot be empty.");
            return false;
        }

        return true;
    }

    /// <summary>Parses true, false, 1, 0, on, off, yes and no, ignoring case.</summary>
    public static bool TryGetBoolArg(
        IReadOnlyDictionary<string, string>? args,
        string key,
        out bool value,
        out RuntimeQueryResponseEnvelope? error)
    {
        value = false;
        error = null;

        if (!TryGetStringArg(args, key, out var raw, out var stringError))
        {
            error = stringError;
            return false;
        }

        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        error = RuntimeQueryResponseEnvelope.Failure(
            code: "INVALID_ARGUMENTS",
            message: $"Argument '--{key}' must be true/false (or 1/0, on/off).");
        return false;
    }
}
