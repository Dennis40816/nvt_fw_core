// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Tests version management registry self-tests.</summary>
public sealed partial class VersionManagementExperienceRegistrySelfTestTests
{
    /// <summary>Environment Self-test still verifies the asserted newest package when v2 policy is manual-only.</summary>
    [Fact]
    public async Task EnvironmentSelfTestVerifiesManualOnlyV2NewestPackage()
    {
        string latest = SourcePath("self-test-v2-manual");
        VersionManagerState initial = State(
            [Admission("1.0.7")], active: "1.0.7", lastKnownGood: "1.0.7");
        var repository = new CountingRepository();
        var result = new UpdateCatalogLoadResult(
            CatalogV2(("1.0.8", "manual-only")),
            UpdateCatalogLoadIssue.None,
            new(2, CatalogContentDigest));
        using var inlineStore1 = new LeaseCountingStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("1.0.7"),
            "managed-root",
            inlineStore1,
            new PathCatalogSource((latest, result)),
            repository,
            new SequenceRegistrySource(RegistryWithPublication(
                "1.0.8", 2, CatalogContentDigest, 2, SecondRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionEnvironmentSelfTestResult selfTest = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        Assert.True(selfTest.IsSuccess);
        Assert.True(Assert.Single(selfTest.Attempts).IsVerified);
        Assert.Equal([ManagedAppVersion.Parse("1.0.8")], repository.VerifiedVersions);
    }

    /// <summary>Self-test inspects every automatic source in order without consuming state or prompt generation.</summary>
    [Fact]
    public async Task EnvironmentSelfTestIsReadOnlyOrderedAndSkipsDeprecated()
    {
        string latest = SourcePath("latest");
        string available = SourcePath("available");
        string deprecated = SourcePath("deprecated");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new LeaseCountingStateStore(initial);
        var catalogs = new PathCatalogSource(
            (latest, new(null, UpdateCatalogLoadIssue.SourceUnavailable)),
            (available, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            repository,
            new SequenceRegistrySource(Registry(
                12,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest),
                (deprecated, UpdateSourceRegistryEntryStatus.Deprecated),
                (available, UpdateSourceRegistryEntryStatus.Available))));
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        int leaseRequestsBeforeSelfTest = stateStore.LeaseRequestCount;

        VersionEnvironmentSelfTestResult result = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([latest, available], result.Attempts.Select(attempt => attempt.SourceRoot));
        Assert.Equal(
            [UpdateSourceRegistryEntryStatus.Latest, UpdateSourceRegistryEntryStatus.Available],
            result.Attempts.Select(attempt => attempt.Status));
        Assert.False(result.Attempts[0].IsVerified);
        Assert.True(result.Attempts[1].IsVerified);
        Assert.Equal([available], repository.VerifiedRoots);
        Assert.DoesNotContain(deprecated, catalogs.LoadedRoots);
        Assert.Equal(leaseRequestsBeforeSelfTest, stateStore.LeaseRequestCount);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);

        VersionManagementSnapshot checkedResult = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, checkedResult.Generation);
    }

    /// <summary>A concurrent Check that advances authority makes the older in-flight Self-test stale.</summary>
    [Fact]
    public async Task EnvironmentSelfTestRechecksAuthorityAfterConcurrentCheckCommit()
    {
        string latest = SourcePath("latest-concurrent-authority");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new LeaseCountingStateStore(initial);
        var repository = new BlockingFirstVerificationRepository();
        UpdateSourceRegistryLoadResult stale = Registry(
            7,
            FirstRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        UpdateSourceRegistryLoadResult current = Registry(
            8,
            SecondRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            repository,
            new SequenceRegistrySource(stale, current));

        Task<VersionEnvironmentSelfTestResult> selfTest = experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken).AsTask();
        await repository.FirstVerificationStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        VersionManagementSnapshot check = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);
        _ = repository.ReleaseFirstVerification.TrySetResult();
        VersionEnvironmentSelfTestResult result = await selfTest;

