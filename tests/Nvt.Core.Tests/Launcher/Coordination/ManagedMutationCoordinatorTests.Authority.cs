// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Xunit;
using static Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinatorTests
{
    /// <summary>Read-only initialization preserves the prepared install without acquiring, saving or recovering.</summary>
    [Fact]
    public async Task ReadOnlyInitializationPreservesPreparedMutationWithoutWriterOrRecovery()
    {
        VersionManagerState initial = State([Admission("0.10.5")], "0.10.5", "0.10.5")
            .WithPendingMutation(new(ManagedVersionMutationKind.Install, Admission("0.10.6")));
        using var store = new MemoryStateStore(initial);
        var repository = new TransactionRepository([.. initial.Admissions, initial.PendingMutation!.Admission]);
        using ManagedMutationCoordinator coordinator = Create(store, repository);
        ManagedMutationSnapshot result = await coordinator.InitializeAsync(TestContext.Current.CancellationToken, isReadOnly: true);
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(result.State!));
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
        Assert.Equal(0, store.WriteLeaseCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Equal(ManagedVersionAdmissionState.RecoveryCandidate,
            result.Inventory.Find(ManagedAppVersion.Parse("0.10.6"))!.AdmissionState);
    }

    /// <summary>Only READY-qualified initialization waits for the exact physical writer and reloads its final durable state.</summary>
    [Fact]
    public async Task ManagedReadyInitializationWaitsThenReloadsDurableState()
    {
        VersionManagerState initial = State([Admission("0.10.5")], "0.10.5", "0.10.5");
        using var store = new MemoryStateStore(initial);
        using ManagedMutationCoordinator coordinator = Create(store, new TransactionRepository(initial.Admissions));
        using VersionManagerWriteLeaseResult external = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            store.StatePath, TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.True(external.HoldsStatePath(store.StatePath));
        ManagedMutationSnapshot immediate = await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, immediate.StateIssue);
        Assert.Equal(TimeSpan.Zero, store.Waits[0]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.BeforeAcquire = () => entered.TrySetResult();
        Task<ManagedMutationSnapshot> waiting = coordinator.InitializeAfterManagedReadyAsync(TestContext.Current.CancellationToken).AsTask();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        Assert.DoesNotContain("load:app", store.Trace);
        store.ReplaceState(initial.WithUpdateSource("launcher-committed", sourceRegistryState: null));
        external.Dispose();
        ManagedMutationSnapshot reloaded = await waiting;
        Assert.Equal("launcher-committed", reloaded.State!.UpdateSource);
        Assert.Equal(TimeSpan.FromSeconds(5), store.Waits[1]);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Arbitrary, foreign and disposed writer results cannot load or mutate authority.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NonLiveExactWriterCapabilityFailsClosedBeforeLoad(int kind)
    {
        VersionManagerState initial = State([Admission("0.10.5")], "0.10.5", "0.10.5");
        using var store = new MemoryStateStore(initial);
        using var foreign = new MemoryStateStore(initial);
        VersionManagerWriteLeaseResult result = kind == 0 ? VersionManagerWriteLeaseTestSupport.Acquired()
            : await FileSystemVersionManagerWriteLease.TryAcquireAsync(kind == 1 ? foreign.StatePath : store.StatePath,
                TimeSpan.Zero, TestContext.Current.CancellationToken);
        if (kind == 2) { result.Dispose(); }
        var untrusted = new UntrustedLeaseStore(result);
        using var coordinator = new ManagedMutationCoordinator("managed-root", store.StatePath, untrusted,
            new TransactionRepository(initial.Admissions), new ClearLauncherFence(), new FixedPackageSelection(Catalog("0.10.6")),
            new RetentionPolicy());
        ManagedMutationSnapshot unavailable = await coordinator.InitializeAfterManagedReadyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, unavailable.StateIssue);
        Assert.Equal(0, untrusted.Loads);
        Assert.False(result.HoldsStatePath(kind == 1 ? foreign.StatePath : store.StatePath));
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
    }

    /// <summary>Unavailable complete inventory leaves prepared install and delete journals byte-for-byte intact.</summary>
    [Theory]
    [InlineData(ManagedVersionMutationKind.Install)]
    [InlineData(ManagedVersionMutationKind.Delete)]
    public async Task InventoryUnavailableNeverDropsPreparedMutation(ManagedVersionMutationKind kind)
    {
        VersionManagerState initial = State([Admission("0.10.5"), Admission("0.10.4")], "0.10.5", "0.10.5");
        ManagedVersionAdmission target = kind == ManagedVersionMutationKind.Install ? Admission("0.10.6") : Admission("0.10.4");
        initial = initial.WithPendingMutation(new(kind, target));
        using var store = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions) { InventoryResultOverride = ManagedVersionInventoryReadResult.Unavailable() };
        using ManagedMutationCoordinator coordinator = Create(store, repository);
        ManagedMutationSnapshot unavailable = await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInventoryReadIssue.Unavailable, unavailable.InventoryIssue);
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(unavailable.State!));
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>A launcher journal prevents application mutation recovery without discarding either serialized journal.</summary>
    [Fact]
    public async Task LauncherFencePreservesBothSerializedJournals()
    {
        VersionManagerState initial = State([Admission("0.10.5")], "0.10.5", "0.10.5")
            .WithPendingMutation(new(ManagedVersionMutationKind.Install, Admission("0.10.6")));
        using var store = new MemoryStateStore(initial);
        ManagedLauncherIdentity active = LauncherIdentity(Admission("0.10.5"));
        ManagedLauncherIdentity candidate = LauncherIdentity(Admission("0.10.6"));
        LauncherBootstrapState journal = LauncherBootstrapState.Create(initial.ManagedRootIdentity!, active, active, null, null)
            .Begin(candidate).RecordCandidateLaunch();
        var launcher = new CoordinationLauncherStateStore(store, journal);
        var fence = new TraceFence(store, new(LauncherMutationFenceIssue.None, true, Admission("0.10.5"), Admission("0.10.5"), [Admission("0.10.6")]));
        var repository = new TransactionRepository(initial.Admissions) { InventoryResultOverride = ManagedVersionInventoryReadResult.Unavailable() };
        using ManagedMutationCoordinator coordinator = Create(store, repository, fence);
        ManagedMutationSnapshot result = await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, result.StateIssue);
        Assert.Null(result.State);
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
        Assert.Equal(CoordinationStateCodec.Encode(journal), CoordinationStateCodec.Encode(launcher.State!));
        Assert.Equal(0, repository.InventoryCalls);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Fallback consent precedes owner retirement, which precedes prepared delete, filesystem delete and commit.</summary>
    [Fact]
    public async Task FallbackConsentAndRetirementPrecedePreparedDelete()
    {
        VersionManagerState initial = State([Admission("0.10.5"), Admission("0.10.4")], "0.10.5", "0.10.4");
        using var store = new MemoryStateStore(initial);
        var protection = new LauncherMutationProtection(LauncherMutationFenceIssue.None, false, Admission("0.10.5"), Admission("0.10.4"), []);
        var fence = new TraceFence(store, protection);
        var repository = new TransactionRepository(initial.Admissions)
        { BeforeDelete = () => { Assert.True(store.HoldsWriter); store.Trace.Add("repository:delete"); } };
        using ManagedMutationCoordinator coordinator = Create(store, repository, fence);
        VersionDeleteOperationResult blocked = await coordinator.DeleteAsync(ManagedAppVersion.Parse("0.10.4"), false, TestContext.Current.CancellationToken);
        Assert.Equal(VersionDeleteOperationIssue.RollbackConfirmationRequired, blocked.OperationIssue);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Equal(0, fence.RetireCount);
        Assert.Equal(0, store.SaveCount);
        store.Trace.Clear();
        VersionDeleteOperationResult deleted = await coordinator.DeleteAsync(ManagedAppVersion.Parse("0.10.4"), true, TestContext.Current.CancellationToken);
        Assert.Equal(VersionDeleteOperationIssue.None, deleted.OperationIssue);
        Assert.Equal(1, fence.RetireCount);
        Assert.True(store.Trace.IndexOf("retire:0.10.4") < store.Trace.IndexOf("save:app:Delete"));
        Assert.True(store.Trace.IndexOf("save:app:Delete") < store.Trace.IndexOf("repository:delete"));
        Assert.True(store.Trace.IndexOf("repository:delete") < store.Trace.IndexOf("save:app:committed"));
        Assert.Null(store.State.PendingMutation);
        Assert.Null(store.State.LastKnownGoodVersion);
    }

    /// <summary>The package-selection callback cannot substitute a different requested version.</summary>
    [Fact]
    public async Task PackageSelectionRechecksRequestedVersionBeforeMutation()
    {
        VersionManagerState initial = State([Admission("0.10.5")], "0.10.5", "0.10.5");
        using var store = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using var coordinator = new ManagedMutationCoordinator("managed-root", store.StatePath, store, repository,
            new ClearLauncherFence(), new WrongVersionSelection(), new RetentionPolicy());
        VersionInstallOperationResult result = await coordinator.InstallAsync(ManagedAppVersion.Parse("0.10.6"), TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, result.Install.Issue);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Cancellation after install preparation releases writer custody and leaves durable recovery intent intact.</summary>
    [Fact]
    public async Task PreparedInstallCancellationLeavesJournalForRestart()
    {
        VersionManagerState initial = State([Admission("0.10.5")], "0.10.5", "0.10.5");
        using var store = new MemoryStateStore(initial);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var repository = new TransactionRepository(initial.Admissions)
        {
            BeforeInstall = () => { Assert.True(store.HoldsWriter); cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); },
        };
        using (ManagedMutationCoordinator coordinator = Create(store, repository))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.InstallAsync(
                ManagedAppVersion.Parse("0.10.6"), cancellation.Token).AsTask());
        }
        Assert.Equal(ManagedVersionMutationKind.Install, store.State.PendingMutation!.Kind);
        Assert.False(store.HoldsWriter);
        Assert.Equal(1, store.SaveCount);
        using ManagedMutationCoordinator restarted = Create(store, new TransactionRepository(initial.Admissions));
        ManagedMutationSnapshot recovered = await restarted.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(recovered.State!.PendingMutation);
        Assert.Equal(CoordinationStateCodec.Encode(initial), CoordinationStateCodec.Encode(store.State));
    }

    private static ManagedMutationCoordinator Create(CoordinationStateStore store, IManagedVersionRepository repository, ILauncherMutationFence? fence = null)
        => ManagedMutationTestFactory.Create(ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new FixedPackageSelection(Catalog("0.10.6")), repository, fence);
    private static ManagedLauncherIdentity LauncherIdentity(ManagedVersionAdmission a) => ManagedLauncherIdentity.Create(
        CoordinationFixture.Product, 200_000_000, a.Version, a.AdmissionIdentity, a.ReleaseManifestSha256, a.Version, 1,
        CoordinationFixture.Product.LauncherExecutableRelativePath, 1, new string('b', 64));
    private sealed class UntrustedLeaseStore(VersionManagerWriteLeaseResult result) : IVersionManagerStateStore
    {
        internal int Loads { get; private set; }
        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken)
            => ValueTask.FromResult(result);
        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        { Loads++; throw new InvalidOperationException("Untrusted capability must not load."); }
        public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
    private sealed class WrongVersionSelection : IManagedPackageSelection
    {
        public ValueTask<UpdateCatalogVersionSnapshot?> SelectPackageAsync(VersionManagerState state, ManagedAppVersion version, CancellationToken cancellationToken)
            => ValueTask.FromResult<UpdateCatalogVersionSnapshot?>(Catalog("0.10.7").Versions[0]);
    }
    private sealed class TraceFence(CoordinationStateStore store, LauncherMutationProtection protection) : ILauncherMutationFence
    {
        internal int RetireCount { get; private set; }
        public ValueTask<LauncherMutationProtection> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(store.HoldsWriter);
            store.Trace.Add("fence:load");
            return ValueTask.FromResult(protection);
        }
        public ValueTask<LauncherMutationFenceIssue> RetireLastKnownGoodOwnerAsync(ManagedVersionAdmission expectedOwner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(store.HoldsWriter);
            Assert.Equal(protection.LastKnownGoodOwner, expectedOwner);
            RetireCount++;
            store.Trace.Add($"retire:{expectedOwner.Version}");
            protection = protection with { LastKnownGoodOwner = protection.ActiveOwner };
            return ValueTask.FromResult(LauncherMutationFenceIssue.None);
        }
    }
}
