// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Preserves fresh-root promotion, marker lifecycle and failure custody assertions.</summary>
public sealed class ManagedSetupMaterializerTests
{
    /// <summary>A closed root is promoted and the marker survives until explicit completion.</summary>
    [Fact]
    public async Task MaterializerPromotesClosedRootAndMarkerLifecycle()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var result = await fixture.MaterializeAsync();
        Assert.True(result.IsSuccess, result.Issue.ToString());
        using var installation = result.Installation!;
        Assert.Equal(1, fixture.Repository.InstallCount);
        Assert.Equal("distribution-launcher", await SetupFixture.TextAsync(Path.Combine(fixture.Root, SetupFixture.Layout.DistributionLauncherFileName)));
        Assert.Equal("immutable-bootstrap", await SetupFixture.TextAsync(Path.Combine(fixture.Root, SetupFixture.Layout.BootstrapFileName)));
        Assert.True(File.Exists(Path.Combine(fixture.Root, SetupFixture.Layout.SeedFileName)));
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.Equal(ManagedFirstInstallationTransactionIssue.None,
            await installation.RecordBootstrapLaunchAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ManagedFirstInstallationTransactionIssue.None,
            await installation.CompleteAsync(TestContext.Current.CancellationToken));
        installation.Dispose();
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    /// <summary>Existing root content is never adopted or overwritten.</summary>
    [Fact]
    public async Task MaterializerNeverOverwritesExistingRoot()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Local fixed-drive root admission is required.");
        using var fixture = new SetupFixture();
        Directory.CreateDirectory(fixture.Root);
        string sentinel = Path.Combine(fixture.Root, "keep.txt");
        SetupFixture.Write(sentinel, "owner-data");
        var result = await fixture.MaterializeAsync();
        Assert.Null(result.Installation);
        Assert.Equal(ManagedFirstInstallationMaterializationIssue.RecoveryRequired, result.Issue);
        Assert.Equal(0, fixture.Repository.InstallCount);
        Assert.Equal("owner-data", await SetupFixture.TextAsync(sentinel));
    }

    /// <summary>Cancellation after native marker creation releases all held handles.</summary>
    [Fact]
    public async Task MarkerWriteCancellationReleasesNativeHandle()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        using var cancellation = new CancellationTokenSource();
        var hooks = new ManagedSetupTestHooks
        {
            DestinationCustodyAcquired = (_, _) => new ValueTask(cancellation.CancelAsync()),
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.MaterializeAsync(hooks, token: cancellation.Token));
        string displaced = fixture.MarkerPath + ".displaced";
        new FileInfo(fixture.MarkerPath).MoveTo(displaced);
        Assert.True(File.Exists(displaced));
        Directory.Move(fixture.Parent, fixture.Parent + ".moved");
        Directory.Move(fixture.Parent + ".moved", fixture.Parent);
    }

    /// <summary>A failure before promotion retains the exact marker and owned staging.</summary>
    [Fact]
    public async Task PrePromotionFailureRetainsMarkerAndOwnedStaging()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        using var payload = new TestPayloadCapture(SetupFixture.PayloadIdentity(), "changed-launcher");
        var result = await fixture.MaterializeAsync(payload: payload);
        var observed = await new FileSystemManagedInstallationRootProbe().ObserveAsync(fixture.Root, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedFirstInstallationMaterializationIssue.SourceChanged, result.Issue);
        Assert.Equal(ManagedInstallationRootStatus.Residue, observed.Status);
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.True(Directory.Exists(fixture.StagingPath));
    }

    /// <summary>Post-promotion content drift retains blocking evidence.</summary>
    [Fact]
    public async Task MaterializerRetainsMarkerWhenPostPromotionVerificationDetectsTamper()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var repository = new SyntheticRepository(fixture.Admission)
        {
            InventoryObserved = (count, root) =>
            {
                if (count == 1) SetupFixture.Write(Path.Combine(root, SetupFixture.Layout.DistributionLauncherFileName), "tampered");
            },
        };
        var materializer = new FileSystemManagedFirstInstallationRootMaterializer(repository, repository.InstallHeldAsync,
            new(10_001, 10_000, 1_000_000_000), SetupFixture.Layout, fixture.SeedStore, fixture.Codec,
            SetupFixture.Ceiling, SetupFixture.Ceiling, 200_000_000, new());
        using var payload = new TestPayloadCapture(SetupFixture.PayloadIdentity());
        var result = await materializer.MaterializeAsync(fixture.Root, fixture.StatePath, payload, fixture.Candidate,
            fixture.Seed, TestContext.Current.CancellationToken);
        Assert.Null(result.Installation);
        Assert.Equal(ManagedFirstInstallationMaterializationIssue.RecoveryRequired, result.Issue);
        Assert.True(Directory.Exists(fixture.Root));
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    /// <summary>Every phase authored by setup is decoded by recovery after custody release.</summary>
    [Fact]
    public async Task RecoveryReadsEveryMaterializerProducedMarkerPhase()
    {
        SetupFixture.RequireNative();
        foreach (string phase in new[] { "staging", "root-promoted", "bootstrap-launch-recorded" })
        {
            await VerifyPhaseAsync(phase);
        }
    }

    private static async Task VerifyPhaseAsync(string phase)
    {
        using var fixture = new SetupFixture();
        if (phase == "staging")
        {
            using var payload = new TestPayloadCapture(SetupFixture.PayloadIdentity(), "changed-launcher");
            await fixture.MaterializeAsync(payload: payload);
        }
        else
        {
            var result = await fixture.MaterializeAsync();
            Assert.True(result.IsSuccess, result.Issue.ToString());
            using var installation = result.Installation!;
            if (phase == "bootstrap-launch-recorded") Assert.Equal(ManagedFirstInstallationTransactionIssue.None,
                await installation.RecordBootstrapLaunchAsync(TestContext.Current.CancellationToken));
        }
        var marker = Assert.IsType<ManagedSetupMarker>(fixture.Codec.Decode(await SetupFixture.ReadAsync(fixture.MarkerPath)));
        Assert.Equal(phase, marker.Phase);
        var observed = await fixture.Probe().ObserveAsync(fixture.Root, fixture.StatePath, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryFactKind.Exact, observed.Kind);
    }

    /// <summary>Closed-root topology drift blocks phase advancement without discarding the marker.</summary>
    [Fact]
    public async Task ClosedRootTopologyDriftRetainsMarker()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        bool contentBlocked = false;
        var result = await fixture.MaterializeAsync(new()
        {
            ClosedRootVerified = (root, _) =>
            {
                try { SetupFixture.Write(Path.Combine(root, SetupFixture.Layout.DistributionLauncherFileName), "changed"); }
                catch (IOException) { contentBlocked = true; }
                SetupFixture.Write(Path.Combine(root, "unexpected.txt"), "foreign");
                return ValueTask.CompletedTask;
            },
        });
        Assert.True(contentBlocked);
        Assert.Equal(ManagedFirstInstallationMaterializationIssue.RecoveryRequired, result.Issue);
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "unexpected.txt")));
    }

    /// <summary>Promotion transfers cancellation ownership to recovery and releases the native tree.</summary>
    [Fact]
    public async Task CancellationAfterPromotionIsRecoveryOwned()
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.MaterializeAsync(new() { AfterRootPromotion = _ => cancellation.Cancel() }, token: cancellation.Token);
        Assert.Equal(ManagedFirstInstallationMaterializationIssue.RecoveryRequired, result.Issue);
        Assert.True(Directory.Exists(fixture.Root));
        Assert.True(File.Exists(fixture.MarkerPath));
        new FileInfo(fixture.MarkerPath).MoveTo(fixture.MarkerPath + ".moved");
    }
}
