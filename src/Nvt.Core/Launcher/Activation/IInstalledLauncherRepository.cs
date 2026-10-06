// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Verifies exact owner-bound launcher content and acquires executable custody.</summary>
public interface IInstalledLauncherRepository
{
    /// <summary>Verifies the launcher belonging to the exact admitted application.</summary>
    ValueTask<InstalledLauncherResult> VerifyAsync(
        string managedRoot,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken);

    /// <summary>Holds the exact verified owner-bound executable through launch.</summary>
    ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(
        string managedRoot,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken);
}
