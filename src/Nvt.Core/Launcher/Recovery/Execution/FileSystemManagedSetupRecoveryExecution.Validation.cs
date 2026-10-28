// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedSetupRecoveryExecution
{
    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?>
        RevalidateFinalPostconditionAsync(
            FileSystemRecoveryToken token,
            ManagedSetupRecoveryAction action,
            CancellationToken cancellationToken)
    {
        ManagedSetupRecoveryExecutionPortOutcome? actionPostcondition =
            await RevalidateActionPostconditionAsync(
                token,
                action,
                cancellationToken).ConfigureAwait(false);
        if (actionPostcondition is not null)
        {
            return actionPostcondition.Value;
        }
        StateFileObservation<RawFileEvidence> marker = await ObserveRawFileAsync(
            FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(token.ManagedRoot),
            cancellationToken).ConfigureAwait(false);
        return marker.Issue is null &&
            string.Equals(marker.IdentityDigest, token.MarkerIdentity, StringComparison.Ordinal)
                ? null
                : ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?>
        RevalidateActionPostconditionAsync(
            FileSystemRecoveryToken token,
            ManagedSetupRecoveryAction action,
            CancellationToken cancellationToken)
    {
        string expectedApplication = action ==
            ManagedSetupRecoveryAction.RemoveIncompleteInstallation
                ? "missing"
                : token.ApplicationStateIdentity;
        string expectedLauncher = action ==
            ManagedSetupRecoveryAction.RemoveIncompleteInstallation
                ? "missing"
                : token.LauncherStateIdentity;
        if (!string.Equals(
                ObserveRawOrMissingIdentity(_statePath, cancellationToken),
                expectedApplication,
                StringComparison.Ordinal) ||
            !string.Equals(
                ObserveRawOrMissingIdentity(_launcherStatePath, cancellationToken),
                expectedLauncher,
                StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
        }

        StagingObservation staging = await ObserveStagingContainerAsync(
            token.ManagedRoot,
            expectedChild: null,
            token.TransactionId,
            RecoveryTreeLimits(token),
            cancellationToken).ConfigureAwait(false);
        if (staging.Issue is not null ||
            !string.IsNullOrEmpty(staging.Inventory) ||
            !string.Equals(staging.Digest, "absent", StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
        }
        if (action == ManagedSetupRecoveryAction.RemoveIncompleteInstallation &&
            token.Phase == ManagedSetupRecoveryPhase.Staging)
        {
            WindowsStableCustodyResult finalRoot =
                WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                    token.ManagedRoot,
                    WindowsStableTreeLimits.ForRecoverySkeleton(1),
                    cancellationToken);
            finalRoot.Custody?.Dispose();
            if (!finalRoot.IsExactChildMissing)
            {
                return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
            }
        }
        else
        {
            ExecutionTreeObservation tree = await ObserveExactTreeAsync(
                token.TreeRoot,
                RecoveryTreeLimits(token),
                cancellationToken).ConfigureAwait(false);
            if (tree.Issue is not null)
            {
                return tree.Issue;
            }
            bool treeMatches = action == ManagedSetupRecoveryAction.RemoveIncompleteInstallation
                ? tree.Snapshot is null && string.Equals(tree.Digest, "absent", StringComparison.Ordinal)
                : string.Equals(tree.Inventory, token.TreeInventory, StringComparison.Ordinal) &&
                    string.Equals(tree.Digest, token.TreeSha256, StringComparison.Ordinal);
            if (!treeMatches)
            {
                return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
            }
        }
        return null;
    }

    private static ManagedSetupRecoveryEvidenceObservation Failure(
        ManagedSetupRecoveryEvidenceIssue issue)
    {
        return new(issue);
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?> RevalidateTokenAsync(
        FileSystemRecoveryToken token,
        CancellationToken cancellationToken)
    {
        return await RevalidateStaticSnapshotAsync(
            token,
            token.ApplicationStateIdentity,
            token.LauncherStateIdentity,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?> RevalidateStaticSnapshotAsync(
        FileSystemRecoveryToken token,
        string expectedApplicationState,
        string expectedLauncherState,
        CancellationToken cancellationToken,
        bool validateTreeAndStaging = true)
    {
        string markerPath = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(
            token.ManagedRoot);
        StateFileObservation<RawFileEvidence> marker = await ObserveRawFileAsync(
            markerPath,
            cancellationToken).ConfigureAwait(false);
        if (marker.Issue is { } markerIssue)
        {
            return MapExecutionIssue(markerIssue);
        }
        if (!string.Equals(marker.IdentityDigest, token.MarkerIdentity, StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        }
        if (!string.Equals(
                ObserveRawOrMissingIdentity(_statePath, cancellationToken),
                expectedApplicationState,
                StringComparison.Ordinal) ||
            !string.Equals(
                ObserveRawOrMissingIdentity(_launcherStatePath, cancellationToken),
                expectedLauncherState,
                StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        }
        if (!validateTreeAndStaging)
        {
            return token.Phase == ManagedSetupRecoveryPhase.Staging &&
                !IsExactFinalRootMissing(token, cancellationToken)
                ? ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired
                : null;
        }

        bool absentEarlyStaging = token.Phase == ManagedSetupRecoveryPhase.Staging &&
            string.IsNullOrEmpty(token.TreeInventory);
        if (token.Phase == ManagedSetupRecoveryPhase.Staging &&
            !IsExactFinalRootMissing(token, cancellationToken))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
        }
        ExecutionTreeObservation tree = absentEarlyStaging
            ? new(null, string.Empty, "absent", Issue: null)
            : await ObserveExactTreeAsync(
                token.TreeRoot,
                RecoveryTreeLimits(token),
                cancellationToken).ConfigureAwait(false);
        if (tree.Issue is not null)
        {
            return tree.Issue;
        }
        if (!string.Equals(tree.Inventory, token.TreeInventory, StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        }
        if (!string.Equals(tree.Digest, token.TreeSha256, StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        }

        StagingObservation staging = await ObserveStagingContainerAsync(
            token.ManagedRoot,
            token.Phase == ManagedSetupRecoveryPhase.Staging ? tree.Snapshot : null,
            token.TransactionId,
            RecoveryTreeLimits(token),
            cancellationToken).ConfigureAwait(false);
        return staging.Issue is not null ||
            !string.Equals(staging.Inventory, token.StagingInventory, StringComparison.Ordinal) ||
            !string.Equals(staging.Digest, token.StagingSha256, StringComparison.Ordinal)
                ? ManagedSetupRecoveryExecutionPortOutcome.SourceChanged
                : null;
    }

}
