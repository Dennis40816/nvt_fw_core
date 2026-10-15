// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.Launcher.Setup;

/// <summary>Terminal single-entry decision rendered by the distribution Launcher.</summary>
public enum ManagedLauncherEntryOutcome
{
    /// <summary>The exact distribution payload could not be observed completely.</summary>
    PayloadUnavailable,
    /// <summary>The distribution descriptor or embedded Bootstrap metadata is invalid.</summary>
    PayloadInvalid,
    /// <summary>A healthy installed version was started, directly or through LKG rollback.</summary>
    LaunchInstalled,
    /// <summary>Root and durable state are genuinely absent, so Setup may be shown.</summary>
    SetupRequired,
    /// <summary>Installed or residual facts require the separate recovery capability.</summary>
    RecoveryRequired,
    /// <summary>Another process owns the bounded launch transaction.</summary>
    Busy,
    /// <summary>Local health could not be established before its hard deadline.</summary>
    HealthUnavailable,
    /// <summary>The exact Bootstrap was admitted but process creation or READY failed.</summary>
    LaunchFailed,
    /// <summary>The Root Bootstrap tree could not be proven terminated or safely released.</summary>
    TerminationUnconfirmed,
}

/// <summary>Immutable entry result including local admission duration.</summary>
public sealed record ManagedLauncherEntryResult(
    ManagedLauncherEntryOutcome Outcome,
    string? ManagedRoot,
    TimeSpan AdmissionElapsed,
    TimeSpan TotalElapsed);

/// <summary>Single Application owner for Launcher-to-Setup-or-Bootstrap routing.</summary>
public sealed class ManagedLauncherEntryCoordinator
{
    /// <summary>Accepted P95 target for local-only healthy-installation classification.</summary>
    // Public in frozen ManagedLauncherEntry.cs:446-447.
    public static readonly TimeSpan HealthyRoutingP95Target = TimeSpan.FromMilliseconds(100);

    /// <summary>Delay before Presentation may show non-blocking startup progress.</summary>
    public static readonly TimeSpan ProgressDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Hard cutoff for state and exact-root health observation.</summary>
    public static TimeSpan DefaultHealthObservationDeadline => ProgressDelay;

    /// <summary>
    /// Admission work cutoff, including exact Bootstrap hashing on cold or scanned storage,
    /// while leaving bounded time to prove outer-tree cleanup.
    /// </summary>
    public static readonly TimeSpan DefaultAdmissionOperationCutoff = TimeSpan.FromSeconds(5);

    /// <summary>Completion work cutoff, leaving bounded time to prove outer-tree cleanup.</summary>
    public static readonly TimeSpan DefaultCompletionOperationCutoff = TimeSpan.FromMilliseconds(44500);

    /// <summary>Maximum caller-visible cleanup observation after either operation cutoff.</summary>
    public static readonly TimeSpan DefaultCleanupObservationBudget = TimeSpan.FromMilliseconds(500);

    /// <summary>Hard deadline for bounded local entry admission including cleanup observation.</summary>
    public static TimeSpan DefaultAdmissionDeadline =>
        DefaultAdmissionOperationCutoff + DefaultCleanupObservationBudget;

    /// <summary>Independent Bootstrap/READY deadline including cleanup observation.</summary>
    public static TimeSpan DefaultCompletionDeadline =>
        DefaultCompletionOperationCutoff + DefaultCleanupObservationBudget;

    private sealed record EntryBudgets(
        TimeSpan AdmissionOperationCutoff,
        TimeSpan CompletionOperationCutoff,
        TimeSpan HealthObservationDeadline);

