// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using static Nvt.Core.Tests.Launcher.Activation.ActivationFixture;

namespace Nvt.Core.Tests.Launcher.Activation;

/// <summary>Characterizes generic inventory invariants, exact delete-owner protection and repository result ports.</summary>
public sealed class InventoryAndPortTests
{
    /// <summary>Inventory copies inputs, sorts newest first and counts only committed healthy or damaged admissions.</summary>
    [Fact]
    public void InventoryOrderingCountsAndInputSnapshotArePreserved()
    {
        InstalledVersionSnapshot[] rows =
        [
            Row(Older), Row(Active, ManagedVersionIntegrity.Damaged, active: true),
            Row(Candidate, admissionState: ManagedVersionAdmissionState.RecoveryCandidate),
        ];
        ManagedVersionInventory inventory = ManagedVersionInventory.Create(rows);
        rows[0] = Row(ManagedAppVersion.Parse("9.0.0"));
        Assert.Collection(inventory.Versions, row => Assert.Equal(Candidate, row.Version),
            row => Assert.Equal(Active, row.Version), row => Assert.Equal(Older, row.Version));
        Assert.Equal(1, inventory.HealthyCount);
        Assert.Equal(1, inventory.DamagedCount);
        Assert.Equal(1, inventory.UnadmittedCount);
        Assert.NotNull(inventory.Find(Older));
        Assert.Null(inventory.Find(ManagedAppVersion.Parse("9.0.0")));
        Assert.Empty(ManagedVersionInventory.Create([]).Versions);
    }

