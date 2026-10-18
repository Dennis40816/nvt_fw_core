// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Files;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>
/// Reuses the one complete first-install root proof for Setup completion and exact recovery.
/// </summary>
internal sealed class ManagedSetupClosedRootVerifier(IManagedVersionRepository repository,
    ManagedSetupLayout layout, Func<string, IVersionManagerStateStore> seedStoreFactory)
{
    private readonly IManagedVersionRepository _repository = repository ??
        throw new ArgumentNullException(nameof(repository));

    internal ValueTask<bool> VerifyAsync(
        string root,
        ManagedDistributionPayloadIdentity payload,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(admission);
        return VerifyCoreAsync(
            root,
            payload.LauncherSize,
            payload.LauncherSha256,
            payload.Bootstrap.FileName,
            payload.Bootstrap.Length,
            payload.Bootstrap.Sha256,
            admission,
            cancellationToken);
    }

    internal ValueTask<bool> VerifyAsync(
        string root,
        ManagedSetupRecoveryPayloadIdentity payload,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(admission);
        return VerifyCoreAsync(
            root,
            payload.LauncherSize,
            payload.LauncherSha256,
            payload.BootstrapFileName,
            payload.BootstrapSize,
            payload.BootstrapSha256,
            admission,
            cancellationToken);
    }

    private async ValueTask<bool> VerifyCoreAsync(
        string root,
        long launcherSize,
        string launcherSha256,
        string bootstrapFileName,
        long bootstrapSize,
        string bootstrapSha256,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        if (!await VerifyPayloadAndSeedAsync(
                layout, seedStoreFactory, root,
                launcherSize,
                launcherSha256,
                bootstrapFileName,
                bootstrapSize,
                bootstrapSha256,
                admission,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        ManagedVersionInventoryReadResult inventory = await _repository.InventoryAsync(
            root,
            [admission],
            admission.Version,
            admission.Version,
            failedActivationVersion: null,
            cancellationToken).ConfigureAwait(false);
        ManagedVersionInventory? installed = inventory.Inventory;
        return inventory.IsSuccess && installed is not null &&
            installed.Versions.Count == 1 &&
            installed.HealthyCount == 1 &&
            installed.DamagedCount == 0;
    }

    internal static async ValueTask<bool> VerifyPayloadAndSeedAsync(
        ManagedSetupLayout layout, Func<string, IVersionManagerStateStore> seedStoreFactory,
        string root,
        ManagedDistributionPayloadIdentity payload,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return await VerifyPayloadAndSeedAsync(
            layout, seedStoreFactory, root,
            payload.LauncherSize,
            payload.LauncherSha256,
            payload.Bootstrap.FileName,
            payload.Bootstrap.Length,
            payload.Bootstrap.Sha256,
            admission,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> VerifyPayloadAndSeedAsync(
        ManagedSetupLayout layout, Func<string, IVersionManagerStateStore> seedStoreFactory,
        string root,
        long launcherSize,
        string launcherSha256,
        string bootstrapFileName,
        long bootstrapSize,
        string bootstrapSha256,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        IVersionManagerStateStore seedStore = seedStoreFactory(Path.Combine(root, layout.SeedFileName));
        VersionManagerStateLoadResult seed = await seedStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        VersionManagerState? seedState = seed.State;
        return seed.IsSuccess && seedState is not null &&
            ManagedVersionSeedPolicy.IsCanonicalFirstRunSeed(seedState) &&
            seedState.Admissions is [var only] &&
            only == admission &&
            HasClosedTopLevelInventory(root, layout) &&
            await MatchesAsync(
                Path.Combine(
                    root,
                    layout.DistributionLauncherFileName),
                launcherSize,
                launcherSha256,
                cancellationToken).ConfigureAwait(false) &&
            string.Equals(
                bootstrapFileName,
                layout.BootstrapFileName,
                StringComparison.Ordinal) &&
            await MatchesAsync(
                Path.Combine(
                    root,
                    layout.BootstrapFileName),
                bootstrapSize,
                bootstrapSha256,
                cancellationToken).ConfigureAwait(false);
    }

    private static bool HasClosedTopLevelInventory(string root, ManagedSetupLayout layout)
    {
        string[] expected =
        [
            layout.BootstrapFileName,
            layout.DistributionLauncherFileName,
            layout.SeedFileName,
            FileSystemManagedVersionRepository.VersionsDirectoryName,
        ];
        try
        {
            string[] actual =
            [.. Directory.EnumerateFileSystemEntries(root).Select(
                static path => Path.GetFileName(path) ?? string.Empty)];
            return actual.Order(StringComparer.Ordinal).SequenceEqual(
                    expected.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal) &&
                expected.All(name => !RepositoryPathSafety.IsReparsePoint(Path.Combine(root, name))) &&
                File.Exists(Path.Combine(
                    root,
                    layout.BootstrapFileName)) &&
                File.Exists(Path.Combine(
                    root,
                    layout.DistributionLauncherFileName)) &&
                File.Exists(Path.Combine(
                    root,
                    layout.SeedFileName)) &&
                Directory.Exists(Path.Combine(
                    root,
                    FileSystemManagedVersionRepository.VersionsDirectoryName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static async ValueTask<bool> MatchesAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || RepositoryPathSafety.IsReparsePoint(path))
        {
            return false;
        }
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedSize)
        {
            return false;
        }
        try
        {
            BoundedReadResult read = await BoundedFileReader.ReadAndHashAsync(stream, expectedSize,
                FileCaptureMode.IdentityOnly, cancellationToken).ConfigureAwait(false);
            return string.Equals(Convert.ToHexString(read.Sha256!).ToLowerInvariant(), expectedSha256, StringComparison.Ordinal);
        }
        catch (FileChangedDuringReadException) { return false; }
    }
}
