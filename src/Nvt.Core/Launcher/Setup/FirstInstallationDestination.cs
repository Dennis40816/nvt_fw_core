// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;

using static Nvt.Core.Launcher.Setup.FirstInstallationOutcomes;

namespace Nvt.Core.Launcher.Setup;

internal static class FirstInstallationDestination
{
    internal static async ValueTask<ManagedFirstInstallationOutcome?> ObserveAsync(
        FirstInstallationServices services,
        string managedRoot,
        CancellationToken cancellationToken)
    {
        VersionManagerStateLoadResult state = await services.StateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (state.Issue != VersionManagerStateLoadIssue.Missing)
        {
            return MapNonMissingState(state);
        }

        ManagedInstallationRootObservation root = await services.RootProbe.ObserveAsync(
            managedRoot,
            cancellationToken).ConfigureAwait(false);
        if (root.Status != ManagedInstallationRootStatus.Absent)
        {
            return MapRootStatus(root.Status);
        }

        ManagedFirstInstallationMaterializationIssue destination = await services.RootMaterializer
            .AdmitDestinationAsync(managedRoot, cancellationToken).ConfigureAwait(false);
        if (destination != ManagedFirstInstallationMaterializationIssue.None)
        {
            return MapDestinationAdmissionIssue(destination);
        }

        return null;
    }
}
