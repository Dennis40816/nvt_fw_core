// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Contracts;

// Structural checks only. The product adapter remains the payload/schema policy owner.
internal static class ContractValidation
{
    // The frozen NFC relative-path grammar admits at most 512 characters.
    internal const int MaximumRelativePathCharacters = 512;

    internal static bool IsLowerSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    internal static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumRelativePathCharacters ||
            value.Any(static character => character is < ' ' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*'))
        {
            return false;
        }

        return value.Split('/').All(IsSafeSegment);
    }

    internal static bool IsSafeFileName(string? value) =>
        IsSafeRelativePath(value) && !value!.Contains('/', StringComparison.Ordinal);

    private static bool IsSafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
            segment.EndsWith('.') || segment.EndsWith(' '))
        {
            return false;
        }

        ReadOnlySpan<char> stem = segment.AsSpan();
        int dot = stem.IndexOf('.');
        if (dot >= 0)
        {
            stem = stem[..dot];
        }

        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return stem.Length != 4 || stem[3] is < '1' or > '9' ||
            (!stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) &&
             !stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase));
    }
}
