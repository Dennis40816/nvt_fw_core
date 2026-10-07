// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Characterizes frozen READY and writer budgets and every allowed positive timeout override.</summary>
public sealed class CoordinationBoundaryTests
{
    private static readonly ManagedAppVersion Version = ManagedAppVersion.Parse("1.0.0");
    private static readonly ManagedVersionAdmission Admission = new(Version, "synthetic", new string('a', 64));
    private static readonly ManagedLauncherIdentity Launcher = ManagedLauncherIdentity.Create(CoordinationFixture.Product,
        200_000_000, Version, Admission.AdmissionIdentity, Admission.ReleaseManifestSha256, Version, 1,
        CoordinationFixture.Product.LauncherExecutableRelativePath, 1, new string('b', 64));
    private static readonly ManagedLauncherIdentity[] Launchers = [Launcher];

    /// <summary>The one-tick positive minimum and neighbors of the 20-second default reach both process ports unchanged.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(199_999_999L)]
    [InlineData(200_000_000L)]
    [InlineData(200_000_001L)]
    [InlineData(long.MaxValue)]
    public async Task PositiveReadyOverridesRemainUnchanged(long ticks)
    {
        string root = Path.GetFullPath("synthetic-boundary-root");
        using var app = new CoordinationStateStore(State(root));
        var process = new TraceApplicationProcess(app);
        var repository = new TraceApplicationRepository(app, CoordinationFixture.Product);
        ManagedLauncherResult appResult = await new ManagedActivationCoordinator(root, app, repository, process, TimeSpan.FromTicks(ticks))
            .RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedLauncherOutcome.Ready, appResult.Outcome);
        Assert.Equal(TimeSpan.FromTicks(ticks), Assert.Single(process.Deadlines));
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(app.Waits));
        var launcherState = new CoordinationLauncherStateStore(app);
        var launcherProcess = new TraceLauncherProcess(app);
        LauncherBootstrapResult launcherResult = await new LauncherBootstrapCoordinator(root, app.StatePath, app, launcherState,
            new TraceLauncherRepository(app, Launchers), launcherProcess, TimeSpan.FromTicks(ticks))
            .RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LauncherBootstrapOutcome.Ready, launcherResult.Outcome);
        Assert.Equal(TimeSpan.FromTicks(ticks), Assert.Single(launcherProcess.Deadlines));
        Assert.All(app.Waits.Skip(1), wait => Assert.Equal(TimeSpan.FromMilliseconds(250), wait));
    }

    /// <summary>Null overrides preserve the exact 20-second READY and five-second/250-ms writer defaults.</summary>
    [Fact]
    public async Task DefaultDeadlinesAndWriterBudgetsAreExact()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), ManagedActivationCoordinator.DefaultReadyDeadline);
        Assert.Equal(TimeSpan.FromSeconds(20), LauncherBootstrapCoordinator.DefaultReadyDeadline);
        Assert.Equal(TimeSpan.FromSeconds(5), ManagedActivationCoordinator.DefaultWriterLeaseTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(250), LauncherBootstrapCoordinator.StartupWriterLeaseTimeout);
        string root = Path.GetFullPath("synthetic-boundary-root");
        using var app = new CoordinationStateStore(State(root));
        var process = new TraceApplicationProcess(app);
        _ = await new ManagedActivationCoordinator(root, app, new TraceApplicationRepository(app, CoordinationFixture.Product), process)
            .RunAsync(TestContext.Current.CancellationToken);
        var launcherProcess = new TraceLauncherProcess(app);
        _ = await new LauncherBootstrapCoordinator(root, app.StatePath, app, new CoordinationLauncherStateStore(app),
            new TraceLauncherRepository(app, Launchers), launcherProcess).RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(process.Deadlines));
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(launcherProcess.Deadlines));
        Assert.Equal(TimeSpan.FromSeconds(5), app.Waits[0]);
        Assert.All(app.Waits.Skip(1), wait => Assert.Equal(TimeSpan.FromMilliseconds(250), wait));
    }

    /// <summary>Zero and negative READY overrides fail at the frozen constructor check without acquiring a writer.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-10_000L)]
    [InlineData(long.MinValue)]
    public void NonpositiveReadyOverridesFailInFrozenOrder(long ticks)
    {
        string root = Path.GetFullPath("synthetic-boundary-root");
        using var app = new CoordinationStateStore(State(root));
        var repo = new TraceApplicationRepository(app, CoordinationFixture.Product);
        var process = new TraceApplicationProcess(app);
        Assert.Equal("_readyDeadline", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ManagedActivationCoordinator(root, app, repo, process, TimeSpan.FromTicks(ticks))).ParamName);
        Assert.Equal("_readyDeadline", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LauncherBootstrapCoordinator(root, app.StatePath, app, new CoordinationLauncherStateStore(app),
                new TraceLauncherRepository(app, Launchers), new TraceLauncherProcess(app), TimeSpan.FromTicks(ticks))).ParamName);
        Assert.Empty(app.Waits);
        Assert.Equal("stateStore", Assert.Throws<ArgumentNullException>(() =>
            new ManagedActivationCoordinator(root, null!, repo, process, TimeSpan.FromTicks(ticks))).ParamName);
        Assert.Equal("appStateStore", Assert.Throws<ArgumentNullException>(() =>
            new LauncherBootstrapCoordinator(root, app.StatePath, null!, new CoordinationLauncherStateStore(app),
                new TraceLauncherRepository(app, Launchers), new TraceLauncherProcess(app), TimeSpan.FromTicks(ticks))).ParamName);
    }

    /// <summary>Seed writer waits reject zero and negative values before their read-only preflight.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public async Task SeedWriterOverrideMustBePositive(long ticks)
    {
        string root = Path.GetFullPath("synthetic-boundary-root");
        using var app = new CoordinationStateStore(State(root));
        var bootstrapper = new ManagedVersionSeedBootstrapper(root, app, app,
            new TraceApplicationRepository(app, CoordinationFixture.Product));
        ArgumentOutOfRangeException error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            bootstrapper.EnsureInitializedAsync(TimeSpan.FromTicks(ticks), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("writerLeaseTimeout", error.ParamName);
        Assert.Empty(app.Trace);
    }

    /// <summary>The smallest positive seed wait and neighbors of 250 ms preserve the supplied override.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(2_499_999L)]
    [InlineData(2_500_000L)]
    [InlineData(2_500_001L)]
    [InlineData(49_999_999L)]
    [InlineData(50_000_000L)]
    [InlineData(50_000_001L)]
    [InlineData(long.MaxValue)]
    public async Task PositiveSeedWriterOverridesRemainUnchanged(long ticks)
    {
        string root = Path.GetFullPath("synthetic-boundary-root");
        using var destination = new CoordinationStateStore(State(root));
        destination.LoadIssue = VersionManagerStateLoadIssue.Missing;
        using var seed = new CoordinationStateStore(ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(Admission));
        var bootstrapper = new ManagedVersionSeedBootstrapper(root, destination, seed,
            new TraceApplicationRepository(destination, CoordinationFixture.Product));
        Assert.Equal(ManagedVersionSeedOutcome.Seeded, await bootstrapper.EnsureInitializedAsync(
            TimeSpan.FromTicks(ticks), TestContext.Current.CancellationToken));
        Assert.Equal(TimeSpan.FromTicks(ticks), Assert.Single(destination.Waits));
        Assert.Equal(1, destination.SaveCount);
    }

    /// <summary>Canonical seed admission cardinality is fixed at one, with zero and two rejected.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CanonicalSeedAdmissionCountHasExactBoundary(int count)
    {
        ManagedVersionAdmission second = new(ManagedAppVersion.Parse("1.0.1"), "second", new string('b', 64));
        ManagedVersionAdmission[] admissions = count switch { 0 => [], 1 => [Admission], _ => [Admission, second] };
        VersionManagerState state = VersionManagerState.Create(null, count == 0 ? null : Version, count == 0 ? null : Version,
            admissions, null, null, false);
        Assert.Equal(count == 1, ManagedVersionSeedPolicy.IsCanonicalFirstRunSeed(state));
    }

    /// <summary>The frozen seed inventory accepts exactly one healthy row, with zero and two rejected.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CanonicalSeedInventoryCountHasExactBoundary(int count)
    {
        string root = Path.GetFullPath("synthetic-boundary-root");
        using var destination = new CoordinationStateStore(State(root)) { LoadIssue = VersionManagerStateLoadIssue.Missing };
        using var seed = new CoordinationStateStore(ManagedVersionSeedPolicy.CreateCanonicalFirstRunSeed(Admission));
        ManagedVersionAdmission second = new(ManagedAppVersion.Parse("1.0.1"), "second", new string('b', 64));
        ManagedVersionAdmission[] admissions = count switch { 0 => [], 1 => [Admission], _ => [Admission, second] };
        ManagedVersionInventory inventory = ManagedVersionInventory.Create(admissions.Select(a => new InstalledVersionSnapshot(
            a.Version, a.AdmissionIdentity, ManagedVersionIntegrity.Healthy, null, a.Version == Version, a.Version == Version,
            ManagedVersionAdmissionState.Admitted, a)));
        var bootstrapper = new ManagedVersionSeedBootstrapper(root, destination, seed, new FixedInventoryRepository(inventory));
        Assert.Equal(count == 1 ? ManagedVersionSeedOutcome.Seeded : ManagedVersionSeedOutcome.DamagedSeedPayload,
            await bootstrapper.EnsureInitializedAsync(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken));
        Assert.Equal(count == 1 ? 1 : 0, destination.SaveCount);
    }

    private sealed class FixedInventoryRepository(ManagedVersionInventory inventory) : IManagedVersionRepository
    {
        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string managedRoot, IReadOnlyList<ManagedVersionAdmission> admissions,
            ManagedAppVersion? activeVersion, ManagedAppVersion? lastKnownGoodVersion, ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken) => ValueTask.FromResult(ManagedVersionInventoryReadResult.Success(inventory));
        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(string sourceRoot, UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot, UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string managedRoot, ManagedVersionAdmission admission, ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static VersionManagerState State(string root) => VersionManagerState.Create(null, Version, Version,
        [Admission], null, null, false, managedRootIdentity: root);
}
