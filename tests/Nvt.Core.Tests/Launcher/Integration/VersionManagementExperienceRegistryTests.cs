// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Tests version management registry operations.</summary>
public sealed partial class VersionManagementExperienceRegistryTests
{
    /// <summary>All-manual Registry authority is readmitted under the lease with zero package I/O.</summary>
    [Fact]
    public async Task RegistryAutomaticAllManualReadmitsAuthorityAndAdvancesOnlyExistingCompatibleState()
    {
        string latest = SourcePath("catalog-v2-latest");
        VersionManagerState initial = State(
            [Admission("1.0.6")],
            active: "1.0.6",
            lastKnownGood: "1.0.6",
            source: latest,
            sourceRegistryState: new(1, FirstRegistryDigest, isManualPin: false));
        using var stateStore = new LeaseCountingStateStore(initial);
        var repository = new CountingRepository();
        UpdateCatalogSnapshot catalog = CatalogV2(("1.0.8", "manual-only"));
        var catalogResult = new UpdateCatalogLoadResult(
            catalog,
            UpdateCatalogLoadIssue.None,
            new(2, CatalogContentDigest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, catalogResult)),
            repository,
            new SequenceRegistrySource(
                RegistryWithPublication("1.0.8", 2, CatalogContentDigest, 2, SecondRegistryDigest,
                    (latest, UpdateSourceRegistryEntryStatus.Latest)),
                RegistryWithPublication("1.0.8", 2, CatalogContentDigest, 2, SecondRegistryDigest,
                    (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Empty(repository.VerifiedVersions);
        Assert.Equal(1, stateStore.SaveCount);
        Assert.Equal(new VersionSourceRegistryState(2, SecondRegistryDigest, false), stateStore.State.SourceRegistryState);
        VersionManagerState expected = initial.WithUpdateSource(
            latest,
            new(2, SecondRegistryDigest, isManualPin: false));
        Assert.True(expected.CreateDurableSnapshotToken().Matches(
            stateStore.State.CreateDurableSnapshotToken()));
        Assert.Null(result.Catalog);
        Assert.Null(result.VerifiedCandidate);
        Assert.Equal(VersionSourceStatus.Connected, result.SourceStatus);
        Assert.Equal(UpdateCatalogLoadIssue.None, result.CatalogIssue);
        Assert.False(result.ShouldPromptForUpdate);
        Assert.Equal(VersionRegistryStatus.LatestSelected, result.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.None, result.RegistryIssue);
    }

    /// <summary>Registry automatic checks admit only the newest eligible notify row before package I/O.</summary>
    [Fact]
    public async Task RegistryAutomaticMixedCatalogVerifiesAndPublishesOnlyNewestNotifyRow()
    {
        string latest = SourcePath("catalog-v2-latest");
        VersionManagerState initial = State(
            [Admission("1.0.6")],
            active: "1.0.6",
            lastKnownGood: "1.0.6");
        using var stateStore = new MemoryStateStore(initial);
        var repository = new CountingRepository();
        UpdateCatalogSnapshot catalog = CatalogV2(
            ("1.0.9", "manual-only"),
            ("1.0.8", "notify"),
            ("1.0.7", "manual-only"));
        var catalogResult = new UpdateCatalogLoadResult(
            catalog,
            UpdateCatalogLoadIssue.None,
            new(2, CatalogContentDigest));
        UpdateSourceRegistryLoadResult registry = RegistryWithPublication(
            "1.0.9",
            2,
            CatalogContentDigest,
            2,
            SecondRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, catalogResult)),
            repository,
            new SequenceRegistrySource(registry, registry));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [ManagedAppVersion.Parse("1.0.8"), ManagedAppVersion.Parse("1.0.8")],
            repository.VerifiedVersions);
        UpdateCatalogVersionSnapshot visible = Assert.Single(result.Catalog!.Versions);
        Assert.Equal(ManagedAppVersion.Parse("1.0.8"), visible.Version);
        Assert.Equal(UpdateNotificationPolicy.Notify, visible.NotificationPolicy);
        Assert.Equal(visible.Version, result.VerifiedCandidate!.Version);
        Assert.True(result.ShouldPromptForUpdate);
        Assert.Equal(latest, stateStore.State.UpdateSource);
        Assert.Equal(
            new VersionSourceRegistryState(2, SecondRegistryDigest, isManualPin: false),
            stateStore.State.SourceRegistryState);
    }

