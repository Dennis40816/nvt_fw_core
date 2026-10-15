// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Transport;

using static Nvt.Core.Launcher.Setup.FirstInstallationOutcomes;

namespace Nvt.Core.Launcher.Setup;

/// <summary>Single Application owner for genuinely-absent first installation.</summary>
public sealed class ManagedFirstInstallationExperience
{
    /// <summary>Bounded wait for the canonical cross-process state writer.</summary>
    public static readonly TimeSpan WriterLeaseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Bounded first-install admission wait from the recorded Bootstrap launch through Launcher admission.
    /// </summary>
    public static readonly TimeSpan DefaultAdmissionOperationCutoff = TimeSpan.FromSeconds(30);

    private readonly FirstInstallationServices _services;
    private readonly Action<Exception>? _observerDiagnostics;

    /// <summary>Creates the one first-install orchestration owner.</summary>
    public ManagedFirstInstallationExperience(
        string statePathIdentity,
        IVersionManagerStateStore stateStore,
        IManagedInstallationRootProbe rootProbe,
        IManagedDistributionPayloadSource payloadSource,
        IFreshInstallationCandidateSource candidateSource,
        IManagedFirstInstallationRootMaterializer rootMaterializer,
        IImmutableBootstrapLeaseHandoff handoff,
        TimeSpan? admissionOperationCutoff = null,
        TimeSpan? completionOperationCutoff = null,
        TimeProvider? timeProvider = null,
        Action<Exception>? observerDiagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(statePathIdentity) ||
            !Path.IsPathFullyQualified(statePathIdentity))
        {
            throw new ArgumentException("State path identity must be absolute.", nameof(statePathIdentity));
        }
        string statePathIdentityValue = Path.GetFullPath(statePathIdentity);
        IVersionManagerStateStore stateStoreValue = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        IManagedInstallationRootProbe rootProbeValue = rootProbe ?? throw new ArgumentNullException(nameof(rootProbe));
        IManagedDistributionPayloadSource payloadSourceValue = payloadSource ?? throw new ArgumentNullException(nameof(payloadSource));
        IFreshInstallationCandidateSource candidateSourceValue = candidateSource ?? throw new ArgumentNullException(nameof(candidateSource));
        IManagedFirstInstallationRootMaterializer rootMaterializerValue = rootMaterializer ?? throw new ArgumentNullException(nameof(rootMaterializer));
        IImmutableBootstrapLeaseHandoff handoffValue = handoff ?? throw new ArgumentNullException(nameof(handoff));
        TimeSpan admissionOperationCutoffValue = admissionOperationCutoff ?? DefaultAdmissionOperationCutoff;
        TimeSpan completionOperationCutoffValue = completionOperationCutoff ??
            ManagedLauncherEntryCoordinator.DefaultCompletionOperationCutoff;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(admissionOperationCutoffValue, TimeSpan.Zero, nameof(admissionOperationCutoff));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(completionOperationCutoffValue, TimeSpan.Zero, nameof(completionOperationCutoff));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(admissionOperationCutoffValue, SetupWaitBudget.MaximumTimerDuration, nameof(admissionOperationCutoff));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(completionOperationCutoffValue, SetupWaitBudget.MaximumTimerDuration, nameof(completionOperationCutoff));
        TimeProvider timeProviderValue = timeProvider ?? TimeProvider.System;
        _observerDiagnostics = observerDiagnostics;
        _services = new(statePathIdentityValue, stateStoreValue, rootProbeValue,
            payloadSourceValue, candidateSourceValue, rootMaterializerValue, handoffValue,
            new(admissionOperationCutoffValue, completionOperationCutoffValue), timeProviderValue);
    }

    /// <summary>Builds one immutable plan without persistent mutation.</summary>
    public async ValueTask<ManagedFirstInstallationPlanResult> PrepareAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return PlanFailure(ManagedFirstInstallationOutcome.Cancelled);
        }
        try
        {
            return await PrepareCoreAsync(managedRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlanFailure(ManagedFirstInstallationOutcome.Cancelled);
        }
    }

    private async ValueTask<ManagedFirstInstallationPlanResult> PrepareCoreAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeRoot(managedRoot, out string root))
        {
            return PlanFailure(ManagedFirstInstallationOutcome.InvalidDestination);
        }

        ManagedFirstInstallationOutcome? destinationIssue = await FirstInstallationDestination
            .ObserveAsync(_services, root, cancellationToken).ConfigureAwait(false);
        if (destinationIssue is { } failure)
        {
            return PlanFailure(failure);
        }

        ManagedDistributionPayloadInspectionResult payload = await _services.PayloadSource
            .InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!payload.IsSuccess)
        {
            return PlanFailure(MapPayloadIssue(payload.Issue));
        }

        FreshInstallationCandidateResult candidate = await _services.CandidateSource
            .InspectFreshInstallationAsync(cancellationToken).ConfigureAwait(false);
        return candidate.IsSuccess
            ? new(
                new ManagedFirstInstallationPlan(
                    root,
                    _services.StatePathIdentity,
                    payload.Identity!,
                    candidate.Candidate!),
                ManagedFirstInstallationOutcome.ReadyToInstall)
            : PlanFailure(MapCandidateIssue(candidate.Issue));
    }

    /// <summary>Revalidates and executes a reviewed plan without a progress observer.</summary>
    public ValueTask<ManagedFirstInstallationResult> InstallAndLaunchAsync(
        ManagedFirstInstallationPlan plan,
        CancellationToken cancellationToken) => InstallAndLaunchAsync(plan, null, cancellationToken);

    /// <summary>Revalidates and executes one exact user-reviewed first-install plan.</summary>
    public async ValueTask<ManagedFirstInstallationResult> InstallAndLaunchAsync(
        ManagedFirstInstallationPlan plan,
        IProgress<ManagedFirstInstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (cancellationToken.IsCancellationRequested)
        {
            return Result(plan, ManagedFirstInstallationOutcome.Cancelled);
        }
        try
        {
            IProgress<ManagedFirstInstallationProgress>? safeProgress = progress is null
                ? null
                : new NonThrowingProgress(progress, _observerDiagnostics);
            return await new FirstInstallationAttempt(_services, plan, safeProgress).RunAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(plan, ManagedFirstInstallationOutcome.Cancelled);
        }
    }

    private sealed class NonThrowingProgress(
        IProgress<ManagedFirstInstallationProgress> inner,
        Action<Exception>? observerDiagnostics)
        : IProgress<ManagedFirstInstallationProgress>
    {
        private readonly IProgress<ManagedFirstInstallationProgress> _inner = inner ??
            throw new ArgumentNullException(nameof(inner));

        public void Report(ManagedFirstInstallationProgress value)
        {
            try
            {
                _inner.Report(value);
            }
            // Presentation observers cannot change installation; fatal exceptions still propagate.
            catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException))
            {
                try
                {
                    observerDiagnostics?.Invoke(exception);
                }
                // Diagnostics are non-authoritative observers too; fatal exceptions still propagate.
                catch (Exception diagnosticException) when (diagnosticException is not (OutOfMemoryException or AccessViolationException))
                {
                }
            }
        }
    }
}
