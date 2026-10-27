// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Xunit;
using static Nvt.Core.Tests.Launcher.Recovery.Execution.RecoveryEvidenceFixture;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Locks exact writer custody and fail-closed recovery execution.</summary>
public sealed partial class RecoveryPhysicalExecutionTests
{
    /// <summary>An exact marker before staging creation is a deterministic Missing/Missing prefix.</summary>
    [Fact]
    public async Task MissingStatesAndAbsentStagingProduceExactEvidenceWithoutLauncher()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync();

        ManagedSetupRecoveryEvidenceObservation first = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        ManagedSetupRecoveryEvidenceObservation second = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, first.Issue);
        Assert.Null(first.InstalledLauncher);
        Assert.Equal(first.ExecutionToken, second.ExecutionToken);
    }

    /// <summary>Marker executable sizes must satisfy the shared 200 MB admission bound.</summary>
    [Theory]
    [InlineData(200_000_001L, 10L)]
    [InlineData(10L, 200_000_001L)]
    [InlineData(long.MaxValue, 10L)]
    [InlineData(10L, long.MaxValue)]
    public async Task OversizedRecoveryPayloadFailsTypedBeforeLimitArithmetic(
        long launcherSize,
        long bootstrapSize)
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync(
            launcherSize: launcherSize,
            bootstrapSize: bootstrapSize);

        ManagedSetupRecoveryEvidenceObservation observation = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.Invalid, observation.Issue);
        Assert.Null(observation.ExecutionToken);
    }

    /// <summary>Staging Missing/Missing is actionable only while the final root is exactly absent.</summary>
    [Fact]
    public async Task EarlyStagingRejectsARecreatedFinalManagedRoot()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync();
        _ = Directory.CreateDirectory(fixture.ManagedRoot);

        ManagedSetupRecoveryEvidenceObservation observation = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.Invalid, observation.Issue);
    }

    /// <summary>A final root created after staging observation invalidates execution.</summary>
    [Fact]
    public async Task EarlyStagingExecutionRejectsFinalRootCreatedAfterObservation()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        _ = Directory.CreateDirectory(fixture.ManagedRoot);
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(fixture.Transaction.StatePathIdentity)
                .TryAcquireWriteLeaseAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

        ManagedSetupRecoveryExecutionPortOutcome outcome = await fixture.Executor.ExecuteAsync(
            new ManagedSetupRecoveryExecutionRequest(
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.True(File.Exists(FileSystemManagedInstallationRootProbe
            .GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>The post-manifest fixed skeleton is exact, while any extra file is foreign residue.</summary>
    [Fact]
    public async Task MissingStatesAcceptOnlyExactTerminalSkeleton()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateTerminalSkeletonAsync();

        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTree(
            fixture.ManagedRoot,
            limits: WindowsStableTreeLimits.ForRecoverySkeleton(3),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(acquired.IsAcquired);
        using (WindowsStablePathCustody custody = acquired.Custody!)
        {
            Assert.True(custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot));
            Assert.Equal(
                [string.Empty, "versions", "versions/1.0.6"],
                snapshot!.Directories.Keys.Order(StringComparer.Ordinal));
        }

        ManagedSetupRecoveryEvidenceObservation exact = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, exact.Issue);
        Assert.Null(exact.InstalledLauncher);

        await WriteAsync(
            Path.Combine(fixture.ManagedRoot, "foreign.txt"),
            "foreign",
            TestContext.Current.CancellationToken);
        ManagedSetupRecoveryEvidenceObservation foreign = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.Invalid, foreign.Issue);
    }

    /// <summary>Earliest staging rollback deletes only the exact marker under the live writer lease.</summary>
    [Fact]
    public async Task EarliestStagingRollbackDeletesMarkerLast()
    {
        RequireNative();
        int deletes = 0;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync(
            count => deletes = count);
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        ManagedSetupRecoveryEvidenceObservation repeated = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        Assert.Equal(evidence.ExecutionToken, repeated.ExecutionToken);
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(fixture.Transaction.StatePathIdentity)
                .TryAcquireWriteLeaseAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

        ManagedSetupRecoveryExecutionPortOutcome outcome = await fixture.Executor.ExecuteAsync(
            new ManagedSetupRecoveryExecutionRequest(
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.True(
            outcome == ManagedSetupRecoveryExecutionPortOutcome.Completed,
            $"Outcome {outcome}; deletes {deletes}");
        Assert.False(File.Exists(
            FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>Terminal skeleton rollback removes version/root directories before its marker.</summary>
    [Fact]
    public async Task TerminalSkeletonRollbackDeletesDirectoriesThenMarker()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateTerminalSkeletonAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(fixture.Transaction.StatePathIdentity)
                .TryAcquireWriteLeaseAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

        ManagedSetupRecoveryExecutionPortOutcome outcome = await fixture.Executor.ExecuteAsync(
            new ManagedSetupRecoveryExecutionRequest(
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.False(Directory.Exists(fixture.ManagedRoot));
        Assert.False(File.Exists(
            FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>The authority manifest is the final tree file and the marker remains until completion.</summary>
    [Fact]
    public async Task FullRollbackDeletesAuthorityManifestAfterEveryOtherTreeFile()
    {
        RequireNative();
        string? root = null;
        bool observedManifestRemoval = false;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(_ =>
        {
            string managedRoot = root!;
            if (!File.Exists(
                    FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(managedRoot)))
            {
                return;
            }
            string manifest = Path.Combine(
                managedRoot,
                "versions",
                "1.0.6",
                "RELEASE-MANIFEST.json");
            Assert.True(File.Exists(
                FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(managedRoot)));
            if (!File.Exists(manifest))
            {
                observedManifestRemoval = true;
                if (Directory.Exists(managedRoot))
                {
                    Assert.Empty(Directory.EnumerateFiles(
                        managedRoot,
                        "*",
                        SearchOption.AllDirectories));
                }
            }
        });
        root = fixture.ManagedRoot;
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(fixture.Transaction.StatePathIdentity)
                .TryAcquireWriteLeaseAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

        ManagedSetupRecoveryExecutionPortOutcome outcome = await fixture.Executor.ExecuteAsync(
            new ManagedSetupRecoveryExecutionRequest(
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(fixture.ManagedRoot), $"Outcome: {outcome}");
        WindowsStableCustodyResult missingRoot =
            WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                fixture.ManagedRoot,
                WindowsStableTreeLimits.ForRecoverySkeleton(0),
                TestContext.Current.CancellationToken);
        Assert.True(missingRoot.IsExactChildMissing, $"Issue: {missingRoot.Issue}");
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.True(observedManifestRemoval);
        Assert.False(Directory.Exists(fixture.ManagedRoot));
        Assert.False(File.Exists(
            FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>Rollback removes Launcher state before Application state and tree bytes.</summary>
    [Fact]
    public async Task RollbackDeletesLauncherStateBeforeApplicationState()
    {
        RequireNative();
        string? appState = null;
        string? launcherState = null;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(
            count =>
            {
                Assert.False(File.Exists(launcherState));
                Assert.Equal(count == 1, File.Exists(appState));
            },
            withLauncherState: true);
        appState = fixture.Transaction.StatePathIdentity;
        launcherState = RecoveryEvidenceFixture.LauncherPathFor(appState);
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(appState).TryAcquireWriteLeaseAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

        ManagedSetupRecoveryExecutionPortOutcome outcome = await fixture.Executor.ExecuteAsync(
            new ManagedSetupRecoveryExecutionRequest(
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
    }

    /// <summary>Every fixed delete boundary is restartable or already complete, with the original 25/26 totals.</summary>
    [Theory]
    [MemberData(nameof(DeleteBoundaries))]
    public async Task EveryDeleteBoundaryReobservesAndCompletesFromExactPrefix(
        bool withLauncherState, int totalDeletes, int boundary)
    {
        RequireNative();
        using var cancellation = new CancellationTokenSource();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(
            count => { if (count == boundary) cancellation.Cancel(); }, withLauncherState);
        var evidence = await fixture.Executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        if (boundary == 0) await cancellation.CancelAsync();
        ManagedSetupRecoveryExecutionPortOutcome interrupted;
        using (var lease = await new RecoveryLeaseSource(fixture.StatePath).TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken))
        {
            interrupted = await fixture.Executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!), lease, cancellation.Token);
        }
        if (boundary == totalDeletes)
        {
            Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, interrupted);
            Assert.False(File.Exists(fixture.MarkerPath));
            return;
        }
        Assert.Equal(boundary == 0 ? ManagedSetupRecoveryExecutionPortOutcome.Cancelled
            : ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired, interrupted);
        int resumedDeletes = 0;
        var resumedExecutor = fixture.CreateExecutor(count => resumedDeletes = count);
        var resumed = await resumedExecutor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.True(resumed.Issue == ManagedSetupRecoveryEvidenceIssue.None, $"Boundary {boundary}/{totalDeletes}: {resumed.Issue}");
        var repeated = await resumedExecutor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(resumed.ExecutionToken, repeated.ExecutionToken);
        using var resumeLease = await new RecoveryLeaseSource(fixture.StatePath).TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var underLease = await resumedExecutor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(resumed.ExecutionToken, underLease.ExecutionToken);
        var outcome = await resumedExecutor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            resumed.ExecutionToken!), resumeLease, TestContext.Current.CancellationToken);
        Assert.True(outcome == ManagedSetupRecoveryExecutionPortOutcome.Completed,
            $"Boundary {boundary}/{totalDeletes}, resumed deletes {resumedDeletes}: {outcome}");
        Assert.Equal(totalDeletes - boundary, resumedDeletes);
    }

    /// <summary>The independent fixture has 23 tree entries plus state(s) and marker; every boundary is enumerated.</summary>
    public static IEnumerable<object[]> DeleteBoundaries()
    {
        foreach (int boundary in Enumerable.Range(0, 26)) yield return [false, 25, boundary];
        foreach (int boundary in Enumerable.Range(0, 27)) yield return [true, 26, boundary];
    }
    /// <summary>An uninterrupted rollback hashes each held tree file exactly once.</summary>
    [Fact]
    public async Task RollbackTreeProofIsLinearInFileCountAndBytes()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string[] files = Directory.GetFiles(
            fixture.ManagedRoot,
            "*",
            SearchOption.AllDirectories);
        long expectedBytes = files.Sum(static path => new FileInfo(path).Length);
        int hashedFiles = 0;
        long hashedBytes = 0;
        FileSystemManagedSetupRecoveryExecution executor = fixture.CreateExecutor(
            afterHashedFile: length =>
            {
                hashedFiles++;
                hashedBytes += length;
            });
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(fixture.Transaction.StatePathIdentity)
                .TryAcquireWriteLeaseAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

        ManagedSetupRecoveryExecutionPortOutcome outcome = await executor.ExecuteAsync(
            new ManagedSetupRecoveryExecutionRequest(
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.Equal(files.Length, hashedFiles);
        Assert.Equal(expectedBytes, hashedBytes);
    }

    /// <summary>A full staging child remains actionable after state deletion and converges.</summary>
    [Fact]
    public async Task FullStagingChildCrashPrefixReobservesAndCompletes()
    {
        RequireNative();
        using var cancellation = new CancellationTokenSource();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateFullStagingAsync(
            count =>
            {
                if (count == 1)
                {
                    cancellation.Cancel();
                }
            });
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        using (VersionManagerWriteLeaseResult lease =
               await new RecoveryLeaseSource(fixture.Transaction.StatePathIdentity)
                   .TryAcquireWriteLeaseAsync(
                       TimeSpan.FromSeconds(1),
                       TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired,
                await fixture.Executor.ExecuteAsync(
                    new ManagedSetupRecoveryExecutionRequest(
                        ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                        evidence.ExecutionToken!),
                    lease,
                    cancellation.Token));
        }

        FileSystemManagedSetupRecoveryExecution resumed = fixture.CreateExecutor();
        ManagedSetupRecoveryEvidenceObservation prefix = await resumed.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, prefix.Issue);
        using VersionManagerWriteLeaseResult resumedLease =
            await new RecoveryLeaseSource(fixture.Transaction.StatePathIdentity)
                .TryAcquireWriteLeaseAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);
        Assert.Equal(
            ManagedSetupRecoveryExecutionPortOutcome.Completed,
            await resumed.ExecuteAsync(
                new ManagedSetupRecoveryExecutionRequest(
                    ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                    prefix.ExecutionToken!),
                resumedLease,
                TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(FileSystemManagedInstallationRootProbe
            .GetStagingContainerPath(fixture.ManagedRoot)));
    }

}
