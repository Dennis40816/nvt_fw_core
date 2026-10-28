// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedSetupRecoveryExecution
{
    private async ValueTask<PrefixObservation> ObserveMarkerDerivedPrefixAsync(
        ManagedSetupRecoveryTransaction transaction,
        ManagedVersionAdmission admission,
        ManagedAppVersion version,
        string treeRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            if (transaction.Phase == ManagedSetupRecoveryPhase.Staging)
            {
                PrefixObservation early = await ObserveEarlyStagingAsync(transaction, cancellationToken).ConfigureAwait(false);
                if (early.IsExact || early.Issue is not null) return early;
            }
            WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                treeRoot, WindowsStableTreeLimits.ForRecoverySkeleton(3), cancellationToken);
            if (acquired.IsExactChildMissing)
                return await ObserveAbsentTreePrefixAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (!acquired.IsAcquired)
            {
                acquired.Custody?.Dispose();
                return acquired.Issue != WindowsStableCustodyIssue.Unavailable
                    ? new(false, null, null, null, null, null, MapEvidenceIssue(acquired.Issue))
                    : await ObserveBroadPrefixAsync(transaction, admission, version, treeRoot, cancellationToken).ConfigureAwait(false);
            }
            using WindowsStablePathCustody custody = acquired.Custody!;
            return await ObserveTerminalSkeletonAsync(transaction, version, custody, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.StateUnavailable);
        }
    }

    private async ValueTask<PrefixObservation> ObserveEarlyStagingAsync(
        ManagedSetupRecoveryTransaction transaction, CancellationToken cancellationToken)
    {
        WindowsStableCustodyResult finalRoot =
            WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                transaction.ManagedRootIdentity,
                WindowsStableTreeLimits.ForRecoverySkeleton(1),
                cancellationToken);
        finalRoot.Custody?.Dispose();
        if (!finalRoot.IsExactChildMissing)
        {
            return new(
                false,
                null,
                null,
                null,
                null,
                null,
                finalRoot.Issue == WindowsStableCustodyIssue.AccessDenied
                ? ManagedSetupRecoveryEvidenceIssue.PermissionDenied
                : ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        StagingObservation staging = await ObserveStagingContainerAsync(
            transaction.ManagedRootIdentity,
            expectedChild: null,
            transaction.TransactionId,
            RecoveryTreeLimits(transaction.Payload),
            cancellationToken).ConfigureAwait(false);
        return staging.Issue is ManagedSetupRecoveryEvidenceIssue.Invalid
            ? new(false, null, null, null, null, null, Issue: null)
            : staging.Issue is { } issue
                ? new(false, null, null, null, null, null, issue)
            : staging.IsEmptyOrAbsent
                ? new(
                true,
                null,
                "absent",
                string.Empty,
                staging.Inventory,
                staging.Digest,
                Issue: null)
                : new(false, null, null, null, null, null, Issue: null);
    }

    private async ValueTask<PrefixObservation> ObserveAbsentTreePrefixAsync(
        ManagedSetupRecoveryTransaction transaction, CancellationToken cancellationToken)
    {
        StagingObservation staging = await ObserveStagingContainerAsync(
            transaction.ManagedRootIdentity,
            expectedChild: null,
            transaction.TransactionId,
            RecoveryTreeLimits(transaction.Payload),
            cancellationToken).ConfigureAwait(false);
        return staging.Issue is { } issue
            ? new(false, null, null, null, null, null, issue)
            : staging.IsEmptyOrAbsent
                ? new(
                true,
                null,
                "absent",
                string.Empty,
                staging.Inventory,
                staging.Digest,
                Issue: null)
                : new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.Invalid);
    }

    private async ValueTask<PrefixObservation> ObserveBroadPrefixAsync(
        ManagedSetupRecoveryTransaction transaction, ManagedVersionAdmission admission, ManagedAppVersion version,
        string treeRoot, CancellationToken cancellationToken)
    {
        var broadLimits = new WindowsStableTreeLimits(
            checked(_payloadTreeLimits.MaximumFiles + 3),
            checked(_payloadTreeLimits.MaximumDirectories + 3),
            checked(
                _payloadTreeLimits.MaximumBytes +
                transaction.Payload.LauncherSize +
                transaction.Payload.BootstrapSize +
                (64 * 1024)));
        WindowsStableCustodyResult broad = WindowsStablePathCustody.TryAcquireImmutableTree(
            treeRoot,
            limits: broadLimits,
            cancellationToken: cancellationToken);
        if (!broad.IsAcquired)
        {
            broad.Custody?.Dispose();
            return new(false, null, null, null, null, null, MapEvidenceIssue(broad.Issue));
        }
        using WindowsStablePathCustody broadCustody = broad.Custody!;
        if (!broadCustody.RevalidateClosedTree() ||
            !broadCustody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? broadSnapshot))
        {
            return new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }
        string authorityManifest = string.Join(
            '/',
            FileSystemManagedVersionRepository.VersionsDirectoryName,
            version.ToString(),
            "RELEASE-MANIFEST.json");
        if (!broadSnapshot!.Files.ContainsKey(authorityManifest))
        {
            return new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        if (HasCompleteTopLevel(broadSnapshot))
        {
            return new(false, null, null, null, null, null, Issue: null);
        }
        bool reachable = await VerifyRetainedManifestPrefixAsync(
            broadCustody,
            broadSnapshot,
            transaction,
            admission,
            version,
            authorityManifest,
            cancellationToken).ConfigureAwait(false);
        if (!reachable)
        {
            return new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        string broadInventory = await SecureSnapshotProofAsync(
            broadCustody,
            broadSnapshot,
            cancellationToken).ConfigureAwait(false);
        StagingObservation prefixStaging = await ObserveStagingContainerAsync(
            transaction.ManagedRootIdentity,
            transaction.Phase == ManagedSetupRecoveryPhase.Staging ? broadSnapshot : null,
            transaction.TransactionId,
            RecoveryTreeLimits(transaction.Payload),
            cancellationToken).ConfigureAwait(false);
        return prefixStaging.Issue is { } prefixStagingIssue
            ? new(false, null, null, null, null, null, prefixStagingIssue)
            : new(
                true,
                broadSnapshot,
                ProofDigest(broadInventory),
                broadInventory,
                prefixStaging.Inventory,
                prefixStaging.Digest,
                Issue: null);
    }

    private async ValueTask<PrefixObservation> ObserveTerminalSkeletonAsync(
        ManagedSetupRecoveryTransaction transaction, ManagedAppVersion version,
        WindowsStablePathCustody custody, CancellationToken cancellationToken)
    {
        if (!custody.RevalidateClosedTree() ||
            !custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot))
        {
            return new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }
        string versionPath = string.Join('/',
            FileSystemManagedVersionRepository.VersionsDirectoryName,
            version.ToString());
        var remainingDirectories =
            snapshot!.Directories.Keys.ToHashSet(RepositoryPathSafety.PathComparer);
        bool exactSkeleton = snapshot.Files.Count == 0 &&
            (remainingDirectories.SetEquals(
                [string.Empty, FileSystemManagedVersionRepository.VersionsDirectoryName, versionPath]) ||
             remainingDirectories.SetEquals(
                [string.Empty, FileSystemManagedVersionRepository.VersionsDirectoryName]) ||
             remainingDirectories.SetEquals([string.Empty]));
        if (!exactSkeleton)
        {
            return new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        StagingObservation residue = await ObserveStagingResidueAsync(
            transaction, snapshot, cancellationToken).ConfigureAwait(false);
        string terminalInventory = await SecureSnapshotProofAsync(
            custody,
            snapshot,
            cancellationToken).ConfigureAwait(false);
        return residue.Issue is { } residueIssue
            ? new(false, null, null, null, null, null, residueIssue)
            : residue.IsEmptyOrAbsent || transaction.Phase == ManagedSetupRecoveryPhase.Staging
                ? new(
                    true,
                    snapshot,
                    ProofDigest(terminalInventory),
                    terminalInventory,
                    residue.Inventory,
                    residue.Digest,
                    Issue: null)
                : new(false, null, null, null, null, null, ManagedSetupRecoveryEvidenceIssue.Invalid);
    }

    private ValueTask<StagingObservation> ObserveStagingResidueAsync(
        ManagedSetupRecoveryTransaction transaction,
        WindowsStableOwnedTreeSnapshot treeSnapshot,
        CancellationToken cancellationToken)
    {
        return ObserveStagingContainerAsync(
            transaction.ManagedRootIdentity,
            transaction.Phase == ManagedSetupRecoveryPhase.Staging ? treeSnapshot : null,
            transaction.TransactionId,
            RecoveryTreeLimits(transaction.Payload),
            cancellationToken);
    }

    private static async ValueTask<StagingObservation> ObserveStagingContainerAsync(
        string managedRoot,
        WindowsStableOwnedTreeSnapshot? expectedChild,
        string transactionId,
        WindowsStableTreeLimits treeLimits,
        CancellationToken cancellationToken)
    {
        string container = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(managedRoot);
        var limits = new WindowsStableTreeLimits(
            checked(treeLimits.MaximumFiles + 1),
            checked(treeLimits.MaximumDirectories + 1),
            treeLimits.MaximumBytes);
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
            container,
            limits,
            cancellationToken);
        if (acquired.IsExactChildMissing)
        {
            return expectedChild is null
                ? new(true, string.Empty, "absent", Issue: null)
                : new(false, null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            return new(false, null, null, MapEvidenceIssue(acquired.Issue));
        }

        using WindowsStablePathCustody custody = acquired.Custody!;
        if (!custody.RevalidateClosedTree() ||
            !custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot))
        {
            return new(false, null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }
        bool matches = expectedChild is null
            ? snapshot!.Files.Count == 0 &&
                snapshot.Directories.Keys.SequenceEqual([string.Empty], RepositoryPathSafety.PathComparer)
            : ContainerContainsOnlyExpectedChild(snapshot!, expectedChild, transactionId);
        if (!matches)
        {
            return new(false, null, null, ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        string proof = await SecureSnapshotProofAsync(
            custody,
            snapshot!,
            cancellationToken).ConfigureAwait(false);
        return custody.RevalidateClosedTree()
            ? new(expectedChild is null, proof, ProofDigest(proof), Issue: null)
            : new(false, null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
    }

    private static bool ContainerContainsOnlyExpectedChild(
        WindowsStableOwnedTreeSnapshot container,
        WindowsStableOwnedTreeSnapshot child,
        string transactionId)
    {
        string Prefix(string relative)
        {
            return string.IsNullOrEmpty(relative)
                ? transactionId
                : string.Concat(transactionId, "/", relative);
        }
        var expectedFiles = child.Files.ToDictionary(
            pair => Prefix(pair.Key),
            static pair => pair.Value,
            RepositoryPathSafety.PathComparer);
        var expectedDirectories = child.Directories.ToDictionary(
            pair => Prefix(pair.Key),
            static pair => pair.Value,
            RepositoryPathSafety.PathComparer);
        return container.Files.Count == expectedFiles.Count &&
            container.Directories.Count == expectedDirectories.Count + 1 &&
            container.Directories.ContainsKey(string.Empty) &&
            expectedFiles.All(pair =>
                container.Files.TryGetValue(pair.Key, out WindowsStablePathIdentity identity) &&
                identity == pair.Value) &&
            expectedDirectories.All(pair =>
                container.Directories.TryGetValue(pair.Key, out WindowsStablePathIdentity identity) &&
                identity == pair.Value);
    }

    private bool HasCompleteTopLevel(WindowsStableOwnedTreeSnapshot snapshot)
    {
        return snapshot.Files.ContainsKey(
                _layout.DistributionLauncherFileName) &&
            snapshot.Files.ContainsKey(_layout.BootstrapFileName) &&
            snapshot.Files.ContainsKey(_layout.SeedFileName) &&
            snapshot.Directories.ContainsKey(FileSystemManagedVersionRepository.VersionsDirectoryName);
    }

}
