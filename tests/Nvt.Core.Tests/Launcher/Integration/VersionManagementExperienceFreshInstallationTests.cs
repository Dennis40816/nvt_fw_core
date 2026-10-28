// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.Launcher.Setup;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Frozen read-only first-install selection, failure precedence and complete candidate identity.</summary>
public sealed partial class VersionManagementExperienceFreshInstallationTests
{
    /// <summary>An absent registry fails without consulting state, catalogs or packages.</summary>
    [Fact]
    public async Task MissingRegistryReturnsNotConfiguredWithoutMutation()
    {
        using var store = new MemoryStateStore(FreshState());
        var repository = new HealthyRepository();
        var catalog = new PathCatalogSource();
        using VersionManagementExperience experience = CreateFreshExperience(store, null, catalog, repository);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            ExpectedFreshCandidate(), TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.RegistryNotConfigured, inspected);
        AssertFreshFailure(FreshInstallationCandidateIssue.RegistryNotConfigured, reverified);
        Assert.Empty(catalog.LoadedRoots);
        Assert.Equal(0, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>Every defined registry issue, including a failed None receipt, retains the frozen mapping.</summary>
    [Theory]
    [InlineData(UpdateSourceRegistryLoadIssue.None, FreshInstallationCandidateIssue.SourceRejected)]
    [InlineData(UpdateSourceRegistryLoadIssue.NotConfigured, FreshInstallationCandidateIssue.RegistryNotConfigured)]
    [InlineData(UpdateSourceRegistryLoadIssue.RegistryMissing, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateSourceRegistryLoadIssue.RegistryUnavailable, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateSourceRegistryLoadIssue.PermissionDenied, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateSourceRegistryLoadIssue.AuthenticationRequired, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateSourceRegistryLoadIssue.RegistryTimedOut, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateSourceRegistryLoadIssue.UnsafeLocator, FreshInstallationCandidateIssue.SourceRejected)]
    [InlineData(UpdateSourceRegistryLoadIssue.RegistryTooLarge, FreshInstallationCandidateIssue.SourceRejected)]
    [InlineData(UpdateSourceRegistryLoadIssue.InvalidManifest, FreshInstallationCandidateIssue.SourceRejected)]
    [InlineData(UpdateSourceRegistryLoadIssue.UnstableRead, FreshInstallationCandidateIssue.SourceRejected)]
    [InlineData(UpdateSourceRegistryLoadIssue.ReplicaConflict, FreshInstallationCandidateIssue.SourceRejected)]
    public async Task RegistryFailureUsesFrozenIssueMap(
        UpdateSourceRegistryLoadIssue issue, FreshInstallationCandidateIssue expected)
    {
        using var store = new MemoryStateStore(FreshState());
        var registry = new SequenceRegistrySource(new UpdateSourceRegistryLoadResult(null, issue));
        var repository = new HealthyRepository();
        var catalog = new PathCatalogSource();
        using VersionManagementExperience experience = CreateFreshExperience(store, registry, catalog, repository);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            ExpectedFreshCandidate(), TestContext.Current.CancellationToken);

        AssertFreshFailure(expected, inspected);
        AssertFreshFailure(expected, reverified);
        Assert.Equal(2, registry.LoadCount);
        Assert.Empty(catalog.LoadedRoots);
        Assert.Equal(0, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>An undefined issue throws the exact frozen exception in either operation.</summary>
    [Fact]
    public async Task UndefinedRegistryIssueRetainsLiteralException()
    {
        using var store = new MemoryStateStore(FreshState());
        var registry = new SequenceRegistrySource(new UpdateSourceRegistryLoadResult(null, (UpdateSourceRegistryLoadIssue)987));
        using VersionManagementExperience experience = CreateFreshExperience(store, registry);

        InvalidOperationException inspected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken).AsTask());
        InvalidOperationException reverified = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            experience.ReverifyFreshInstallationAsync(ExpectedFreshCandidate(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("Registry returned an undefined issue.", inspected.Message);
        Assert.Equal("Registry returned an undefined issue.", reverified.Message);
        Assert.Equal(2, registry.LoadCount);
    }

    /// <summary>A supported equal or newer package captures all thirteen identity fields and reverifies unchanged.</summary>
    [Theory]
    [InlineData("0.10.5")]
    [InlineData("0.10.6")]
    public async Task SupportedCandidateCapturesExactIdentityAndReverifiesWithoutMutation(string runningVersion)
    {
        UpdateCatalogVersionSnapshot package = Row("0.10.6");
        var verified = new VerifiedUpdateCandidate(package.Version, package.Identity, package.ReleaseNotes);
        var repository = new HealthyRepository(verificationResult: new(verified, ManagedVersionInstallIssue.None)
        {
            HasSupportedManagedLauncher = true,
        });
        var registry = new SequenceRegistrySource(FreshRegistry());
        using var store = new MemoryStateStore(FreshState());
        using VersionManagementExperience experience = CreateFreshExperience(store, registry,
            new FixedCatalogSource(UpdateCatalogSnapshot.Create(IntegrationTestData.Limits, [package])), repository, runningVersion);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidate candidate = Assert.IsType<FreshInstallationCandidate>(inspected.Candidate);
        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            candidate, TestContext.Current.CancellationToken);

        Assert.True(inspected.IsSuccess);
        Assert.Equal(FreshInstallationCandidateIssue.None, inspected.Issue);
        Assert.Equal(ExpectedFreshCandidate(package).Identity, candidate.Identity);
        Assert.Same(package, candidate.Package);
        Assert.Equal(verified.Version, candidate.Package.Version);
        Assert.Equal(verified.AdmissionIdentity, candidate.Package.Identity);
        Assert.Equal(verified.ReleaseNotes, candidate.Package.ReleaseNotes);
        Assert.True(reverified.IsSuccess);
        Assert.Equal(candidate.Identity, reverified.Candidate!.Identity);
        Assert.Same(package, reverified.Candidate.Package);
        Assert.Equal(2, registry.LoadCount);
        Assert.Equal(2, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>A fully admitted older newest package is skipped without being classified as rejected.</summary>
    [Fact]
    public async Task OlderNewestPackageLeavesCandidateUnavailable()
    {
        using var store = new MemoryStateStore(FreshState());
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(FreshRegistry()), repository: repository, runningVersion: "0.10.7");

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.CandidateUnavailable, inspected);
        Assert.Equal(1, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>A verified package without a supported launcher is rejected, and cannot revalidate a prior token.</summary>
    [Fact]
    public async Task UnsupportedLauncherIsRejectedAndChangesCapturedSource()
    {
        FreshInstallationCandidate expected = ExpectedFreshCandidate();
        var repository = new HealthyRepository(verificationResult: new(
            new(expected.Package.Version, expected.Package.Identity, expected.Package.ReleaseNotes),
            ManagedVersionInstallIssue.None));
        using var store = new MemoryStateStore(FreshState());
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(FreshRegistry()), repository: repository);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            expected, TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.SourceRejected, inspected);
        AssertFreshFailure(FreshInstallationCandidateIssue.SourceChanged, reverified);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>Catalog observation failures are unavailable; authority failures reject or invalidate the captured source.</summary>
    [Theory]
    [InlineData(UpdateCatalogLoadIssue.SourceMissing, FreshInstallationCandidateIssue.SourceUnavailable, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateCatalogLoadIssue.SourceUnavailable, FreshInstallationCandidateIssue.SourceUnavailable, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateCatalogLoadIssue.PermissionDenied, FreshInstallationCandidateIssue.SourceUnavailable, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(UpdateCatalogLoadIssue.InvalidManifest, FreshInstallationCandidateIssue.SourceRejected, FreshInstallationCandidateIssue.SourceChanged)]
    public async Task CatalogFailuresPreserveInspectionAndReverificationMapping(
        UpdateCatalogLoadIssue issue, FreshInstallationCandidateIssue inspectionIssue, FreshInstallationCandidateIssue reverifyIssue)
    {
        using var store = new MemoryStateStore(FreshState());
        var catalog = new PathCatalogSource((SourcePath("fresh"), new(null, issue)));
        var repository = new HealthyRepository();
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(FreshRegistry()), catalog, repository);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            ExpectedFreshCandidate(), TestContext.Current.CancellationToken);

        AssertFreshFailure(inspectionIssue, inspected);
        AssertFreshFailure(reverifyIssue, reverified);
        Assert.Equal(0, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>A missing package or unavailable package state remains unavailable; a mismatch invalidates authority.</summary>
    [Theory]
    [InlineData(ManagedVersionInstallIssue.PackageUnavailable, FreshInstallationCandidateIssue.SourceUnavailable, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(ManagedVersionInstallIssue.StateUnavailable, FreshInstallationCandidateIssue.SourceUnavailable, FreshInstallationCandidateIssue.SourceUnavailable)]
    [InlineData(ManagedVersionInstallIssue.PackageMismatch, FreshInstallationCandidateIssue.SourceRejected, FreshInstallationCandidateIssue.SourceChanged)]
    public async Task PackageFailuresPreserveInspectionAndReverificationMapping(
        ManagedVersionInstallIssue issue, FreshInstallationCandidateIssue inspectionIssue, FreshInstallationCandidateIssue reverifyIssue)
    {
        using var store = new MemoryStateStore(FreshState());
        var repository = new HealthyRepository(verificationResult: new(null, issue));
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(FreshRegistry()), repository: repository);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            ExpectedFreshCandidate(), TestContext.Current.CancellationToken);

        AssertFreshFailure(inspectionIssue, inspected);
        AssertFreshFailure(reverifyIssue, reverified);
        Assert.Equal(2, repository.VerifyCount);
        AssertNoFreshMutation(store, repository);
    }

    /// <summary>Unavailable wins over rejected independently of their automatic entry order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableWinsOverRejected(bool unavailableFirst)
    {
        string latest = SourcePath("fresh");
        string available = SourcePath("fallback");
        var catalog = new PathCatalogSource(
            (latest, new(null, unavailableFirst ? UpdateCatalogLoadIssue.SourceMissing : UpdateCatalogLoadIssue.InvalidManifest)),
            (available, new(null, unavailableFirst ? UpdateCatalogLoadIssue.InvalidManifest : UpdateCatalogLoadIssue.SourceMissing)));
        using var store = new MemoryStateStore(FreshState());
        using VersionManagementExperience experience = CreateFreshExperience(store,
            new SequenceRegistrySource(Registry(1, FirstRegistryDigest,
                (latest, UpdateSourceRegistryEntryStatus.Latest), (available, UpdateSourceRegistryEntryStatus.Available))), catalog);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);

        AssertFreshFailure(FreshInstallationCandidateIssue.SourceUnavailable, inspected);
        Assert.Equal([latest, available], catalog.LoadedRoots);
    }

    /// <summary>Latest precedes Available entries in declaration order, and Deprecated is never inspected.</summary>
    [Fact]
    public async Task SelectionFallsThroughInFrozenOrderWithoutDeprecatedSources()
    {
        string latest = SourcePath("fresh");
        string first = SourcePath("first-fallback");
        string second = SourcePath("second-fallback");
        string deprecated = SourcePath("deprecated");
        var registry = new SequenceRegistrySource(Registry(1, FirstRegistryDigest,
            (deprecated, UpdateSourceRegistryEntryStatus.Deprecated),
            (first, UpdateSourceRegistryEntryStatus.Available),
            (latest, UpdateSourceRegistryEntryStatus.Latest),
            (second, UpdateSourceRegistryEntryStatus.Available)));
        var catalog = new PathCatalogSource(
            (second, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)),
            (deprecated, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None)));
        using var store = new MemoryStateStore(FreshState());
        using VersionManagementExperience experience = CreateFreshExperience(store, registry, catalog);

