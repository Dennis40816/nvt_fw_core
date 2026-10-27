// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Tests.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Checks the public evidence constructor's production repositories with strict synthetic projections.</summary>
public sealed class RecoveryProductionExecutionTests
{
    /// <summary>The real admission reader enforces its independent byte ceiling and admitted cases complete physical rollback.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task ProductionRecoveryConsumesInclusiveAdmissionByteCeiling(int offset, bool accepted)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync(includeLauncher: true);
        int admissionBytes = new AdmissionCodec().Encode(fixture.Admission).Length;
        var limits = PackageFixture.FrozenLimits with { MaximumAdmissionBytes = admissionBytes + offset };
        var executor = new FileSystemManagedSetupRecoveryExecution(fixture.ApplicationStore,
            RecoveryEvidenceFixture.LauncherSuffix, fixture.LauncherStore, RecoveryEvidenceFixture.Layout,
            path => new SyntheticSeedStore(path, fixture.Seed), fixture.Policy, limits, new AdmissionCodec());
        string before = RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(accepted ? ManagedSetupRecoveryEvidenceIssue.None
            : ManagedSetupRecoveryEvidenceIssue.SourceChanged, evidence.Issue);
        Assert.Equal(before, RecoveryEvidenceFixture.DirectoryProof(fixture.ManagedRoot));
        Assert.True(File.Exists(fixture.StatePath));
        Assert.True(File.Exists(fixture.MarkerPath));
        if (!accepted)
        {
            Assert.Null(evidence.ExecutionToken);
            return;
        }
        Assert.True(fixture.Policy.Projections > 0);
        Assert.Equal(fixture.Package.ManifestBytes, fixture.Policy.ObservedBytes);
        var launcher = Assert.IsType<Nvt.Core.Launcher.Contracts.ManagedLauncherIdentity>(evidence.InstalledLauncher);
        Assert.Equal(fixture.Admission.Version, launcher.OwnerAppVersion);
        Assert.Equal(fixture.Admission.AdmissionIdentity, launcher.OwnerAdmissionIdentity);
        var expectedLauncher = fixture.Package.Manifest.Launcher!;
        Assert.Equal(expectedLauncher.Size, launcher.Size);
        Assert.Equal(expectedLauncher.Sha256, launcher.Sha256);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.Completed, outcome);
        Assert.False(Directory.Exists(fixture.ManagedRoot));
        Assert.False(File.Exists(fixture.StatePath));
        Assert.False(File.Exists(fixture.LauncherStatePath));
        Assert.False(File.Exists(fixture.MarkerPath));
    }
}
