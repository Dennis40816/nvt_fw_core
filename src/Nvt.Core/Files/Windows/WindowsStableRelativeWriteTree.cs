// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Files.Windows;

internal readonly record struct WindowsStableTreeReservation(
    int Files,
    int Directories,
    long Bytes,
    WindowsStableTreeLimits Limits)
{
    internal bool IsWithinLimits
    {
        get
        {
            Limits.Validate();
            return Limits.Allows(Files, Directories, Bytes);
        }
    }
}

/// <summary>Owns one already-resolved package root used to create a relative write tree.</summary>
internal sealed class WindowsStableRelativeWriteRoot : IDisposable
{
    private readonly WindowsStablePathCustody? _custody;
    // Guard: owned until Dispose; RootHandle rejects access after the handle is cleared.
    private SafeFileHandle? _rootHandle;

    private WindowsStableRelativeWriteRoot(
        string rootPath,
        SafeFileHandle rootHandle,
        WindowsStablePathCustody? custody)
    {
        RootPath = rootPath;
        _rootHandle = rootHandle;
        _custody = custody;
    }

    internal string RootPath { get; }
    internal SafeFileHandle RootHandle => _rootHandle ??
        throw new ObjectDisposedException(nameof(WindowsStableRelativeWriteRoot));

    internal static WindowsStableCustodyIssue TryAcquire(
        string absoluteRoot,
        out WindowsStableRelativeWriteRoot? root)
    {
        root = null;
        WindowsStablePathCustody? ownedCustody = null;
        SafeFileHandle? duplicate = null;
        try
        {
            string path = Path.GetFullPath(absoluteRoot);
            WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireWritableParent(path);
            if (!acquired.IsAcquired)
            {
                return acquired.Issue;
            }
            ownedCustody = acquired.Custody!;
            duplicate = WindowsStablePathCustody.Duplicate(
                ownedCustody.RootDirectoryHandle ?? throw new IOException("Writable root handle was not retained."));
            root = new(path, duplicate, ownedCustody);
            duplicate = null;
            ownedCustody = null;
            return WindowsStableCustodyIssue.None;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return WindowsStableCustodyIssue.Unavailable;
        }
        finally
        {
            duplicate?.Dispose();
            ownedCustody?.Dispose();
        }
    }

