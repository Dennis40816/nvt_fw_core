// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Checks authority retention, exact staging identity and legal restart suffixes.</summary>
public sealed class RecoveryExecutionRegressionTests
{
    /// <summary>Foreign root entries created after state deletion leave the authority manifest intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignRootEntryBeforeAuthorityDeletionRequiresManualIntervention(bool directory)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        string foreign = Path.Combine(fixture.ManagedRoot, "foreign");
        string manifest = Path.Combine(fixture.VersionRoot, "RELEASE-MANIFEST.json");
        var executor = fixture.CreateExecutor(count =>
        {
            if (count != 1) return;
            if (directory) Directory.CreateDirectory(foreign);
            else RecoveryEvidenceFixture.Write(foreign, "foreign");
        });
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired, outcome);
        Assert.True(File.Exists(manifest));
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.False(File.Exists(fixture.StatePath));
        if (directory) Assert.True(Directory.Exists(foreign));
        else Assert.Equal("foreign", await RecoveryEvidenceFixture.TextAsync(foreign, TestContext.Current.CancellationToken));
    }

    /// <summary>A differently cased authority name is deleted once, after every other tree file.</summary>
    [Fact]
    public async Task DifferentlyCasedAuthorityManifestIsDeletedOnceAndLast()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        string canonical = Path.Combine(fixture.VersionRoot, "RELEASE-MANIFEST.json");
        string temporary = Path.Combine(fixture.VersionRoot, "renamed-manifest");
        string manifest = Path.Combine(fixture.VersionRoot, "release-manifest.json");
        new FileInfo(canonical).MoveTo(temporary);
        new FileInfo(temporary).MoveTo(manifest);
        Assert.Contains(Directory.EnumerateFiles(fixture.VersionRoot), path => string.Equals(path, manifest, StringComparison.Ordinal));
        int deletes = 0;
        int? authorityBoundary = null;
        var executor = fixture.CreateExecutor(count =>
        {
            deletes = count;
            if (File.Exists(manifest) || authorityBoundary is not null) return;
            authorityBoundary = count;
            Assert.True(File.Exists(fixture.MarkerPath));
            Assert.Empty(Directory.EnumerateFiles(fixture.ManagedRoot, "*", SearchOption.AllDirectories));
        });
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.Equal(21, authorityBoundary);
        Assert.Equal(25, deletes);
        Assert.False(Directory.Exists(fixture.ManagedRoot));
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    /// <summary>Staging restarts after seed removal retain the exact original remaining deletion count.</summary>
    [Theory]
    [InlineData(13)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    public async Task StagingPrefixWithoutSeedReobservesAndCompletes(int boundary)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateFullStagingAsync();
        await RecoveryExecutionBoundaryTests.InterruptAfterAsync(fixture, boundary);
        string container = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(fixture.ManagedRoot);
        Assert.False(File.Exists(Path.Combine(container, fixture.Transaction.TransactionId,
            RecoveryEvidenceFixture.Layout.SeedFileName)));
        int deletes = 0;
        var executor = fixture.CreateExecutor(count => deletes = count);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        var repeated = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(evidence.ExecutionToken, repeated.ExecutionToken);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.Equal(26 - boundary, deletes);
        Assert.False(Directory.Exists(container));
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    /// <summary>Foreign-only staging without a seed is a manual-intervention diagnosis with zero mutation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOnlyStagingWithoutSeedRequiresManualInterventionWithZeroMutation(bool withApplicationState)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync();
        string container = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(fixture.ManagedRoot);
        string child = Path.Combine(container, fixture.Transaction.TransactionId);
        Directory.CreateDirectory(child);
        RecoveryEvidenceFixture.Write(Path.Combine(child, "foreign.txt"), "foreign");
        if (withApplicationState) RecoveryEvidenceFixture.Write(fixture.StatePath, "synthetic-state\n");
        string before = RecoveryEvidenceFixture.DirectoryProof(container);
        int deletes = 0;
        var executor = fixture.CreateExecutor(count => deletes = count);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.Invalid, evidence.Issue);
        Assert.Null(evidence.ExecutionToken);
        var experience = new ManagedInstallationRecoveryExperience(fixture.ApplicationStore,
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new RecordingRecoveryMarkerProbe(new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Exact, fixture.Transaction)),
            new RecordingRecoveryLifetimeProbe(), executor);
        var diagnosis = await experience.DiagnoseAsync(fixture.ManagedRoot, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryOutcome.ManualInterventionRequired, diagnosis.Outcome);
        Assert.Null(diagnosis.Plan);
        Assert.Equal(0, deletes);
        Assert.Equal(before, RecoveryEvidenceFixture.DirectoryProof(container));
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.Equal(withApplicationState, File.Exists(fixture.StatePath));
    }

    /// <summary>A newly created or replaced empty staging container is never deleted using an old token.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewEmptyStagingContainerAfterObservationIsRetained(bool initiallyPresent)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        string container = FileSystemManagedInstallationRootProbe.GetStagingContainerPath(fixture.ManagedRoot);
        string retained = string.Concat(container, ".retained");
        if (initiallyPresent) Directory.CreateDirectory(container);
        var executor = fixture.CreateExecutor(count =>
        {
            if (count != 1) return;
            if (initiallyPresent) new DirectoryInfo(container).MoveTo(retained);
            Directory.CreateDirectory(container);
        });
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.True(Directory.Exists(container));
        Assert.Empty(Directory.EnumerateFileSystemEntries(container));
        Assert.Equal(initiallyPresent, Directory.Exists(retained));
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    /// <summary>An empty final root recreated after an absent-root observation returns SourceChanged without mutation.</summary>
    [Fact]
    public async Task EmptyNonStagingRootCreatedAfterObservationReturnsSourceChanged()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateTerminalSkeletonAsync();
        await RecoveryExecutionBoundaryTests.InterruptAfterAsync(fixture, 3);
        Assert.False(Directory.Exists(fixture.ManagedRoot));
        int deletes = 0;
        var executor = fixture.CreateExecutor(count => deletes = count);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        Directory.CreateDirectory(fixture.ManagedRoot);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.Equal(0, deletes);
        Assert.True(Directory.Exists(fixture.ManagedRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.ManagedRoot));
        Assert.True(File.Exists(fixture.MarkerPath));
    }
}
