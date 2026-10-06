// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Tests.Launcher.Coordination;

internal static partial class ManagedMutationTestSupport
{
    internal const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    internal static VersionManagerState State(IReadOnlyList<ManagedVersionAdmission> admissions,
        string active, string lastKnownGood, string source = "source-root", VersionSourceRegistryState? sourceRegistryState = null)
        => VersionManagerState.Create(source, ManagedAppVersion.Parse(active), ManagedAppVersion.Parse(lastKnownGood),
            admissions, null, null, false, managedRootIdentity: "managed-root", sourceRegistryState: sourceRegistryState);
    internal static ManagedVersionAdmission Admission(string version) => new(ManagedAppVersion.Parse(version), $"identity-{version}", Hash);
    internal static TestCatalog Catalog(string version) => new([UpdateCatalogVersionSnapshot.Create(
        134_217_728, 65_536, ManagedAppVersion.Parse(version), new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
        new($"packages/SyntheticApp-v{version}-win-x64.zip"), 42, Hash, Hash, $"Release {version}", UpdateNotificationPolicy.Notify)]);
    internal sealed record TestCatalog(IReadOnlyList<UpdateCatalogVersionSnapshot> Versions);
    internal sealed class MemoryStateStore(VersionManagerState state) : CoordinationStateStore(state);
    internal sealed class FailingStateStore(VersionManagerState state, int failOnSave) : CoordinationStateStore(state, failOnSave);

    internal interface IBoundPackageSelection : IManagedPackageSelection
    {
        string? Source { get; set; }
    }
    internal sealed class FixedPackageSelection(TestCatalog snapshot) : IBoundPackageSelection
    {
        public string? Source { get; set; }
        public ValueTask<UpdateCatalogVersionSnapshot?> SelectPackageAsync(
            VersionManagerState state, ManagedAppVersion version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(state.UpdateSource == Source ? snapshot.Versions.SingleOrDefault(p => p.Version == version) : null);
        }
    }
    internal sealed class MutablePackageSelection(TestCatalog snapshot) : IBoundPackageSelection
    {
        public string? Source { get; set; }
        internal TestCatalog Snapshot { get; set; } = snapshot;
        public ValueTask<UpdateCatalogVersionSnapshot?> SelectPackageAsync(
            VersionManagerState state, ManagedAppVersion version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(state.UpdateSource == Source ? Snapshot.Versions.SingleOrDefault(p => p.Version == version) : null);
        }
    }

    internal sealed class RetentionPolicy : IManagedRetentionPolicy
    {
        public bool ShouldOfferRetentionReview(ManagedVersionInventory inventory, bool updateSucceeded) => updateSucceeded && inventory.HealthyCount > 3;
        public bool ShouldClearRetentionReview(ManagedVersionInventory inventory) => inventory.HealthyCount <= 3;
    }
    internal sealed class ClearLauncherFence : ILauncherMutationFence
    {
        public ValueTask<LauncherMutationProtection> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LauncherMutationProtection(LauncherMutationFenceIssue.None, false, null, null, []));
        }
        public ValueTask<LauncherMutationFenceIssue> RetireLastKnownGoodOwnerAsync(
            ManagedVersionAdmission expectedOwner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(LauncherMutationFenceIssue.None);
        }
    }
}

internal static class ManagedMutationTestFactory
{
    internal static ManagedMutationCoordinator Create(ManagedAppVersion currentAppVersion, string root,
        CoordinationStateStore state, ManagedMutationTestSupport.IBoundPackageSelection selection,
        IManagedVersionRepository repository, ILauncherMutationFence? fence = null)
    {
        selection.Source = state.State.UpdateSource;
        return new(root, state.StatePath, state, repository, fence ?? new ManagedMutationTestSupport.ClearLauncherFence(),
            selection, new ManagedMutationTestSupport.RetentionPolicy());
    }
}
