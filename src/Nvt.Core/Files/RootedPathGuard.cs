// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Files;

/// <summary>Resolves confined filesystem paths and rejects reparse points.</summary>
public static class RootedPathGuard
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Resolves a root, creates its directory if missing, and returns a trailing separator.</summary>
    public static string ResolveRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        string fullPath = Path.GetFullPath(rootDirectory);
        RejectExistingParentReparsePoints(fullPath);
        DirectoryInfo directory = Directory.CreateDirectory(fullPath);
        RejectReparsePoint(directory.FullName);
        return EnsureTrailingSeparator(directory.FullName);
    }

    /// <summary>Resolves an existing directory root and rejects reparse points in its ancestry.</summary>
    public static string ResolveExistingRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        string fullPath = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Allowed root was not found: {fullPath}");
        }

        RejectReparsePoint(fullPath);
        return EnsureTrailingSeparator(fullPath);
    }

    /// <summary>Resolves an existing regular file confined to one of the supplied roots.</summary>
    public static string ResolveExistingFileUnderRoots(
        string path,
        IReadOnlyList<string> allowedRoots)
    {
        return ResolveFileUnderRoots(path, allowedRoots, mustExist: true);
    }

    /// <summary>Resolves a confined regular file or a new target with an existing parent.</summary>
    public static string ResolveFileUnderRoots(
        string path,
        IReadOnlyList<string> allowedRoots,
        bool mustExist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(allowedRoots);

        string fullPath = Path.GetFullPath(path);
        EnsureUnderAnyRoot(fullPath, allowedRoots);
        if (File.Exists(fullPath))
        {
            RejectReparsePoint(fullPath);
            RegularFileGuard.RequirePath(fullPath);
            return fullPath;
        }

        if (mustExist)
        {
            throw new FileNotFoundException("Artifact file was not found.", fullPath);
        }

        if (Directory.Exists(fullPath))
        {
            throw new IOException("A document target cannot be an existing directory.");
        }

        string? parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                $"Document target directory was not found: {parent}");
        }

        RejectExistingParentReparsePoints(fullPath);
        return fullPath;
    }

    /// <summary>Resolves an existing file from forward-slash relative segments under an existing root.</summary>
    public static string ResolveExistingRelativeFileUnderRoot(
        string relativePath,
        string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (Path.IsPathFullyQualified(relativePath) ||
            relativePath.IndexOfAny(['\\', ':', '\0']) >= 0)
        {
            throw new ArgumentException(
                "File paths must be relative and use forward slashes.",
                nameof(relativePath));
        }

        string[] segments = relativePath.Split('/');
        if (segments.Any(static segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
        {
            throw new ArgumentException(
                "File paths cannot contain empty, current, or parent segments.",
                nameof(relativePath));
        }

        string root = ResolveExistingRoot(rootDirectory);
        string fullPath = Path.GetFullPath(Path.Combine([root, .. segments]));
        EnsureUnderRoot(fullPath, root);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Relative file was not found.", fullPath);
        }

        RejectReparsePoint(fullPath);
        RegularFileGuard.RequirePath(fullPath);
        return fullPath;
    }

    /// <summary>Resolves a plain filename under a root without creating the root or requiring a file.</summary>
    public static string ResolveFileNameUnderRoot(string fileName, string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (fileName.IndexOfAny(['/', '\\', ':']) >= 0 ||
            fileName is "." or ".." ||
            Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("File name must be a plain filename without path syntax.", nameof(fileName));
        }

        string root = EnsureTrailingSeparator(Path.GetFullPath(rootDirectory));
        string fullPath = Path.GetFullPath(Path.Combine(root, fileName));
        EnsureUnderRoot(fullPath, root);
        return fullPath;
    }

    private static void EnsureUnderAnyRoot(string fullPath, IReadOnlyList<string> allowedRoots)
    {
        if (allowedRoots.Count == 0)
        {
            throw new InvalidOperationException("At least one allowed root is required.");
        }

        foreach (string root in allowedRoots)
        {
            string normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
            if (fullPath.StartsWith(normalizedRoot, PathComparison))
            {
                return;
            }
        }

        throw new UnauthorizedAccessException("Path is outside the configured root.");
    }

    private static void EnsureUnderRoot(string fullPath, string root)
    {
        string normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        if (!fullPath.StartsWith(normalizedRoot, PathComparison))
        {
            throw new UnauthorizedAccessException("Path is outside the configured root.");
        }
    }

    /// <summary>Compares normalized paths using ordinal case folding on Windows and ordinal comparison elsewhere.</summary>
    public static bool IsUnderRoot(string fullPath, string root)
    {
        string normalizedPath = Path.GetFullPath(fullPath);
        string normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        return normalizedPath.StartsWith(normalizedRoot, PathComparison);
    }

    private static void RejectReparsePoint(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("Reparse points are not allowed.");
        }

        string? directoryPath = Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(directoryPath))
        {
            FileAttributes directoryAttributes = File.GetAttributes(directoryPath);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Reparse points are not allowed.");
            }

            directoryPath = Directory.GetParent(directoryPath)?.FullName;
        }
    }

    private static void RejectExistingParentReparsePoints(string path)
    {
        string? currentPath = path;
        while (!string.IsNullOrWhiteSpace(currentPath) &&
               !File.Exists(currentPath) &&
               !Directory.Exists(currentPath))
        {
            currentPath = Path.GetDirectoryName(currentPath);
        }

        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            RejectReparsePoint(currentPath);
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
