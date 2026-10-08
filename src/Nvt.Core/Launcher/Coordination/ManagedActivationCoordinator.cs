// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Stable outcome from one supervised application start.</summary>
public enum ManagedProcessStartOutcome
{
    /// <summary>The process reported the authenticated expected ready version.</summary>
    Ready,
    /// <summary>The executable could not be started.</summary>
    StartFailed,
    /// <summary>The process exited before ready.</summary>
    ExitedBeforeReady,
    /// <summary>The ready deadline elapsed.</summary>
    ReadyTimeout,
    /// <summary>The inherited one-use ready message was invalid.</summary>
    InvalidReadySignal,
    /// <summary>The host could not confirm that a rejected or timed-out process exited.</summary>
    TerminationUnconfirmed,
}

/// <summary>Stable process adapter result without command-line handshake material.</summary>
public sealed record ManagedProcessStartResult(
    ManagedProcessStartOutcome Outcome,
    int? ExitCode);

/// <summary>Authoritative status of the child-owned managed-process lifetime.</summary>
public enum ManagedProcessLifetimeStatus
{
    /// <summary>The prior managed child still owns its inherited lifetime lease.</summary>
    Active,
    /// <summary>No process owns the prior managed-child lifetime lease.</summary>
    Exited,
    /// <summary>The lifetime lease could not be inspected safely.</summary>
    Unavailable,
}

/// <summary>Starts one exact managed version through an inherited one-use ready channel.</summary>
public interface IManagedApplicationProcess
{
    /// <summary>Inspects the child-owned lifetime lease for a recoverable recorded attempt.</summary>
    ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(
        string managedRoot,
        CancellationToken cancellationToken);

    /// <summary>Starts and supervises one exact managed payload.</summary>
    /// <param name="managedRoot">Stable launcher-owned managed root.</param>
    /// <param name="version">Exact verified target version.</param>
    /// <param name="executableLease">Repository-owned exact executable held through process creation.</param>
    /// <param name="readyDeadline">Budget from start entry through lease acquisition, validation, contained creation, and readiness; cleanup confirmation may extend the terminal result by up to ten seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The supervised start result.</returns>
    ValueTask<ManagedProcessStartResult> StartUntilReadyAsync(
        string managedRoot,
        ManagedAppVersion version,
        IManagedExecutableLaunchLease executableLease,
        TimeSpan readyDeadline,
        CancellationToken cancellationToken);
}

/// <summary>Stable application-side outcome from consuming the inherited ready context.</summary>
public enum ApplicationReadySignalOutcome
{
    /// <summary>No inherited launcher context was present.</summary>
    NotInherited,
    /// <summary>The exact inherited context accepted the one-use ready write.</summary>
    Reported,
    /// <summary>The inherited context was incomplete, mismatched, or malformed.</summary>
    InvalidInheritedContext,
    /// <summary>The exact inherited channel could not accept the ready write.</summary>
    WriteFailed,
}

/// <summary>Application-side one-use inherited ready-channel writer.</summary>
public interface IApplicationReadySignal
{
    /// <summary>Reports that the expected version reached the usable main-window boundary.</summary>
    /// <param name="version">Running application version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed inherited-context and write outcome.</returns>
    ValueTask<ApplicationReadySignalOutcome> ReportReadyAsync(
        ManagedAppVersion version,
        CancellationToken cancellationToken);
}

/// <summary>Launcher run result category.</summary>
public enum ManagedLauncherOutcome
{
    /// <summary>The selected version reached ready.</summary>
    Ready,
    /// <summary>The pending version failed and last-known-good reached ready.</summary>
    RolledBack,
    /// <summary>Launcher state is missing, malformed, or unavailable.</summary>
    InvalidState,
    /// <summary>No active admitted version is available.</summary>
    NoActiveVersion,
    /// <summary>The selected installed payload is damaged.</summary>
    DamagedVersion,
    /// <summary>The selected version failed and no valid rollback completed.</summary>
    StartFailed,
    /// <summary>A required durable activation phase could not be committed.</summary>
    StateUnavailable,
    /// <summary>Another application or launcher owns the version-manager transaction.</summary>
    Busy,
    /// <summary>The rejected process may still be running, so rollback was not started.</summary>
    TerminationUnconfirmed,
}

/// <summary>Stable launcher outcome with selected and optional failed versions.</summary>
public sealed record ManagedLauncherResult(
    ManagedLauncherOutcome Outcome,
    ManagedAppVersion? RunningVersion,
    ManagedAppVersion? FailedVersion);

