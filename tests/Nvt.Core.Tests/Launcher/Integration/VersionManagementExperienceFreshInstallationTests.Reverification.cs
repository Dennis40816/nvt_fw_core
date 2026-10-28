// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Tests.Launcher.Setup;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

public sealed partial class VersionManagementExperienceFreshInstallationTests
{
    /// <summary>All six registry authority facts and all three entry facts are checked before catalog inspection.</summary>
    [Theory]
    [InlineData("id")]
    [InlineData("revision")]
    [InlineData("digest")]
    [InlineData("schema")]
    [InlineData("latest")]
    [InlineData("catalog-digest")]
    [InlineData("missing")]
    [InlineData("status")]
    [InlineData("deprecated")]
    [InlineData("catalog-path")]
    [InlineData("source-root")]
    public async Task RegistryOrEntryDriftRejectsBeforeReinspection(string field)
    {
        using var store = new MemoryStateStore(FreshState());
        var registry = new SequenceRegistrySource(FreshRegistry(), ChangeFreshRegistry(field));
        var catalog = new PathCatalogSource((SourcePath("fresh"), new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = CreateFreshExperience(store, registry, catalog, repository);
        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);

        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            Assert.IsType<FreshInstallationCandidate>(inspected.Candidate), TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.SourceChanged, reverified);
        Assert.Equal(2, registry.LoadCount);
        Assert.Equal([SourcePath("fresh")], catalog.LoadedRoots);
        Assert.Equal(1, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>Each package identity field participates in the final complete captured-identity comparison.</summary>
    [Theory]
    [InlineData("path")]
    [InlineData("size")]
    [InlineData("package-hash")]
    [InlineData("manifest-hash")]
    public async Task PackageIdentityDriftRejectsAfterReadmission(string field)
    {
        using var store = new MemoryStateStore(FreshState());
        UpdateCatalogVersionSnapshot package = Row("0.10.6");
        UpdateCatalogVersionSnapshot changed = UpdateCatalogVersionSnapshot.Create(
            134_217_728, 65_536, package.Version, package.PublishedAt,
            field == "path" ? new("packages/changed.zip") : package.PackagePath,
            field == "size" ? package.PackageSize + 1 : package.PackageSize,
            field == "package-hash" ? SecondRegistryDigest : package.PackageSha256,
            field == "manifest-hash" ? SecondRegistryDigest : package.ReleaseManifestSha256,
            package.ReleaseNotes, package.NotificationPolicy);
        var catalog = new SequencePathCatalogSource(SourcePath("fresh"),
            UpdateCatalogSnapshot.Create(IntegrationTestData.Limits, [package]),
            UpdateCatalogSnapshot.Create(IntegrationTestData.Limits, [changed]));
        var repository = new HealthyRepository();
        var registry = new SequenceRegistrySource(FreshRegistry());
        using VersionManagementExperience experience = CreateFreshExperience(store, registry, catalog, repository);
        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);

        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            Assert.IsType<FreshInstallationCandidate>(inspected.Candidate), TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.SourceChanged, reverified);
        Assert.Equal(2, registry.LoadCount);
        Assert.Equal(2, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>A previously admitted package can disappear or lose launcher support before revalidation.</summary>
    [Theory]
    [InlineData(false, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(true, FreshInstallationCandidateIssue.SourceChanged)]
    public async Task PackageDisappearanceOrUnsupportedLauncherRejectsCapturedCandidate(
        bool unsupportedLauncher, FreshInstallationCandidateIssue expectedIssue)
    {
        using var store = new MemoryStateStore(FreshState());
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(FreshRegistry()), repository: repository);
        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidate expected = Assert.IsType<FreshInstallationCandidate>(inspected.Candidate);
        repository.VerificationResultOverride = unsupportedLauncher
            ? new(new(expected.Package.Version, expected.Package.Identity, expected.Package.ReleaseNotes), ManagedVersionInstallIssue.None)
            : new(null, ManagedVersionInstallIssue.PackageUnavailable);

        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            expected, TestContext.Current.CancellationToken);

        AssertFreshFailure(expectedIssue, reverified);
        Assert.Equal(2, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>A healthy historical source cannot rescue an unavailable automatic source.</summary>
    [Fact]
    public async Task DeprecatedSourceNeverSuppliesTheOnlyHealthyCandidate()
    {
        string latest = SourcePath("fresh");
        string deprecated = SourcePath("deprecated");
        var catalog = new PathCatalogSource((deprecated, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        var repository = new HealthyRepository();
        using var store = new MemoryStateStore(FreshState());
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(Registry(1, FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest), (deprecated, UpdateSourceRegistryEntryStatus.Deprecated))),
            catalog, repository);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.SourceUnavailable, inspected);
        Assert.Equal([latest], catalog.LoadedRoots);
        Assert.Equal(0, repository.VerifyCount);
    }

    /// <summary>The first supported admission wins without inspecting any remaining automatic entry.</summary>
    [Fact]
    public async Task LatestSuccessStopsBeforeAvailableEntries()
    {
        string latest = SourcePath("fresh");
        string available = SourcePath("available");
        var catalog = new PathCatalogSource(
            (latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)),
            (available, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        using var store = new MemoryStateStore(FreshState());
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(Registry(1, FirstRegistryDigest,
                (available, UpdateSourceRegistryEntryStatus.Available), (latest, UpdateSourceRegistryEntryStatus.Latest))), catalog);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);

        Assert.True(inspected.IsSuccess);
        Assert.Equal(latest, inspected.Candidate!.Identity.SourceRoot);
        Assert.Equal([latest], catalog.LoadedRoots);
    }

    /// <summary>Cancellation is checked before registry configuration or the first port call in either operation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancellationPrecedesRegistryLoadAndConfiguration(bool configured)
    {
        using var store = new MemoryStateStore(FreshState());
        var registry = new SequenceRegistrySource(FreshRegistry());
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = CreateFreshExperience(store, configured ? registry : null, repository: repository);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => experience.InspectFreshInstallationAsync(cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => experience.ReverifyFreshInstallationAsync(
            ExpectedFreshCandidate(), cancellation.Token).AsTask());

        Assert.Equal(0, registry.LoadCount);
        Assert.Equal(0, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>Reverification rejects null before cancellation or registry configuration.</summary>
    [Fact]
    public async Task ReverificationNullCheckPrecedesCancellation()
    {
        using var store = new MemoryStateStore(FreshState());
        using VersionManagementExperience experience = CreateFreshExperience(store, null);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        ArgumentNullException thrown = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            experience.ReverifyFreshInstallationAsync(null!, cancellation.Token).AsTask());

        Assert.Equal("expected", thrown.ParamName);
        Assert.Equal(0, store.LoadCount);
    }

    /// <summary>Disposal precedes cancellation and even the null expected-candidate check.</summary>
    [Fact]
    public async Task DisposedExperienceRejectsBeforeOtherChecks()
    {
        using var store = new MemoryStateStore(FreshState());
        var registry = new SequenceRegistrySource(FreshRegistry());
        using VersionManagementExperience experience = CreateFreshExperience(store, registry);
        experience.Dispose();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => experience.InspectFreshInstallationAsync(cancellation.Token).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => experience.ReverifyFreshInstallationAsync(null!, cancellation.Token).AsTask());

        Assert.Equal(0, registry.LoadCount);
        Assert.Equal(0, store.WriteLeaseCount);
    }

    /// <summary>Candidate queries leave an in-flight discovery check and its generation alive, without any writer.</summary>
    [Fact]
    public async Task FreshQueriesDoNotSupersedeRunningCheck()
    {
        using var store = new MemoryStateStore(FreshState());
        var repository = new BlockingFirstVerificationRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, new FixedCatalogSource(Catalog("0.10.6")),
            repository, new SequenceRegistrySource(FreshRegistry()));
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken, isReadOnly: true);
        Task<VersionManagementSnapshot> running = experience.CheckAsync(false, TestContext.Current.CancellationToken).AsTask();
        try
        {
            await repository.FirstVerificationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
            FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
                Assert.IsType<FreshInstallationCandidate>(inspected.Candidate), TestContext.Current.CancellationToken);

