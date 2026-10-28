// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using TransactionRepository = Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport.TransactionRepository;

namespace Nvt.Core.Tests.Launcher.Integration;

public sealed partial class VersionManagementExperienceTests
{
    /// <summary>Read-only startup preserves install/delete journals and observed inventory; ordinary startup still recovers.</summary>
    [Theory]
    [InlineData(ManagedVersionMutationKind.Install, false)]
    [InlineData(ManagedVersionMutationKind.Install, true)]
    [InlineData(ManagedVersionMutationKind.Delete, true)]
    public async Task ReadOnlyInitializationPreservesPendingMutationWithoutLeaseSaveOrDelete(
        ManagedVersionMutationKind kind, bool targetInstalled)
    {
        ManagedVersionAdmission active = Admission("0.10.5");
        ManagedVersionAdmission target = Admission("0.10.4");
        VersionManagerState prepared = State(
            kind == ManagedVersionMutationKind.Delete ? [active, target] : [active],
            active: "0.10.5", lastKnownGood: "0.10.5")
            .WithPendingMutation(new(kind, target));
        using var store = new MemoryStateStore(prepared);
        var repository = new TransactionRepository(targetInstalled ? [active, target] : [active]);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new FixedCatalogSource(Catalog("0.10.6")), repository);

        VersionManagementSnapshot result = await experience.InitializeAsync(
            TestContext.Current.CancellationToken, isReadOnly: true);

        Assert.Same(prepared, result.State);
        Assert.Same(prepared, store.State);
        Assert.Equal(VersionManagerStateLoadIssue.None, result.StateIssue);
        Assert.Equal(ManagedVersionInventoryReadIssue.None, result.InventoryIssue);
        Assert.Equal(targetInstalled ? 2 : 1, result.Inventory.Versions.Count);
        if (kind == ManagedVersionMutationKind.Install && targetInstalled)
        {
            Assert.Equal(ManagedVersionAdmissionState.RecoveryCandidate,
                result.Inventory.Find(target.Version)!.AdmissionState);
        }
        Assert.Equal(0, store.WriteLeaseCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Equal(0, repository.InstallCalls);
        Assert.Equal(0, repository.VerifyPackageCalls);

        VersionManagementSnapshot recovered = await experience.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Null(recovered.State!.PendingMutation);
        Assert.Equal(1, store.WriteLeaseCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(kind == ManagedVersionMutationKind.Delete ? 1 : 0, repository.DeleteCalls);
    }
}
