// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;


namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Complete durable Application-state categories admitted by diagnosis.</summary>
public enum RecoveryStateCase
{
    /// <summary>No state exists.</summary>
    Missing,
    /// <summary>The canonical first-run state is bound to the requested root.</summary>
    Exact,
    /// <summary>State bytes or shape are invalid.</summary>
    Invalid,
    /// <summary>The adapter reported a managed-root mismatch.</summary>
    ManagedRootMismatch,
    /// <summary>A validated state remains unbound.</summary>
    Unbound,
    /// <summary>A validated state is bound to another root.</summary>
    WrongBound,
    /// <summary>State could not be observed completely.</summary>
    Unavailable,
}

/// <summary>Ordered read-only read-only observation boundaries.</summary>
public enum RecoveryObservationBoundary
{
    /// <summary>Bootstrap lifetime observation.</summary>
    BootstrapLifetime,
    /// <summary>Application lifetime observation.</summary>
    ApplicationLifetime,
    /// <summary>Launcher lifetime observation.</summary>
    LauncherLifetime,
    /// <summary>Application state observation.</summary>
    State,
    /// <summary>Managed root observation.</summary>
    Root,
    /// <summary>Setup marker observation.</summary>
    Marker,
    /// <summary>Candidate, Launcher, inventory, and restart-prefix observation.</summary>
    Evidence,
}

/// <summary>Launcher-state shapes used by the closed recovery table tests.</summary>
public enum RecoveryLauncherCase
{
    /// <summary>No Launcher state exists.</summary>
    Missing,
    /// <summary>The first candidate is requested.</summary>
    Requested,
    /// <summary>The first candidate launch is durably recorded.</summary>
    CandidateLaunchRecorded,
    /// <summary>The first candidate failed before READY.</summary>
    Failed,
    /// <summary>The first candidate is committed Active and LKG.</summary>
    Ready,
    /// <summary>A different candidate is pending.</summary>
    RequestedDifferentCandidate,
    /// <summary>An ordinary update retains an existing Active and LKG.</summary>
    OrdinaryUpdate,
    /// <summary>READY retains a foreign failure.</summary>
    ReadyWithFailure,
}

internal static class RecoveryTestData
{
    internal const string ManagedRoot = @"C:\managed\FixtureProduct";
    internal const string StatePath = @"C:\state\version-manager-state.json";
    private static readonly ManagedAppVersion _version = ManagedAppVersion.Parse("1.0.6");
    internal static ManagedVersionAdmission Admission { get; } =
        new(_version, "entry", new string('1', 64));
    internal static ManagedLauncherIdentity Launcher { get; } = ManagedLauncherIdentity.Create(
        ContractFixture.Descriptor, ManagedImmutableBootstrapIdentity.MaximumExecutableBytes, _version,
        Admission.AdmissionIdentity,
        Admission.ReleaseManifestSha256,
        _version,
        ManagedLauncherIdentity.SupportedProtocolVersion,
        ContractFixture.LauncherPath,
        123,
        new string('a', 64));
    internal static ManagedLauncherIdentity OtherLauncher { get; } = ManagedLauncherIdentity.Create(
        ContractFixture.Descriptor, ManagedImmutableBootstrapIdentity.MaximumExecutableBytes, _version,
        Admission.AdmissionIdentity,
        Admission.ReleaseManifestSha256,
        _version,
        ManagedLauncherIdentity.SupportedProtocolVersion,
        ContractFixture.LauncherPath,
        124,
        new string('b', 64));

    internal static VersionManagerStateLoadResult MissingAppState { get; } =
        new(null, VersionManagerStateLoadIssue.Missing);

    internal static VersionManagerStateLoadResult CanonicalAppState { get; } = new(
        ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(Admission).BindToManagedRoot(ManagedRoot),
        VersionManagerStateLoadIssue.None);

    internal static VersionManagerStateLoadResult NonCanonicalBoundAppState { get; } = new(
        VersionManagerState.Create(
            updateSource: null,
            activeVersion: null,
            lastKnownGoodVersion: null,
            admissions: [],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: ManagedRoot),
        VersionManagerStateLoadIssue.None);

    internal static ManagedSetupRecoveryTransaction Transaction(ManagedSetupRecoveryPhase phase)
    {
        return new(
            "0123456789abcdef0123456789abcdef",
            ManagedRoot,
            StatePath,
            phase,
            ["FixtureProduct", "FixtureProduct.managed-setup-transaction.v1.json",
                ".managed-setup-staging/0123456789abcdef0123456789abcdef"],
            new(123, new string('a', 64), 456, new string('b', 64),
                "FixtureProduct.Bootstrap.exe", 789, new string('c', 64)),
            new(4, new string('d', 64), 1, "1.0.6", new string('e', 64),
                @"G:\AUTO\catalog.json", "registry", @"G:\AUTO", "latest", "1.0.6",
                "packages/app.zip", 1024, new string('f', 64),
                Admission.ReleaseManifestSha256, Admission.AdmissionIdentity));
    }

