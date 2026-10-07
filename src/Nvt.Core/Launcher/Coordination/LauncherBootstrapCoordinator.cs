// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Single Application owner for Bootstrap launcher selection, readiness, and exact rollback.</summary>
public sealed partial class LauncherBootstrapCoordinator
{
    /// <summary>The bounded default nested READY deadline.</summary>
    public static readonly TimeSpan DefaultReadyDeadline = TimeSpan.FromSeconds(20);
    /// <summary>The dedicated short startup writer contention budget.</summary>
    public static readonly TimeSpan StartupWriterLeaseTimeout = TimeSpan.FromMilliseconds(250);
    private readonly IVersionManagerStateStore _appStateStore;
    private readonly ILauncherBootstrapStateStore _launcherStateStore;
    private readonly string _managedRoot;
    private readonly IManagedLauncherProcess _process;
    private readonly TimeSpan _readyDeadline;
    private readonly IInstalledLauncherRepository _repository;
    private readonly string _statePath;

    /// <summary>Creates supervision over explicit roots, state paths, repositories and process ports.</summary>
    public LauncherBootstrapCoordinator(
        string managedRoot,
        string statePath,
        IVersionManagerStateStore appStateStore,
        ILauncherBootstrapStateStore launcherStateStore,
        IInstalledLauncherRepository repository,
        IManagedLauncherProcess process,
        TimeSpan? readyDeadline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _managedRoot = ManagedRootPathIdentity.Normalize(managedRoot);
        _statePath = Path.GetFullPath(statePath);
        _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
        _launcherStateStore = launcherStateStore ?? throw new ArgumentNullException(nameof(launcherStateStore));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _readyDeadline = readyDeadline ?? DefaultReadyDeadline;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_readyDeadline, TimeSpan.Zero);
    }

    /// <summary>Runs one launcher selection and recorded fallback transaction.</summary>
    public async ValueTask<LauncherBootstrapResult> RunAsync(CancellationToken cancellationToken)
    {
        VersionManagerWriteLeaseResult lease = await _appStateStore.TryAcquireWriteLeaseAsync(
            StartupWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            lease.Dispose();
            return Result(lease.Issue == VersionManagerWriteLeaseIssue.Busy
                ? LauncherBootstrapOutcome.Busy
                : LauncherBootstrapOutcome.StateUnavailable);
        }

        try
        {
            (LauncherBootstrapOutcome issue, VersionManagerState? appState, LauncherBootstrapState? launcherState) =
                await LoadRawStatesAsync(cancellationToken).ConfigureAwait(false);
            if (issue != LauncherBootstrapOutcome.Ready)
            {
                return Result(issue);
            }
            ActiveAttemptRecovery recovery = await RecoverActiveAttemptsAsync(
                appState!,
                launcherState!,
                cancellationToken).ConfigureAwait(false);
            if (recovery.Outcome != LauncherBootstrapOutcome.Ready)
            {
                return Result(recovery.Outcome, failed: recovery.Failed);
            }
            appState = recovery.AppState;
            launcherState = recovery.LauncherState;
            if (HasCrossJournalConflict(appState, launcherState))
            {
                return Result(LauncherBootstrapOutcome.AppMutationPending);
            }
            if (appState!.PendingMutation is not null)
            {
                return Result(LauncherBootstrapOutcome.AppMutationPending);
            }
            if (appState.PendingActivation is not null)
            {
                ManagedLauncherIdentity? active = launcherState!.Active;
                if (active is null || !TryFindAdmission(appState, active, out ManagedVersionAdmission? activeAdmission))
                {
                    return Result(LauncherBootstrapOutcome.InvalidState);
                }
                InstalledLauncherLaunchResult acquired = await _repository.AcquireLaunchLeaseAsync(
                    _managedRoot,
                    activeAdmission!,
                    cancellationToken).ConfigureAwait(false);
                if (!Matches(acquired, active))
                {
                    acquired.Lease?.Dispose();
                    return Result(MapVerification(acquired.Issue), failed: active);
                }
                using IManagedExecutableLaunchLease executableLease = acquired.Lease!;
                launcherState = launcherState.RecordActiveLaunch();
                if (!await TrySaveLauncherStateAsync(appState, launcherState, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return Result(LauncherBootstrapOutcome.StateUnavailable, failed: active);
                }
                lease.Dispose();
                return await LaunchExistingAsync(
                    appState,
                    launcherState!,
                    active,
                    executableLease,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            if (launcherState!.Pending is { Phase: LauncherActivationPhase.RollbackLaunchRecorded })
            {
                lease.Dispose();
                return await LaunchRecordedRollbackAsync(appState, launcherState, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (launcherState.Pending is { Phase: LauncherActivationPhase.CandidateLaunchRecorded })
            {
                LauncherBootstrapState rollbackRecorded = launcherState.RecordRollbackLaunch();
                if (!await TrySaveLauncherStateAsync(appState, rollbackRecorded, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return Result(LauncherBootstrapOutcome.StateUnavailable);
                }
                lease.Dispose();
                return await LaunchRecordedRollbackAsync(appState, rollbackRecorded, cancellationToken)
                    .ConfigureAwait(false);
            }

            ManagedVersionAdmission? desiredAdmission = FindActiveAdmission(appState);
            if (desiredAdmission is null)
            {
                return Result(LauncherBootstrapOutcome.InvalidState);
            }
            InstalledLauncherLaunchResult desiredResult = await _repository.AcquireLaunchLeaseAsync(
                _managedRoot,
                desiredAdmission,
                cancellationToken).ConfigureAwait(false);
            if (!desiredResult.IsAcquired)
            {
                desiredResult.Lease?.Dispose();
                return Result(MapVerification(desiredResult.Issue));
            }
            using IManagedExecutableLaunchLease desiredExecutableLease = desiredResult.Lease!;
            ManagedLauncherIdentity desired = desiredResult.Identity!;
            if (!desired.MatchesOwner(desiredAdmission))
            {
                return Result(LauncherBootstrapOutcome.DamagedLauncher, failed: desired);
            }

            if (launcherState.Pending is { Phase: LauncherActivationPhase.Requested } requested)
            {
                if (requested.Candidate != desired)
                {
                    return Result(LauncherBootstrapOutcome.StateChanged, failed: requested.Candidate);
                }
            }
            else if (launcherState.Active == desired)
            {
                launcherState = launcherState.RecordActiveLaunch();
                if (!await TrySaveLauncherStateAsync(appState, launcherState, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return Result(LauncherBootstrapOutcome.StateUnavailable, failed: desired);
                }
                lease.Dispose();
                return await LaunchExistingAsync(
                    appState,
                    launcherState,
                    desired,
                    desiredExecutableLease,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                launcherState = launcherState.Begin(desired);
                if (!await TrySaveLauncherStateAsync(appState, launcherState, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return Result(LauncherBootstrapOutcome.StateUnavailable);
                }
            }

            launcherState = launcherState.RecordCandidateLaunch();
            if (!await TrySaveLauncherStateAsync(appState, launcherState, cancellationToken)
                .ConfigureAwait(false))
            {
                return Result(LauncherBootstrapOutcome.StateUnavailable);
            }
            lease.Dispose();
            return await LaunchCandidateAsync(
                appState,
                launcherState,
                desired,
                desiredExecutableLease,
                cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async ValueTask<LauncherBootstrapResult> LaunchExistingAsync(
        VersionManagerState appState,
        LauncherBootstrapState launcherState,
        ManagedLauncherIdentity launcher,
        IManagedExecutableLaunchLease executableLease,
        CancellationToken cancellationToken)
    {
        if (HasCrossJournalConflict(appState, launcherState))
        {
            return Result(LauncherBootstrapOutcome.AppMutationPending, failed: launcher);
        }
        LauncherProcessStartResult start = await _process.StartUntilReadyAsync(
            _managedRoot,
            _statePath,
            launcher,
            executableLease,
            _readyDeadline,
            cancellationToken).ConfigureAwait(false);
        return start.Outcome switch
        {
            LauncherProcessStartOutcome.Ready =>
                await ValidateExistingReadyAsync(launcher, start.ReadyAdmission, cancellationToken)
                    .ConfigureAwait(false),
            LauncherProcessStartOutcome.TerminationUnconfirmed =>
                Result(LauncherBootstrapOutcome.TerminationUnconfirmed, failed: launcher),
            LauncherProcessStartOutcome.StartFailed or
            LauncherProcessStartOutcome.ExitedBeforeReady or
            LauncherProcessStartOutcome.ReadyTimeout or
            LauncherProcessStartOutcome.InvalidReadySignal =>
                await ClearActiveLaunchAsync(launcher, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Managed launcher returned an undefined process outcome."),
        };
    }

    private async ValueTask<LauncherBootstrapResult> LaunchCandidateAsync(
        VersionManagerState appState,
        LauncherBootstrapState launcherState,
        ManagedLauncherIdentity candidate,
        IManagedExecutableLaunchLease executableLease,
        CancellationToken cancellationToken)
    {
        if (HasCrossJournalConflict(appState, launcherState))
        {
            return Result(LauncherBootstrapOutcome.AppMutationPending, failed: candidate);
        }
        LauncherProcessStartResult start = await _process.StartUntilReadyAsync(
            _managedRoot,
            _statePath,
            candidate,
            executableLease,
            _readyDeadline,
            cancellationToken).ConfigureAwait(false);
        return start.Outcome switch
        {
            LauncherProcessStartOutcome.Ready =>
                await CommitCandidateReadyAsync(candidate, start.ReadyAdmission, cancellationToken)
                    .ConfigureAwait(false),
            LauncherProcessStartOutcome.TerminationUnconfirmed =>
                Result(LauncherBootstrapOutcome.TerminationUnconfirmed, failed: candidate),
            LauncherProcessStartOutcome.StartFailed or
            LauncherProcessStartOutcome.ExitedBeforeReady or
            LauncherProcessStartOutcome.ReadyTimeout or
            LauncherProcessStartOutcome.InvalidReadySignal =>
                await RecordAndLaunchRollbackAsync(candidate, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Managed launcher returned an undefined process outcome."),
        };
    }

    private async ValueTask<LauncherBootstrapResult> CommitCandidateReadyAsync(
        ManagedLauncherIdentity candidate,
        ManagedVersionAdmission? readyAdmission,
        CancellationToken cancellationToken)
    {
        using VersionManagerWriteLeaseResult lease = await _appStateStore.TryAcquireWriteLeaseAsync(
            StartupWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            return Result(LauncherBootstrapOutcome.StateUnavailable, failed: candidate);
        }
        (LauncherBootstrapOutcome issue, VersionManagerState? appState, LauncherBootstrapState? launcherState) =
            await LoadConsistentStatesAsync(cancellationToken).ConfigureAwait(false);
        if (issue != LauncherBootstrapOutcome.Ready)
        {
            return Result(issue, failed: candidate);
        }
        if (appState!.PendingActivation is not null || appState.PendingMutation is not null ||
            !TryFindAdmission(appState, candidate, out ManagedVersionAdmission? candidateAdmission) ||
            readyAdmission != candidateAdmission ||
            appState.ActiveVersion != candidate.OwnerAppVersion ||
            launcherState!.Pending is not
            { Phase: LauncherActivationPhase.CandidateLaunchRecorded, Candidate: var pendingCandidate } ||
            pendingCandidate != candidate)
        {
            return Result(LauncherBootstrapOutcome.StateChanged, failed: candidate);
        }
        LauncherBootstrapState committed = launcherState.CommitReady();
        return await TrySaveLauncherStateAsync(appState, committed, cancellationToken).ConfigureAwait(false)
            ? new(LauncherBootstrapOutcome.Ready, candidate, null)
            : Result(LauncherBootstrapOutcome.StateUnavailable, failed: candidate);
    }

    private async ValueTask<LauncherBootstrapResult> RecordAndLaunchRollbackAsync(
        ManagedLauncherIdentity candidate,
        CancellationToken cancellationToken)
    {
        using VersionManagerWriteLeaseResult lease = await _appStateStore.TryAcquireWriteLeaseAsync(
            StartupWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            return Result(LauncherBootstrapOutcome.StateUnavailable, failed: candidate);
        }
        (LauncherBootstrapOutcome issue, VersionManagerState? appState, LauncherBootstrapState? launcherState) =
            await LoadConsistentStatesAsync(cancellationToken).ConfigureAwait(false);
        if (issue != LauncherBootstrapOutcome.Ready)
        {
            return Result(issue, failed: candidate);
        }
        if (launcherState!.Pending is not
            { Phase: LauncherActivationPhase.CandidateLaunchRecorded, Candidate: var pendingCandidate } ||
            pendingCandidate != candidate)
        {
            return Result(LauncherBootstrapOutcome.StateChanged, failed: candidate);
        }
        if (launcherState.Pending.PreviousLastKnownGood is null)
        {
            LauncherBootstrapState failed = launcherState.FailCandidate();
            return await TrySaveLauncherStateAsync(appState!, failed, cancellationToken).ConfigureAwait(false)
                ? Result(LauncherBootstrapOutcome.StartFailed, failed: candidate)
                : Result(LauncherBootstrapOutcome.StateUnavailable, failed: candidate);
        }
        LauncherBootstrapState rollback = launcherState.RecordRollbackLaunch();
        if (!await TrySaveLauncherStateAsync(appState!, rollback, cancellationToken).ConfigureAwait(false))
        {
            return Result(LauncherBootstrapOutcome.StateUnavailable, failed: candidate);
        }
        lease.Dispose();
        return await LaunchRecordedRollbackAsync(appState!, rollback, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<LauncherBootstrapResult> LaunchRecordedRollbackAsync(
        VersionManagerState appState,
        LauncherBootstrapState launcherState,
        CancellationToken cancellationToken)
    {
        PendingLauncherActivation pending = launcherState.Pending is
        { Phase: LauncherActivationPhase.RollbackLaunchRecorded } value
                ? value
                : throw new InvalidOperationException("Launcher rollback is not recorded.");
        ManagedLauncherIdentity? rollback = pending.PreviousLastKnownGood;
        if (rollback is null || !TryFindAdmission(appState, rollback, out ManagedVersionAdmission? admission))
        {
            return Result(LauncherBootstrapOutcome.RollbackUnavailable, failed: pending.Candidate);
        }
        InstalledLauncherLaunchResult verified = await _repository.AcquireLaunchLeaseAsync(
            _managedRoot,
            admission!,
            cancellationToken).ConfigureAwait(false);
        if (!Matches(verified, rollback))
        {
            verified.Lease?.Dispose();
            return Result(LauncherBootstrapOutcome.RollbackUnavailable, failed: pending.Candidate);
        }
        using IManagedExecutableLaunchLease executableLease = verified.Lease!;
        if (HasCrossJournalConflict(appState, launcherState))
        {
            return Result(LauncherBootstrapOutcome.AppMutationPending, failed: pending.Candidate);
        }
        LauncherProcessStartResult start = await _process.StartUntilReadyAsync(
            _managedRoot,
            _statePath,
            rollback,
            executableLease,
            _readyDeadline,
            cancellationToken).ConfigureAwait(false);
        if (start.Outcome != LauncherProcessStartOutcome.Ready)
        {
            return Result(
                start.Outcome == LauncherProcessStartOutcome.TerminationUnconfirmed
                    ? LauncherBootstrapOutcome.TerminationUnconfirmed
                    : LauncherBootstrapOutcome.RollbackUnavailable,
                failed: pending.Candidate);
        }
        using VersionManagerWriteLeaseResult lease = await _appStateStore.TryAcquireWriteLeaseAsync(
            StartupWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            return Result(LauncherBootstrapOutcome.StateUnavailable, failed: pending.Candidate);
        }
        (LauncherBootstrapOutcome issue, VersionManagerState? reloadedApp, LauncherBootstrapState? reloaded) =
            await LoadConsistentStatesAsync(cancellationToken).ConfigureAwait(false);
        ManagedVersionAdmission? activeAdmission = reloadedApp is null ? null : FindActiveAdmission(reloadedApp);
        if (issue != LauncherBootstrapOutcome.Ready ||
            reloadedApp!.PendingActivation is not null ||
            reloadedApp.PendingMutation is not null ||
            start.ReadyAdmission != activeAdmission ||
            reloaded!.Pending != pending)
        {
            return Result(LauncherBootstrapOutcome.StateChanged, failed: pending.Candidate);
        }
        LauncherBootstrapState committed = reloaded.CommitRollback();
        return await TrySaveLauncherStateAsync(reloadedApp, committed, cancellationToken).ConfigureAwait(false)
            ? new(LauncherBootstrapOutcome.RolledBack, rollback, pending.Candidate)
            : Result(LauncherBootstrapOutcome.StateUnavailable, failed: pending.Candidate);
    }

    private async ValueTask<LauncherBootstrapResult> ValidateExistingReadyAsync(
        ManagedLauncherIdentity launcher,
        ManagedVersionAdmission? readyAdmission,
        CancellationToken cancellationToken)
    {
        using VersionManagerWriteLeaseResult lease = await _appStateStore.TryAcquireWriteLeaseAsync(
            StartupWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            return Result(LauncherBootstrapOutcome.StateUnavailable, failed: launcher);
        }
        (LauncherBootstrapOutcome issue, VersionManagerState? appState, LauncherBootstrapState? launcherState) =
            await LoadConsistentStatesAsync(cancellationToken).ConfigureAwait(false);
        ManagedVersionAdmission? activeAdmission = appState is null ? null : FindActiveAdmission(appState);
        if (issue != LauncherBootstrapOutcome.Ready ||
            appState!.PendingMutation is not null ||
            readyAdmission != activeAdmission ||
            launcherState!.Pending is not
            { Candidate: var guarded, Phase: LauncherActivationPhase.ActiveLaunchRecorded } ||
            guarded != launcher || launcherState.Active != launcher)
        {
            return Result(LauncherBootstrapOutcome.StateChanged, failed: launcher);
        }
        LauncherBootstrapState cleared = launcherState.ClearActiveLaunch(launcher);
        return await TrySaveLauncherStateAsync(appState, cleared, cancellationToken).ConfigureAwait(false)
            ? new(LauncherBootstrapOutcome.Ready, launcher, null)
            : Result(LauncherBootstrapOutcome.StateUnavailable, failed: launcher);
    }

    private async ValueTask<LauncherBootstrapResult> ClearActiveLaunchAsync(
        ManagedLauncherIdentity launcher,
        CancellationToken cancellationToken)
    {
        using VersionManagerWriteLeaseResult lease = await _appStateStore.TryAcquireWriteLeaseAsync(
            StartupWriterLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            return Result(LauncherBootstrapOutcome.StateUnavailable, failed: launcher);
        }
        (LauncherBootstrapOutcome issue, VersionManagerState? appState, LauncherBootstrapState? state) =
            await LoadConsistentStatesAsync(cancellationToken).ConfigureAwait(false);
        if (issue != LauncherBootstrapOutcome.Ready || state!.Pending is not
            { Candidate: var guarded, Phase: LauncherActivationPhase.ActiveLaunchRecorded } ||
            guarded != launcher)
        {
            return Result(LauncherBootstrapOutcome.StateChanged, failed: launcher);
        }
        LauncherBootstrapState cleared = state.ClearActiveLaunch(launcher);
        return await TrySaveLauncherStateAsync(appState!, cleared, cancellationToken).ConfigureAwait(false)
            ? Result(LauncherBootstrapOutcome.StartFailed, failed: launcher)
            : Result(LauncherBootstrapOutcome.StateUnavailable, failed: launcher);
    }

    private async ValueTask<(LauncherBootstrapOutcome, VersionManagerState?)> LoadAppStateAsync(
        CancellationToken cancellationToken)
    {
        VersionManagerStateLoadResult loaded = await _appStateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.IsSuccess)
        {
            return (loaded.Issue == VersionManagerStateLoadIssue.Unavailable
                ? LauncherBootstrapOutcome.StateUnavailable
                : LauncherBootstrapOutcome.InvalidState, null);
        }
        return loaded.State!.IsBoundToManagedRoot(_managedRoot)
            ? (LauncherBootstrapOutcome.Ready, loaded.State)
            : (LauncherBootstrapOutcome.ManagedRootMismatch, null);
    }

    private async ValueTask<(LauncherBootstrapOutcome, LauncherBootstrapState?)> LoadLauncherStateAsync(
        CancellationToken cancellationToken)
    {
        LauncherBootstrapStateLoadResult loaded = await _launcherStateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (loaded.Issue == LauncherBootstrapStateLoadIssue.Missing)
        {
            return (LauncherBootstrapOutcome.Ready,
                LauncherBootstrapState.Create(_managedRoot, null, null, null, null));
        }
        if (!loaded.IsSuccess)
        {
            return (loaded.Issue == LauncherBootstrapStateLoadIssue.Unavailable
                ? LauncherBootstrapOutcome.StateUnavailable
                : LauncherBootstrapOutcome.InvalidState, null);
        }
        return loaded.State!.IsBoundToManagedRoot(_managedRoot)
            ? (LauncherBootstrapOutcome.Ready, loaded.State)
            : (LauncherBootstrapOutcome.ManagedRootMismatch, null);
    }

    private async ValueTask<(LauncherBootstrapOutcome, VersionManagerState?, LauncherBootstrapState?)>
        LoadRawStatesAsync(CancellationToken cancellationToken)
    {
        (LauncherBootstrapOutcome appIssue, VersionManagerState? appState) =
            await LoadAppStateAsync(cancellationToken).ConfigureAwait(false);
        if (appIssue != LauncherBootstrapOutcome.Ready)
        {
            return (appIssue, null, null);
        }
        (LauncherBootstrapOutcome launcherIssue, LauncherBootstrapState? launcherState) =
            await LoadLauncherStateAsync(cancellationToken).ConfigureAwait(false);
        if (launcherIssue != LauncherBootstrapOutcome.Ready)
        {
            return (launcherIssue, null, null);
        }
        return (LauncherBootstrapOutcome.Ready, appState, launcherState);
    }

    private async ValueTask<(LauncherBootstrapOutcome, VersionManagerState?, LauncherBootstrapState?)>
        LoadConsistentStatesAsync(CancellationToken cancellationToken)
    {
        (LauncherBootstrapOutcome issue, VersionManagerState? appState, LauncherBootstrapState? launcherState) =
            await LoadRawStatesAsync(cancellationToken).ConfigureAwait(false);
        return issue == LauncherBootstrapOutcome.Ready && HasCrossJournalConflict(appState!, launcherState!)
            ? (LauncherBootstrapOutcome.AppMutationPending, null, null)
            : (issue, appState, launcherState);
    }

    private async ValueTask<bool> TrySaveLauncherStateAsync(
        VersionManagerState appState,
        LauncherBootstrapState state,
        CancellationToken cancellationToken)
    {
        if (HasCrossJournalConflict(appState, state))
        {
            return false;
        }
        LauncherBootstrapStateSaveResult saved = await _launcherStateStore.TrySaveAsync(
            state,
            cancellationToken).ConfigureAwait(false);
        return saved.IsSuccess;
    }

    private static bool HasCrossJournalConflict(
        VersionManagerState appState,
        LauncherBootstrapState launcherState)
    {
        return launcherState.Pending is { Phase: not LauncherActivationPhase.ActiveLaunchRecorded } &&
               (appState.PendingActivation is not null || appState.PendingMutation is not null);
    }

    private static ManagedVersionAdmission? FindActiveAdmission(VersionManagerState state)
    {
        return state.ActiveVersion is { } active
            ? state.Admissions.SingleOrDefault(candidate => candidate.Version == active)
            : null;
    }

    private static bool TryFindAdmission(
        VersionManagerState state,
        ManagedLauncherIdentity launcher,
        out ManagedVersionAdmission? admission)
    {
        admission = state.Admissions.SingleOrDefault(candidate => candidate.Version == launcher.OwnerAppVersion);
        return admission is not null && launcher.MatchesOwner(admission);
    }

    private static bool Matches(InstalledLauncherLaunchResult result, ManagedLauncherIdentity expected)
    {
        return result.IsAcquired && result.Identity == expected;
    }

    private static LauncherBootstrapOutcome MapVerification(InstalledLauncherIssue issue)
    {
        return issue == InstalledLauncherIssue.ProtocolMismatch
            ? LauncherBootstrapOutcome.ProtocolMismatch
            : issue is InstalledLauncherIssue.Tampered or InstalledLauncherIssue.InvalidManifest or InstalledLauncherIssue.UnsafePath
                ? LauncherBootstrapOutcome.DamagedLauncher
                : LauncherBootstrapOutcome.StateUnavailable;
    }

    private static LauncherBootstrapResult Result(
        LauncherBootstrapOutcome outcome,
        ManagedLauncherIdentity? running = null,
        ManagedLauncherIdentity? failed = null)
    {
        return new(outcome, running, failed);
    }
}
