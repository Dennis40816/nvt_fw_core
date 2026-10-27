// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedSetupRecoveryExecution
{
    private async ValueTask<PreparedRollbackTree> PrepareRollbackTreeAsync(
        FileSystemRecoveryToken token, CancellationToken cancellationToken)
    {
        bool absentTree = string.IsNullOrEmpty(token.TreeInventory) &&
            string.Equals(token.TreeSha256, "absent", StringComparison.Ordinal);
        WindowsStablePathCustody? custody = null;
        bool transferred = false;
        try
        {
            WindowsStableOwnedTreeSnapshot? snapshot = null;
            if (absentTree)
            {
                if (token.Phase != ManagedSetupRecoveryPhase.Staging)
                {
                    var missing = WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                        token.TreeRoot, WindowsStableTreeLimits.ForRecoverySkeleton(0), cancellationToken);
                    missing.Custody?.Dispose();
                    if (!missing.IsExactChildMissing)
                        return new(null, missing.Issue == WindowsStableCustodyIssue.None
                            ? ManagedSetupRecoveryExecutionPortOutcome.SourceChanged
                            : MapExecutionIssue(missing.Issue));
                }
            }
            else
            {
                var acquired = WindowsStablePathCustody.TryAcquireDeleteCapableTree(
                    token.TreeRoot, RecoveryTreeLimits(token), cancellationToken);
                custody = acquired.Custody;
                if (!acquired.IsAcquired) return new(null, MapExecutionIssue(acquired.Issue));
                if (!custody!.TryCreateOwnedSnapshot(out snapshot))
                    return new(null, ManagedSetupRecoveryExecutionPortOutcome.SourceChanged);
                string proof = await SecureSnapshotProofAsync(custody, snapshot!, cancellationToken,
                    _afterHashedFile).ConfigureAwait(false);
                if (!string.Equals(proof, token.TreeInventory, StringComparison.Ordinal) ||
                    !string.Equals(ProofDigest(proof), token.TreeSha256, StringComparison.Ordinal) ||
                    !RepositoryPathSafety.PathComparer.Equals(
                        TreeSteps(snapshot!, ManagedAppVersion.Parse(token.Version)), token.TreeSteps))
                    return new(null, ManagedSetupRecoveryExecutionPortOutcome.SourceChanged);
            }
            var staging = await ObserveStagingContainerAsync(token.ManagedRoot,
                token.Phase == ManagedSetupRecoveryPhase.Staging ? snapshot : null,
                token.TransactionId, RecoveryTreeLimits(token), cancellationToken).ConfigureAwait(false);
            if (staging.Issue is not null ||
                !string.Equals(staging.Inventory, token.StagingInventory, StringComparison.Ordinal) ||
                !string.Equals(staging.Digest, token.StagingSha256, StringComparison.Ordinal))
                return new(null, staging.Issue is null ? ManagedSetupRecoveryExecutionPortOutcome.SourceChanged
                    : MapExecutionIssue(staging.Issue.Value));
            if (token.Phase != ManagedSetupRecoveryPhase.Staging && custody is not null &&
                !custody.RevalidateClosedTree())
                return new(null, ManagedSetupRecoveryExecutionPortOutcome.SourceChanged);
            transferred = true;
            return new(custody, null);
        }
        finally
        {
            if (!transferred) custody?.Dispose();
        }
    }
    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?> DeleteTreeAsync(
        FileSystemRecoveryToken token,
        WindowsStablePathCustody? custody,
        RecoveryMutationProgress progress,
        CancellationToken cancellationToken)
    {
        string[] steps = SplitLines(token.TreeSteps);
        if (steps.Length == 0)
        {
            return string.IsNullOrEmpty(token.TreeInventory) &&
                string.Equals(token.TreeSha256, "absent", StringComparison.Ordinal)
                ? null
                : ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired;
        }
        if (custody is null)
        {
            return ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired;
        }
        string manifest = string.Join('/', FileSystemManagedVersionRepository.VersionsDirectoryName,
            token.Version, "RELEASE-MANIFEST.json");
        foreach (string step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSetupRecoveryExecutionPortOutcome? progressIssue =
                await RevalidateRollbackTreeStepAsync(
                    token,
                    treeCustodyHeld: true,
                    cancellationToken).ConfigureAwait(false);
            if (progressIssue is not null)
            {
                return progressIssue.Value;
            }
            string relative = step.Length == 2 ? string.Empty : step[2..];
            if (step[0] == 'F' && RepositoryPathSafety.PathComparer.Equals(relative, manifest) &&
                !HasExactAuthorityRemainder(custody, manifest, token.Version))
                return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
            if (step[0] == 'F')
                relative = Path.GetRelativePath(custody.RootPath, custody.GetAbsoluteFilePath(relative)).Replace('\\', '/');
            WindowsStableCustodyIssue deleted = custody.TryDeleteHeldEntry(
                relative,
                directory: step[0] == 'D');
            if (deleted != WindowsStableCustodyIssue.None)
            {
                return MapExecutionIssue(deleted);
            }
            progress.DeleteCount++;
            _afterDelete?.Invoke(progress.DeleteCount);
            cancellationToken.ThrowIfCancellationRequested();
        }
        WindowsStableCustodyResult remaining =
            WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                token.TreeRoot,
                WindowsStableTreeLimits.ForRecoverySkeleton(0),
                cancellationToken);
        remaining.Custody?.Dispose();
        return remaining.IsExactChildMissing
            ? null
            : ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired;
    }

    private static bool HasExactAuthorityRemainder(
        WindowsStablePathCustody custody, string manifest, string version)
    {
        if (!custody.RevalidateClosedTree() ||
            !custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot)) return false;
        string versions = FileSystemManagedVersionRepository.VersionsDirectoryName;
        return snapshot!.Files.Count == 1 && snapshot.Files.ContainsKey(manifest) &&
            snapshot.Directories.Keys.ToHashSet(RepositoryPathSafety.PathComparer)
                .SetEquals([string.Empty, versions, string.Concat(versions, "/", version)]);
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?>
        RevalidateRollbackTreeStepAsync(
            FileSystemRecoveryToken token,
            bool treeCustodyHeld,
            CancellationToken cancellationToken)
    {
        if (!string.Equals(
                ObserveRawOrMissingIdentity(_statePath, cancellationToken),
                "missing",
                StringComparison.Ordinal) ||
            !string.Equals(
                ObserveRawOrMissingIdentity(_launcherStatePath, cancellationToken),
                "missing",
                StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
        }
        if (token.Phase == ManagedSetupRecoveryPhase.Staging &&
            !IsExactFinalRootMissing(token, cancellationToken))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
        }
        StateFileObservation<RawFileEvidence> marker = await ObserveRawFileAsync(
            FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(token.ManagedRoot),
            cancellationToken).ConfigureAwait(false);
        if (marker.Issue is not null ||
            !string.Equals(marker.IdentityDigest, token.MarkerIdentity, StringComparison.Ordinal))
        {
            return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        }
        if (treeCustodyHeld)
        {
            return null;
        }
        StagingObservation staging = await ObserveStagingContainerAsync(
            token.ManagedRoot,
            expectedChild: null,
            token.TransactionId,
            RecoveryTreeLimits(token),
            cancellationToken).ConfigureAwait(false);
        if (staging.Issue is not null)
        {
            return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
        }
        string? expectedStaging = token.StagingInventory;
        return expectedStaging is not null &&
            string.Equals(staging.Inventory, expectedStaging, StringComparison.Ordinal) &&
            string.Equals(staging.Digest, ProofOrAbsentDigest(expectedStaging), StringComparison.Ordinal)
                ? null
                : ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
    }

    private static string ProofOrAbsentDigest(string proof)
    {
        return string.IsNullOrEmpty(proof) ? "absent" : ProofDigest(proof);
    }

    private static bool IsExactFinalRootMissing(
        FileSystemRecoveryToken token,
        CancellationToken cancellationToken)
    {
        WindowsStableCustodyResult finalRoot =
            WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                token.ManagedRoot,
                WindowsStableTreeLimits.ForRecoverySkeleton(1),
                cancellationToken);
        finalRoot.Custody?.Dispose();
        return finalRoot.IsExactChildMissing;
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?> DeleteEmptyStagingAsync(
        FileSystemRecoveryToken token,
        RecoveryMutationProgress progress,
        CancellationToken cancellationToken)
    {
        StagingObservation observed = await ObserveStagingContainerAsync(
            token.ManagedRoot,
            expectedChild: null,
            token.TransactionId,
            RecoveryTreeLimits(token),
            cancellationToken).ConfigureAwait(false);
        if (observed.Issue is not null)
        {
            return ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired;
        }
        string expected = string.Join('\n', SplitLines(token.StagingInventory)
            .Where(static line => line.StartsWith("D||", StringComparison.Ordinal)));
        if (!string.Equals(observed.Inventory, expected, StringComparison.Ordinal) ||
            !string.Equals(observed.Digest, ProofOrAbsentDigest(expected), StringComparison.Ordinal))
            return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        if (string.Equals(observed.Digest, "absent", StringComparison.Ordinal))
        {
            return null;
        }
        string container = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(
            token.ManagedRoot);
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireDeleteCapableTree(
            container,
            WindowsStableTreeLimits.ForRecoverySkeleton(1),
            cancellationToken);
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            return MapExecutionIssue(acquired.Issue);
        }
        WindowsStableCustodyIssue deleted;
        using (WindowsStablePathCustody custody = acquired.Custody!)
        {
            if (!custody.RevalidateClosedTree() ||
                !custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot) ||
                !string.Equals(await SecureSnapshotProofAsync(custody, snapshot!, cancellationToken)
                    .ConfigureAwait(false), expected, StringComparison.Ordinal))
                return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
            deleted = custody.TryDeleteHeldEntry(string.Empty, directory: true);
        }
        if (deleted != WindowsStableCustodyIssue.None)
        {
            return MapExecutionIssue(deleted);
        }
        progress.DeleteCount++;
        _afterDelete?.Invoke(progress.DeleteCount);
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private ManagedSetupRecoveryExecutionPortOutcome? DeleteExactFileIfPresent(
        string path,
        string expectedIdentity,
        RecoveryMutationProgress progress,
        CancellationToken cancellationToken,
        bool missingIsComplete = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(expectedIdentity, "missing", StringComparison.Ordinal))
        {
            return string.Equals(
                    ObserveRawOrMissingIdentity(path, cancellationToken),
                    "missing",
                    StringComparison.Ordinal)
                ? null
                : ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        }
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireDeleteCapableFile(
            path,
            cancellationToken);
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            return acquired.IsExactChildMissing && missingIsComplete
                ? null
                : ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
        }
        WindowsStableCustodyIssue deleted;
        using (WindowsStablePathCustody custody = acquired.Custody!)
        {
            using FileStream stream = custody.OpenReadOnlyFile(Path.GetFileName(path));
            string digest = LowerHex(SHA256.HashData(stream));
            if (!custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot) ||
                !string.Equals(
                    string.Concat(TreeDigest(snapshot!), ":", digest),
                    expectedIdentity,
                    StringComparison.Ordinal))
            {
                return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
            }
            deleted = custody.TryDeleteHeldEntry(
                Path.GetFileName(path),
                directory: false,
                revalidateTopology: false);
        }
        if (deleted != WindowsStableCustodyIssue.None)
        {
            return MapExecutionIssue(deleted);
        }
        progress.DeleteCount++;
        if (!missingIsComplete)
        {
            _afterDelete?.Invoke(progress.DeleteCount);
            return WindowsStablePathCustody.TryObserveExactMissingFile(path, CancellationToken.None)
                ? null
                : ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired;
        }
        _afterDelete?.Invoke(progress.DeleteCount);
        cancellationToken.ThrowIfCancellationRequested();
        return WindowsStablePathCustody.TryObserveExactMissingFile(path, cancellationToken)
            ? null
            : ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired;
    }

}
