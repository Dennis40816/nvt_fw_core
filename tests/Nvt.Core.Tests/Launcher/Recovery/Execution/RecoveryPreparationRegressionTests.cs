// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Checks final preparation topology and typed failures when observing restart prefixes.</summary>
public sealed class RecoveryPreparationRegressionTests
{
    /// <summary>A foreign root file introduced during hashing leaves both durable states untouched.</summary>
    [Fact]
    public async Task ForeignRootFileDuringPreparationReturnsSourceChangedWithoutDeletingStates()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync(withLauncherState: true);
        var evidence = await fixture.Executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        string before = RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot);
        byte[] appState = await RecoveryEvidenceFixture.ReadAsync(fixture.StatePath, TestContext.Current.CancellationToken);
        byte[] launcherState = await RecoveryEvidenceFixture.ReadAsync(fixture.LauncherStatePath, TestContext.Current.CancellationToken);
        string foreign = Path.Combine(fixture.ManagedRoot, "foreign.txt");
        int deletes = 0;
        var executor = fixture.CreateExecutor(count => deletes = count,
            afterHashedFile: _ => RecoveryEvidenceFixture.Write(foreign, "foreign"));
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.Equal(0, deletes);
        Assert.Equal(appState, await RecoveryEvidenceFixture.ReadAsync(fixture.StatePath, TestContext.Current.CancellationToken));
        Assert.Equal(launcherState, await RecoveryEvidenceFixture.ReadAsync(fixture.LauncherStatePath, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(fixture.VersionRoot, "RELEASE-MANIFEST.json")));
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.Equal("foreign", await RecoveryEvidenceFixture.TextAsync(foreign, TestContext.Current.CancellationToken));
        File.Delete(foreign);
        Assert.Equal(before, RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot));
        string retained = string.Concat(fixture.ManagedRoot, ".released");
        new DirectoryInfo(fixture.ManagedRoot).MoveTo(retained);
        Assert.True(Directory.Exists(retained));
    }

    /// <summary>Both prefix entry paths map manifest policy IO failures to typed outcomes and release custody.</summary>
    [Theory]
    [InlineData(false, false, 13)]
    [InlineData(false, true, 13)]
    [InlineData(true, false, 13)]
    [InlineData(true, true, 13)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 2)]
    public async Task ManifestPolicyFailureAtDeletionRestartReturnsStateUnavailable(
        bool staging, bool accessDenied, int boundary)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await CreatePrefixFixtureAsync(staging);
        await RecoveryExecutionBoundaryTests.InterruptAfterAsync(fixture, boundary);
        fixture.Policy.Exception = accessDenied
            ? new UnauthorizedAccessException("Synthetic manifest policy access failure.")
            : new IOException("Synthetic manifest policy read failure.");
        string treeRoot = GetTreeRoot(fixture, staging);
        string before = RecoveryEvidenceFixture.DirectoryProof(treeRoot);
        int calls = fixture.Policy.Calls;
        int deletes = 0;
        var executor = fixture.CreateExecutor(count => deletes = count, useProductionRepository: true);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.StateUnavailable, evidence.Issue);
        Assert.Null(evidence.ExecutionToken);
        Assert.Equal(calls + (boundary == 13 ? 1 : 2), fixture.Policy.Calls);
        Assert.Equal(0, deletes);
        Assert.Equal(before, RecoveryEvidenceFixture.DirectoryProof(treeRoot));
        Assert.True(File.Exists(fixture.MarkerPath));
        string retained = string.Concat(treeRoot, ".released");
        new DirectoryInfo(treeRoot).MoveTo(retained);
        Assert.True(Directory.Exists(retained));
    }

    private static async Task<RecoveryEvidenceFixture> CreatePrefixFixtureAsync(bool staging)
    {
        if (staging)
        {
            return await RecoveryEvidenceFixture.CreateFullStagingAsync();
        }
        return await RecoveryEvidenceFixture.CreateAsync();
    }

    private static string GetTreeRoot(RecoveryEvidenceFixture fixture, bool staging) => staging
        ? Path.Combine(FileSystemManagedInstallationRootProbe.GetStagingContainerPath(fixture.ManagedRoot),
            fixture.Transaction.TransactionId) : fixture.ManagedRoot;
}
