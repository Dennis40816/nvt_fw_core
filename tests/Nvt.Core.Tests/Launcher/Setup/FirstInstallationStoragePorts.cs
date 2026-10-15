// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

using static Nvt.Core.Tests.Launcher.Setup.FirstInstallationTestSupport;

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class SequencedStateStore(params VersionManagerStateLoadResult[] loads)
    : IVersionManagerStateStore, IDisposable
{
    private int _loadIndex;
    // Guard: the scenario driver serializes acquisitions; the coordinator releases each before the next.
    private VersionManagerWriteLeaseResult? _ownedWriteLease;

    public void Dispose() => _ownedWriteLease?.Dispose();
    internal string StatePathIdentity { get; set; } = string.Empty;
    internal string? FirstLeasePathOverride { get; set; }
    internal string? SecondLeasePathOverride { get; set; }
    internal Action<string>? Observe { get; set; }
    internal bool LeaseActive { get; private set; }
    internal bool ContractViolated { get; private set; }
    internal VersionManagerWriteLeaseIssue LeaseIssue { get; set; }
    internal VersionManagerWriteLeaseIssue SecondLeaseIssue { get; set; }
    internal int LeaseCount { get; private set; }
    internal int LeaseAttemptCount { get; private set; }
    internal Action? SecondLeaseAction { get; set; }
    internal List<TimeSpan> LeaseTimeouts { get; } = [];
    internal int LoadCount { get; private set; }
    internal int SaveCount { get; private set; }
    internal List<string> Events { get; } = [];

    internal void RecordContractViolation(bool violated) => ContractViolated |= violated;

    public async ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LeaseAttemptCount++;
        LeaseTimeouts.Add(waitTimeout);
        if (LeaseAttemptCount == 2)
        {
            SecondLeaseAction?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }
        VersionManagerWriteLeaseIssue issue = LeaseAttemptCount == 2 &&
            SecondLeaseIssue != VersionManagerWriteLeaseIssue.None
                ? SecondLeaseIssue
                : LeaseIssue;
        if (issue != VersionManagerWriteLeaseIssue.None)
        {
            _ownedWriteLease = new VersionManagerWriteLeaseResult(issue, null);
            return _ownedWriteLease;
        }
        ContractViolated |= LeaseActive;
        string path = (LeaseAttemptCount == 2 ? SecondLeasePathOverride : FirstLeasePathOverride) ??
            StatePathIdentity;
        VersionManagerWriteLeaseResult acquired = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            path, waitTimeout, cancellationToken);
        if (!acquired.HoldsStatePath(path))
        {
            _ownedWriteLease = acquired;
            return acquired;
        }
        LeaseActive = true;
        LeaseCount++;
        int leaseNumber = LeaseCount;
        Events.Add($"writer-acquire:{leaseNumber}");
        ObservedWriterCustody? custody = new(acquired, () =>
        {
            LeaseActive = false;
            Events.Add($"writer-release:{leaseNumber}");
            Observe?.Invoke($"writer-release:{leaseNumber}");
        });
        try
        {
            _ownedWriteLease = new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.None, custody);
            custody = null; // The result owns both exact custody and the observation callback.
            Observe?.Invoke($"writer-acquire:{leaseNumber}");
            return _ownedWriteLease;
        }
        finally
        {
            custody?.Dispose();
        }
    }

    public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observe?.Invoke("state-load");
        LoadCount++;
        int index = Math.Min(_loadIndex++, loads.Length - 1);
        return ValueTask.FromResult(loads[index]);
    }

    public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken)
    {
        SaveCount++;
        throw new InvalidOperationException("Setup must not write per-user version state.");
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class ObservedWriterCustody(VersionManagerWriteLeaseResult inner, Action release)
    : IVersionManagerWriteLeaseCustody
{
    public bool HoldsStatePath(string statePath) => inner.HoldsStatePath(statePath);

    public void Dispose()
    {
        inner.Dispose();
        release();
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class SequencedRootProbe(params ManagedInstallationRootStatus[] statuses)
    : IManagedInstallationRootProbe
{
    private int _index;
    internal Action<string>? Observe { get; set; }

    internal int ObserveCount { get; private set; }

    public ValueTask<ManagedInstallationRootObservation> ObserveAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observe?.Invoke("root-observe");
        ObserveCount++;
        int index = Math.Min(_index++, statuses.Length - 1);
        return ValueTask.FromResult(new ManagedInstallationRootObservation(statuses[index]));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class RecordingPayloadSource(ManagedDistributionPayloadIdentity identity)
    : IManagedDistributionPayloadSource, IDisposable
{
    internal Action<string>? Observe { get; set; }
    internal ManagedDistributionPayloadIssue CaptureIssue { get; set; }
    internal int CaptureCount { get; private set; }
    internal ManagedDistributionPayloadIssue InspectIssue { get; init; }
    internal ManagedDistributionPayloadInspectionResult? InspectionOverride { get; set; }
    internal int InspectCount { get; private set; }
    internal PayloadCapture? LastCapture { get; private set; }

    public void Dispose()
    {
        if (LastCapture is { Disposed: false } capture)
        {
            capture.Dispose();
        }
    }
    internal bool ReturnCaptureOnFailure { get; set; }
    internal bool ReturnMismatchedCapture { get; set; }
    internal bool ReturnNullCaptureOnSuccess { get; set; }

    public ValueTask<ManagedDistributionPayloadEntryAdmissionResult> AdmitEntryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(InspectIssue == ManagedDistributionPayloadIssue.None
            ? new ManagedDistributionPayloadEntryAdmissionResult(
                identity.LauncherVersion,
                identity.Bootstrap,
                ManagedDistributionPayloadIssue.None)
            : new ManagedDistributionPayloadEntryAdmissionResult(default, null, InspectIssue));
    }

    public ValueTask<ManagedDistributionPayloadInspectionResult> InspectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observe?.Invoke("payload-inspect");
        InspectCount++;
        if (InspectionOverride is { } inspected)
        {
            return ValueTask.FromResult(inspected);
        }
        return ValueTask.FromResult(InspectIssue == ManagedDistributionPayloadIssue.None
            ? new ManagedDistributionPayloadInspectionResult(
                identity,
                ManagedDistributionPayloadIssue.None)
            : new ManagedDistributionPayloadInspectionResult(null, InspectIssue));
    }

    public ValueTask<ManagedDistributionPayloadCaptureResult> CaptureExactAsync(
        ManagedDistributionPayloadIdentity expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observe?.Invoke("payload-capture");
        CaptureCount++;
        Assert.Equal(identity, expected);
        ManagedDistributionPayloadIdentity capturedIdentity = ReturnMismatchedCapture
            ? new(
                65_536,
                identity.LauncherVersion,
                new string('d', 40),
                identity.LauncherSize,
                identity.LauncherSha256,
                identity.DescriptorSize,
                identity.DescriptorSha256,
                identity.Bootstrap)
            : identity;
        LastCapture = !ReturnNullCaptureOnSuccess &&
            (CaptureIssue == ManagedDistributionPayloadIssue.None || ReturnCaptureOnFailure)
                ? new PayloadCapture(capturedIdentity, Observe)
                : null;
        return ValueTask.FromResult(new ManagedDistributionPayloadCaptureResult(
            LastCapture,
            CaptureIssue));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class PayloadCapture(ManagedDistributionPayloadIdentity identity, Action<string>? observe = null)
    : IManagedDistributionPayloadCapture
{
    internal bool Disposed { get; private set; }
    public ManagedDistributionPayloadIdentity Identity => identity;

    public void Dispose()
    {
        Disposed = true;
        observe?.Invoke("payload-dispose");
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class RecordingCandidateSource(FreshInstallationCandidate candidate)
    : IFreshInstallationCandidateSource
{
    internal Action<string>? Observe { get; set; }
    internal FreshInstallationCandidateResult? InspectionOverride { get; set; }
    internal FreshInstallationCandidateResult? ReverificationOverride { get; set; }
    internal FreshInstallationCandidateIssue InspectIssue { get; init; }
    internal FreshInstallationCandidateIssue ReverifyIssue { get; init; }
    internal int InspectCount { get; private set; }
    internal int ReverifyCount { get; private set; }

    public ValueTask<FreshInstallationCandidateResult> InspectFreshInstallationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observe?.Invoke("candidate-inspect");
        InspectCount++;
        if (InspectionOverride is { } inspected)
        {
            return ValueTask.FromResult(inspected);
        }
        return ValueTask.FromResult(InspectIssue == FreshInstallationCandidateIssue.None
            ? new FreshInstallationCandidateResult(
                candidate,
                FreshInstallationCandidateIssue.None)
            : new FreshInstallationCandidateResult(null, InspectIssue));
    }

    public ValueTask<FreshInstallationCandidateResult> ReverifyFreshInstallationAsync(
        FreshInstallationCandidate expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observe?.Invoke("candidate-reverify");
        ReverifyCount++;
        if (ReverificationOverride is { } reverified)
        {
            return ValueTask.FromResult(reverified);
        }
        Assert.Equal(candidate.Identity, expected.Identity);
        return ValueTask.FromResult(ReverifyIssue == FreshInstallationCandidateIssue.None
            ? new FreshInstallationCandidateResult(candidate, FreshInstallationCandidateIssue.None)
            : new FreshInstallationCandidateResult(null, ReverifyIssue));
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class RecordingRootMaterializer(SequencedStateStore state)
    : IManagedFirstInstallationRootMaterializer
{
    internal Action<string>? Observe { get; set; }
    internal ManagedFirstInstallationMaterializationResult? MaterializationOverride { get; set; }
    internal ManagedFirstInstallationMaterializationIssue AdmissionIssue { get; set; }
    internal int AdmissionCount { get; private set; }
    internal ManagedFirstInstallationMaterializationIssue Issue { get; init; }
    internal int MaterializeCount { get; private set; }
    internal IReadOnlyList<ManagedFirstInstallationProgress> ProgressUpdates { get; init; } = [];
    internal RecordingPromotedInstallation Transaction { get; } = new(state);
    internal bool ReturnInstallationOnFailure { get; set; }

    public ValueTask<ManagedFirstInstallationMaterializationIssue> AdmitDestinationAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observe?.Invoke("destination-admit");
        AdmissionCount++;
        return ValueTask.FromResult(AdmissionIssue);
    }

    public ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        string managedRoot,
        string statePathIdentity,
        IManagedDistributionPayloadCapture payload,
        FreshInstallationCandidate candidate,
        VersionManagerState seed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.True(state.LeaseActive);
        Assert.True(ManagedVersionSeedPolicy.IsCanonicalFirstRunSeed(seed));
        Assert.Equal(candidate.Package.Version, seed.ActiveVersion);
        Assert.Equal(payload.Identity, PayloadIdentity());
        Observe?.Invoke("materialize");
        MaterializeCount++;
        if (MaterializationOverride is { } materialized)
        {
            return ValueTask.FromResult(materialized);
        }
        if (Issue != ManagedFirstInstallationMaterializationIssue.None)
        {
            if (ReturnInstallationOnFailure)
            {
                Transaction.Initialize(managedRoot, Admission(candidate));
            }
            return ValueTask.FromResult(new ManagedFirstInstallationMaterializationResult(
                ReturnInstallationOnFailure ? Transaction : null,
                Issue));
        }
        Transaction.Initialize(managedRoot, Admission(candidate));
        return ValueTask.FromResult(new ManagedFirstInstallationMaterializationResult(
            Transaction,
            ManagedFirstInstallationMaterializationIssue.None));
    }

    public ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        string managedRoot,
        string statePathIdentity,
        IManagedDistributionPayloadCapture payload,
        FreshInstallationCandidate candidate,
        VersionManagerState seed,
        IProgress<ManagedFirstInstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        foreach (ManagedFirstInstallationProgress update in ProgressUpdates)
        {
            progress?.Report(update);
        }
        return MaterializeAsync(
            managedRoot,
            statePathIdentity,
            payload,
            candidate,
            seed,
            cancellationToken);
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class RecordingPromotedInstallation(SequencedStateStore state)
    : IManagedPromotedFirstInstallation
{
    internal bool ContractViolated { get; private set; }

    private sealed record Promotion(string ManagedRoot, ManagedVersionAdmission Admission);
    // Guard: the scenario driver initializes this complete observation before publishing the receipt.
    private Promotion? _promotion;
    public string ManagedRoot => _promotion?.ManagedRoot ?? string.Empty;
    public ManagedVersionAdmission Admission => _promotion?.Admission!;
    internal int CompleteCount { get; private set; }
    internal int BootstrapLeaseAcquireCount { get; private set; }
    internal Action? BootstrapLeaseAcquireAction { get; set; }
    internal Action? BootstrapLeaseAcquiredAction { get; set; }
    internal ManagedExecutableLaunchIssue BootstrapLeaseIssue { get; set; }
    internal ManagedExecutableLaunchLeaseResult? BootstrapLeaseResultOverride { get; set; }
    internal int DisposeCount { get; private set; }
    internal Action? DisposeAction { get; set; }
    internal RecordingExecutableLaunchLease? LastBootstrapLease { get; private set; }
    internal ManagedFirstInstallationTransactionIssue CompleteIssue { get; set; }
    internal Action? CompleteAction { get; set; }
    internal int RecordLaunchCount { get; private set; }
    internal Action? RecordLaunchAction { get; set; }
    internal ManagedFirstInstallationTransactionIssue RecordLaunchIssue { get; set; }

    public void Dispose()
    {
        DisposeCount++;
        DisposeAction?.Invoke();
    }

    internal void Initialize(string managedRoot, ManagedVersionAdmission admission)
    {
        _promotion = new(managedRoot, admission);
    }

    public ValueTask<ManagedFirstInstallationTransactionIssue> RecordBootstrapLaunchAsync(
        CancellationToken cancellationToken)
    {
        RecordLaunchCount++;
        state.Events.Add("record-bootstrap-launch");
        state.Observe?.Invoke("record-bootstrap-launch");
        RecordLaunchAction?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        ContractViolated |= !state.LeaseActive;
        return ValueTask.FromResult(RecordLaunchIssue);
    }

    public ValueTask<ManagedExecutableLaunchLeaseResult> AcquireBootstrapLaunchLeaseAsync(
        ManagedImmutableBootstrapIdentity expectedIdentity,
        CancellationToken cancellationToken)
    {
        ContractViolated |= !state.LeaseActive;
        ContractViolated |= Bootstrap != expectedIdentity;
        ContractViolated |= RecordLaunchCount != 1;
        BootstrapLeaseAcquireCount++;
        state.Events.Add("acquire-bootstrap-lease");
        state.Observe?.Invoke("acquire-bootstrap-lease");
        BootstrapLeaseAcquireAction?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (BootstrapLeaseResultOverride is not null)
        {
            LastBootstrapLease = BootstrapLeaseResultOverride.Lease as
                RecordingExecutableLaunchLease;
            return ValueTask.FromResult(BootstrapLeaseResultOverride);
        }
        if (BootstrapLeaseIssue != ManagedExecutableLaunchIssue.None)
        {
            return ValueTask.FromResult(new ManagedExecutableLaunchLeaseResult(
                null,
                BootstrapLeaseIssue));
        }
        LastBootstrapLease = new RecordingExecutableLaunchLease(
            Path.Combine(ManagedRoot, expectedIdentity.FileName),
            ManagedRoot);
        BootstrapLeaseAcquiredAction?.Invoke();
        return ValueTask.FromResult(new ManagedExecutableLaunchLeaseResult(
            LastBootstrapLease,
            ManagedExecutableLaunchIssue.None));
    }

    public ValueTask<ManagedFirstInstallationTransactionIssue> CompleteAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ContractViolated |= !state.LeaseActive;
        CompleteAction?.Invoke();
        CompleteCount++;
        state.Events.Add("complete");
        state.Observe?.Invoke("complete");
        return ValueTask.FromResult(CompleteIssue);
    }
}

// Guard: the scenario driver and awaited completion signals serialize mutable test observations.
internal sealed class RecordingExecutableLaunchLease(
    string executablePath,
    string workingDirectory) : IManagedExecutableLaunchLease
{
    internal bool Disposed { get; private set; }
    internal Action? DisposeAction { get; set; }
    public string ExecutablePath => executablePath;
    public string WorkingDirectory => workingDirectory;
    public bool TryValidateForStart()
    {
        return !Disposed;
    }

    public void Dispose()
    {
        Disposed = true;
        DisposeAction?.Invoke();
    }
}

