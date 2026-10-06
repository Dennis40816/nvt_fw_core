// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;

namespace Nvt.Core.Launcher.Contracts;

/// <summary>Canonical stable three-component application semantic version.</summary>
public readonly record struct ManagedAppVersion : IComparable<ManagedAppVersion>
{
    private ManagedAppVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>Gets the major version component.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version component.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version component.</summary>
    public int Patch { get; }

    /// <summary>Parses a canonical stable three-component semantic version.</summary>
    /// <param name="value">Version text in <c>major.minor.patch</c> form.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="FormatException">The value is not canonical.</exception>
    public static ManagedAppVersion Parse(string value)
    {
        return TryParse(value, out ManagedAppVersion version)
            ? version
            : throw new FormatException($"'{value}' is not a canonical stable semantic version.");
    }

    /// <summary>Attempts to parse a canonical stable three-component semantic version.</summary>
    /// <param name="value">Candidate version text.</param>
    /// <param name="version">The parsed version when successful.</param>
    /// <returns>Whether parsing succeeds.</returns>
    public static bool TryParse(string? value, out ManagedAppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value) ||
            !Version.TryParse(value, out Version? parsed) ||
            parsed.Major < 0 ||
            parsed.Minor < 0 ||
            parsed.Build < 0 ||
            parsed.Revision >= 0 ||
            !string.Equals(parsed.ToString(3), value, StringComparison.Ordinal))
        {
            return false;
        }

        version = new(parsed.Major, parsed.Minor, parsed.Build);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(ManagedAppVersion other)
    {
        int major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        int minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
    }

    /// <summary>Returns whether the left version is older than the right version.</summary>
    public static bool operator <(ManagedAppVersion left, ManagedAppVersion right) => left.CompareTo(right) < 0;

    /// <summary>Returns whether the left version is newer than the right version.</summary>
    public static bool operator >(ManagedAppVersion left, ManagedAppVersion right) => left.CompareTo(right) > 0;

    /// <summary>Returns whether the left version is no newer than the right version.</summary>
    public static bool operator <=(ManagedAppVersion left, ManagedAppVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Returns whether the left version is no older than the right version.</summary>
    public static bool operator >=(ManagedAppVersion left, ManagedAppVersion right) => left.CompareTo(right) >= 0;
}
