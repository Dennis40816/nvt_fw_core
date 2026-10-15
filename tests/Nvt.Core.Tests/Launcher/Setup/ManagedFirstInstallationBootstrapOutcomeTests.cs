// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Tests first-install Bootstrap outcomes and receipt boundaries.</summary>
public sealed partial class ManagedFirstInstallationBootstrapOutcomeTests : FirstInstallationTestSupport
{
    /// <summary>
    /// First install retains its own bounded package-admission budget.
    /// </summary>
    [Fact]
    public async Task InstallUsesIndependentDefaultAdmissionBudget()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        ImmutableBootstrapWaitBudget? observed = null;
        harness.Handoff.AdmissionOutcome = ImmutableBootstrapAdmissionOutcome.HealthUnavailable;
        harness.Handoff.AdmissionBudgetObserved = budget => observed = budget;
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            harness.Handoff,
            timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(
            ManagedFirstInstallationExperience.DefaultAdmissionOperationCutoff,
            observed?.RemainingOperation);
        Assert.Equal(
            ManagedFirstInstallationExperience.DefaultAdmissionOperationCutoff +
            ManagedLauncherEntryCoordinator.DefaultCleanupObservationBudget,
            observed?.RemainingTotal);
        Assert.True(
            ManagedFirstInstallationExperience.DefaultAdmissionOperationCutoff >
            ManagedLauncherEntryCoordinator.DefaultAdmissionOperationCutoff);
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            ManagedFirstInstallationExperience.DefaultAdmissionOperationCutoff);
    }

    /// <summary>Bootstrap custody acquisition is included in the single Setup admission budget.</summary>
    [Fact]
    public async Task InstallBootstrapLeaseAcquisitionUsesAdmissionBudget()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        harness.Materializer.Transaction.BootstrapLeaseAcquireAction = () =>
            time.Advance(TimeSpan.FromSeconds(31));
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            harness.Handoff,
            timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.TimedOut),
            result.LaunchFailure);
        Assert.Equal(1, harness.Materializer.Transaction.BootstrapLeaseAcquireCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>Bootstrap start time is deducted from the single admission budget.</summary>
    [Fact]
    public async Task InstallAdmissionBudgetIncludesStartElapsed()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        ImmutableBootstrapWaitBudget? observed = null;
        harness.Handoff.StartAction = () => time.Advance(TimeSpan.FromMilliseconds(600));
        harness.Handoff.AdmissionOutcome = ImmutableBootstrapAdmissionOutcome.HealthUnavailable;
        harness.Handoff.AdmissionBudgetObserved = budget => observed = budget;
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            harness.Handoff,
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromMilliseconds(44500),
            time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(900), observed?.RemainingOperation);
        Assert.Equal(TimeSpan.FromMilliseconds(1400), observed?.RemainingTotal);
    }

    /// <summary>A start that returns only after the admission cutoff can never complete Setup.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallRejectsReceiptReturnedAfterAdmissionCutoff(
        bool admissionIgnoresCancellation)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        harness.Handoff.StartAction = () => time.Advance(TimeSpan.FromMilliseconds(100));
        harness.Handoff.IgnoreAdmissionCancellation = admissionIgnoresCancellation;
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            harness.Handoff,
            TimeSpan.FromMilliseconds(20),
            timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(harness.Handoff.LastLaunch?.Disposed);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>Only first-install READY, never rollback, permits marker removal.</summary>
    [Fact]
    public async Task InstallRollbackDoesNotCompleteFirstInstallation()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.CompletionOutcome = ImmutableBootstrapCompletionOutcome.RolledBack;
        harness.Handoff.CompletionExitCode = 1;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>A completion returned after its hard cutoff cannot remove the Setup marker.</summary>
    [Fact]
    public async Task InstallRejectsReadyReturnedAfterCompletionCutoff()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        harness.Handoff.CompletionAction = () => time.Advance(TimeSpan.FromSeconds(1));
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            harness.Handoff,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(500),
            time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>A Bootstrap start that honors the internal deadline fails closed after promotion.</summary>
    [Fact]
    public async Task InstallBootstrapStartHasInternalDeadline()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var handoff = new BlockingStartBootstrapHandoff(harness.State);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff,
            TimeSpan.FromMilliseconds(25), timeProvider: time);

        Task<ManagedFirstInstallationResult> running = experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken).AsTask();
        await handoff.Entered.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(25));
        ManagedFirstInstallationResult result = await running.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.TimedOut),
            result.LaunchFailure);
        Assert.Equal(1, handoff.StartCount);
        Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
    }

    /// <summary>Exact Bootstrap admission exit semantics reach the presentation result intact.</summary>
    [Fact]
    public async Task InstallPreservesBootstrapAdmissionExitReason()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.AdmissionOutcome = ImmutableBootstrapAdmissionOutcome.HealthUnavailable;
        harness.Handoff.AdmissionExitCode = 22;
        harness.Handoff.AdmissionExitIssue = ImmutableBootstrapExitIssue.InvalidInheritedContext;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.InvalidInheritedContext,
                22),
            result.LaunchFailure);
    }

    /// <summary>Exact Bootstrap completion exit semantics reach the presentation result intact.</summary>
    [Fact]
    public async Task InstallPreservesBootstrapCompletionExitReason()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.CompletionOutcome = ImmutableBootstrapCompletionOutcome.Failed;
        harness.Handoff.CompletionExitCode = 15;
        harness.Handoff.CompletionExitIssue = ImmutableBootstrapExitIssue.StartFailed;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.StartFailed,
                15),
            result.LaunchFailure);
    }

    /// <summary>Contradictory or partial admission receipts fail closed at the Application boundary.</summary>
    [Theory]
    [InlineData(
        ImmutableBootstrapAdmissionOutcome.Admitted,
        22,
        ImmutableBootstrapExitIssue.InvalidInheritedContext)]
    [InlineData(
        ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
        22,
        ImmutableBootstrapExitIssue.None)]
    [InlineData(
        ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
        22,
        ImmutableBootstrapExitIssue.Unknown)]
    [InlineData(
        ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
        ImmutableBootstrapExitCodeCodec.Ready,
        ImmutableBootstrapExitIssue.Unknown)]
    [InlineData(
        ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
        ImmutableBootstrapExitCodeCodec.RolledBack,
        ImmutableBootstrapExitIssue.Unknown)]
    [InlineData(
        ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
        null,
        ImmutableBootstrapExitIssue.InvalidInheritedContext)]
    [InlineData((ImmutableBootstrapAdmissionOutcome)999, null, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Admitted, null, (ImmutableBootstrapExitIssue)999)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Busy, null, ImmutableBootstrapExitIssue.Busy)]
    public async Task InstallRejectsMalformedAdmissionReceipt(
        ImmutableBootstrapAdmissionOutcome outcome,
        int? exitCode,
        ImmutableBootstrapExitIssue exitIssue)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.AdmissionOutcome = outcome;
        harness.Handoff.AdmissionExitCode = exitCode;
        harness.Handoff.AdmissionExitIssue = exitIssue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.InvalidReceipt,
                exitCode),
            result.LaunchFailure);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>Contradictory or partial completion receipts fail closed at the Application boundary.</summary>
    [Theory]
    [InlineData(
        ImmutableBootstrapCompletionOutcome.Ready,
        0,
        ImmutableBootstrapExitIssue.StartFailed)]
    [InlineData(
        ImmutableBootstrapCompletionOutcome.Failed,
        15,
        ImmutableBootstrapExitIssue.None)]
    [InlineData(
        ImmutableBootstrapCompletionOutcome.Failed,
        15,
        ImmutableBootstrapExitIssue.Busy)]
    [InlineData(
        ImmutableBootstrapCompletionOutcome.Failed,
        null,
        ImmutableBootstrapExitIssue.StartFailed)]
    [InlineData(
        ImmutableBootstrapCompletionOutcome.Ready,
        null,
        ImmutableBootstrapExitIssue.None)]
    [InlineData(
        ImmutableBootstrapCompletionOutcome.Ready,
        1,
        ImmutableBootstrapExitIssue.None)]
    [InlineData((ImmutableBootstrapCompletionOutcome)999, null, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Ready, 0, (ImmutableBootstrapExitIssue)999)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Unavailable, 18, ImmutableBootstrapExitIssue.StateUnavailable)]
    [InlineData(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed, null, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapCompletionOutcome.RolledBack, 0, ImmutableBootstrapExitIssue.None)]
    public async Task InstallRejectsMalformedCompletionReceipt(
        ImmutableBootstrapCompletionOutcome outcome,
        int? exitCode,
        ImmutableBootstrapExitIssue exitIssue)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.CompletionOutcome = outcome;
        harness.Handoff.CompletionExitCode = exitCode;
        harness.Handoff.CompletionExitIssue = exitIssue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.InvalidReceipt,
                exitCode),
            result.LaunchFailure);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>The public final result cannot represent an unspecified launch-terminal failure.</summary>
    [Fact]
    public void LaunchTerminalResultRequiresOneAuthoritativeFailure()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.None,
                ManagedFirstInstallationLaunchIssue.Cancelled));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.None));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                (ManagedFirstInstallationLaunchStage)999,
                ManagedFirstInstallationLaunchIssue.Cancelled));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                (ManagedFirstInstallationLaunchIssue)999));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ManagedFirstInstallationResult(
                (ManagedFirstInstallationOutcome)999,
                Root("undefined-outcome"),
                ManagedAppVersion.Parse("1.0.7")));
        _ = Assert.Throws<ArgumentException>(() =>
            _ = new ManagedFirstInstallationResult(
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
                Root("invalid-result"),
                ManagedAppVersion.Parse("1.0.7")));
        _ = Assert.Throws<ArgumentException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.Busy,
                ImmutableBootstrapExitCodeCodec.EncodeFailure(
                    ImmutableBootstrapExitIssue.StartFailed)));
        _ = Assert.Throws<ArgumentException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.TimedOut,
                ImmutableBootstrapExitCodeCodec.Ready));
        _ = Assert.Throws<ArgumentException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.InvalidInheritedContext));
        _ = Assert.Throws<ArgumentException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.Busy));
        _ = Assert.Throws<ArgumentException>(() =>
            _ = new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.ApplicationReady,
                ManagedFirstInstallationLaunchIssue.RolledBack));

    }

    /// <summary>Record copies must be rechecked before their failure facts can be adopted.</summary>
    [Fact]
    public void LaunchTerminalRecordCopiesRemainRechecked()
    {
        var valid = new ManagedFirstInstallationLaunchFailure(
            ManagedFirstInstallationLaunchStage.ApplicationReady,
            ManagedFirstInstallationLaunchIssue.StartFailed,
            ImmutableBootstrapExitCodeCodec.EncodeFailure(
                ImmutableBootstrapExitIssue.StartFailed));
        Assert.True(valid.HasValidShape);
        Assert.False((valid with { ExitCode = null }).HasValidShape);
        Assert.False((valid with { Issue = ManagedFirstInstallationLaunchIssue.Busy }).HasValidShape);
        Assert.False((valid with { Stage = ManagedFirstInstallationLaunchStage.PostPromotion }).HasValidShape);
        var validResult = new ManagedFirstInstallationResult(
            ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
            Root("valid-result"),
            ManagedAppVersion.Parse("1.0.7"),
            valid);
        Assert.True(validResult.HasValidShape);
        Assert.True(validResult.IsRecoveryOwned);
        Assert.False(new ManagedFirstInstallationResult(
            ManagedFirstInstallationOutcome.StateUnavailable,
            Root("retryable-state"),
            ManagedAppVersion.Parse("1.0.7")).IsRecoveryOwned);
        Assert.False((validResult with
        {
            Outcome = ManagedFirstInstallationOutcome.RecoveryRequired,
        }).HasValidShape);
        _ = new ManagedFirstInstallationLaunchFailure(
            ManagedFirstInstallationLaunchStage.ApplicationReady,
            ManagedFirstInstallationLaunchIssue.RolledBack,
            ImmutableBootstrapExitCodeCodec.RolledBack);
    }

    /// <summary>A no-exit cleanup failure is a valid typed termination receipt, not malformed data.</summary>
    [Fact]
    public async Task InstallAcceptsNoExitTerminationUnconfirmedReceipt()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.AdmissionOutcome = ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed;
        harness.Handoff.AdmissionExitIssue = ImmutableBootstrapExitIssue.TerminationUnconfirmed;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.LauncherAdmission,
                ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed),
            result.LaunchFailure);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>Every typed Bootstrap start issue preserves its Setup outcome.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapStartIssue.Busy, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    [InlineData(ImmutableBootstrapStartIssue.Damaged, ManagedFirstInstallationOutcome.RecoveryRequired)]
    [InlineData(ImmutableBootstrapStartIssue.StartFailed, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    [InlineData(ImmutableBootstrapStartIssue.Unavailable, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    public async Task InstallMapsBootstrapStartIssue(
        ImmutableBootstrapStartIssue issue,
        ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var handoff = new SetupBootstrapHandoff(harness.State) { StartIssue = issue };
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        if (issue == ImmutableBootstrapStartIssue.Busy)
        {
            _ = await experience.InstallAndLaunchAsync(
                harness.Plan,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, handoff.StartCount);
        }
    }

    /// <summary>An undefined start issue becomes one typed invalid receipt without throwing.</summary>
    [Fact]
    public async Task InstallFailsClosedForUndefinedBootstrapStartIssue()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var handoff = new SetupBootstrapHandoff(harness.State)
        {
            StartIssue = (ImmutableBootstrapStartIssue)999,
        };
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(
            new ManagedFirstInstallationLaunchFailure(
                ManagedFirstInstallationLaunchStage.BootstrapStart,
                ManagedFirstInstallationLaunchIssue.InvalidReceipt),
            result.LaunchFailure);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>A receipt attached to a failed start is disposed and fails closed.</summary>
    [Fact]
    public async Task InstallFailsClosedForReceiptAttachedToStartIssue()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var handoff = new MalformedStartBootstrapHandoff(
            harness.State,
            attachLaunch: true,
            ImmutableBootstrapStartIssue.Busy);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(handoff.Launch.Disposed);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>A purported successful start without a receipt fails closed.</summary>
    [Fact]
    public async Task InstallFailsClosedForSuccessfulStartWithoutReceipt()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var handoff = new MalformedStartBootstrapHandoff(
            harness.State,
            attachLaunch: false,
            ImmutableBootstrapStartIssue.None);
        ManagedFirstInstallationExperience experience = Create(
            harness.State,
            harness.Roots,
            harness.Payload,
            harness.CandidateSource,
            harness.Materializer,
            handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.False(handoff.Launch.Disposed);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>A failed payload result still transfers any attached custody for immediate disposal.</summary>
    [Fact]
    public async Task InstallDisposesPayloadCaptureAttachedToFailure()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Payload.CaptureIssue = ManagedDistributionPayloadIssue.Unavailable;
        harness.Payload.ReturnCaptureOnFailure = true;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.PayloadUnavailable, result.Outcome);
        Assert.True(Assert.IsType<PayloadCapture>(harness.Payload.LastCapture).Disposed);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
    }

    /// <summary>A success issue without attached custody is malformed and fails closed.</summary>
    [Fact]
    public async Task InstallRejectsMissingPayloadCaptureAttachedToSuccess()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Payload.ReturnNullCaptureOnSuccess = true;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.PayloadInvalid, result.Outcome);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
    }

    /// <summary>A success issue cannot launder custody for another payload identity.</summary>
    [Fact]
    public async Task InstallDisposesAndRejectsMismatchedPayloadCapture()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Payload.ReturnMismatchedCapture = true;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.PayloadInvalid, result.Outcome);
        Assert.True(Assert.IsType<PayloadCapture>(harness.Payload.LastCapture).Disposed);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
    }

    /// <summary>A failed materialization result cannot leak an attached promoted-root receipt.</summary>
    [Fact]
    public async Task InstallDisposesPromotedInstallationAttachedToFailure()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync(
            ManagedFirstInstallationMaterializationIssue.StateUnavailable);
        harness.Materializer.ReturnInstallationOnFailure = true;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>Missing durable state after Bootstrap READY is known recovery residue.</summary>
    [Fact]
    public async Task InstallMapsMissingPostReadyStateToRecoveryRequired()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync(finalState: MissingState());

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>The public typed outcome set includes the accepted planning and progress states.</summary>
    [Fact]
    public void OutcomeContractIncludesInstallingAndCancelled()
    {
        Assert.Contains(ManagedFirstInstallationOutcome.Installing, Enum.GetValues<ManagedFirstInstallationOutcome>());
        Assert.Contains(ManagedFirstInstallationOutcome.Cancelled, Enum.GetValues<ManagedFirstInstallationOutcome>());
    }

    private sealed class BlockingStartBootstrapHandoff(SequencedStateStore state)
        : IImmutableBootstrapLeaseHandoff
    {
        // Guard: the scenario driver and awaited signals serialize all accesses.
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Entered => _entered.Task;
        internal int StartCount { get; private set; }

        public async ValueTask<ImmutableBootstrapStartResult> StartAsync(
            string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity,
            IManagedExecutableLaunchLease ownedLease,
            CancellationToken cancellationToken)
        {
            Assert.False(state.LeaseActive);
            StartCount++;
            _ = _entered.TrySetResult();
            try
            {
                await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(cancellationToken);
                throw new InvalidOperationException("The internal deadline must cancel Bootstrap start.");
            }
            finally
            {
                ownedLease.Dispose();
            }
        }
    }

}
