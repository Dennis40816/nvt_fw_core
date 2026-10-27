// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.TestSupport;
using Xunit;
using static Nvt.Core.Tests.Launcher.Recovery.Execution.RecoveryEvidenceFixture;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Locks exact writer custody and fail-closed recovery execution.</summary>
public sealed partial class RecoveryMutationGuardTests
{
    /// <summary>Held tree custody blocks content mutation after state deletion.</summary>
    [Fact]
    public async Task RemainingTreeMutationAtDeleteBoundaryStopsBeforeTreeDeletion()
    {
        RequireNative();
        string? payload = null;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(count =>
        {
            if (count == 1)
            {
                Write(payload!, "changed after state deletion");
            }
        });
        payload = Path.Combine(fixture.ManagedRoot, "versions", "1.0.6", "README.txt");
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

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired, outcome);
        Assert.True(File.Exists(payload));
        Assert.NotEqual(
            "changed after state deletion",
            await TextAsync(payload, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(FileSystemManagedInstallationRootProbe
            .GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>Replacing the managed root after state deletion cannot satisfy remaining proof.</summary>
    [Fact]
    public async Task ManagedRootReplacementAtDeleteBoundaryFailsClosed()
    {
        RequireNative();
        string? root = null;
        string? original = null;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(count =>
        {
            if (count == 1)
            {
                new DirectoryInfo(root!).MoveTo(original!);
                _ = Directory.CreateDirectory(root!);
            }
        });
        root = fixture.ManagedRoot;
        original = string.Concat(root, "-original");
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

        Assert.NotEqual(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.True(Directory.Exists(root));
        Assert.False(Directory.Exists(original));
        Assert.True(File.Exists(FileSystemManagedInstallationRootProbe
            .GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>The exclusive delete handle blocks a rename in the final held-object window.</summary>
    [Fact]
    public void HeldDeleteBlocksRenameBeforeIdentityAndTopologyRevalidation()
    {
        RequireNative();
        using var workspace = TestWorkspace.Create();
        string root = Path.Combine(workspace.RootPath, "root");
        string target = Path.Combine(root, "target.txt");
        string moved = Path.Combine(workspace.RootPath, "moved.txt");
        _ = Directory.CreateDirectory(root);
        Write(target, "exact");
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireDeleteCapableTree(
            root,
            new WindowsStableTreeLimits(1, 1, 16),
            TestContext.Current.CancellationToken);
        Assert.True(acquired.IsAcquired, acquired.Issue.ToString());
        bool renameBlocked = false;
        using (WindowsStablePathCustody custody = acquired.Custody!)
        {
            WindowsStableCustodyIssue deleted = custody.TryDeleteHeldEntry(
                "target.txt",
                directory: false,
                afterExclusiveOpen: () =>
                {
                    try
                    {
                        new FileInfo(target).MoveTo(moved);
                    }
                    catch (IOException)
                    {
                        renameBlocked = true;
                    }
                });
            Assert.Equal(WindowsStableCustodyIssue.None, deleted);
        }
        Assert.True(renameBlocked);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(moved));
    }

    /// <summary>Marker-last completion rechecks every action-specific postcondition.</summary>
    [Theory]
    [InlineData("application")]
    [InlineData("root")]
    [InlineData("staging")]
    public async Task ResidueRecreatedAtMarkerDeleteBoundaryCannotComplete(string target)
    {
        RequireNative();
        string? statePath = null;
        string? managedRoot = null;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(count =>
        {
            if (count != 25)
            {
                return;
            }
            if (target == "application")
            {
                Write(statePath!, "foreign-state");
            }
            else
            {
                _ = Directory.CreateDirectory(target == "root"
                    ? managedRoot!
                    : FileSystemManagedInstallationRootProbe.GetStagingContainerPath(managedRoot!));
            }
        });
        statePath = fixture.Transaction.StatePathIdentity;
        managedRoot = fixture.ManagedRoot;
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(statePath).TryAcquireWriteLeaseAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

        ManagedSetupRecoveryExecutionPortOutcome outcome = await fixture.Executor.ExecuteAsync(
            new ManagedSetupRecoveryExecutionRequest(
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
    }

    /// <summary>A retained-manifest prefix rejects a manifest hole and foreign residue.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedManifestPrefixRejectsHoleOrForeignResidue(bool createForeignResidue)
    {
        RequireNative();
        using var cancellation = new CancellationTokenSource();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(count =>
        {
            if (count == 2)
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
        if (createForeignResidue)
        {
            await WriteAsync(
                Path.Combine(fixture.ManagedRoot, "foreign.txt"),
                "foreign",
                TestContext.Current.CancellationToken);
        }
        else
        {
            File.Delete(Path.Combine(
                fixture.ManagedRoot,
                FileSystemManagedVersionRepository.VersionsDirectoryName,
                fixture.Transaction.Candidate.Version,
                "RELEASE-MANIFEST.json"));
        }

        ManagedSetupRecoveryEvidenceObservation observation = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(ManagedSetupRecoveryEvidenceIssue.None, observation.Issue);
    }

    /// <summary>Unchanged exact evidence produces one deeply immutable value-equal token.</summary>
    [Fact]
    public async Task ReobservationProducesValueEqualTokenAndReplacementChangesIt()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();

        ManagedSetupRecoveryEvidenceObservation first = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        ManagedSetupRecoveryEvidenceObservation second = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, first.Issue);
        Assert.Equal(first.ExecutionToken, second.ExecutionToken);

        string launcherPath = Path.Combine(
            fixture.ManagedRoot,
            RecoveryEvidenceFixture.Layout.DistributionLauncherFileName);
        byte[] unchanged = await ReadAsync(
            launcherPath,
            TestContext.Current.CancellationToken);
        new FileInfo(launcherPath).MoveTo(Path.Combine(Path.GetDirectoryName(fixture.ManagedRoot)!, "retained-launcher"));
        await WriteBytesAsync(
            launcherPath,
            unchanged,
            TestContext.Current.CancellationToken);

        ManagedSetupRecoveryEvidenceObservation replacement = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, replacement.Issue);
        Assert.NotEqual(first.ExecutionToken, replacement.ExecutionToken);
    }

    /// <summary>A replayable disposable cannot impersonate the canonical writer lease.</summary>
    [Fact]
    public async Task ArbitraryDisposableLeaseCannotAuthorizeRecoveryMutation()
    {
        using var workspace = TestWorkspace.Create();
        string statePath = Path.Combine(workspace.RootPath, "state", "version-manager.v1.json");
        var executor = RecoveryEvidenceFixture.CreateUnobservedExecutor(statePath);
        using var disposable = new NoOpDisposable();
        using var forgedLease = new VersionManagerWriteLeaseResult(
            VersionManagerWriteLeaseIssue.None,
            disposable);
        var request = new ManagedSetupRecoveryExecutionRequest(
            ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            new ForeignToken());

        ManagedSetupRecoveryExecutionPortOutcome outcome = await executor.ExecuteAsync(
            request,
            forgedLease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable, outcome);
        Assert.False(Directory.Exists(Path.GetDirectoryName(statePath)));
    }

    /// <summary>A live production lease for another exact state path grants no authority.</summary>
    [Fact]
    public async Task DifferentStatePathLeaseCannotAuthorizeRecoveryMutation()
    {
        RequireNative();
        using var workspace = TestWorkspace.Create();
        string expectedState = Path.Combine(workspace.RootPath, "expected", "version-manager.v1.json");
        string foreignState = Path.Combine(workspace.RootPath, "foreign", "version-manager.v1.json");
        using VersionManagerWriteLeaseResult foreignLease =
            await new RecoveryLeaseSource(foreignState).TryAcquireWriteLeaseAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);
        Assert.True(foreignLease.IsAcquired);
        var executor = RecoveryEvidenceFixture.CreateUnobservedExecutor(expectedState);
        var request = new ManagedSetupRecoveryExecutionRequest(
            ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            new ForeignToken());

        ManagedSetupRecoveryExecutionPortOutcome outcome = await executor.ExecuteAsync(
            request,
            foreignLease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable, outcome);
        Assert.False(File.Exists(expectedState));
    }

    /// <summary>Disposing the exact production lease revokes recovery authority.</summary>
    [Fact]
    public async Task DisposedExactLeaseCannotAuthorizeRecoveryMutation()
    {
        RequireNative();
        using var workspace = TestWorkspace.Create();
        string statePath = Path.Combine(workspace.RootPath, "state", "version-manager.v1.json");
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(statePath).TryAcquireWriteLeaseAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);
        Assert.True(lease.IsAcquired);
        lease.Dispose();
        var executor = RecoveryEvidenceFixture.CreateUnobservedExecutor(statePath);
        var request = new ManagedSetupRecoveryExecutionRequest(
            ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            new ForeignToken());

        ManagedSetupRecoveryExecutionPortOutcome outcome = await executor.ExecuteAsync(
            request,
            lease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable, outcome);
        Assert.False(File.Exists(statePath));
    }

    /// <summary>A live exact writer does not make a foreign opaque token actionable.</summary>
    [Fact]
    public async Task ForeignTokenFailsClosedUnderExactLiveLease()
    {
        RequireNative();
        using var workspace = TestWorkspace.Create();
        string statePath = Path.Combine(workspace.RootPath, "state", "version-manager.v1.json");
        using VersionManagerWriteLeaseResult lease =
            await new RecoveryLeaseSource(statePath).TryAcquireWriteLeaseAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);
        Assert.True(lease.IsAcquired);
        var executor = RecoveryEvidenceFixture.CreateUnobservedExecutor(statePath);
        var request = new ManagedSetupRecoveryExecutionRequest(
            ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            new ForeignToken());

        ManagedSetupRecoveryExecutionPortOutcome outcome = await executor.ExecuteAsync(
            request,
            lease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.False(File.Exists(statePath));
    }

    /// <summary>Cancellation before any deletion is a typed result and never escapes as OCE.</summary>
    [Fact]
    public async Task PreMutationCancellationReturnsTypedCancelled()
    {
        using var workspace = TestWorkspace.Create();
        string statePath = Path.Combine(workspace.RootPath, "state", "version-manager.v1.json");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var executor = RecoveryEvidenceFixture.CreateUnobservedExecutor(statePath);
        using var disposable = new NoOpDisposable();
        using var lease = new VersionManagerWriteLeaseResult(
            VersionManagerWriteLeaseIssue.None,
            disposable);
        var request = new ManagedSetupRecoveryExecutionRequest(
            ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            new ForeignToken());

        ManagedSetupRecoveryExecutionPortOutcome outcome = await executor.ExecuteAsync(
            request,
            lease,
            cancellation.Token);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Cancelled, outcome);
    }

}
