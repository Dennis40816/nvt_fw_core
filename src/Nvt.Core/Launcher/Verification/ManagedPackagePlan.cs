// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Verification;

// Closed verification facts and a live ZIP reader for internal extraction consumers.
// Disposal releases the reader but leaves the caller's held package stream open.
internal sealed class ManagedPackagePlan : IDisposable
{
    private readonly ZipArchive archive;
    private readonly ReadOnlyDictionary<string, ZipArchiveEntry> entries;
    private readonly byte[] manifestBytes;
    private readonly byte[] checksumBytes;
    private readonly PackageVerificationLimits limits;
    private bool disposed;

    internal ManagedPackagePlan(ZipArchive archive, PackageManifest manifest,
        Dictionary<string, ZipArchiveEntry> entries, byte[] manifestBytes, byte[] checksumBytes,
        int implicitDirectoryCount, long expandedBytes, PackageVerificationLimits limits,
        VerifiedUpdateCandidate candidate, ManagedLauncherIdentity? launcher)
    {
        this.archive = archive;
        Manifest = manifest;
        this.entries = new ReadOnlyDictionary<string, ZipArchiveEntry>(entries);
        this.manifestBytes = manifestBytes;
        this.checksumBytes = checksumBytes;
        this.limits = limits;
        ImplicitDirectoryCount = implicitDirectoryCount;
        ExpandedBytes = expandedBytes;
        Candidate = candidate;
        LauncherIdentity = launcher;
        MemberPaths = Array.AsReadOnly(entries.Keys.ToArray());
    }

    internal PackageManifest Manifest { get; }
    internal IReadOnlyCollection<string> MemberPaths { get; }
    internal ReadOnlyMemory<byte> ManifestBytes => manifestBytes;
    internal ReadOnlyMemory<byte> ChecksumBytes => checksumBytes;
    internal VerifiedUpdateCandidate Candidate { get; }
    internal ManagedLauncherIdentity? LauncherIdentity { get; }
    internal bool HasSupportedManagedLauncher => LauncherIdentity is not null;
    internal int FileCount => entries.Count;
    internal int ImplicitDirectoryCount { get; }
    internal long ExpandedBytes { get; }
    internal long MaximumInstalledFiles => (long)limits.MaximumArchiveEntries + 1;
    internal long InstalledFileCount => (long)FileCount + 1;
    internal int MaximumInstalledDirectories => limits.MaximumInstalledDirectories;

    internal bool AdmitsAdmissionLength(long length) => length >= 1 && length <= limits.MaximumAdmissionBytes;
    internal bool AdmitsInstalledFileCount(long count) => count >= 0 && count <= MaximumInstalledFiles;

    internal async ValueTask ExtractAsync(Func<string, Stream> createDestination,
        CancellationToken cancellationToken, Action<long, long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(createDestination);
        ObjectDisposedException.ThrowIf(disposed, this);
        var budget = new ExpandedByteBudget(limits.MaximumExpandedBytes);
        long extractedBytes = 0;
        progress?.Invoke(0, ExpandedBytes);
        var expectedFiles = Manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        foreach ((string relativePath, ZipArchiveEntry entry) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long expectedLength;
            string expectedHash;
            if (relativePath.Equals(ManagedPackageVerifier.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                expectedLength = manifestBytes.LongLength;
                expectedHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            }
            else if (relativePath.Equals(ManagedPackageVerifier.ChecksumFileName, StringComparison.OrdinalIgnoreCase))
            {
                expectedLength = checksumBytes.LongLength;
                expectedHash = Convert.ToHexString(SHA256.HashData(checksumBytes)).ToLowerInvariant();
            }
            else if (expectedFiles.TryGetValue(relativePath, out PackageFile? expected))
            {
                expectedLength = expected.Size;
                expectedHash = expected.Sha256;
            }
            else
            {
                throw new InvalidDataException("Archive entry is absent from the admitted manifest.");
            }
            await using Stream source = entry.Open();
            await using Stream destination = createDestination(relativePath);
            BoundedArchiveReadResult extracted = await BoundedArchiveReader.CopyAndHashAsync(
                source, expectedLength, budget, destination, cancellationToken, bytes =>
                {
                    extractedBytes = checked(extractedBytes + bytes);
                    progress?.Invoke(extractedBytes, ExpandedBytes);
                }).ConfigureAwait(false);
            if (!extracted.IsSuccess || !string.Equals(extracted.Sha256, expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Archive content changed after admission.");
            }
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            archive.Dispose();
        }
    }
}

internal sealed record ManagedPackagePlanResult(ManagedPackagePlan? Plan, ManagedVersionInstallIssue Issue)
{
    internal bool IsSuccess => Plan is not null && Issue == ManagedVersionInstallIssue.None;
}
