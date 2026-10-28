// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Checks interruption evidence at every setup durability boundary.</summary>
public sealed class ManagedSetupInterruptionTests
{
    /// <summary>Injected interruptions preserve each exact durable prefix and release custody.</summary>
    [Theory]
    [InlineData("BeforeStagingMarkerWrite", false, null)]
    [InlineData("AfterStagingMarkerWrite", false, "staging")]
    [InlineData("BeforeSeedWrite", false, "staging")]
    [InlineData("AfterSeedWrite", false, "staging")]
    [InlineData("BeforePromotion", false, "staging")]
    [InlineData("AfterPromotion", true, "staging")]
    [InlineData("BeforeRootPromotedMarkerWrite", true, "staging")]
    [InlineData("AfterRootPromotedMarkerWrite", true, "root-promoted")]
    [InlineData("BeforeBootstrapLaunchMarkerWrite", true, "root-promoted")]
    [InlineData("AfterBootstrapLaunchMarkerWrite", true, "bootstrap-launch-recorded")]
    public async Task FailureAtEveryDurableBoundaryRetainsPrefix(string boundaryName, bool rootPromoted, string? phase)
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var boundary = Enum.Parse<ManagedSetupBoundary>(boundaryName);
        var hooks = new ManagedSetupTestHooks
        {
            BoundaryReached = observed =>
            {
                if (observed == boundary) throw new IOException("Synthetic interruption at durable boundary.");
            },
        };
        var result = await fixture.MaterializeAsync(hooks);
        if (boundary is ManagedSetupBoundary.BeforeBootstrapLaunchMarkerWrite or ManagedSetupBoundary.AfterBootstrapLaunchMarkerWrite)
        {
            Assert.True(result.IsSuccess, result.Issue.ToString());
            using var installation = result.Installation!;
            Assert.Equal(ManagedFirstInstallationTransactionIssue.RecoveryRequired,
                await installation.RecordBootstrapLaunchAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Null(result.Installation);
            Assert.Equal(rootPromoted ? ManagedFirstInstallationMaterializationIssue.RecoveryRequired
                : ManagedFirstInstallationMaterializationIssue.PromotionFailed, result.Issue);
        }
        Assert.Equal(rootPromoted, Directory.Exists(fixture.Root));
        Assert.True(File.Exists(fixture.MarkerPath));
        var fact = await fixture.Probe().ObserveAsync(fixture.Root, fixture.StatePath, TestContext.Current.CancellationToken);
        if (phase is null) Assert.Equal(ManagedSetupRecoveryFactKind.Malformed, fact.Kind);
        else
        {
            Assert.Equal(ManagedSetupRecoveryFactKind.Exact, fact.Kind);
            Assert.Equal(phase, fixture.Codec.Decode(await SetupFixture.ReadAsync(fixture.MarkerPath))!.Phase);
        }
        new FileInfo(fixture.MarkerPath).MoveTo(fixture.MarkerPath + ".released");
    }

    /// <summary>A real child setup hard stop requires a setup-capable shared probe mode.</summary>
    [Theory]
    [InlineData("BeforeStagingMarkerWrite")]
    [InlineData("AfterStagingMarkerWrite")]
    [InlineData("BeforePromotion")]
    [InlineData("AfterPromotion")]
    [InlineData("BeforeRootPromotedMarkerWrite")]
    [InlineData("AfterRootPromotedMarkerWrite")]
    [InlineData("BeforeBootstrapLaunchMarkerWrite")]
    [InlineData("AfterBootstrapLaunchMarkerWrite")]
    [InlineData("BeforeFirstDurableApplicationStateWrite")]
    [InlineData("AfterFirstDurableApplicationStateWrite")]
    public void RealHardStopRequiresSharedSetupChild(string boundary)
    {
        Assert.Skip($"Shared TestProbe and LinkedProbe have no setup materializer mode for {boundary}. ChildProcessFixture is available; shared child wiring is owned by the host.");
    }
}
