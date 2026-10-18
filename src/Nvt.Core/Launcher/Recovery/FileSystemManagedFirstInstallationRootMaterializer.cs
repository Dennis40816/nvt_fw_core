// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Files.Windows;
using System.Text.Json;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Stages one closed first installation and promotes it by one same-volume move.</summary>
public sealed partial class FileSystemManagedFirstInstallationRootMaterializer
    : IManagedFirstInstallationRootMaterializer
{
    private const string RootPromotedPhase = ManagedSetupMarkerIO.RootPromotedPhase;
    private const string BootstrapLaunchRecordedPhase =
        ManagedSetupMarkerIO.BootstrapLaunchRecordedPhase;

    private readonly Func<string, CancellationToken, ValueTask>? _stagingCustodyAcquired;
    private readonly Func<string, CancellationToken, ValueTask>? _destinationCustodyAcquired;
    private readonly Func<string, CancellationToken, ValueTask>? _closedRootVerified;
    private readonly Action<string>? _beforeRepositoryStagingDelete;
    private readonly Action<string>? _beforeRepositoryStagingCleanup;
    private readonly Action<int, WindowsSetupStagingCleanupState>?
        _repositoryStagingCleanupAttemptObserved;
    private readonly Func<TimeSpan, CancellationToken, ValueTask>?
        _repositoryStagingCleanupDelay;
    private readonly Func<int, int>? _repositoryStagingDeleteOpenStatusOverride;
    private readonly Func<int, int>? _repositoryOwnedDeletionObservationStatusOverride;
    private readonly Action? _afterMarkerTopologyProof;
    private readonly Action<string>? _afterRootPromotion;
    private readonly Action<ManagedSetupBoundary>? _boundaryReached;
    private readonly ManagedSetupPayloadInstaller _installer;
    private readonly IManagedVersionRepository _repository;
    private readonly ManagedSetupLayout _layout;
    private readonly Func<string, IVersionManagerStateStore> _seedStoreFactory;
    private readonly ManagedSetupMarkerIO _markerIO;
    private readonly WindowsStableTreeLimits _payloadTreeLimits;
    private readonly long _maximumExecutableBytes;
    private readonly int _maximumDescriptorBytes;
    private readonly Action<Exception>? _observerDiagnostics;

    /// <summary>Creates a fresh-root materializer with mandatory product codecs and explicit byte ceilings.</summary>
    /// <remarks>The seed factory returns the product's strict store for the supplied exact seed path.</remarks>
    public FileSystemManagedFirstInstallationRootMaterializer(
        FileSystemManagedVersionRepository repository, ManagedSetupLayout layout,
        Func<string, IVersionManagerStateStore> seedStoreFactory, IManagedSetupMarkerCodec markerCodec,
        int maximumMarkerBytes, int maximumDescriptorBytes, long maximumExecutableBytes,
        Action<Exception>? observerDiagnostics = null)
        : this(repository ?? throw new ArgumentNullException(nameof(repository)),
            (writeRoot, sourceRoot, package, progress, cancellationToken) => repository
                .MaterializeVerifiedPayloadWithinHeldRootAsync(writeRoot, sourceRoot, package,
                    requireNew: true, cancellationToken, progress),
            repository.TreeLimits, layout, seedStoreFactory, markerCodec,
            maximumMarkerBytes, maximumDescriptorBytes, maximumExecutableBytes, new(), observerDiagnostics)
    {
    }

    internal FileSystemManagedFirstInstallationRootMaterializer(
        IManagedVersionRepository repository, ManagedSetupPayloadInstaller installer,
        WindowsStableTreeLimits payloadTreeLimits, ManagedSetupLayout layout,
        Func<string, IVersionManagerStateStore> seedStoreFactory, IManagedSetupMarkerCodec markerCodec,
        int maximumMarkerBytes, int maximumDescriptorBytes, long maximumExecutableBytes,
        ManagedSetupTestHooks hooks, Action<Exception>? observerDiagnostics = null)
    {
        _boundaryReached = hooks.BoundaryReached;
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _seedStoreFactory = seedStoreFactory ?? throw new ArgumentNullException(nameof(seedStoreFactory));
        _markerIO = new(markerCodec, maximumMarkerBytes, maximumDescriptorBytes);
        payloadTreeLimits.Validate();
        _payloadTreeLimits = payloadTreeLimits;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExecutableBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumExecutableBytes, ManagedImmutableBootstrapIdentity.MaximumExecutableBytes);
        _maximumExecutableBytes = maximumExecutableBytes;
        _maximumDescriptorBytes = maximumDescriptorBytes;
        _observerDiagnostics = observerDiagnostics;
        _stagingCustodyAcquired = hooks.StagingCustodyAcquired;
        _destinationCustodyAcquired = hooks.DestinationCustodyAcquired;
        _closedRootVerified = hooks.ClosedRootVerified;
        _beforeRepositoryStagingDelete = hooks.BeforeRepositoryStagingDelete;
        _beforeRepositoryStagingCleanup = hooks.BeforeRepositoryStagingCleanup;
        _repositoryStagingCleanupAttemptObserved = hooks.RepositoryStagingCleanupAttemptObserved;
        _repositoryStagingCleanupDelay = hooks.RepositoryStagingCleanupDelay;
        _repositoryStagingDeleteOpenStatusOverride = hooks.RepositoryStagingDeleteOpenStatusOverride;
        _repositoryOwnedDeletionObservationStatusOverride = hooks.RepositoryOwnedDeletionObservationStatusOverride;
        _afterMarkerTopologyProof = hooks.AfterMarkerTopologyProof;
        _afterRootPromotion = hooks.AfterRootPromotion;
    }

    /// <inheritdoc />
    public ValueTask<ManagedFirstInstallationMaterializationIssue> AdmitDestinationAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ManagedSetupDestinationCustody.Admit(managedRoot));
    }

    /// <inheritdoc />
    public async ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        string managedRoot,
        string statePathIdentity,
        IManagedDistributionPayloadCapture payload,
        FreshInstallationCandidate candidate,
        VersionManagerState seed,
        CancellationToken cancellationToken)
    {
        return await MaterializeAsync(
            managedRoot,
            statePathIdentity,
            payload,
            candidate,
            seed,
            progress: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        string managedRoot,
        string statePathIdentity,
        IManagedDistributionPayloadCapture payload,
        FreshInstallationCandidate candidate,
        VersionManagerState seed,
        IProgress<ManagedFirstInstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(statePathIdentity);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        ManagedInstallationRootStatus rootAdmission =
            FileSystemManagedInstallationRootProbe.AdmitRoot(managedRoot, out string root);
        if (rootAdmission != ManagedInstallationRootStatus.Absent)
        {
            return Failure(MapObservedRoot(rootAdmission));
        }
        if (!Path.IsPathFullyQualified(statePathIdentity))
        {
            return Failure(ManagedFirstInstallationMaterializationIssue.StateUnavailable);
        }
        if (payload is not IManagedDistributionPayloadContent content ||
            payload.Identity.DescriptorSize > _maximumDescriptorBytes ||
            payload.Identity.LauncherSize > _maximumExecutableBytes ||
            payload.Identity.Bootstrap.Length > _maximumExecutableBytes ||
            !string.Equals(
                payload.Identity.Bootstrap.FileName,
                _layout.BootstrapFileName,
                StringComparison.Ordinal))
        {
            return Failure(ManagedFirstInstallationMaterializationIssue.SourceChanged);
        }

        ManagedVersionAdmission expectedAdmission = new(
            candidate.Package.Version,
            candidate.Package.Identity,
            candidate.Package.ReleaseManifestSha256);
        if (!ManagedVersionSeedPolicy.IsCanonicalFirstRunSeed(seed) ||
            seed.Admissions is not [var only] ||
            only != expectedAdmission)
        {
            return Failure(ManagedFirstInstallationMaterializationIssue.SourceChanged);
        }

        var probe = new FileSystemManagedInstallationRootProbe();
        ManagedInstallationRootObservation initial = await probe.ObserveAsync(
            root,
            cancellationToken).ConfigureAwait(false);
        if (initial.Status != ManagedInstallationRootStatus.Absent)
        {
            return Failure(MapObservedRoot(initial.Status));
        }

        ManagedSetupDestinationCustody? custody = null;
        FileStream? markerStream = null;
        bool promotionCommitted = false;
        try
        {
            ManagedFirstInstallationMaterializationIssue custodyIssue =
                ManagedSetupDestinationCustody.TryAcquire(
                    root,
                    out custody,
                    _repositoryStagingDeleteOpenStatusOverride,
                    _repositoryOwnedDeletionObservationStatusOverride);
            if (custodyIssue != ManagedFirstInstallationMaterializationIssue.None)
            {
                return Failure(custodyIssue);
            }
            if (_destinationCustodyAcquired is not null)
            {
                await _destinationCustodyAcquired(root, cancellationToken).ConfigureAwait(false);
            }
            ManagedFirstInstallationMaterializationIssue finalAdmission = custody!.AdmitFreshDestination();
            if (finalAdmission != ManagedFirstInstallationMaterializationIssue.None)
            {
                return Failure(finalAdmission);
            }

            string markerPath = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(root);
            string stagingContainer = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(root);
            string transactionId = Guid.NewGuid().ToString("N");
            string stagingRoot = Path.Combine(stagingContainer, transactionId);
            var marker = ManagedSetupMarker.Create(
                transactionId,
                root,
                Path.GetFullPath(statePathIdentity),
                stagingRoot,
                payload.Identity,
                candidate,
                ManagedSetupMarkerIO.StagingPhase);

            MarkerCreateResult createdMarker = await CreateNewMarkerAsync(
                custody,
                Path.GetFileName(markerPath),
                _markerIO.Serialize(marker),
                _boundaryReached, cancellationToken).ConfigureAwait(false);
            if (!createdMarker.IsSuccess)
            {
                return Failure(createdMarker.Issue);
            }
            markerStream = createdMarker.Stream!;
            ManagedFirstInstallationMaterializationIssue staging = custody!.CreateStaging(
                stagingContainer,
                stagingRoot);
            if (staging != ManagedFirstInstallationMaterializationIssue.None)
            {
                return Failure(staging);
            }
            if (_stagingCustodyAcquired is not null)
            {
                await _stagingCustodyAcquired(stagingRoot, cancellationToken).ConfigureAwait(false);
            }

            string launcherPath = Path.Combine(stagingRoot, _layout.DistributionLauncherFileName);
            string bootstrapPath = Path.Combine(stagingRoot, _layout.BootstrapFileName);
            await content.CopyDistributionLauncherAsync(launcherPath, cancellationToken)
                .ConfigureAwait(false);
            await content.CopyBootstrapAsync(bootstrapPath, cancellationToken).ConfigureAwait(false);
            if (!await MatchesAsync(
                    launcherPath,
                    payload.Identity.LauncherSize,
                    payload.Identity.LauncherSha256,
                    cancellationToken).ConfigureAwait(false) ||
                !await MatchesAsync(
                    bootstrapPath,
                    payload.Identity.Bootstrap.Length,
                    payload.Identity.Bootstrap.Sha256,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure(ManagedFirstInstallationMaterializationIssue.SourceChanged);
            }

            ManagedVersionInstallResult payloadResult;
            using (WindowsStableRelativeWriteRoot writeRoot = custody.OpenPackageWriteRoot())
            {
                payloadResult = await _installer(writeRoot, candidate.Identity.SourceRoot,
                    candidate.Package, progress, cancellationToken).ConfigureAwait(false);
            }
            if (!payloadResult.IsSuccess || payloadResult.Admission != expectedAdmission)
            {
                return Failure(MapInstallIssue(payloadResult.Issue));
            }

            progress?.Report(ManagedFirstInstallationProgress.Indeterminate(
                ManagedFirstInstallationProgressStage.FinalizingInstallation));

            IVersionManagerStateStore seedStore = _seedStoreFactory(Path.Combine(stagingRoot, _layout.SeedFileName));
            _boundaryReached?.Invoke(ManagedSetupBoundary.BeforeSeedWrite);
            await seedStore.SaveAsync(seed, cancellationToken).ConfigureAwait(false);
            _boundaryReached?.Invoke(ManagedSetupBoundary.AfterSeedWrite);

            string repositoryStaging = Path.Combine(
                stagingRoot,
                FileSystemManagedVersionRepository.StagingDirectoryName);
            _beforeRepositoryStagingCleanup?.Invoke(repositoryStaging);
            if (!await RemoveEmptyRepositoryStagingAsync(
                    custody,
                    repositoryStaging,
                    _beforeRepositoryStagingDelete,
                    _repositoryStagingCleanupAttemptObserved,
                    _repositoryStagingCleanupDelay,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure(ManagedFirstInstallationMaterializationIssue.RecoveryRequired);
            }
            if (!await VerifyStagedRootAsync(
                    stagingRoot,
                    payload.Identity,
                    expectedAdmission,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure(ManagedFirstInstallationMaterializationIssue.StateUnavailable);
            }

            if (Directory.Exists(root) || File.Exists(root))
            {
                return Failure(ManagedFirstInstallationMaterializationIssue.RecoveryRequired);
            }
            _boundaryReached?.Invoke(ManagedSetupBoundary.BeforePromotion);
            ManagedFirstInstallationMaterializationIssue promotion = custody.Promote(
                Path.GetFileName(root));
            if (promotion != ManagedFirstInstallationMaterializationIssue.None)
            {
                return Failure(promotion);
            }
            promotionCommitted = true;
            _boundaryReached?.Invoke(ManagedSetupBoundary.AfterPromotion);
            _afterRootPromotion?.Invoke(root);
            WindowsStableTreeLimits setupTreeLimits = CreateSetupTreeLimits(root, payload.Identity);
            ManagedFirstInstallationMaterializationIssue captured = custody.CaptureClosedTree(
                root,
                setupTreeLimits,
                cancellationToken);
            if (captured != ManagedFirstInstallationMaterializationIssue.None ||
                !await VerifyClosedRootAsync(
                    root,
                    payload.Identity,
                    expectedAdmission,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure(ManagedFirstInstallationMaterializationIssue.RecoveryRequired);
            }
            if (_closedRootVerified is not null)
            {
                await _closedRootVerified(root, cancellationToken).ConfigureAwait(false);
                if (!await VerifyClosedRootAsync(
                        root,
                        payload.Identity,
                        expectedAdmission,
                        cancellationToken).ConfigureAwait(false))
                {
                    return Failure(ManagedFirstInstallationMaterializationIssue.RecoveryRequired);
                }
            }

            ManagedSetupMarker promotedMarker = marker with
            {
                Phase = ManagedSetupMarkerIO.RootPromotedPhase,
            };
            await ReplaceExactMarkerAsync(
                    _markerIO,
                    markerStream,
                    marker,
                    promotedMarker,
                    custody.RevalidateClosedTree,
                    _afterMarkerTopologyProof,
                    _boundaryReached, cancellationToken)
                .ConfigureAwait(false);
            ManagedDistributionPayloadIdentity payloadIdentity = payload.Identity;
            ManagedSetupDestinationCustody promotedCustody = custody;
            FileStream promotedMarkerStream = markerStream;
            var success = new ManagedFirstInstallationMaterializationResult(null,
                ManagedFirstInstallationMaterializationIssue.None)
            {
                Installation = new PromotedInstallation(
                    root,
                    expectedAdmission,
                    payloadIdentity.Bootstrap,
                    promotedMarker,
                    promotedMarkerStream,
                    promotedCustody,
                    _markerIO, _maximumExecutableBytes, _observerDiagnostics, _boundaryReached,
                    cancellation => VerifyClosedRootAsync(
                        root,
                        payloadIdentity,
                        expectedAdmission,
                        cancellation),
                    _afterMarkerTopologyProof),
            };
            custody = null;
            markerStream = null;
            return success;
        }
        catch (OperationCanceledException)
        {
            if (!promotionCommitted)
            {
                throw;
            }
            return Failure(ManagedFirstInstallationMaterializationIssue.RecoveryRequired);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(promotionCommitted
                ? ManagedFirstInstallationMaterializationIssue.RecoveryRequired
                : ManagedFirstInstallationMaterializationIssue.PermissionDenied);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return Failure(promotionCommitted
                ? ManagedFirstInstallationMaterializationIssue.RecoveryRequired
                : ManagedFirstInstallationMaterializationIssue.PromotionFailed);
        }
        catch (InvalidOperationException)
        {
            return Failure(promotionCommitted
                ? ManagedFirstInstallationMaterializationIssue.RecoveryRequired
                : ManagedFirstInstallationMaterializationIssue.StateUnavailable);
        }
        catch (Exception exception) when (promotionCommitted && SetupExceptionPolicy.IsNonFatal(exception))
        {
            SetupExceptionPolicy.ReportDiagnostics(exception, _observerDiagnostics);
            return Failure(ManagedFirstInstallationMaterializationIssue.RecoveryRequired);
        }
        finally
        {
            try
            {
                if (markerStream is not null) await markerStream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                custody?.Dispose();
            }
        }
    }
}