    internal static WindowsStableRelativeWriteRoot FromHeldDirectory(
        string absoluteRoot,
        SafeFileHandle heldRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteRoot);
        ArgumentNullException.ThrowIfNull(heldRoot);
        return Path.IsPathFullyQualified(absoluteRoot) &&
               WindowsStablePathCustody.HasExpectedType(heldRoot, directory: true)
            ? new(
                Path.GetFullPath(absoluteRoot),
                WindowsStablePathCustody.Duplicate(heldRoot),
                custody: null)
            : throw new IOException("The held package root is not a plain absolute directory.");
    }

    internal WindowsStableCustodyIssue TryCreateVersionTree(
        string versionName,
        string stagingDirectoryName,
        string versionsDirectoryName,
        WindowsStableTreeReservation reservation,
        int maximumRelativePathCharacters,
        Func<string, bool> isSafeRelativePayloadPath,
        Action<string>? afterDirectoryCreated,
        out WindowsStableRelativeWriteTree? tree)
    {
        tree = null;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelativePathCharacters);
        ArgumentNullException.ThrowIfNull(isSafeRelativePayloadPath);
        try
        {
            _ = RootedPathGuard.ResolveFileNameUnderRoot(stagingDirectoryName, RootPath);
            _ = RootedPathGuard.ResolveFileNameUnderRoot(versionsDirectoryName, RootPath);
        }
        catch (ArgumentException)
        {
            return WindowsStableCustodyIssue.InvalidPath;
        }
        if (!reservation.IsWithinLimits || !WindowsStablePathCustody.IsSingleComponent(versionName))
        {
            return WindowsStableCustodyIssue.InvalidPath;
        }

        SafeFileHandle? stagingParent = null;
        SafeFileHandle? versionsParent = null;
        SafeFileHandle? stagingRoot = null;
        try
        {
            int stagingStatus = OpenOrCreateDirectory(
                RootHandle,
                stagingDirectoryName,
                allowWriteShare: false,
                out stagingParent);
            int versionsStatus = OpenOrCreateDirectory(
                RootHandle,
                versionsDirectoryName,
                allowWriteShare: true,
                out versionsParent);
            if (stagingStatus != WindowsStablePathCustody.NativeMethods.StatusSuccess ||
                versionsStatus != WindowsStablePathCustody.NativeMethods.StatusSuccess ||
                !WindowsStablePathCustody.HasExpectedType(stagingParent, directory: true) ||
                !WindowsStablePathCustody.HasExpectedType(versionsParent, directory: true))
            {
                return WindowsStableCustodyIssue.ReparsePoint;
            }

            string stagingName = Guid.NewGuid().ToString("N");
            int rootStatus = CreatePromotableDirectory(stagingParent, stagingName, out stagingRoot);
            if (rootStatus != WindowsStablePathCustody.NativeMethods.StatusSuccess ||
                !WindowsStablePathCustody.HasExpectedType(stagingRoot, directory: true))
            {
                return WindowsStablePathCustody.MapStatus(rootStatus);
            }
            string stagingPath = Path.Combine(
                RootPath,
                stagingDirectoryName,
                stagingName);
            tree = new(
                stagingPath,
                Path.Combine(RootPath, versionsDirectoryName, versionName),
                versionName,
                reservation,
                maximumRelativePathCharacters,
                isSafeRelativePayloadPath,
                stagingParent,
                versionsParent,
                stagingRoot,
                afterDirectoryCreated);
            stagingParent = null;
            versionsParent = null;
            stagingRoot = null;
            return WindowsStableCustodyIssue.None;
        }
        finally
        {
            stagingRoot?.Dispose();
            versionsParent?.Dispose();
            stagingParent?.Dispose();
        }
    }

    public void Dispose()
    {
        _rootHandle?.Dispose();
        _rootHandle = null;
        _custody?.Dispose();
    }

    private static int OpenOrCreateDirectory(
        SafeFileHandle parent,
        string name,
        bool allowWriteShare,
        out SafeFileHandle handle)
    {
        return WindowsStablePathCustody.CreateRelative(
            parent,
            name,
            WindowsStablePathCustody.NativeMethods.ListDirectory |
                WindowsStablePathCustody.NativeMethods.AddFile |
                WindowsStablePathCustody.NativeMethods.AddSubdirectory |
                WindowsStablePathCustody.NativeMethods.ReadAttributes |
                WindowsStablePathCustody.NativeMethods.Synchronize,
            WindowsStablePathCustody.NativeMethods.ShareRead |
                (allowWriteShare ? WindowsStablePathCustody.NativeMethods.ShareWrite : 0) |
                WindowsStablePathCustody.NativeMethods.ShareDelete,
            WindowsStablePathCustody.NativeMethods.DirectoryFile |
                WindowsStablePathCustody.NativeMethods.SynchronousIoNonAlert |
                WindowsStablePathCustody.NativeMethods.OpenReparsePoint,
            WindowsStablePathCustody.NativeMethods.FileOpenIf,
            out handle);
    }

    internal static int CreateDirectory(
        SafeFileHandle parent,
        string name,
        out SafeFileHandle handle)
    {
        return WindowsStablePathCustody.CreateRelative(
            parent,
            name,
            WindowsStablePathCustody.NativeMethods.ListDirectory |
                WindowsStablePathCustody.NativeMethods.AddFile |
                WindowsStablePathCustody.NativeMethods.AddSubdirectory |
                WindowsStablePathCustody.NativeMethods.ReadAttributes |
                WindowsStablePathCustody.NativeMethods.Delete |
                WindowsStablePathCustody.NativeMethods.Synchronize,
            WindowsStablePathCustody.NativeMethods.ShareRead |
                WindowsStablePathCustody.NativeMethods.ShareDelete,
            WindowsStablePathCustody.NativeMethods.DirectoryFile |
                WindowsStablePathCustody.NativeMethods.SynchronousIoNonAlert |
                WindowsStablePathCustody.NativeMethods.OpenReparsePoint,
            WindowsStablePathCustody.NativeMethods.FileCreate,
            out handle);
    }

    private static int CreatePromotableDirectory(
        SafeFileHandle parent,
        string name,
        out SafeFileHandle handle)
    {
        return WindowsStablePathCustody.CreateRelative(
            parent,
            name,
            WindowsStablePathCustody.NativeMethods.ListDirectory |
                WindowsStablePathCustody.NativeMethods.AddFile |
                WindowsStablePathCustody.NativeMethods.AddSubdirectory |
                WindowsStablePathCustody.NativeMethods.ReadAttributes |
                WindowsStablePathCustody.NativeMethods.Delete |
                WindowsStablePathCustody.NativeMethods.Synchronize,
            WindowsStablePathCustody.NativeMethods.ShareRead |
                WindowsStablePathCustody.NativeMethods.ShareDelete,
            WindowsStablePathCustody.NativeMethods.DirectoryFile |
                WindowsStablePathCustody.NativeMethods.SynchronousIoNonAlert |
                WindowsStablePathCustody.NativeMethods.OpenReparsePoint,
            WindowsStablePathCustody.NativeMethods.FileCreate,
            out handle);
    }
}

