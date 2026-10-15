// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Tests the genuinely-absent-only first-install orchestration.</summary>
public sealed partial class ManagedFirstInstallationExperienceTests : FirstInstallationTestSupport
{
    /// <summary>Residue is rejected before payload, Registry, Catalog, or package access.</summary>
    [Fact]
    public async Task PrepareWithResidueTouchesNoPayloadOrSource()
    {
        string root = Root("prepare-residue");
        using SequencedStateStore state = new(MissingState());
        SequencedRootProbe roots = new(ManagedInstallationRootStatus.Residue);
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource source = new(Candidate());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            roots,
            payload,
            source,
            new RecordingRootMaterializer(state),
            handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);

        Assert.Null(result.Plan);
        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(0, payload.InspectCount);
        Assert.Equal(0, source.InspectCount);
    }

    /// <summary>The happy path rechecks under one lease, releases it before start, and completes after READY.</summary>
    [Fact]
    public async Task CompletedInstallationUsesExactRechecksAndLeaseOrdering()
    {
        string root = Root("completed");
        FreshInstallationCandidate candidate = Candidate();
        using SequencedStateStore state = new(
            MissingState(),
            MissingState(),
            BoundState(root, candidate));
        SequencedRootProbe roots = new(
            ManagedInstallationRootStatus.Absent,
            ManagedInstallationRootStatus.Absent);
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource source = new(candidate);
        RecordingRootMaterializer materializer = new(state);
        using SetupBootstrapHandoff handoff = new(state);
        payload.Observe = state.Events.Add;
        ManagedFirstInstallationExperience experience = Create(
            state,
            roots,
            payload,
            source,
            materializer,
            handoff);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
        Assert.Equal(2, state.LeaseCount);
        Assert.Equal(0, state.SaveCount);
        Assert.Equal(1, payload.CaptureCount);
        Assert.Equal(1, source.ReverifyCount);
        Assert.Equal(2, materializer.AdmissionCount);
        Assert.Equal(1, materializer.MaterializeCount);
        Assert.Equal(1, materializer.Transaction.RecordLaunchCount);
        Assert.Equal(1, materializer.Transaction.BootstrapLeaseAcquireCount);
        Assert.Equal(1, materializer.Transaction.CompleteCount);
        Assert.Equal(1, handoff.StartCount);
        Assert.Same(materializer.Transaction.LastBootstrapLease, handoff.ReceivedLease);
        Assert.True(materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Equal(1, materializer.Transaction.DisposeCount);
        Assert.Equal(
            [
                "payload-inspect",
                "writer-acquire:1",
                "payload-capture",
                "record-bootstrap-launch",
                "acquire-bootstrap-lease",
                "payload-dispose",
                "writer-release:1",
                "handoff-owned-lease",
                "admitted",
                "ready",
                "writer-acquire:2",
                "complete",
                "writer-release:2",
            ],
            state.Events);
    }

    /// <summary>Determinate progress is stage-local, bounded, and owns the sole percentage.</summary>
    [Fact]
    public void FirstInstallationProgressRejectsInvalidCountersAndCalculatesOnePercent()
    {
        var progress = new ManagedFirstInstallationProgress(
            ManagedFirstInstallationProgressStage.ReadingPackage,
            42,
            100);

        Assert.Equal(42, progress.Percent);
        Assert.Null(ManagedFirstInstallationProgress.Indeterminate(
            ManagedFirstInstallationProgressStage.RevalidatingSource).Percent);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedFirstInstallationProgress(
            ManagedFirstInstallationProgressStage.ReadingPackage,
            -1,
            100));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedFirstInstallationProgress(
            ManagedFirstInstallationProgressStage.ReadingPackage,
            1,
            null));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedFirstInstallationProgress(
            ManagedFirstInstallationProgressStage.ReadingPackage,
            101,
            100));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedFirstInstallationProgress(
            ManagedFirstInstallationProgressStage.ReadingPackage,
            0,
            0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedFirstInstallationProgress(
            (ManagedFirstInstallationProgressStage)999,
            0,
            null));
    }

    /// <summary>The one install owner preserves measured materializer progress and stage order.</summary>
    [Fact]
    public async Task CompletedInstallationProjectsOneTypedProgressSequence()
    {
        string root = Root("completed-progress");
        FreshInstallationCandidate candidate = Candidate();
        using SequencedStateStore state = new(
            MissingState(),
            MissingState(),
            BoundState(root, candidate));
        RecordingRootMaterializer materializer = new(state)
        {
            ProgressUpdates =
            [
                new(ManagedFirstInstallationProgressStage.ReadingPackage, 42, 100),
                new(ManagedFirstInstallationProgressStage.InstallingPackage, 1, 2),
            ],
        };
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(
                ManagedInstallationRootStatus.Absent,
                ManagedInstallationRootStatus.Absent),
            payload,
            new RecordingCandidateSource(candidate),
            materializer,
            handoff);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);
        var updates = new List<ManagedFirstInstallationProgress>();

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan),
            new InlineProgress<ManagedFirstInstallationProgress>(updates.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
        Assert.Equal(
            [
                ManagedFirstInstallationProgressStage.RevalidatingSource,
                ManagedFirstInstallationProgressStage.ReadingPackage,
                ManagedFirstInstallationProgressStage.InstallingPackage,
                ManagedFirstInstallationProgressStage.FinalizingInstallation,
                ManagedFirstInstallationProgressStage.StartingApplication,
            ],
            updates.Select(static update => update.Stage));
        Assert.Equal([null, 42, 50, null, null], updates.Select(static update => update.Percent));
    }

    /// <summary>A presentation observer cannot change installation outcome or ordering.</summary>
    [Fact]
    public async Task ThrowingProgressObserverCannotFailInstallation()
    {
        string root = Root("throwing-progress");
        FreshInstallationCandidate candidate = Candidate();
        using SequencedStateStore state = new(
            MissingState(),
            MissingState(),
            BoundState(root, candidate));
        RecordingRootMaterializer materializer = new(state)
        {
            ProgressUpdates =
            [
                new(ManagedFirstInstallationProgressStage.ReadingPackage, 1, 2),
            ],
        };
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(
                ManagedInstallationRootStatus.Absent,
                ManagedInstallationRootStatus.Absent),
            payload,
            new RecordingCandidateSource(candidate),
            materializer,
            handoff);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);

        var progress = new ThrowingProgress<ManagedFirstInstallationProgress>();
        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan),
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
        Assert.Equal(4, progress.ReportCount);
        Assert.Equal(1, materializer.MaterializeCount);
        Assert.False(materializer.Transaction.ContractViolated);
    }

    /// <summary>Authority drift stops before any root materialization or Bootstrap start.</summary>
    [Fact]
    public async Task SourceDriftStopsBeforeRootMutation()
    {
        string root = Root("source-drift");
        FreshInstallationCandidate candidate = Candidate();
        using SequencedStateStore state = new(MissingState(), MissingState());
        RecordingCandidateSource source = new(candidate)
        {
            ReverifyIssue = FreshInstallationCandidateIssue.SourceChanged,
        };
        RecordingRootMaterializer materializer = new(state);
        using SetupBootstrapHandoff handoff = new(state);
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(
                ManagedInstallationRootStatus.Absent,
                ManagedInstallationRootStatus.Absent),
            payload,
            source,
            materializer,
            handoff);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.SourceChanged, result.Outcome);
        Assert.Equal(0, materializer.MaterializeCount);
        Assert.Equal(0, handoff.StartCount);
    }

    /// <summary>A promoted root remains recovery evidence when exact Bootstrap start fails.</summary>
    [Fact]
    public async Task BootstrapStartFailureDoesNotClearTransactionMarker()
    {
        string root = Root("start-failed");
        FreshInstallationCandidate candidate = Candidate();
        using SequencedStateStore state = new(MissingState(), MissingState());
        RecordingRootMaterializer materializer = new(state);
        using SetupBootstrapHandoff handoff = new(state)
        {
            StartIssue = ImmutableBootstrapStartIssue.StartFailed,
        };
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(
                ManagedInstallationRootStatus.Absent,
                ManagedInstallationRootStatus.Absent),
            payload,
            new RecordingCandidateSource(candidate),
            materializer,
            handoff);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(1, materializer.Transaction.RecordLaunchCount);
        Assert.Equal(0, materializer.Transaction.CompleteCount);
    }

    /// <summary>
    /// Cancellation while durably recording Bootstrap launch is retained at the exact
    /// post-promotion/pre-record boundary and never starts Bootstrap.
    /// </summary>
    [Fact]
    public async Task RecordBootstrapLaunchCancellationReportsPostPromotionBoundary()
    {
        using var cancellation = new CancellationTokenSource();
        string root = Root("record-launch-cancelled");
        FreshInstallationCandidate candidate = Candidate();
        using SequencedStateStore state = new(MissingState(), MissingState());
        RecordingRootMaterializer materializer = new(state);
        materializer.Transaction.RecordLaunchAction = cancellation.Cancel;
        using SetupBootstrapHandoff handoff = new(state);
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(
                ManagedInstallationRootStatus.Absent,
                ManagedInstallationRootStatus.Absent),
            payload,
            new RecordingCandidateSource(candidate),
            materializer,
            handoff);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan),
            cancellation.Token);

        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.PostPromotion,
                ManagedFirstInstallationLaunchIssue.Cancelled),
            result.LaunchFailure);
        Assert.Equal(1, materializer.Transaction.RecordLaunchCount);
        Assert.Equal(0, handoff.StartCount);
        Assert.Equal(0, materializer.Transaction.CompleteCount);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }

    // Guard: the single installation invocation serializes callbacks and the final assertion.
    private sealed class ThrowingProgress<T> : IProgress<T>
    {
        internal int ReportCount { get; private set; }

        public void Report(T value)
        {
            ReportCount++;
            throw new InvalidOperationException("Presentation observer must not control installation.");
        }
    }

}
