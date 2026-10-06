// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using System.Text.Json;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;

namespace Nvt.Core.Launcher.Repository;

internal sealed class InstalledPayloadProof(FileSystemManagedVersionRepository repository,
    ProductDescriptor descriptor, IProductPackagePolicy policy, PackageVerificationLimits limits,
    ManagedPackageVerifier verifier)
{
    internal bool TryReadManifest(ReadOnlyMemory<byte> bytes, ManagedVersionAdmission admission,
        out PackageManifest? manifest)
    {
        manifest = null;
        if (!policy.TryReadManifest(bytes, admission.Version, archivePaths: null, out PackageManifest? projected) ||
            projected is null || projected.Files is null || projected.Version != admission.Version ||
            !string.Equals(projected.ProductId, descriptor.ProductId, StringComparison.Ordinal) ||
            !string.Equals(projected.RuntimeIdentifier, descriptor.RuntimeIdentifier, StringComparison.Ordinal) ||
            projected.Files.Count < 0 || projected.Files.Count > limits.MaximumArchiveEntries - 2)
        {
            return false;
        }
        var files = new List<PackageFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (projected.Launcher is { } launcher)
        {
            if (launcher.ProtocolVersion != ManagedLauncherIdentity.SupportedProtocolVersion ||
                !string.Equals(launcher.ExecutableRelativePath, descriptor.LauncherExecutableRelativePath, StringComparison.Ordinal) ||
                launcher.Size <= 0 || launcher.Size > limits.MaximumExecutableBytes ||
                !ContractValidation.IsLowerSha256(launcher.Sha256))
            {
                return false;
            }
            PackageFile? declared = projected.Files.FirstOrDefault(file =>
                string.Equals(file?.Path, launcher.ExecutableRelativePath, StringComparison.Ordinal));
            if (declared is null || declared.Size != launcher.Size ||
                !string.Equals(declared.Sha256, launcher.Sha256, StringComparison.Ordinal))
            {
                return false;
            }
        }
        foreach (PackageFile? file in projected.Files)
        {
            if (files.Count >= limits.MaximumArchiveEntries - 2 || file is null ||
                !ContractValidation.IsSafeRelativePath(file.Path) || !policy.IsSafeRelativePayloadPath(file.Path) ||
                file.Path.Equals(ManagedPackageVerifier.AdmissionFileName, StringComparison.OrdinalIgnoreCase) ||
                file.Size <= 0 || !ContractValidation.IsLowerSha256(file.Sha256) || !paths.Add(file.Path))
            {
                return false;
            }
            files.Add(file);
        }
        manifest = projected with { Files = files.AsReadOnly() };
        return true;
    }

    internal async ValueTask<ManagedVersionDamageReason?> VerifyAsync(string versionRoot,
        ManagedVersionAdmission admission, CancellationToken cancellationToken)
    {
        try
        {
            if (!RepositoryPathSafety.IsSafeOwnedTree(versionRoot))
            {
                return Directory.Exists(versionRoot)
                    ? ManagedVersionDamageReason.UnexpectedPath : ManagedVersionDamageReason.MissingFile;
            }
            if (await repository.ReadAdmissionAsync(versionRoot, cancellationToken).ConfigureAwait(false) != admission)
            {
                return ManagedVersionDamageReason.ManifestMismatch;
            }
            string manifestPath = Path.Combine(versionRoot, ManagedPackageVerifier.ManifestFileName);
            byte[]? manifestBytes = await RepositoryPathSafety.ReadBoundedFileAsync(manifestPath,
                limits.MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
            if (manifestBytes is null)
            {
                return File.Exists(manifestPath)
                    ? ManagedVersionDamageReason.ManifestMismatch : ManagedVersionDamageReason.MissingFile;
            }
            if (!string.Equals(Hash(manifestBytes), admission.ReleaseManifestSha256, StringComparison.Ordinal) ||
                !TryReadManifest(manifestBytes, admission, out PackageManifest? manifest))
            {
                return ManagedVersionDamageReason.ManifestMismatch;
            }
            byte[]? checksumBytes = await RepositoryPathSafety.ReadBoundedFileAsync(
                Path.Combine(versionRoot, ManagedPackageVerifier.ChecksumFileName), limits.MaximumManifestBytes,
                cancellationToken).ConfigureAwait(false);
            if (checksumBytes is null || !verifier.VerifyChecksumDocument(checksumBytes, manifestBytes, manifest!.Files))
            {
                return ManagedVersionDamageReason.ManifestMismatch;
            }
            var budget = new ExpandedByteBudget(limits.MaximumExpandedBytes);
            if (!budget.Consume(manifestBytes.Length) || !budget.Consume(checksumBytes.Length))
            {
                return ManagedVersionDamageReason.ContentMismatch;
            }
            HashSet<string> expected = BuildExpectedFiles(manifest!.Files);
            long verificationTotal = 0;
            foreach (PackageFile file in manifest.Files)
            {
                verificationTotal = checked(verificationTotal + file.Size);
            }
            foreach (PackageFile file in manifest.Files)
            {
                string path = RepositoryPathSafety.ResolvePayloadPath(versionRoot, file.Path);
                if (!File.Exists(path) || RepositoryPathSafety.IsReparsePoint(path))
                {
                    return ManagedVersionDamageReason.MissingFile;
                }
                if (new FileInfo(path).Length != file.Size)
                {
                    return ManagedVersionDamageReason.ContentMismatch;
                }
                BoundedArchiveReadResult result = await BoundedArchiveReader.ReadFileAndHashAsync(path, file.Size,
                    budget, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess || !string.Equals(result.Sha256, file.Sha256, StringComparison.Ordinal))
                {
                    return ManagedVersionDamageReason.ContentMismatch;
                }
            }
            string[] actual = [.. Directory.EnumerateFiles(versionRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(versionRoot, path).Replace('\\', '/'))];
            return actual.Length == expected.Count && actual.All(expected.Contains)
                ? null : ManagedVersionDamageReason.UnexpectedPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return ManagedVersionDamageReason.Unreadable;
        }
    }

    internal static HashSet<string> BuildExpectedFiles(IReadOnlyList<PackageFile> files) =>
        new(files.Select(file => file.Path).Concat(MetadataPaths), StringComparer.OrdinalIgnoreCase);

    private static readonly string[] MetadataPaths = [ManagedPackageVerifier.ManifestFileName,
        ManagedPackageVerifier.ChecksumFileName, ManagedPackageVerifier.AdmissionFileName];

    internal static bool HasExactTopology(WindowsStablePathCustody custody, IReadOnlyList<PackageFile> files)
    {
        if (!custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot))
        {
            return false;
        }
        HashSet<string> expected = BuildExpectedFiles(files);
        if (!expected.SetEquals(snapshot!.Files.Keys))
        {
            return false;
        }
        var directories = new HashSet<string>(WindowsStablePathCustody.PathComparer) { string.Empty };
        foreach (string file in expected)
        {
            int separator = file.LastIndexOf('/');
            while (separator >= 0)
            {
                string directory = file[..separator];
                _ = directories.Add(directory);
                separator = directory.LastIndexOf('/');
            }
        }
        return directories.SetEquals(snapshot.Directories.Keys);
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

}