            Assert.True(reverified.IsSuccess);
            Assert.False(running.IsCompleted);
            Assert.Equal(0, store.WriteLeaseCount);
            Assert.Equal(0, store.SaveCount);
            Assert.Equal(1, store.LoadCount);
        }
        finally
        {
            _ = repository.ReleaseFirstVerification.TrySetResult();
            _ = await running;
        }
        VersionManagementSnapshot completed = await running;
        Assert.Equal(1, completed.Generation);
        Assert.Equal(UpdateSourceRegistryIssue.None, completed.RegistryIssue);
        Assert.NotNull(completed.VerifiedCandidate);
    }

    /// <summary>Exact revalidation does not repeat inspection's running-version eligibility threshold.</summary>
    [Fact]
    public async Task ReverificationPreservesCapturedIdentityWithoutReapplyingVersionThreshold()
    {
        using var store = new MemoryStateStore(FreshState());
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(FreshRegistry()), repository: repository, runningVersion: "0.10.7");

        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            ExpectedFreshCandidate(), TestContext.Current.CancellationToken);

        Assert.True(reverified.IsSuccess);
        Assert.Equal(ExpectedFreshCandidate().Identity, reverified.Candidate!.Identity);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>Entry path matching uses platform comparison, while the final captured identity remains ordinal.</summary>
    [Fact]
    public async Task CaseOnlyPathDriftStillChangesTheCompleteIdentity()
    {
        UpdateSourceRegistrySnapshot original = FreshRegistry().Snapshot!;
        UpdateSourceRegistryEntry changedEntry = UpdateSourceRegistryEntry.Create(
            original.Entries[0].CatalogPath.ToUpperInvariant(), UpdateSourceRegistryEntryStatus.Latest);
        UpdateSourceRegistryLoadResult changed = new(UpdateSourceRegistrySnapshot.Create(
            IntegrationTestData.Limits, IntegrationTestData.Descriptor(), original.RegistryId, original.RegistryRevision,
            original.PublishedAtUtc, original.CatalogPublication, original.ContentDigest, [changedEntry]), UpdateSourceRegistryLoadIssue.None);
        using var store = new MemoryStateStore(FreshState());
        var repository = new HealthyRepository();
        var catalog = new PathCatalogSource(
            (SourcePath("fresh"), new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)),
            (changedEntry.SourceRoot, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(FreshRegistry(), changed), catalog, repository);
        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);

        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            Assert.IsType<FreshInstallationCandidate>(inspected.Candidate), TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.SourceChanged, reverified);
        Assert.Equal(OperatingSystem.IsWindows() ? 2 : 1, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    private static UpdateSourceRegistryLoadResult ChangeFreshRegistry(string field)
    {
        UpdateSourceRegistrySnapshot original = FreshRegistry().Snapshot!;
        UpdateSourceRegistryEntry entry = original.Entries[0];
        UpdateSourceRegistryEntry replacement = UpdateSourceRegistryEntry.Create(
            Path.Combine(SourcePath("replacement"), "update-catalog.v1.json"), UpdateSourceRegistryEntryStatus.Latest);
        UpdateSourceRegistryEntry[] entries = field switch
        {
            "missing" => [replacement],
            "status" => [UpdateSourceRegistryEntry.Create(entry.CatalogPath, UpdateSourceRegistryEntryStatus.Available), replacement],
            "deprecated" => [UpdateSourceRegistryEntry.Create(entry.CatalogPath, UpdateSourceRegistryEntryStatus.Deprecated), replacement],
            "catalog-path" => [UpdateSourceRegistryEntry.Create(Path.Combine(entry.SourceRoot, "renamed.json"), entry.Status)],
            "source-root" => [replacement],
            _ => [entry],
        };
        string registryId = field == "id" ? "FIXTURE-registry" : original.RegistryId;
        UpdateCatalogPublicationAssertion publication = UpdateCatalogPublicationAssertion.Create(
            field == "latest" ? "0.10.7" : "0.10.6", field == "schema" ? 2 : 1,
            field == "catalog-digest" ? SecondRegistryDigest : CatalogContentDigest);
        return new(UpdateSourceRegistrySnapshot.Create(IntegrationTestData.Limits, IntegrationTestData.Descriptor(registryId),
            registryId, field == "revision" ? 2 : 1, original.PublishedAtUtc, publication,
            field == "digest" ? SecondRegistryDigest : FirstRegistryDigest, entries), UpdateSourceRegistryLoadIssue.None);
    }
}