/// <summary>Creates and promotes one exact no-follow staged package tree.</summary>
internal sealed class WindowsStableRelativeWriteTree : IDisposable
{
    private readonly int _maximumRelativePathCharacters;
    private readonly Func<string, bool> _isSafeRelativePayloadPath;
    private readonly Action<string>? _afterDirectoryCreated;
    // Guard: entries are added in Writing and cleared by Cleanup or Dispose before leaving ownership.
    private readonly Dictionary<string, SafeFileHandle> _directories =
        new(StringComparer.OrdinalIgnoreCase);
    // Guard: entries are added in Writing; handles are released on preparation, cleanup or disposal.
    private readonly Dictionary<string, SafeFileHandle> _files =
        new(StringComparer.OrdinalIgnoreCase);
    // Guard: entries are added in Writing; descendants are released in Prepared, all entries on cleanup/disposal.
    private readonly List<SafeFileHandle> _ownedDirectories = [];
    // Guard: retained in every live phase and released only by Dispose.
    private readonly SafeFileHandle _stagingParent;
    // Guard: retained in every live phase and released only by Dispose.
    private readonly SafeFileHandle _versionsParent;
    private readonly WindowsStableTreeReservation _reservation;
    private readonly string _stagingPath;
    private readonly string _finalPath;
    private readonly string _versionName;
    // Guard: each operation validates this phase before accessing handles; successful transitions replace it.
    private Phase _phase = new Phase.Writing();

    private abstract record Phase
    {
        private Phase() { }

        internal sealed record Writing : Phase;
        internal sealed record Prepared(WindowsStableOwnedTreeSnapshot Snapshot) : Phase;
        internal sealed record Promoted(WindowsStableOwnedTreeSnapshot Snapshot) : Phase;
        internal sealed record CleanedUp(WindowsStableCustodyIssue Issue) : Phase;
        internal sealed record Disposed : Phase;
    }

