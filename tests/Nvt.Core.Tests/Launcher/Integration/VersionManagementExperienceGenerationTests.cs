// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using TransactionRepository = Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport.TransactionRepository;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Deterministic discovery ownership, cancellation and shared mutation ordering.</summary>
public sealed class VersionManagementExperienceGenerationTests
{
    /// <summary>A late old result cannot replace a newer result or consume its automatic prompt.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewCheckOwnsPublicationAndPromptDespiteLateOldCompletion(bool registry)
    {
        string latest = SourcePath("generation");
        using var store = Store(latest);
        var catalogs = new GatedCatalog(Catalog("0.10.7"), Catalog("0.10.6"));
        using VersionManagementExperience experience = Create(store, catalogs, registry, latest);
        Task<VersionManagementSnapshot> old = experience.CheckAsync(true, TestContext.Current.CancellationToken).AsTask();
        await catalogs.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        VersionManagementSnapshot current = await experience.CheckAsync(true, TestContext.Current.CancellationToken);
        _ = catalogs.Release.TrySetResult();
        VersionManagementSnapshot obsolete = await old;

        Assert.True(current.ShouldPromptForUpdate);
        Assert.Same(current, obsolete);
        Assert.Equal(2, current.Generation);
        Assert.Equal(ManagedAppVersion.Parse("0.10.6"), current.VerifiedCandidate!.Version);
        VersionManagementSnapshot repeated = await experience.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.False(repeated.ShouldPromptForUpdate);
    }

    /// <summary>Caller cancellation after supersession cannot restore an older owned prior observation.</summary>
    [Fact]
    public async Task ObsoleteCallerCancellationDoesNotRestoreItsPriorObservation()
    {
        string latest = SourcePath("cancelled-generation");
        using var store = Store(latest);
        var catalogs = new GatedCatalog(Catalog("0.10.7"), Catalog("0.10.6"), throwAfterRelease: true);
        using VersionManagementExperience experience = Create(store, catalogs, false, latest);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<VersionManagementSnapshot> old = experience.CheckAsync(true, caller.Token).AsTask();
        await catalogs.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        VersionManagementSnapshot current = await experience.CheckAsync(true, TestContext.Current.CancellationToken);

        await caller.CancelAsync();
        _ = catalogs.Release.TrySetResult();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.WaitAsync(TestContext.Current.CancellationToken));
        VersionManagementSnapshot published = await experience.ResumeRegistryAsync(TestContext.Current.CancellationToken);