/// <summary>Application-owned launcher workflow for verification, ready commit, and one rollback.</summary>
public sealed class ManagedActivationCoordinator
{
    private enum ManagedVersionHealth
    {
        Healthy,
        Unhealthy,
        Unavailable,
    }

    /// <summary>The bounded default main-window ready deadline.</summary>
    public static readonly TimeSpan DefaultReadyDeadline = TimeSpan.FromSeconds(20);
    /// <summary>Bounded wait for another process to release the exact writer lease.</summary>
    public static readonly TimeSpan DefaultWriterLeaseTimeout = TimeSpan.FromSeconds(5);

    private readonly string _managedRoot;
    private readonly IManagedApplicationProcess _process;
    private readonly IManagedVersionRepository _repository;
    private readonly TimeSpan _readyDeadline;
    private readonly IVersionManagerStateStore _stateStore;

    /// <summary>Creates the stable launcher use case.</summary>
    /// <param name="managedRoot">Stable launcher-owned root.</param>
    /// <param name="stateStore">Atomic launcher state.</param>
    /// <param name="repository">Installed payload verifier.</param>
    /// <param name="process">Inherited ready-channel process adapter.</param>
    /// <param name="readyDeadline">Optional deterministic deadline.</param>
    public ManagedActivationCoordinator(
        string managedRoot,
        IVersionManagerStateStore stateStore,
        IManagedVersionRepository repository,
        IManagedApplicationProcess process,
        TimeSpan? readyDeadline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        _managedRoot = ManagedRootPathIdentity.Normalize(managedRoot);
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _readyDeadline = readyDeadline ?? DefaultReadyDeadline;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_readyDeadline, TimeSpan.Zero);
    }

    /// <summary>Runs one stable launcher selection and at most one automatic rollback.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stable launcher result.</returns>
    public async ValueTask<ManagedLauncherResult> RunAsync(CancellationToken cancellationToken)
    {
        using VersionManagerWriteLeaseResult lease = await _stateStore.TryAcquireWriteLeaseAsync(
            DefaultWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            return new(
                lease.Issue == VersionManagerWriteLeaseIssue.Busy
                    ? ManagedLauncherOutcome.Busy
                    : ManagedLauncherOutcome.StateUnavailable,
                null,
                null);
        }
        VersionManagerStateLoadResult loaded = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess)
        {
            return new(ManagedLauncherOutcome.InvalidState, null, null);
        }
        VersionManagerState state = loaded.State!;
        if (!state.IsBoundToManagedRoot(_managedRoot))
        {
            return new(ManagedLauncherOutcome.InvalidState, null, null);
        }
        PendingVersionActivation? pending = state.PendingActivation;
        if (pending?.Phase == VersionActivationPhase.ActiveLaunchRecorded)
        {
            ManagedProcessLifetimeStatus lifetime = await _process.GetLifetimeStatusAsync(
                _managedRoot,
                cancellationToken).ConfigureAwait(false);
            if (lifetime != ManagedProcessLifetimeStatus.Exited)
            {
                return new(
                    ManagedLauncherOutcome.TerminationUnconfirmed,
                    null,
                    pending.CandidateVersion);
            }
            state = VersionActivationPolicy.ClearActiveLaunch(state, pending.CandidateVersion);
            if (!await TrySaveAsync(state, cancellationToken).ConfigureAwait(false))
            {
                return new(
                    ManagedLauncherOutcome.StateUnavailable,
                    null,
                    pending.CandidateVersion);
            }
            pending = null;
        }
        if (pending?.Phase == VersionActivationPhase.RollbackLaunchRecorded)
        {
            return await LaunchRecordedRollbackAsync(state, cancellationToken).ConfigureAwait(false);
        }
        if (pending?.Phase == VersionActivationPhase.CandidateLaunchRecorded)
        {
            return await RecordAndLaunchRollbackAsync(
                state,
                pending.CandidateVersion,
                cancellationToken).ConfigureAwait(false);
        }

        ManagedAppVersion? target = pending?.CandidateVersion ?? state.ActiveVersion;
        if (target is null)
        {
            return new(ManagedLauncherOutcome.NoActiveVersion, null, null);
        }

        ManagedVersionHealth targetHealth = await GetHealthAsync(
            state,
            target.Value,
            cancellationToken).ConfigureAwait(false);
        if (targetHealth == ManagedVersionHealth.Unavailable)
        {
            return new(ManagedLauncherOutcome.StateUnavailable, null, target);
        }
        if (targetHealth == ManagedVersionHealth.Unhealthy)
        {
            return pending?.CandidateVersion == target
                ? await RecordAndLaunchRollbackAsync(state, target.Value, cancellationToken).ConfigureAwait(false)
                : new(ManagedLauncherOutcome.DamagedVersion, null, target);
        }

        ManagedVersionAdmission? targetAdmission = state.Admissions.SingleOrDefault(
            admission => admission.Version == target.Value);
        ManagedExecutableLaunchLeaseResult launchLease = targetAdmission is null
            ? new(null, ManagedExecutableLaunchIssue.Unavailable)
            : await _repository.AcquireApplicationLaunchLeaseAsync(
                _managedRoot,
                targetAdmission,
                cancellationToken).ConfigureAwait(false);
        if (!launchLease.IsAcquired)
        {
            return new(ManagedLauncherOutcome.StateUnavailable, null, target);
        }
        using IManagedExecutableLaunchLease executableLease = launchLease.Lease!;

        if (pending?.CandidateVersion == target)
        {
            state = VersionActivationPolicy.RecordCandidateLaunch(state);
            if (!await TrySaveAsync(state, cancellationToken).ConfigureAwait(false))
            {
                return new(ManagedLauncherOutcome.StateUnavailable, null, target);
            }
        }
        else
        {
            state = VersionActivationPolicy.RecordActiveLaunch(state);
            if (!await TrySaveAsync(state, cancellationToken).ConfigureAwait(false))
            {
                return new(ManagedLauncherOutcome.StateUnavailable, null, target);
            }
        }

        ManagedProcessStartResult start = await _process.StartUntilReadyAsync(
            _managedRoot,
            target.Value,
            executableLease,
            _readyDeadline,
            cancellationToken).ConfigureAwait(false);
        if (start.Outcome == ManagedProcessStartOutcome.Ready)
        {
            state = state.PendingActivation?.Phase == VersionActivationPhase.CandidateLaunchRecorded
                ? VersionActivationPolicy.CommitReady(state, target.Value)
                : state.PendingActivation?.Phase == VersionActivationPhase.ActiveLaunchRecorded
                    ? VersionActivationPolicy.ClearActiveLaunch(state, target.Value)
                    : throw new InvalidOperationException("Ready process has no matching durable launch phase.");
            return await TrySaveAsync(state, cancellationToken).ConfigureAwait(false)
                ? new(ManagedLauncherOutcome.Ready, target, null)
                : new(ManagedLauncherOutcome.StateUnavailable, target, null);
        }

        if (start.Outcome == ManagedProcessStartOutcome.TerminationUnconfirmed)
        {
            return new(ManagedLauncherOutcome.TerminationUnconfirmed, null, target);
        }
        if (state.PendingActivation?.Phase == VersionActivationPhase.ActiveLaunchRecorded)
        {
            VersionManagerState cleared = VersionActivationPolicy.ClearActiveLaunch(state, target.Value);
            return await TrySaveAsync(cleared, cancellationToken).ConfigureAwait(false)
                ? new(ManagedLauncherOutcome.StartFailed, null, target)
                : new(ManagedLauncherOutcome.StateUnavailable, null, target);
        }
        return await RecordAndLaunchRollbackAsync(state, target.Value, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ManagedLauncherResult> RecordAndLaunchRollbackAsync(
        VersionManagerState state,
        ManagedAppVersion failedVersion,
        CancellationToken cancellationToken)
    {
        ActivationRecoveryDecision recovery = VersionActivationPolicy.RecordRollbackLaunch(state, failedVersion);
        return await TrySaveAsync(recovery.State, cancellationToken).ConfigureAwait(false)
            ? await LaunchRecordedRollbackAsync(recovery.State, cancellationToken).ConfigureAwait(false)
            : new(ManagedLauncherOutcome.StateUnavailable, null, failedVersion);
    }

    private async ValueTask<ManagedLauncherResult> LaunchRecordedRollbackAsync(
        VersionManagerState state,
        CancellationToken cancellationToken)
    {
        PendingVersionActivation pending = state.PendingActivation is
        { Phase: VersionActivationPhase.RollbackLaunchRecorded } value
                ? value
                : throw new InvalidOperationException("Rollback launch was not durably recorded.");
        ManagedAppVersion failedVersion = pending.CandidateVersion;
        ManagedAppVersion? rollback = pending.PreviousLastKnownGoodVersion;
        if (rollback is null || rollback == failedVersion)
        {
            ActivationRecoveryDecision unavailableRollback = VersionActivationPolicy.FailActivation(
                state,
                failedVersion);
            return await TrySaveAsync(unavailableRollback.State, cancellationToken).ConfigureAwait(false)
                ? new(ManagedLauncherOutcome.StartFailed, null, failedVersion)
                : new(ManagedLauncherOutcome.StateUnavailable, null, failedVersion);
        }

        ManagedVersionHealth rollbackHealth = await GetHealthAsync(
            state,
            rollback.Value,
            cancellationToken).ConfigureAwait(false);
        if (rollbackHealth == ManagedVersionHealth.Unavailable)
        {
            return new(ManagedLauncherOutcome.StateUnavailable, null, failedVersion);
        }
        if (rollbackHealth == ManagedVersionHealth.Unhealthy)
        {
            ActivationRecoveryDecision unavailableRollback = VersionActivationPolicy.FailActivation(
                state,
                failedVersion);
            return await TrySaveAsync(unavailableRollback.State, cancellationToken).ConfigureAwait(false)
                ? new(ManagedLauncherOutcome.StartFailed, null, failedVersion)
                : new(ManagedLauncherOutcome.StateUnavailable, null, failedVersion);
        }

        ManagedVersionAdmission? rollbackAdmission = state.Admissions.SingleOrDefault(
            admission => admission.Version == rollback.Value);
        ManagedExecutableLaunchLeaseResult launchLease = rollbackAdmission is null
            ? new(null, ManagedExecutableLaunchIssue.Unavailable)
            : await _repository.AcquireApplicationLaunchLeaseAsync(
                _managedRoot,
                rollbackAdmission,
                cancellationToken).ConfigureAwait(false);
        if (!launchLease.IsAcquired)
        {
            return new(ManagedLauncherOutcome.StateUnavailable, null, failedVersion);
        }
        using IManagedExecutableLaunchLease executableLease = launchLease.Lease!;
        ManagedProcessStartResult fallback = await _process.StartUntilReadyAsync(
            _managedRoot,
            rollback.Value,
            executableLease,
            _readyDeadline,
            cancellationToken).ConfigureAwait(false);
        if (fallback.Outcome == ManagedProcessStartOutcome.Ready)
        {
            VersionManagerState committed = VersionActivationPolicy.CommitRollback(state, rollback.Value);
            return await TrySaveAsync(committed, cancellationToken).ConfigureAwait(false)
                ? new(ManagedLauncherOutcome.RolledBack, rollback, failedVersion)
                : new(ManagedLauncherOutcome.StateUnavailable, rollback, failedVersion);
        }
        if (fallback.Outcome == ManagedProcessStartOutcome.TerminationUnconfirmed)
        {
            return new(ManagedLauncherOutcome.TerminationUnconfirmed, null, failedVersion);
        }

        ActivationRecoveryDecision terminal = VersionActivationPolicy.FailActivation(state, failedVersion);
        return await TrySaveAsync(terminal.State, cancellationToken).ConfigureAwait(false)
            ? new(ManagedLauncherOutcome.StartFailed, null, failedVersion)
            : new(ManagedLauncherOutcome.StateUnavailable, null, failedVersion);
    }

    private async ValueTask<bool> TrySaveAsync(
        VersionManagerState state,
        CancellationToken cancellationToken)
    {
        VersionManagerStateSaveResult result = await _stateStore.TrySaveAsync(
            state,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    private async ValueTask<ManagedVersionHealth> GetHealthAsync(
        VersionManagerState state,
        ManagedAppVersion version,
        CancellationToken cancellationToken)
    {
        ManagedVersionAdmission? admission = state.Admissions.SingleOrDefault(
            candidate => candidate.Version == version);
        if (admission is null)
        {
            return ManagedVersionHealth.Unhealthy;
        }
        ManagedVersionInventoryReadResult inventoryResult = await _repository.InventoryAsync(
            _managedRoot,
            state.Admissions,
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            state.FailedActivationVersion,
            cancellationToken).ConfigureAwait(false);
        return !inventoryResult.IsSuccess
            ? ManagedVersionHealth.Unavailable
            : inventoryResult.Inventory!.Find(version) is
            {
                AdmissionState: ManagedVersionAdmissionState.Admitted,
                Integrity: ManagedVersionIntegrity.Healthy,
                ObservedAdmission: { } observed,
            } && observed == admission
                ? ManagedVersionHealth.Healthy
                : ManagedVersionHealth.Unhealthy;
    }
}
