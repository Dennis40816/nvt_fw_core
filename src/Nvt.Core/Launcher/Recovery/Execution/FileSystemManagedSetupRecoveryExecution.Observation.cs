// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedSetupRecoveryExecution
{
    /// <inheritdoc />
    public async ValueTask<ManagedSetupRecoveryEvidenceObservation> ObserveAsync(
        ManagedSetupRecoveryTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RepositoryPathSafety.PathComparer.Equals(transaction.StatePathIdentity, _statePath) ||
            !ManagedAppVersion.TryParse(transaction.Candidate.Version, out ManagedAppVersion version) ||
            !HasAdmittedExecutableSizes(transaction.Payload))
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.Invalid);
        }

        ManagedVersionAdmission admission;
        string treeRoot;
        try
        {
            admission = new(
                version,
                transaction.Candidate.EntryIdentity,
                transaction.Candidate.ReleaseManifestSha256);
            treeRoot = transaction.Phase == ManagedSetupRecoveryPhase.Staging
                ? Path.Combine(
                    FileSystemManagedInstallationRootProbe.GetStagingContainerPath(
                        transaction.ManagedRootIdentity),
                    transaction.TransactionId)
                : Path.GetFullPath(transaction.ManagedRootIdentity);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        return await ObserveStateSnapshotsAsync(transaction, admission, treeRoot, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ManagedSetupRecoveryEvidenceObservation> ObserveStateSnapshotsAsync(
        ManagedSetupRecoveryTransaction transaction, ManagedVersionAdmission admission,
        string treeRoot, CancellationToken cancellationToken)
    {
        StateFileObservation<RawFileEvidence> marker = await ObserveRawFileAsync(
            FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(
                transaction.ManagedRootIdentity),
            cancellationToken).ConfigureAwait(false);
        if (marker.Issue is { } markerIssue)
        {
            return Failure(markerIssue);
        }

        StateFileObservation<VersionManagerStateLoadResult> appState =
            await ObserveStateFileAsync(
                _statePath,
                _applicationStateStore.LoadAsync,
                static result => result.Issue == VersionManagerStateLoadIssue.Missing,
                cancellationToken).ConfigureAwait(false);
        if (appState.Issue is { } appIssue)
        {
            return Failure(appIssue);
        }
        StateFileObservation<LauncherBootstrapStateLoadResult> launcherObservation =
            await ObserveStateFileAsync(
                _launcherStatePath,
                _launcherStateStore.LoadAsync,
                static result => result.Issue == LauncherBootstrapStateLoadIssue.Missing,
                cancellationToken).ConfigureAwait(false);
        if (launcherObservation.Issue is { } launcherIssue)
        {
            return Failure(launcherIssue);
        }
        LauncherBootstrapStateLoadResult launcherState = launcherObservation.Value!;
        if (launcherState.Issue is LauncherBootstrapStateLoadIssue.Invalid)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        if (launcherState.Issue is LauncherBootstrapStateLoadIssue.Unavailable)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.StateUnavailable);
        }
        return await ObserveInventoryAsync(new(transaction, admission, treeRoot, appState, launcherObservation, marker),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ManagedSetupRecoveryEvidenceObservation> ObserveInventoryAsync(
        RecoveryObservation observation, CancellationToken cancellationToken)
    {
        var (transaction, admission, treeRoot, appState, launcherObservation, marker) = observation;
        var launcherState = launcherObservation.Value!;
        var version = admission.Version;
        bool missingStatePair = appState.Value!.Issue == VersionManagerStateLoadIssue.Missing &&
            launcherState.Issue == LauncherBootstrapStateLoadIssue.Missing;
        if (missingStatePair)
        {
            PrefixObservation prefix = await ObserveMarkerDerivedPrefixAsync(
                transaction, admission, version, treeRoot, cancellationToken).ConfigureAwait(false);
            if (prefix.Issue is { } prefixIssue)
            {
                return Failure(prefixIssue);
            }
            if (prefix.IsExact)
            {
                return CreateEvidence(
                    transaction,
                    admission,
                    installedLauncher: null,
                    launcherState,
                    appState,
                    launcherObservation,
                    marker,
                    treeRoot,
                    prefix.Snapshot,
                    prefix.TreeInventory!,
                    prefix.StagingInventory!,
                    prefix.StagingDigest!,
                    RecoveryTreeLimits(transaction.Payload));
            }
        }

        WindowsStableTreeLimits limits;
        try
        {
            limits = CreateSetupTreeLimits(treeRoot, transaction.Payload);
        }
        catch (IOException exception) when (transaction.Phase == ManagedSetupRecoveryPhase.Staging &&
            (exception is FileNotFoundException or DirectoryNotFoundException))
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.Invalid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.StateUnavailable);
        }
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTree(
            treeRoot,
            limits: limits,
            cancellationToken: cancellationToken);
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            return Failure(MapEvidenceIssue(acquired.Issue));
        }

        using WindowsStablePathCustody custody = acquired.Custody!;
        try
        {
            return await ObserveHeldInventoryAsync(observation, custody, limits, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.PermissionDenied);
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or InvalidOperationException)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.StateUnavailable);
        }
    }

    private async ValueTask<ManagedSetupRecoveryEvidenceObservation> ObserveHeldInventoryAsync(
        RecoveryObservation observation, WindowsStablePathCustody custody, WindowsStableTreeLimits limits,
        CancellationToken cancellationToken)
    {
        var (transaction, admission, treeRoot, appState, launcherObservation, marker) = observation;
        var launcherState = launcherObservation.Value!;
        bool missingStatePair = appState.Value!.Issue == VersionManagerStateLoadIssue.Missing &&
            launcherState.Issue == LauncherBootstrapStateLoadIssue.Missing;
        if (!custody.RevalidateClosedTree() ||
            !custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot))
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }
        bool closedRoot = await VerifyClosedRecoveryRootAsync(
            observation, custody, snapshot!, cancellationToken).ConfigureAwait(false);
        if (!closedRoot && missingStatePair)
        {
            return await ObserveRetainedManifestAsync(observation, custody, snapshot!, limits, cancellationToken).ConfigureAwait(false);
        }
        if (!closedRoot)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }
        InstalledLauncherResult installed = await _launcherRepository.VerifyAsync(
            treeRoot,
            admission,
            cancellationToken).ConfigureAwait(false);
        if (!installed.IsVerified)
        {
            return Failure(MapEvidenceIssue(installed.Issue));
        }
        if (!custody.RevalidateClosedTree())
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }

        StagingObservation staging = await ObserveStagingResidueAsync(
            transaction,
            snapshot!,
            cancellationToken).ConfigureAwait(false);
        string treeInventory = await SecureSnapshotProofAsync(
            custody,
            snapshot!,
            cancellationToken).ConfigureAwait(false);
        return staging.Issue is { } stagingIssue
            ? Failure(stagingIssue)
            : CreateEvidence(
                transaction,
                admission,
                installed.Identity!,
                launcherState,
                appState,
                launcherObservation,
                marker,
                treeRoot,
                snapshot!,
                treeInventory,
                staging.Inventory!,
                staging.Digest!,
                limits);
    }

    private async ValueTask<bool> VerifyClosedRecoveryRootAsync(
        RecoveryObservation observation, WindowsStablePathCustody custody,
        WindowsStableOwnedTreeSnapshot snapshot, CancellationToken cancellationToken)
    {
        var payload = observation.Transaction.Payload;
        var admission = observation.Admission;
        var seed = await _seedStoreFactory(Path.Combine(custody.RootPath, _layout.SeedFileName))
            .LoadAsync(cancellationToken).ConfigureAwait(false);
        // Custody has rejected reparse entries; its snapshot retains each accepted on-disk name.
        var topLevel = snapshot.Files.Keys.Concat(snapshot.Directories.Keys)
            .Where(static path => !string.IsNullOrEmpty(path) && !path.Contains('/', StringComparison.Ordinal))
            .ToHashSet(RepositoryPathSafety.PathComparer);
        if (!seed.IsSuccess || seed.State is not { } state ||
            !ManagedVersionSeedPolicy.IsCanonicalFirstRunSeed(state) ||
            state.Admissions is not [var only] || only != admission ||
            !topLevel.SetEquals([_layout.BootstrapFileName, _layout.DistributionLauncherFileName,
                _layout.SeedFileName, FileSystemManagedVersionRepository.VersionsDirectoryName]) ||
            !snapshot.Files.ContainsKey(_layout.DistributionLauncherFileName) ||
            !snapshot.Files.ContainsKey(_layout.BootstrapFileName) ||
            !snapshot.Files.ContainsKey(_layout.SeedFileName) ||
            !snapshot.Directories.ContainsKey(FileSystemManagedVersionRepository.VersionsDirectoryName) ||
            !await MatchesHeldAsync(custody, _layout.DistributionLauncherFileName,
                payload.LauncherSize, payload.LauncherSha256, cancellationToken).ConfigureAwait(false) ||
            !string.Equals(payload.BootstrapFileName, _layout.BootstrapFileName, StringComparison.Ordinal) ||
            !await MatchesHeldAsync(custody, _layout.BootstrapFileName,
                payload.BootstrapSize, payload.BootstrapSha256, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        var inventory = await _repository.InventoryAsync(custody.RootPath, [admission],
            admission.Version, admission.Version, failedActivationVersion: null, cancellationToken).ConfigureAwait(false);
        return inventory.IsSuccess && inventory.Inventory is { } installed &&
            installed.Versions.Count == 1 && installed.HealthyCount == 1 && installed.DamagedCount == 0;
    }

    private async ValueTask<ManagedSetupRecoveryEvidenceObservation> ObserveRetainedManifestAsync(
        RecoveryObservation observation, WindowsStablePathCustody custody, WindowsStableOwnedTreeSnapshot snapshot,
        WindowsStableTreeLimits limits, CancellationToken cancellationToken)
    {
        var (transaction, admission, treeRoot, appState, launcherObservation, marker) = observation;
        var launcherState = launcherObservation.Value!;
        var version = admission.Version;
        string authorityManifest = string.Join(
            '/',
            FileSystemManagedVersionRepository.VersionsDirectoryName,
            version.ToString(),
            "RELEASE-MANIFEST.json");
        try
        {
            bool prefix = snapshot.Files.ContainsKey(authorityManifest) &&
                await VerifyRetainedManifestPrefixAsync(
                    custody,
                    snapshot,
                    transaction,
                    admission,
                    version,
                    authorityManifest,
                    cancellationToken).ConfigureAwait(false);
            if (!prefix)
            {
                return Failure(ManagedSetupRecoveryEvidenceIssue.SourceChanged);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(ManagedSetupRecoveryEvidenceIssue.StateUnavailable);
        }
        StagingObservation prefixStaging = await ObserveStagingResidueAsync(
            transaction,
            snapshot,
            cancellationToken).ConfigureAwait(false);
        string prefixInventory = await SecureSnapshotProofAsync(
            custody,
            snapshot,
            cancellationToken).ConfigureAwait(false);
        return prefixStaging.Issue is { } prefixIssue
            ? Failure(prefixIssue)
            : CreateEvidence(
                transaction,
                admission,
                installedLauncher: null,
                launcherState,
                appState,
                launcherObservation,
                marker,
                treeRoot,
                snapshot,
                prefixInventory,
                prefixStaging.Inventory!,
                prefixStaging.Digest!,
                limits);
    }

    private ManagedSetupRecoveryEvidenceObservation CreateEvidence(
        ManagedSetupRecoveryTransaction transaction,
        ManagedVersionAdmission admission,
        ManagedLauncherIdentity? installedLauncher,
        LauncherBootstrapStateLoadResult launcherState,
        StateFileObservation<VersionManagerStateLoadResult> appState,
        StateFileObservation<LauncherBootstrapStateLoadResult> launcherObservation,
        StateFileObservation<RawFileEvidence> marker,
        string treeRoot,
        WindowsStableOwnedTreeSnapshot? treeSnapshot,
        string treeInventory,
        string stagingInventory,
        string stagingDigest,
        WindowsStableTreeLimits limits)
    {
        string treeDigest = treeSnapshot is null ? "absent" : ProofDigest(treeInventory);
        var token = new FileSystemRecoveryToken(
            _statePath,
            transaction.ManagedRootIdentity,
            transaction.TransactionId,
            transaction.Phase,
            admission.Version.ToString(),
            TransactionDigest(transaction),
            treeRoot,
            treeDigest,
            treeInventory,
            treeSnapshot is null ? string.Empty : TreeSteps(treeSnapshot, admission.Version),
            stagingInventory,
            stagingDigest,
            limits.MaximumFiles,
            limits.MaximumDirectories,
            limits.MaximumBytes,
            marker.IdentityDigest!,
            appState.IdentityDigest!,
            launcherObservation.IdentityDigest!,
            LauncherStateDigest(launcherState),
            LauncherDigest(installedLauncher));
        return new ManagedSetupRecoveryEvidenceObservation(
            admission,
            installedLauncher,
            launcherState,
            token);
    }

    private sealed record RecoveryObservation(ManagedSetupRecoveryTransaction Transaction,
        ManagedVersionAdmission Admission, string TreeRoot,
        StateFileObservation<VersionManagerStateLoadResult> Application,
        StateFileObservation<LauncherBootstrapStateLoadResult> Launcher,
        StateFileObservation<RawFileEvidence> Marker);
}