/// <summary>Composes the real candidate source with the existing first-install orchestration and synthetic Setup ports.</summary>
public sealed class VersionManagementExperienceFreshInstallationCompositionTests : FirstInstallationTestSupport
{
    /// <summary>Setup inspects and reverifies through the experience, stopping before materialization when authority drifts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstInstallationUsesRealCandidateSourceAndRejectsInterveningDrift(bool drift)
    {
        string root = Root("composed-installation");
        string sourceRoot = SourcePath("composed-source");
        var registry = new SequenceRegistrySource(
            Registry(1, FirstRegistryDigest, (sourceRoot, UpdateSourceRegistryEntryStatus.Latest)),
            Registry(drift ? 2 : 1, FirstRegistryDigest, (sourceRoot, UpdateSourceRegistryEntryStatus.Latest)));
        using var sourceState = new MemoryStateStore(VersionManagementExperienceTestSupport.State(
            [VersionManagementExperienceTestSupport.Admission("0.10.5")], "0.10.5", "0.10.5"));
        var repository = new HealthyRepository();
        using VersionManagementExperience candidateSource = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", sourceState,
            new FixedCatalogSource(Catalog("0.10.6")), repository, registry);
        VersionManagerStateLoadResult[] loads = [MissingState(), MissingState(), MissingState()];
        using var state = new SequencedStateStore(loads);
        var roots = new SequencedRootProbe(ManagedInstallationRootStatus.Absent, ManagedInstallationRootStatus.Absent);
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        var materializer = new RecordingRootMaterializer(state);
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience setup = Create(state, roots, payload, candidateSource, materializer, handoff,
            timeProvider: IntegrationTestData.Clock());

