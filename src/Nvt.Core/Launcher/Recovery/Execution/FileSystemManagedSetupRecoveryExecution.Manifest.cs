// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedSetupRecoveryExecution
{
    private async ValueTask<bool> VerifyRetainedManifestPrefixAsync(
        WindowsStablePathCustody custody,
        WindowsStableOwnedTreeSnapshot snapshot,
        ManagedSetupRecoveryTransaction transaction,
        ManagedVersionAdmission admission,
        ManagedAppVersion version,
        string authorityManifest,
        CancellationToken cancellationToken)
    {
        byte[] manifestBytes;
        using (FileStream stream = custody.OpenReadOnlyFile(authorityManifest))
        {
            stream.Position = 0;
            if (stream.Length < 1 || stream.Length > _limits.MaximumManifestBytes)
            {
                return false;
            }
            manifestBytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
        }
        if (!string.Equals(
                LowerHex(SHA256.HashData(manifestBytes)),
                admission.ReleaseManifestSha256,
                StringComparison.Ordinal))
        {
            return false;
        }
        if (!_packageRepository.InstalledProof.TryReadManifest(
                manifestBytes,
                admission,
                out PackageManifest? manifest))
        {
            return false;
        }

        string versionRoot = string.Join(
            '/',
            FileSystemManagedVersionRepository.VersionsDirectoryName,
            version.ToString());
        if (!HasExactManifestPrefix(snapshot, manifest!, version, authorityManifest, versionRoot)) return false;
        bool verified = await VerifySurvivingPrefixFilesAsync(
            custody,
            snapshot,
            transaction,
            admission,
            manifest!,
            manifestBytes,
            versionRoot,
            cancellationToken).ConfigureAwait(false);
        return verified && custody.RevalidateClosedTree();
    }


    private bool HasExactManifestPrefix(WindowsStableOwnedTreeSnapshot snapshot, PackageManifest manifest,
        ManagedAppVersion version, string authorityManifest, string versionRoot)
    {
        var expectedFiles = new HashSet<string>(RepositoryPathSafety.PathComparer)
        {
            _layout.DistributionLauncherFileName,
            _layout.BootstrapFileName,
            _layout.SeedFileName,
            authorityManifest,
            string.Concat(versionRoot, "/SHA256SUMS.txt"),
            string.Concat(versionRoot, "/", FileSystemManagedVersionRepository.AdmissionFileName),
        };
        foreach (PackageFile file in manifest.Files!)
        {
            if (!ContractValidation.IsSafeRelativePath(file.Path) || !_productPolicy.IsSafeRelativePayloadPath(file.Path))
            {
                return false;
            }
            _ = expectedFiles.Add(string.Concat(versionRoot, "/", file.Path.Replace('\\', '/')));
        }
        var expectedDirectories = new HashSet<string>(RepositoryPathSafety.PathComparer)
        {
            string.Empty,
            FileSystemManagedVersionRepository.VersionsDirectoryName,
            versionRoot,
        };
        foreach (string file in expectedFiles.Where(path => path.StartsWith(
                     string.Concat(versionRoot, "/"),
                     StringComparison.OrdinalIgnoreCase)))
        {
            string? parent = Path.GetDirectoryName(file.Replace('/', Path.DirectorySeparatorChar));
            while (!string.IsNullOrEmpty(parent))
            {
                string normalized = parent.Replace('\\', '/');
                _ = expectedDirectories.Add(normalized);
                if (RepositoryPathSafety.PathComparer.Equals(normalized, versionRoot))
                {
                    break;
                }
                parent = Path.GetDirectoryName(parent);
            }
        }
        string[] expectedSteps = BuildTreeSteps(
            MatchSnapshotSpelling(expectedFiles, snapshot.Files.Keys),
            MatchSnapshotSpelling(expectedDirectories, snapshot.Directories.Keys), version);
        string[] actualSteps = SplitLines(TreeSteps(snapshot, version));
        return actualSteps.Length <= expectedSteps.Length &&
            expectedSteps.Skip(expectedSteps.Length - actualSteps.Length)
                .SequenceEqual(actualSteps, RepositoryPathSafety.PathComparer);
    }

    private static IEnumerable<string> MatchSnapshotSpelling(
        IEnumerable<string> expectedPaths, IEnumerable<string> actualPaths)
    {
        var spelling = actualPaths.ToDictionary(static path => path, RepositoryPathSafety.PathComparer);
        return expectedPaths.Select(path => spelling.TryGetValue(path, out string? actual) ? actual : path);
    }

    private async ValueTask<bool> VerifySurvivingPrefixFilesAsync(
        WindowsStablePathCustody custody,
        WindowsStableOwnedTreeSnapshot snapshot,
        ManagedSetupRecoveryTransaction transaction,
        ManagedVersionAdmission admission,
        PackageManifest manifest,
        byte[] manifestBytes,
        string versionRoot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Files.ContainsKey(
                _layout.DistributionLauncherFileName) &&
            !await MatchesHeldAsync(
                custody,
                _layout.DistributionLauncherFileName,
                transaction.Payload.LauncherSize,
                transaction.Payload.LauncherSha256,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        if (snapshot.Files.ContainsKey(_layout.BootstrapFileName) &&
            !await MatchesHeldAsync(
                custody,
                _layout.BootstrapFileName,
                transaction.Payload.BootstrapSize,
                transaction.Payload.BootstrapSha256,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        string seedName = _layout.SeedFileName;
        if (snapshot.Files.ContainsKey(seedName))
        {
            VersionManagerStateLoadResult seed = await _seedStoreFactory(Path.Combine(custody.RootPath, seedName)).LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!seed.IsSuccess || seed.State is null ||
                !ManagedVersionSeedPolicy.IsCanonicalFirstRunSeed(seed.State) ||
                seed.State.Admissions is not [var only] || only != admission)
            {
                return false;
            }
        }
        foreach (PackageFile file in manifest.Files!)
        {
            string relative = string.Concat(versionRoot, "/", file.Path.Replace('\\', '/'));
            if (snapshot.Files.ContainsKey(relative) &&
                !await MatchesHeldAsync(
                    custody,
                    relative,
                    file.Size,
                    file.Sha256,
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }
        string admissionPath = string.Concat(
            versionRoot,
            "/",
            FileSystemManagedVersionRepository.AdmissionFileName);
        if (snapshot.Files.ContainsKey(admissionPath) &&
            await _packageRepository.ReadAdmissionAsync(
                Path.Combine(custody.RootPath, versionRoot.Replace('/', Path.DirectorySeparatorChar)),
                cancellationToken).ConfigureAwait(false) != admission)
        {
            return false;
        }
        string checksumPath = string.Concat(versionRoot, "/SHA256SUMS.txt");
        return !snapshot.Files.ContainsKey(checksumPath) ||
            await VerifyHeldChecksumAsync(
                custody,
                checksumPath,
                manifestBytes,
                manifest.Files,
                cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> MatchesHeldAsync(
        WindowsStablePathCustody custody,
        string relative,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        using FileStream stream = custody.OpenReadOnlyFile(relative);
        stream.Position = 0;
        return stream.Length == expectedSize && string.Equals(
            LowerHex(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)),
            expectedSha256,
            StringComparison.Ordinal);
    }

    private async ValueTask<bool> VerifyHeldChecksumAsync(
        WindowsStablePathCustody custody,
        string relative,
        byte[] manifestBytes,
        IReadOnlyList<PackageFile> files,
        CancellationToken cancellationToken)
    {
        using FileStream stream = custody.OpenReadOnlyFile(relative);
        stream.Position = 0;
        if (stream.Length < 1 || stream.Length > _limits.MaximumManifestBytes)
        {
            return false;
        }
        byte[] bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return _verifier.VerifyChecksumDocument(bytes, manifestBytes, files);
    }

}
