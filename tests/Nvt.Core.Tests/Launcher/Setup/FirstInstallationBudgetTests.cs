// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Time;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Uses manual physical time to characterize first-install budget boundaries.</summary>
public sealed class FirstInstallationBudgetTests : FirstInstallationTestSupport
{
    /// <summary>Each supported timing override requires a positive duration with its public parameter name.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task ConstructorRejectsNonpositiveAdmissionAndCompletion(long ticks)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        TimeSpan invalid = TimeSpan.FromTicks(ticks);
        Assert.Equal("admissionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(harness.State, harness.Roots, harness.Payload, harness.CandidateSource,
                harness.Materializer, harness.Handoff, admissionOperationCutoff: invalid)).ParamName);
        Assert.Equal("completionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(harness.State, harness.Roots, harness.Payload, harness.CandidateSource,
                harness.Materializer, harness.Handoff, completionOperationCutoff: invalid)).ParamName);
    }

    /// <summary>The smallest positive explicit duration is accepted before any timer is started.</summary>
    [Fact]
    public async Task ConstructorAcceptsOneTickOverrides()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        Assert.NotNull(Create(harness.State, harness.Roots, harness.Payload, harness.CandidateSource,
            harness.Materializer, harness.Handoff, TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)));
    }

    /// <summary>The inclusive BCL timer ceiling is admitted for both operation parameters.</summary>
    [Fact]
    public async Task ConstructorAcceptsMaximumTimerDuration()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        TimeSpan maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        Assert.NotNull(Create(harness.State, harness.Roots, harness.Payload, harness.CandidateSource,
            harness.Materializer, harness.Handoff, admissionOperationCutoff: maximum));
        Assert.NotNull(Create(harness.State, harness.Roots, harness.Payload, harness.CandidateSource,
            harness.Materializer, harness.Handoff, completionOperationCutoff: maximum));
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff, maximum, maximum);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
    }

    /// <summary>Oversized timers are rejected before any irreversible install work.</summary>
    [Fact]
    public async Task ConstructorRejectsOneMillisecondAboveMaximumTimerDuration()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        TimeSpan invalid = TimeSpan.FromMilliseconds(uint.MaxValue);
        Assert.Equal("admissionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(harness.State, harness.Roots, harness.Payload, harness.CandidateSource,
                harness.Materializer, harness.Handoff, admissionOperationCutoff: invalid)).ParamName);
        Assert.Equal("completionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(harness.State, harness.Roots, harness.Payload, harness.CandidateSource,
                harness.Materializer, harness.Handoff, completionOperationCutoff: invalid)).ParamName);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
    }

    /// <summary>The inclusive timer ceiling still expires at the exact admission cutoff.</summary>
    [Fact]
    public async Task MaximumAdmissionCutoffTimesOutAtExactBoundary()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        TimeSpan maximum = TimeSpan.FromMilliseconds(4_294_967_294);
        harness.Materializer.Transaction.BootstrapLeaseAcquiredAction = () => time.Advance(maximum);
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff,
            admissionOperationCutoff: maximum, timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(new ManagedFirstInstallationLaunchFailure(
            ManagedFirstInstallationLaunchStage.BootstrapStart, ManagedFirstInstallationLaunchIssue.TimedOut),
            result.LaunchFailure);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>READY at the inclusive timer ceiling is too late for first-install finalization.</summary>
    [Fact]
    public async Task MaximumCompletionCutoffTimesOutAtExactBoundary()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        TimeSpan maximum = TimeSpan.FromMilliseconds(4_294_967_294);
        harness.Handoff.CompletionAction = () => time.Advance(maximum);
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff,
            completionOperationCutoff: maximum, timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(new ManagedFirstInstallationLaunchFailure(
            ManagedFirstInstallationLaunchStage.ApplicationReady, ManagedFirstInstallationLaunchIssue.TimedOut),
            result.LaunchFailure);
        Assert.True(harness.Handoff.LastLaunch!.Disposed);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>Launch custody acquisition consumes the exact 30-second admission budget.</summary>
    [Theory]
    [InlineData(29_999, ManagedFirstInstallationOutcome.Completed)]
    [InlineData(30_000, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    [InlineData(30_001, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    public async Task AdmissionUsesExactDefaultBoundary(int milliseconds, ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        harness.Materializer.Transaction.BootstrapLeaseAcquiredAction = () =>
            time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff, timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Equal(expected == ManagedFirstInstallationOutcome.Completed ? 1 : 0,
            harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>The independent READY operation preserves its exact 44,500-millisecond cutoff.</summary>
    [Theory]
    [InlineData(44_499, ManagedFirstInstallationOutcome.Completed)]
    [InlineData(44_500, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    [InlineData(44_501, ManagedFirstInstallationOutcome.InstalledButLaunchFailed)]
    public async Task CompletionUsesExactDefaultBoundary(int milliseconds, ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        harness.Handoff.CompletionAction = () => time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff, timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == ManagedFirstInstallationOutcome.Completed ? 1 : 0,
            harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>One elapsed observation keeps the mapped failure and its exit code consistent across the cutoff.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapCompletionOutcome.RolledBack, 1, ImmutableBootstrapExitIssue.None,
        ManagedFirstInstallationLaunchIssue.RolledBack)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Failed, 15, ImmutableBootstrapExitIssue.StartFailed,
        ManagedFirstInstallationLaunchIssue.StartFailed)]
    public async Task CompletionSamplesElapsedOnceWhenClockCrossesCutoff(
        ImmutableBootstrapCompletionOutcome outcome, int exitCode, ImmutableBootstrapExitIssue exitIssue,
        ManagedFirstInstallationLaunchIssue expectedIssue)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var manual = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        // Guard: the awaited scenario invokes the completion port and timestamp delegate serially.
        int? completionReads = null;
        var time = new DelegateTimeProvider(manual.GetUtcNow, () =>
        {
            long timestamp = manual.GetTimestamp();
            if (completionReads is { } count)
            {
                completionReads = count + 1;
                manual.Advance(TimeSpan.FromTicks(1));
            }
            return timestamp;
        }, manual.TimestampFrequency);
        harness.Handoff.CompletionAction = () =>
        {
            manual.Advance(ManagedLauncherEntryCoordinator.DefaultCompletionOperationCutoff - TimeSpan.FromTicks(2));
            completionReads = 0;
        };
        harness.Handoff.CompletionOutcome = outcome;
        harness.Handoff.CompletionExitCode = exitCode;
        harness.Handoff.CompletionExitIssue = exitIssue;
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff, timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(1, completionReads);
        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.Equal(expectedIssue, result.LaunchFailure!.Issue);
        Assert.Equal(exitCode, result.LaunchFailure.ExitCode);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.True(harness.Handoff.LastLaunch!.Disposed);
    }

    /// <summary>Finalization uses independent writer authority with an exact five-second timer.</summary>
    [Theory]
    [InlineData(4_999, ManagedFirstInstallationOutcome.Completed)]
    [InlineData(5_000, ManagedFirstInstallationOutcome.RecoveryRequired)]
    [InlineData(5_001, ManagedFirstInstallationOutcome.RecoveryRequired)]
    public async Task FinalizationUsesExactWriterLeaseBoundary(int milliseconds, ManagedFirstInstallationOutcome expected)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        harness.State.SecondLeaseAction = () => time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff, timeProvider: time);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], harness.State.LeaseTimeouts);
        Assert.Equal(expected == ManagedFirstInstallationOutcome.Completed ? 1 : 0,
            harness.Materializer.Transaction.CompleteCount);
    }
}
