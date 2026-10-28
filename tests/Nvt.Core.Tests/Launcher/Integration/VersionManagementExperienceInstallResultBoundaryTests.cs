// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using TransactionRepository = Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport.TransactionRepository;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Tests version management install result boundaries.</summary>
public sealed partial class VersionManagementExperienceInstallResultBoundaryTests
{
    /// <summary>A contradictory failed adapter result cannot become a durable admission.</summary>
    [Fact]
    public async Task InconsistentFailedInstallResultNeverCommitsAdmission()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        UpdateCatalogSnapshot catalog = Catalog("0.10.6");
        UpdateCatalogVersionSnapshot package = Assert.Single(catalog.Versions);
        var candidate = new ManagedVersionAdmission(
            package.Version,
            package.Identity,
            package.ReleaseManifestSha256);
        using MemoryStateStore stateStore = CreateStateStore(active);
        var repository = new TransactionRepository([active])
        {
            InstallResultOverride = new(
                candidate,
                ManagedVersionInstallIssue.PromotionFailed,
                WasAlreadyInstalled: false),
        };
        using VersionManagementExperience experience = CreateExperience(
            active,
            catalog,
            stateStore,
            repository);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);

        VersionInstallOperationResult result = await experience.InstallAsync(
            candidate.Version,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.PromotionFailed, result.Install.Issue);
        Assert.Null(result.Install.Admission);
        Assert.DoesNotContain(result.Snapshot.State!.Admissions, item => item.Version == candidate.Version);
        Assert.Null(result.Snapshot.State.PendingMutation);
        Assert.Null(stateStore.State.PendingMutation);
        Assert.Equal(2, stateStore.SaveCount);
    }

    /// <summary>A nominal issue code cannot make a missing or mismatched admission successful.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedSuccessfulInstallResultsFailClosed(bool returnMismatchedAdmission)
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        UpdateCatalogSnapshot catalog = Catalog("0.10.6");
        UpdateCatalogVersionSnapshot package = Assert.Single(catalog.Versions);
        ManagedVersionAdmission? returnedAdmission = returnMismatchedAdmission
            ? new(package.Version, "different-admission", package.ReleaseManifestSha256)
            : null;
        using MemoryStateStore stateStore = CreateStateStore(active);
        var repository = new TransactionRepository([active])
        {
            InstallResultOverride = new(
                returnedAdmission,
                ManagedVersionInstallIssue.None,
                WasAlreadyInstalled: false),
        };
        using VersionManagementExperience experience = CreateExperience(
            active,
            catalog,
            stateStore,
            repository);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);

        VersionInstallOperationResult result = await experience.InstallAsync(
            package.Version,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, result.Install.Issue);
        Assert.Null(result.Install.Admission);
        Assert.Null(result.Snapshot.State!.PendingMutation);
        Assert.DoesNotContain(result.Snapshot.State.Admissions, item => item.Version == package.Version);
    }

    /// <summary>Restart retains a contradictory journal while its target is not safely recoverable.</summary>
    [Fact]
    public async Task InconsistentInstallResultRetainsRecoverablePreparedJournal()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        UpdateCatalogSnapshot catalog = Catalog("0.10.6");
        UpdateCatalogVersionSnapshot package = Assert.Single(catalog.Versions);
        var candidate = new ManagedVersionAdmission(
            package.Version,
            package.Identity,
            package.ReleaseManifestSha256);
        using MemoryStateStore stateStore = CreateStateStore(active);
        var repository = new TransactionRepository([active], unadmittedVersion: "0.10.6")
        {
            InstallResultOverride = new(
                candidate,
                ManagedVersionInstallIssue.PromotionFailed,
                WasAlreadyInstalled: false),
        };
        using (VersionManagementExperience first = CreateExperience(
                   active,
                   catalog,
                   stateStore,
                   repository))
        {
            _ = await first.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);
            _ = await first.InstallAsync(candidate.Version, TestContext.Current.CancellationToken);
        }

        Assert.Equal(candidate, stateStore.State.PendingMutation?.Admission);
        using VersionManagementExperience restarted = CreateExperience(
            active,
            catalog,
            stateStore,
            repository);
        VersionManagementSnapshot recovered = await restarted.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(candidate, recovered.State!.PendingMutation?.Admission);
        Assert.DoesNotContain(recovered.State.Admissions, item => item.Version == candidate.Version);
        Assert.Equal(candidate, stateStore.State.PendingMutation?.Admission);
    }

    /// <summary>A contradictory failed install cannot become an activation target.</summary>
    [Fact]
    public async Task FailedInstallCannotProceedToActivation()
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        UpdateCatalogSnapshot catalog = Catalog("0.10.6");
        UpdateCatalogVersionSnapshot package = Assert.Single(catalog.Versions);
        var candidate = new ManagedVersionAdmission(
            package.Version,
            package.Identity,
            package.ReleaseManifestSha256);
        using MemoryStateStore stateStore = CreateStateStore(active);
        var repository = new TransactionRepository([active], unadmittedVersion: "0.10.6")
        {
            InstallResultOverride = new(
                candidate,
                ManagedVersionInstallIssue.PromotionFailed,
                WasAlreadyInstalled: false),
        };
        using VersionManagementExperience experience = CreateExperience(
            active,
            catalog,
            stateStore,
            repository);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);
        _ = await experience.InstallAsync(candidate.Version, TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await experience.PrepareActivationAsync(
                candidate.Version,
                TestContext.Current.CancellationToken));

        Assert.DoesNotContain(stateStore.State.Admissions, item => item.Version == candidate.Version);
        Assert.Null(stateStore.State.PendingActivation);
    }

    private static MemoryStateStore CreateStateStore(ManagedVersionAdmission active)
    {
        return new(VersionManagerState.Create(
            "source-root",
            active.Version,
            active.Version,
            [active],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root"));
    }

    private static VersionManagementExperience CreateExperience(
        ManagedVersionAdmission active,
        UpdateCatalogSnapshot catalog,
        MemoryStateStore stateStore,
        TransactionRepository repository)
    {
        return VersionManagementExperienceTestFactory.Create(
            active.Version,
            "managed-root",
            stateStore,
            new FixedCatalogSource(catalog),
            repository);
    }
}