        Assert.Equal(UpdateSourceRegistryIssue.None, check.RegistryIssue);
        Assert.Equal(8, stateStore.State.SourceRegistryState!.AcceptedRevision);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateSourceRegistryIssue.RevisionRollback, result.AuthorityIssue);
        Assert.Equal(8, result.AcceptedRegistryRevision);
        Assert.Empty(result.Attempts);
        Assert.Equal(1, stateStore.SaveCount);
    }

    /// <summary>Unreadable durable authority blocks Self-test before package inspection.</summary>
    [Fact]
    public async Task EnvironmentSelfTestStateUnavailableBeforeInspectionFailsWithoutVerification()
    {
        string latest = SourcePath("state-unavailable-before");
        using var stateStore = new SequenceLoadStateStore(
            new VersionManagerStateLoadResult(null, VersionManagerStateLoadIssue.Unavailable));
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            repository,
            new SequenceRegistrySource(Registry(
                12,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionEnvironmentSelfTestResult result = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, result.AuthorityIssue);
        Assert.Empty(result.Attempts);
        Assert.Empty(repository.VerifiedRoots);
        Assert.Equal(1, stateStore.LoadCount);
        Assert.Equal(0, stateStore.LeaseRequestCount);
        Assert.Equal(0, stateStore.SaveCount);
    }

    /// <summary>Authority that becomes unreadable discards verified attempts and performs no mutation.</summary>
    [Fact]
    public async Task EnvironmentSelfTestStateUnavailableAfterInspectionDiscardsAttempts()
    {
        string latest = SourcePath("state-unavailable-after");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new SequenceLoadStateStore(
            new VersionManagerStateLoadResult(initial, VersionManagerStateLoadIssue.None),
            new VersionManagerStateLoadResult(null, VersionManagerStateLoadIssue.ManagedRootMismatch));
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            repository,
            new SequenceRegistrySource(Registry(
                12,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionEnvironmentSelfTestResult result = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, result.AuthorityIssue);
        Assert.Empty(result.Attempts);
        Assert.Equal([latest], repository.VerifiedRoots);
        Assert.Equal(2, stateStore.LoadCount);
        Assert.Equal(0, stateStore.LeaseRequestCount);
        Assert.Equal(0, stateStore.SaveCount);
    }

    /// <summary>Self-test reuses normal newest-package admission and never scans historical packages.</summary>
    [Fact]
    public async Task EnvironmentSelfTestReusesNewestPackageAdmission()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new LeaseCountingStateStore(initial);
        var repository = new CountingRepository(ManagedAppVersion.Parse("0.10.5"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(
                CatalogWithOlderReleaseNote("Release 0.10.5"),
                UpdateCatalogLoadIssue.None))),
            repository,
            new SequenceRegistrySource(Registry(
                12,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionEnvironmentSelfTestResult result = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        VersionEnvironmentSelfTestAttempt attempt = Assert.Single(result.Attempts);
        Assert.True(result.IsSuccess);
        Assert.True(attempt.IsVerified);
        Assert.Equal(ManagedAppVersion.Parse("0.10.6"), attempt.NewestVersion);
        Assert.Equal(ManagedVersionInstallIssue.None, attempt.PackageIssue);
        Assert.Equal([ManagedAppVersion.Parse("0.10.6")], repository.VerifiedVersions);
        Assert.Equal(0, stateStore.LeaseRequestCount);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
    }

    /// <summary>A maximum-size valid catalog still admits only its canonical newest package.</summary>
    [Fact]
    public async Task EnvironmentSelfTestAcceptsMaximumCatalogWithoutHistoricalPackageScan()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new LeaseCountingStateStore(initial);
        var repository = new CountingRepository(ManagedAppVersion.Parse("0.10.1"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(
                CatalogWithVersionCount(IntegrationTestData.Limits.MaximumCatalogEntries),
                UpdateCatalogLoadIssue.None))),
            repository,
            new SequenceRegistrySource(RegistryWithPublication(
                "0.10.128",
                1,
                CatalogContentDigest,
                12,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionEnvironmentSelfTestResult result = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        VersionEnvironmentSelfTestAttempt attempt = Assert.Single(result.Attempts);
        Assert.True(result.IsSuccess);
        Assert.Equal(ManagedAppVersion.Parse("0.10.128"), attempt.NewestVersion);
        Assert.Equal([ManagedAppVersion.Parse("0.10.128")], repository.VerifiedVersions);
        Assert.Equal(0, stateStore.LeaseRequestCount);
        Assert.Equal(0, stateStore.SaveCount);
    }

    /// <summary>Every Catalog identity assertion is enforced before package verification.</summary>
    [Theory]
    [InlineData("sha256")]
    [InlineData("schema")]
    [InlineData("latest-version")]
    public async Task EnvironmentSelfTestRejectsCatalogAssertionMismatchBeforePackageVerification(
        string mismatch)
    {
        string latest = SourcePath("latest-assertion-mismatch");
        var repository = new CountingRepository();
        string expectedLatest = mismatch == "latest-version" ? "0.10.7" : "0.10.6";
        int expectedSchema = mismatch == "schema" ? 2 : 1;
        string actualDigest = mismatch == "sha256" ? new string('d', 64) : CatalogContentDigest;
        var mismatched = new UpdateCatalogLoadResult(
            Catalog("0.10.6"),
            UpdateCatalogLoadIssue.None,
            new UpdateCatalogContentIdentity(1, actualDigest));
        using var inlineStore2 = new LeaseCountingStateStore(State(
                [Admission("0.10.5")],
                active: "0.10.5",
                lastKnownGood: "0.10.5"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            inlineStore2,
            new PathCatalogSource((latest, mismatched)),
            repository,
            new SequenceRegistrySource(RegistryWithPublication(
                expectedLatest,
                expectedSchema,
                CatalogContentDigest,
                12,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionEnvironmentSelfTestResult result = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        VersionEnvironmentSelfTestAttempt attempt = Assert.Single(result.Attempts);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateCatalogLoadIssue.InvalidManifest, attempt.CatalogIssue);
        Assert.Empty(repository.VerifiedRoots);
    }

    /// <summary>Self-test and normal resolution reject the same mismatched typed candidate.</summary>
    [Fact]
    public async Task EnvironmentSelfTestAndCheckShareCandidateMismatchRejection()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new LeaseCountingStateStore(initial);
        var repository = new HealthyRepository(verificationResult: new(
            new(
                ManagedAppVersion.Parse("0.10.7"),
                "identity-0.10.7",
                "Release 0.10.7"),
            ManagedVersionInstallIssue.None));
        UpdateSourceRegistryLoadResult registry = Registry(
            12,
            FirstRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))),
            repository,
            new SequenceRegistrySource(registry, registry));

        VersionEnvironmentSelfTestResult selfTest = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);
        VersionManagementSnapshot check = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        VersionEnvironmentSelfTestAttempt attempt = Assert.Single(selfTest.Attempts);
        Assert.False(selfTest.IsSuccess);
        Assert.False(attempt.IsVerified);
        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, attempt.PackageIssue);
        Assert.Equal(UpdateSourceRegistryIssue.CandidatesExhausted, check.RegistryIssue);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
    }

    /// <summary>Cancellation during an actual source read propagates without touching durable state.</summary>
    [Fact]
    public async Task EnvironmentSelfTestMidReadCancellationPerformsZeroMutation()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new LeaseCountingStateStore(initial);
        var catalogs = new CancelFirstCatalogSource(Catalog("0.10.6"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            new HealthyRepository(),
            new SequenceRegistrySource(Registry(
                12,
                FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest))));
        using var cancellation = new CancellationTokenSource();

        Task<VersionEnvironmentSelfTestResult> checking = experience.RunEnvironmentSelfTestAsync(
            cancellation.Token).AsTask();
        await catalogs.FirstLoadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checking.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, stateStore.LeaseRequestCount);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
    }

    /// <summary>Self-test observes caller cancellation without performing any read or mutation.</summary>
    [Fact]
    public async Task EnvironmentSelfTestCancellationIsPropagatedWithoutMutation()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        using var stateStore = new LeaseCountingStateStore(initial);
        var registry = new SequenceRegistrySource(Registry(
            1,
            FirstRegistryDigest,
            (SourcePath("latest"), UpdateSourceRegistryEntryStatus.Latest)));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            new PathCatalogSource(),
            new HealthyRepository(),
            registry);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await experience.RunEnvironmentSelfTestAsync(cancellation.Token));

        Assert.Equal(0, registry.LoadCount);
        Assert.Equal(0, stateStore.LeaseRequestCount);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
    }

    /// <summary>Caller cancellation restores the prior snapshot and releases the owned check.</summary>
    [Fact]
    public async Task RegistryCheckCallerCancellationAllowsCleanRetry()
    {
        string latest = SourcePath("latest");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5",
            source: latest);
        using var stateStore = new MemoryStateStore(initial);
        var catalogs = new CancelFirstCatalogSource(Catalog("0.10.6"));
        UpdateSourceRegistryLoadResult registry = Registry(
            1,
            FirstRegistryDigest,
            (latest, UpdateSourceRegistryEntryStatus.Latest));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            new HealthyRepository(),
            new SequenceRegistrySource(registry, registry, registry));
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        Task<VersionManagementSnapshot> cancelled = experience.CheckAsync(
            isAutomatic: false,
            cancellation.Token).AsTask();
        await catalogs.FirstLoadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelled.WaitAsync(TestContext.Current.CancellationToken));

        VersionManagementSnapshot retried = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceStatus.Connected, retried.SourceStatus);
        Assert.Equal(VersionRegistryStatus.LatestSelected, retried.RegistryStatus);
        Assert.Equal(1, stateStore.SaveCount);
    }

    /// <summary>The legacy/manual source path uses the same caller-cancellation cleanup owner.</summary>
    [Fact]
    public async Task DirectSourceCheckCallerCancellationAllowsCleanRetry()
    {
        string source = SourcePath("manual");
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5",
            source: source);
        using var stateStore = new MemoryStateStore(initial);
        var catalogs = new CancelFirstCatalogSource(Catalog("0.10.6"));
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            new HealthyRepository());
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        Task<VersionManagementSnapshot> cancelled = experience.CheckAsync(
            isAutomatic: false,
            cancellation.Token).AsTask();
        await catalogs.FirstLoadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelled.WaitAsync(TestContext.Current.CancellationToken));

        VersionManagementSnapshot retried = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceStatus.Connected, retried.SourceStatus);
        Assert.Equal(0, stateStore.SaveCount);
    }

    /// <summary>Until the immutable locator is injected, self-test reports typed NotConfigured.</summary>
    [Fact]
    public async Task EnvironmentSelfTestWithoutInjectedRegistryIsTypedNotConfigured()
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
            new HealthyRepository());

        VersionEnvironmentSelfTestResult result = await experience.RunEnvironmentSelfTestAsync(
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateSourceRegistryLoadIssue.NotConfigured, result.RegistryIssue);
        Assert.Empty(result.Attempts);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
    }

    /// <summary>A retained automatic source cannot bypass its missing fixed Registry authority.</summary>
    [Fact]
    public async Task AutomaticRegistryStateWithoutInjectedLocatorFailsClosedWithoutSourceContact()
    {
        string automatic = SourcePath("automatic");
        VersionManagerState initial = VersionManagerState.Create(
            automatic,
            ManagedAppVersion.Parse("0.10.5"),
            ManagedAppVersion.Parse("0.10.5"),
            [Admission("0.10.5")],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed-root",
            sourceRegistryState: new(4, FirstRegistryDigest, isManualPin: false));
        using var stateStore = new MemoryStateStore(initial);
        var catalogs = new PathCatalogSource(
            (automatic, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            repository);
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceStatus.NotConfigured, result.SourceStatus);
        Assert.Equal(VersionRegistryStatus.NotConfigured, result.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.NotConfigured, result.RegistryIssue);
        Assert.Null(result.Catalog);
        Assert.Null(result.VerifiedCandidate);
        Assert.False(result.ShouldPromptForUpdate);
        Assert.Empty(catalogs.LoadedRoots);
        Assert.Empty(repository.VerifiedRoots);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
    }

    /// <summary>An explicit manual pin remains usable without the automatic Registry locator.</summary>
    [Fact]
    public async Task ManualPinWithoutInjectedLocatorChecksOnlyItsCommittedSource()
    {
        string manual = SourcePath("manual");
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
        var catalogs = new PathCatalogSource(
            (manual, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            catalogs,
            repository);
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken);

        VersionManagementSnapshot result = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceStatus.Connected, result.SourceStatus);
        Assert.Equal(VersionRegistryStatus.ManualPin, result.RegistryStatus);
        Assert.Equal(UpdateSourceRegistryIssue.None, result.RegistryIssue);
        Assert.Equal([manual], catalogs.LoadedRoots);
        Assert.Equal([manual], repository.VerifiedRoots);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Same(initial, stateStore.State);
    }
}
