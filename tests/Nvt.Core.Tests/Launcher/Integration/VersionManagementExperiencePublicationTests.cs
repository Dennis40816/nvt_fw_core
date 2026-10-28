// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.Launcher.Persistence;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using VersionDeleteOperationResult = Nvt.Core.Launcher.Integration.VersionDeleteOperationResult;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Checks mutation publication against the observations made by each operation.</summary>
public sealed class VersionManagementExperiencePublicationTests
{
    /// <summary>A rejected delete publishes the damaged inventory observed by its second read.</summary>
    [Fact]
    public async Task RejectedDeletePublishesItsSecondInventoryRead()
    {
        using var store = new MemoryStateStore(Initial());
        var repository = new DamageOnSecondInventoryRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new FixedCatalogSource(Catalog("0.10.6")), repository);

        VersionDeleteOperationResult deleted = await experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.5"), false, TestContext.Current.CancellationToken);
        VersionManagementSnapshot checkedSnapshot = await experience.CheckAsync(
            false, TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.PolicyBlocked, deleted.OperationIssue);
        Assert.Equal(ManagedVersionDeleteBlock.ActiveVersion, deleted.Decision.Block);
        Assert.Equal(ManagedVersionIntegrity.Damaged, Assert.Single(deleted.Snapshot.Inventory.Versions).Integrity);
        Assert.Same(deleted.Snapshot.Inventory, checkedSnapshot.Inventory);
        Assert.Equal(ManagedVersionIntegrity.Damaged, Assert.Single(checkedSnapshot.Inventory.Versions).Integrity);
        Assert.Equal(2, repository.InventoryCount);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Operations that cannot reload must retain an earlier failed registry commit's unavailable state.</summary>
    [Theory]
    [InlineData("initialize")]
    [InlineData("delete")]
    [InlineData("install")]
    [InlineData("retention")]
    [InlineData("activation")]
    [InlineData("cancel")]
    public async Task BusyOperationAfterRegistrySaveFailureKeepsUnavailableState(string operation)
    {
        string latest = SourcePath("publication-save-failure");
        using var store = new FailingStateStore(Initial(), failOnSave: 1);
        var registry = new SequenceRegistrySource(
            Registry(1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest)));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(), registry);
        VersionManagementSnapshot failed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, failed.StateIssue);
        Assert.Equal(ManagedVersionInventoryReadIssue.Unavailable, failed.InventoryIssue);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, failed.RegistryIssue);
        using VersionManagerWriteLeaseResult writer = await store.TryAcquireWriteLeaseAsync(
            TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.True(writer.IsAcquired);

        await RunBusyOperationAsync(experience, operation, TestContext.Current.CancellationToken);
        VersionManagementSnapshot checkedSnapshot = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, checkedSnapshot.StateIssue);
        Assert.Equal(ManagedVersionInventoryReadIssue.Unavailable, checkedSnapshot.InventoryIssue);
        Assert.Equal(VersionRegistryStatus.Unavailable, checkedSnapshot.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, checkedSnapshot.RegistryIssue);
        Assert.Null(checkedSnapshot.Catalog);
        Assert.Null(checkedSnapshot.VerifiedCandidate);
        Assert.Equal(2, registry.LoadCount);
        Assert.Equal(2, store.LoadCount);
        Assert.Equal(1, store.SaveCount);
    }

    /// <summary>Initialization preserves the prior captured before I/O even if discovery completes during that I/O.</summary>
    [Fact]
    public async Task ReloadUsesPriorCapturedBeforeConcurrentCheckCompletion()
    {
        using var store = new GatedReloadStore(Initial());
        var catalogs = new GatedCheckCatalog(Catalog("0.10.6"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, catalogs, new HealthyRepository());
        Task<VersionManagementSnapshot> checking = experience.CheckAsync(true, TestContext.Current.CancellationToken).AsTask();
        await catalogs.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task<VersionManagementSnapshot> initializing = experience.InitializeAsync(
            TestContext.Current.CancellationToken, isReadOnly: true).AsTask();
        try
        {
            await store.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            _ = catalogs.Release.TrySetResult();
            VersionManagementSnapshot completed = await checking;
            Assert.NotNull(completed.Catalog);
            Assert.NotNull(completed.VerifiedCandidate);
            Assert.True(completed.ShouldPromptForUpdate);
        }
        finally
        {
            _ = catalogs.Release.TrySetResult();
            _ = store.Release.TrySetResult();
        }
        VersionManagementSnapshot reloaded = await initializing;

        // The frozen reload captures the in-flight check's prior, not its later completed result.
        Assert.Null(reloaded.Catalog);
        Assert.Null(reloaded.VerifiedCandidate);
        Assert.Equal(VersionSourceStatus.Checking, reloaded.SourceStatus);
        Assert.Equal(1, reloaded.Generation);
        Assert.False(reloaded.ShouldPromptForUpdate);
        VersionManagementSnapshot repeated = await experience.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.NotNull(repeated.VerifiedCandidate);
        Assert.False(repeated.ShouldPromptForUpdate);
        Assert.Equal(2, repeated.Generation);
    }

    /// <summary>A failure before the first snapshot is available survives release and the gate can be acquired again.</summary>
    [Fact]
    public async Task FailureWithoutPublishedSnapshotPreservesOriginalExceptionAndReleasesGate()
    {
        var expected = new IOException("Injected reload failure before publication.");
        using var store = new MemoryStateStore(Initial())
        {
            BeforeLoad = () => throw expected,
        };
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new FixedCatalogSource(Catalog("0.10.6")), new HealthyRepository());

        IOException observed = await Assert.ThrowsAsync<IOException>(() => experience.PrepareActivationAsync(
            ManagedAppVersion.Parse("0.10.5"), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, observed);
        Assert.False(store.HoldsWriter);
        store.BeforeLoad = null;
        VersionManagementSnapshot recovered = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VersionManagerStateLoadIssue.None, recovered.StateIssue);
        Assert.Single(recovered.Inventory.Versions);
    }

    /// <summary>A reload failure after initialization reaches the release hook and still surfaces unchanged.</summary>
    [Fact]
    public async Task ReloadFailureAfterInitializationPreservesOriginalExceptionAndReleasesGate()
    {
        var expected = new IOException("Injected reload failure after initialization.");
        using var store = new MemoryStateStore(Initial());
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new FixedCatalogSource(Catalog("0.10.6")), new HealthyRepository());
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        store.BeforeLoad = () => throw expected;

        IOException observed = await Assert.ThrowsAsync<IOException>(() => experience.PrepareActivationAsync(
            ManagedAppVersion.Parse("0.10.5"), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, observed);
        Assert.False(store.HoldsWriter);
        store.BeforeLoad = null;
        VersionManagementSnapshot recovered = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VersionManagerStateLoadIssue.None, recovered.StateIssue);
    }

    /// <summary>A repository failure after the observed reload projects its snapshot and still surfaces unchanged.</summary>
    [Fact]
    public async Task FailureAfterObservedReloadPreservesOriginalExceptionAndReleasesGate()
    {
        var expected = new IOException("Injected delete failure after the reload.");
        using var store = new MemoryStateStore(State([Admission("0.10.4"), Admission("0.10.5")], "0.10.5", "0.10.5"));
        var repository = new ThrowingDeleteRepository(expected);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new FixedCatalogSource(Catalog("0.10.6")), repository);
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);

        IOException observed = await Assert.ThrowsAsync<IOException>(() => experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.4"), false, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, observed);
        Assert.Equal(1, repository.DeleteCount);
        Assert.False(store.HoldsWriter);
        VersionManagementSnapshot recovered = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VersionManagerStateLoadIssue.None, recovered.StateIssue);
        Assert.Equal(2, repository.DeleteCount);
        Assert.Equal(ManagedAppVersion.Parse("0.10.5"), Assert.Single(recovered.Inventory.Versions).Version);
    }

    private static VersionManagerState Initial() => State([Admission("0.10.5")], "0.10.5", "0.10.5");

    private static async Task RunBusyOperationAsync(
        VersionManagementExperience experience, string operation, CancellationToken cancellationToken)
    {
        ManagedAppVersion version = ManagedAppVersion.Parse("0.10.6");
        switch (operation)
        {
            case "initialize":
                _ = await experience.InitializeAsync(cancellationToken);
                break;
            case "delete":
                _ = await experience.DeleteAsync(version, false, cancellationToken);
                break;
            case "install":
                _ = await experience.InstallAsync(version, cancellationToken);
                break;
            case "retention":
                _ = await experience.AcknowledgeRetentionReviewAsync(cancellationToken);
                break;
            case "activation":
                InvalidOperationException activation = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => experience.PrepareActivationAsync(version, cancellationToken).AsTask());
                Assert.Equal("Another version-management writer is active.", activation.Message);
                break;
            case "cancel":
                InvalidOperationException cancel = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => experience.CancelPendingActivationAsync(cancellationToken).AsTask());
                Assert.Equal("Another version-management writer is active.", cancel.Message);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private sealed class GatedReloadStore(VersionManagerState state) : MemoryStateStore(state)
    {
        // Guard: Interlocked selects the gated reload; Started/Release order the test and coordinator.
        private int _loads;
        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _loads) == 2)
            {
                _ = Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await base.LoadAsync(cancellationToken);
        }
    }

    private sealed class GatedCheckCatalog(UpdateCatalogSnapshot catalog) : IRootCatalogSourceTestDouble
    {
        // Guard: Interlocked selects the first check; Started/Release order its completion.
        private int _loads;
        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<UpdateCatalogLoadResult> LoadAsync(string sourceRoot, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _loads) == 1)
            {
                _ = Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return new(catalog, UpdateCatalogLoadIssue.None, new(1, CatalogContentDigest));
        }
    }

    private sealed class ThrowingDeleteRepository(Exception failure) : IManagedVersionRepository
    {
        // Guard: the experience's coordinator serializes calls to the mutable repository double.
        private readonly HealthyRepository _inner = new();
        // Guard: Interlocked/Volatile protects the delete count and its observation by the test.
        private int _deletes;
        internal int DeleteCount => Volatile.Read(ref _deletes);

        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) =>
            _inner.VerifyPackageAsync(sourceRoot, package, cancellationToken);

        public ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot,
            UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) =>
            _inner.InstallAsync(managedRoot, sourceRoot, package, cancellationToken);

        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions, ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion, ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken) =>
            _inner.InventoryAsync(
                managedRoot, admissions, activeVersion, lastKnownGoodVersion, failedActivationVersion, cancellationToken);

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string managedRoot,
            ManagedVersionAdmission admission, ManagedAppVersion? activeVersion, CancellationToken cancellationToken)
        {
            // Only the first call fails: the journalled delete is retried by the recovery that follows.
            return Interlocked.Increment(ref _deletes) == 1
                ? throw failure
                : ValueTask.FromResult(ManagedVersionDeleteIssue.None);
        }
    }

    private sealed class DamageOnSecondInventoryRepository : IManagedVersionRepository
    {
        // Guard: the experience's coordinator serializes calls to the mutable repository double.
        private readonly HealthyRepository _inner = new();
        // Guard: Interlocked/Volatile protects the inventory sequence and its observation by the test.
        private int _reads;
        internal int InventoryCount => Volatile.Read(ref _reads);

        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) =>
            _inner.VerifyPackageAsync(sourceRoot, package, cancellationToken);

        public ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot,
            UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) =>
            _inner.InstallAsync(managedRoot, sourceRoot, package, cancellationToken);

        public async ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions, ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion, ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken)
        {
            bool damaged = Interlocked.Increment(ref _reads) >= 2;
            ManagedVersionInventoryReadResult observed = await _inner.InventoryAsync(
                managedRoot, admissions, activeVersion, lastKnownGoodVersion, failedActivationVersion, cancellationToken);
            return ManagedVersionInventoryReadResult.Success(ManagedVersionInventory.Create(
                observed.Inventory!.Versions.Select(row => damaged ? row with
                {
                    Integrity = ManagedVersionIntegrity.Damaged,
                    DamageReason = ManagedVersionDamageReason.ContentMismatch,
                } : row)));
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string managedRoot,
            ManagedVersionAdmission admission, ManagedAppVersion? activeVersion, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(managedRoot, admission, activeVersion, cancellationToken);
    }
}
