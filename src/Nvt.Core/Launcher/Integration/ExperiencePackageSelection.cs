// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Launcher.Integration;

internal sealed class ExperiencePackageSelection(VersionManagementExperience experience) : IManagedPackageSelection
{
    public ValueTask<UpdateCatalogVersionSnapshot?> SelectPackageAsync(
        VersionManagerState state, ManagedAppVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(experience.SelectPackage(state, version));
    }
}
