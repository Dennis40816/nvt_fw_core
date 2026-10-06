// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace Nvt.Core.Launcher.Persistence;

/// <summary>OS-backed exclusive writer for one exact canonical application-state path.</summary>
public static class FileSystemVersionManagerWriteLease
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Tries to acquire the single writer handle, with a bounded contention wait.</summary>
    /// <param name="statePath">The nonblank application-state path.</param>
    /// <param name="waitTimeout">Maximum contention wait; zero still attempts acquisition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A live exact-path lease, busy result, or unavailable result.</returns>
    public static ValueTask<VersionManagerWriteLeaseResult> TryAcquireAsync(
        string statePath, TimeSpan waitTimeout, CancellationToken cancellationToken) =>
        TryAcquireAsync(statePath, waitTimeout, WriteLeaseOperations.Default, cancellationToken);

    internal static async ValueTask<VersionManagerWriteLeaseResult> TryAcquireAsync(
        string statePath, TimeSpan waitTimeout, WriteLeaseOperations operations,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(waitTimeout, TimeSpan.Zero);
        string lockPath;
        try
        {
            lockPath = GetLockPath(statePath);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(VersionManagerWriteLeaseIssue.Unavailable);
        }

        long started = operations.TickCount64();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
#pragma warning disable CA2000 // Ownership transfers to VersionManagerWriteLeaseResult.
                FileStream stream = operations.Open(lockPath);
                return new(VersionManagerWriteLeaseIssue.None,
                    new FileSystemVersionManagerWriteLeaseCustody(statePath, stream));
#pragma warning restore CA2000
            }
            catch (UnauthorizedAccessException)
            {
                return new(VersionManagerWriteLeaseIssue.Unavailable);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                var elapsed = TimeSpan.FromMilliseconds(operations.TickCount64() - started);
                if (elapsed >= waitTimeout)
                {
                    return new(VersionManagerWriteLeaseIssue.Busy);
                }
                TimeSpan remaining = waitTimeout - elapsed;
                await operations.Delay(remaining < RetryInterval ? remaining : RetryInterval,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return new(VersionManagerWriteLeaseIssue.Unavailable);
            }
        }
    }

    internal static string GetLockPath(string statePath)
    {
        string identity = NormalizeIdentityPath(statePath);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        string directory = Path.GetDirectoryName(Path.GetFullPath(statePath)) ??
            throw new ArgumentException("Version-manager state has no parent directory.", nameof(statePath));
        return Path.Combine(directory, $".{Path.GetFileName(statePath)}.{hash[..24]}.writer.lock");
    }

    internal static string NormalizeIdentityPath(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    private sealed class FileSystemVersionManagerWriteLeaseCustody(
        string statePath, FileStream stream) : IVersionManagerWriteLeaseCustody
    {
        private readonly string _statePath = NormalizeIdentityPath(statePath);
        private FileStream? _stream = stream ?? throw new ArgumentNullException(nameof(stream));

        public bool HoldsStatePath(string statePath)
        {
            FileStream? current = Volatile.Read(ref _stream);
            return current is not null &&
                !current.SafeFileHandle.IsClosed &&
                !current.SafeFileHandle.IsInvalid &&
                string.Equals(_statePath, NormalizeIdentityPath(statePath), StringComparison.Ordinal);
        }

        public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
    }

    private static bool IsSharingViolation(IOException exception)
    {
        int nativeCode = exception.HResult & 0xFFFF;
        return nativeCode is 32 or 33;
    }
}

// Per-call operations allow deterministic timing tests without a global hook or a second lock owner.
internal sealed record WriteLeaseOperations(
    Func<string, FileStream> Open,
    Func<long> TickCount64,
    Func<TimeSpan, CancellationToken, Task> Delay)
{
    internal static readonly WriteLeaseOperations Default = new(
        static path => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, bufferSize: 1, FileOptions.WriteThrough),
        static () => Environment.TickCount64,
        static (delay, token) => Task.Delay(delay, token));
}
