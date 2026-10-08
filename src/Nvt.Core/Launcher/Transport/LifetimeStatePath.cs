// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Transport;

// Exact lifetime-state spelling is protocol metadata; Files owns native path custody.
internal static class LifetimeStatePath
{
    internal static bool TryNormalizeExactAbsolutePath(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value) ||
            IsDeviceExtendedOrAlternateStream(value))
        {
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(value);
            return PathComparer.Equals(normalized, value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsDeviceExtendedOrAlternateStream(string path)
    {
        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            path.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            return true;
        }
        string? root = Path.GetPathRoot(path);
        return root is null || path.AsSpan(root.Length).Contains(':');
    }

    internal static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