        FreshInstallationCandidateResult inspected = await experience.InspectFreshInstallationAsync(TestContext.Current.CancellationToken);
        FreshInstallationCandidateResult reverified = await experience.ReverifyFreshInstallationAsync(
            Assert.IsType<FreshInstallationCandidate>(inspected.Candidate), TestContext.Current.CancellationToken);

        Assert.True(inspected.IsSuccess);
        Assert.Equal(second, inspected.Candidate!.Identity.SourceRoot);
        Assert.Equal(FreshInstallationSourceStatus.Available, inspected.Candidate.Identity.SourceStatus);
        Assert.True(reverified.IsSuccess);
        Assert.Equal([latest, first, second, second], catalog.LoadedRoots);
        Assert.Equal(2, registry.LoadCount);
    }

    private static VersionManagerState FreshState() => State([Admission("0.10.5")], "0.10.5", "0.10.5");

    private static UpdateSourceRegistryLoadResult FreshRegistry() => Registry(1, FirstRegistryDigest,
        (SourcePath("fresh"), UpdateSourceRegistryEntryStatus.Latest));

    private static VersionManagementExperience CreateFreshExperience(
        MemoryStateStore store, IUpdateSourceRegistry? registry, IUpdateCatalogSource? catalog = null,
        HealthyRepository? repository = null, string runningVersion = "0.10.5") => VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse(runningVersion), "managed-root", store,
            catalog ?? new FixedCatalogSource(Catalog("0.10.6")), repository ?? new HealthyRepository(), registry);

    private static FreshInstallationCandidate ExpectedFreshCandidate(UpdateCatalogVersionSnapshot? package = null)
    {
        package ??= Row("0.10.6");
        string root = SourcePath("fresh");
        return new(new(128, "fixture-registry", 1, FirstRegistryDigest, 1, ManagedAppVersion.Parse("0.10.6"),
            CatalogContentDigest, Path.Combine(root, "update-catalog.v1.json"), root, FreshInstallationSourceStatus.Latest,
            package.PackagePath.Value!, package.PackageSize, package.PackageSha256, package.ReleaseManifestSha256),
            package, new(package.Version, package.Identity, package.ReleaseNotes));
    }

    private static void AssertFreshFailure(FreshInstallationCandidateIssue issue, FreshInstallationCandidateResult outcome)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Null(outcome.Candidate);
        Assert.Equal(issue, outcome.Issue);
    }

    private static void AssertNoFreshMutation(MemoryStateStore store, HealthyRepository repository)
    {
        Assert.Equal(0, store.WriteLeaseCount);
        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, repository.InstallCount);
        Assert.Empty(repository.Deleted);
        Assert.False(store.HoldsWriter);
    }
}
