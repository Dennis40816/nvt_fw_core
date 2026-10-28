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
/// <remarks>
/// The <c>payload</c> must be a capture produced by Core, which implements the Core-internal
/// <c>IManagedDistributionPayloadContent</c>. Once the root and state-path checks pass, a capture
/// from outside Core returns <c>ManagedFirstInstallationMaterializationIssue.SourceChanged</c>.
/// </remarks>
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
                .WithInstallationProgress(progress)
                .MaterializeVerifiedPayloadWithinHeldRootAsync(writeRoot, sourceRoot, package,
                    requireNew: true, cancellationToken),
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

        var request = new MaterializationRequest(
            root, statePathIdentity, payload.Identity, content, candidate, seed, expectedAdmission, progress);
        return await MaterializeAbsentRootAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // Owns the destination custody and the marker stream until a promoted installation takes them.
    // Failures after the promotion are mapped in CompletePromotedRootAsync, so this catch sees only earlier ones.
    private async ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAbsentRootAsync(
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        ManagedSetupDestinationCustody? custody = null;
        FileStream? markerStream = null;
        try
        {
            ManagedFirstInstallationMaterializationIssue custodyIssue =
                ManagedSetupDestinationCustody.TryAcquire(
                    request.Root,
                    out custody,
                    _repositoryStagingDeleteOpenStatusOverride,
                    _repositoryOwnedDeletionObservationStatusOverride);
            if (custodyIssue != ManagedFirstInstallationMaterializationIssue.None)
            {
                return Failure(custodyIssue);
            }
            if (_destinationCustodyAcquired is not null)
            {
                await _destinationCustodyAcquired(request.Root, cancellationToken).ConfigureAwait(false);
            }
            ManagedFirstInstallationMaterializationIssue finalAdmission = custody!.AdmitFreshDestination();
            if (finalAdmission != ManagedFirstInstallationMaterializationIssue.None)
            {
                return Failure(finalAdmission);
            }

            SetupTransaction transaction = CreateSetupTransaction(request);
            MarkerCreateResult createdMarker = await CreateNewMarkerAsync(
                custody,
                transaction.MarkerName,
                _markerIO.Serialize(transaction.Marker),
                _boundaryReached, cancellationToken).ConfigureAwait(false);
            if (!createdMarker.IsSuccess)
            {
                return Failure(createdMarker.Issue);
            }
            markerStream = createdMarker.Stream!;
            ManagedFirstInstallationMaterializationIssue promoted = await StageAndPromoteAsync(
                request, transaction, custody, markerStream, cancellationToken).ConfigureAwait(false);
            if (promoted != ManagedFirstInstallationMaterializationIssue.None)
            {
                return Failure(promoted);
            }

            var success = new ManagedFirstInstallationMaterializationResult(null,
                ManagedFirstInstallationMaterializationIssue.None)
            {
                Installation = new PromotedInstallation(
                    request.Root,
                    request.ExpectedAdmission,
                    request.PayloadIdentity.Bootstrap,
                    transaction.PromotedMarker,
                    markerStream,
                    custody,
                    _markerIO, _maximumExecutableBytes, _observerDiagnostics, _boundaryReached,
                    cancellation => VerifyClosedRootAsync(
                        request.Root,
                        request.PayloadIdentity,
                        request.ExpectedAdmission,
                        cancellation),
                    _afterMarkerTopologyProof),
            };
            custody = null;
            markerStream = null;
            return success;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException && IsExpectedFailure(exception))
        {
            return Failure(MapUnpromotedFailure(exception));
        }
        finally
        {
            try { if (markerStream is not null) await DisposeMarkerStreamAsync(markerStream, _observerDiagnostics).ConfigureAwait(false); }
            finally { custody?.Dispose(); }
        }
    }

    private static SetupTransaction CreateSetupTransaction(MaterializationRequest request)
    {
        string markerPath = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(request.Root);
        string stagingContainer = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(request.Root);
        string transactionId = Guid.NewGuid().ToString("N");
        string stagingRoot = Path.Combine(stagingContainer, transactionId);
        var marker = ManagedSetupMarker.Create(
            transactionId,
            request.Root,
            Path.GetFullPath(request.StatePathIdentity),
            stagingRoot,
            request.PayloadIdentity,
            request.Candidate,
            ManagedSetupMarkerIO.StagingPhase);
        ManagedSetupMarker promotedMarker = marker with
        {
            Phase = ManagedSetupMarkerIO.RootPromotedPhase,
        };
        return new(Path.GetFileName(markerPath), stagingContainer, stagingRoot, marker, promotedMarker);
    }

    private async ValueTask<ManagedFirstInstallationMaterializationIssue> StageAndPromoteAsync(
        MaterializationRequest request,
        SetupTransaction transaction,
        ManagedSetupDestinationCustody custody,
        FileStream markerStream,
        CancellationToken cancellationToken)
    {
        ManagedFirstInstallationMaterializationIssue staged = await CopyBootstrapFilesAsync(
            request, transaction, custody, cancellationToken).ConfigureAwait(false);
        if (staged != ManagedFirstInstallationMaterializationIssue.None)
        {
            return staged;
        }
        staged = await InstallStagedPayloadAsync(
            request, transaction, custody, cancellationToken).ConfigureAwait(false);
        if (staged != ManagedFirstInstallationMaterializationIssue.None)
        {
            return staged;
        }

        if (Directory.Exists(request.Root) || File.Exists(request.Root))
        {
            return ManagedFirstInstallationMaterializationIssue.RecoveryRequired;
        }
        _boundaryReached?.Invoke(ManagedSetupBoundary.BeforePromotion);
        ManagedFirstInstallationMaterializationIssue promotion = custody.Promote(
            Path.GetFileName(request.Root));
        if (promotion != ManagedFirstInstallationMaterializationIssue.None)
        {
            return promotion;
        }
        return await CompletePromotedRootAsync(
            request, transaction, custody, markerStream, cancellationToken).ConfigureAwait(false)
            ? ManagedFirstInstallationMaterializationIssue.None
            : ManagedFirstInstallationMaterializationIssue.RecoveryRequired;
    }

    private async ValueTask<ManagedFirstInstallationMaterializationIssue> CopyBootstrapFilesAsync(
        MaterializationRequest request,
        SetupTransaction transaction,
        ManagedSetupDestinationCustody custody,
        CancellationToken cancellationToken)
    {
        ManagedFirstInstallationMaterializationIssue staging = custody.CreateStaging(
            transaction.StagingContainer,
            transaction.StagingRoot);
        if (staging != ManagedFirstInstallationMaterializationIssue.None)
        {
            return staging;
        }
        if (_stagingCustodyAcquired is not null)
        {
            await _stagingCustodyAcquired(transaction.StagingRoot, cancellationToken).ConfigureAwait(false);
        }

        string launcherPath = Path.Combine(transaction.StagingRoot, _layout.DistributionLauncherFileName);
        string bootstrapPath = Path.Combine(transaction.StagingRoot, _layout.BootstrapFileName);
        await request.Content.CopyDistributionLauncherAsync(launcherPath, cancellationToken)
            .ConfigureAwait(false);
        await request.Content.CopyBootstrapAsync(bootstrapPath, cancellationToken).ConfigureAwait(false);
        if (!await MatchesAsync(
                launcherPath,
                request.PayloadIdentity.LauncherSize,
                request.PayloadIdentity.LauncherSha256,
                cancellationToken).ConfigureAwait(false) ||
            !await MatchesAsync(
                bootstrapPath,
                request.PayloadIdentity.Bootstrap.Length,
                request.PayloadIdentity.Bootstrap.Sha256,
                cancellationToken).ConfigureAwait(false))
        {
            return ManagedFirstInstallationMaterializationIssue.SourceChanged;
        }
        return ManagedFirstInstallationMaterializationIssue.None;
    }

    private async ValueTask<ManagedFirstInstallationMaterializationIssue> InstallStagedPayloadAsync(
        MaterializationRequest request,
        SetupTransaction transaction,
        ManagedSetupDestinationCustody custody,
        CancellationToken cancellationToken)
    {
        ManagedVersionInstallResult payloadResult;
        using (WindowsStableRelativeWriteRoot writeRoot = custody.OpenPackageWriteRoot())
        {
            payloadResult = await _installer(writeRoot, request.Candidate.Identity.SourceRoot,
                request.Candidate.Package, request.Progress, cancellationToken).ConfigureAwait(false);
        }
        if (!payloadResult.IsSuccess || payloadResult.Admission != request.ExpectedAdmission)
        {
            return MapInstallIssue(payloadResult.Issue);
        }

        request.Progress?.Report(ManagedFirstInstallationProgress.Indeterminate(
            ManagedFirstInstallationProgressStage.FinalizingInstallation));

        IVersionManagerStateStore seedStore = _seedStoreFactory(
            Path.Combine(transaction.StagingRoot, _layout.SeedFileName));
        _boundaryReached?.Invoke(ManagedSetupBoundary.BeforeSeedWrite);
        await seedStore.SaveAsync(request.Seed, cancellationToken).ConfigureAwait(false);
        _boundaryReached?.Invoke(ManagedSetupBoundary.AfterSeedWrite);

        string repositoryStaging = Path.Combine(
            transaction.StagingRoot,
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
            return ManagedFirstInstallationMaterializationIssue.RecoveryRequired;
        }
        if (!await VerifyStagedRootAsync(
                transaction.StagingRoot,
                request.PayloadIdentity,
                request.ExpectedAdmission,
                cancellationToken).ConfigureAwait(false))
        {
            return ManagedFirstInstallationMaterializationIssue.StateUnavailable;
        }
        return ManagedFirstInstallationMaterializationIssue.None;
    }

    // Runs after the root move commits. Any non-fatal failure here leaves a root that needs recovery.
    private async ValueTask<bool> CompletePromotedRootAsync(
        MaterializationRequest request,
        SetupTransaction transaction,
        ManagedSetupDestinationCustody custody,
        FileStream markerStream,
        CancellationToken cancellationToken)
    {
        try
        {
            _boundaryReached?.Invoke(ManagedSetupBoundary.AfterPromotion);
            _afterRootPromotion?.Invoke(request.Root);
            WindowsStableTreeLimits setupTreeLimits = CreateSetupTreeLimits(request.Root, request.PayloadIdentity);
            ManagedFirstInstallationMaterializationIssue captured = custody.CaptureClosedTree(
                request.Root,
                setupTreeLimits,
                cancellationToken);
            if (captured != ManagedFirstInstallationMaterializationIssue.None ||
                !await VerifyClosedRootAsync(
                    request.Root,
                    request.PayloadIdentity,
                    request.ExpectedAdmission,
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            if (_closedRootVerified is not null)
            {
                await _closedRootVerified(request.Root, cancellationToken).ConfigureAwait(false);
                if (!await VerifyClosedRootAsync(
                        request.Root,
                        request.PayloadIdentity,
                        request.ExpectedAdmission,
                        cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
            }

            await ReplaceExactMarkerAsync(
                    _markerIO,
                    markerStream,
                    transaction.Marker,
                    transaction.PromotedMarker,
                    custody.RevalidateClosedTree,
                    _afterMarkerTopologyProof,
                    _boundaryReached, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (SetupExceptionPolicy.IsNonFatal(exception))
        {
            if (!IsExpectedFailure(exception))
            {
                SetupExceptionPolicy.ReportDiagnostics(exception, _observerDiagnostics);
            }
            return false;
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is OperationCanceledException or UnauthorizedAccessException or IOException or
            JsonException or InvalidDataException or InvalidOperationException;

    private static ManagedFirstInstallationMaterializationIssue MapUnpromotedFailure(Exception exception) =>
        exception switch
        {
            UnauthorizedAccessException => ManagedFirstInstallationMaterializationIssue.PermissionDenied,
            InvalidOperationException => ManagedFirstInstallationMaterializationIssue.StateUnavailable,
            _ => ManagedFirstInstallationMaterializationIssue.PromotionFailed,
        };

    private sealed record MaterializationRequest(
        string Root,
        string StatePathIdentity,
        ManagedDistributionPayloadIdentity PayloadIdentity,
        IManagedDistributionPayloadContent Content,
        FreshInstallationCandidate Candidate,
        VersionManagerState Seed,
        ManagedVersionAdmission ExpectedAdmission,
        IProgress<ManagedFirstInstallationProgress>? Progress);

    private sealed record SetupTransaction(
        string MarkerName,
        string StagingContainer,
        string StagingRoot,
        ManagedSetupMarker Marker,
        ManagedSetupMarker PromotedMarker);
}