    internal static ManagedSetupRecoveryEvidenceObservation Evidence(
        RecoveryLauncherCase launcherCase,
        string token = "evidence-1",
        bool includeInstalledLauncher = true)
    {
        LauncherBootstrapStateLoadResult state = launcherCase switch
        {
            RecoveryLauncherCase.Missing => new(null, LauncherBootstrapStateLoadIssue.Missing),
            RecoveryLauncherCase.Requested => Exact(
                LauncherBootstrapState.Create(ManagedRoot, null, null,
                    PendingLauncherActivation.Create(Launcher, null, null,
                        LauncherActivationPhase.Requested), null)),
            RecoveryLauncherCase.CandidateLaunchRecorded => Exact(
                LauncherBootstrapState.Create(ManagedRoot, null, null,
                    PendingLauncherActivation.Create(Launcher, null, null,
                        LauncherActivationPhase.CandidateLaunchRecorded), null)),
            RecoveryLauncherCase.Failed => Exact(
                LauncherBootstrapState.Create(ManagedRoot, null, null, null, Launcher)),
            RecoveryLauncherCase.Ready => Exact(
                LauncherBootstrapState.Create(ManagedRoot, Launcher, Launcher, null, null)),
            RecoveryLauncherCase.RequestedDifferentCandidate => Exact(
                LauncherBootstrapState.Create(ManagedRoot, null, null,
                    PendingLauncherActivation.Create(OtherLauncher, null, null,
                        LauncherActivationPhase.Requested), null)),
            RecoveryLauncherCase.OrdinaryUpdate => Exact(
                LauncherBootstrapState.Create(ManagedRoot, Launcher, Launcher,
                    PendingLauncherActivation.Create(OtherLauncher, Launcher, Launcher,
                        LauncherActivationPhase.Requested), null)),
            RecoveryLauncherCase.ReadyWithFailure => Exact(
                LauncherBootstrapState.Create(ManagedRoot, Launcher, Launcher, null,
                    OtherLauncher)),
            _ => throw new ArgumentOutOfRangeException(nameof(launcherCase)),
        };
        return new(
            Admission,
            includeInstalledLauncher ? Launcher : null,
            state,
            new TestRecoveryExecutionToken(token));

        static LauncherBootstrapStateLoadResult Exact(LauncherBootstrapState value)
        {
            return new(value, LauncherBootstrapStateLoadIssue.None);
        }
    }
}

internal sealed record TestRecoveryExecutionToken(string Value)
    : ManagedSetupRecoveryExecutionToken;

internal sealed class RecoveryHarness
{
    internal RecoveryHarness(
        RecordingRecoveryStateStore state,
        RecordingRecoveryRootProbe root,
        RecordingRecoveryMarkerProbe marker,
        RecordingRecoveryLifetimeProbe lifetime,
        RecordingRecoveryEvidenceProbe evidence)
    {
        State = state;
        Root = root;
        Marker = marker;
        Lifetime = lifetime;
        Evidence = evidence;
        Experience = new(state, root, marker, lifetime, evidence);
    }

    internal ManagedInstallationRecoveryExperience Experience { get; }
    internal RecordingRecoveryStateStore State { get; }
    internal RecordingRecoveryRootProbe Root { get; }
    internal RecordingRecoveryMarkerProbe Marker { get; }
    internal RecordingRecoveryLifetimeProbe Lifetime { get; }
    internal RecordingRecoveryEvidenceProbe Evidence { get; }

    internal static RecoveryHarness Create(
        VersionManagerStateLoadResult appState,
        ManagedSetupRecoveryFact marker,
        ManagedSetupRecoveryEvidenceObservation evidence,
        ManagedInstallationRootStatus root = ManagedInstallationRootStatus.Residue)
    {
        return new(
            new RecordingRecoveryStateStore(appState),
            new RecordingRecoveryRootProbe(root),
            new RecordingRecoveryMarkerProbe(marker),
            new RecordingRecoveryLifetimeProbe(),
            new RecordingRecoveryEvidenceProbe(evidence));
    }
}

internal sealed class RecordingRecoveryStateStore : IManagedSetupRecoveryStateStore
{
    private readonly Action? _beforeReturn;
    private readonly Queue<VersionManagerStateLoadResult> _results;

    internal RecordingRecoveryStateStore(params VersionManagerStateLoadResult[] results)
        : this(results, beforeReturn: null)
    {
    }

