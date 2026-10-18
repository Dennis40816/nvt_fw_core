// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Nvt.Core.Files;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Verification;

/// <summary>Verifies a bounded compressed package and its complete closed payload.</summary>
/// <remarks>The caller holds stable read custody throughout the operation. Strict manifest schema and product payload policy remain in the mandatory adapter.</remarks>
internal sealed class ManagedPackageVerifier
{
    internal const string ManifestFileName = "RELEASE-MANIFEST.json";
    internal const string ChecksumFileName = "SHA256SUMS.txt";
    internal const string AdmissionFileName = ".managed-admission.v1.json";

    private readonly ProductDescriptor descriptor;
    private readonly IProductPackagePolicy productPolicy;
    private readonly PackageVerificationLimits limits;

    /// <summary>Creates a verifier using explicit product names, strict policy and positive ceilings.</summary>
    /// <param name="descriptor">The product's runtime descriptor.</param>
    /// <param name="productPolicy">The mandatory strict manifest and payload adapter.</param>
    /// <param name="limits">The product's explicit verification ceilings.</param>
    public ManagedPackageVerifier(
        ProductDescriptor descriptor,
        IProductPackagePolicy productPolicy,
        PackageVerificationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(productPolicy);
        ArgumentNullException.ThrowIfNull(limits);
        // Recheck record copies and object initializers at the consuming boundary.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumArchiveEntries, nameof(limits.MaximumArchiveEntries));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumPackageBytes, nameof(limits.MaximumPackageBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumExpandedBytes, nameof(limits.MaximumExpandedBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumManifestBytes, nameof(limits.MaximumManifestBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumAdmissionBytes, nameof(limits.MaximumAdmissionBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumExecutableBytes, nameof(limits.MaximumExecutableBytes));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limits.MaximumExecutableBytes,
            ManagedImmutableBootstrapIdentity.MaximumExecutableBytes, nameof(limits.MaximumExecutableBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumInstalledDirectories, nameof(limits.MaximumInstalledDirectories));
        this.descriptor = descriptor;
        this.productPolicy = productPolicy;
        this.limits = limits;
    }

    /// <summary>Checks the complete package identity, exact manifest bytes, inventory, checksums and actual expanded content.</summary>
    /// <param name="package">A borrowed readable, seekable stream held stable by the caller; it remains open.</param>
    /// <param name="candidate">The catalog-admitted package and manifest identity.</param>
    /// <param name="cancellationToken">Cancellation for all bounded reads.</param>
    /// <returns>A complete verified candidate or the frozen terminal failure category.</returns>
    /// <remarks>Unsupported streams and I/O failures return PackageUnavailable. Cancellation and adapter programming exceptions propagate.</remarks>
    public async ValueTask<ManagedPackageVerificationResult> VerifyAsync(
        Stream package,
        UpdateCatalogVersionSnapshot candidate,
        CancellationToken cancellationToken)
    {
        ManagedPackagePlanResult result = await CreatePlanAsync(package, candidate, cancellationToken).ConfigureAwait(false);
        using ManagedPackagePlan? plan = result.Plan;
        return result.IsSuccess
            ? new(plan!.Candidate, ManagedVersionInstallIssue.None)
            {
                HasSupportedManagedLauncher = plan.HasSupportedManagedLauncher,
            }
            : new(null, result.Issue);
    }

    // A successful plan owns the ZIP reader, never the caller's held package stream.
    // Installation propagates read failures so its own catch keeps the frozen install categories.
    internal async ValueTask<ManagedPackagePlanResult> CreatePlanAsync(
        Stream package,
        UpdateCatalogVersionSnapshot candidate,
        CancellationToken cancellationToken,
        bool propagateReadFailures = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(candidate);
        ZipArchive? archive = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.CanRead || !package.CanSeek)
            {
                return Failure(ManagedVersionInstallIssue.PackageUnavailable);
            }
            long observedLength = package.Length;
            if (candidate.PackageSize > limits.MaximumPackageBytes || observedLength != candidate.PackageSize)
            {
                return Failure(ManagedVersionInstallIssue.PackageUnavailable);
            }

