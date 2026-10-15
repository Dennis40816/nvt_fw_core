// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Transport;

using static Nvt.Core.Launcher.Setup.FirstInstallationOutcomes;

namespace Nvt.Core.Launcher.Setup;

internal sealed class FirstInstallationAttempt(
    FirstInstallationServices services,
    ManagedFirstInstallationPlan plan,
    IProgress<ManagedFirstInstallationProgress>? progress)
{
    private readonly FirstInstallationServices _services = services;
    private readonly ManagedFirstInstallationPlan _plan = plan;
    private readonly IProgress<ManagedFirstInstallationProgress>? _progress = progress;
    // Guard: one RunAsync invocation owns these holders and serializes every access across awaits.
    private IManagedPromotedFirstInstallation? _promoted;
    // Guard: the same invocation owns this lease until transfer to the handoff.
    private IManagedExecutableLaunchLease? _bootstrapLease;
    // Guard: the same invocation creates and disposes the complete admission session.
    private FirstInstallationAdmission? _admission;

    internal async ValueTask<ManagedFirstInstallationResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        // Once promoted, nonfatal adapter/contract failures leave the marker with recovery.
        catch (Exception exception) when (_promoted is not null && exception is not
            (OperationCanceledException or OutOfMemoryException or AccessViolationException))
        {
            return Result(_plan, ManagedFirstInstallationOutcome.RecoveryRequired);
        }
    }

    private async ValueTask<ManagedFirstInstallationResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(_plan.StatePathIdentity, _services.StatePathIdentity, StringComparison.Ordinal))
        {
            return Result(_plan, ManagedFirstInstallationOutcome.RecoveryRequired);
        }
        try
        {
            using (VersionManagerWriteLeaseResult lease = await _services.StateStore.TryAcquireWriteLeaseAsync(
                ManagedFirstInstallationExperience.WriterLeaseTimeout, cancellationToken).ConfigureAwait(false))
            {
                if (!lease.HoldsStatePath(_services.StatePathIdentity))
                {
                    return Result(_plan, lease.Issue == VersionManagerWriteLeaseIssue.Busy
                        ? ManagedFirstInstallationOutcome.Busy : ManagedFirstInstallationOutcome.StateUnavailable);
                }
                ManagedFirstInstallationOutcome? destination = await FirstInstallationDestination
                    .ObserveAsync(_services, _plan.ManagedRoot, cancellationToken).ConfigureAwait(false);
                if (destination is { } issue)
                {
                    return Result(_plan, issue);
                }
                ManagedDistributionPayloadCaptureResult captured = await _services.PayloadSource
                    .CaptureExactAsync(_plan.Payload, cancellationToken).ConfigureAwait(false);
                if (!captured.IsSuccess || captured.Capture!.Identity != _plan.Payload)
                {
                    captured.Capture?.Dispose();
                    // Frozen ManagedInstallationSetup.cs:207-212,788-800 maps a
                    // mismatched successful capture (None) to PayloadInvalid.
                    return Result(_plan, MapPayloadIssue(captured.Issue));
                }
                using IManagedDistributionPayloadCapture payload = captured.Capture;

                ManagedFirstInstallationResult? failed = await PromoteAsync(payload, cancellationToken).ConfigureAwait(false);
                if (failed is not null)
                {
                    return failed;
                }
                failed = await RecordAndAcquireAsync(cancellationToken).ConfigureAwait(false);
                if (failed is not null)
                {
                    return failed;
                }
            }
            ManagedFirstInstallationResult? launchFailure = await LaunchAsync(cancellationToken).ConfigureAwait(false);
            return launchFailure ?? await FinalizeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_promoted is not null && cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(_plan, ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.PostPromotion, ManagedFirstInstallationLaunchIssue.Cancelled);
        }
        finally
        {
            _admission?.Dispose();
            _bootstrapLease?.Dispose();
            _promoted?.Dispose();
        }
    }

    private async ValueTask<ManagedFirstInstallationResult?> PromoteAsync(
        IManagedDistributionPayloadCapture payload,
        CancellationToken cancellationToken)
    {
        _progress?.Report(ManagedFirstInstallationProgress.Indeterminate(
            ManagedFirstInstallationProgressStage.RevalidatingSource));
        FreshInstallationCandidateResult reverified = await _services.CandidateSource
            .ReverifyFreshInstallationAsync(_plan.Candidate, cancellationToken)
            .ConfigureAwait(false);
        if (!reverified.IsSuccess || reverified.Candidate!.Identity != _plan.Candidate.Identity)
        {
            return Result(_plan, MapCandidateIssue(reverified.Issue));
        }

        ManagedVersionAdmission admission = CreateAdmission(reverified.Candidate);
        VersionManagerState seed = ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(admission);
        ManagedFirstInstallationMaterializationResult materialized = await _services.RootMaterializer
            .MaterializeAsync(
                _plan.ManagedRoot,
                _services.StatePathIdentity,
                payload,
                reverified.Candidate,
                seed,
                _progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (!materialized.IsSuccess)
        {
            materialized.Installation?.Dispose();
            return Result(_plan, MapMaterializationIssue(materialized.Issue));
        }
        _promoted = materialized.Installation!;
        if (!ManagedRootPathIdentity.Equals(_promoted.ManagedRoot, _plan.ManagedRoot) ||
            _promoted!.Admission != admission)
        {
            return Result(_plan, ManagedFirstInstallationOutcome.RecoveryRequired);
        }

        return null;
    }

    private async ValueTask<ManagedFirstInstallationResult?> RecordAndAcquireAsync(CancellationToken cancellationToken)
    {
        _progress?.Report(ManagedFirstInstallationProgress.Indeterminate(
            ManagedFirstInstallationProgressStage.FinalizingInstallation));
        ManagedFirstInstallationTransactionIssue recorded = await _promoted!
            .RecordBootstrapLaunchAsync(cancellationToken).ConfigureAwait(false);
        if (recorded != ManagedFirstInstallationTransactionIssue.None)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.RecoveryRequired,
                ManagedFirstInstallationLaunchStage.PostPromotion,
                MapTransactionFailure(recorded));
        }

        _admission = FirstInstallationAdmission.Create(
            _services.AdmissionOperationCutoff, _services.TimeProvider, cancellationToken);
        ManagedExecutableLaunchLeaseResult acquired;
        try
        {
            acquired = await _promoted!.AcquireBootstrapLaunchLeaseAsync(
                _plan.Payload.Bootstrap,
                _admission!.Linked.Token).ConfigureAwait(false);
            // Take ownership before cancellation/deadline checks; the adapter may
            // create a valid lease and cancel the caller before this resumes.
            _bootstrapLease = acquired.Lease;
        }
        catch (OperationCanceledException) when (
            _admission!.Deadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.TimedOut);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (_services.TimeProvider.GetElapsedTime(_admission!.Started) >= _services.AdmissionOperationCutoff)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.TimedOut);
        }
        if (!acquired.IsAcquired)
        {
            ImmutableBootstrapStartIssue issue = MapBootstrapLeaseFailure(acquired);
            return LaunchFailure(
                _plan,
                MapBootstrapStartIssue(issue),
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                MapBootstrapStartFailure(issue));
        }
        return null;
    }

    private async ValueTask<ManagedFirstInstallationResult?> LaunchAsync(CancellationToken cancellationToken)
    {
        _progress?.Report(ManagedFirstInstallationProgress.Indeterminate(
            ManagedFirstInstallationProgressStage.StartingApplication));
        CancellationTokenSource activeAdmissionDeadline = _admission?.Deadline ??
            throw new InvalidOperationException("Bootstrap admission deadline was not created.");
        CancellationTokenSource activeAdmissionLinked = _admission?.Linked ??
            throw new InvalidOperationException("Bootstrap admission token was not created.");
        ImmutableBootstrapStartResult started;
        try
        {
            IManagedExecutableLaunchLease ownedLease = _bootstrapLease ??
                throw new InvalidOperationException(
                    "Promoted Bootstrap launch custody was not acquired.");
            _bootstrapLease = null;
            started = await _services.Handoff.StartAsync(
                _plan.ManagedRoot,
                _plan.Payload.Bootstrap,
                ownedLease,
                activeAdmissionLinked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            activeAdmissionDeadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.TimedOut);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.Cancelled);
        }
        IImmutableBootstrapLaunch? launch = started.Launch;
        try
        {
            if (!started.HasValidShape)
            {
                return LaunchFailure(
                    _plan,
                    ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                    ManagedFirstInstallationLaunchStage.BootstrapStart,
                    ManagedFirstInstallationLaunchIssue.InvalidReceipt);
            }
            if (!started.IsStarted)
            {
                return LaunchFailure(
                    _plan,
                    cancellationToken.IsCancellationRequested
                        ? ManagedFirstInstallationOutcome.InstalledButLaunchFailed
                        : MapBootstrapStartIssue(started.Issue),
                    ManagedFirstInstallationLaunchStage.BootstrapStart,
                    cancellationToken.IsCancellationRequested
                        ? ManagedFirstInstallationLaunchIssue.Cancelled
                        : MapBootstrapStartFailure(started.Issue));
            }
            IImmutableBootstrapLaunch admittedLaunch = launch!;

            ManagedFirstInstallationResult? failure = await ObserveAdmissionAsync(
                admittedLaunch, activeAdmissionDeadline, activeAdmissionLinked, cancellationToken).ConfigureAwait(false);
            return failure ?? await ObserveCompletionAsync(admittedLaunch, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            launch?.Dispose();
        }
    }

    private async ValueTask<ManagedFirstInstallationResult?> ObserveAdmissionAsync(
        IImmutableBootstrapLaunch admittedLaunch,
        CancellationTokenSource activeAdmissionDeadline,
        CancellationTokenSource activeAdmissionLinked,
        CancellationToken cancellationToken)
    {
        ImmutableBootstrapAdmissionResult admission;
        try
        {
            admission = await admittedLaunch
                .WaitForAdmissionAsync(
                    RemainingBudget(
                        _admission!.Started,
                        _services.AdmissionOperationCutoff,
                        ManagedLauncherEntryCoordinator.DefaultCleanupObservationBudget),
                    activeAdmissionLinked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            activeAdmissionDeadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.TimedOut);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.Cancelled);
        }
        if (!admission.HasValidShape)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.InvalidReceipt,
                admission.ExitCode);
        }
        if (admission.Outcome == ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed,
                admission.ExitCode);
        }
        if (_services.TimeProvider.GetElapsedTime(_admission!.Started) >= _services.AdmissionOperationCutoff)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.TimedOut,
                exitCode: null);
        }
        if (admission.Outcome != ImmutableBootstrapAdmissionOutcome.Admitted)
        {
            return LaunchFailure(
                _plan,
                cancellationToken.IsCancellationRequested
                    ? ManagedFirstInstallationOutcome.InstalledButLaunchFailed
                    : MapBootstrapAdmissionIssue(admission.Outcome),
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                cancellationToken.IsCancellationRequested
                    ? ManagedFirstInstallationLaunchIssue.Cancelled
                    : MapBootstrapAdmissionFailure(admission),
                cancellationToken.IsCancellationRequested ? null : admission.ExitCode);
        }

        return null;
    }

    private async ValueTask<ManagedFirstInstallationResult?> ObserveCompletionAsync(
        IImmutableBootstrapLaunch admittedLaunch,
        CancellationToken cancellationToken)
    {
        ImmutableBootstrapCompletionResult completion;
        long completionStarted = _services.TimeProvider.GetTimestamp();
        using var completionDeadline = new CancellationTokenSource(
            _services.CompletionOperationCutoff,
            _services.TimeProvider);
        using var completionLinked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            completionDeadline.Token);
        try
        {
            completion = await admittedLaunch.WaitForCompletionAsync(
                RemainingBudget(
                    completionStarted,
                    _services.CompletionOperationCutoff,
                    ManagedLauncherEntryCoordinator.DefaultCleanupObservationBudget),
                completionLinked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            completionDeadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.TimedOut);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.Cancelled);
        }
        if (!completion.HasValidShape)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.InvalidReceipt,
                completion.ExitCode);
        }
        if (completion.Outcome == ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed,
                completion.ExitCode);
        }
        // Keep the frozen cutoff predicate, using one observation for the whole receipt.
        TimeSpan completionElapsed = _services.TimeProvider.GetElapsedTime(completionStarted);
        if (completionElapsed >= _services.CompletionOperationCutoff ||
            completion.Outcome != ImmutableBootstrapCompletionOutcome.Ready)
        {
            return LaunchFailure(
                _plan,
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                completionElapsed >= _services.CompletionOperationCutoff
                    ? ManagedFirstInstallationLaunchIssue.TimedOut
                    : cancellationToken.IsCancellationRequested
                        ? ManagedFirstInstallationLaunchIssue.Cancelled
                        : MapBootstrapCompletionFailure(completion),
                completionElapsed >= _services.CompletionOperationCutoff ||
                    cancellationToken.IsCancellationRequested
                        ? null
                        : completion.ExitCode);
        }
        return null;
    }

    private async ValueTask<ManagedFirstInstallationResult> FinalizeAsync()
    {
        using var finalizationDeadline = new CancellationTokenSource(
            ManagedFirstInstallationExperience.WriterLeaseTimeout,
            _services.TimeProvider);
        try
        {
            using VersionManagerWriteLeaseResult finalizeLease = await _services.StateStore
                .TryAcquireWriteLeaseAsync(
                    ManagedFirstInstallationExperience.WriterLeaseTimeout,
                    finalizationDeadline.Token).ConfigureAwait(false);
            if (!finalizeLease.HoldsStatePath(_services.StatePathIdentity))
            {
                return Result(_plan, ManagedFirstInstallationOutcome.RecoveryRequired);
            }

            VersionManagerStateLoadResult bound = await _services.StateStore
                .LoadAsync(finalizationDeadline.Token).ConfigureAwait(false);
            if (!bound.IsSuccess ||
                !ManagedVersionSeedPolicy.IsCanonicalBoundFirstRunState(
                    bound.State!,
                    _plan.ManagedRoot,
                    _promoted!.Admission))
            {
                return Result(_plan, ManagedFirstInstallationOutcome.RecoveryRequired);
            }

            ManagedFirstInstallationTransactionIssue completed = await _promoted!
                .CompleteAsync(finalizationDeadline.Token).ConfigureAwait(false);
            return completed == ManagedFirstInstallationTransactionIssue.None
                ? Result(_plan, ManagedFirstInstallationOutcome.Completed)
                : Result(_plan, ManagedFirstInstallationOutcome.RecoveryRequired);
        }
        catch (OperationCanceledException) when (finalizationDeadline.IsCancellationRequested)
        {
            return Result(_plan, ManagedFirstInstallationOutcome.RecoveryRequired);
        }
    }

    private ImmutableBootstrapWaitBudget RemainingBudget(long started, TimeSpan operationCutoff, TimeSpan cleanupBudget)
    {
        TimeSpan elapsed = _services.TimeProvider.GetElapsedTime(started);
        return SetupWaitBudget.FromElapsed(operationCutoff, elapsed, cleanupBudget);
    }
}
