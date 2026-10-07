// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Repository;

internal static class RepositoryPathSafety
{
    internal static bool TryResolveRelativeFile(string root, string relativePath, out string resolved)
    {
        resolved = string.Empty;
        try
        {
            string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
            string candidate = Path.GetFullPath(Path.Combine(fullRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(candidate) || IsReparsePoint(candidate))
            {
                return false;
            }
            string? current = Path.GetDirectoryName(candidate);
            while (current is not null && !string.Equals(EnsureTrailingSeparator(Path.GetFullPath(current)),
                fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (IsReparsePoint(current))
                {
                    return false;
                }
                current = Path.GetDirectoryName(current);
            }
            resolved = candidate;
            return current is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    internal static string GetExactVersionDirectory(string versionsRoot, ManagedAppVersion version)
    {
        string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(versionsRoot));
        string target = Path.GetFullPath(Path.Combine(fullRoot, version.ToString()));
        return target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            ? target
            : throw new InvalidOperationException("Managed version target escaped the versions root.");
    }

    internal static bool IsSafeExistingDirectory(string path) => Directory.Exists(path) && !IsReparsePoint(path);

    internal static bool IsSafeOwnedTree(string root)
    {
        if (!IsSafeExistingDirectory(root))
        {
            return false;
        }
        try
        {
            return !Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Any(IsReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    internal static async ValueTask<byte[]?> ReadBoundedFileAsync(string path, int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            return null;
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length is < 1 || length > maximumBytes)
        {
            return null;
        }
        try
        {
            BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(stream, length,
                FileCaptureMode.CaptureBytes, cancellationToken).ConfigureAwait(false);
            return result.Bytes;
        }
        catch (FileChangedDuringReadException exception) when (exception.ChangeKind == FileChangeKind.ShortRead)
        {
            throw new EndOfStreamException("Unable to read beyond the end of the stream.");
        }
        catch (FileChangedDuringReadException)
        {
            return null;
        }
    }

    internal static string ResolvePayloadPath(string root, string relativePath)
    {
        string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            ? path
            : throw new InvalidDataException("Payload path escaped the managed root.");
    }

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
