// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Integration;

/// <summary>Resolves the stable managed root independently of package identity.</summary>
public static class ManagedInstallationLayout
{
    /// <summary>Resolves a side-by-side payload base or preserves an unmanaged development base.</summary>
    /// <param name="applicationBaseDirectory">Running payload base directory.</param>
    /// <returns>The stable managed root.</returns>
    public static string ResolveManagedRoot(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        var payload = new DirectoryInfo(Path.GetFullPath(applicationBaseDirectory));
        DirectoryInfo? versions = payload.Parent;
        return ManagedAppVersion.TryParse(payload.Name, out _) &&
               versions is not null &&
               string.Equals(versions.Name, FileSystemManagedVersionRepository.VersionsDirectoryName,
                   StringComparison.OrdinalIgnoreCase) &&
               versions.Parent is not null
            ? versions.Parent.FullName
            : payload.FullName;
    }
}