    /// <summary>All-manual authority with no effective source never writes a v1.0.7-incompatible state.</summary>
    [Fact]
    public async Task RegistryAutomaticAllManualWithNullSourceDoesNotSaveRegistryState()
    {
        string latest = SourcePath("catalog-v2-latest");
        ManagedAppVersion version = ManagedAppVersion.Parse("1.0.6");
        VersionManagerState initial = VersionManagerState.Create(
            null,
            version,
            version,
            [Admission("1.0.6")],
            null,
            null,
            false,
            managedRootIdentity: "managed-root");
        using var stateStore = new LeaseCountingStateStore(initial);
        var repository = new CountingRepository();
        var catalogResult = new UpdateCatalogLoadResult(
            CatalogV2(("1.0.8", "manual-only")),
            UpdateCatalogLoadIssue.None,
            new(2, CatalogContentDigest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            version,
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, catalogResult)),
            repository,
            new SequenceRegistrySource(
                RegistryWithPublication("1.0.8", 2, CatalogContentDigest, 2, SecondRegistryDigest,
                    (latest, UpdateSourceRegistryEntryStatus.Latest)),
                RegistryWithPublication("1.0.8", 2, CatalogContentDigest, 2, SecondRegistryDigest,
                    (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Empty(repository.VerifiedVersions);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Null(stateStore.State.UpdateSource);
        Assert.Null(stateStore.State.SourceRegistryState);
        Assert.Null(result.Catalog);
        Assert.Equal(VersionSourceStatus.Connected, result.SourceStatus);
    }

    /// <summary>Missing Registry coupling and selected-root mismatch both admit authority without saving.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistryAutomaticAllManualIncompatibleCouplingDoesNotSave(bool sourceMismatch)
    {
        string latest = SourcePath("catalog-v2-latest");
        string effectiveSource = sourceMismatch ? SourcePath("other") : latest;
        VersionManagerState initial = State(
            [Admission("1.0.6")],
            active: "1.0.6",
            lastKnownGood: "1.0.6",
            source: effectiveSource,
            sourceRegistryState: sourceMismatch
                ? new(1, FirstRegistryDigest, isManualPin: false)
                : null);
        using var stateStore = new LeaseCountingStateStore(initial);
        var catalogResult = new UpdateCatalogLoadResult(
            CatalogV2(("1.0.8", "manual-only")),
            UpdateCatalogLoadIssue.None,
            new(2, CatalogContentDigest));
        UpdateSourceRegistryLoadResult registry = RegistryWithPublication(
            "1.0.8",
            2,
            CatalogContentDigest,
            2,
            SecondRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, catalogResult)),
            new CountingRepository(),
            new SequenceRegistrySource(registry, registry));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Null(result.Catalog);
        Assert.Equal(VersionSourceStatus.Connected, result.SourceStatus);
    }

    /// <summary>Every compatibility guard rejects an all-manual authority-only state write.</summary>
    [Fact]
    public void AllManualAuthoritySaveRequiresSourceExistingRegistryNonPinAndMatchingRoot()
    {
        string selected = SourcePath("selected");
        string other = SourcePath("other");
        ManagedAppVersion version = ManagedAppVersion.Parse("1.0.6");
        ManagedVersionAdmission admission = Admission("1.0.6");
        VersionManagerState nullSource = VersionManagerState.Create(
            null, version, version, [admission], null, null, false, managedRootIdentity: "managed-root");
        VersionManagerState missingRegistry = VersionManagerState.Create(
            selected, version, version, [admission], null, null, false, managedRootIdentity: "managed-root");
        VersionManagerState manualPin = VersionManagerState.Create(
            selected, version, version, [admission], null, null, false,
            managedRootIdentity: "managed-root", sourceRegistryState: new(1, FirstRegistryDigest, true));
        VersionManagerState mismatch = VersionManagerState.Create(
            other, version, version, [admission], null, null, false,
            managedRootIdentity: "managed-root", sourceRegistryState: new(1, FirstRegistryDigest, false));
        VersionManagerState compatible = VersionManagerState.Create(
            selected, version, version, [admission], null, null, false,
            managedRootIdentity: "managed-root", sourceRegistryState: new(1, FirstRegistryDigest, false));

        Assert.False(VersionManagementExperience.CanPersistAllManualRegistryAuthority(nullSource, selected));
        Assert.False(VersionManagementExperience.CanPersistAllManualRegistryAuthority(missingRegistry, selected));
        Assert.False(VersionManagementExperience.CanPersistAllManualRegistryAuthority(manualPin, selected));
        Assert.False(VersionManagementExperience.CanPersistAllManualRegistryAuthority(mismatch, selected));
        Assert.True(VersionManagementExperience.CanPersistAllManualRegistryAuthority(compatible, selected));
    }

    /// <summary>A policy-only correction during Registry readmission rejects the in-flight check.</summary>
    [Theory]
    [InlineData("notify", "manual-only", 1)]
    [InlineData("manual-only", "notify", 0)]
    public async Task RegistryPolicyCorrectionDuringReadmissionRejectsWithoutStateMutation(
        string initialPolicy,
        string correctedPolicy,
        int expectedVerificationCount)
    {
        string latest = SourcePath("catalog-v2-latest");
        VersionManagerState initial = State(
            [Admission("1.0.6")],
            active: "1.0.6",
            lastKnownGood: "1.0.6");
        using var stateStore = new MemoryStateStore(initial);
        var catalogs = new SequencePathCatalogSource(
            latest,
            CatalogV2(("1.0.8", initialPolicy)),
            CatalogV2(("1.0.8", correctedPolicy)));
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.6"),
            "managed-root",
            stateStore,
            catalogs,
            repository,
            new SequenceRegistrySource(
                RegistryWithPublication("1.0.8", 1, CatalogContentDigest, 2, SecondRegistryDigest,
                    (latest, UpdateSourceRegistryEntryStatus.Latest)),
                RegistryWithPublication("1.0.8", 1, CatalogContentDigest, 2, SecondRegistryDigest,
                    (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedVerificationCount, repository.VerifiedVersions.Count);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(UpdateSourceRegistryIssue.RegistryChanged, result.RegistryIssue);
    }

    /// <summary>Latest then ordered available is the only automatic candidate order.</summary>
    [Fact]
    public async Task RegistryUsesLatestThenAvailableAndNeverProbesDeprecated()
    {
        string latest = SourcePath("latest");
        string available = SourcePath("available-b");
        string deprecated = SourcePath("deprecated");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var catalogSource = new PathCatalogSource(
            (latest, new(null, UpdateCatalogLoadIssue.SourceUnavailable)),
            (available, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        var registry = new SequenceRegistrySource(Registry(
            revision: 4,
            FirstRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest),
            (available, UpdateSourceRegistryEntryStatus.Available),
            (deprecated, UpdateSourceRegistryEntryStatus.Deprecated)));
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogSource,
            repository,
            registry);

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal([latest, available, available], catalogSource.LoadedRoots);
        Assert.Equal([available, available], repository.VerifiedRoots);
        Assert.DoesNotContain(deprecated, catalogSource.LoadedRoots);
        Assert.Equal(available, stateStore.State.UpdateSource);
        Assert.Equal(new VersionSourceRegistryState(4, FirstRegistryDigest, isManualPin: false),
            stateStore.State.SourceRegistryState);
        Assert.Equal(VersionRegistryStatus.FallbackSelected, result.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.None, result.RegistryIssue);
    }

    /// <summary>Candidate exhaustion preserves every durable state value.</summary>
    [Fact]
    public async Task AllRegistryCandidatesFailWithoutDurableMutation()
    {
        string latest = SourcePath("latest");
        string available = SourcePath("available");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var registry = new SequenceRegistrySource(Registry(
            revision: 7,
            FirstRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest),
            (available, UpdateSourceRegistryEntryStatus.Available)));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource(),
            new HealthyRepository(),
            registry);

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(VersionRegistryStatus.Exhausted, result.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.CandidatesExhausted, result.RegistryIssue);
    }

    /// <summary>Lower revisions and same-revision byte conflicts fail closed.</summary>
    [Theory]
    [InlineData(3, FirstRegistryDigest, UpdateSourceRegistryIssue.RevisionRollback)]
    [InlineData(5, SecondRegistryDigest, UpdateSourceRegistryIssue.RevisionConflict)]
    public async Task RegistryAntiRollbackRejectsStaleOrConflictingAuthorityWithoutMutation(
        long observedRevision,
        string observedDigest,
        UpdateSourceRegistryIssue expectedIssue)
    {
        string priorSource = SourcePath("prior-source");
        string latest = SourcePath("latest");
        VersionManagerState initial = VersionManagerState.Create(
            priorSource,
            ManagedAppVersion.Parse("0.10.5"),
            ManagedAppVersion.Parse("0.10.5"),
            [Admission("0.10.5")],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root",
            sourceRegistryState: new(5, FirstRegistryDigest, isManualPin: false));
        using var stateStore = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            new SequenceRegistrySource(Registry(
                observedRevision,
                observedDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(VersionRegistryStatus.Rejected, result.RegistryStatus);
        Assert.Equal(expectedIssue, result.RegistryIssue);
    }

    /// <summary>A stale backup remains visible but cannot roll back accepted primary authority.</summary>
    [Fact]
    public async Task MissingPrimaryAndStaleBackupFailAntiRollbackWithReplicaHealth()
    {
        string latest = SourcePath("latest");
        UpdateSourceRegistryLoadResult primary = Registry(
            8,
            SecondRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        UpdateSourceRegistryLoadResult backup = Registry(
            7,
            FirstRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        using var stateStore = new MemoryStateStore(State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5"));
        var replicated = new ReplicatedUpdateSourceRegistry(
            [
                new SequenceRegistrySource(
                    primary,
                    primary,
                    new(null, UpdateSourceRegistryLoadIssue.RegistryMissing),
                    new(null, UpdateSourceRegistryLoadIssue.RegistryMissing)),
                new SequenceRegistrySource(backup, backup, backup, backup),
            ]);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            replicated);

        VersionManagementSnapshot accepted = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);
        VersionManagerState acceptedState = stateStore.State;
        int savesAfterAcceptance = stateStore.SaveCount;

        VersionManagementSnapshot rejected = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);
        VersionEnvironmentSelfTestResult selfTest = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSourceRegistryIssue.None, accepted.RegistryIssue);
        Assert.Equal(new VersionSourceRegistryState(8, SecondRegistryDigest, isManualPin: false),
            acceptedState.SourceRegistryState);
        Assert.Equal(VersionRegistryStatus.Rejected, rejected.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.RevisionRollback, rejected.RegistryIssue);
        Assert.Same(acceptedState, stateStore.State);
        Assert.Equal(savesAfterAcceptance, stateStore.SaveCount);
        Assert.False(selfTest.IsSuccess);
        Assert.Equal(UpdateSourceRegistryLoadIssue.None, selfTest.RegistryIssue);
        Assert.Equal(UpdateSourceRegistryIssue.RevisionRollback, selfTest.AuthorityIssue);
        Assert.Equal(8, selfTest.AcceptedRegistryRevision);
        Assert.Empty(selfTest.Attempts);
        Assert.Collection(
            selfTest.Replicas,
            replica =>
            {
                Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryMissing, replica.Issue);
                Assert.False(replica.IsSelected);
            },
            replica =>
            {
                Assert.Equal(7, replica.RegistryRevision);
                Assert.True(replica.IsSelected);
            });
    }

    /// <summary>A route hotfix is admitted when the changed bytes carry a higher revision.</summary>
    [Fact]
    public async Task HigherRevisionRouteHotfixCommitsChangedRegistryAuthority()
    {
        string priorSource = SourcePath("prior-source");
        string latest = SourcePath("hotfix-latest");
        VersionManagerState initial = VersionManagerState.Create(
            priorSource,
            ManagedAppVersion.Parse("0.10.5"),
            ManagedAppVersion.Parse("0.10.5"),
            [Admission("0.10.5")],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root",
            sourceRegistryState: new(5, FirstRegistryDigest, isManualPin: false));
        using var stateStore = new MemoryStateStore(initial);
        UpdateSourceRegistryLoadResult hotfix = Registry(
            6,
            SecondRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            new SequenceRegistrySource(hotfix, hotfix));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, stateStore.SaveCount);
        Assert.Equal(latest, stateStore.State.UpdateSource);
        Assert.Equal(
            new VersionSourceRegistryState(6, SecondRegistryDigest, isManualPin: false),
            stateStore.State.SourceRegistryState);
        Assert.Equal(VersionRegistryStatus.LatestSelected, result.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.None, result.RegistryIssue);
    }

