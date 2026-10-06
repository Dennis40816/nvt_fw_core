// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;

namespace Nvt.Core.Files;

/// <summary>Reads a measured stream length and probes at most one trailing byte.</summary>
public static class BoundedFileReader
{
    /// <summary>Reads and hashes the complete measured content, optionally keeping its bytes.</summary>
    /// <param name="stream">The readable stream at the beginning of its content.</param>
    /// <param name="observedLength">The nonnegative complete length measured before reading.</param>
    /// <param name="mode">Whether to keep the content bytes as well as their hash.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The measured length, raw SHA-256 hash, and optional captured bytes.</returns>
    /// <exception cref="ArgumentNullException">The stream is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The length is negative or the mode is undefined.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    /// <exception cref="FileSizeLimitExceededException">Capture would exceed byte-array capacity.</exception>
    /// <exception cref="FileChangedDuringReadException">The stream ended early, grew, or changed its final length or position.</exception>
    public static async ValueTask<BoundedReadResult> ReadAndHashAsync(
        Stream stream,
        long observedLength,
        FileCaptureMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(observedLength);
        ValidateMode(mode);
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == FileCaptureMode.CaptureBytes && observedLength > Array.MaxLength)
        {
            throw new FileSizeLimitExceededException(
                observedLength, Array.MaxLength, isCaptureStorageLimit: true);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[]? acceptedBytes = mode == FileCaptureMode.CaptureBytes
            ? new byte[checked((int)observedLength)]
            : null;
        byte[] buffer = new byte[64 * 1024];
        long offset = 0;
        while (offset < observedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(buffer.Length, checked(observedLength - offset));
            Memory<byte> destination = acceptedBytes is null
                ? buffer.AsMemory(0, requested)
                : acceptedBytes.AsMemory(checked((int)offset), requested);
            int read = await stream.ReadAsync(destination, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                throw new FileChangedDuringReadException(FileChangeKind.ShortRead);
            }

            hash.AppendData(destination.Span[..read]);
            offset = checked(offset + read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        int trailingRead = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (trailingRead != 0)
        {
            throw new FileChangedDuringReadException(FileChangeKind.Growth);
        }
        if (stream.CanSeek)
        {
            long finalLength = stream.Length;
            if (finalLength != observedLength)
            {
                throw new FileChangedDuringReadException(
                    finalLength < observedLength
                        ? FileChangeKind.Shrinkage
                        : FileChangeKind.Growth);
            }
            if (stream.Position != observedLength)
            {
                throw new FileChangedDuringReadException(FileChangeKind.PositionChanged);
            }
        }
        return new BoundedReadResult(observedLength, hash.GetHashAndReset(), acceptedBytes);
    }

    private static void ValidateMode(FileCaptureMode mode)
    {
        if (mode is not (FileCaptureMode.IdentityOnly or FileCaptureMode.CaptureBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
