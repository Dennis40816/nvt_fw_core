// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Nvt.Core.Files;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

// Bounded byte transport and mechanical checks only. The mandatory codec owns all wire grammar.
internal sealed class ManagedSetupMarkerIO
{
    internal const string StagingPhase = "staging";
    internal const string RootPromotedPhase = "root-promoted";
    internal const string BootstrapLaunchRecordedPhase = "bootstrap-launch-recorded";
    private readonly IManagedSetupMarkerCodec _codec;
    private readonly int _maximumMarkerBytes;
    private readonly int _maximumDescriptorBytes;

    internal ManagedSetupMarkerIO(IManagedSetupMarkerCodec codec, int maximumMarkerBytes, int maximumDescriptorBytes)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMarkerBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDescriptorBytes);
        _maximumMarkerBytes = maximumMarkerBytes;
        _maximumDescriptorBytes = maximumDescriptorBytes;
    }

    internal ReadOnlyMemory<byte> Serialize(ManagedSetupMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ReadOnlyMemory<byte> bytes = _codec.Encode(marker);
        ManagedSetupMarker? decoded = bytes.Length > 0 && bytes.Length <= _maximumMarkerBytes ? DecodeValidated(bytes) : null;
        return decoded is not null && IsMechanicalIdentity(decoded) && ManagedSetupMarker.Equivalent(marker, decoded)
            ? bytes.ToArray() : throw new InvalidDataException("Setup marker violated its canonical schema.");
    }

    internal async ValueTask<ManagedSetupMarker?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanRead || !stream.CanSeek || stream.Length < 1 || stream.Length > _maximumMarkerBytes) return null;
        long length = stream.Length;
        stream.Position = 0;
        BoundedReadResult read;
        try
        {
            read = await BoundedFileReader.ReadAndHashAsync(stream, length, FileCaptureMode.CaptureBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (FileChangedDuringReadException exception) when (exception.ChangeKind == FileChangeKind.ShortRead)
        { throw new EndOfStreamException("Unable to read beyond the end of the stream."); }
        catch (FileChangedDuringReadException) { return null; }
        ManagedSetupMarker? marker = DecodeValidated(read.Bytes!);
        return marker is not null && IsMechanicalIdentity(marker)
            ? marker with { OwnedPaths = Array.AsReadOnly(marker.OwnedPaths.ToArray()) } : null;
    }

    private ManagedSetupMarker? DecodeValidated(ReadOnlyMemory<byte> bytes)
    {
        try { return _codec.Decode(bytes); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException)
        { return null; }
    }

    private bool IsMechanicalIdentity(ManagedSetupMarker marker)
    {
        ManagedSetupRecoveryPayloadIdentity? payload = marker.Payload;
        ManagedSetupRecoveryCandidateIdentity? candidate = marker.Candidate;
        return marker.TransactionId is { Length: 32 } && marker.TransactionId.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
            RepositoryPathSafety.TryNormalizeExactAbsolutePath(marker.ManagedRootIdentity, out _) &&
            RepositoryPathSafety.TryNormalizeExactAbsolutePath(marker.StatePathIdentity, out _) &&
            marker.Phase is StagingPhase or RootPromotedPhase or BootstrapLaunchRecordedPhase &&
            marker.OwnedPaths is not null && marker.OwnedPaths.All(ContractValidation.IsSafeRelativePath) &&
            payload is not null && payload.LauncherSize > 0 && ContractValidation.IsLowerSha256(payload.LauncherSha256) &&
            payload.DescriptorSize > 0 && payload.DescriptorSize <= _maximumDescriptorBytes && ContractValidation.IsLowerSha256(payload.DescriptorSha256) &&
            ContractValidation.IsSafeFileName(payload.BootstrapFileName) && payload.BootstrapSize is > 0 and <= ManagedImmutableBootstrapIdentity.MaximumExecutableBytes &&
            ContractValidation.IsLowerSha256(payload.BootstrapSha256) && candidate is not null && candidate.RegistryRevision > 0 &&
            ContractValidation.IsLowerSha256(candidate.RegistryDigest) && candidate.CatalogSchemaVersion > 0 &&
            ManagedAppVersion.TryParse(candidate.CatalogLatestVersion, out _) && ContractValidation.IsLowerSha256(candidate.CatalogDigest) &&
            RepositoryPathSafety.TryNormalizeExactAbsolutePath(candidate.CatalogPath, out _) && !string.IsNullOrWhiteSpace(candidate.RegistryId) &&
            RepositoryPathSafety.TryNormalizeExactAbsolutePath(candidate.SourceRoot, out _) && candidate.SourceStatus is "latest" or "available" or "deprecated" &&
            ManagedAppVersion.TryParse(candidate.Version, out _) && ContractValidation.IsSafeRelativePath(candidate.PackagePath) &&
            candidate.PackageSize > 0 && ContractValidation.IsLowerSha256(candidate.PackageSha256) &&
            ContractValidation.IsLowerSha256(candidate.ReleaseManifestSha256) && !string.IsNullOrWhiteSpace(candidate.EntryIdentity);
    }
}
