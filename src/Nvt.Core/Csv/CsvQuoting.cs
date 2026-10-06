// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Csv;

/// <summary>
/// Provides quoting for individual CSV fields.
/// </summary>
public static class CsvQuoting
{
    /// <summary>
    /// Quotes fields containing a comma, double quote, carriage return, or line feed,
    /// doubling embedded double quotes.
    /// </summary>
    /// <param name="value">The field value to quote.</param>
    /// <returns>
    /// The unchanged value when quoting is unnecessary; otherwise, the escaped value
    /// enclosed in double quotes. All other characters are preserved.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
