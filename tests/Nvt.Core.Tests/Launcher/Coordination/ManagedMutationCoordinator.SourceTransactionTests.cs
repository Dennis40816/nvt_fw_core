// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.TestSupport;
using Xunit;
using static Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Scoped source transactions share mutation ordering and exact writer authority.</summary>
public sealed partial class ManagedMutationCoordinatorSourceTransactionTests
{
    /// <summary>Each frozen superseding operation invokes its callback once before writer acquisition.</summary>
    [Theory]
    [InlineData("install")]
    [InlineData("delete")]
    [InlineData("source")]
    [InlineData("retention")]
    [InlineData("activation")]
    public async Task SupersessionRunsOnceBeforeWriterAcquisition(string operation)
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        coordinator.SupersedeRunningCheck = () =>
        {
            Assert.False(store.HoldsWriter);
            store.Trace.Add("supersede");
        };
        store.BeforeAcquire = () => Assert.Equal(["supersede"], store.Trace);

        await RunOperationAsync(operation, coordinator, TestContext.Current.CancellationToken);

        Assert.Equal(["supersede", "acquire:0", "writer:None"], store.Trace.Take(3));
        Assert.Equal(1, store.Trace.Count(static entry => entry == "supersede"));
        Assert.Equal(1, store.WriteLeaseCount);
        Assert.False(store.HoldsWriter);
    }

    /// <summary>A queued operation cannot supersede discovery until the existing coordinator gate opens.</summary>
    [Theory]
    [InlineData("install")]
    [InlineData("delete")]
    [InlineData("source")]
    public async Task SupersessionWaitsInsideTheSharedMutationGate(string operation)
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        var entered = new SignalWait("Source gate entered");
        var release = new SignalWait("Release source gate");
        coordinator.SupersedeRunningCheck = () => store.Trace.Add("supersede");
        Task<bool> holder = coordinator.RunSourceTransactionAsync(false, async (idleScope, token) =>
        {
            _ = entered.Set();
            await release.WaitAsync(token);
            return true;
        }, TestContext.Current.CancellationToken).AsTask();
        await entered.WaitAsync(TestContext.Current.CancellationToken);
        Task queued = RunOperationAsync(operation, coordinator, TestContext.Current.CancellationToken);
        try
        {
            Assert.False(queued.IsCompleted);
            Assert.Empty(store.Trace);
            Assert.Equal(0, store.WriteLeaseCount);
        }
        finally
        {
            _ = release.Set();
            await holder;
            await queued;
        }
        Assert.Equal(["supersede", "acquire:0", "writer:None"], store.Trace.Take(3));
        Assert.Equal(1, store.Trace.Count(static entry => entry == "supersede"));
    }

    /// <summary>An absent callback preserves the existing writer trace and mutation save counts.</summary>
    [Theory]
    [InlineData("install", 2)]
    [InlineData("delete", 2)]
    [InlineData("source", 0)]
    [InlineData("retention", 0)]
    [InlineData("activation", 1)]
    public async Task NoSupersessionCallbackPreservesExistingBehavior(string operation, int saves)
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);

        await RunOperationAsync(operation, coordinator, TestContext.Current.CancellationToken);

        Assert.Equal(["acquire:0", "writer:None"], store.Trace.Take(2));
        Assert.Equal(saves, store.SaveCount);
        Assert.Equal(1, store.WriteLeaseCount);
        Assert.False(store.HoldsWriter);
    }

    /// <summary>A registry callback can reject obsolete ownership inside the gate without acquiring or superseding.</summary>
    [Fact]
    public async Task RegistryOwnershipPrecedesWriterAndDoesNotSupersedeItself()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        coordinator.SupersedeRunningCheck = () => store.Trace.Add("supersede");

        bool admitted = await coordinator.RunSourceTransactionAsync(false,
            static (_, _) => ValueTask.FromResult(false), TestContext.Current.CancellationToken);

        Assert.False(admitted);
        Assert.Empty(store.Trace);
        Assert.Equal(0, store.WriteLeaseCount);
    }

    /// <summary>Invalid capabilities cannot authorize even the first durable reload in a transaction.</summary>
    [Theory]
    [InlineData("arbitrary")]
    [InlineData("foreign")]
    [InlineData("disposed")]
    public async Task TransactionRejectsArbitraryForeignAndDisposedWriters(string kind)
    {
        using var store = new MemoryStateStore(Initial());
        using var foreign = new MemoryStateStore(Initial());
        using VersionManagerWriteLeaseResult writer = await InvalidWriterAsync(kind, store, foreign);
        using ManagedMutationCoordinator coordinator = Create(store, writer: writer);

        bool acquired = await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            bool accepted = await scope.AcquireWriterAsync(token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ReloadAsync(token).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.LoadClearLauncherFenceAsync(token).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.TrySaveAsync(Initial(), Initial(), false, token).AsTask());
            Assert.Equal(VersionManagerStateLoadIssue.Unavailable, scope.PublishStateUnavailable().StateIssue);
            return accepted;
        }, TestContext.Current.CancellationToken);

        Assert.False(acquired);
        Assert.Empty(store.Trace);
        Assert.Equal(0, store.SaveCount);
        Assert.False(writer.HoldsStatePath(foreign.StatePath));
    }

    /// <summary>The bridge checks live custody again after readmission instead of trusting historical acquisition.</summary>
    [Fact]
    public async Task WriterDisposedDuringReadmissionCannotSave()
    {
        using var store = new MemoryStateStore(Initial());
        using VersionManagerWriteLeaseResult writer = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            store.StatePath, TimeSpan.Zero, TestContext.Current.CancellationToken);
        using ManagedMutationCoordinator coordinator = Create(store, writer: writer);

        await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            ManagedMutationSnapshot durable = await scope.ReloadAsync(token);
            writer.Dispose();
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.TrySaveAsync(
                durable.State!, Proposed(durable.State!), true, token).AsTask());
            return true;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(0, store.SaveCount);
        Assert.Equal(CoordinationStateCodec.Encode(Initial()), CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>Every persisted state field participates in the lease-held match before conditional save.</summary>
    [Theory]
    [InlineData("root")]
    [InlineData("root-spelling")]
    [InlineData("source")]
    [InlineData("source-spelling")]
    [InlineData("active")]
    [InlineData("fallback")]
    [InlineData("admissions")]
    [InlineData("admission-version")]
    [InlineData("admission-identity")]
    [InlineData("admission-hash")]
    [InlineData("activation")]
    [InlineData("failed")]
    [InlineData("retention")]
    [InlineData("mutation")]
    [InlineData("registry")]
    [InlineData("registry-revision")]
    [InlineData("registry-digest")]
    [InlineData("registry-pin")]
    public async Task CompleteDurableSnapshotDriftAbortsSave(string field)
    {
        VersionManagerState initial = Initial();
        VersionManagerState changed = ChangeField(initial, field);
        using var store = new MemoryStateStore(initial);
        using ManagedMutationCoordinator coordinator = Create(store);
        ManagedMutationSnapshot observed = await coordinator.InitializeAsync(
            TestContext.Current.CancellationToken, isReadOnly: true);
        store.BeforeAcquire = () => store.ReplaceState(changed);

        bool saved = await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            _ = await scope.ReloadAsync(token);
            Assert.False(scope.MatchesDurableSnapshot(observed.State!));
            return await scope.TrySaveAsync(observed.State!, Proposed(initial), true, token);
        }, TestContext.Current.CancellationToken);

        Assert.False(saved);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(CoordinationStateCodec.Encode(changed), CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>Activation and mutation journal facts are compared in full, including nested admission identity.</summary>
    [Theory]
    [InlineData("activation-candidate")]
    [InlineData("activation-identity")]
    [InlineData("activation-prior-active")]
    [InlineData("activation-prior-fallback")]
    [InlineData("activation-phase")]
    [InlineData("mutation-kind")]
    [InlineData("mutation-version")]
    [InlineData("mutation-identity")]
    [InlineData("mutation-hash")]
    public async Task CompleteJournalSnapshotDriftAbortsSave(string field)
    {
        (VersionManagerState expected, VersionManagerState changed) = JournalStates(field);
        using var store = new MemoryStateStore(expected);
        var repository = new TransactionRepository(expected.Admissions)
        {
            InventoryResultOverride = ManagedVersionInventoryReadResult.Unavailable(),
        };
        using ManagedMutationCoordinator coordinator = Create(store, repository: repository);
        store.BeforeAcquire = () => store.ReplaceState(changed);

        bool saved = await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            _ = await scope.ReloadAsync(token);
            Assert.False(scope.MatchesDurableSnapshot(expected));
            return await scope.TrySaveAsync(expected, Proposed(expected), true, token);
        }, TestContext.Current.CancellationToken);

        Assert.False(saved);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(CoordinationStateCodec.Encode(changed), CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>Initialization and activation cancellation retain the frozen absence of supersession.</summary>
    [Fact]
    public async Task InitializationAndActivationCancellationDoNotSupersedeDiscovery()
    {
        VersionManagerState initial = Initial();
        using var store = new MemoryStateStore(VersionActivationPolicy.BeginActivation(initial, ManagedAppVersion.Parse("1.0.1")));
        using ManagedMutationCoordinator coordinator = Create(store);
        coordinator.SupersedeRunningCheck = () => store.Trace.Add("supersede");

        _ = await coordinator.InitializeAsync(TestContext.Current.CancellationToken, isReadOnly: true);
        _ = await coordinator.CancelPendingActivationAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("supersede", store.Trace);
        Assert.Equal(1, store.WriteLeaseCount);
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>Overlapping calls and later busy results cannot replace the first pending or held lease.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceTransactionAcquiresOnlyOneWriterResult(bool completeBeforeSecond)
    {
        using var store = new MemoryStateStore(Initial());
        using var custody = new CountingCustody(store.StatePath);
        using var writer = new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.None, custody);
        var release = new SignalWait("Release source writer acquisition");
        var port = new SuppliedLeaseStore(store, writer, release);
        using ManagedMutationCoordinator coordinator = Create(store, stateStore: port);
        await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            Task<bool> first = scope.AcquireWriterAsync(token).AsTask();
            try
            {
                if (completeBeforeSecond)
                {
                    _ = release.Set();
                    Assert.True(await first);
                }

                await Assert.ThrowsAsync<InvalidOperationException>(() => scope.AcquireWriterAsync(token).AsTask());
                Assert.Equal(1, port.AcquisitionCount);
            }
            finally
            {
                _ = release.Set();
                Assert.True(await first);
            }
            Assert.True(writer.HoldsStatePath(store.StatePath));
            Assert.Equal(0, custody.DisposeCount);
            _ = await scope.ReloadAsync(token);
            return true;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, port.AcquisitionCount);
        Assert.False(writer.HoldsStatePath(store.StatePath));
        Assert.Equal(1, custody.DisposeCount);
    }

    /// <summary>Orphaned acquisition and explicit in-flight disposal both release late custody exactly once.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedScopeRejectsLateWriterWithoutRevival(bool disposeInsideCallback)
    {
        using var store = new MemoryStateStore(Initial());
        using var custody = new CountingCustody(store.StatePath);
        using var writer = new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.None, custody);
        var release = new SignalWait("Complete orphaned source writer acquisition");
        var port = new SuppliedLeaseStore(store, writer, release);
        using ManagedMutationCoordinator coordinator = Create(store, stateStore: port);
        var (scope, acquiring) = await coordinator.RunSourceTransactionAsync(false, (transaction, token) =>
        {
            Task<bool> pending = transaction.AcquireWriterAsync(token).AsTask();
            DisposeScopeIfRequested(transaction, disposeInsideCallback);
            return ValueTask.FromResult((transaction, pending));
        }, TestContext.Current.CancellationToken);
        Assert.False(acquiring.IsCompleted);
        Assert.Equal(0, custody.DisposeCount);
        _ = release.Set();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => acquiring.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, custody.DisposeCount);
        Assert.False(writer.HoldsStatePath(store.StatePath));
        Assert.Throws<ObjectDisposedException>(() => scope.Snapshot);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => scope.AcquireWriterAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => scope.ReloadAsync(TestContext.Current.CancellationToken).AsTask());
        ((IDisposable)scope).Dispose();
        Assert.Equal(1, custody.DisposeCount);
        Assert.Equal(1, port.AcquisitionCount);
    }

    /// <summary>Readmission and both fences retain frozen ordering and a failed fence prevents save.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public async Task RegistryTransactionPreservesReloadReadmissionFenceAndSaveOrder(int blockedFence, bool succeeds)
    {
        VersionManagerState initial = Initial();
        using var store = new MemoryStateStore(initial);
        var fence = new SourceFence(store, blockedFence);
        using ManagedMutationCoordinator coordinator = Create(store, fence);
        coordinator.SupersedeRunningCheck = () => store.Trace.Add("supersede");

        bool saved = await CommitRegistryAsync(coordinator, store, initial);

        Assert.Equal(succeeds, saved);
        Assert.Equal(succeeds ? 1 : 0, store.SaveCount);
        Assert.Equal(ExpectedRegistryTrace(blockedFence), store.Trace);
        Assert.Equal(CoordinationStateCodec.Encode(succeeds ? Proposed(initial) : initial),
            CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>Manual source commits retain their fence-before-reload and registry-aware second fence order.</summary>
    [Fact]
    public async Task ManualSourceTransactionPreservesItsDistinctFenceOrder()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store, new SourceFence(store));
        coordinator.SupersedeRunningCheck = () => store.Trace.Add("supersede");

        bool saved = await coordinator.RunSourceTransactionAsync(true, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            Assert.NotNull(await scope.LoadClearLauncherFenceAsync(token));
            ManagedMutationSnapshot durable = await scope.ReloadAsync(token);
            Assert.NotNull(await scope.LoadClearLauncherFenceAsync(token));
            return await scope.TrySaveAsync(durable.State!, Proposed(durable.State!), true, token);
        }, TestContext.Current.CancellationToken);

        Assert.True(saved);
        Assert.Equal(["supersede", "acquire:0", "writer:None", "fence:1", "load:app", "fence:2",
            "save:app:committed"], store.Trace);
    }

    /// <summary>Equal publications preserve the exact reloaded durable state object and perform no write.</summary>
    [Fact]
    public async Task UnchangedPublicationRetainsDurableObjectWithoutSave()
    {
        VersionManagerState initial = Initial();
        using var store = new MemoryStateStore(initial);
        using ManagedMutationCoordinator coordinator = Create(store);

        await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            ManagedMutationSnapshot durable = await scope.ReloadAsync(token);
            Assert.NotSame(initial, durable.State);
            Assert.True(scope.MatchesDurableSnapshot(initial));
            VersionManagerState equal = initial.WithUpdateSource(initial.UpdateSource, initial.SourceRegistryState);
            Assert.True(await scope.TrySaveAsync(initial, equal, false, token));
            Assert.Same(durable.State, scope.Snapshot.State);
            return true;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(0, store.SaveCount);
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>A failed atomic save neither adopts proposed state nor replaces prior durable bytes.</summary>
    [Fact]
    public async Task SourceSaveFailureLeavesPriorState()
    {
        VersionManagerState initial = Initial();
        using var store = new FailingStateStore(initial, failOnSave: 1);
        using ManagedMutationCoordinator coordinator = Create(store);

        bool saved = await coordinator.RunSourceTransactionAsync(true, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            ManagedMutationSnapshot durable = await scope.ReloadAsync(token);
            bool committed = await scope.TrySaveAsync(initial, Proposed(initial), true, token);
            Assert.Same(durable, scope.Snapshot);
            return committed;
        }, TestContext.Current.CancellationToken);

        Assert.False(saved);
        Assert.Equal(1, store.SaveCount);
        Assert.False(store.HoldsWriter);
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>A callback cannot retain authority after its awaited scope has ended.</summary>
    [Fact]
    public async Task CompletedTransactionInvalidatesEscapedScope()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        ManagedMutationCoordinator.SourceTransaction? escaped = null;
        await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            escaped = scope;
            Assert.True(await scope.AcquireWriterAsync(token));
            _ = await scope.ReloadAsync(token);
            return true;
        }, TestContext.Current.CancellationToken);

        ManagedMutationCoordinator.SourceTransaction expired = Assert.IsType<ManagedMutationCoordinator.SourceTransaction>(escaped);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => expired.AcquireWriterAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => expired.ReloadAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => expired.TrySaveAsync(
            Initial(), Proposed(Initial()), true, TestContext.Current.CancellationToken).AsTask());
        Assert.Throws<ObjectDisposedException>(() => expired.MatchesDurableSnapshot(Initial()));
        Assert.Equal(0, store.SaveCount);
        Assert.False(store.HoldsWriter);
        Assert.Equal(1, store.WriteLeaseCount);
    }

    /// <summary>Pre-cancellation prevents the hook, callback and writer from starting.</summary>
    [Fact]
    public async Task CancellationBeforeGateStartsNoTransaction()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        coordinator.SupersedeRunningCheck = () => store.Trace.Add("supersede");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunSourceTransactionAsync(
            true, (_, _) => { store.Trace.Add("callback"); return ValueTask.FromResult(true); }, cancellation.Token).AsTask());

        Assert.Empty(store.Trace);
        Assert.Equal(0, store.WriteLeaseCount);
    }

    /// <summary>Cancellation while queued does not supersede or release the other callback's gate.</summary>
    [Fact]
    public async Task CancellationWhileWaitingForGateStartsNoWriter()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new SignalWait("Source gate entered before cancellation");
        var release = new SignalWait("Release source gate after cancellation");
        coordinator.SupersedeRunningCheck = () => store.Trace.Add("supersede");
        Task<bool> holder = coordinator.RunSourceTransactionAsync(false, async (idleScope, token) =>
        {
            _ = entered.Set();
            await release.WaitAsync(token);
            return true;
        }, TestContext.Current.CancellationToken).AsTask();
        await entered.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            Task<OperationCanceledException> canceled = Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => RunOperationAsync("source", coordinator, cancellation.Token));
            await cancellation.CancelAsync();
            _ = await canceled;
            Assert.False(holder.IsCompleted);
            Assert.Empty(store.Trace);
        }
        finally
        {
            _ = release.Set();
            await holder;
        }
        Assert.Equal(0, store.WriteLeaseCount);
    }

    /// <summary>A cancellation raised by supersession propagates before the writer can be held.</summary>
    [Fact]
    public async Task CancellationAfterHookBeforeLeaseLeavesNoWriter()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        coordinator.SupersedeRunningCheck = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RunOperationAsync("source", coordinator, cancellation.Token));

        Assert.False(store.HoldsWriter);
        Assert.Equal(0, store.SaveCount);
        Assert.DoesNotContain("load:app", store.Trace);
        coordinator.SupersedeRunningCheck = null;
        await RunOperationAsync("source", coordinator, TestContext.Current.CancellationToken);
    }

    /// <summary>Cancellation after acquisition releases both writer and gate without a save.</summary>
    [Fact]
    public async Task CancellationAfterLeaseReleasesWriterAndGate()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var acquired = new SignalWait("Source writer acquired before cancellation");
        var release = new SignalWait("Release source writer");
        Task<bool> pending = coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            _ = acquired.Set();
            await release.WaitAsync(token);
            return true;
        }, cancellation.Token).AsTask();
        await acquired.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(store.HoldsWriter);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await cancellation.CancelAsync();
            _ = release.Set();
            try
            {
                await pending;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Join writer and gate cleanup even when an earlier assertion failed.
            }
        }
        Assert.False(store.HoldsWriter);
        Assert.Equal(0, store.SaveCount);
        await RunOperationAsync("source", coordinator, TestContext.Current.CancellationToken);
        Assert.Equal(2, store.WriteLeaseCount);
    }

    /// <summary>A throwing supersession callback leaves the gate reusable and never acquires a writer.</summary>
    [Theory]
    [InlineData("source"), InlineData("install"), InlineData("delete"), InlineData("activation")]
    public async Task SupersessionFailureReleasesGateBeforeWriterAcquisition(string operation)
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        var failure = new IOException("Synthetic supersession failure.");
        coordinator.SupersedeRunningCheck = () => throw failure;
        IOException thrown = await Assert.ThrowsAsync<IOException>(() => RunOperationAsync(operation, coordinator, TestContext.Current.CancellationToken));
        Assert.Same(failure, thrown);
        Assert.False(store.HoldsWriter);
        Assert.Equal(0, store.WriteLeaseCount);
        coordinator.SupersedeRunningCheck = null;
        await RunOperationAsync(operation, coordinator, TestContext.Current.CancellationToken);
        Assert.Equal(1, store.WriteLeaseCount);
        Assert.False(store.HoldsWriter);
    }

    private static VersionManagerState Initial() => State(
        [Admission("1.0.2"), Admission("1.0.1"), Admission("1.0.0")], "1.0.2", "1.0.1",
        Path.GetFullPath("source-root"), new VersionSourceRegistryState(7, Hash, isManualPin: false));

    private static VersionManagerState Proposed(VersionManagerState state) => state.WithUpdateSource(
        Path.GetFullPath("replacement-source"), new VersionSourceRegistryState(8, Hash, isManualPin: true));

    private static ManagedMutationCoordinator Create(CoordinationStateStore store,
        ILauncherMutationFence? fence = null, VersionManagerWriteLeaseResult? writer = null,
        IManagedVersionRepository? repository = null, IVersionManagerStateStore? stateStore = null)
    {
        repository ??= new TransactionRepository(store.State.Admissions, unadmittedVersion: "1.0.3");
        var selection = new FixedPackageSelection(Catalog("1.0.3")) { Source = store.State.UpdateSource };
        IVersionManagerStateStore port = stateStore ?? (writer is null ? store : new SuppliedLeaseStore(store, writer));
        return new("managed-root", store.StatePath, port, repository,
            fence ?? new ClearLauncherFence(), selection, new RetentionPolicy());
    }

    private static async Task RunOperationAsync(string operation, ManagedMutationCoordinator coordinator, CancellationToken token)
    {
        switch (operation)
        {
            case "install":
                Assert.True((await coordinator.InstallAsync(ManagedAppVersion.Parse("1.0.3"), token)).Install.IsSuccess);
                break;
            case "delete":
                Assert.Equal(VersionDeleteOperationIssue.None,
                    (await coordinator.DeleteAsync(ManagedAppVersion.Parse("1.0.1"), true, token)).OperationIssue);
                break;
            case "retention":
                _ = await coordinator.AcknowledgeRetentionReviewAsync(token);
                break;
            case "activation":
                _ = await coordinator.PrepareActivationAsync(ManagedAppVersion.Parse("1.0.1"), token);
                break;
            default:
                Assert.True(await coordinator.RunSourceTransactionAsync(true,
                    static (scope, cancellationToken) => scope.AcquireWriterAsync(cancellationToken), token));
                break;
        }
    }

    private static async ValueTask<VersionManagerWriteLeaseResult> InvalidWriterAsync(
        string kind, CoordinationStateStore store, CoordinationStateStore foreign)
    {
        if (kind == "arbitrary")
        {
            return new(VersionManagerWriteLeaseIssue.None, new MemoryStream());
        }
        VersionManagerWriteLeaseResult? writer = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            kind == "foreign" ? foreign.StatePath : store.StatePath, TimeSpan.Zero, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(writer.IsAcquired);
            if (kind == "disposed") { writer.Dispose(); }
            VersionManagerWriteLeaseResult transferred = writer;
            writer = null;
            return transferred;
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static VersionManagerState ChangeField(VersionManagerState state, string field)
    {
        ManagedVersionAdmission[] admissions = state.Admissions.Select(admission =>
            admission.Version != ManagedAppVersion.Parse("1.0.0") ? admission : field switch
            {
                "admission-version" => admission with { Version = ManagedAppVersion.Parse("0.9.9") },
                "admission-identity" => admission with { AdmissionIdentity = "changed" },
                "admission-hash" => admission with { ReleaseManifestSha256 = new string('b', 64) },
                _ => admission,
            }).ToArray();
        if (field == "admissions")
        {
            admissions = [.. admissions.Where(static admission => admission.Version != ManagedAppVersion.Parse("1.0.0"))];
        }
        return VersionManagerState.Create(
            field switch
            {
                "source" => Path.GetFullPath("changed-source"),
                "source-spelling" => state.UpdateSource!.ToUpperInvariant(),
                _ => state.UpdateSource,
            },
            field == "active" ? ManagedAppVersion.Parse("1.0.1") : state.ActiveVersion,
            field == "fallback" ? ManagedAppVersion.Parse("1.0.2") : state.LastKnownGoodVersion,
            admissions,
            field == "activation" ? new PendingVersionActivation(ManagedAppVersion.Parse("1.0.1"), "identity-1.0.1",
                state.ActiveVersion, state.LastKnownGoodVersion) : null,
            field == "failed" ? ManagedAppVersion.Parse("1.0.1") : null,
            field == "retention",
            field == "mutation" ? new PendingManagedVersionMutation(ManagedVersionMutationKind.Install, Admission("1.0.3")) : null,
            field switch
            {
                "root" => Path.GetFullPath("other-root"),
                "root-spelling" => state.ManagedRootIdentity!.ToUpperInvariant(),
                _ => state.ManagedRootIdentity,
            },
            field switch
            {
                "registry" => null,
                "registry-revision" => new(8, Hash, isManualPin: false),
                "registry-digest" => new(7, new string('b', 64), isManualPin: false),
                "registry-pin" => new(7, Hash, isManualPin: true),
                _ => state.SourceRegistryState,
            });
    }

    private static (VersionManagerState Expected, VersionManagerState Changed) JournalStates(string field)
    {
        VersionManagerState state = Initial();
        if (!field.StartsWith("activation", StringComparison.Ordinal))
        {
            var mutation = new PendingManagedVersionMutation(ManagedVersionMutationKind.Install, Admission("1.0.3"));
            PendingManagedVersionMutation changedMutation = field switch
            {
                "mutation-kind" => new(ManagedVersionMutationKind.Delete, state.Admissions[0]),
                "mutation-version" => mutation with { Admission = mutation.Admission with { Version = ManagedAppVersion.Parse("1.0.4") } },
                "mutation-identity" => mutation with { Admission = mutation.Admission with { AdmissionIdentity = "changed" } },
                _ => mutation with { Admission = mutation.Admission with { ReleaseManifestSha256 = new string('b', 64) } },
            };
            return (state.WithPendingMutation(mutation), state.WithPendingMutation(changedMutation));
        }
        ManagedVersionAdmission[] admissions = state.Admissions.Select(
            static admission => admission with { AdmissionIdentity = "shared-journal-identity" }).ToArray();
        var activation = new PendingVersionActivation(ManagedAppVersion.Parse("1.0.1"), "shared-journal-identity",
            state.ActiveVersion, state.LastKnownGoodVersion);
        PendingVersionActivation changedActivation = field switch
        {
            "activation-candidate" => activation with { CandidateVersion = ManagedAppVersion.Parse("1.0.0") },
            "activation-identity" => activation with { CandidateAdmissionIdentity = "changed" },
            "activation-prior-active" => activation with { PreviousActiveVersion = ManagedAppVersion.Parse("1.0.0") },
            "activation-prior-fallback" => activation with { PreviousLastKnownGoodVersion = ManagedAppVersion.Parse("1.0.0") },
            _ => activation with { Phase = VersionActivationPhase.CandidateLaunchRecorded },
        };
        VersionManagerState expected = VersionManagerState.Create(state.UpdateSource, state.ActiveVersion,
            state.LastKnownGoodVersion, admissions, activation, null, false,
            managedRootIdentity: state.ManagedRootIdentity, sourceRegistryState: state.SourceRegistryState);
        ManagedVersionAdmission[] changedAdmissions = admissions.Select(admission =>
            field == "activation-identity" && admission.Version == activation.CandidateVersion
                ? admission with { AdmissionIdentity = "changed" } : admission).ToArray();
        VersionManagerState changed = VersionManagerState.Create(state.UpdateSource, state.ActiveVersion,
            state.LastKnownGoodVersion, changedAdmissions, changedActivation, null, false,
            managedRootIdentity: state.ManagedRootIdentity, sourceRegistryState: state.SourceRegistryState);
        return (expected, changed);
    }

    private static ValueTask<bool> CommitRegistryAsync(ManagedMutationCoordinator coordinator,
        CoordinationStateStore store, VersionManagerState initial) => coordinator.RunSourceTransactionAsync(
            false, async (scope, token) =>
            {
                store.Trace.Add("owns-check:true");
                Assert.True(await scope.AcquireWriterAsync(token));
                _ = await scope.ReloadAsync(token);
                Assert.True(scope.MatchesDurableSnapshot(initial));
                if (await scope.LoadClearLauncherFenceAsync(token) is null) { return false; }
                store.Trace.Add("registry:reload");
                store.Trace.Add("candidate:readmit");
                if (await scope.LoadClearLauncherFenceAsync(token) is null) { return false; }
                return await scope.TrySaveAsync(initial, Proposed(initial), true, token);
            }, TestContext.Current.CancellationToken);

    private static string[] ExpectedRegistryTrace(int blockedFence) => blockedFence switch
    {
        1 => ["owns-check:true", "acquire:0", "writer:None", "load:app", "fence:1"],
        2 => ["owns-check:true", "acquire:0", "writer:None", "load:app", "fence:1", "registry:reload", "candidate:readmit", "fence:2"],
        _ => ["owns-check:true", "acquire:0", "writer:None", "load:app", "fence:1", "registry:reload", "candidate:readmit", "fence:2", "save:app:committed"],
    };

    private static void DisposeScopeIfRequested(IDisposable scope, bool dispose)
    {
        if (dispose) { scope.Dispose(); }
    }

    private sealed class CountingCustody(string statePath) : IVersionManagerWriteLeaseCustody
    {
        // Guard: Interlocked/Volatile protects the disposal count across acquisition continuations.
        private int _disposeCount;
        internal int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool HoldsStatePath(string candidate) => DisposeCount == 0 &&
            string.Equals(statePath, candidate, StringComparison.Ordinal);
        public void Dispose() => _ = Interlocked.Increment(ref _disposeCount);
    }

    private sealed class SuppliedLeaseStore(CoordinationStateStore store, VersionManagerWriteLeaseResult writer,
        SignalWait? release = null) : IVersionManagerStateStore
    {
        // Guard: Interlocked/Volatile protects the count; release gates the first acquisition.
        private int _acquisitionCount;
        internal int AcquisitionCount => Volatile.Read(ref _acquisitionCount);
        public async ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _acquisitionCount) > 1) { return new(VersionManagerWriteLeaseIssue.Busy); }
            if (release is not null) { await release.WaitAsync(cancellationToken); }
            return writer;
        }
        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken) => store.LoadAsync(cancellationToken);
        public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken) => store.SaveAsync(state, cancellationToken);
    }

    private sealed class SourceFence(CoordinationStateStore store, int blockedFence = 0) : ILauncherMutationFence
    {
        // Guard: the coordinator's _mutation gate serializes this test port's reads.
        private int _reads;
        public ValueTask<LauncherMutationProtection> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(store.HoldsWriter);
            store.Trace.Add($"fence:{++_reads}");
            return ValueTask.FromResult(new LauncherMutationProtection(LauncherMutationFenceIssue.None,
                _reads == blockedFence, null, null, []));
        }
        public ValueTask<LauncherMutationFenceIssue> RetireLastKnownGoodOwnerAsync(
            ManagedVersionAdmission expectedOwner, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Source transactions cannot retire launcher owners.");
    }
}
