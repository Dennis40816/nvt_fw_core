// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinator
{
    // Invoked only under _mutation, before releasing it. Integration owns the projection.
    internal Action<ManagedMutationSnapshot, bool>? ObserveExperienceSnapshot { get; set; }

    private ManagedMutationSnapshot ObserveReloadedSnapshot(ManagedMutationSnapshot snapshot)
    {
        ObserveExperienceSnapshot?.Invoke(snapshot, true);
        return snapshot;
    }

    private void PublishAndReleaseMutation()
    {
        try
        {
            if (_current is { } snapshot) { ObserveExperienceSnapshot?.Invoke(snapshot, false); }
        }
        finally { _ = _mutation.Release(); }
    }

    internal Action? SupersedeRunningCheck
    {
        get => Volatile.Read(ref _supersedeRunningCheck);
        set => Volatile.Write(ref _supersedeRunningCheck, value);
    }

    private ValueTask<VersionManagerWriteLeaseResult> AcquireSupersedingWriteLeaseAsync(
        CancellationToken cancellationToken)
    {
        SupersedeRunningCheck?.Invoke();
        return AcquireWriteLeaseAsync(TimeSpan.Zero, cancellationToken);
    }

    // The callback retains the frozen distinction between manual commits (supersede first)
    // and registry commits (check owned discovery first, without superseding themselves).
    internal async ValueTask<T> RunSourceTransactionAsync<T>(
        bool supersedeRunningCheck,
        Func<SourceTransaction, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (supersedeRunningCheck)
            {
                SupersedeRunningCheck?.Invoke();
            }
            using var transaction = new SourceTransaction(this);
            return await operation(transaction, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _mutation.Release();
        }
    }

    // Only the awaited callback may use this scope. _mutation serializes mutations;
    // _lifetimeLock protects the phase, writer and snapshot, including late completions.
    internal sealed class SourceTransaction : IDisposable
    {
        private readonly ManagedMutationCoordinator _owner;
        private readonly Lock _lifetimeLock = new();
        private TransactionLifetime _lifetime = new(WriterPhase.None, null, null);

        internal SourceTransaction(ManagedMutationCoordinator owner)
        {
            _owner = owner;
        }

        internal ManagedMutationSnapshot Snapshot => RequireLifetime().Snapshot ??
            throw new InvalidOperationException("Source transaction has not reloaded durable state.");

        internal async ValueTask<bool> AcquireWriterAsync(CancellationToken cancellationToken)
        {
            lock (_lifetimeLock)
            {
                TransactionLifetime lifetime = RequireLifetime();
                if (lifetime.Phase != WriterPhase.None)
                {
                    throw new InvalidOperationException("Source transaction already acquired its writer result.");
                }
                _lifetime = lifetime with { Phase = WriterPhase.Acquiring };
            }
            VersionManagerWriteLeaseResult? acquired = await _owner.AcquireWriteLeaseAsync(
                TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_lifetimeLock)
                {
                    TransactionLifetime lifetime = RequireLifetime();
                    _lifetime = lifetime with { Phase = WriterPhase.Held, Writer = acquired };
                    bool isAcquired = acquired.IsAcquired;
                    acquired = null;
                    return isAcquired;
                }
            }
            finally
            {
                acquired?.Dispose();
            }
        }

        internal async ValueTask<ManagedMutationSnapshot> ReloadAsync(CancellationToken cancellationToken)
        {
            _ = RequireWriter();
            ManagedMutationSnapshot snapshot = await _owner.ReloadDurableCurrentWithoutLockAsync(
                cancellationToken).ConfigureAwait(false);
            lock (_lifetimeLock)
            {
                _lifetime = RequireLifetime() with { Snapshot = snapshot };
            }
            return snapshot;
        }

        internal bool MatchesDurableSnapshot(VersionManagerState expected)
        {
            ArgumentNullException.ThrowIfNull(expected);
            _ = RequireWriter();
            return Snapshot.State is { } durable && expected.CreateDurableSnapshotToken().Matches(
                durable.CreateDurableSnapshotToken());
        }

        internal ValueTask<LauncherMutationProtection?> LoadClearLauncherFenceAsync(
            CancellationToken cancellationToken)
        {
            _ = RequireWriter();
            return _owner.LoadClearLauncherFenceAsync(cancellationToken);
        }

        // Compare the pre-readmission snapshot with the lease-held reload. Do not reload
        // after readmission: the writer remains held, and a new read would change ordering.
        // The caller performs its pin check and both fences at their frozen positions.
        internal async ValueTask<bool> TrySaveAsync(
            VersionManagerState expected,
            VersionManagerState proposed,
            bool stateChanged,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(proposed);
            if (!MatchesDurableSnapshot(expected))
            {
                return false;
            }
            if (stateChanged && !await _owner.TrySaveAsync(proposed, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            lock (_lifetimeLock)
            {
                ManagedMutationSnapshot snapshot = Snapshot;
                if (stateChanged)
                {
                    snapshot = snapshot with { State = proposed };
                    _lifetime = RequireLifetime() with { Snapshot = snapshot };
                }
                _owner._current = snapshot;
                return true;
            }
        }

        internal ManagedMutationSnapshot PublishStateUnavailable()
        {
            _ = RequireLifetime();
            return _owner.PublishStateUnavailable();
        }

        private TransactionLifetime RequireLifetime()
        {
            lock (_lifetimeLock)
            {
                return _lifetime.Phase != WriterPhase.Closed ? _lifetime
                    : throw new ObjectDisposedException(nameof(SourceTransaction));
            }
        }

        private TransactionLifetime RequireWriter()
        {
            TransactionLifetime lifetime = RequireLifetime();
            return lifetime.Writer?.HoldsStatePath(_owner._statePath) == true
                ? lifetime
                : throw new InvalidOperationException("Another version-management writer is active.");
        }

        void IDisposable.Dispose()
        {
            lock (_lifetimeLock)
            {
                TransactionLifetime lifetime = _lifetime;
                _lifetime = new(WriterPhase.Closed, null, null);
                lifetime.Writer?.Dispose();
            }
        }

        private enum WriterPhase { None, Acquiring, Held, Closed }

        private sealed record TransactionLifetime(
            WriterPhase Phase, VersionManagerWriteLeaseResult? Writer, ManagedMutationSnapshot? Snapshot);
    }
}
