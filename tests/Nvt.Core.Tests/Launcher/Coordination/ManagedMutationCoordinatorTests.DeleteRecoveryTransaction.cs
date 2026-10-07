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
    /// <summary>A restart after delete prepare but before mutation performs the exact guarded delete.</summary>
    [Fact]
    public async Task PreparedDeleteExecutesAndCommitsOnRestart()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        ManagedVersionAdmission pending = initial.Admissions.Single(item =>
            item.Version == ManagedAppVersion.Parse("0.10.4"));
        VersionManagerState prepared = initial.WithPendingMutation(
            new(ManagedVersionMutationKind.Delete, pending));
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
        Assert.Equal(1, repository.DeleteCalls);
        Assert.Equal(1, stateStore.SaveCount);
    }

    /// <summary>A delete failure plus failed journal clear retries the exact delete after restart.</summary>
    [Fact]
    public async Task FailedDeleteClearSaveFailureRetriesAndConvergesAfterRestart()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions)
        {
            DeleteIssue = ManagedVersionDeleteIssue.DeleteFailed,
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
            Assert.Equal(1, repository.DeleteCalls);
        }

        repository.DeleteIssue = ManagedVersionDeleteIssue.None;
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
            item.Version == ManagedAppVersion.Parse("0.10.4"));
        Assert.Equal(2, repository.DeleteCalls);
    }

    /// <summary>A failed delete recovery commit stays journaled and converges after another restart.</summary>
    [Fact]
    public async Task DeleteRecoveryCommitSaveFailureRemainsJournaledUntilNextRestart()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        ManagedVersionAdmission pending = initial.Admissions.Single(item =>
            item.Version == ManagedAppVersion.Parse("0.10.4"));
        VersionManagerState prepared = initial.WithPendingMutation(
            new(ManagedVersionMutationKind.Delete, pending));
        using var stateStore = new FailingStateStore(prepared, failOnSave: 1);
        var repository = new TransactionRepository(
            initial.Admissions.Where(item => item.Version != pending.Version));
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
        Assert.DoesNotContain(recovered.State.Admissions, item => item.Version == pending.Version);
        Assert.Equal(2, repository.DeleteCalls);
    }

    /// <summary>A transient recovery delete failure preserves the journal for a later retry.</summary>
    [Fact]
    public async Task DeleteRecoveryFailurePreservesPreparedJournal()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5"), Admission("0.10.4")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        ManagedVersionAdmission pending = initial.Admissions.Single(item =>
            item.Version == ManagedAppVersion.Parse("0.10.4"));
        VersionManagerState prepared = initial.WithPendingMutation(
            new(ManagedVersionMutationKind.Delete, pending));
        using var stateStore = new MemoryStateStore(prepared);
        var repository = new TransactionRepository(initial.Admissions)
        {
            DeleteIssue = ManagedVersionDeleteIssue.DeleteFailed,
        };
        using ManagedMutationCoordinator restarted = ManagedMutationTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new FixedPackageSelection(Catalog("0.10.6")),
            repository);

        ManagedMutationSnapshot blocked = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(pending, blocked.State!.PendingMutation?.Admission);
        Assert.Equal(1, repository.DeleteCalls);
        Assert.Equal(0, stateStore.SaveCount);
    }
}
