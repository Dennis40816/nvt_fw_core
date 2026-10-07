// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class ManagedActivationCoordinatorTests
{
    /// <summary>Unconfirmed candidate cleanup preserves the recoverable journal and starts no fallback.</summary>
    [Fact]
    public async Task CandidateTerminationUnconfirmedPreservesJournalWithoutStartingFallback()
    {
        ManagedAppVersion candidate = ManagedAppVersion.Parse("0.10.6");
        VersionManagerState requested = VersionActivationPolicy.BeginActivation(State(), candidate);
        var store = new FakeStateStore(requested);
        var process = new FakeProcess(ManagedProcessStartOutcome.TerminationUnconfirmed);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            store,
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.Equal(["0.10.6"], process.Starts);
        Assert.Equal(VersionActivationPhase.CandidateLaunchRecorded, store.State.PendingActivation?.Phase);
        Assert.Equal(candidate, store.State.PendingActivation?.CandidateVersion);
    }

    /// <summary>A durably quarantined active version cannot be launched again.</summary>
    [Fact]
    public async Task FailedActiveVersionDoesNotLaunch()
    {
        ManagedAppVersion failed = ManagedAppVersion.Parse("0.10.5");
        VersionManagerState state = VersionManagerState.Create(
            updateSource: null,
            activeVersion: failed,
            lastKnownGoodVersion: failed,
            admissions: [Admission("0.10.5"), Admission("0.10.6")],
            pendingActivation: null,
            failedActivationVersion: failed,
            retentionReviewDue: false,
            managedRootIdentity: "managed");
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(state),
            new HealthyRepository(),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.DamagedVersion, result.Outcome);
        Assert.Equal(failed, result.FailedVersion);
        Assert.Empty(process.Starts);
    }

    /// <summary>A healthy self-admitted directory is not launcher authority.</summary>
    [Fact]
    public async Task HealthyUnadmittedInventoryDoesNotLaunch()
    {
        ManagedVersionAdmission admission = Admission("0.10.5");
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(State()),
            new FixedInventoryRepository(ManagedVersionInventoryReadResult.Success(
                ManagedVersionInventory.Create(
                [
                    new(
                        admission.Version,
                        admission.AdmissionIdentity,
                        ManagedVersionIntegrity.Healthy,
                        DamageReason: null,
                        IsActive: true,
                        IsLastKnownGood: true,
                        ManagedVersionAdmissionState.Unadmitted,
                        admission),
                ]))),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.DamagedVersion, result.Outcome);
        Assert.Empty(process.Starts);
    }

    /// <summary>A healthy row cannot launch under another admission identity.</summary>
    [Fact]
    public async Task MismatchedAdmissionIdentityDoesNotLaunch()
    {
        ManagedVersionAdmission admission = Admission("0.10.5");
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(State()),
            new FixedInventoryRepository(ManagedVersionInventoryReadResult.Success(
                ManagedVersionInventory.Create(
                [
                    new(
                        admission.Version,
                        "different-admission",
                        ManagedVersionIntegrity.Healthy,
                        DamageReason: null,
                        IsActive: true,
                        IsLastKnownGood: true),
                ]))),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.DamagedVersion, result.Outcome);
        Assert.Empty(process.Starts);
    }

    /// <summary>An unavailable whole inventory never launches even an admitted active version.</summary>
    [Fact]
    public async Task UnavailableInventoryReturnsStateUnavailableWithoutStartingProcess()
    {
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            new FakeStateStore(State()),
            new FixedInventoryRepository(ManagedVersionInventoryReadResult.Unavailable()),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(ManagedAppVersion.Parse("0.10.5"), result.FailedVersion);
        Assert.Empty(process.Starts);
    }

    /// <summary>An unavailable fallback inventory preserves the recorded rollback and starts nothing.</summary>
    [Fact]
    public async Task RollbackLaunchRecordedInventoryUnavailablePreservesJournalWithoutStartingProcess()
    {
        ManagedAppVersion failed = ManagedAppVersion.Parse("0.10.6");
        VersionManagerState requested = VersionActivationPolicy.BeginActivation(State(), failed);
        VersionManagerState candidateRecorded = VersionActivationPolicy.RecordCandidateLaunch(requested);
        VersionManagerState rollbackRecorded = VersionActivationPolicy.RecordRollbackLaunch(
            candidateRecorded,
            failed).State;
        var store = new FakeStateStore(rollbackRecorded);
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var coordinator = new ManagedActivationCoordinator(
            "managed",
            store,
            new FixedInventoryRepository(ManagedVersionInventoryReadResult.Unavailable()),
            process,
            TimeSpan.FromSeconds(1));

        ManagedLauncherResult result = await coordinator.RunAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, result.Outcome);
        Assert.Null(result.RunningVersion);
        Assert.Equal(failed, result.FailedVersion);
        Assert.Empty(process.Starts);
        Assert.Equal(rollbackRecorded, store.State);
        Assert.Equal(VersionActivationPhase.RollbackLaunchRecorded, store.State.PendingActivation?.Phase);
    }

    private sealed class FixedInventoryRepository(ManagedVersionInventoryReadResult result)
        : IManagedVersionRepository
    {
        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot,
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
            string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions,
            ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion,
            ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}

public sealed partial class LauncherBootstrapCoordinatorTests
{
    /// <summary>Unconfirmed candidate cleanup preserves the exact journal and starts no fallback.</summary>
    [Fact]
    public async Task CandidateTerminationUnconfirmedPreservesJournalWithoutStartingFallback()
    {
        var launcherStore = new RecordingLauncherStateStore(
            LauncherBootstrapState.Create(Root, Launcher100, Launcher100, pending: null, failed: null));
        var process = new RecordingLauncherProcess(LauncherProcessStartOutcome.TerminationUnconfirmed);

        LauncherBootstrapResult result = await Create(
            new RecordingAppStateStore(AppState(App101, App100)),
            launcherStore,
            new RecordingLauncherRepository(Launcher101, Launcher100),
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherBootstrapOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.Equal([Launcher101], process.Started);
        Assert.Equal(LauncherActivationPhase.CandidateLaunchRecorded, launcherStore.Current!.Pending!.Phase);
    }
}