            package.Position = 0;
            BoundedReadResult read = await BoundedFileReader.ReadAndHashAsync(
                package, observedLength, FileCaptureMode.IdentityOnly, cancellationToken).ConfigureAwait(false);
            if (read.Sha256 is null || !string.Equals(Convert.ToHexString(read.Sha256).ToLowerInvariant(),
                    candidate.PackageSha256, StringComparison.Ordinal))
            {
                return Failure(ManagedVersionInstallIssue.PackageMismatch);
            }
            package.Position = 0;
            archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            ManagedPackagePlanResult result = await CreateArchivePlanAsync(archive, candidate, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                archive = null;
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!propagateReadFailures &&
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Failure(ManagedVersionInstallIssue.PackageUnavailable);
        }
        finally
        {
            if (archive is not null)
            {
                await archive.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<ManagedPackagePlanResult> CreateArchivePlanAsync(
        ZipArchive archive,
        UpdateCatalogVersionSnapshot candidate,
        CancellationToken cancellationToken)
    {
        if (ScanArchive(archive, candidate, cancellationToken) is not { } inventory)
        {
            return Failure(ManagedVersionInstallIssue.UnsafeArchive);
        }
        (Dictionary<string, ZipArchiveEntry> entries, HashSet<string> directories, long expandedBytes) = inventory;
        var budget = new ExpandedByteBudget(limits.MaximumExpandedBytes);
        (ManagedPackagePlanResult? failure, byte[] manifestBytes, string? manifestSha256) =
            await ReadMetadataAsync(entries, ManifestFileName, budget, cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }
        if (!string.Equals(manifestSha256, candidate.ReleaseManifestSha256, StringComparison.Ordinal))
        {
            return Failure(ManagedVersionInstallIssue.InvalidPayload);
        }
        // Preserve the source's document-before-policy order and pass exact bytes.
        if (!productPolicy.TryReadManifest(manifestBytes, candidate.Version,
                Array.AsReadOnly(entries.Keys.ToArray()), out PackageManifest? projected) ||
            !TryNormalizeManifest(projected, candidate, entries, out PackageManifest? manifest,
                out ManagedLauncherIdentity? launcher))
        {
            return Failure(ManagedVersionInstallIssue.InvalidPayload);
        }
        (failure, byte[] checksumBytes, _) =
            await ReadMetadataAsync(entries, ChecksumFileName, budget, cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }
        if (!VerifyChecksumDocument(checksumBytes, manifestBytes, manifest!.Files))
        {
            return Failure(ManagedVersionInstallIssue.InvalidPayload);
        }
        failure = await VerifyPayloadFilesAsync(entries, manifest.Files, budget, cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }
        return new(new ManagedPackagePlan(archive, manifest, entries, manifestBytes, checksumBytes,
            directories.Count, expandedBytes, limits,
            new(candidate.Version, candidate.Identity, candidate.ReleaseNotes), launcher), ManagedVersionInstallIssue.None);
    }

    // Returns null when the archive is unsafe: wrong entry count, links, paths outside the root, or too
    // many directories or expanded bytes.
    private ArchiveInventory? ScanArchive(ZipArchive archive, UpdateCatalogVersionSnapshot candidate,
        CancellationToken cancellationToken)
    {
        if (archive.Entries.Count == 0 || archive.Entries.Count > limits.MaximumArchiveEntries)
        {
            return null;
        }

        string prefix = descriptor.GetArchiveRootName(candidate.Version) + "/";
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLink(entry) || !entry.FullName.StartsWith(prefix, StringComparison.Ordinal) ||
                entry.FullName.Contains('\\', StringComparison.Ordinal))
            {
                return null;
            }
            string relativePath = entry.FullName[prefix.Length..];
            if (entry.FullName.EndsWith('/'))
            {
                if (entry.Length != 0 || (relativePath.Length > 0 && !IsSafePath(relativePath.TrimEnd('/'))))
                {
                    return null;
                }
                continue;
            }
            if (!IsSafePath(relativePath) || !entries.TryAdd(relativePath, entry))
            {
                return null;
            }
            string[] components = relativePath.Split('/');
            for (int count = 1; count < components.Length; count++)
            {
                _ = directories.Add(string.Join('/', components.AsSpan(0, count).ToArray()));
                if (directories.Count > limits.MaximumInstalledDirectories)
                {
                    return null;
                }
            }
            // Subtract before adding so malicious ZIP64 declarations cannot overflow.
            if (entry.Length < 0 || entry.Length > limits.MaximumExpandedBytes - expandedBytes)
            {
                return null;
            }
            expandedBytes += entry.Length;
        }
        return new(entries, directories, expandedBytes);
    }