        Assert.Same(current.Catalog, published.Catalog);
        Assert.Equal(current.VerifiedCandidate, published.VerifiedCandidate);
        Assert.Equal(current.Generation, published.Generation);
        Assert.True(published.ShouldPromptForUpdate);
    }

    /// <summary>Disposal cancels ownership even when an adapter ignores cancellation until completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalRetiresCheckWithoutLatePublication(bool registry)
    {
        string latest = SourcePath("dispose-generation");
        using var store = Store(latest);
        var catalogs = new GatedCatalog(Catalog("0.10.6"), Catalog("0.10.6"));
        using VersionManagementExperience experience = Create(store, catalogs, registry, latest);
        Task<VersionManagementSnapshot> old = experience.CheckAsync(true, TestContext.Current.CancellationToken).AsTask();
        await catalogs.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        experience.Dispose();
        _ = catalogs.Release.TrySetResult();
        if (registry)
        {
            _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => old.WaitAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            VersionManagementSnapshot retired = await old;
            Assert.Null(retired.VerifiedCandidate);
            Assert.False(retired.ShouldPromptForUpdate);
        }
        Assert.Equal(0, store.SaveCount);
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => experience.CheckAsync(
            true, TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>A check queued during install cannot start discovery before the shared coordinator gate opens.</summary>
    [Fact]
    public async Task DiscoveryWaitsForMutationAndSeesItsPublishedSnapshot()
    {
        string latest = SourcePath("mutation-gate");
        using var store = Store(latest);
        var catalogs = new MutableCatalogSource(Catalog("0.10.6"));
        var repository = new GatedInstallRepository(store.State.Admissions);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, catalogs, repository);
        _ = await experience.CheckAsync(false, TestContext.Current.CancellationToken);
        Task<VersionInstallOperationResult> installing = experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"), TestContext.Current.CancellationToken).AsTask();
        await repository.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task<VersionManagementSnapshot> checking = experience.CheckAsync(false, TestContext.Current.CancellationToken).AsTask();

        Assert.False(checking.IsCompleted);
        Assert.Equal(1, catalogs.LoadCount);
        Assert.True(store.HoldsWriter);
        _ = repository.Release.TrySetResult();
        VersionInstallOperationResult installed = await installing;
        VersionManagementSnapshot checkedSnapshot = await checking;

        Assert.True(installed.Install.IsSuccess);
        Assert.Contains(checkedSnapshot.State!.Admissions, row => row.Version == ManagedAppVersion.Parse("0.10.6"));
        Assert.Equal(2, catalogs.LoadCount);
        Assert.Equal(2, store.SaveCount);
        Assert.False(store.HoldsWriter);
    }

    private static MemoryStateStore Store(string source) => new(State(
        [Admission("0.10.5")], "0.10.5", "0.10.5", source));

    /// <summary>Disposal keeps the frozen release exception; cancellation keeps its prepared journal for restart.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MutationDisposalAndCancellationPreserveTheirDurableBoundaries(bool dispose)
    {
        string latest = SourcePath("mutation-interruption");
        using var store = Store(latest);
        var catalog = new FixedCatalogSource(Catalog("0.10.6"));
        var repository = new GatedInstallRepository(store.State.Admissions);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, catalog, repository);
        _ = await experience.CheckAsync(false, TestContext.Current.CancellationToken);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<VersionInstallOperationResult> installing = experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"), caller.Token).AsTask();
        await repository.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        if (dispose) { experience.Dispose(); }
        else { await caller.CancelAsync(); }
        _ = repository.Release.TrySetResult();
        if (dispose)
        {
            _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => installing.WaitAsync(TestContext.Current.CancellationToken));
            Assert.Null(store.State.PendingMutation);
            Assert.Equal(2, store.SaveCount);
        }
        else
        {
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installing.WaitAsync(TestContext.Current.CancellationToken));
            Assert.Equal(ManagedVersionMutationKind.Install, store.State.PendingMutation?.Kind);
            Assert.Equal(1, store.SaveCount);
        }
        Assert.False(store.HoldsWriter);

        using VersionManagementExperience restarted = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, catalog, repository);
        VersionManagementSnapshot recovered = await restarted.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(recovered.State!.PendingMutation);
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(dispose ? 2 : 1, recovered.Inventory.HealthyCount);
    }

    private static VersionManagementExperience Create(MemoryStateStore store, IUpdateCatalogSource catalogs, bool registry, string latest) =>
        VersionManagementExperienceTestFactory.Create(ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            catalogs, new HealthyRepository(), registry ? new SequenceRegistrySource(Registry(
                1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))) : null);

    private sealed class GatedCatalog(UpdateCatalogSnapshot first, UpdateCatalogSnapshot later, bool throwAfterRelease = false)
        : IRootCatalogSourceTestDouble
    {
        // Guard: Interlocked assigns the first invocation; the signals own completion ordering.
        private int _calls;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<UpdateCatalogLoadResult> LoadAsync(string sourceRoot, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) != 1) { return Loaded(later); }
            _ = Started.TrySetResult();
            await Release.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (throwAfterRelease) { cancellationToken.ThrowIfCancellationRequested(); }
            return Loaded(first);
        }
        private static UpdateCatalogLoadResult Loaded(UpdateCatalogSnapshot snapshot) => new(
            snapshot, UpdateCatalogLoadIssue.None, new(1, CatalogContentDigest));
    }

    private sealed class GatedInstallRepository(IReadOnlyList<ManagedVersionAdmission> admissions) : IManagedVersionRepository
    {
        private readonly TransactionRepository _inner = new(admissions);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(string sourceRoot,
            UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) => _inner.VerifyPackageAsync(sourceRoot, package, cancellationToken);
        public async ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot,
            UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken)
        {
            _ = Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await _inner.InstallAsync(managedRoot, sourceRoot, package, cancellationToken);
        }
        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> installed, ManagedAppVersion? activeVersion, ManagedAppVersion? lastKnownGoodVersion,
            ManagedAppVersion? failedActivationVersion, CancellationToken cancellationToken) => _inner.InventoryAsync(
                managedRoot, installed, activeVersion, lastKnownGoodVersion, failedActivationVersion, cancellationToken);
        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string managedRoot, ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion, CancellationToken cancellationToken) => _inner.DeleteAsync(managedRoot, admission, activeVersion, cancellationToken);
    }
}
