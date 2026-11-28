// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Launcher.Recovery.Execution;
using Nvt.Core.Tests.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Verification;
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

    /// <summary>A real materializer or seed bootstrapper is killed with live custody at the exact boundary.</summary>
    [Theory]
    [InlineData("BeforeStagingMarkerWrite", false, null, false, false, false)]
    [InlineData("AfterStagingMarkerWrite", false, "staging", false, false, true)]
    [InlineData("BeforePromotion", false, "staging", true, false, true)]
    [InlineData("AfterPromotion", true, "staging", true, false, false)]
    [InlineData("BeforeRootPromotedMarkerWrite", true, "staging", true, false, false)]
    [InlineData("AfterRootPromotedMarkerWrite", true, "root-promoted", true, false, true)]
    [InlineData("BeforeBootstrapLaunchMarkerWrite", true, "root-promoted", true, false, true)]
    [InlineData("AfterBootstrapLaunchMarkerWrite", true, "bootstrap-launch-recorded", true, false, true)]
    [InlineData("BeforeFirstDurableApplicationStateWrite", true, "bootstrap-launch-recorded", true, false, true)]
    [InlineData("AfterFirstDurableApplicationStateWrite", true, "bootstrap-launch-recorded", true, true, true)]
    public async Task RealHardStopReloadsSetupPrefix(string boundary, bool promoted, string? phase,
        bool hasPayload, bool hasState, bool actionable)
    {
        SetupFixture.RequireNative();
        var package = PackageFixture.Create(includeLauncher: true);
        using var fixture = new SetupFixture(installedPackage: package);
        TestFiles.WriteAllBytes(Path.Combine(fixture.Parent, "hard-stop-package.zip"), package.PackageBytes);
        string signal = Path.Combine(fixture.Parent, "hard-stop-ready");
        await using var child = RecoveryHardStop.Start("setup-materializer", fixture.Parent, boundary, signal);
        await RecoveryHardStop.StopAsync(child, signal, boundary);
        using var restarted = new SetupFixture(fixture.Parent, package, package.PackageBytes);
        Assert.Equal(promoted, Directory.Exists(restarted.Root));
        Assert.Equal(hasState, File.Exists(restarted.StatePath));
        var fact = await restarted.Probe().ObserveAsync(restarted.Root, restarted.StatePath, TestContext.Current.CancellationToken);
        Assert.Equal(phase is null ? ManagedSetupRecoveryFactKind.Malformed : ManagedSetupRecoveryFactKind.Exact, fact.Kind);
        await AssertSetupPrefixAsync(restarted, package, phase, promoted, hasPayload);
        await AssertSetupRecoveryAsync(restarted, package, actionable);
    }

    private static async Task AssertSetupPrefixAsync(SetupFixture fixture, PackageFixture package, string? phase,
        bool promoted, bool hasPayload)
    {
        if (phase is null)
        {
            Assert.Empty(await SetupFixture.ReadAsync(fixture.MarkerPath));
            Assert.False(Directory.Exists(fixture.StagingPath));
            return;
        }
        var marker = fixture.Codec.Decode(await SetupFixture.ReadAsync(fixture.MarkerPath))!;
        Assert.Equal(phase, marker.Phase);
        Assert.Equal(fixture.Admission.AdmissionIdentity, marker.Candidate.EntryIdentity);
        Assert.Equal(fixture.Root, marker.ManagedRootIdentity);
        Assert.Equal(fixture.StatePath, marker.StatePathIdentity);
        string staged = Path.Combine(fixture.StagingPath, marker.TransactionId);
        Assert.Equal(hasPayload && !promoted, Directory.Exists(staged));
        Assert.Equal(hasPayload && !promoted, Directory.Exists(fixture.StagingPath));
        if (!hasPayload) { Assert.False(Directory.Exists(fixture.StagingPath)); return; }
        string tree = promoted ? fixture.Root : staged;
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string version = "versions/" + fixture.Admission.Version;
        foreach (var pair in package.Files) expected.Add(version + "/" + pair.Key, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pair.Value)));
        AddExpected(expected, version + "/RELEASE-MANIFEST.json", package.ManifestBytes);
        AddExpected(expected, version + "/SHA256SUMS.txt", package.ChecksumBytes);
        AddExpected(expected, version + "/.managed-admission.v1.json", new AdmissionCodec().Encode(fixture.Admission).ToArray());
        AddExpected(expected, SetupFixture.Layout.DistributionLauncherFileName, "distribution-launcher"u8.ToArray());
        AddExpected(expected, SetupFixture.Layout.BootstrapFileName, "immutable-bootstrap"u8.ToArray());
        AddExpected(expected, SetupFixture.Layout.SeedFileName, System.Text.Encoding.UTF8.GetBytes($"synthetic-seed:{fixture.Admission.Version}:{fixture.Admission.AdmissionIdentity}"));
        var actual = RecoveryHardStop.Snapshot(tree);
        Assert.Equal(expected, new SortedDictionary<string, string>(actual.Where(pair => pair.Value != "directory").ToDictionary(), StringComparer.Ordinal));
        Assert.Equal(ExpectedDirectories(expected.Keys), actual.Where(pair => pair.Value == "directory").Select(pair => pair.Key));
    }

    private static IEnumerable<string> ExpectedDirectories(IEnumerable<string> files) => files
        .SelectMany(path => Enumerable.Range(1, path.Split('/').Length - 1)
            .Select(count => string.Join('/', path.Split('/').Take(count))))
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static void AddExpected(SortedDictionary<string, string> expected, string path, byte[] bytes) =>
        expected.Add(path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));

    private static async Task AssertSetupRecoveryAsync(SetupFixture fixture, PackageFixture package, bool actionable)
    {
        var state = new HardStopApplicationStore(fixture);
        var executor = new FileSystemManagedSetupRecoveryExecution(state, RecoveryEvidenceFixture.LauncherSuffix,
            new RecoveryPhysicalLauncherStore(RecoveryEvidenceFixture.LauncherPathFor(fixture.StatePath), null),
            SetupFixture.Layout, fixture.SeedStore, package.Policy, PackageFixture.FrozenLimits, new AdmissionCodec());
        var diagnosis = new ManagedInstallationRecoveryExperience(state, new FileSystemManagedInstallationRootProbe(),
            fixture.Probe(), new RecordingRecoveryLifetimeProbe(), executor);
        var before = await fixture.SnapshotAsync();
        var result = await diagnosis.DiagnoseAsync(fixture.Root, TestContext.Current.CancellationToken);
        Assert.Equal(actionable ? ManagedSetupRecoveryOutcome.ActionAvailable : ManagedSetupRecoveryOutcome.ManualInterventionRequired, result.Outcome);
        Assert.Equal(before, await fixture.SnapshotAsync());
        if (!actionable) { Assert.Null(result.Plan); return; }
        Assert.Equal(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, result.Plan!.Action);
        var evidence = await executor.ObserveAsync(result.Transaction!, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        using var lease = await state.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.True(lease.IsAcquired);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, await executor.ExecuteAsync(
            new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(fixture.MarkerPath));
        Assert.False(File.Exists(fixture.StatePath));
        Assert.False(Directory.Exists(fixture.Root));
        Assert.False(Directory.Exists(fixture.StagingPath));
    }
}