    // Reads one bounded metadata document. Returns the failure, or null with the exact bytes and their hash.
    private async ValueTask<(ManagedPackagePlanResult? Failure, byte[] Bytes, string? Sha256)> ReadMetadataAsync(
        Dictionary<string, ZipArchiveEntry> entries, string name, ExpandedByteBudget budget,
        CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(name, out ZipArchiveEntry? entry) ||
            entry.Length < 1 || entry.Length > limits.MaximumManifestBytes)
        {
            return (Failure(ManagedVersionInstallIssue.InvalidPayload), [], null);
        }
        using var buffer = new MemoryStream(checked((int)entry.Length));
        BoundedArchiveReadResult read;
        await using (Stream input = entry.Open())
        {
            read = await BoundedArchiveReader.ReadAtMostAndHashAsync(input,
                limits.MaximumManifestBytes, budget, buffer, cancellationToken).ConfigureAwait(false);
        }
        if (!read.IsSuccess)
        {
            return (ReadFailure(read), [], null);
        }
        return read.Length < 1 ? (Failure(ManagedVersionInstallIssue.InvalidPayload), [], null)
            : (null, buffer.ToArray(), read.Sha256);
    }

    // Hashes every declared payload file against the manifest. Returns the failure, or null when all match.
    private static async ValueTask<ManagedPackagePlanResult?> VerifyPayloadFilesAsync(
        Dictionary<string, ZipArchiveEntry> entries, IReadOnlyList<PackageFile> files, ExpandedByteBudget budget,
        CancellationToken cancellationToken)
    {
        foreach (PackageFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(file.Path, out ZipArchiveEntry? entry) || entry.Length != file.Size)
            {
                return Failure(ManagedVersionInstallIssue.InvalidPayload);
            }
            await using Stream input = entry.Open();
            BoundedArchiveReadResult content = await BoundedArchiveReader.ReadAndHashAsync(
                input, file.Size, budget, cancellationToken).ConfigureAwait(false);
            if (!content.IsSuccess)
            {
                return ReadFailure(content);
            }
            if (!string.Equals(content.Sha256, file.Sha256, StringComparison.Ordinal))
            {
                return Failure(ManagedVersionInstallIssue.InvalidPayload);
            }
        }
        return null;
    }

    private sealed record ArchiveInventory(
        Dictionary<string, ZipArchiveEntry> Entries, HashSet<string> Directories, long ExpandedBytes);

    private bool TryNormalizeManifest(
        PackageManifest? projected,
        UpdateCatalogVersionSnapshot candidate,
        Dictionary<string, ZipArchiveEntry> entries,
        out PackageManifest? manifest,
        out ManagedLauncherIdentity? launcher)
    {
        manifest = null;
        launcher = null;
        if (projected is null || projected.Files is null ||
            !string.Equals(projected.ProductId, descriptor.ProductId, StringComparison.Ordinal) ||
            !string.Equals(projected.RuntimeIdentifier, descriptor.RuntimeIdentifier, StringComparison.Ordinal) ||
            projected.Version != candidate.Version || projected.Files.Count < 0 ||
            projected.Files.Count > limits.MaximumArchiveEntries - 2)
        {
            return false;
        }
        var files = new List<PackageFile>(projected.Files.Count);
        foreach (PackageFile? file in projected.Files)
        {
            if (files.Count >= limits.MaximumArchiveEntries - 2 || file is null)
            {
                return false;
            }
            files.Add(file);
        }
        // The source validates the launcher contract before normalized payload paths.
        if (projected.Launcher is { } declaration)
        {
            try
            {
                launcher = ManagedLauncherIdentity.Create(descriptor, limits.MaximumExecutableBytes,
                    candidate.Version, candidate.Identity, candidate.ReleaseManifestSha256,
                    declaration.LauncherVersion, declaration.ProtocolVersion, declaration.ExecutableRelativePath,
                    declaration.Size, declaration.Sha256);
            }
            catch (ArgumentException)
            {
                return false;
            }
            PackageFile? file = files.Find(file => string.Equals(file.Path, declaration.ExecutableRelativePath, StringComparison.Ordinal));
            if (file is null || file.Size != declaration.Size ||
                !string.Equals(file.Sha256, declaration.Sha256, StringComparison.Ordinal))
            {
                return false;
            }
        }
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredBytes = 0;
        foreach (PackageFile file in files)
        {
            if (!IsSafePath(file.Path) ||
                file.Path.Equals(AdmissionFileName, StringComparison.OrdinalIgnoreCase) ||
                file.Path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
                file.Path.Equals(ChecksumFileName, StringComparison.OrdinalIgnoreCase) ||
                file.Size <= 0 || file.Size > limits.MaximumExpandedBytes - declaredBytes ||
                !ContractValidation.IsLowerSha256(file.Sha256) || !declared.Add(file.Path))
            {
                return false;
            }
            declaredBytes += file.Size;
        }
        _ = declared.Add(ManifestFileName);
        _ = declared.Add(ChecksumFileName);
        if (!declared.SetEquals(entries.Keys))
        {
            return false;
        }
        manifest = projected with { Files = files.AsReadOnly() };
        return true;
    }

    internal bool VerifyChecksumDocument(byte[] checksumBytes, byte[] manifestBytes, IReadOnlyList<PackageFile> files)
    {
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(checksumBytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        if (text.Contains('\r') && !text.Contains("\r\n", StringComparison.Ordinal))
        {
            return false;
        }
        var expected = files.ToDictionary(file => file.Path, file => file.Sha256, StringComparer.Ordinal);
        expected.Add(ManifestFileName, Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant());
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
            {
                return false;
            }
            string hash = line[..64];
            string path = line[66..];
            if (!ContractValidation.IsLowerSha256(hash) || !IsSafePath(path) || !actual.TryAdd(path, hash))
            {
                return false;
            }
        }
        return expected.Count == actual.Count && expected.All(pair =>
            actual.TryGetValue(pair.Key, out string? hash) && string.Equals(hash, pair.Value, StringComparison.Ordinal));
    }

    private bool IsSafePath(string? path) => ContractValidation.IsSafeRelativePath(path) && productPolicy.IsSafeRelativePayloadPath(path!);

    private static bool IsLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
        ((entry.ExternalAttributes & 0xFFFF) & (int)FileAttributes.ReparsePoint) != 0;

    private static ManagedPackagePlanResult ReadFailure(BoundedArchiveReadResult read) =>
        Failure(read.Issue == BoundedArchiveReadIssue.AggregateLengthExceeded
            ? ManagedVersionInstallIssue.UnsafeArchive : ManagedVersionInstallIssue.InvalidPayload);

    private static ManagedPackagePlanResult Failure(ManagedVersionInstallIssue issue) => new(null, issue);
}