    /// <summary>The one-active-row ceiling accepts zero and one, and rejects two after duplicate detection.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InventoryActiveCountBoundaryIsPreserved(int activeCount)
    {
        InstalledVersionSnapshot[] rows = [Row(Older, active: activeCount > 0), Row(Active, active: activeCount > 1)];
        if (activeCount <= 1)
        {
            Assert.Equal(2, ManagedVersionInventory.Create(rows).Versions.Count);
        }
        else
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() => ManagedVersionInventory.Create(rows));
            Assert.StartsWith("Installed inventory can contain at most one active version.", error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Duplicate and inconsistent rows fail in the frozen predicate order.</summary>
    [Fact]
    public void InventoryRejectsDuplicateAndInconsistentRows()
    {
        Assert.Throws<ArgumentNullException>(() => ManagedVersionInventory.Create(null!));
        ArgumentException duplicate = Assert.Throws<ArgumentException>(() =>
            ManagedVersionInventory.Create([Row(Older, active: true), Row(Older, active: true)]));
        Assert.StartsWith("Installed inventory versions must be unique.", duplicate.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            ManagedVersionInventory.Create([Row(Older) with { AdmissionIdentity = " " }]));
        Assert.Throws<ArgumentException>(() =>
            ManagedVersionInventory.Create([Row(Older) with { DamageReason = ManagedVersionDamageReason.MissingFile }]));
        Assert.Throws<ArgumentException>(() => ManagedVersionInventory.Create(
            [Row(Older, ManagedVersionIntegrity.Damaged) with { DamageReason = null }]));
        Assert.Throws<ArgumentException>(() => ManagedVersionInventory.Create(
            [Row(Older, admissionState: ManagedVersionAdmissionState.RecoveryCandidate) with { ObservedAdmission = null }]));
    }

    /// <summary>Generic delete decisions preserve active protection and rollback-loss information without giving consent.</summary>
    [Fact]
    public void DeleteDecisionProtectsActiveAndWarnsForLastKnownGood()
    {
        ManagedVersionInventory inventory = ManagedVersionInventory.Create(
            [Row(Active, active: true), Row(Older, lastKnownGood: true), Row(Candidate, ManagedVersionIntegrity.Damaged)]);
        ManagedVersionDeleteDecision active = VersionManagementPolicy.DecideDelete(inventory, Active);
        ManagedVersionDeleteDecision lastKnownGood = VersionManagementPolicy.DecideDelete(inventory, Older);
        ManagedVersionDeleteDecision damaged = VersionManagementPolicy.DecideDelete(inventory, Candidate);
        Assert.Equal(ManagedVersionDeleteBlock.ActiveVersion, active.Block);
        Assert.False(active.IsAllowed);
        Assert.True(lastKnownGood.IsAllowed);
        Assert.True(lastKnownGood.RequiresRollbackLossWarning);
        Assert.True(damaged.IsAllowed);
        Assert.Equal(ManagedVersionDeleteBlock.NotInstalled,
            VersionManagementPolicy.DecideDelete(inventory, ManagedAppVersion.Parse("9.0.0")).Block);
        Assert.Throws<ArgumentNullException>(() => VersionManagementPolicy.DecideDelete(null!, Older));
        Assert.Throws<ArgumentNullException>(() => VersionManagementPolicy.DecideDelete(inventory, Older, null!, null));
    }

    /// <summary>Unreadable journals and every recorded activation fence take priority over owner and inventory checks.</summary>
    [Theory]
    [InlineData(LauncherMutationFenceIssue.None, true)]
    [InlineData(LauncherMutationFenceIssue.Invalid, false)]
    [InlineData(LauncherMutationFenceIssue.Unavailable, false)]
    [InlineData((LauncherMutationFenceIssue)99, false)]
    public void LauncherFencePrecedesAllOtherDeleteChecks(LauncherMutationFenceIssue issue, bool hasActivation)
    {
        var protection = new LauncherMutationProtection(issue, hasActivation,
            Admission(Active), Admission(Older), []);
        Assert.False(protection.IsClear);
        ManagedVersionDeleteDecision decision = VersionManagementPolicy.DecideDelete(
            ManagedVersionInventory.Create([]), Active, protection, Admission(Active));
        Assert.Equal(ManagedVersionDeleteBlock.LauncherActivationPending, decision.Block);
        Assert.False(decision.RequiresRollbackLossWarning);
    }

    /// <summary>Hard protection compares complete admission records and distinguishes fallback-only authority.</summary>
    [Fact]
    public void DeleteOwnerProtectionUsesExactAdmissions()
    {
        var protection = new LauncherMutationProtection(LauncherMutationFenceIssue.None, false,
            Admission(Active), Admission(Older), [Admission(Candidate)]);
        ManagedVersionInventory inventory = ManagedVersionInventory.Create([Row(Older), Row(Active), Row(Candidate)]);
        Assert.True(protection.IsClear);
        Assert.Equal(ManagedVersionDeleteBlock.LauncherOwner,
            VersionManagementPolicy.DecideDelete(inventory, Active, protection, Admission(Active)).Block);
        Assert.Equal(ManagedVersionDeleteBlock.LauncherOwner,
            VersionManagementPolicy.DecideDelete(inventory, Candidate, protection, Admission(Candidate)).Block);
        ManagedVersionDeleteDecision rollback = VersionManagementPolicy.DecideDelete(
            inventory, Older, protection, Admission(Older));
        Assert.True(rollback.IsAllowed);
        Assert.True(rollback.RequiresRollbackLossWarning);
        Assert.False(protection.IsHardProtected(Admission(Active) with { AdmissionIdentity = "other" }));
        Assert.False(protection.IsHardProtected(Admission(Active) with { ReleaseManifestSha256 = OtherDigest }));
        Assert.False(protection.IsHardProtected(Admission(Active) with { Version = Older }));
        Assert.False(protection.IsLastKnownGoodOnly(Admission(Older) with { ReleaseManifestSha256 = OtherDigest }));
        Assert.True(protection.IsLastKnownGoodOnly(Admission(Older)));
        Assert.Throws<ArgumentNullException>(() => protection.IsHardProtected(null!));
        Assert.Throws<ArgumentNullException>(() => protection.IsLastKnownGoodOnly(null!));
        var shared = protection with { ActiveOwner = Admission(Older) };
        Assert.False(shared.IsLastKnownGoodOnly(Admission(Older)));
        var pending = protection with { PendingOwners = [Admission(Older)] };
        Assert.False(pending.IsLastKnownGoodOnly(Admission(Older)));
    }

    /// <summary>Uncommitted inventory cannot become an ordinary delete target.</summary>
    [Theory]
    [InlineData(ManagedVersionAdmissionState.RecoveryCandidate)]
    [InlineData(ManagedVersionAdmissionState.Unadmitted)]
    public void UncommittedInventoryRequiresRecovery(ManagedVersionAdmissionState admissionState)
    {
        ManagedVersionInventory inventory = ManagedVersionInventory.Create([Row(Older, active: true, admissionState: admissionState)]);
        Assert.Equal(ManagedVersionDeleteBlock.RecoveryRequired, VersionManagementPolicy.DecideDelete(inventory, Older).Block);
    }

    /// <summary>Inventory retains the frozen treatment of unrecognized row enum values and multiple fallback flags.</summary>
    [Fact]
    public void InventoryRetainsSourceEnumAndFallbackFlagPredicates()
    {
        ManagedVersionInventory inventory = ManagedVersionInventory.Create(
        [
            Row(Older, lastKnownGood: true) with { Integrity = (ManagedVersionIntegrity)99 },
            Row(Active, lastKnownGood: true) with { AdmissionState = (ManagedVersionAdmissionState)99 },
        ]);
        Assert.Equal(2, inventory.UnadmittedCount);
        Assert.Equal(0, inventory.HealthyCount);
        Assert.Equal(0, inventory.DamagedCount);
    }

    /// <summary>Whole-inventory results never publish partial facts on unavailability.</summary>
    [Fact]
    public void InventoryReadResultsAreFailClosed()
    {
        ManagedVersionInventory inventory = ManagedVersionInventory.Create([]);
        ManagedVersionInventoryReadResult success = ManagedVersionInventoryReadResult.Success(inventory);
        Assert.True(success.IsSuccess);
        Assert.Same(inventory, success.Inventory);
        ManagedVersionInventoryReadResult failure = ManagedVersionInventoryReadResult.Unavailable();
        Assert.False(failure.IsSuccess);
        Assert.Null(failure.Inventory);
        Assert.Equal(ManagedVersionInventoryReadIssue.Unavailable, failure.Issue);
        Assert.Throws<ArgumentNullException>(() => ManagedVersionInventoryReadResult.Success(null!));
    }

    /// <summary>State results require both a snapshot and the exact success issue.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(99)]
    public void StateResultSuccessPredicatesArePreserved(int issue)
    {
        Assert.Equal(issue == 0, new VersionManagerStateLoadResult(State(), (VersionManagerStateLoadIssue)issue).IsSuccess);
        Assert.False(new VersionManagerStateLoadResult(null, (VersionManagerStateLoadIssue)issue).IsSuccess);
        Assert.Equal(issue == 0, new VersionManagerStateSaveResult((VersionManagerStateSaveIssue)issue).IsSuccess);
        Assert.Equal(issue == 0, new LauncherBootstrapStateLoadResult(LauncherState(), (LauncherBootstrapStateLoadIssue)issue).IsSuccess);
        Assert.False(new LauncherBootstrapStateLoadResult(null, (LauncherBootstrapStateLoadIssue)issue).IsSuccess);
        Assert.Equal(issue == 0, new LauncherBootstrapStateSaveResult((LauncherBootstrapStateSaveIssue)issue).IsSuccess);
    }

    /// <summary>The retained default repository method denies launch custody and checks cancellation first.</summary>
    [Fact]
    public async Task DefaultApplicationLaunchLeaseDeniesCustodyAndHonorsCancellation()
    {
        IManagedVersionRepository repository = new Repository();
        ManagedExecutableLaunchLeaseResult result = await repository.AcquireApplicationLaunchLeaseAsync(
            Root, Admission(Active), TestContext.Current.CancellationToken);
        Assert.False(result.IsAcquired);
        Assert.Null(result.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.Unavailable, result.Issue);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await repository.AcquireApplicationLaunchLeaseAsync(Root, Admission(Active), cancellation.Token);
        });
    }

    private sealed class Repository : IManagedVersionRepository
    {
        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot, string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
            string managedRoot, IReadOnlyList<ManagedVersionAdmission> admissions, ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion, ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot, ManagedVersionAdmission admission, ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
