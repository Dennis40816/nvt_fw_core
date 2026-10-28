// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using VersionDeleteOperationIssue = Nvt.Core.Launcher.Coordination.VersionDeleteOperationIssue;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Tests version management concurrency.</summary>
public sealed partial class VersionManagementExperienceConcurrencyTests
{
    /// <summary>Experiences for the same managed root serialize through shared state and reload before committing.</summary>
    [Fact]
    public async Task SameManagedRootSharingStateSerializesInstallsAndReloadsBeforeSecondCommit()
    {
        VersionManagerState initial = VersionManagerState.Create(
            "source",
            activeVersion: null,
            lastKnownGoodVersion: null,
            admissions: [],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-a");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new FirstInstallBlockingRepository();
        using VersionManagementExperience first = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-a",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository);
        using VersionManagementExperience second = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-a",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.7")),
            repository);
        _ = await first.InitializeAsync(TestContext.Current.CancellationToken);
        _ = await second.InitializeAsync(TestContext.Current.CancellationToken);
        _ = await first.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);
        _ = await second.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);

        Task<VersionInstallOperationResult> firstInstall = first.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"),
            TestContext.Current.CancellationToken).AsTask();
        await repository.FirstInstallStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        VersionInstallOperationResult contended = await second.InstallAsync(
            ManagedAppVersion.Parse("0.10.7"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, contended.Install.Issue);
        Assert.Equal(1, repository.InstallCount);
        _ = repository.ReleaseFirstInstall.TrySetResult();
        Assert.True((await firstInstall).Install.IsSuccess);

        VersionInstallOperationResult retried = await second.InstallAsync(
            ManagedAppVersion.Parse("0.10.7"),
            TestContext.Current.CancellationToken);

        Assert.True(retried.Install.IsSuccess);
        Assert.Equal(2, repository.InstallCount);
        Assert.Equal(
            [ManagedAppVersion.Parse("0.10.7"), ManagedAppVersion.Parse("0.10.6")],
            stateStore.State.Admissions.Select(admission => admission.Version));
        Assert.Null(stateStore.State.PendingMutation);
    }

    /// <summary>A shared state file never lets another managed root observe or mutate its authority.</summary>
    [Theory]
    [InlineData("0.10.5")]
    [InlineData("0.10.7")]
    public async Task DifferentManagedRootSharingStateFailsClosedForSameOrDifferentRunningVersion(
        string runningVersion)
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        VersionManagerState initial = VersionManagerState.Create(
            "source",
            active.Version,
            active.Version,
            [active],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-a");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new FirstInstallBlockingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse(runningVersion),
            "managed-b",
            stateStore,
            new FixedCatalogSource(Catalog("0.10.6")),
            repository);

        VersionManagementSnapshot initialized = await experience.InitializeAsync(
            TestContext.Current.CancellationToken);
        VersionInstallOperationResult install = await experience.InstallAsync(
            ManagedAppVersion.Parse("0.10.6"),
            TestContext.Current.CancellationToken);
        VersionDeleteOperationResult delete = await experience.DeleteAsync(
            active.Version,
            rollbackLossConfirmed: true,
            TestContext.Current.CancellationToken);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await experience.PrepareActivationAsync(
                active.Version,
                TestContext.Current.CancellationToken));

        Assert.Equal(VersionManagerStateLoadIssue.ManagedRootMismatch, initialized.StateIssue);
        Assert.Null(initialized.State);
        Assert.Equal(ManagedVersionInstallIssue.StateUnavailable, install.Install.Issue);
        Assert.Equal(VersionDeleteOperationIssue.StateUnavailable, delete.OperationIssue);
        Assert.Equal(0, repository.InventoryCount);
        Assert.Equal(0, repository.InstallCount);
        Assert.Equal(0, repository.DeleteCount);
        Assert.Equal(initial, stateStore.State);
    }

    /// <summary>An admission is not reported already-installed when its exact current-root payload is absent or damaged.</summary>
    [Theory]
    [InlineData(true, ManagedVersionIntegrity.Healthy)]
    [InlineData(false, ManagedVersionIntegrity.Damaged)]
    public async Task ExistingAdmissionRequiresExactHealthyPayloadBeforeAlreadyInstalled(
        bool omitPayload,
        ManagedVersionIntegrity integrity)
    {
        UpdateCatalogSnapshot catalog = Catalog("0.10.6");
        UpdateCatalogVersionSnapshot package = Assert.Single(catalog.Versions);
        var admission = new ManagedVersionAdmission(
            package.Version,
            package.Identity,
            package.ReleaseManifestSha256);
        VersionManagerState initial = VersionManagerState.Create(
            "source",
            activeVersion: null,
            lastKnownGoodVersion: null,
            [admission],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-a");
        var repository = new FirstInstallBlockingRepository
        {
            OmitInventory = omitPayload,
            InventoryIntegrity = integrity,
        };
        using var stateStore = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-a",
            stateStore,
            new FixedCatalogSource(catalog),
            repository);
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);

        VersionInstallOperationResult result = await experience.InstallAsync(
            admission.Version,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, result.Install.Issue);
        Assert.False(result.Install.WasAlreadyInstalled);
        Assert.Equal(0, repository.InstallCount);
        Assert.Equal([admission], result.Snapshot.State!.Admissions);
    }

    private sealed class FirstInstallBlockingRepository : IManagedVersionRepository
    {
        // Guard: Interlocked/Volatile publishes install calls across the install gate.
        private int _installCount;

        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource FirstInstallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource ReleaseFirstInstall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int InstallCount => Volatile.Read(ref _installCount);

        // Guard: the coordinator serializes mutations; tests read after awaiting their completion.
        internal int DeleteCount { get; private set; }

        // Guard: the coordinator serializes inventory reads; tests observe after awaited calls or gates.
        internal int InventoryCount { get; private set; }

        internal bool OmitInventory { get; init; }

        internal ManagedVersionIntegrity InventoryIntegrity { get; init; } = ManagedVersionIntegrity.Healthy;

        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ManagedPackageVerificationResult(
                new(package.Version, package.Identity, package.ReleaseNotes),
                ManagedVersionInstallIssue.None));
        }

        public async ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot,
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _installCount) == 1)
            {
                _ = FirstInstallStarted.TrySetResult();
                await ReleaseFirstInstall.Task.WaitAsync(cancellationToken);
            }
            return new(
                new(package.Version, package.Identity, package.ReleaseManifestSha256),
                ManagedVersionInstallIssue.None,
                WasAlreadyInstalled: false);
        }

        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
            string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions,
            ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion,
            ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken)
        {
            InventoryCount++;
            ManagedVersionInventory inventory = OmitInventory
                ? ManagedVersionInventory.Create([])
                : ManagedVersionInventory.Create(admissions.Select(admission => new
                InstalledVersionSnapshot(
                    admission.Version,
                    admission.AdmissionIdentity,
                    InventoryIntegrity,
                    DamageReason: InventoryIntegrity == ManagedVersionIntegrity.Healthy
                        ? null
                        : ManagedVersionDamageReason.ContentMismatch,
                    IsActive: admission.Version == activeVersion,
                    IsLastKnownGood: admission.Version == lastKnownGoodVersion,
                    ManagedVersionAdmissionState.Admitted,
                    admission)));
            return ValueTask.FromResult(ManagedVersionInventoryReadResult.Success(inventory));
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken)
        {
            DeleteCount++;
            return ValueTask.FromResult(ManagedVersionDeleteIssue.None);
        }
    }
}
