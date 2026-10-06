// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;

namespace Nvt.Core.Launcher.Verification;

internal enum BoundedArchiveReadIssue
{
    None,
    EntryLengthExceeded,
    EntryLengthMismatch,
    AggregateLengthExceeded,
}

internal sealed class ExpandedByteBudget
{
    internal ExpandedByteBudget(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        MaximumBytes = maximumBytes;
    }

    internal long MaximumBytes { get; }

    internal long ConsumedBytes { get; private set; }

    internal long RemainingBytes => MaximumBytes - ConsumedBytes;

    internal bool Consume(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ConsumedBytes = checked(ConsumedBytes + count);
        return ConsumedBytes <= MaximumBytes;
    }
}

internal sealed record BoundedArchiveReadResult(
    long Length,
    string? Sha256,
    BoundedArchiveReadIssue Issue)
{
    internal bool IsSuccess => Issue == BoundedArchiveReadIssue.None;
}

/// <summary>Counts actual decompressed bytes while hashing and optionally extracting one entry.</summary>
internal static class BoundedArchiveReader
{
    private const int BufferSize = 64 * 1024;

    internal static async ValueTask<BoundedArchiveReadResult> ReadFileAndHashAsync(
        string path,
        long declaredLength,
        ExpandedByteBudget budget,
        CancellationToken cancellationToken,
        Action<int>? bytesTransferred = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var source = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        return await ReadAndHashAsync(
            source,
            declaredLength,
            budget,
            cancellationToken,
            bytesTransferred).ConfigureAwait(false);
    }

    internal static ValueTask<BoundedArchiveReadResult> ReadAndHashAsync(
        Stream source,
        long declaredLength,
        ExpandedByteBudget budget,
        CancellationToken cancellationToken,
        Action<int>? bytesTransferred = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(declaredLength);
        return ReadCoreAsync(
            source,
            declaredLength,
            exactLength: true,
            budget,
            destination: null,
            bytesTransferred,
            cancellationToken);
    }

    internal static ValueTask<BoundedArchiveReadResult> ReadAtMostAndHashAsync(
        Stream source,
        long maximumLength,
        ExpandedByteBudget budget,
        Stream? destination,
        CancellationToken cancellationToken,
        Action<int>? bytesTransferred = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        return ReadCoreAsync(
            source,
            maximumLength,
            exactLength: false,
            budget,
            destination,
            bytesTransferred,
            cancellationToken);
    }

    internal static ValueTask<BoundedArchiveReadResult> CopyAndHashAsync(
        Stream source,
        long declaredLength,
        ExpandedByteBudget budget,
        Stream destination,
        CancellationToken cancellationToken,
        Action<int>? bytesTransferred = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(declaredLength);
        ArgumentNullException.ThrowIfNull(destination);
        return ReadCoreAsync(
            source,
            declaredLength,
            exactLength: true,
            budget,
            destination,
            bytesTransferred,
            cancellationToken);
    }

    private static async ValueTask<BoundedArchiveReadResult> ReadCoreAsync(
        Stream source,
        long lengthLimit,
        bool exactLength,
        ExpandedByteBudget budget,
        Stream? destination,
        Action<int>? bytesTransferred,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
        long length = 0;
        while (true)
        {
            long entryRemaining = lengthLimit - length;
            long permitted = Math.Min(entryRemaining, budget.RemainingBytes);
            int requested = permitted < BufferSize
                ? checked((int)permitted + 1)
                : BufferSize;
            int read = await source.ReadAsync(
                buffer.AsMemory(0, requested),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length = checked(length + read);
            bool withinAggregate = budget.Consume(read);
            if (!withinAggregate)
            {
                return new(length, null, BoundedArchiveReadIssue.AggregateLengthExceeded);
            }
            if (length > lengthLimit)
            {
                return new(length, null, BoundedArchiveReadIssue.EntryLengthExceeded);
            }

            hash.AppendData(buffer, 0, read);
            if (destination is not null)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            bytesTransferred?.Invoke(read);
        }

        return exactLength && length != lengthLimit
            ? new(length, null, BoundedArchiveReadIssue.EntryLengthMismatch)
            : new(length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), BoundedArchiveReadIssue.None);
    }
}
