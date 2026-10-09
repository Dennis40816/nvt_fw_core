// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.LogConsole;

/// <summary>A bounded preview derived on demand, never stored alongside row content.</summary>
/// <param name="Text">The capped first line, excluding CR and LF and without splitting a surrogate pair.</param>
/// <param name="HasMoreContent">Whether any original content, including a line break, follows the preview.</param>
public readonly record struct ConsoleFirstLine(string Text, bool HasMoreContent)
{
    /// <summary>Reads a first-line preview with at most cap + 1 UTF-16 characters of input and 1,024 per read.</summary>
    /// <param name="content">The unchanged segmented content.</param>
    /// <param name="maxCharacters">The UTF-16 output cap, from 0 through 4,096; default 1,024.</param>
    /// <returns>The preview and whether it omits any content.</returns>
    public static ConsoleFirstLine Read(ILogTextContent content, int maxCharacters = 1024)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCharacters, 4096);
        var prefix = new char[Math.Min(content.Length, maxCharacters + 1)];
        for (var offset = 0; offset < prefix.Length; offset += 1024)
            content.Read(offset, prefix.AsSpan(offset, Math.Min(1024, prefix.Length - offset)));
        var lineBreak = prefix.AsSpan().IndexOfAny('\r', '\n');
        var length = Math.Min(maxCharacters, prefix.Length);
        if (lineBreak >= 0) length = Math.Min(length, lineBreak);
        if (length > 0 && length < prefix.Length && char.IsHighSurrogate(prefix[length - 1])
            && char.IsLowSurrogate(prefix[length])) length--;
        return new ConsoleFirstLine(new string(prefix, 0, length), length < content.Length);
    }
}
