// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Checks that surviving tree evidence is exactly one ordered suffix and never a content hint.</summary>
public sealed class RecoveryPrefixGuardTests
{
    /// <summary>Cancellation during held hashing releases custody before returning the pre-mutation result.</summary>
    [Fact]
    public async Task CancelledTreeProofReleasesCustodyWithoutMutation()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        var evidence = await fixture.Executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        string before = RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var executor = fixture.CreateExecutor(afterHashedFile: _ => cancellation.Cancel());
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, cancellation.Token);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Cancelled, outcome);
        Assert.Equal(before, RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot));
        Assert.True(File.Exists(fixture.StatePath));
        Assert.True(File.Exists(fixture.MarkerPath));
        string payload = Path.Combine(fixture.VersionRoot, "README.txt");
        string retained = Path.Combine(Path.GetDirectoryName(fixture.ManagedRoot)!, "released-payload");
        new FileInfo(payload).MoveTo(retained);
        Assert.True(File.Exists(retained));
    }

    /// <summary>A later ordinary-file hole is rejected while an earlier ordinary file remains.</summary>
    [Fact]
    public async Task HoleInOrderedDeletionPrefixCannotAuthorizeFurtherMutation()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        await RecoveryExecutionBoundaryTests.InterruptAfterAsync(fixture, 11);
        Assert.True(File.Exists(Path.Combine(fixture.ManagedRoot, RecoveryEvidenceFixture.Layout.BootstrapFileName)));
        File.Delete(Path.Combine(fixture.ManagedRoot, RecoveryEvidenceFixture.Layout.SeedFileName));
        int deletes = 0;
        var executor = fixture.CreateExecutor(count => deletes = count);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.Invalid, evidence.Issue);
        Assert.Null(evidence.ExecutionToken);
        Assert.Equal(0, deletes);
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    /// <summary>Same-length tampering of the retained authority manifest fails its admitted digest before decoding.</summary>
    [Fact]
    public async Task TamperedRetainedAuthorityManifestCannotAuthorizeFurtherMutation()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        await RecoveryExecutionBoundaryTests.InterruptAfterAsync(fixture, 11);
        string path = Path.Combine(fixture.VersionRoot, "RELEASE-MANIFEST.json");
        byte[] bytes = await RecoveryEvidenceFixture.ReadAsync(path, TestContext.Current.CancellationToken);
        bytes[0] ^= 1;
        TestFiles.WriteAllBytes(path, bytes);
        var evidence = await fixture.Executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.Invalid, evidence.Issue);
        Assert.Null(evidence.ExecutionToken);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    /// <summary>Byte-identical payload replacement changes topology identity even with unchanged admitted size and digest.</summary>
    [Fact]
    public async Task SameContentReplacementWithDifferentHeldIdentityFailsBeforeStateDeletion()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        var evidence = await fixture.Executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        string payload = Path.Combine(fixture.VersionRoot, "README.txt");
        byte[] bytes = await RecoveryEvidenceFixture.ReadAsync(payload, TestContext.Current.CancellationToken);
        new FileInfo(payload).MoveTo(Path.Combine(Path.GetDirectoryName(fixture.ManagedRoot)!, "retained-payload"));
        TestFiles.WriteAllBytes(payload, bytes);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await fixture.Executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.SourceChanged, outcome);
        Assert.True(File.Exists(fixture.StatePath));
        Assert.True(File.Exists(fixture.MarkerPath));
        Assert.Equal(bytes, await RecoveryEvidenceFixture.ReadAsync(payload, TestContext.Current.CancellationToken));
    }

    /// <summary>A torn marker is classified before evidence or execution and retains every byte.</summary>
    [Fact]
    public async Task TornMarkerRequiresManualInterventionWithZeroMutation()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        RecoveryEvidenceFixture.Write(fixture.MarkerPath, "{torn");
        string before = RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot);
        int deletes = 0;
        var diagnosis = new ManagedInstallationRecoveryExperience(fixture.ApplicationStore,
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new FileSystemManagedSetupRecoveryProbe(new SyntheticMarkerCodec(), 65_536, 65_536),
            new RecordingRecoveryLifetimeProbe(), fixture.CreateExecutor(count => deletes = count));
        var result = await diagnosis.DiagnoseAsync(fixture.ManagedRoot, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryOutcome.ManualInterventionRequired, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, deletes);
        Assert.Equal(before, RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot));
        Assert.Equal("{torn", await RecoveryEvidenceFixture.TextAsync(fixture.MarkerPath, TestContext.Current.CancellationToken));
    }

    /// <summary>A staging tree outside the exact inventory and restart prefix is non-actionable and untouched.</summary>
    [Fact]
    public async Task UnprovedStagingInventoryRequiresManualInterventionWithZeroMutation()
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateFullStagingAsync();
        File.Delete(fixture.StatePath);
        string child = Path.Combine(FileSystemManagedInstallationRootProbe.GetStagingContainerPath(fixture.ManagedRoot),
            fixture.Transaction.TransactionId);
        RecoveryEvidenceFixture.Write(Path.Combine(child, "foreign.txt"), "foreign");
        string before = RecoveryEvidenceFixture.DirectoryProof(child);
        int deletes = 0;
        var diagnosis = new ManagedInstallationRecoveryExperience(fixture.ApplicationStore,
            new RecordingRecoveryRootProbe(ManagedInstallationRootStatus.Residue),
            new RecordingRecoveryMarkerProbe(new ManagedSetupRecoveryFact(ManagedSetupRecoveryFactKind.Exact, fixture.Transaction)),
            new RecordingRecoveryLifetimeProbe(), fixture.CreateExecutor(count => deletes = count));
        var result = await diagnosis.DiagnoseAsync(fixture.ManagedRoot, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryOutcome.ManualInterventionRequired, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, deletes);
        Assert.Equal(before, RecoveryEvidenceFixture.DirectoryProof(child));
        Assert.True(File.Exists(fixture.MarkerPath));
    }
}
