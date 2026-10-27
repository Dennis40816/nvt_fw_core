// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Checks product name matching while preserving held on-disk deletion spelling and order.</summary>
public sealed class RecoveryCaseSpellingTests
{
    /// <summary>The production payload proof admits case variants and rollback retains canonical deletion totals.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CaseVariantNamesCompleteFullRollbackWithOriginalDeletionCount(
        bool directoryCase, bool withLauncherState)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync(withLauncherState: withLauncherState);
        RenameCase(fixture, directoryCase);
        string readme = Path.Combine(fixture.VersionRoot, "README.txt");
        int? readmeBoundary = null;
        int deletes = 0;
        var executor = fixture.CreateExecutor(count =>
        {
            deletes = count;
            if (readmeBoundary is null && !File.Exists(readme)) readmeBoundary = count;
        }, useProductionRepository: true);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        Assert.True(fixture.Policy.Projections > 0);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.Equal(withLauncherState ? 26 : 25, deletes);
        Assert.Equal((directoryCase ? 8 : 10) + (withLauncherState ? 1 : 0), readmeBoundary);
        Assert.False(Directory.Exists(fixture.ManagedRoot));
        Assert.False(File.Exists(fixture.StatePath));
        Assert.False(File.Exists(fixture.LauncherStatePath));
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    /// <summary>Case variants remain a legal prefix after the first ordinary file is deleted.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CaseVariantNamesRestartAfterFirstOrdinaryFileAndComplete(
        bool directoryCase, bool withLauncherState)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync(withLauncherState: withLauncherState);
        RenameCase(fixture, directoryCase);
        int boundary = withLauncherState ? 3 : 2;
        await RecoveryExecutionBoundaryTests.InterruptAfterAsync(fixture, boundary);
        Assert.False(File.Exists(Path.Combine(fixture.VersionRoot, "tools", "worker", "1.0.0", "worker.exe")));
        Assert.True(File.Exists(Path.Combine(fixture.VersionRoot, "README.txt")));
        int deletes = 0;
        var executor = fixture.CreateExecutor(count => deletes = count, useProductionRepository: true);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        var repeated = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(evidence.ExecutionToken, repeated.ExecutionToken);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.Equal((withLauncherState ? 26 : 25) - boundary, deletes);
        Assert.False(Directory.Exists(fixture.ManagedRoot));
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    private static void RenameCase(RecoveryEvidenceFixture fixture, bool directoryCase)
    {
        string original = directoryCase
            ? Path.Combine(fixture.ManagedRoot, "versions") : Path.Combine(fixture.VersionRoot, "README.txt");
        string renamed = directoryCase
            ? Path.Combine(fixture.ManagedRoot, "Versions") : Path.Combine(fixture.VersionRoot, "readme.txt");
        string temporary = string.Concat(original, ".renamed");
        if (directoryCase)
        {
            new DirectoryInfo(original).MoveTo(temporary);
            new DirectoryInfo(temporary).MoveTo(renamed);
        }
        else
        {
            new FileInfo(original).MoveTo(temporary);
            new FileInfo(temporary).MoveTo(renamed);
        }
        Assert.Contains(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(renamed)!),
            path => string.Equals(path, renamed, StringComparison.Ordinal));
    }
}
