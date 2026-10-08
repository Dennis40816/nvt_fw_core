// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Strict product state adapters and verified repositories bound to one exact runtime root and state path.</summary>
public sealed record LauncherBootstrapRuntimeServices
{
    /// <summary>Creates mandatory runtime services without schema or admission fallbacks.</summary>
    public LauncherBootstrapRuntimeServices(
        IVersionManagerStateStore applicationStateStore,
        IVersionManagerStateStore seedStateStore,
        ILauncherBootstrapStateStore launcherStateStore,
        IManagedVersionRepository versionRepository,
        IInstalledLauncherRepository launcherRepository)
    {
        ApplicationStateStore = applicationStateStore ?? throw new ArgumentNullException(nameof(applicationStateStore));
        SeedStateStore = seedStateStore ?? throw new ArgumentNullException(nameof(seedStateStore));
        LauncherStateStore = launcherStateStore ?? throw new ArgumentNullException(nameof(launcherStateStore));
        VersionRepository = versionRepository ?? throw new ArgumentNullException(nameof(versionRepository));
        LauncherRepository = launcherRepository ?? throw new ArgumentNullException(nameof(launcherRepository));
    }

    /// <summary>Gets the strict adapter for the exact requested application state path.</summary>
    public IVersionManagerStateStore ApplicationStateStore { get; }
    /// <summary>Gets the strict adapter for the product's explicit packaged seed.</summary>
    public IVersionManagerStateStore SeedStateStore { get; }
    /// <summary>Gets the strict adapter for the corresponding launcher journal.</summary>
    public ILauncherBootstrapStateStore LauncherStateStore { get; }
    /// <summary>Gets the repository that verifies the product's validated managed payloads.</summary>
    public IManagedVersionRepository VersionRepository { get; }
    /// <summary>Gets the repository that verifies exact owner-bound launcher executables.</summary>
    public IInstalledLauncherRepository LauncherRepository { get; }
}
