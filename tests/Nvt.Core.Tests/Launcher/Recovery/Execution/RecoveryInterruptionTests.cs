// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Stops real executors at observable physical boundaries and reloads the surviving prefix.</summary>
public sealed class RecoveryInterruptionTests
{
    /// <summary>Each observable delete boundary preserves exactly its physical prefix after a parent hard stop.</summary>
    [Theory]
    [MemberData(nameof(PhysicalBoundaries))]
    public async Task RealExecutorHardStopReloadsPhysicalPrefix(string shape, int deletion, string side)
    {
        Assert.SkipWhen(side == "before", $"No exact pre-delete hook exists for {shape} deletion {deletion}. AfterDelete(n-1) precedes intervening validation/custody work and cannot prove BeforeDelete(n); product seams are out of scope.");
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = shape == "staging" ? await RecoveryEvidenceFixture.CreateFullStagingAsync()
            : await RecoveryEvidenceFixture.CreateAsync(withLauncherState: shape == "root-with-launcher-state");
        string parent = Path.GetDirectoryName(fixture.ManagedRoot)!;
        var marker = RecoveryHardStopChild.Marker(fixture.Transaction);
        RecoveryHardStop.WriteJson(fixture.MarkerPath, marker);
        string scenario = Path.Combine(parent, "hard-stop-scenario.json");
        RecoveryHardStop.WriteJson(scenario, marker);
        using (var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken))
            Assert.True(lease.IsAcquired);
        string[] order = DeletionOrder(fixture, shape);
        Assert.Equal(shape == "root" ? 25 : 26, order.Length);
        var expected = ExpectedPrefix(parent, order, deletion);
        string signal = Path.Combine(parent, "hard-stop-ready");
        await using var child = RecoveryHardStop.Start("recovery-executor", scenario, deletion.ToString(System.Globalization.CultureInfo.InvariantCulture), signal);
        await RecoveryHardStop.StopAsync(child, signal, deletion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(expected, RecoveryHardStop.Snapshot(parent));
        await AssertRestartAsync(fixture, parent, scenario, shape, deletion, order.Length);
    }

    private static SortedDictionary<string, string> ExpectedPrefix(string parent, string[] order, int deletion)
    {
        var expected = RecoveryHardStop.Snapshot(parent);
        foreach (string path in order.Take(deletion)) Assert.True(expected.Remove(path), path);
        return expected;
    }

    private static async Task AssertRestartAsync(RecoveryEvidenceFixture original, string parent, string scenario,
        string shape, int deletion, int total)
    {
        var marker = await RecoveryHardStop.ReadJsonAsync<ManagedSetupMarker>(scenario);
        using var restarted = RecoveryEvidenceFixture.Reopen(parent, RecoveryHardStopChild.Transaction(marker),
            shape == "root-with-launcher-state");
        var state = await restarted.ApplicationStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(shape == "root-with-launcher-state" && deletion == 1
            ? Nvt.Core.Launcher.Activation.VersionManagerStateLoadIssue.None
            : Nvt.Core.Launcher.Activation.VersionManagerStateLoadIssue.Missing, state.Issue);
        if (deletion == total)
        {
            Assert.False(File.Exists(original.MarkerPath));
            Assert.False(Directory.Exists(original.ManagedRoot));
            Assert.False(Directory.Exists(FileSystemManagedInstallationRootProbe.GetStagingContainerPath(original.ManagedRoot)));
            var diagnosis = new ManagedInstallationRecoveryExperience(restarted.ApplicationStore,
                new FileSystemManagedInstallationRootProbe(),
                new FileSystemManagedSetupRecoveryProbe(new SyntheticMarkerCodec(), SetupFixture.Ceiling, SetupFixture.Ceiling),
                new RecordingRecoveryLifetimeProbe(), restarted.CreateExecutor());
            Assert.Equal(ManagedSetupRecoveryOutcome.NoRecoveryNeeded,
                (await diagnosis.DiagnoseAsync(original.ManagedRoot, TestContext.Current.CancellationToken)).Outcome);
            return;
        }
        var fact = await new FileSystemManagedSetupRecoveryProbe(new SyntheticMarkerCodec(), SetupFixture.Ceiling,
            SetupFixture.Ceiling).ObserveAsync(restarted.ManagedRoot, restarted.StatePath, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryFactKind.Exact, fact.Kind);
        int remaining = 0;
        var executor = restarted.CreateExecutor(count => remaining = count);
        var evidence = await executor.ObserveAsync(fact.Transaction!, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        using var lease = await restarted.ApplicationStore.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.True(lease.IsAcquired);
        var result = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, result);
        Assert.Equal(total - deletion, remaining);
        Assert.False(File.Exists(restarted.MarkerPath));
        Assert.False(File.Exists(restarted.StatePath));
        Assert.False(File.Exists(restarted.LauncherStatePath));
        Assert.False(Directory.Exists(restarted.ManagedRoot));
        Assert.False(Directory.Exists(FileSystemManagedInstallationRootProbe.GetStagingContainerPath(restarted.ManagedRoot)));
    }

    private static string[] DeletionOrder(RecoveryEvidenceFixture fixture, string shape)
    {
        string parent = Path.GetDirectoryName(fixture.ManagedRoot)!;
        string tree = shape == "staging" ? "managed.managed-setup-staging/" + fixture.Transaction.TransactionId : "managed";
        string version = tree + "/versions/1.0.6";
        string[] treeSteps =
        [
            version + "/tools/worker/1.0.0/worker.exe", version + "/docs/contracts/policy.json",
            version + "/profiles/built-in/index.json", version + "/.managed-admission.v1.json",
            version + "/FixtureProduct.exe", version + "/LICENSE.txt", version + "/README.txt",
            version + "/SHA256SUMS.txt", version + "/THIRD-PARTY-NOTICES.txt",
            tree + "/Distribution.exe", tree + "/FixtureProduct.Bootstrap.exe", tree + "/seed.json",
            version + "/tools/worker/1.0.0", version + "/docs/contracts", version + "/profiles/built-in",
            version + "/tools/worker", version + "/docs", version + "/profiles", version + "/tools",
            version + "/RELEASE-MANIFEST.json", version, tree + "/versions", tree,
        ];
        string[] states = shape == "root-with-launcher-state"
            ? [Path.GetRelativePath(parent, fixture.LauncherStatePath).Replace('\\', '/'), "state/state.json"] : ["state/state.json"];
        string[] container = shape == "staging" ? ["managed.managed-setup-staging"] : [];
        return [.. states, .. treeSteps, .. container, Path.GetFileName(fixture.MarkerPath)];
    }

    /// <summary>Enumerates both sides of all 25/26 root and 26 staging deletions, including marker removal.</summary>
    public static IEnumerable<object[]> PhysicalBoundaries()
    {
        (string Shape, int Count)[] shapes = [("root", 25), ("root-with-launcher-state", 26), ("staging", 26)];
        foreach (var shape in shapes)
            foreach (int deletion in Enumerable.Range(1, shape.Count))
                foreach (string side in new[] { "before", "after" })
                    yield return [shape.Shape, deletion, side];
    }
}
