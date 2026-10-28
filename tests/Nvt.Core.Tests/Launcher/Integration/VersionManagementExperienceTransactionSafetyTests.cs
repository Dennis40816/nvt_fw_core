// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using TransactionRepository = Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport.TransactionRepository;
using VersionDeleteOperationIssue = Nvt.Core.Launcher.Coordination.VersionDeleteOperationIssue;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Tests version management transaction safety.</summary>
public sealed partial class VersionManagementExperienceTransactionSafetyTests
{
    /// <summary>A requested activation blocks install before durable or repository mutation.</summary>
    [Fact]
    public async Task PendingActivationInstallReturnsTypedFailureWithoutMutation()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        ManagedVersionAdmission candidate = Admission("0.10.6");
        var pending = new PendingVersionActivation(
            candidate.Version,
            candidate.AdmissionIdentity,
            active.Version,
            active.Version);
        VersionManagerState initial = VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active, candidate],
            pending,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.7")),
            repository);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);

        VersionInstallOperationResult result = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.7"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, result.Install.Issue);
        Assert.Equal(pending, result.Snapshot.State!.PendingActivation);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, repository.DeleteCalls);
    }

    /// <summary>A requested activation blocks delete before durable or repository mutation.</summary>
    [Fact]
    public async Task PendingActivationDeleteReturnsTypedFailureWithoutMutation()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        ManagedVersionAdmission candidate = Admission("0.10.6");
        ManagedVersionAdmission removable = Admission("0.10.4");
        var pending = new PendingVersionActivation(
            candidate.Version,
            candidate.AdmissionIdentity,
            active.Version,
            active.Version);
        VersionManagerState initial = VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active, candidate, removable],
            pending,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.7")),
            repository);

        VersionDeleteOperationResult result = await experience.DeleteAsync(
            removable.Version,
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.StateUnavailable, result.OperationIssue);
        Assert.Equal(ManagedVersionDeleteBlock.RecoveryRequired, result.Decision.Block);
        Assert.Equal(pending, result.Snapshot.State!.PendingActivation);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, repository.DeleteCalls);
    }

    /// <summary>Recovery of an unrelated install preserves a failed activation quarantine.</summary>
    [Fact]
    public async Task UnrelatedInstallRecoveryPreservesFailedActivationVersion()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        ManagedVersionAdmission failed = Admission("0.10.4");
        VersionManagerState initial = VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active, failed],
            pendingActivation: null,
            failed.Version,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root");
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions);
        using (VersionManagementExperience first = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository))
        {
            _ = await first.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);
            VersionInstallOperationResult interrupted = await first.InstallAsync(
                ManagedAppVersion.Parse("0.10.6"),
                TestContext.Current.CancellationToken);

            Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, interrupted.Install.Issue);
            Assert.Equal(ManagedVersionMutationKind.Install, stateStore.State.PendingMutation?.Kind);
        }

        using VersionManagementExperience restarted = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository);
        VersionManagementSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.Equal(failed.Version, recovered.State.FailedActivationVersion);
        Assert.Equal(failed.Version, stateStore.State.FailedActivationVersion);
        Assert.Equal(1, repository.InstallCalls);
    }

    /// <summary>A successful unrelated install preserves a failed activation quarantine.</summary>
    [Fact]
    public async Task SuccessfulUnrelatedInstallPreservesFailedActivationVersion()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        ManagedVersionAdmission failed = Admission("0.10.4");
        VersionManagerState initial = VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active, failed],
            pendingActivation: null,
            failed.Version,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);

        VersionInstallOperationResult result = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Install.IsSuccess);
        Assert.Equal(failed.Version, result.Snapshot.State!.FailedActivationVersion);
        Assert.Equal(failed.Version, stateStore.State.FailedActivationVersion);
        Assert.Equal(1, repository.InstallCalls);
    }

    /// <summary>Deleting and reinstalling the failed version clears its prior quarantine.</summary>
    [Fact]
    public async Task DeleteThenReinstallFailedVersionLeavesQuarantineCleared()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        ManagedVersionAdmission failed = Admission("0.10.4");
        VersionManagerState initial = VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active, failed],
            pendingActivation: null,
            failed.Version,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.4")),
            repository);

        VersionDeleteOperationResult deleted = await experience.DeleteAsync(
            failed.Version,
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);
        VersionInstallOperationResult reinstalled = await experience.InstallAsync(
            failed.Version,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.None, deleted.OperationIssue);
        Assert.True(reinstalled.Install.IsSuccess);
        Assert.Null(reinstalled.Snapshot.State!.FailedActivationVersion);
        Assert.Null(stateStore.State.FailedActivationVersion);
        Assert.Equal(1, repository.DeleteCalls);
        Assert.Equal(1, repository.InstallCalls);
    }

    /// <summary>A normal delete clears the reminder when only three healthy versions remain.</summary>
    [Fact]
    public async Task SuccessfulDeleteAtHealthyThresholdClearsRetentionReview()
    {
        ManagedVersionAdmission active = Admission("0.10.6");
        ManagedVersionAdmission removable = Admission("0.10.3");
        VersionManagerState initial = VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active, Admission("0.10.5"), Admission("0.10.4"), removable],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: true,
            managedRootIdentity: "managed-root");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new TransactionRepository(initial.Admissions);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.7")),
            repository);

        VersionDeleteOperationResult result = await experience.DeleteAsync(
            removable.Version,
            rollbackLossConfirmed: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionDeleteOperationIssue.None, result.OperationIssue);
        Assert.Equal(1, repository.DeleteCalls);
        Assert.Equal(2, stateStore.SaveCount);
        Assert.Equal(3, result.Snapshot.Inventory.HealthyCount);
        Assert.False(result.Snapshot.State!.RetentionReviewDue);
        Assert.False(stateStore.State.RetentionReviewDue);
    }

    /// <summary>A recovered delete clears the reminder when only three healthy versions remain.</summary>
    [Fact]
    public async Task RecoveredDeleteAtHealthyThresholdClearsRetentionReview()
    {
        ManagedVersionAdmission active = Admission("0.10.6");
        ManagedVersionAdmission removable = Admission("0.10.3");
        VersionManagerState initial = VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active, Admission("0.10.5"), Admission("0.10.4"), removable],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: true,
            managedRootIdentity: "managed-root");
        using var stateStore = new FailingStateStore(initial, failOnSave: 2);
        var repository = new TransactionRepository(initial.Admissions);
        using (VersionManagementExperience first = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.7")),
            repository))
        {
            VersionDeleteOperationResult interrupted = await first.DeleteAsync(
                removable.Version,
                rollbackLossConfirmed: false,
                TestContext.Current.CancellationToken);

            Assert.Equal(VersionDeleteOperationIssue.StateUnavailable, interrupted.OperationIssue);
            Assert.Equal(ManagedVersionMutationKind.Delete, stateStore.State.PendingMutation?.Kind);
            Assert.True(stateStore.State.RetentionReviewDue);
        }

        using VersionManagementExperience restarted = VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.7")),
            repository);
        VersionManagementSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.Equal(3, recovered.Inventory.HealthyCount);
        Assert.False(recovered.State.RetentionReviewDue);
        Assert.False(stateStore.State.RetentionReviewDue);
        Assert.Equal(2, repository.DeleteCalls);
    }
}
