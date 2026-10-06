// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.IO;

namespace Nvt.Core.Launcher.Persistence;

/// <summary>Bounded raw application-state access for the application's mandatory strict codec.</summary>
/// <remarks>The caller retains the writer lease across the complete transaction, including all writes.</remarks>
public sealed class VersionManagerStateFile
{
    private readonly int _maximumBytes;

    /// <summary>Creates raw access for one explicit state path and inclusive positive byte ceiling.</summary>
    /// <param name="path">Exact application-state path; product default paths remain with the caller.</param>
    /// <param name="maximumBytes">The application's positive raw-state ceiling.</param>
    public VersionManagerStateFile(string path, int maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        StatePathIdentity = Path.GetFullPath(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        _maximumBytes = maximumBytes;
    }

    /// <summary>Gets the full exact state path used by the strict adapter and writer custody.</summary>
    public string StatePathIdentity { get; }

    /// <summary>Tries to own this application's state writer across the complete transaction.</summary>
    /// <param name="waitTimeout">Maximum bounded contention wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exact-path writer result.</returns>
    public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
        TimeSpan waitTimeout, CancellationToken cancellationToken) =>
        FileSystemVersionManagerWriteLease.TryAcquireAsync(StatePathIdentity, waitTimeout, cancellationToken);

    /// <summary>Reads complete bounded raw bytes; absence, links, empty and oversized files return null.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete raw bytes for mandatory strict decoding, or null.</returns>
    public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken) =>
        BoundedStateFile.ReadAsync(StatePathIdentity, _maximumBytes, cancellationToken);

    /// <summary>Publishes bytes produced by the strict adapter using the shared atomic byte writer.</summary>
    /// <param name="bytes">Already validated and encoded application-state bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completion token.</returns>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length > _maximumBytes)
        {
            throw new InvalidOperationException("Version-manager state exceeds its bounded size.");
        }
        await AtomicOutput.WriteBytesAsync(StatePathIdentity, bytes, cancellationToken).ConfigureAwait(false);
    }
}
