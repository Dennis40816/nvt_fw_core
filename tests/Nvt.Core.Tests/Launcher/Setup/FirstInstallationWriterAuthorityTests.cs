// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Uses production writer custody to reject a live receipt for another exact state path.</summary>
public sealed class FirstInstallationWriterAuthorityTests : FirstInstallationTestSupport
{
    [Fact]
    public async Task ForeignInitialWriterFailsBeforeRecheckOrMutation()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.State.FirstLeasePathOverride = Root("foreign-initial-state.json");

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.StateUnavailable, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.Equal(1, harness.State.LeaseCount);
        Assert.Equal(1, harness.State.LoadCount);
        Assert.Equal(0, harness.Payload.CaptureCount);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.False(harness.State.LeaseActive);
    }

    [Fact]
    public async Task ForeignFinalizationWriterRetainsMarkerBeforeStateLoad()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.State.SecondLeasePathOverride = Root("foreign-final-state.json");

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Null(result.LaunchFailure);
        Assert.Equal(2, harness.State.LeaseCount);
        Assert.Equal(2, harness.State.LoadCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.Equal(1, harness.Handoff.StartCount);
        Assert.False(harness.State.LeaseActive);
    }
}
