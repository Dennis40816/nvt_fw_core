// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using VersionDeleteOperationIssue = Nvt.Core.Launcher.Coordination.VersionDeleteOperationIssue;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Tests the stateful version-management use case rather than only its pure policies.</summary>
public sealed partial class VersionManagementExperienceTests
{
    /// <summary>A newly installed fourth healthy version persists a soft reminder that Keep all can clear.</summary>
    [Fact]
    public async Task SuccessfulFourthInstallPersistsRetentionReviewUntilAcknowledged()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4"), Admission("0.10.3")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository);

        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);
        VersionInstallOperationResult installed = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"),
            TestContext.Current.CancellationToken);

        Assert.True(installed.Install.IsSuccess);
        Assert.Equal(4, installed.Snapshot.Inventory.HealthyCount);
        Assert.True(installed.Snapshot.State!.RetentionReviewDue);
        Assert.True(stateStore.State.RetentionReviewDue);

        VersionManagementSnapshot acknowledged = await experience.AcknowledgeRetentionReviewAsync(
            TestContext.Current.CancellationToken);

        Assert.False(acknowledged.State!.RetentionReviewDue);
        Assert.False(stateStore.State.RetentionReviewDue);
        Assert.Equal(4, acknowledged.Inventory.HealthyCount);
        Assert.Empty(repository.Deleted);
    }

    /// <summary>A failed retention save keeps the durable reminder and returns typed unavailable state.</summary>
    [Fact]
    public async Task RetentionAcknowledgementSaveFailurePreservesReminder()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5")
            .WithRetentionReviewDue(retentionReviewDue: true);
        using var stateStore = new FailingStateStore(initial, failOnSave: 1);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new CountingCatalogSource(),
            new HealthyRepository());

        VersionManagementSnapshot result = await experience.AcknowledgeRetentionReviewAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, result.StateIssue);
        Assert.True(result.State!.RetentionReviewDue);
        Assert.True(stateStore.State.RetentionReviewDue);
        Assert.Equal(1, stateStore.SaveCount);
    }

    /// <summary>Deleting last-known-good revalidates and refuses mutation until the second warning is confirmed.</summary>
    [Fact]
    public async Task LastKnownGoodDeleteRequiresSeparateConfirmedRequest()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.4");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository);
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);

        VersionDeleteOperationResult warning = await experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.4"),
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);

        Assert.True(warning.Decision.RequiresRollbackLossWarning);
        Assert.Equal(VersionDeleteOperationIssue.RollbackConfirmationRequired, warning.OperationIssue);
        Assert.Null(warning.RepositoryIssue);
        Assert.Empty(repository.Deleted);
        Assert.Equal(ManagedAppVersion.Parse("0.10.4"), stateStore.State.LastKnownGoodVersion);

        VersionDeleteOperationResult deleted = await experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.4"),
            rollbackLossConfirmed: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.None, deleted.OperationIssue);
        Assert.Equal(ManagedVersionDeleteIssue.None, deleted.RepositoryIssue);
        Assert.Equal([ManagedAppVersion.Parse("0.10.4")], repository.Deleted);
        Assert.Null(deleted.Snapshot.State!.LastKnownGoodVersion);
        Assert.Null(stateStore.State.LastKnownGoodVersion);
    }

    /// <summary>Permission failure remains distinct from an absent or offline source.</summary>
    [Fact]
    public async Task PermissionDeniedSourcePublishesTypedVisibleStatus()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var inlineStore1 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore1,
            new IssueCatalogSource(UpdateCatalogLoadIssue.PermissionDenied),
            new HealthyRepository());

        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        VersionManagementSnapshot checkedSnapshot = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceStatus.PermissionDenied, checkedSnapshot.SourceStatus);
        Assert.Equal(UpdateCatalogLoadIssue.PermissionDenied, checkedSnapshot.CatalogIssue);
        Assert.Null(checkedSnapshot.Catalog);
    }

    /// <summary>Every source failure maps to a stable visible status without throwing.</summary>
    [Theory]
    [InlineData((int)UpdateCatalogLoadIssue.SourceMissing, VersionSourceStatus.Offline)]
    [InlineData((int)UpdateCatalogLoadIssue.SourceUnavailable, VersionSourceStatus.Offline)]
    [InlineData((int)UpdateCatalogLoadIssue.PermissionDenied, VersionSourceStatus.PermissionDenied)]
    [InlineData((int)UpdateCatalogLoadIssue.UnsafeSource, VersionSourceStatus.Invalid)]
    [InlineData((int)UpdateCatalogLoadIssue.CatalogTooLarge, VersionSourceStatus.Invalid)]
    [InlineData((int)UpdateCatalogLoadIssue.InvalidManifest, VersionSourceStatus.Invalid)]
    [InlineData((int)UpdateCatalogLoadIssue.UnstableRead, VersionSourceStatus.Invalid)]
    [InlineData((int)UpdateCatalogLoadIssue.None, VersionSourceStatus.Invalid)]
    [InlineData(999, VersionSourceStatus.Invalid)]
    public async Task SourceIssuesMapToStableVisibleStatus(
        int issueCode,
        VersionSourceStatus expected)
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var inlineStore2 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore2,
            new IssueCatalogSource((UpdateCatalogLoadIssue)issueCode),
            new HealthyRepository());

        VersionManagementSnapshot snapshot = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, snapshot.SourceStatus);
        Assert.Equal((UpdateCatalogLoadIssue)issueCode, snapshot.CatalogIssue);
    }

    /// <summary>A newer catalog entry remains invalid until complete package verification succeeds.</summary>
    [Fact]
    public async Task UnverifiedNewerPackagePublishesInvalidStatus()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var inlineStore3 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore3,
            new FixedCatalogSource(Catalog("0.10.6")),
            new HealthyRepository(verifyPackages: false));

        VersionManagementSnapshot snapshot = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceStatus.Invalid, snapshot.SourceStatus);
        Assert.Null(snapshot.VerifiedCandidate);
        Assert.False(snapshot.ShouldPromptForUpdate);
    }

    /// <summary>An automatic all-manual Catalog performs zero package I/O and publishes no policy-bearing row.</summary>
    [Fact]
    public async Task AutomaticAllManualCatalogSkipsPackageAndPublishesExactConnectedSnapshot()
    {
        var repository = new HealthyRepository();
        using var inlineStore4 = new MemoryStateStore(State([Admission("1.0.6")], "1.0.6", "1.0.6"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            inlineStore4,
            new FixedCatalogSource(CatalogV2(("1.0.8", "manual-only"))),
            repository);

        VersionManagementSnapshot snapshot = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, repository.VerifyCount);
        Assert.Null(snapshot.Catalog);
        Assert.Null(snapshot.VerifiedCandidate);
        Assert.Equal(VersionSourceStatus.Connected, snapshot.SourceStatus);
        Assert.Equal(UpdateCatalogLoadIssue.None, snapshot.CatalogIssue);
        Assert.False(snapshot.ShouldPromptForUpdate);
    }

    /// <summary>Automatic checks verify and expose only the newest eligible notify row.</summary>
    [Fact]
    public async Task AutomaticMixedCatalogVerifiesAndExposesOnlyNewestNotifyRow()
    {
        var repository = new HealthyRepository();
        using var inlineStore5 = new MemoryStateStore(State([Admission("1.0.6")], "1.0.6", "1.0.6"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            inlineStore5,
            new FixedCatalogSource(CatalogV2(
                ("1.0.8", "manual-only"),
                ("1.0.7", "notify"))),
            repository);

        VersionManagementSnapshot snapshot = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, repository.VerifyCount);
        Assert.Equal("1.0.7", snapshot.VerifiedCandidate!.Version.ToString());
        UpdateCatalogVersionSnapshot visible = Assert.Single(snapshot.Catalog!.Versions);
        Assert.Equal("1.0.7", visible.Version.ToString());
        Assert.Equal(UpdateNotificationPolicy.Notify, visible.NotificationPolicy);
        Assert.DoesNotContain(snapshot.Catalog.Versions, entry => entry.Version.ToString() == "1.0.8");
    }

    /// <summary>An explicit check preserves all admitted rows including manual-only entries.</summary>
    [Fact]
    public async Task ExplicitMixedCatalogExposesAllRowsAndSelectsNewestRegardlessOfPolicy()
    {
        var repository = new HealthyRepository();
        using var inlineStore6 = new MemoryStateStore(State([Admission("1.0.6")], "1.0.6", "1.0.6"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            inlineStore6,
            new FixedCatalogSource(CatalogV2(
                ("1.0.8", "manual-only"),
                ("1.0.7", "notify"))),
            repository);

        VersionManagementSnapshot snapshot = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, repository.VerifyCount);
        Assert.Equal("1.0.8", snapshot.VerifiedCandidate!.Version.ToString());
        Assert.Equal(["1.0.8", "1.0.7"], snapshot.Catalog!.Versions.Select(entry => entry.Version.ToString()));
        Assert.False(snapshot.ShouldPromptForUpdate);
    }

    /// <summary>Install consumes the checked Catalog snapshot and never rereads corrected policy authority.</summary>
    [Fact]
    public async Task InstallUsesCheckedV2SnapshotWithoutCatalogReread()
    {
        UpdateCatalogSnapshot checkedCatalog = CatalogV2(("1.0.8", "notify"));
        var source = new MutableCatalogSource(checkedCatalog);
        using var inlineStore7 = new MemoryStateStore(State([Admission("1.0.6")], "1.0.6", "1.0.6"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            inlineStore7,
            source,
            new HealthyRepository());

        _ = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);
        source.Snapshot = CatalogV2(("1.0.8", "manual-only"));

        VersionInstallOperationResult result = await experience.InstallAsync(
            ManagedAppVersion.Parse("1.0.8"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Install.IsSuccess);
        Assert.Equal(1, source.LoadCount);
        Assert.Equal(
            Assert.Single(checkedCatalog.Versions).Identity,
            result.Install.Admission!.AdmissionIdentity);
    }

    /// <summary>No configured source short-circuits discovery and remains explicitly NotConfigured.</summary>
    [Fact]
    public async Task MissingConfiguredSourceSkipsCatalogRead()
    {
        ManagedAppVersion version = ManagedAppVersion.Parse("0.10.5");
        VersionManagerState initial = VersionManagerState.Create(
            null,
            version,
            version,
            [Admission("0.10.5")],
            null,
            null,
            false,
            managedRootIdentity: "managed-root");
        var source = new CountingCatalogSource();
        using var inlineStore8 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            version,
            "managed-root",
            inlineStore8,
            source,
            new HealthyRepository());

        VersionManagementSnapshot snapshot = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceStatus.NotConfigured, snapshot.SourceStatus);
        Assert.Equal(0, source.LoadCount);
    }

    /// <summary>Delete adapter failure preserves admission and returns a typed repository issue.</summary>
    [Fact]
    public async Task RepositoryDeleteFailurePreservesInstalledState()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        var repository = new HealthyRepository(deleteIssue: ManagedVersionDeleteIssue.DeleteFailed);
        using var inlineStore9 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore9,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository);

        VersionDeleteOperationResult result = await experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.4"),
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.RepositoryFailure, result.OperationIssue);
        Assert.Equal(ManagedVersionDeleteIssue.DeleteFailed, result.RepositoryIssue);
        Assert.Contains(result.Snapshot.State!.Admissions, item => item.Version == ManagedAppVersion.Parse("0.10.4"));
    }

    /// <summary>Disposal is idempotent and all later use is rejected.</summary>
    [Fact]
    public async Task DisposeIsIdempotentAndRejectsLaterUse()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var inlineStore10 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore10,
            new FixedCatalogSource(Catalog("0.10.6")),
            new HealthyRepository());

        experience.Dispose();
        experience.Dispose();

        _ = await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await experience.InitializeAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Confirming a new source cancels an old check before it can republish the old path.</summary>
    [Fact]
    public async Task CommittingNewSourceSupersedesInFlightOldSourceCheck()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        var source = new SupersedingCatalogSource(Catalog("0.10.7"));
        using var inlineStore11 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore11,
            source,
            new HealthyRepository());
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);

        Task<VersionManagementSnapshot> staleCheck = experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken).AsTask();
        _ = await source.FirstCheckStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        VersionManagementSnapshot committed = await experience.CommitUpdateSourceAsync(
            "new-source-root",
            TestContext.Current.CancellationToken);
        _ = await staleCheck;

        Assert.True(source.FirstCheckWasCancelled);
        Assert.Equal("new-source-root", committed.State!.UpdateSource);
        Assert.Equal(ManagedAppVersion.Parse("0.10.7"), Assert.Single(committed.Catalog!.Versions).Version);
        Assert.Equal(VersionSourceStatus.Connected, committed.SourceStatus);
    }

    /// <summary>A failed durable source commit keeps the prior source and never probes the candidate.</summary>
    [Fact]
    public async Task CommitSourceSaveFailurePreservesCommittedSourceAndSkipsCatalog()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 1);
        var source = new CountingCatalogSource();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            source,
            new HealthyRepository());

        VersionManagementSnapshot result = await experience.CommitUpdateSourceAsync(
            "candidate-source-root",
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, result.StateIssue);
        Assert.Equal("source-root", result.State!.UpdateSource);
        Assert.Equal("source-root", stateStore.State.UpdateSource);
        Assert.Equal(1, stateStore.SaveCount);
        Assert.Equal(0, source.LoadCount);
    }

    /// <summary>An interrupted refresh restores the last completed source result instead of remaining Checking.</summary>
    [Theory]
    [InlineData(VersionSourceStatus.Connected, true, (int)UpdateCatalogLoadIssue.None)]
    [InlineData(VersionSourceStatus.Invalid, false, (int)UpdateCatalogLoadIssue.None)]
    [InlineData(VersionSourceStatus.PermissionDenied, true, (int)UpdateCatalogLoadIssue.PermissionDenied)]
    [InlineData(VersionSourceStatus.Offline, true, (int)UpdateCatalogLoadIssue.SourceMissing)]
    [InlineData(VersionSourceStatus.Offline, true, (int)UpdateCatalogLoadIssue.SourceUnavailable)]
    [InlineData(VersionSourceStatus.Invalid, true, (int)UpdateCatalogLoadIssue.UnsafeSource)]
    [InlineData(VersionSourceStatus.Invalid, true, (int)UpdateCatalogLoadIssue.CatalogTooLarge)]
    [InlineData(VersionSourceStatus.Invalid, true, (int)UpdateCatalogLoadIssue.InvalidManifest)]
    [InlineData(VersionSourceStatus.Invalid, true, (int)UpdateCatalogLoadIssue.UnstableRead)]
    [InlineData(VersionSourceStatus.Invalid, true, 999)]
    public async Task MutationInterruptingRefreshRestoresPriorSourceStatus(
        VersionSourceStatus expected,
        bool verifyPackages,
        int issueCode)
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        UpdateCatalogLoadIssue issue = (UpdateCatalogLoadIssue)issueCode;
        UpdateCatalogLoadResult firstResult = issue == UpdateCatalogLoadIssue.None
            ? new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)
            : new(null, issue);
        var source = new CompletedThenBlockingCatalogSource(firstResult);
        using var inlineStore12 = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore12,
            source,
            new HealthyRepository(verifyPackages));

        VersionManagementSnapshot completed = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, completed.SourceStatus);

        Task<VersionManagementSnapshot> interruptedCheck = experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken).AsTask();
        _ = await source.SecondCheckStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        VersionManagementSnapshot restored = await experience.AcknowledgeRetentionReviewAsync(
            TestContext.Current.CancellationToken);
        VersionManagementSnapshot interruptedResult = await interruptedCheck;

        Assert.Equal(expected, restored.SourceStatus);
        Assert.Equal(expected, interruptedResult.SourceStatus);
        Assert.True(source.SecondCheckWasCancelled);
    }

    private sealed class IssueCatalogSource(UpdateCatalogLoadIssue issue) : IRootCatalogSourceTestDouble
    {
        public ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new UpdateCatalogLoadResult(null, issue));
        }
    }

    private sealed class CountingCatalogSource : IRootCatalogSourceTestDouble
    {
        // Guard: the owning test awaits each check before reading the count or checking again.
        internal int LoadCount { get; private set; }

        public ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            LoadCount++;
            throw new InvalidOperationException("A source-free check must not read the catalog.");
        }
    }

    private sealed class SupersedingCatalogSource(UpdateCatalogSnapshot newSourceSnapshot)
        : IRootCatalogSourceTestDouble
    {
        // Guard: Interlocked/Volatile publishes cancellation to the owning test.
        private int _firstCheckWasCancelled;

        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource<bool> FirstCheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool FirstCheckWasCancelled => Volatile.Read(ref _firstCheckWasCancelled) != 0;

        public async ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            if (string.Equals(sourceRoot, "source-root", StringComparison.Ordinal))
            {
                _ = FirstCheckStarted.TrySetResult(true);
                try
                {
                    await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _ = Interlocked.Exchange(ref _firstCheckWasCancelled, 1);
                    throw;
                }
            }
            return new(newSourceSnapshot, UpdateCatalogLoadIssue.None);
        }
    }

    private sealed class CompletedThenBlockingCatalogSource(UpdateCatalogLoadResult firstResult)
        : IRootCatalogSourceTestDouble
    {
        // Guard: Interlocked selects the first load across concurrent checks.
        private int _loadCount;
        // Guard: Interlocked/Volatile publishes cancellation to the owning test.
        private int _secondCheckWasCancelled;

        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource<bool> SecondCheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool SecondCheckWasCancelled => Volatile.Read(ref _secondCheckWasCancelled) != 0;

        public async ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _loadCount) == 1)
            {
                return firstResult;
            }

            _ = SecondCheckStarted.TrySetResult(true);
            try
            {
                await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _ = Interlocked.Exchange(ref _secondCheckWasCancelled, 1);
                throw;
            }

            throw new InvalidOperationException("The blocking catalog check must be cancelled by the mutation.");
        }
    }
}