        ManagedFirstInstallationPlanResult prepared = await setup.PrepareAsync(root, TestContext.Current.CancellationToken);
        ManagedFirstInstallationPlan plan = Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan);
        loads[2] = BoundState(root, plan.Candidate);
        ManagedFirstInstallationResult installed = await setup.InstallAndLaunchAsync(plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.ReadyToInstall, prepared.Outcome);
        Assert.Equal(drift ? ManagedFirstInstallationOutcome.SourceChanged : ManagedFirstInstallationOutcome.Completed, installed.Outcome);
        Assert.Equal(2, registry.LoadCount);
        Assert.Equal(drift ? 1 : 2, repository.VerifyCount);
        Assert.Equal(drift ? 0 : 1, materializer.MaterializeCount);
        Assert.Equal(drift ? 0 : 1, handoff.StartCount);
        Assert.Equal(drift ? 1 : 2, state.LeaseCount);
        Assert.False(state.LeaseActive);
        Assert.False(state.ContractViolated);
        Assert.False(materializer.Transaction.ContractViolated);
        Assert.Equal(0, sourceState.WriteLeaseCount);
        Assert.Equal(0, sourceState.LoadCount);
        Assert.Equal(0, sourceState.SaveCount);
        Assert.Equal(0, repository.InstallCount);
        Assert.Empty(repository.Deleted);
    }
}
