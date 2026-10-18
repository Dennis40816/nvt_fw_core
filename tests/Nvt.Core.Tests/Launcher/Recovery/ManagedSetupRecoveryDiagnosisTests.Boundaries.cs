// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

public sealed partial class ManagedSetupRecoveryDiagnosisTests
{
    /// <summary>The first durable application write changes evidence without giving diagnosis mutation authority.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstDurableApplicationStateWriteKeepsOnlyAuthorizedAction(bool written)
    {
        var harness = RecoveryHarness.Create(written ? RecoveryTestData.CanonicalAppState : RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Exact, RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded)),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing));
        var result = await harness.Experience.DiagnoseAsync(RecoveryTestData.ManagedRoot, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryOutcome.ActionAvailable, result.Outcome);
        Assert.Equal(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, result.Plan!.Action);
        Assert.Equal(0, harness.State.LeaseCount);
        Assert.Equal(3, harness.Lifetime.Calls.Count);
        Assert.Equal(1, harness.State.LoadCount);
        Assert.Equal(1, harness.Root.CallCount);
        Assert.Equal(1, harness.Marker.CallCount);
        Assert.Equal(1, harness.Evidence.CallCount);
    }

    /// <summary>Diagnosis uses only its five read-only ports and never requests source authority or process work.</summary>
    [Fact]
    public async Task DiagnosisDoesNoRegistryCatalogOrProcessWork()
    {
        var harness = RecoveryHarness.Create(RecoveryTestData.MissingAppState,
            new(ManagedSetupRecoveryFactKind.Exact, RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.Staging)),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing));
        var result = await harness.Experience.DiagnoseAsync(RecoveryTestData.ManagedRoot, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryOutcome.ActionAvailable, result.Outcome);
        Assert.Equal(0, harness.State.LeaseCount);
        Assert.Equal(1, harness.State.LoadCount);
        Assert.Equal(1, harness.Evidence.CallCount);
        // No source or process-start port is supplied. Every durable write port throws in this harness.
        Assert.All(typeof(ManagedInstallationRecoveryExperience).GetConstructors().Single().GetParameters(), parameter =>
            Assert.Contains(parameter.ParameterType, new[] { typeof(IManagedSetupRecoveryStateReader),
                typeof(IManagedInstallationRootProbe), typeof(IManagedSetupRecoveryProbe),
                typeof(IManagedProcessLifetimeProbe), typeof(IManagedSetupRecoveryEvidenceProbe) }));
    }

    /// <summary>An unavailable first durable application write exposes no plan and requests no writer.</summary>
    [Fact]
    public async Task UnavailableFirstDurableApplicationStateWriteStaysReadOnly()
    {
        var harness = RecoveryHarness.Create(new(null, VersionManagerStateLoadIssue.Unavailable),
            new(ManagedSetupRecoveryFactKind.Exact, RecoveryTestData.Transaction(ManagedSetupRecoveryPhase.BootstrapLaunchRecorded)),
            RecoveryTestData.Evidence(RecoveryLauncherCase.Missing));
        var result = await harness.Experience.DiagnoseAsync(RecoveryTestData.ManagedRoot, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryOutcome.HealthUnavailable, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, harness.State.LeaseCount);
        Assert.Equal(0, harness.Evidence.CallCount);
    }
}
