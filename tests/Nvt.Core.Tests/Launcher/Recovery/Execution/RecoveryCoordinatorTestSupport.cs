// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

internal sealed class ExecutionHarness
{
    internal ExecutionHarness(
        ExecutionStateStore state,
        RecordingRecoveryRootProbe root,
        RecordingRecoveryMarkerProbe marker,
        RecordingRecoveryLifetimeProbe lifetime,
        RecordingRecoveryEvidenceProbe evidence,
        RecordingRecoveryExecutionPort execution)
    {
        State = state;
        Root = root;
        Marker = marker;
        Lifetime = lifetime;
        Evidence = evidence;
        Execution = execution;
        Experience = new(state, root, marker, lifetime, evidence);
        Coordinator = new(state, root, marker, lifetime, evidence, execution);
    }

    internal ManagedInstallationRecoveryExperience Experience { get; }
    internal ManagedSetupRecoveryExecutionCoordinator Coordinator { get; }
    internal ExecutionStateStore State { get; }
    internal RecordingRecoveryRootProbe Root { get; }
    internal RecordingRecoveryMarkerProbe Marker { get; }
    internal RecordingRecoveryLifetimeProbe Lifetime { get; }
    internal RecordingRecoveryEvidenceProbe Evidence { get; }
    internal RecordingRecoveryExecutionPort Execution { get; }

    internal static ExecutionHarness Create(
        VersionManagerStateLoadResult appState,
        ManagedSetupRecoveryFact marker,
        ManagedSetupRecoveryEvidenceObservation evidence,
        ManagedInstallationRootStatus root = ManagedInstallationRootStatus.Residue)
    {
        return new(
            new ExecutionStateStore(appState),
            new RecordingRecoveryRootProbe(root),
            new RecordingRecoveryMarkerProbe(marker),
            new RecordingRecoveryLifetimeProbe(),
            new RecordingRecoveryEvidenceProbe(evidence),
            new RecordingRecoveryExecutionPort());
    }
}

internal sealed class ExecutionStateStore : IManagedSetupRecoveryStateStore
{
    private readonly Action? _beforeReturn;
    // All mutable fixture state belongs to this test's single asynchronous flow.
    private readonly Queue<VersionManagerStateLoadResult> _results;
    private LeaseObservation? _lastAcquisition;

    internal ExecutionStateStore(params VersionManagerStateLoadResult[] results)
        : this(results, beforeReturn: null)
    {
    }

    internal ExecutionStateStore(
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
    internal bool LeaseHeld => _lastAcquisition?.Scope?.IsActive == true;
    internal VersionManagerWriteLeaseResult? LastLease => _lastAcquisition?.Lease;
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

    public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LeaseCount++;
        AcquireLease();
        return ValueTask.FromResult(_lastAcquisition!.Lease);
    }

    private void AcquireLease()
    {
        ExecutionLeaseScope? scope = LeaseIssue == VersionManagerWriteLeaseIssue.None ? new() : null;
        VersionManagerWriteLeaseResult? result = scope is null
            ? new(LeaseIssue) : new(VersionManagerWriteLeaseIssue.None, scope);
        try
        {
            _lastAcquisition = new(result, scope);
            result = null; // The observation owns both objects after acquisition is recorded.
            scope = null;
        }
        finally
        {
            result?.Dispose();
            scope?.Dispose();
        }
    }

    public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    private sealed record LeaseObservation(VersionManagerWriteLeaseResult Lease, ExecutionLeaseScope? Scope);
}

internal sealed class RecordingRecoveryExecutionPort : IManagedSetupRecoveryExecutionPort
{
    // Accessed only by the owning test's single asynchronous flow.
    internal int CallCount { get; private set; }
    internal ManagedSetupRecoveryExecutionPortOutcome Outcome { get; set; } =
        ManagedSetupRecoveryExecutionPortOutcome.Completed;
    internal Func<ManagedSetupRecoveryExecutionRequest, ValueTask>? OnExecute { get; set; }
    internal VersionManagerWriteLeaseResult? WriterLease { get; private set; }

    public async ValueTask<ManagedSetupRecoveryExecutionPortOutcome> ExecuteAsync(
        ManagedSetupRecoveryExecutionRequest request,
        VersionManagerWriteLeaseResult writerLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        WriterLease = writerLease;
        if (OnExecute is not null)
        {
            await OnExecute(request).ConfigureAwait(false);
        }
        return Outcome;
    }
}

internal sealed class ExecutionLeaseScope : IDisposable
{
    // Interlocked/Volatile protect disposal observation across the coordinator's awaits.
    private int _disposed;
    internal bool IsActive => Volatile.Read(ref _disposed) == 0;
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
