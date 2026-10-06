// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Coordination;
using Xunit;
using static Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Characterizes prepared install and delete crash convergence.</summary>
public sealed partial class ManagedMutationCoordinatorTests
{
    /// <summary>A promoted install with a failed commit save is admitted on the next process start.</summary>
    [Fact]
    public async Task InstallCommitSaveFailureConvergesFromDurableJournalAfterRestart()
    {
        string source = Path.GetFullPath("source-root");
        var registryState = new VersionSourceRegistryState(7, Hash, isManualPin: false);
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5",
            source: source,
            sourceRegistryState: registryState);
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions);
        using (ManagedMutationCoordinator first = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository))
        {
            _ = await first.InitializeAsync(TestContext.Current.CancellationToken);
            VersionInstallOperationResult interrupted = await first.InstallAsync(
                ManagedAppVersion.Parse("0.10.6"),
                TestContext.Current.CancellationToken);
            Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, interrupted.Install.Issue);
            Assert.Equal(ManagedVersionMutationKind.Install, stateStore.State.PendingMutation?.Kind);
        }

        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);
        ManagedMutationSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.Contains(recovered.State.Admissions, admission =>
            admission.Version == ManagedAppVersion.Parse("0.10.6"));
        Assert.Equal(registryState, recovered.State.SourceRegistryState);
        Assert.Equal(2, recovered.Inventory.HealthyCount);
    }

    /// <summary>A recovered fourth healthy install retains the same review reminder as a normal install.</summary>
    [Fact]
    public async Task RecoveredFourthHealthyInstallPersistsRetentionReview()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4"), Admission("0.10.3")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions);
        using (ManagedMutationCoordinator first = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository))
        {
            _ = await first.InitializeAsync(TestContext.Current.CancellationToken);
            VersionInstallOperationResult interrupted = await first.InstallAsync(
                ManagedAppVersion.Parse("0.10.6"),
                TestContext.Current.CancellationToken);
            Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, interrupted.Install.Issue);
            Assert.Equal(ManagedVersionMutationKind.Install, stateStore.State.PendingMutation?.Kind);
            Assert.Equal(1, repository.InstallCalls);
            Assert.Equal(0, repository.DeleteCalls);
        }

        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        ManagedMutationSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.Equal(4, recovered.Inventory.HealthyCount);
        Assert.True(recovered.State.RetentionReviewDue);
        Assert.True(stateStore.State.RetentionReviewDue);
        Assert.Equal(1, repository.InstallCalls);
        Assert.Equal(0, repository.DeleteCalls);
    }

    /// <summary>A completed delete with a failed commit save removes its admission after restart.</summary>
    [Fact]
    public async Task DeleteCommitSaveFailureConvergesFromDurableJournalAfterRestart()
    {
        var registryState = new VersionSourceRegistryState(7, Hash, isManualPin: false);
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5",
            source: Path.GetFullPath("source-root"),
            sourceRegistryState: registryState);
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions);
        using (ManagedMutationCoordinator first = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository))
        {
            VersionDeleteOperationResult interrupted = await first.DeleteAsync(
                ManagedAppVersion.Parse("0.10.4"),
                rollbackLossConfirmed: false,
                TestContext.Current.CancellationToken);
            Assert.Equal(VersionDeleteOperationIssue.StateUnavailable, interrupted.OperationIssue);
            Assert.Equal(ManagedVersionMutationKind.Delete, stateStore.State.PendingMutation?.Kind);
        }

        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);
        ManagedMutationSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.DoesNotContain(recovered.State.Admissions, admission =>
            admission.Version == ManagedAppVersion.Parse("0.10.4"));
        Assert.Equal(registryState, recovered.State.SourceRegistryState);
        Assert.Null(recovered.Inventory.Find(ManagedAppVersion.Parse("0.10.4")));
    }

    /// <summary>An unadmitted SemVer directory has a typed recovery block and never reaches admission lookup.</summary>
    [Fact]
    public async Task UnadmittedDirectoryDeleteReturnsRecoveryBlockWithoutCrashing()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        var repository = new TransactionRepository(initial.Admissions, unadmittedVersion: "0.10.4");
        using var stateStore = new MemoryStateStore(initial);
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        VersionDeleteOperationResult result = await experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.4"),
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.PolicyBlocked, result.OperationIssue);
        Assert.Equal(ManagedVersionDeleteBlock.RecoveryRequired, result.Decision.Block);
        Assert.Equal(1, result.Snapshot.Inventory.UnadmittedCount);
    }

    /// <summary>A valid self-admission without the exact pending install remains unadmitted.</summary>
    [Fact]
    public async Task ValidSelfAdmissionWithoutPendingInstallRemainsUnadmitted()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        var repository = new TransactionRepository(
            [.. initial.Admissions, Admission("0.10.6")]);
        using var stateStore = new MemoryStateStore(initial);
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        ManagedMutationSnapshot snapshot = await experience.InitializeAsync(
            TestContext.Current.CancellationToken);

        InstalledVersionSnapshot observed = Assert.IsType<InstalledVersionSnapshot>(
            snapshot.Inventory.Find(ManagedAppVersion.Parse("0.10.6")));
        Assert.Equal(ManagedVersionAdmissionState.Unadmitted, observed.AdmissionState);
        Assert.Equal(Admission("0.10.6"), observed.ObservedAdmission);
        Assert.Null(snapshot.State!.PendingMutation);
    }

    /// <summary>A failed install-prepare save prevents the repository mutation from starting.</summary>
    [Fact]
    public async Task InstallPrepareSaveFailureStartsNoRepositoryMutation()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 1);
        var repository = new TransactionRepository(initial.Admissions);
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        VersionInstallOperationResult result = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, result.Install.Issue);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Null(stateStore.State.PendingMutation);
    }

    /// <summary>A confirmed install from an obsolete source generation fails typed before any mutation.</summary>
    [Fact]
    public async Task SourceChangedAfterConfirmationReturnsPackageUnavailableWithoutMutation()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        stateStore.ReplaceState(State(
            initial.Admissions,
            active: "0.10.5",
            lastKnownGood: "0.10.5",
            source: "other-source"));

        VersionInstallOperationResult result = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, result.Install.Issue);
        Assert.Equal("other-source", result.Snapshot.State!.UpdateSource);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Null(stateStore.State.PendingMutation);
    }

    /// <summary>A newer catalog generation that removes the requested version fails before mutation.</summary>
    [Fact]
    public async Task CurrentCatalogWithoutRequestedVersionReturnsPackageUnavailableWithoutMutation()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        var source = new MutablePackageSelection(Catalog("0.10.6"));
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            source,
            repository);
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        source.Snapshot = Catalog("0.10.7");
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);

        VersionInstallOperationResult result = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, result.Install.Issue);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Null(stateStore.State.PendingMutation);
    }

    /// <summary>An exact admitted retry is idempotent and never opens a second filesystem transaction.</summary>
    [Fact]
    public async Task ExactAlreadyAdmittedInstallReturnsSuccessWithoutMutation()
    {
        TestCatalog catalog = Catalog("0.10.5");
        UpdateCatalogVersionSnapshot package = Assert.Single(catalog.Versions);
        var admitted = new ManagedVersionAdmission(
            package.Version,
            package.Identity,
            package.ReleaseManifestSha256);
        VersionManagerState initial = State(
            [admitted],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(catalog),
            repository);

        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        VersionInstallOperationResult result = await experience.InstallAsync(
            package.Version,
            TestContext.Current.CancellationToken);

        Assert.True(result.Install.IsSuccess);
        Assert.True(result.Install.WasAlreadyInstalled);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Null(result.Snapshot.State!.PendingMutation);
    }

    /// <summary>An admitted version with another catalog identity fails closed without a journal.</summary>
    [Fact]
    public async Task ConflictingAlreadyAdmittedInstallReturnsIdentityConflictWithoutMutation()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.5")),
            repository);

        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        VersionInstallOperationResult result = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.5"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.IdentityConflict, result.Install.Issue);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Null(result.Snapshot.State!.PendingMutation);
    }

    /// <summary>A restart after install prepare but before filesystem mutation clears an absent target.</summary>
    [Fact]
    public async Task PreparedInstallWithoutPayloadClearsOnRestart()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        ManagedVersionAdmission pending = Admission("0.10.6");
        VersionManagerState prepared = initial.WithPendingMutation(
            new(ManagedVersionMutationKind.Install, pending));
        using var stateStore = new MemoryStateStore(prepared);
        var repository = new TransactionRepository(initial.Admissions);
        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        ManagedMutationSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.DoesNotContain(recovered.State.Admissions, item => item.Version == pending.Version);
        Assert.Equal(1, stateStore.SaveCount);
        Assert.Equal(0, repository.InstallCalls);
    }

    /// <summary>A repository failure plus failed journal clear is safely cleared by the next restart.</summary>
    [Fact]
    public async Task FailedInstallClearSaveFailureConvergesAfterRestart()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions)
        {
            InstallIssue = ManagedVersionInstallIssue.PromotionFailed,
        };
        using (ManagedMutationCoordinator first = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository))
        {
            _ = await first.InitializeAsync(TestContext.Current.CancellationToken);
            VersionInstallOperationResult interrupted = await first.InstallAsync(
                ManagedAppVersion.Parse("0.10.6"),
                TestContext.Current.CancellationToken);

            Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, interrupted.Install.Issue);
            Assert.Equal(ManagedVersionMutationKind.Install, stateStore.State.PendingMutation?.Kind);
            Assert.Equal(1, repository.InstallCalls);
        }

        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);
        ManagedMutationSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.DoesNotContain(recovered.State.Admissions, item =>
            item.Version == ManagedAppVersion.Parse("0.10.6"));
    }

    /// <summary>A failed recovery commit remains journaled and the following restart admits the exact payload.</summary>
    [Fact]
    public async Task InstallRecoveryCommitSaveFailureRemainsJournaledUntilNextRestart()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        ManagedVersionAdmission pending = Admission("0.10.6");
        VersionManagerState prepared = initial.WithPendingMutation(
            new(ManagedVersionMutationKind.Install, pending));
        using var stateStore = new FailingStateStore(prepared, failOnSave: 1);
        var repository = new TransactionRepository([.. initial.Admissions, pending]);
        using (ManagedMutationCoordinator firstRestart = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository))
        {
            ManagedMutationSnapshot stillPrepared = await firstRestart.InitializeAsync(
                TestContext.Current.CancellationToken);
            Assert.NotNull(stillPrepared.State!.PendingMutation);
            Assert.DoesNotContain(stillPrepared.State.Admissions, item => item.Version == pending.Version);
        }

        using ManagedMutationCoordinator secondRestart = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);
        ManagedMutationSnapshot recovered = await secondRestart.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.Contains(recovered.State.Admissions, item => item == pending);
    }

    /// <summary>A promoted payload with another identity never self-admits from an install journal.</summary>
    [Fact]
    public async Task InstallRecoveryIdentityMismatchStaysBlocked()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        ManagedVersionAdmission pending = Admission("0.10.6");
        var observed = new ManagedVersionAdmission(
            pending.Version,
            "unexpected-identity",
            new string('b', 64));
        VersionManagerState prepared = initial.WithPendingMutation(
            new(ManagedVersionMutationKind.Install, pending));
        using var stateStore = new MemoryStateStore(prepared);
        var repository = new TransactionRepository([.. initial.Admissions, observed]);
        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        ManagedMutationSnapshot blocked = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(pending, blocked.State!.PendingMutation?.Admission);
        Assert.DoesNotContain(blocked.State.Admissions, item => item.Version == pending.Version);
        Assert.Equal(ManagedVersionAdmissionState.Unadmitted, blocked.Inventory.Find(pending.Version)?.AdmissionState);
        Assert.Equal(0, stateStore.SaveCount);
    }

    /// <summary>A failed delete-prepare save prevents the repository mutation from starting.</summary>
    [Fact]
    public async Task DeletePrepareSaveFailureStartsNoRepositoryMutation()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 1);
        var repository = new TransactionRepository(initial.Admissions);
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        VersionDeleteOperationResult result = await experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.4"),
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.StateUnavailable, result.OperationIssue);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Null(stateStore.State.PendingMutation);
    }

    /// <summary>An initial delete treats an already absent exact target as committed.</summary>
    [Fact]
    public async Task InitialJournaledDeleteCommitsWhenTargetAlreadyDisappeared()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions)
        {
            DeleteIssue = ManagedVersionDeleteIssue.NotInstalled,
        };
        using ManagedMutationCoordinator experience = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        VersionDeleteOperationResult result = await experience.DeleteAsync(
            ManagedAppVersion.Parse("0.10.4"),
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.None, result.OperationIssue);
        Assert.Equal(ManagedVersionDeleteIssue.NotInstalled, result.RepositoryIssue);
        Assert.DoesNotContain(result.Snapshot.State!.Admissions, admission =>
            admission.Version == ManagedAppVersion.Parse("0.10.4"));
        Assert.Null(result.Snapshot.State.PendingMutation);
        Assert.Equal(2, stateStore.SaveCount);
    }

    /// <summary>An absent delete target converges after its commit save fails and the app restarts.</summary>
    [Fact]
    public async Task AlreadyAbsentDeleteCommitSaveFailureConvergesOnRestart()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions)
        {
            DeleteIssue = ManagedVersionDeleteIssue.NotInstalled,
        };
        using (ManagedMutationCoordinator first = ManagedMutationTestFactory.Create(
                   ManagedAppVersion.Parse("0.10.5"),
                   "managed-root",
                   stateStore,
                   new FixedPackageSelection(Catalog("0.10.6")),
                   repository))
        {
            VersionDeleteOperationResult interrupted = await first.DeleteAsync(
                ManagedAppVersion.Parse("0.10.4"),
                rollbackLossConfirmed: false,
                TestContext.Current.CancellationToken);
            Assert.Equal(VersionDeleteOperationIssue.StateUnavailable, interrupted.OperationIssue);
            Assert.Equal(ManagedVersionMutationKind.Delete, stateStore.State.PendingMutation?.Kind);
        }

        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);
        ManagedMutationSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.DoesNotContain(recovered.State.Admissions, admission =>
            admission.Version == ManagedAppVersion.Parse("0.10.4"));
        Assert.Equal(2, repository.DeleteCalls);
    }

}