    internal WindowsStableRelativeWriteTree(
        string stagingPath,
        string finalPath,
        string versionName,
        WindowsStableTreeReservation reservation,
        int maximumRelativePathCharacters,
        Func<string, bool> isSafeRelativePayloadPath,
        SafeFileHandle stagingParent,
        SafeFileHandle versionsParent,
        SafeFileHandle stagingRoot,
        Action<string>? afterDirectoryCreated)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelativePathCharacters);
        ArgumentNullException.ThrowIfNull(isSafeRelativePayloadPath);
        reservation.Limits.Validate();
        _stagingPath = stagingPath;
        _finalPath = finalPath;
        _versionName = versionName;
        _reservation = reservation;
        _maximumRelativePathCharacters = maximumRelativePathCharacters;
        _isSafeRelativePayloadPath = isSafeRelativePayloadPath;
        _stagingParent = stagingParent;
        _versionsParent = versionsParent;
        _afterDirectoryCreated = afterDirectoryCreated;
        _directories.Add(string.Empty, stagingRoot);
        _ownedDirectories.Add(stagingRoot);
    }

    internal string StagingPath
    {
        get
        {
            ThrowIfDisposed();
            return _stagingPath;
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "The original handle stays in custody; the duplicate transfers to the returned FileStream.")]
    internal FileStream CreateFile(string relativePath)
    {
        _ = RequirePhase<Phase.Writing>(nameof(CreateFile));
        if ((relativePath is null || relativePath.Length > _maximumRelativePathCharacters ||
             !_isSafeRelativePayloadPath(relativePath)) ||
            _files.Count >= _reservation.Files)
        {
            throw new IOException("The package write reservation was exceeded.");
        }

        string normalized = relativePath.Replace('\\', '/');
        string[] components = normalized.Split('/');
        SafeFileHandle parent = _directories[string.Empty];
        string parentRelative = string.Empty;
        string parentPath = StagingPath;
        for (int index = 0; index < components.Length - 1; index++)
        {
            string childRelative = parentRelative.Length == 0
                ? components[index]
                : $"{parentRelative}/{components[index]}";
            if (!_directories.TryGetValue(childRelative, out SafeFileHandle? child))
            {
                if (_ownedDirectories.Count - 1 >= _reservation.Directories)
                {
                    throw new IOException("The package directory reservation was exceeded.");
                }
                int status = WindowsStableRelativeWriteRoot.CreateDirectory(parent, components[index], out child);
                if (status != WindowsStablePathCustody.NativeMethods.StatusSuccess ||
                    !WindowsStablePathCustody.HasExpectedType(child, directory: true))
                {
                    child.Dispose();
                    throw new IOException("A package directory changed during relative creation.");
                }
                _directories.Add(childRelative, child);
                _ownedDirectories.Add(child);
                string childPath = Path.Combine(parentPath, components[index]);
                _afterDirectoryCreated?.Invoke(childPath);
            }
            parent = child;
            parentRelative = childRelative;
            parentPath = Path.Combine(parentPath, components[index]);
        }

        int fileStatus = CreateNewFile(parent, components[^1], out SafeFileHandle fileHandle);
        if (fileStatus != WindowsStablePathCustody.NativeMethods.StatusSuccess ||
            !WindowsStablePathCustody.HasExpectedType(fileHandle, directory: false))
        {
            fileHandle.Dispose();
            throw new IOException("A package file changed during relative creation.");
        }
        _files.Add(normalized, fileHandle);
        SafeFileHandle? duplicate = WindowsStablePathCustody.Duplicate(fileHandle);
        try
        {
            var stream = new FileStream(duplicate, FileAccess.Write, bufferSize: 64 * 1024, isAsync: false);
            duplicate = null;
            return stream;
        }
        finally
        {
            duplicate?.Dispose();
        }
    }

    internal WindowsStableCustodyIssue PrepareForPromotion(Action<string>? afterExactTopologyChecked = null)
    {
        _ = RequirePhase<Phase.Writing>(nameof(PrepareForPromotion));
        long bytes = 0;
        foreach (SafeFileHandle file in _files.Values)
        {
            long length = RandomAccess.GetLength(file);
            if (length < 0 || bytes > _reservation.Bytes - length)
            {
                return WindowsStableCustodyIssue.Unavailable;
            }
            bytes += length;
        }
        if (_files.Count != _reservation.Files ||
            _ownedDirectories.Count - 1 != _reservation.Directories ||
            bytes != _reservation.Bytes ||
            !HasExactOwnedTopology())
        {
            return WindowsStableCustodyIssue.Changed;
        }
        afterExactTopologyChecked?.Invoke(StagingPath);
        if (!HasExactOwnedTopology() ||
            !WindowsStablePathCustody.TrySnapshotOwnedTree(
                _files,
                _directories,
                out WindowsStableOwnedTreeSnapshot? snapshot) ||
            snapshot is null)
        {
            return WindowsStableCustodyIssue.Changed;
        }
        foreach (SafeFileHandle file in _files.Values)
        {
            file.Dispose();
        }
        foreach (SafeFileHandle directory in _ownedDirectories.Skip(1))
        {
            directory.Dispose();
        }
        _phase = new Phase.Prepared(snapshot);
        return WindowsStableCustodyIssue.None;
    }

    internal WindowsStableCustodyIssue Promote()
    {
        Phase.Prepared prepared = RequirePhase<Phase.Prepared>(nameof(Promote));

        int status = WindowsStablePathCustody.RenameRelative(
            _directories[string.Empty],
            _versionsParent,
            _versionName);
        if (status != WindowsStablePathCustody.NativeMethods.StatusSuccess)
        {
            return WindowsStablePathCustody.MapStatus(status);
        }
        _phase = new Phase.Promoted(prepared.Snapshot);
        return WindowsStableCustodyIssue.None;
    }

    internal WindowsStableCustodyResult CapturePromotedImmutableTree(
        string finalPath,
        CancellationToken cancellationToken)
    {
        Phase.Promoted promoted = RequirePhase<Phase.Promoted>(nameof(CapturePromotedImmutableTree));
        WindowsStableCustodyResult captured =
            WindowsStablePathCustody.TryCaptureImmutableTreeFromHeldDirectory(
                finalPath,
                _directories[string.Empty],
                _reservation.Limits,
                cancellationToken);
        if (!captured.IsAcquired || captured.Custody!.MatchesOwnedSnapshot(promoted.Snapshot))
        {
            return captured;
        }
        captured.Custody.Dispose();
        return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Changed);
    }

    internal WindowsStableCustodyIssue RollbackPromotionAndCleanup()
    {
        ThrowIfDisposed();
        return Cleanup();
    }

    internal WindowsStableCustodyIssue Cleanup()
    {
        ThrowIfDisposed();
        if (_phase is Phase.CleanedUp cleanedUp)
        {
            return cleanedUp.Issue;
        }
        WindowsStableCustodyIssue issue;
        if (_phase is Phase.Prepared or Phase.Promoted)
        {
            WindowsStableOwnedTreeSnapshot snapshot = _phase is Phase.Prepared prepared
                ? prepared.Snapshot
                : ((Phase.Promoted)_phase).Snapshot;
            issue = WindowsStablePathCustody.TryDeleteExactTreeFromHeldDirectory(
                _phase is Phase.Promoted ? _finalPath : StagingPath,
                _directories[string.Empty],
                snapshot,
                _reservation.Limits);
            ReleaseOwnedHandles();
            _phase = new Phase.CleanedUp(issue);
            return issue;
        }
        issue = WindowsStableCustodyIssue.None;
        foreach (SafeFileHandle file in _files.Values.Reverse())
        {
            if (!WindowsStablePathCustody.MarkDeleteOnClose(file))
            {
                issue = WindowsStableCustodyIssue.Changed;
            }
            file.Dispose();
        }
        _files.Clear();
        foreach (SafeFileHandle directory in _ownedDirectories.AsEnumerable().Reverse())
        {
            if (!WindowsStablePathCustody.MarkDeleteOnClose(directory))
            {
                issue = WindowsStableCustodyIssue.Changed;
            }
            directory.Dispose();
        }
        _ownedDirectories.Clear();
        _directories.Clear();
        _phase = new Phase.CleanedUp(issue);
        return issue;
    }

    public void Dispose()
    {
        if (_phase is Phase.Disposed)
        {
            return;
        }
        if (_phase is Phase.Writing or Phase.Prepared)
        {
            _ = Cleanup();
        }
        else if (_phase is Phase.Promoted)
        {
            ReleaseOwnedHandles();
        }
        _stagingParent.Dispose();
        _versionsParent.Dispose();
        _phase = new Phase.Disposed();
    }

    private TPhase RequirePhase<TPhase>(string operation) where TPhase : Phase
    {
        ThrowIfDisposed();
        return _phase as TPhase ?? throw new InvalidOperationException(
            $"Cannot {operation} while the write tree is in the {_phase.GetType().Name} phase.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_phase is Phase.Disposed, this);
    }

    private void ReleaseOwnedHandles()
    {
        ThrowIfDisposed();
        foreach (SafeFileHandle file in _files.Values.Reverse())
        {
            file.Dispose();
        }
        _files.Clear();
        foreach (SafeFileHandle directory in _ownedDirectories.AsEnumerable().Reverse())
        {
            directory.Dispose();
        }
        _ownedDirectories.Clear();
        _directories.Clear();
    }

    private static int CreateNewFile(
        SafeFileHandle parent,
        string name,
        out SafeFileHandle handle)
    {
        return WindowsStablePathCustody.CreateRelative(
            parent,
            name,
            WindowsStablePathCustody.NativeMethods.WriteData |
                WindowsStablePathCustody.NativeMethods.ReadAttributes |
                WindowsStablePathCustody.NativeMethods.WriteAttributes |
                WindowsStablePathCustody.NativeMethods.Delete |
                WindowsStablePathCustody.NativeMethods.Synchronize,
            WindowsStablePathCustody.NativeMethods.ShareRead |
                WindowsStablePathCustody.NativeMethods.ShareDelete,
            WindowsStablePathCustody.NativeMethods.NonDirectoryFile |
                WindowsStablePathCustody.NativeMethods.SynchronousIoNonAlert |
                WindowsStablePathCustody.NativeMethods.OpenReparsePoint,
            WindowsStablePathCustody.NativeMethods.FileCreate,
            out handle);
    }

    private bool HasExactOwnedTopology()
    {
        _ = RequirePhase<Phase.Writing>(nameof(HasExactOwnedTopology));
        foreach (string directory in _directories.Keys)
        {
            string[] expected =
            [
                .. _directories.Keys
                    .Where(child => child.Length > 0 &&
                        string.Equals(ParentOf(child), directory, StringComparison.OrdinalIgnoreCase))
                    .Select(NameOf),
                .. _files.Keys
                    .Where(child => string.Equals(
                        ParentOf(child),
                        directory,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(NameOf),
            ];
            Array.Sort(expected, StringComparer.Ordinal);
            string absolute = directory.Length == 0
                ? StagingPath
                : Path.Combine(
                    StagingPath,
                    directory.Replace('/', Path.DirectorySeparatorChar));
            if (!WindowsStablePathCustody.TryEnumerateChildNames(
                    absolute,
                    expected.Length + 1,
                    out string[] actual) ||
                !expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string ParentOf(string relativePath)
    {
        int separator = relativePath.LastIndexOf('/');
        return separator < 0 ? string.Empty : relativePath[..separator];
    }

    private static string NameOf(string relativePath)
    {
        int separator = relativePath.LastIndexOf('/');
        return separator < 0 ? relativePath : relativePath[(separator + 1)..];
    }

}