    private readonly EntryBudgets _budgets;
    private readonly string _defaultManagedRoot;
    private readonly IImmutableBootstrapHandoff _handoff;
    private readonly IManagedDistributionPayloadSource _payloadSource;
    private readonly IManagedInstallationRootProbe _rootProbe;
    private readonly ManagedAppVersion _runningLauncherVersion;
    private readonly IVersionManagerStateStore _stateStore;
    private readonly TimeProvider _timeProvider;
    /// <summary>Creates the one bounded local entry coordinator.</summary>
    public ManagedLauncherEntryCoordinator(
        string defaultManagedRoot,
        IVersionManagerStateStore stateStore,
        IManagedInstallationRootProbe rootProbe,
        IManagedDistributionPayloadSource payloadSource,
        ManagedAppVersion runningLauncherVersion,
        IImmutableBootstrapHandoff handoff,
        TimeSpan? admissionOperationCutoff = null,
        TimeSpan? completionOperationCutoff = null,
        TimeProvider? timeProvider = null,
        TimeSpan? healthObservationDeadline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultManagedRoot);
        // Frozen ManagedLauncherEntry.cs:498-499 resolves relative default roots;
        // first-install plan admission separately requires a fully qualified root.
        _defaultManagedRoot = ManagedRootPathIdentity.Normalize(defaultManagedRoot);
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _rootProbe = rootProbe ?? throw new ArgumentNullException(nameof(rootProbe));
        _payloadSource = payloadSource ?? throw new ArgumentNullException(nameof(payloadSource));
        _runningLauncherVersion = runningLauncherVersion;
        _handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
        TimeSpan admission = admissionOperationCutoff ?? DefaultAdmissionOperationCutoff;
        TimeSpan completion = completionOperationCutoff ?? DefaultCompletionOperationCutoff;
        TimeSpan health = healthObservationDeadline ?? DefaultHealthObservationDeadline;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(admission, TimeSpan.Zero, nameof(admissionOperationCutoff));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(completion, TimeSpan.Zero, nameof(completionOperationCutoff));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(health, TimeSpan.Zero, nameof(healthObservationDeadline));
        _budgets = new(admission, completion, health);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }
    /// <summary>Routes one invocation without Registry, Catalog, package, or full inventory access.</summary>
    public async ValueTask<ManagedLauncherEntryResult> RunAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long started = _timeProvider.GetTimestamp();
        using var deadline = new CancellationTokenSource(_budgets.AdmissionOperationCutoff, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token);
        HandoffObservation? observation = null;
        try
        {
            HealthObservationResult health = await ObserveHealthWithinDeadlineAsync(
                deadline, started, cancellationToken).ConfigureAwait(false);
            if (health.TerminalResult is { } terminal)
            {
                return terminal;
            }
            LocalHealthObservation localHealth = health.Observation!;
            if (localHealth.Outcome is { } localOutcome)
            {
                return Result(localOutcome, localHealth.ManagedRoot, started);
            }
            string rootIdentity = localHealth.ManagedRoot!;
            observation = new(rootIdentity, null);

            ImmutableBootstrapStartResult handoff = await _handoff.StartAsync(
                rootIdentity,
                localHealth.Bootstrap!,
                linked.Token).ConfigureAwait(false);
            using IImmutableBootstrapLaunch? launch = handoff.Launch;
            observation = new(rootIdentity, launch);
            if (!handoff.HasValidShape)
            {
                return Result(
                    ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                    rootIdentity,
                    started);
            }
            if (!handoff.IsStarted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (deadline.IsCancellationRequested)
                {
                    linked.Token.ThrowIfCancellationRequested();
                }
                return Result(
                    handoff.Issue switch
                    {
                        ImmutableBootstrapStartIssue.Busy => ManagedLauncherEntryOutcome.Busy,
                        ImmutableBootstrapStartIssue.Damaged =>
                            ManagedLauncherEntryOutcome.RecoveryRequired,
                        ImmutableBootstrapStartIssue.StartFailed =>
                            ManagedLauncherEntryOutcome.LaunchFailed,
                        ImmutableBootstrapStartIssue.Unavailable =>
                            ManagedLauncherEntryOutcome.HealthUnavailable,
                        ImmutableBootstrapStartIssue.None => throw new InvalidOperationException(
                            "A successful Bootstrap start did not return its launch receipt."),
                        _ => throw new InvalidOperationException(
                            "Bootstrap start returned an invalid result."),
                    },
                    rootIdentity,
                    started);
            }
            IImmutableBootstrapLaunch admittedLaunch = launch!;

            var context = new EntryContext(rootIdentity, started, admittedLaunch, deadline, linked);
            return await CompleteInstalledAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Result(
                observation?.Launch is not null
                    ? ManagedLauncherEntryOutcome.TerminationUnconfirmed
                    : ManagedLauncherEntryOutcome.HealthUnavailable,
                observation?.ManagedRoot,
                started);
        }
    }

    private async ValueTask<HealthObservationResult> ObserveHealthWithinDeadlineAsync(
        CancellationTokenSource admissionDeadline,
        long started,
        CancellationToken cancellationToken)
    {
        using (var healthDeadline = new CancellationTokenSource(
            _budgets.HealthObservationDeadline,
            _timeProvider))
        using (var healthLinked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            admissionDeadline.Token,
            healthDeadline.Token))
        {
            try
            {
                LocalHealthObservation observed = await AwaitIsolatedReadOnlyObservationAsync(
                    ObserveLocalHealthAsync, healthLinked.Token).ConfigureAwait(false);
                return new(observed, TerminalResult: null);
            }
            catch (OperationCanceledException) when (
                healthDeadline.IsCancellationRequested &&
                !admissionDeadline.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                // Capture elapsed time before retiring the health timer, as at the source boundary.
                return new(Observation: null, Result(
                    ManagedLauncherEntryOutcome.HealthUnavailable, managedRoot: null, started));
            }
        }

    }

    private async ValueTask<ManagedLauncherEntryResult> CompleteInstalledAsync(
        EntryContext context,
        CancellationToken cancellationToken)
    {
        if (_timeProvider.GetElapsedTime(context.Started) >= _budgets.AdmissionOperationCutoff)
        {
            CancelDeadline(context.Deadline);
        }
        ImmutableBootstrapAdmissionResult admission = await context.Launch
            .WaitForAdmissionAsync(
                Budget(
                    _budgets.AdmissionOperationCutoff,
                    _timeProvider.GetElapsedTime(context.Started)),
                context.Linked.Token).ConfigureAwait(false);
        if (!admission.HasValidShape)
        {
            return Result(
                ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                context.ManagedRoot,
                context.Started);
        }
        if (admission.Outcome == ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed)
        {
            return Result(
                ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                context.ManagedRoot,
                context.Started);
        }
        if (admission.Outcome == ImmutableBootstrapAdmissionOutcome.Admitted &&
            (cancellationToken.IsCancellationRequested ||
                _timeProvider.GetElapsedTime(context.Started) >= _budgets.AdmissionOperationCutoff))
        {
            return await ObserveCleanupAsync(context, cancellationToken).ConfigureAwait(false);
        }
        if (admission.Outcome != ImmutableBootstrapAdmissionOutcome.Admitted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Result(
                admission.Outcome switch
                {
                    ImmutableBootstrapAdmissionOutcome.Busy => ManagedLauncherEntryOutcome.Busy,
                    ImmutableBootstrapAdmissionOutcome.RecoveryRequired =>
                        ManagedLauncherEntryOutcome.RecoveryRequired,
                    ImmutableBootstrapAdmissionOutcome.LaunchFailed =>
                        ManagedLauncherEntryOutcome.LaunchFailed,
                    ImmutableBootstrapAdmissionOutcome.HealthUnavailable =>
                        ManagedLauncherEntryOutcome.HealthUnavailable,
                    ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed =>
                        ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                    ImmutableBootstrapAdmissionOutcome.Admitted => throw new InvalidOperationException(
                        "An admitted Bootstrap was handled as an admission failure."),
                    _ => throw new InvalidOperationException(
                        "Bootstrap admission returned an undefined outcome."),
                },
                context.ManagedRoot,
                context.Started);
        }

        return await ObserveReadyAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ManagedLauncherEntryResult> ObserveCleanupAsync(
        EntryContext context,
        CancellationToken cancellationToken)
    {
        CancelDeadline(context.Deadline);
        TimeSpan cleanupRemaining = SetupWaitBudget.Remaining(
            _budgets.AdmissionOperationCutoff + DefaultCleanupObservationBudget,
            _timeProvider.GetElapsedTime(context.Started));
        if (cleanupRemaining == TimeSpan.Zero)
        {
            return Result(
                ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                context.ManagedRoot,
                context.Started);
        }
        using var cleanupDeadline = new CancellationTokenSource(
            cleanupRemaining,
            _timeProvider);
        ImmutableBootstrapCompletionResult cleanup;
        try
        {
            cleanup = await context.Launch
                .WaitForCompletionAsync(
                    new ImmutableBootstrapWaitBudget(TimeSpan.Zero, cleanupRemaining),
                    cleanupDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cleanupDeadline.IsCancellationRequested)
        {
            return Result(
                ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                context.ManagedRoot,
                context.Started);
        }
        if (!cleanup.HasValidShape)
        {
            return Result(
                ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                context.ManagedRoot,
                context.Started);
        }
        if (cleanup.Outcome == ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed)
        {
            return Result(
                ManagedLauncherEntryOutcome.TerminationUnconfirmed,
                context.ManagedRoot,
                context.Started);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Result(
            ManagedLauncherEntryOutcome.HealthUnavailable,
            context.ManagedRoot,
            context.Started);
    }

    private async ValueTask<ManagedLauncherEntryResult> ObserveReadyAsync(
        EntryContext context,
        CancellationToken cancellationToken)
    {
        TimeSpan admissionElapsed = _timeProvider.GetElapsedTime(context.Started);
        long completionStarted = _timeProvider.GetTimestamp();
        using var completionDeadline = new CancellationTokenSource(
            _budgets.CompletionOperationCutoff,
            _timeProvider);
        using var completionLinked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            completionDeadline.Token);
        ImmutableBootstrapCompletionResult completion;
        try
        {
            completion = await context.Launch
                .WaitForCompletionAsync(
                    new ImmutableBootstrapWaitBudget(
                        _budgets.CompletionOperationCutoff,
                        _budgets.CompletionOperationCutoff + DefaultCleanupObservationBudget),
                    completionLinked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            completionDeadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return CompletionResult(ManagedLauncherEntryOutcome.TerminationUnconfirmed, context, admissionElapsed);
        }
        if (!completion.HasValidShape)
        {
            return CompletionResult(ManagedLauncherEntryOutcome.TerminationUnconfirmed, context, admissionElapsed);
        }
        if (completion.Outcome == ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed)
        {
            return CompletionResult(ManagedLauncherEntryOutcome.TerminationUnconfirmed, context, admissionElapsed);
        }
        if (_timeProvider.GetElapsedTime(completionStarted) >= _budgets.CompletionOperationCutoff)
        {
            return CompletionResult(ManagedLauncherEntryOutcome.TerminationUnconfirmed, context, admissionElapsed);
        }
        if (completion.Outcome is
            ImmutableBootstrapCompletionOutcome.Ready or
            ImmutableBootstrapCompletionOutcome.RolledBack)
        {
            return CompletionResult(ManagedLauncherEntryOutcome.LaunchInstalled, context, admissionElapsed);
        }
        cancellationToken.ThrowIfCancellationRequested();
        ManagedLauncherEntryOutcome outcome = completion.Outcome switch
        {
            ImmutableBootstrapCompletionOutcome.Ready or
            ImmutableBootstrapCompletionOutcome.RolledBack =>
                ManagedLauncherEntryOutcome.LaunchInstalled,
            ImmutableBootstrapCompletionOutcome.Failed =>
                ManagedLauncherEntryOutcome.LaunchFailed,
            ImmutableBootstrapCompletionOutcome.Unavailable =>
                ManagedLauncherEntryOutcome.HealthUnavailable,
            ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed =>
                ManagedLauncherEntryOutcome.TerminationUnconfirmed,
            _ => throw new InvalidOperationException(
                "Bootstrap completion returned an undefined outcome."),
        };
        return CompletionResult(outcome, context, admissionElapsed);
    }

    private ManagedLauncherEntryResult CompletionResult(
        ManagedLauncherEntryOutcome outcome,
        EntryContext context,
        TimeSpan admissionElapsed) =>
        new(outcome, context.ManagedRoot, admissionElapsed, _timeProvider.GetElapsedTime(context.Started));

    private sealed record EntryContext(
        string ManagedRoot,
        long Started,
        IImmutableBootstrapLaunch Launch,
        CancellationTokenSource Deadline,
        CancellationTokenSource Linked);

    private sealed record HealthObservationResult(
        LocalHealthObservation? Observation,
        ManagedLauncherEntryResult? TerminalResult);

    private sealed record HandoffObservation(string ManagedRoot, IImmutableBootstrapLaunch? Launch);

    private async ValueTask<LocalHealthObservation> ObserveLocalHealthAsync(
        CancellationToken cancellationToken)
    {
        ManagedDistributionPayloadEntryAdmissionResult payload = await _payloadSource
            .AdmitEntryAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!payload.IsSuccess)
        {
            return new(
                payload.Issue switch
                {
                    ManagedDistributionPayloadIssue.Unavailable =>
                        ManagedLauncherEntryOutcome.PayloadUnavailable,
                    ManagedDistributionPayloadIssue.Invalid or ManagedDistributionPayloadIssue.Changed =>
                        ManagedLauncherEntryOutcome.PayloadInvalid,
                    ManagedDistributionPayloadIssue.None => throw new InvalidOperationException(
                        "Successful payload admission returned no Bootstrap identity."),
                    _ => throw new InvalidOperationException(
                        "Payload admission returned an undefined issue."),
                },
                ManagedRoot: null,
                Bootstrap: null);
        }
        if (payload.LauncherVersion != _runningLauncherVersion)
        {
            return new(ManagedLauncherEntryOutcome.PayloadInvalid, ManagedRoot: null, Bootstrap: null);
        }
        ManagedImmutableBootstrapIdentity bootstrap = payload.Bootstrap!;

        VersionManagerStateLoadResult loaded = await _stateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (loaded.Issue == VersionManagerStateLoadIssue.Missing)
        {
            return await ObserveMissingRootAsync(bootstrap, cancellationToken).ConfigureAwait(false);
        }
        if (!loaded.IsSuccess)
        {
            return new(
                loaded.Issue is VersionManagerStateLoadIssue.Invalid or
                    VersionManagerStateLoadIssue.ManagedRootMismatch
                    ? ManagedLauncherEntryOutcome.RecoveryRequired
                    : ManagedLauncherEntryOutcome.HealthUnavailable,
                ManagedRoot: null,
                Bootstrap: bootstrap);
        }

        VersionManagerState state = loaded.State!;
        if (state.ManagedRootIdentity is null)
        {
            return new(
                ManagedLauncherEntryOutcome.RecoveryRequired,
                ManagedRoot: null,
                Bootstrap: bootstrap);
        }
        string rootIdentity = ManagedRootPathIdentity.Normalize(state.ManagedRootIdentity);
        ManagedInstallationRootObservation installedRoot = await _rootProbe.ObserveAsync(
            rootIdentity,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return installedRoot.Status == ManagedInstallationRootStatus.Present
            ? new(Outcome: null, rootIdentity, bootstrap)
            : new(
                installedRoot.Status is ManagedInstallationRootStatus.Absent or
                    ManagedInstallationRootStatus.Residue or
                    ManagedInstallationRootStatus.InvalidDestination
                    ? ManagedLauncherEntryOutcome.RecoveryRequired
                    : ManagedLauncherEntryOutcome.HealthUnavailable,
                rootIdentity,
                bootstrap);
    }

    private async ValueTask<LocalHealthObservation> ObserveMissingRootAsync(
        ManagedImmutableBootstrapIdentity bootstrap,
        CancellationToken cancellationToken)
    {
        ManagedInstallationRootObservation root = await _rootProbe.ObserveAsync(
            _defaultManagedRoot,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(
            root.Status switch
            {
                ManagedInstallationRootStatus.Absent =>
                    ManagedLauncherEntryOutcome.SetupRequired,
                ManagedInstallationRootStatus.Present or
                ManagedInstallationRootStatus.Residue or
                ManagedInstallationRootStatus.InvalidDestination =>
                    ManagedLauncherEntryOutcome.RecoveryRequired,
                ManagedInstallationRootStatus.PermissionDenied or
                ManagedInstallationRootStatus.Unavailable =>
                    ManagedLauncherEntryOutcome.HealthUnavailable,
                _ => throw new InvalidOperationException(
                    "Root probe returned an undefined status."),
            },
            _defaultManagedRoot,
            bootstrap);
    }

    private static async ValueTask<T> AwaitIsolatedReadOnlyObservationAsync<T>(
        Func<CancellationToken, ValueTask<T>> observe,
        CancellationToken cancellationToken)
    {
        Func<Task<T>> run = () => observe(cancellationToken).AsTask();
        Task<T> pending = Task.Run(run, CancellationToken.None);
        try
        {
            return await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = ObserveAbandonedReadOnlyTaskAsync(pending);
            throw;
        }
    }

    private static async Task ObserveAbandonedReadOnlyTaskAsync(Task pending)
    {
        try
        {
            await pending.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (SetupObserverExceptions.IsObservedAndIgnored(exception))
        {
            // The caller has already returned a typed timeout/cancellation result.
        }
    }

    private sealed record LocalHealthObservation(
        ManagedLauncherEntryOutcome? Outcome,
        string? ManagedRoot,
        ManagedImmutableBootstrapIdentity? Bootstrap);

    private ManagedLauncherEntryResult Result(
        ManagedLauncherEntryOutcome outcome,
        string? managedRoot,
        long started)
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(started);
        return new(outcome, managedRoot, elapsed, elapsed);
    }

    private static void CancelDeadline(CancellationTokenSource deadline)
    {
        // Complete cancellation callbacks before inspecting or granting cleanup authority.
        deadline.Cancel();
    }

    private static ImmutableBootstrapWaitBudget Budget(TimeSpan operation, TimeSpan elapsed) =>
        SetupWaitBudget.FromElapsed(operation, elapsed, DefaultCleanupObservationBudget);
}
