// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Hosting;

/// <summary>Resolves the operating-system folders that tools use for local state.</summary>
/// <remarks>This type is the one place in Core that reads the operating-system folder table. Hosts call it instead of calling <c>Environment.GetFolderPath</c>.</remarks>
public static class KnownFolders
{
    /// <summary>Gets the per-user folder for application data that does not roam.</summary>
    /// <returns>The folder path. It is empty only when the operating system has no such folder for the current user.</returns>
    public static string GetLocalApplicationData()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
}
