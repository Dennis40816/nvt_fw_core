// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Integration;

internal static partial class VersionManagementExperienceTestSupport
{
    internal sealed class SequenceRegistrySource(params UpdateSourceRegistryLoadResult[] results)
        : IUpdateSourceRegistry
    {
        private int _next;

        internal int LoadCount => _next;

        public ValueTask<UpdateSourceRegistryLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            int index = Math.Min(Interlocked.Increment(ref _next) - 1, results.Length - 1);
            return ValueTask.FromResult(results[index]);
        }
    }

    internal sealed class PathCatalogSource(
        params (string Path, UpdateCatalogLoadResult Outcome)[] results) : IRootCatalogSourceTestDouble
    {
        private readonly Dictionary<string, UpdateCatalogLoadResult> _results =
            results.ToDictionary(entry => entry.Path, entry => entry.Outcome, StringComparer.Ordinal);

        internal List<string> LoadedRoots { get; } = [];

        public ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            LoadedRoots.Add(sourceRoot);
            return ValueTask.FromResult(_results.TryGetValue(sourceRoot, out UpdateCatalogLoadResult? result)
                ? WithDefaultContentIdentity(result)
                : new UpdateCatalogLoadResult(null, UpdateCatalogLoadIssue.SourceMissing));
        }
    }

    internal sealed class SequencePathCatalogSource(
        string path,
        params UpdateCatalogSnapshot[] snapshots) : IRootCatalogSourceTestDouble
    {
        private int _next;

        public ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            Assert.Equal(path, sourceRoot);
            int index = Math.Min(Interlocked.Increment(ref _next) - 1, snapshots.Length - 1);
            return ValueTask.FromResult(new UpdateCatalogLoadResult(
                snapshots[index],
                UpdateCatalogLoadIssue.None,
                DefaultCatalogContentIdentity));
        }
    }

    internal sealed class CancelFirstCatalogSource(UpdateCatalogSnapshot snapshot)
        : IRootCatalogSourceTestDouble
    {
        private int _loadCount;

        internal TaskCompletionSource FirstLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _loadCount) == 1)
            {
                _ = FirstLoadStarted.TrySetResult();
                await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(cancellationToken);
            }
            return new(snapshot, UpdateCatalogLoadIssue.None, DefaultCatalogContentIdentity);
        }
    }

    private static UpdateCatalogContentIdentity DefaultCatalogContentIdentity =>
        new(1, CatalogContentDigest);

    private static UpdateCatalogLoadResult WithDefaultContentIdentity(UpdateCatalogLoadResult result)
    {
        return result.IsSuccess && result.ContentIdentity is null
            ? result with { ContentIdentity = DefaultCatalogContentIdentity }
            : result;
    }

    internal sealed class CountingRepository(ManagedAppVersion? mismatchedVersion = null)
        : IManagedVersionRepository
    {
        private readonly HealthyRepository _inner = new();

        internal List<string> VerifiedRoots { get; } = [];

        internal List<ManagedAppVersion> VerifiedVersions { get; } = [];

        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            VerifiedRoots.Add(sourceRoot);
            VerifiedVersions.Add(package.Version);
            return package.Version == mismatchedVersion
                ? ValueTask.FromResult(new ManagedPackageVerificationResult(
                    Candidate: null,
                    ManagedVersionInstallIssue.PackageMismatch))
                : _inner.VerifyPackageAsync(sourceRoot, package, cancellationToken);
        }

        public ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot,
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            return _inner.InstallAsync(managedRoot, sourceRoot, package, cancellationToken);
        }

        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
            string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions,
            ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion,
            ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken)
        {
            return _inner.InventoryAsync(
                managedRoot,
                admissions,
                activeVersion,
                lastKnownGoodVersion,
                failedActivationVersion,
                cancellationToken);
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken)
        {
            return _inner.DeleteAsync(managedRoot, admission, activeVersion, cancellationToken);
        }
    }

    internal sealed class BlockingFirstVerificationRepository : IManagedVersionRepository
    {
        private readonly HealthyRepository _inner = new();
        private int _verifyCount;

        internal TaskCompletionSource FirstVerificationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseFirstVerification { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _verifyCount) == 1)
            {
                _ = FirstVerificationStarted.TrySetResult();
                await ReleaseFirstVerification.Task.WaitAsync(cancellationToken);
            }
            return await _inner.VerifyPackageAsync(sourceRoot, package, cancellationToken);
        }

        public ValueTask<ManagedVersionInstallResult> InstallAsync(
            string managedRoot,
            string sourceRoot,
            UpdateCatalogVersionSnapshot package,
            CancellationToken cancellationToken)
        {
            return _inner.InstallAsync(managedRoot, sourceRoot, package, cancellationToken);
        }

        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
            string managedRoot,
            IReadOnlyList<ManagedVersionAdmission> admissions,
            ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion,
            ManagedAppVersion? failedActivationVersion,
            CancellationToken cancellationToken)
        {
            return _inner.InventoryAsync(
                managedRoot,
                admissions,
                activeVersion,
                lastKnownGoodVersion,
                failedActivationVersion,
                cancellationToken);
        }

        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            ManagedAppVersion? activeVersion,
            CancellationToken cancellationToken)
        {
            return _inner.DeleteAsync(
                managedRoot,
                admission,
                activeVersion,
                cancellationToken);
        }
    }

    internal sealed class DenyingMutationFence : ILauncherMutationFence
    {
        public ValueTask<LauncherMutationProtection> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LauncherMutationProtection(
                LauncherMutationFenceIssue.None,
                HasPendingActivation: true,
                ActiveOwner: null,
                LastKnownGoodOwner: null,
                PendingOwners: []));
        }

        public ValueTask<LauncherMutationFenceIssue> RetireLastKnownGoodOwnerAsync(
            ManagedVersionAdmission expectedOwner,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(expectedOwner);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(LauncherMutationFenceIssue.Invalid);
        }
    }

}
