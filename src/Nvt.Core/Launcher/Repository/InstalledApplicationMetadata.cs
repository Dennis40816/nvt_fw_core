// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Repository;

/// <summary>Verified installed application facts needed by an upper-layer shortcut owner.</summary>
/// <param name="Version">The exact installed application version.</param>
/// <param name="ProductId">The descriptor's exact product identity.</param>
/// <param name="DisplayName">The caller's application display name.</param>
/// <param name="ExecutablePath">The verified version-specific executable path.</param>
/// <param name="LaunchEntryPoint">The caller's stable entry point for launching the named application.</param>
/// <param name="IconPath">The verified installed icon path.</param>
/// <remarks>This read model grants no authority to start a process or create a shortcut.</remarks>
public sealed record InstalledApplicationMetadata(ManagedAppVersion Version, string ProductId, string DisplayName,
    string ExecutablePath, string LaunchEntryPoint, string IconPath);

public sealed partial class FileSystemManagedVersionRepository
{
    /// <summary>Reads verified installed facts for the caller's shortcut presentation.</summary>
    /// <param name="managedRoot">The caller's application install root.</param>
    /// <param name="admission">The caller's exact installed admission.</param>
    /// <param name="displayName">The caller's display name.</param>
    /// <param name="launchEntryPoint">The caller's fully qualified stable launch entry point.</param>
    /// <param name="iconRelativePath">A product-admitted icon member in the installed payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete verified metadata, or null when installation custody cannot be acquired.</returns>
    public async ValueTask<InstalledApplicationMetadata?> ReadInstalledApplicationAsync(string managedRoot,
        ManagedVersionAdmission admission, string displayName, string launchEntryPoint, string iconRelativePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchEntryPoint);
        if (!Path.IsPathFullyQualified(launchEntryPoint))
        {
            throw new ArgumentException("Launch entry point must be fully qualified.", nameof(launchEntryPoint));
        }
        if (!ContractValidation.IsSafeRelativePath(iconRelativePath) || !policy.IsSafeRelativePayloadPath(iconRelativePath))
        {
            throw new ArgumentException("Icon path is unsafe.", nameof(iconRelativePath));
        }
        var acquired = await AcquireApplicationLaunchLeaseAsync(managedRoot, admission, cancellationToken).ConfigureAwait(false);
        using IManagedExecutableLaunchLease? lease = acquired.Lease;
        if (!acquired.IsAcquired)
        {
            return null;
        }
        string versionRoot = RepositoryPathSafety.GetExactVersionDirectory(
            Path.Combine(Path.GetFullPath(managedRoot), VersionsDirectoryName), admission.Version);
        return RepositoryPathSafety.TryResolveRelativeFile(versionRoot, iconRelativePath, out string iconPath) &&
            lease!.TryValidateForStart()
            ? new(admission.Version, descriptor.ProductId, displayName, lease.ExecutablePath,
                Path.GetFullPath(launchEntryPoint), iconPath) : null;
    }
}
