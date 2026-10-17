// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Checks the Transport admission invariants used by first-install failure projection.</summary>
public sealed class FirstInstallationAdmissionInvariantTests : FirstInstallationTestSupport
{
    /// <summary>Issue-free failure fallbacks cannot describe a valid Transport receipt.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Busy)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.RecoveryRequired)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.LaunchFailed)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed)]
    public void AdmissionFailureWithoutTypedIssueIsUnreachable(ImmutableBootstrapAdmissionOutcome outcome)
    {
        var receipt = new ImmutableBootstrapAdmissionResult(outcome);

        Assert.False(receipt.HasValidShape);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FirstInstallationOutcomes.MapBootstrapAdmissionFailure(receipt));
        Assert.Equal("A valid failed Bootstrap admission requires one typed exit issue.", exception.Message);
    }

    /// <summary>Malformed issue-free receipts stop before the mapper and preserve the promoted marker.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Busy)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.RecoveryRequired)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.LaunchFailed)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed)]
    public async Task InstallRejectsIssueFreeAdmissionFailure(ImmutableBootstrapAdmissionOutcome outcome)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.AdmissionOutcome = outcome;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.True(result.HasValidShape);
        Assert.Equal(ManagedFirstInstallationLaunchIssue.InvalidReceipt, result.LaunchFailure!.Issue);
        Assert.Null(result.LaunchFailure.ExitCode);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.True(harness.Handoff.LastLaunch!.Disposed);
    }

    /// <summary>Every numeric failure family produces a compatible post-promotion result.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task InstallAcceptsEveryTransportAdmissionFailure(int exitCode)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        ImmutableBootstrapExitIssue issue = ImmutableBootstrapExitCodeCodec.DecodeFailure(exitCode);
        var receipt = new ImmutableBootstrapAdmissionResult(
            ImmutableBootstrapExitCodeCodec.ClassifyAdmission(issue), exitCode, issue);
        harness.Handoff.AdmissionOutcome = receipt.Outcome;
        harness.Handoff.AdmissionExitCode = receipt.ExitCode;
        harness.Handoff.AdmissionExitIssue = receipt.ExitIssue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.True(receipt.HasValidShape);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(ManagedFirstInstallationLaunchStage.LauncherAdmission, result.LaunchFailure!.Stage);
        Assert.Equal(exitCode, result.LaunchFailure.ExitCode);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.True(harness.Handoff.LastLaunch!.Disposed);
    }

    /// <summary>Only these three admission failures can omit the process exit code.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapAdmissionOutcome.LaunchFailed, ImmutableBootstrapExitIssue.StartFailed,
        ManagedFirstInstallationLaunchIssue.StartFailed)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, ImmutableBootstrapExitIssue.None,
        ManagedFirstInstallationLaunchIssue.HealthUnavailable)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed, ImmutableBootstrapExitIssue.TerminationUnconfirmed,
        ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed)]
    public async Task InstallAcceptsTransportAdmissionFailureWithoutExitCode(
        ImmutableBootstrapAdmissionOutcome outcome, ImmutableBootstrapExitIssue exitIssue,
        ManagedFirstInstallationLaunchIssue expectedIssue)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var receipt = new ImmutableBootstrapAdmissionResult(outcome, ExitIssue: exitIssue);
        harness.Handoff.AdmissionOutcome = receipt.Outcome;
        harness.Handoff.AdmissionExitIssue = receipt.ExitIssue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.True(receipt.HasValidShape);
        Assert.True(result.HasValidShape);
        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.Equal(expectedIssue, result.LaunchFailure!.Issue);
        Assert.Null(result.LaunchFailure.ExitCode);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }
}
