// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Xunit;
using static Nvt.Core.Tests.Launcher.Recovery.Execution.RecoveryEvidenceFixture;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Locks exact writer custody and fail-closed recovery execution.</summary>
public sealed partial class RecoveryMutationGuardTests
{
    /// <summary>READY convergence removes only residue and the marker.</summary>
    [Fact]
    public async Task ConvergeReadyPreservesExactRootAndBothStates()
    {
        RequireNative();
        int deletes = 0;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(
            count => deletes = count,
            readyLauncherState: true);
        string launcherState = RecoveryEvidenceFixture.LauncherPathFor(
            fixture.Transaction.StatePathIdentity);
        string rootProof = DirectoryProof(fixture.ManagedRoot);
        byte[] appBytes = await ReadAsync(
            fixture.Transaction.StatePathIdentity,
            TestContext.Current.CancellationToken);
        byte[] launcherBytes = await ReadAsync(
            launcherState,
            TestContext.Current.CancellationToken);
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
                ManagedSetupRecoveryAction.ConvergeReady,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.True(
            outcome == ManagedSetupRecoveryExecutionPortOutcome.Completed,
            $"Outcome {outcome}; deletes {deletes}; marker {File.Exists(FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(fixture.ManagedRoot))}");
        Assert.Equal(rootProof, DirectoryProof(fixture.ManagedRoot));
        Assert.Equal(appBytes, await ReadAsync(
            fixture.Transaction.StatePathIdentity,
            TestContext.Current.CancellationToken));
        Assert.Equal(launcherBytes, await ReadAsync(
            launcherState,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(FileSystemManagedInstallationRootProbe
            .GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>Same-file content drift is rejected even when its file identity is unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InPlaceTreeContentOrSizeMutationFailsBeforeDeletion(bool changeSize)
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string payload = Path.Combine(fixture.ManagedRoot, "versions", "1.0.6", "README.txt");
        byte[] original = await ReadAsync(payload, TestContext.Current.CancellationToken);
        byte[] replacement = changeSize
            ? [.. original, (byte)'!']
            : [.. original.Select(static value => (byte)(value ^ 0x5a))];
        await WriteBytesAsync(payload, replacement, TestContext.Current.CancellationToken);
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

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.True(File.Exists(fixture.Transaction.StatePathIdentity));
        Assert.True(File.Exists(FileSystemManagedInstallationRootProbe
            .GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>Application state bytes are bound in addition to the typed state value.</summary>
    [Fact]
    public async Task InPlaceApplicationStateMutationFailsBeforeDeletion()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string statePath = fixture.Transaction.StatePathIdentity;
        byte[] bytes = await ReadAsync(statePath, TestContext.Current.CancellationToken);
        int index = Array.FindIndex(bytes, static value => value is (byte)' ' or (byte)'\r' or (byte)'\n');
        Assert.True(index >= 0);
        bytes[index] = bytes[index] == (byte)' ' ? (byte)'\t' : (byte)' ';
        await WriteBytesAsync(statePath, bytes, TestContext.Current.CancellationToken);
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

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.True(File.Exists(statePath));
        Assert.True(File.Exists(FileSystemManagedInstallationRootProbe
            .GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

    /// <summary>Replacing Application state with byte-identical content still invalidates identity.</summary>
    [Fact]
    public async Task ByteIdenticalApplicationStateReplacementFailsBeforeDeletion()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string statePath = fixture.Transaction.StatePathIdentity;
        byte[] bytes = await ReadAsync(statePath, TestContext.Current.CancellationToken);
        new FileInfo(statePath).MoveTo(statePath + ".retained");
        await WriteBytesAsync(statePath, bytes, TestContext.Current.CancellationToken);
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

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.True(File.Exists(statePath));
        Assert.True(Directory.Exists(fixture.ManagedRoot));
    }

    /// <summary>A held marker delete followed by external recreation cannot report Completed.</summary>
    [Fact]
    public async Task MarkerRecreationAtDeleteBoundaryFailsClosed()
    {
        RequireNative();
        string? marker = null;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(
            _ => Write(marker!, "recreated-marker"),
            readyLauncherState: true);
        marker = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(
            fixture.ManagedRoot);
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
                ManagedSetupRecoveryAction.ConvergeReady,
                evidence.ExecutionToken!),
            lease,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired, outcome);
        Assert.True(File.Exists(marker));
    }

    /// <summary>A state recreated immediately after held deletion cannot advance the sequence.</summary>
    [Fact]
    public async Task LauncherStateRecreationAtDeleteBoundaryFailsClosed()
    {
        RequireNative();
        string? launcherState = null;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync(
            count =>
            {
                if (count == 1)
                {
                    Write(launcherState!, "foreign-state");
                }
            },
            withLauncherState: true);
        launcherState = RecoveryEvidenceFixture.LauncherPathFor(
            fixture.Transaction.StatePathIdentity);
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
        Assert.True(File.Exists(launcherState));
        Assert.True(File.Exists(fixture.Transaction.StatePathIdentity));
    }

    /// <summary>A reparse replacement after observation is never accepted as the held payload file.</summary>
    [Fact]
    public async Task PayloadReparseReplacementFailsClosedBeforeDeletion()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string payload = Path.Combine(fixture.ManagedRoot, "versions", "1.0.6", "README.txt");
        string outside = Path.Combine(Path.GetDirectoryName(fixture.ManagedRoot)!, "outside.txt");
        await WriteAsync(outside, "outside", TestContext.Current.CancellationToken);
        File.Delete(payload);
        try
        {
            _ = File.CreateSymbolicLink(payload, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Skip("Windows symbolic-link privilege is required.");
        }
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
        Assert.Equal("outside", await TextAsync(
            outside,
            TestContext.Current.CancellationToken));
        Assert.True(File.Exists(fixture.Transaction.StatePathIdentity));
    }

    /// <summary>A marker removed after observation invalidates the token before mutation.</summary>
    [Fact]
    public async Task MarkerMissingAfterObservationFailsBeforeDeletion()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        File.Delete(FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(
            fixture.ManagedRoot));
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

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.True(File.Exists(fixture.Transaction.StatePathIdentity));
        Assert.True(Directory.Exists(fixture.ManagedRoot));
    }

    /// <summary>A contended marker returns a typed non-mutating outcome.</summary>
    [Fact]
    public async Task ContendedMarkerFailsTypedBeforeDeletion()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string marker = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(
            fixture.ManagedRoot);
        using var contention = new FileStream(
            marker,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
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

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable, outcome);
        Assert.True(File.Exists(fixture.Transaction.StatePathIdentity));
        Assert.True(Directory.Exists(fixture.ManagedRoot));
    }

    /// <summary>A marker replaced by a reparse point never authorizes mutation.</summary>
    [Fact]
    public async Task MarkerReparseReplacementFailsBeforeDeletion()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string marker = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(
            fixture.ManagedRoot);
        string outside = Path.Combine(Path.GetDirectoryName(fixture.ManagedRoot)!, "outside-marker");
        await WriteAsync(outside, "outside", TestContext.Current.CancellationToken);
        File.Delete(marker);
        try
        {
            _ = File.CreateSymbolicLink(marker, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Skip("Windows symbolic-link privilege is required.");
        }
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
        Assert.Equal("outside", await TextAsync(
            outside,
            TestContext.Current.CancellationToken));
        Assert.True(File.Exists(fixture.Transaction.StatePathIdentity));
    }

    /// <summary>An absent staging container replaced by a directory reparse point fails closed.</summary>
    [Fact]
    public async Task StagingReparseReplacementFailsBeforeDeletion()
    {
        RequireNative();
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateAsync();
        ManagedSetupRecoveryEvidenceObservation evidence = await fixture.Executor.ObserveAsync(
            fixture.Transaction,
            TestContext.Current.CancellationToken);
        string staging = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(
            fixture.ManagedRoot);
        string outside = Path.Combine(Path.GetDirectoryName(fixture.ManagedRoot)!, "outside-staging");
        _ = Directory.CreateDirectory(outside);
        try
        {
            _ = Directory.CreateSymbolicLink(staging, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Skip("Windows symbolic-link privilege is required.");
        }
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
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.True(File.Exists(fixture.Transaction.StatePathIdentity));
    }

    /// <summary>Recreating an empty staging container after held deletion cannot complete.</summary>
    [Fact]
    public async Task StagingContainerRecreationAtDeleteBoundaryFailsClosed()
    {
        RequireNative();
        string? staging = null;
        using RecoveryEvidenceFixture fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync(
            count =>
            {
                if (count == 1)
                {
                    _ = Directory.CreateDirectory(staging!);
                }
            });
        staging = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(fixture.ManagedRoot);
        _ = Directory.CreateDirectory(staging);
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

        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired, outcome);
        Assert.True(Directory.Exists(staging));
        Assert.True(File.Exists(FileSystemManagedInstallationRootProbe
            .GetTransactionMarkerPath(fixture.ManagedRoot)));
    }

}