    /// <summary>Manual source confirmation pins until an explicit successful resume.</summary>
    [Fact]
    public async Task ManualSourceIsDurablyPinnedUntilExplicitResume()
    {
        string manual = SourcePath("manual");
        string registryLatest = SourcePath("registry-latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var registry = new SequenceRegistrySource(RegistryWithPublication(
            "0.10.7",
            1,
            CatalogContentDigest,
            revision: 8,
            FirstRegistryDigest,
            (registryLatest, UpdateSourceRegistryEntryStatus.Latest)));
        var catalogs = new PathCatalogSource(
            (manual, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)),
            (registryLatest, new(Catalog("0.10.7"), UpdateCatalogLoadIssue.None)));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            new HealthyRepository(),
            registry);

        VersionManagementSnapshot pinned = await experience.CommitUpdateSourceAsync(
            manual,
            TestContext.Current.CancellationToken);
        _ = await experience.CheckAsync(isAutomatic: false, TestContext.Current.CancellationToken);

        Assert.True(stateStore.State.SourceRegistryState!.IsManualPin);
        Assert.Equal(0, stateStore.State.SourceRegistryState.AcceptedRevision);
        Assert.Null(stateStore.State.SourceRegistryState.AcceptedDigest);
        Assert.Equal(manual, stateStore.State.UpdateSource);
        Assert.Equal(0, registry.LoadCount);
        Assert.Equal(VersionRegistryStatus.ManualPin, pinned.RegistryStatus);

        VersionManagementSnapshot resumed = await experience.ResumeRegistryAsync(
            TestContext.Current.CancellationToken);

        Assert.False(stateStore.State.SourceRegistryState!.IsManualPin);
        Assert.Equal(registryLatest, stateStore.State.UpdateSource);
        Assert.Equal(2, registry.LoadCount);
        Assert.Equal(VersionRegistryStatus.LatestSelected, resumed.RegistryStatus);
    }

    /// <summary>A failed Resume cannot clear its durable manual pin.</summary>
    [Fact]
    public async Task FailedResumePreservesManualPinAndSource()
    {
        string manual = SourcePath("manual");
        string latest = SourcePath("latest");
        VersionManagerState initial = VersionManagerState.Create(
            manual,
            ManagedAppVersion.Parse("0.10.5"),
            ManagedAppVersion.Parse("0.10.5"),
            [Admission("0.10.5")],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root",
            sourceRegistryState: new(0, null, isManualPin: true));
        using var stateStore = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource(),
            new HealthyRepository(),
            new SequenceRegistrySource(Registry(
                1,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.ResumeRegistryAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.True(stateStore.State.SourceRegistryState!.IsManualPin);
        Assert.Equal(manual, stateStore.State.UpdateSource);
        Assert.Equal(UpdateSourceRegistryIssue.CandidatesExhausted, result.RegistryIssue);
    }

}
