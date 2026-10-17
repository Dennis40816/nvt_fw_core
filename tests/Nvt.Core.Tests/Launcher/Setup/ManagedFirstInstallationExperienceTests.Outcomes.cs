// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

public sealed partial class ManagedFirstInstallationExperienceTests
{
    /// <summary>A pre-cancelled plan attempt is typed and touches no external source.</summary>
    [Fact]
    public async Task PrepareAlreadyCancelledTouchesNoPayloadOrCandidate()
    {
        using SequencedStateStore state = new(MissingState());
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource candidate = new(Candidate());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent),
            payload,
            candidate,
            new RecordingRootMaterializer(state),
            handoff);
        using var cancellation = new CancellationTokenSource();
        Cancel(cancellation);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root("prepare-cancelled"),
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, state.LoadCount);
        Assert.Equal(0, payload.InspectCount);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>State failures and an invalid destination stop before root or source access.</summary>
    [Theory]
    [InlineData(VersionManagerStateLoadIssue.Invalid, ManagedFirstInstallationOutcome.RecoveryRequired)]
    [InlineData(VersionManagerStateLoadIssue.ManagedRootMismatch, ManagedFirstInstallationOutcome.RecoveryRequired)]
    [InlineData(VersionManagerStateLoadIssue.Unavailable, ManagedFirstInstallationOutcome.StateUnavailable)]
    public async Task PrepareMapsStateFailureWithoutRootOrSourceAccess(
        VersionManagerStateLoadIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using SequencedStateStore state = new(new VersionManagerStateLoadResult(null, issue));
        SequencedRootProbe roots = new(ManagedInstallationRootStatus.Absent);
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource candidate = new(Candidate());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            roots,
            payload,
            candidate,
            new RecordingRootMaterializer(state),
            handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root($"prepare-state-{issue}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, payload.InspectCount);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>An invalid destination is rejected before all adapter access.</summary>
    [Fact]
    public async Task PrepareInvalidDestinationTouchesNoAdapter()
    {
        using SequencedStateStore state = new(MissingState());
        SequencedRootProbe roots = new(ManagedInstallationRootStatus.Absent);
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource candidate = new(Candidate());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            roots,
            payload,
            candidate,
            new RecordingRootMaterializer(state),
            handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            "relative-root",
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InvalidDestination, result.Outcome);
        Assert.Equal(0, state.LoadCount);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, payload.InspectCount);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>An overlong absolute destination is typed invalid before any port is touched.</summary>
    [Fact]
    public async Task PrepareOverlongDestinationTouchesNoAdapter()
    {
        using SequencedStateStore state = new(MissingState());
        SequencedRootProbe roots = new(ManagedInstallationRootStatus.Absent);
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource candidate = new(Candidate());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            roots,
            payload,
            candidate,
            new RecordingRootMaterializer(state),
            handoff);
        string destination = $"C:\\{new string('x', 40000)}";

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InvalidDestination, result.Outcome);
        Assert.Equal(0, state.LoadCount);
        Assert.Equal(0, roots.ObserveCount);
        Assert.Equal(0, payload.InspectCount);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>Root admission failures remain typed and stop before source access.</summary>
    [Theory]
    [InlineData(ManagedInstallationRootStatus.InvalidDestination, ManagedFirstInstallationOutcome.InvalidDestination)]
    [InlineData(ManagedInstallationRootStatus.PermissionDenied, ManagedFirstInstallationOutcome.PermissionDenied)]
    [InlineData(ManagedInstallationRootStatus.Unavailable, ManagedFirstInstallationOutcome.StateUnavailable)]
    [InlineData(ManagedInstallationRootStatus.Present, ManagedFirstInstallationOutcome.RecoveryRequired)]
    [InlineData(ManagedInstallationRootStatus.Residue, ManagedFirstInstallationOutcome.RecoveryRequired)]
    public async Task PrepareMapsRootFailureWithoutSourceAccess(
        ManagedInstallationRootStatus status,
        ManagedFirstInstallationOutcome expected)
    {
        using SequencedStateStore state = new(MissingState());
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource candidate = new(Candidate());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(status),
            payload,
            candidate,
            new RecordingRootMaterializer(state),
            handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root($"prepare-root-{status}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, payload.InspectCount);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>Destination admission is typed and always precedes payload or source observation.</summary>
    [Theory]
    [InlineData(ManagedFirstInstallationMaterializationIssue.InvalidDestination, ManagedFirstInstallationOutcome.InvalidDestination)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.PermissionDenied, ManagedFirstInstallationOutcome.PermissionDenied)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.StateUnavailable, ManagedFirstInstallationOutcome.StateUnavailable)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.RecoveryRequired, ManagedFirstInstallationOutcome.RecoveryRequired)]
    public async Task PrepareMapsDestinationAdmissionWithoutSourceAccess(
        ManagedFirstInstallationMaterializationIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using SequencedStateStore state = new(MissingState());
        using RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource candidate = new(Candidate());
        var materializer = new RecordingRootMaterializer(state) { AdmissionIssue = issue };
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent),
            payload,
            candidate,
            materializer,
            handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root($"prepare-admission-{issue}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, materializer.AdmissionCount);
        Assert.Equal(0, payload.InspectCount);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>Payload failures remain typed and stop before Registry/Catalog candidate access.</summary>
    [Theory]
    [InlineData(ManagedDistributionPayloadIssue.Unavailable, ManagedFirstInstallationOutcome.PayloadUnavailable)]
    [InlineData(ManagedDistributionPayloadIssue.Invalid, ManagedFirstInstallationOutcome.PayloadInvalid)]
    [InlineData(ManagedDistributionPayloadIssue.Changed, ManagedFirstInstallationOutcome.SourceChanged)]
    public async Task PrepareMapsPayloadFailureWithoutCandidateAccess(
        ManagedDistributionPayloadIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using SequencedStateStore state = new(MissingState());
        using RecordingPayloadSource payload = new(PayloadIdentity()) { InspectIssue = issue };
        RecordingCandidateSource candidate = new(Candidate());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent),
            payload,
            candidate,
            new RecordingRootMaterializer(state),
            handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root($"prepare-payload-{issue}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, payload.InspectCount);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>Candidate admission failures retain the existing typed source policy.</summary>
    [Theory]
    [InlineData(FreshInstallationCandidateIssue.RegistryNotConfigured, ManagedFirstInstallationOutcome.SourceUnavailable)]
    [InlineData(FreshInstallationCandidateIssue.SourceUnavailable, ManagedFirstInstallationOutcome.SourceUnavailable)]
    [InlineData(FreshInstallationCandidateIssue.SourceRejected, ManagedFirstInstallationOutcome.SourceRejected)]
    [InlineData(FreshInstallationCandidateIssue.CandidateUnavailable, ManagedFirstInstallationOutcome.CandidateUnavailable)]
    [InlineData(FreshInstallationCandidateIssue.SourceChanged, ManagedFirstInstallationOutcome.SourceChanged)]
    public async Task PrepareMapsCandidateFailure(
        FreshInstallationCandidateIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using SequencedStateStore state = new(MissingState());
        RecordingCandidateSource candidate = new(Candidate()) { InspectIssue = issue };
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(
            state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent),
            payload,
            candidate,
            new RecordingRootMaterializer(state),
            handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root($"prepare-candidate-{issue}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, candidate.InspectCount);
    }

    /// <summary>Materialization failures preserve their stable public outcome and never start Bootstrap.</summary>
    [Theory]
    [InlineData(ManagedFirstInstallationMaterializationIssue.InvalidDestination, ManagedFirstInstallationOutcome.InvalidDestination)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.PermissionDenied, ManagedFirstInstallationOutcome.PermissionDenied)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.SourceUnavailable, ManagedFirstInstallationOutcome.SourceUnavailable)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.SourceChanged, ManagedFirstInstallationOutcome.SourceChanged)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.PromotionFailed, ManagedFirstInstallationOutcome.StateUnavailable)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.RecoveryRequired, ManagedFirstInstallationOutcome.RecoveryRequired)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.StateUnavailable, ManagedFirstInstallationOutcome.StateUnavailable)]
    public async Task InstallMapsMaterializationFailureBeforeBootstrap(
        ManagedFirstInstallationMaterializationIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync(issue);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, harness.Materializer.MaterializeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>Writer contention and writer failure remain distinct typed outcomes.</summary>
    [Theory]
    [InlineData(VersionManagerWriteLeaseIssue.Busy, ManagedFirstInstallationOutcome.Busy)]
    [InlineData(VersionManagerWriteLeaseIssue.Unavailable, ManagedFirstInstallationOutcome.StateUnavailable)]
    public async Task InstallMapsWriterLeaseFailure(
        VersionManagerWriteLeaseIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.State.LeaseIssue = issue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.False(result.IsRecoveryOwned);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>The lease-held admission recheck stops before payload capture and source reverify.</summary>
    [Theory]
    [InlineData(ManagedFirstInstallationMaterializationIssue.InvalidDestination, ManagedFirstInstallationOutcome.InvalidDestination)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.PermissionDenied, ManagedFirstInstallationOutcome.PermissionDenied)]
    [InlineData(ManagedFirstInstallationMaterializationIssue.StateUnavailable, ManagedFirstInstallationOutcome.StateUnavailable)]
    public async Task InstallRechecksDestinationBeforeCapturingSources(
        ManagedFirstInstallationMaterializationIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.AdmissionIssue = issue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(2, harness.Materializer.AdmissionCount);
        Assert.Equal(0, harness.Payload.CaptureCount);
        Assert.Equal(0, harness.CandidateSource.ReverifyCount);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>Admission failures preserve their stable outcome after promoted-root evidence exists.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Busy, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.RecoveryRequired, ManagedFirstInstallationOutcome.RecoveryRequired)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.LaunchFailed, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    public async Task InstallMapsBootstrapAdmissionFailure(
        ImmutableBootstrapAdmissionOutcome admission,
        ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync(admissionOutcome: admission);
        int? exitCode;
        ImmutableBootstrapExitIssue exitIssue;
        switch (admission)
        {
            case ImmutableBootstrapAdmissionOutcome.Busy:
                exitCode = ImmutableBootstrapExitCodeCodec.EncodeFailure(
                    ImmutableBootstrapExitIssue.Busy);
                exitIssue = ImmutableBootstrapExitIssue.Busy;
                break;
            case ImmutableBootstrapAdmissionOutcome.RecoveryRequired:
                exitCode = ImmutableBootstrapExitCodeCodec.EncodeFailure(
                    ImmutableBootstrapExitIssue.InvalidState);
                exitIssue = ImmutableBootstrapExitIssue.InvalidState;
                break;
            case ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed:
                exitCode = null;
                exitIssue = ImmutableBootstrapExitIssue.TerminationUnconfirmed;
                break;
            case ImmutableBootstrapAdmissionOutcome.Admitted:
            case ImmutableBootstrapAdmissionOutcome.HealthUnavailable:
                exitCode = null;
                exitIssue = ImmutableBootstrapExitIssue.None;
                break;
            case ImmutableBootstrapAdmissionOutcome.LaunchFailed:
                exitCode = null;
                exitIssue = ImmutableBootstrapExitIssue.StartFailed;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(admission), admission, null);
        }
        harness.Handoff.AdmissionExitCode = exitCode;
        harness.Handoff.AdmissionExitIssue = exitIssue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        if (admission == ImmutableBootstrapAdmissionOutcome.Busy)
        {
            _ = await harness.Experience.InstallAndLaunchAsync(
                harness.Plan,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.Handoff.StartCount);
        }
    }

    /// <summary>Caller cancellation during either Bootstrap wait drains the launch adapter first.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstallBootstrapCancellationWithoutTypedCleanupIsLaunchFailure(bool duringAdmission)
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var cancellingHandoff = new CancellingBootstrapHandoff(
            harness.State,
            cancellation,
            duringAdmission);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            cancellingHandoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                duringAdmission
                    ? ManagedFirstInstallationLaunchStage.LauncherAdmission
                    : ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.Cancelled),
            result.LaunchFailure);
        Assert.True(cancellingHandoff.Launch.CleanupReturned);
        Assert.True(cancellingHandoff.Launch.Disposed);
        Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>Caller cancellation thrown by process start retains the Bootstrap-start stage.</summary>
    [Fact]
    public async Task InstallBootstrapStartCancellationRetainsExactStage()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var handoff = new CancellingThrowingStartHandoff(harness.State, cancellation);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.Cancelled),
            result.LaunchFailure);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    /// <summary>Caller cancellation cannot hide that root promotion already completed.</summary>
    [Fact]
    public async Task InstallReportsPromotedRootWhenAdapterReturnsTypedFailureAfterCancellingCaller()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var handoff = new TypedFailureCancellingHandoff(harness.State, cancellation);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(handoff.Launch.Disposed);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    /// <summary>A start adapter that cancels after creating a receipt cannot leak that receipt.</summary>
    [Fact]
    public async Task InstallOwnsStartReceiptBeforeCallerCancellationWins()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var handoff = new CancellingStartBootstrapHandoff(harness.State, cancellation);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(handoff.Launch.Disposed);
        Assert.Equal(1, handoff.Launch.AdmissionWaitCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    /// <summary>Cancellation cannot hide a contradictory start receipt and issue.</summary>
    [Fact]
    public async Task InstallCancellationCannotHideMalformedStartReceipt()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var handoff = new MalformedStartBootstrapHandoff(
            harness.State,
            attachLaunch: true,
            ImmutableBootstrapStartIssue.StartFailed,
            cancellation);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(handoff.Launch.Disposed);
        Assert.Equal(0, handoff.Launch.AdmissionWaitCount);
    }

    /// <summary>Cancellation cannot hide failure to prove the Bootstrap job empty.</summary>
    [Fact]
    public async Task InstallCancellationCannotHideTerminationUnconfirmed()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.AdmissionOutcome = ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed;
        harness.Handoff.AdmissionExitIssue = ImmutableBootstrapExitIssue.TerminationUnconfirmed;
        harness.Handoff.AdmissionAction = cancellation.Cancel;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>READY commits before a concurrent caller cancellation and still removes the marker.</summary>
    [Fact]
    public async Task InstallReadyWinsConcurrentCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.CompletionAction = cancellation.Cancel;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
        Assert.Equal(1, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(2, harness.State.LeaseCount);
    }

    /// <summary>Final writer contention after READY is recovery, never a retryable second launch.</summary>
    [Fact]
    public async Task InstallFinalizeBusyIsRecoveryAndCannotStartAgain()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.State.SecondLeaseIssue = VersionManagerWriteLeaseIssue.Busy;

        ManagedFirstInstallationResult first = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);
        int startsAfterFailure = harness.Handoff.StartCount;
        _ = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, first.Outcome);
        Assert.True(first.IsRecoveryOwned);
        Assert.Equal(1, startsAfterFailure);
        Assert.Equal(startsAfterFailure, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>Every durable transaction failure after promotion belongs only to Recovery.</summary>
    [Theory]
    [InlineData(ManagedFirstInstallationTransactionIssue.RecoveryRequired)]
    [InlineData(ManagedFirstInstallationTransactionIssue.StateUnavailable)]
    public async Task PostPromotionRecordFailureRetainsExactRecoveryReason(
        ManagedFirstInstallationTransactionIssue issue)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.Transaction.RecordLaunchIssue = issue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.PostPromotion,
                issue == ManagedFirstInstallationTransactionIssue.RecoveryRequired
                    ? ManagedFirstInstallationLaunchIssue.RecoveryRequired
                    : ManagedFirstInstallationLaunchIssue.StateUnavailable),
            result.LaunchFailure);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>Bootstrap lease failures retain their exact typed projection and recovery marker.</summary>
    [Theory]
    [InlineData(
        ManagedExecutableLaunchIssue.Unavailable,
        ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
        ManagedFirstInstallationLaunchIssue.Unavailable)]
    [InlineData(
        ManagedExecutableLaunchIssue.Tampered,
        ManagedFirstInstallationOutcome.RecoveryRequired,
        ManagedFirstInstallationLaunchIssue.Damaged)]
    [InlineData(
        ManagedExecutableLaunchIssue.UnsafePath,
        ManagedFirstInstallationOutcome.RecoveryRequired,
        ManagedFirstInstallationLaunchIssue.Damaged)]
    public async Task InstallMapsBootstrapLeaseAcquisitionFailure(
        ManagedExecutableLaunchIssue leaseIssue,
        ManagedFirstInstallationOutcome expectedOutcome,
        ManagedFirstInstallationLaunchIssue expectedLaunchIssue)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.Transaction.BootstrapLeaseIssue = leaseIssue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                expectedLaunchIssue),
            result.LaunchFailure);
        Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
        Assert.Equal(1, harness.Materializer.Transaction.BootstrapLeaseAcquireCount);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    /// <summary>A contradictory Bootstrap lease receipt fails closed and disposes any issued lease.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallRejectsMalformedBootstrapLeaseReceipt(bool attachLease)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        RecordingExecutableLaunchLease? issuedLease = null;
        try
        {
            issuedLease = attachLease
                ? new RecordingExecutableLaunchLease(
                    Path.Combine(harness.Plan.ManagedRoot, Bootstrap.FileName),
                    harness.Plan.ManagedRoot)
                : null;
            harness.Materializer.Transaction.BootstrapLeaseResultOverride = new(
                issuedLease,
                attachLease
                    ? ManagedExecutableLaunchIssue.Unavailable
                    : ManagedExecutableLaunchIssue.None);

            ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
                harness.Plan,
                TestContext.Current.CancellationToken);

            Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
            Assert.True(result.IsRecoveryOwned);
            Assert.Equal(
                new ManagedFirstInstallationLaunchFailure(
                    ManagedFirstInstallationLaunchStage.BootstrapStart,
                    ManagedFirstInstallationLaunchIssue.Damaged),
                result.LaunchFailure);
            Assert.Equal(attachLease, issuedLease?.Disposed ?? false);
            Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
            Assert.Equal(0, harness.Handoff.StartCount);
            Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
            Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        }
        finally
        {
            issuedLease?.Dispose();
        }
    }

    /// <summary>Caller cancellation during Bootstrap lease acquisition preserves the post-promotion boundary.</summary>
    [Fact]
    public async Task InstallBootstrapLeaseAcquisitionCancellationRetainsRecoveryMarker()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.Transaction.BootstrapLeaseAcquireAction = cancellation.Cancel;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.PostPromotion,
                ManagedFirstInstallationLaunchIssue.Cancelled),
            result.LaunchFailure);
        Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
        Assert.Equal(1, harness.Materializer.Transaction.BootstrapLeaseAcquireCount);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    /// <summary>A lease returned concurrently with caller cancellation remains owned and disposed.</summary>
    [Fact]
    public async Task InstallDisposesBootstrapLeaseWhenCallerCancelsAfterAcquisition()
    {
        using var cancellation = new CancellationTokenSource();
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.Transaction.BootstrapLeaseAcquiredAction = cancellation.Cancel;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.PostPromotion,
                ManagedFirstInstallationLaunchIssue.Cancelled),
            result.LaunchFailure);
        Assert.Equal(1, harness.Materializer.Transaction.BootstrapLeaseAcquireCount);
        Assert.NotNull(harness.Materializer.Transaction.LastBootstrapLease);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease.Disposed);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    /// <summary>A post-READY marker-finalization failure remains recovery-owned.</summary>
    [Fact]
    public async Task PostReadyCompletionFailureIsRecoveryOwned()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.Transaction.CompleteIssue =
            ManagedFirstInstallationTransactionIssue.StateUnavailable;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.IsRecoveryOwned);
        Assert.Null(result.LaunchFailure);
        Assert.Equal(1, harness.Handoff.StartCount);
        Assert.Equal(1, harness.Materializer.Transaction.CompleteCount);
    }

    private sealed class CancellingThrowingStartHandoff(
        SequencedStateStore state,
        CancellationTokenSource cancellation) : IImmutableBootstrapLeaseHandoff
    {
        public ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            IManagedExecutableLaunchLease ownedLease,
            CancellationToken cancellationToken)
        {
            state.RecordContractViolation(state.LeaseActive);
            ownedLease.Dispose();
            Cancel(cancellation);
            return ValueTask.FromException<ImmutableBootstrapStartResult>(
                new OperationCanceledException(cancellation.Token));
        }
    }
}
