// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Transport;

internal static class LauncherReadyProtocol
{
    internal static string CreateExpectedPrefix(ManagedLauncherIdentity launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        string admissionDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(launcher.OwnerAdmissionIdentity))).ToLowerInvariant();
        return string.Join(
            ':',
            "READY-LAUNCHER",
            launcher.ProtocolVersion,
            launcher.OwnerAppVersion,
            admissionDigest,
            launcher.OwnerReleaseManifestSha256,
            launcher.Sha256);
    }

    internal static string Create(
        ManagedLauncherIdentity launcher,
        ManagedVersionAdmission readyAdmission)
    {
        ArgumentNullException.ThrowIfNull(readyAdmission);
        return string.Join(
            ':',
            CreateExpectedPrefix(launcher),
            readyAdmission.Version,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(readyAdmission.AdmissionIdentity)),
            readyAdmission.ReleaseManifestSha256);
    }

    internal static bool TryParse(
        string? value,
        string expectedPrefix,
        out ManagedVersionAdmission? admission)
    {
        admission = null;
        if (value is null || !value.StartsWith(expectedPrefix + ":", StringComparison.Ordinal))
        {
            return false;
        }
        string[] fields = value[(expectedPrefix.Length + 1)..].Split(':');
        if (fields.Length != 3 ||
            !ManagedAppVersion.TryParse(fields[0], out ManagedAppVersion version) ||
            !IsLowerSha256(fields[2]))
        {
            return false;
        }
        try
        {
            string identity = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
            if (string.IsNullOrWhiteSpace(identity) || identity.Length > 2048)
            {
                return false;
            }
            admission = new(version, identity, fields[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static bool IsExpectedPrefix(string? value)
    {
        if (value is null)
        {
            return false;
        }
        string[] fields = value.Split(':');
        return fields.Length == 6 &&
               string.Equals(fields[0], "READY-LAUNCHER", StringComparison.Ordinal) &&
               int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int protocol) &&
               protocol == ManagedLauncherIdentity.SupportedProtocolVersion &&
               ManagedAppVersion.TryParse(fields[2], out _) &&
               IsLowerSha256(fields[3]) &&
               IsLowerSha256(fields[4]) &&
               IsLowerSha256(fields[5]);
    }

    private static bool IsLowerSha256(string? value)
    {
        return value is { Length: 64 } &&
               value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
