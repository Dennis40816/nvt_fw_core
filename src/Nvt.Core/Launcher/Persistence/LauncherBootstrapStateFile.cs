// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.IO;
using Nvt.Core.Launcher.Activation;

namespace Nvt.Core.Launcher.Persistence;

/// <summary>Bounded raw launcher-state access derived from the exact application-state path.</summary>
/// <remarks>The caller holds the application-state writer; this collaborator acquires no independent lease.</remarks>
public sealed class LauncherBootstrapStateFile
{
    private readonly int _maximumBytes;

    /// <summary>Creates raw launcher-state access with an explicit product suffix and byte ceiling.</summary>
    /// <param name="versionManagerStatePath">Exact application-state path.</param>
    /// <param name="pathSuffix">The fixed product-owned suffix appended to the full application-state path.</param>
    /// <param name="maximumBytes">The application's positive inclusive launcher-state byte ceiling.</param>
    public LauncherBootstrapStateFile(string versionManagerStatePath, string pathSuffix, int maximumBytes)
    {
        StatePathIdentity = DerivePath(versionManagerStatePath, pathSuffix);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        _maximumBytes = maximumBytes;
    }

    /// <summary>Gets the exact derived launcher-state path.</summary>
    public string StatePathIdentity { get; }

    /// <summary>Appends the product's fixed suffix to the entire normalized application-state path.</summary>
    /// <param name="versionManagerStatePath">Nonblank application-state path.</param>
    /// <param name="pathSuffix">Nonblank fixed product suffix.</param>
    /// <returns>The injectively derived path for the supplied fixed suffix.</returns>
    public static string DerivePath(string versionManagerStatePath, string pathSuffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionManagerStatePath);
        string fullPath = Path.GetFullPath(versionManagerStatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathSuffix);
        return fullPath + pathSuffix;
    }

    /// <summary>Reads complete bounded raw bytes for mandatory strict decoding.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete bytes, or null for absence, links, empty or oversized files.</returns>
    public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken) =>
        BoundedStateFile.ReadAsync(StatePathIdentity, _maximumBytes, cancellationToken);

    /// <summary>Publishes validated launcher-state bytes under caller-held application-state custody.</summary>
    /// <param name="bytes">Already validated and encoded launcher-state bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stable launcher-state persistence outcome.</returns>
    public async ValueTask<LauncherBootstrapStateSaveResult> TryWriteAsync(
        ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        try
        {
            if (bytes.Length > _maximumBytes)
            {
                return new(LauncherBootstrapStateSaveIssue.Unavailable);
            }
            await AtomicOutput.WriteBytesAsync(StatePathIdentity, bytes, cancellationToken).ConfigureAwait(false);
            return new(LauncherBootstrapStateSaveIssue.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(LauncherBootstrapStateSaveIssue.Unavailable);
        }
    }
}
