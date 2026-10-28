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
    /// <summary>Identical accepted registry authority is idempotent.</summary>
    [Fact]
    public async Task SameRevisionAndDigestAreIdempotentWithoutAStateRewrite()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = VersionManagerState.Create(
            latest,
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
            new SequenceRegistrySource(
                Registry(5, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest)),
                Registry(5, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(VersionRegistryStatus.LatestSelected, result.RegistryStatus);
    }

    /// <summary>Deprecated prior source is retained for recovery but never auto-contacted.</summary>
    [Fact]
    public async Task DeprecatedPriorSourceIsRetainedButNeverContactedWhenCandidatesFail()
    {
        string deprecated = SourcePath("deprecated");
        string latest = SourcePath("latest");
        VersionManagerState initial = VersionManagerState.Create(
            deprecated,
            ManagedAppVersion.Parse("0.10.5"),
            ManagedAppVersion.Parse("0.10.5"),
            [Admission("0.10.5")],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root");
        using var stateStore = new MemoryStateStore(initial);
        var catalogs = new PathCatalogSource();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            new HealthyRepository(),
            new SequenceRegistrySource(Registry(
                1,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest),
                (deprecated, UpdateSourceRegistryEntryStatus.Deprecated))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal([latest], catalogs.LoadedRoots);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Equal(deprecated, stateStore.State.UpdateSource);
        Assert.Equal(VersionRegistryStatus.DeprecatedRetained, result.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.CurrentSourceDeprecated, result.RegistryIssue);
    }

    /// <summary>Atomic state-save failure leaves the prior authority durable.</summary>
    [Fact]
    public async Task StateSaveFailureAfterReadmissionLeavesDurableAuthorityUnchanged()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new FailingStateStore(initial, failOnSave: 1);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            new SequenceRegistrySource(
                Registry(1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest)),
                Registry(1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Same(initial, stateStore.State);
        Assert.Equal(1, stateStore.SaveCount);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, result.RegistryIssue);
    }

    /// <summary>Any non-newest catalog publication change rejects the commit.</summary>
    [Fact]
    public async Task NonNewestCatalogChangeDuringLeaseReadmissionRejectsCommit()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var catalogs = new SequencePathCatalogSource(
            latest,
            CatalogWithOlderReleaseNote("first"),
            CatalogWithOlderReleaseNote("changed"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            new HealthyRepository(),
            new SequenceRegistrySource(
                Registry(1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest)),
                Registry(1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(UpdateSourceRegistryIssue.RegistryChanged, result.RegistryIssue);
    }

    /// <summary>Pending launcher state fences registry commits.</summary>
    [Fact]
    public async Task PendingLauncherMutationFenceBlocksRegistryCommitEvenWhenLeaseIsFree()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            new SequenceRegistrySource(
                Registry(1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest)),
                Registry(1, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))),
            new DenyingMutationFence());

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, result.RegistryIssue);
    }

    /// <summary>A changed registry during re-admission cannot commit.</summary>
    [Fact]
    public async Task RegistryChangingDuringCommitIsRejectedWithoutStateMutation()
    {
        string latest = SourcePath("latest");
        string other = SourcePath("other");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        var registry = new SequenceRegistrySource(
            Registry(9, FirstRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest)),
            Registry(10, SecondRegistryDigest, (other, UpdateSourceRegistryEntryStatus.Latest)));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            registry);

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(UpdateSourceRegistryIssue.RegistryChanged, result.RegistryIssue);
    }

    /// <summary>Registry read failure is typed and leaves the prior source intact.</summary>
    [Theory]
    [InlineData(UpdateSourceRegistryLoadIssue.PermissionDenied, UpdateSourceRegistryIssue.PermissionDenied)]
    [InlineData(UpdateSourceRegistryLoadIssue.AuthenticationRequired, UpdateSourceRegistryIssue.AuthenticationRequired)]
    [InlineData(UpdateSourceRegistryLoadIssue.RegistryTimedOut, UpdateSourceRegistryIssue.TimedOut)]
    public async Task RegistryReadFailureIsTypedAndPreservesPriorSource(
        UpdateSourceRegistryLoadIssue loadIssue,
        UpdateSourceRegistryIssue expectedIssue)
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource(),
            new HealthyRepository(),
            new SequenceRegistrySource(new UpdateSourceRegistryLoadResult(
                Snapshot: null,
                loadIssue)));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(VersionRegistryStatus.Unavailable, result.RegistryStatus);
        Assert.Equal(expectedIssue, result.RegistryIssue);
    }

    /// <summary>Writer contention after read-only admission performs no mutation.</summary>
    [Fact]
    public async Task BusyWriterLeaseAfterCandidateAdmissionCausesZeroMutation()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new SequencedLeaseStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            new SequenceRegistrySource(Registry(
                9,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, result.RegistryIssue);
        Assert.Equal(VersionManagerStateLoadIssue.Unavailable, result.StateIssue);
    }

    /// <summary>A durable state change while waiting for the lease rejects the registry commit.</summary>
    [Fact]
    public async Task DurableStateChangeDuringReadmissionCausesZeroMutation()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        VersionManagerState changed = initial.WithRetentionReviewDue(retentionReviewDue: true);
        using var stateStore = new ReloadChangedStateStore(initial, changed);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            new SequenceRegistrySource(Registry(
                9,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(changed, stateStore.State);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, result.RegistryIssue);
    }

    /// <summary>Revision-only and digest-only durable Registry drift reject lease-held readmission.</summary>
    [Theory]
    [InlineData(2, FirstRegistryDigest)]
    [InlineData(1, SecondRegistryDigest)]
    public async Task DurableRegistryAuthorityChangeDuringReadmissionCausesZeroMutation(
        long changedRevision,
        string changedDigest)
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5",
            source: latest,
            sourceRegistryState: new(1, FirstRegistryDigest, false));
        VersionManagerState changed = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5",
            source: latest,
            sourceRegistryState: new(changedRevision, changedDigest, false));
        using var stateStore = new ReloadChangedStateStore(initial, changed);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            new HealthyRepository(),
            new SequenceRegistrySource(Registry(
                3,
                new string('3', 64),
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(changed, stateStore.State);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, result.RegistryIssue);
    }

}
