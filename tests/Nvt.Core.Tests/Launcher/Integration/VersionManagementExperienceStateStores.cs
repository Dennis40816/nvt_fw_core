// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Tests.Launcher.Coordination;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Integration;

internal static partial class VersionManagementExperienceTestSupport
{
    // Each test owns the store. The existing Coordination fixture owns real writer custody
    // and its workspace; memory observations retain frozen object-identity assertions.
    internal class MemoryStateStore : IVersionManagerStateStore, IDisposable
    {
        private readonly CoordinationStateStore _writer;
        // Guard: test gates serialize successful acquisition; a contending result cannot replace live custody.
        private VersionManagerWriteLeaseResult? _lastWriter;
        internal MemoryStateStore(VersionManagerState state)
        {
            _writer = new(state);
            State = state;
        }
        internal string StatePath => _writer.StatePath;
        internal bool HoldsWriter => _lastWriter?.HoldsStatePath(StatePath) == true;
        internal VersionManagerState State { get; private set; }
        internal int SaveCount { get; private set; }
        internal int WriteLeaseCount { get; private set; }
        internal int LeaseRequestCount => WriteLeaseCount;
        internal int LoadCount { get; private set; }
        internal int FailOnSave { get; set; } = int.MaxValue;
        internal Action? BeforeAcquire { get; set; }
        internal Action? BeforeLoad { get; set; }
        internal Action? BeforeSave { get; set; }
        internal void ReplaceState(VersionManagerState state) => State = state;
        public virtual async ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            BeforeAcquire?.Invoke();
            WriteLeaseCount++;
            VersionManagerWriteLeaseResult writer = await _writer.TryAcquireWriteLeaseAsync(waitTimeout, cancellationToken);
            if (writer.IsAcquired) { _lastWriter = writer; }
            return writer;
        }
        public virtual ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeLoad?.Invoke();
            LoadCount++;
            return ValueTask.FromResult(new VersionManagerStateLoadResult(State, VersionManagerStateLoadIssue.None));
        }
        public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(HoldsWriter);
            BeforeSave?.Invoke();
            SaveCount++;
            if (SaveCount == FailOnSave) { throw new IOException("Injected state commit failure."); }
            State = state;
            return ValueTask.CompletedTask;
        }
        public void Dispose()
        {
            _lastWriter?.Dispose();
            _writer.Dispose();
        }
    }

    internal sealed class FailingStateStore : MemoryStateStore
    {
        internal FailingStateStore(VersionManagerState state, int failOnSave) : base(state) => FailOnSave = failOnSave;
    }

    internal sealed class LeaseCountingStateStore(VersionManagerState state) : MemoryStateStore(state);

    internal sealed class SequencedLeaseStateStore(VersionManagerState state) : MemoryStateStore(state)
    {
        public override async ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            if (WriteLeaseCount == 0) { return await base.TryAcquireWriteLeaseAsync(waitTimeout, cancellationToken); }
            return new(VersionManagerWriteLeaseIssue.Busy);
        }
    }

    internal sealed class ReloadChangedStateStore(VersionManagerState initial, VersionManagerState changed) : MemoryStateStore(initial)
    {
        public override ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            if (LoadCount != 0) { ReplaceState(changed); }
            return base.LoadAsync(cancellationToken);
        }
    }

    internal sealed class SequenceLoadStateStore(params VersionManagerStateLoadResult[] outcomes)
        : MemoryStateStore(VersionManagementExperienceTestSupport.State([Admission("0.10.5")], "0.10.5", "0.10.5"))
    {
        // Guard: Interlocked protects the sequence index for concurrent read-only queries.
        private int _next;
        public override async ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            _ = await base.LoadAsync(cancellationToken);
            return outcomes[Math.Min(Interlocked.Increment(ref _next) - 1, outcomes.Length - 1)];
        }
    }
}