    internal RecordingRecoveryStateStore(
        VersionManagerStateLoadResult[] results,
        Action? beforeReturn = null,
        string statePathIdentity = RecoveryTestData.StatePath)
    {
        _results = new(results);
        _beforeReturn = beforeReturn;
        StatePathIdentity = Path.GetFullPath(statePathIdentity);
    }

    internal int LoadCount { get; private set; }
    internal int LeaseCount { get; private set; }
    internal VersionManagerWriteLeaseIssue LeaseIssue { get; set; }
    internal bool LeaseHeld { get; private set; }
    internal VersionManagerWriteLeaseResult? LastLease { get; private set; }
    public string StatePathIdentity { get; }

    public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadCount++;
        _beforeReturn?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        VersionManagerStateLoadResult result = _results.Count > 1 ? _results.Dequeue() : _results.Peek();
        return ValueTask.FromResult(result);
    }

    public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        LeaseCount++;
        throw new InvalidOperationException("Diagnosis must never acquire a writer.");
    }

    public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}

internal sealed class RecordingRecoveryRootProbe : IManagedInstallationRootProbe
{
    private readonly Action? _beforeReturn;

    internal RecordingRecoveryRootProbe(
        ManagedInstallationRootStatus status,
        Action? beforeReturn = null)
    {
        Status = status;
        _beforeReturn = beforeReturn;
    }

    internal int CallCount { get; private set; }
    internal ManagedInstallationRootStatus Status { get; set; }

    public ValueTask<ManagedInstallationRootObservation> ObserveAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        _beforeReturn?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ManagedInstallationRootObservation(Status));
    }
}

internal sealed class RecordingRecoveryMarkerProbe : IManagedSetupRecoveryProbe
{
    private readonly Action? _beforeReturn;
    private readonly Queue<ManagedSetupRecoveryFact> _facts;

    internal RecordingRecoveryMarkerProbe(params ManagedSetupRecoveryFact[] facts)
        : this(facts, beforeReturn: null)
    {
    }

    internal RecordingRecoveryMarkerProbe(
        ManagedSetupRecoveryFact[] facts,
        Action? beforeReturn)
    {
        _facts = new(facts);
        _beforeReturn = beforeReturn;
    }

    internal int CallCount { get; private set; }
    internal List<string> StatePaths { get; } = [];

    public ValueTask<ManagedSetupRecoveryFact> ObserveAsync(
        string managedRoot,
        string statePathIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        StatePaths.Add(statePathIdentity);
        _beforeReturn?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        ManagedSetupRecoveryFact result = _facts.Count > 1 ? _facts.Dequeue() : _facts.Peek();
        return ValueTask.FromResult(result);
    }
}

internal sealed class RecordingRecoveryLifetimeProbe : IManagedProcessLifetimeProbe
{
    private readonly Action<ManagedProcessLifetimeKind>? _beforeReturn;

    internal RecordingRecoveryLifetimeProbe(
        Action<ManagedProcessLifetimeKind>? beforeReturn = null)
    {
        _beforeReturn = beforeReturn;
    }

    internal Dictionary<ManagedProcessLifetimeKind, ManagedProcessLifetimeStatus> Statuses { get; } =
        Enum.GetValues<ManagedProcessLifetimeKind>().ToDictionary(
            static kind => kind,
            static _ => ManagedProcessLifetimeStatus.Exited);
    internal List<ManagedProcessLifetimeKind> Calls { get; } = [];
    internal List<string> StatePaths { get; } = [];

    public ValueTask<ManagedProcessLifetimeStatus> ObserveAsync(
        string statePath,
        ManagedProcessLifetimeKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(kind);
        StatePaths.Add(statePath);
        _beforeReturn?.Invoke(kind);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Statuses[kind]);
    }
}

internal sealed class RecordingRecoveryEvidenceProbe : IManagedSetupRecoveryEvidenceProbe
{
    private readonly Action? _beforeReturn;
    private readonly Queue<ManagedSetupRecoveryEvidenceObservation> _results;

    internal RecordingRecoveryEvidenceProbe(
        params ManagedSetupRecoveryEvidenceObservation[] results)
        : this(results, beforeReturn: null)
    {
    }

    internal RecordingRecoveryEvidenceProbe(
        ManagedSetupRecoveryEvidenceObservation[] results,
        Action? beforeReturn)
    {
        _results = new(results);
        _beforeReturn = beforeReturn;
    }

    internal int CallCount { get; private set; }

    public ValueTask<ManagedSetupRecoveryEvidenceObservation> ObserveAsync(
        ManagedSetupRecoveryTransaction transaction,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        _beforeReturn?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        ManagedSetupRecoveryEvidenceObservation result =
            _results.Count > 1 ? _results.Dequeue() : _results.Peek();
        return ValueTask.FromResult(result);
    }
}

