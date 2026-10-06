// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Files.Windows;

internal sealed partial class WindowsStablePathCustody
{
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "Successful exact file custody transfers every held handle into the returned owner.")]
    internal static WindowsStableCustodyResult TryAcquireDeleteCapableFile(
        string absoluteFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() ||
            !TryNormalizeAbsoluteLocalPath(absoluteFilePath, out string filePath) ||
            Path.GetDirectoryName(filePath) is not { } parent ||
            Path.GetPathRoot(filePath) is not { } volumeRoot)
        {
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.InvalidPath);
        }
        var handles = new List<SafeFileHandle>();
        bool ownershipTransferred = false;
        var files = new Dictionary<string, HeldEntry>(PathComparer);
        var topology = new Dictionary<string, string[]>(PathComparer);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identities = new List<(string Path, SafeFileHandle Handle)>();
            WindowsStableCustodyIssue ancestorIssue = OpenAncestorChain(
                volumeRoot,
                parent,
                handles,
                identities,
                out SafeFileHandle? parentHandle);
            if (ancestorIssue != WindowsStableCustodyIssue.None || parentHandle is null)
            {
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(ancestorIssue);
            }
            string name = Path.GetFileName(filePath);
            int status = OpenRelative(
                parentHandle,
                name,
                directory: false,
                writableParent: false,
                allowWriteShare: false,
                allowDeleteShare: true,
                requestDeleteAccess: false,
                out SafeFileHandle fileHandle);
            if (IsExactChildMissingStatus(status))
            {
                fileHandle.Dispose();
                DisposeHandles(handles);
                return WindowsStableCustodyResult.MissingExactChild();
            }
            if (status != NativeMethods.StatusSuccess || !HasExpectedType(fileHandle, directory: false))
            {
                fileHandle.Dispose();
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(
                    status == NativeMethods.StatusSuccess
                        ? WindowsStableCustodyIssue.ReparsePoint
                        : MapStatus(status));
            }
            handles.Add(fileHandle);
            identities.Add((filePath, fileHandle));
            files.Add(name, new(filePath, fileHandle));
            topology.Add(parent, [name]);
            WindowsStableCustodyResult result = RevalidateIdentities(identities)
                ? new(
                    new WindowsStablePathCustody(
                        parent,
                        parentHandle,
                        handles,
                        files,
                        identities,
                        topology),
                    WindowsStableCustodyIssue.None)
                : FailAndDispose(handles, WindowsStableCustodyIssue.Changed);
            ownershipTransferred = result.IsAcquired;
            return result;
        }
        catch (OperationCanceledException)
        {
            DisposeHandles(handles);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            DisposeHandles(handles);
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.AccessDenied);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            DisposeHandles(handles);
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Unavailable);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                DisposeHandles(handles);
            }
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "Successful exact tree custody transfers every held handle into the returned owner.")]
    internal static WindowsStableCustodyResult TryAcquireDeleteCapableTree(
        string absoluteRoot,
        WindowsStableTreeLimits limits,
        CancellationToken cancellationToken = default)
    {
        limits.Validate();
        if (!OperatingSystem.IsWindows() ||
            !TryNormalizeAbsoluteLocalPath(absoluteRoot, out string root) ||
            Path.GetDirectoryName(root) is not { } parent ||
            Path.GetPathRoot(root) is not { } volumeRoot)
        {
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.InvalidPath);
        }
        var handles = new List<SafeFileHandle>();
        bool ownershipTransferred = false;
        var files = new Dictionary<string, HeldEntry>(PathComparer);
        var topology = new Dictionary<string, string[]>(PathComparer);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identities = new List<(string Path, SafeFileHandle Handle)>();
            WindowsStableCustodyIssue ancestorIssue = OpenAncestorChain(
                volumeRoot,
                parent,
                handles,
                identities,
                out SafeFileHandle? parentHandle);
            if (ancestorIssue != WindowsStableCustodyIssue.None || parentHandle is null)
            {
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(ancestorIssue);
            }
            int status = OpenRelative(
                parentHandle,
                Path.GetFileName(root),
                directory: true,
                writableParent: false,
                allowWriteShare: false,
                allowDeleteShare: true,
                requestDeleteAccess: false,
                out SafeFileHandle rootHandle);
            if (status != NativeMethods.StatusSuccess || !HasExpectedType(rootHandle, directory: true))
            {
                rootHandle.Dispose();
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(
                    status == NativeMethods.StatusSuccess
                        ? WindowsStableCustodyIssue.ReparsePoint
                        : MapStatus(status));
            }
            handles.Add(rootHandle);
            identities.Add((root, rootHandle));
            WindowsStableCustodyIssue captured = CaptureTree(
                root,
                rootHandle,
                handles,
                files,
                identities,
                topology,
                limits,
                allowDeleteShare: true,
                requestDeleteAccess: false,
                cancellationToken);
            WindowsStableCustodyResult result = captured == WindowsStableCustodyIssue.None && RevalidateIdentities(identities)
                ? new(
                    new WindowsStablePathCustody(
                        root,
                        rootHandle,
                        handles,
                        files,
                        identities,
                        topology),
                    WindowsStableCustodyIssue.None)
                : FailAndDispose(
                    handles,
                    captured == WindowsStableCustodyIssue.None
                        ? WindowsStableCustodyIssue.Changed
                        : captured);
            ownershipTransferred = result.IsAcquired;
            return result;
        }
        catch (OperationCanceledException)
        {
            DisposeHandles(handles);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            DisposeHandles(handles);
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.AccessDenied);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            DisposeHandles(handles);
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Unavailable);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                DisposeHandles(handles);
            }
        }
    }

    internal static WindowsStableCustodyResult TryAcquireImmutableTreeOrExactMissing(
        string absoluteRoot,
        WindowsStableTreeLimits limits,
        CancellationToken cancellationToken = default)
    {
        WindowsStableCustodyResult acquired = TryAcquireImmutableTree(
            absoluteRoot,
            limits,
            cancellationToken: cancellationToken);
        return acquired.Issue == WindowsStableCustodyIssue.Unavailable &&
            TryObserveExactMissingDirectory(absoluteRoot, cancellationToken)
                ? WindowsStableCustodyResult.MissingExactChild()
                : acquired;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "The duplicate of the exact held root transfers into the returned custody; every failure disposes it.")]
    internal static WindowsStableCustodyResult TryCaptureImmutableTreeFromHeldDirectory(
        string absoluteRoot,
        SafeFileHandle heldRoot,
        WindowsStableTreeLimits limits,
        CancellationToken cancellationToken = default)
    {
        return TryCaptureTreeFromHeldDirectory(
            absoluteRoot,
            heldRoot,
            limits,
            deleteCapable: false,
            cancellationToken);
    }

    /// <summary>
    /// Consumes a promoted delete-capable directory handle and returns immutable custody
    /// over the same directory identity. A read-only bridge is opened relative to the retained
    /// parent while the original still blocks replacement; the final handle denies later deletes.
    /// </summary>
    internal static WindowsStableCustodyResult TryTransitionPromotedTreeToImmutableCustody(
        string absoluteRoot,
        SafeFileHandle heldParent,
        ref SafeFileHandle? heldRoot,
        WindowsStableTreeLimits limits,
        CancellationToken cancellationToken = default)
    {
        limits.Validate();
        SafeFileHandle? source = heldRoot;
        if (source is null || source.IsInvalid || source.IsClosed ||
            heldParent.IsInvalid || heldParent.IsClosed ||
            !TryGetIdentity(source, out WindowsStablePathIdentity expectedIdentity))
        {
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.InvalidPath);
        }

        SafeFileHandle? bridge = null;
        SafeFileHandle? immutable = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bridgeStatus = OpenRelative(
                heldParent,
                Path.GetFileName(absoluteRoot),
                directory: true,
                writableParent: false,
                allowWriteShare: true,
                allowDeleteShare: true,
                requestDeleteAccess: false,
                out SafeFileHandle openedBridge);
            bridge = openedBridge;
            if (bridgeStatus != NativeMethods.StatusSuccess ||
                !TryGetIdentity(openedBridge, out WindowsStablePathIdentity bridgeIdentity) ||
                bridgeIdentity != expectedIdentity)
            {
                return WindowsStableCustodyResult.Failure(
                    bridgeStatus == NativeMethods.StatusSuccess
                        ? WindowsStableCustodyIssue.Changed
                        : MapStatus(bridgeStatus));
            }
            SafeFileHandle readBridge = openedBridge;

            source.Dispose();
            heldRoot = null;

            int immutableStatus = OpenRelative(
                heldParent,
                Path.GetFileName(absoluteRoot),
                directory: true,
                writableParent: false,
                allowWriteShare: true,
                allowDeleteShare: false,
                requestDeleteAccess: false,
                out SafeFileHandle reopened);
            immutable = reopened;
            if (immutableStatus != NativeMethods.StatusSuccess)
            {
                return WindowsStableCustodyResult.Failure(MapStatus(immutableStatus));
            }
            readBridge.Dispose();
            bridge = null;

            return
                TryGetIdentity(immutable, out WindowsStablePathIdentity actualIdentity) &&
                actualIdentity == expectedIdentity
                    ? TryCaptureTreeFromHeldDirectory(
                        absoluteRoot,
                        immutable,
                        limits,
                        deleteCapable: false,
                        cancellationToken)
                    : WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Changed);
        }
        finally
        {
            immutable?.Dispose();
            bridge?.Dispose();
        }
    }

    internal static WindowsStableCustodyIssue TryDeleteExactTreeFromHeldDirectory(
        string absoluteRoot,
        SafeFileHandle heldRoot,
        WindowsStableOwnedTreeSnapshot expected,
        WindowsStableTreeLimits limits,
        CancellationToken cancellationToken = default)
    {
        WindowsStableCustodyResult captured = TryCaptureTreeFromHeldDirectory(
            absoluteRoot,
            heldRoot,
            limits,
            deleteCapable: true,
            cancellationToken);
        if (!captured.IsAcquired)
        {
            return captured.Issue;
        }
        using WindowsStablePathCustody custody = captured.Custody!;
        return custody.MatchesOwnedSnapshot(expected) &&
               custody.RevalidateClosedTree()
            ? custody.DeleteHeldTree()
            : WindowsStableCustodyIssue.Changed;
    }

    internal static bool TrySnapshotOwnedTree(
        IReadOnlyDictionary<string, SafeFileHandle> files,
        IReadOnlyDictionary<string, SafeFileHandle> directories,
        out WindowsStableOwnedTreeSnapshot? snapshot)
    {
        var fileIdentities = new Dictionary<string, WindowsStablePathIdentity>(
            PathComparer);
        var directoryIdentities = new Dictionary<string, WindowsStablePathIdentity>(
            PathComparer);
        foreach ((string path, SafeFileHandle handle) in files)
        {
            if (!TryGetIdentity(handle, out WindowsStablePathIdentity identity))
            {
                snapshot = null;
                return false;
            }
            fileIdentities.Add(path, identity);
        }
        foreach ((string path, SafeFileHandle handle) in directories)
        {
            if (!TryGetIdentity(handle, out WindowsStablePathIdentity identity))
            {
                snapshot = null;
                return false;
            }
            directoryIdentities.Add(path, identity);
        }
        snapshot = new(fileIdentities, directoryIdentities);
        return true;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "The duplicate of the exact held root transfers into the returned custody; every failure disposes it.")]
    private static WindowsStableCustodyResult TryCaptureTreeFromHeldDirectory(
        string absoluteRoot,
        SafeFileHandle heldRoot,
        WindowsStableTreeLimits limits,
        bool deleteCapable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heldRoot);
        limits.Validate();
        if (!OperatingSystem.IsWindows() || heldRoot.IsInvalid || heldRoot.IsClosed ||
            !TryNormalizeAbsoluteLocalPath(absoluteRoot, out string root) ||
            !HasExpectedType(heldRoot, directory: true))
        {
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.InvalidPath);
        }

        var handles = new List<SafeFileHandle>();
        bool ownershipTransferred = false;
        var files = new Dictionary<string, HeldEntry>(PathComparer);
        var identities = new List<(string Path, SafeFileHandle Handle)>();
        var topology = new Dictionary<string, string[]>(PathComparer);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileHandle rootHandle = Duplicate(heldRoot);
            handles.Add(rootHandle);
            identities.Add((root, rootHandle));
            if (!IsSameIdentity(heldRoot, rootHandle))
            {
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Changed);
            }
            WindowsStableCustodyIssue captured = CaptureTree(
                root,
                rootHandle,
                handles,
                files,
                identities,
                topology,
                limits,
                allowDeleteShare: deleteCapable,
                requestDeleteAccess: deleteCapable,
                cancellationToken);
            if (captured != WindowsStableCustodyIssue.None ||
                !RevalidateIdentities(identities))
            {
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(
                    captured == WindowsStableCustodyIssue.None
                        ? WindowsStableCustodyIssue.Changed
                        : captured);
            }
            WindowsStableCustodyResult result = new(
                new WindowsStablePathCustody(
                    root,
                    rootHandle,
                    handles,
                    files,
                    identities,
                    topology),
                WindowsStableCustodyIssue.None);
            ownershipTransferred = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            DisposeHandles(handles);
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DisposeHandles(handles);
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Unavailable);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                DisposeHandles(handles);
            }
        }
    }

    internal bool MatchesOwnedSnapshot(WindowsStableOwnedTreeSnapshot expected)
    {
        return TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? actual) &&
            actual!.Files.Count == expected.Files.Count &&
            actual.Directories.Count == expected.Directories.Count &&
            actual.Files.All(pair =>
                expected.Files.TryGetValue(pair.Key, out WindowsStablePathIdentity identity) &&
                identity == pair.Value) &&
            actual.Directories.All(pair =>
                expected.Directories.TryGetValue(pair.Key, out WindowsStablePathIdentity identity) &&
                identity == pair.Value);
    }

    internal bool TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot)
    {
        ThrowIfDisposed();
        var files = new Dictionary<string, WindowsStablePathIdentity>(
            PathComparer);
        var directories = new Dictionary<string, WindowsStablePathIdentity>(
            PathComparer);
        foreach ((string relative, HeldEntry entry) in _files)
        {
            if (!TryGetIdentity(entry.Handle, out WindowsStablePathIdentity identity))
            {
                snapshot = null;
                return false;
            }
            files.Add(relative, identity);
        }
        foreach ((string absolute, SafeFileHandle handle) in _identities)
        {
            if (!IsDirectory(handle))
            {
                continue;
            }
            string relative = Path.GetRelativePath(RootPath, absolute);
            relative = relative == "." ? string.Empty : relative.Replace('\\', '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
            {
                continue;
            }
            if (!TryGetIdentity(handle, out WindowsStablePathIdentity identity))
            {
                snapshot = null;
                return false;
            }
            directories.Add(relative, identity);
        }
        snapshot = new(files, directories);
        return true;
    }

    internal WindowsStableCustodyIssue TryDeleteHeldEntry(
        string relativePath,
        bool directory,
        Action? afterExclusiveOpen = null,
        bool revalidateTopology = true)
    {
        ThrowIfDisposed();
        string normalized = string.IsNullOrEmpty(relativePath)
            ? string.Empty
            : relativePath.Replace('/', Path.DirectorySeparatorChar);
        string absolute = string.IsNullOrEmpty(normalized)
            ? RootPath
            : Path.GetFullPath(Path.Combine(RootPath, normalized));
        if (!TryFindHeldEntry(absolute, directory, out SafeFileHandle? expectedHandle) ||
            (directory && _topology.TryGetValue(absolute, out string[]? children) && children.Length != 0))
        {
            return WindowsStableCustodyIssue.Changed;
        }
        var exclusiveHandles = new List<SafeFileHandle>();
        try
        {
            WindowsStableCustodyIssue opened = TryOpenExclusivePathChain(
                absolute,
                directory,
                expectedHandle!,
                exclusiveHandles,
                out SafeFileHandle? deleteHandle);
            if (opened != WindowsStableCustodyIssue.None || deleteHandle is null)
            {
                return opened;
            }
            afterExclusiveOpen?.Invoke();
            if (!IsSameIdentity(expectedHandle!, deleteHandle) ||
                (revalidateTopology && directory &&
                 (!TryEnumerateChildNames(absolute, 1, out string[] actual) || actual.Length != 0)) ||
                !MarkDeleteOnClose(deleteHandle))
            {
                return WindowsStableCustodyIssue.Changed;
            }
            ForgetHeldEntry(absolute, expectedHandle!, directory);
            return WindowsStableCustodyIssue.None;
        }
        finally
        {
            DisposeHandles(exclusiveHandles);
        }
    }

    private WindowsStableCustodyIssue TryOpenExclusivePathChain(
        string absolute,
        bool targetIsDirectory,
        SafeFileHandle expectedTarget,
        List<SafeFileHandle> exclusiveHandles,
        out SafeFileHandle? deleteHandle)
    {
        deleteHandle = null;
        var paths = new Stack<string>();
        string current = absolute;
        while (true)
        {
            paths.Push(current);
            if (PathComparer.Equals(current, RootPath))
            {
                break;
            }
            current = Path.GetDirectoryName(current) ?? string.Empty;
            if (string.IsNullOrEmpty(current) ||
                !current.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
            {
                return WindowsStableCustodyIssue.Changed;
            }
        }

        while (paths.TryPop(out string? path))
        {
            bool isTarget = PathComparer.Equals(path, absolute);
            bool isDirectory = !isTarget || targetIsDirectory;
            string? parent = Path.GetDirectoryName(path);
            if (parent is null ||
                !TryFindHeldDirectory(parent, out SafeFileHandle? parentHandle) ||
                !TryFindHeldEntry(
                    path,
                    isDirectory,
                    out SafeFileHandle? expectedHandle))
            {
                return WindowsStableCustodyIssue.Changed;
            }
            int status = OpenRelative(
                parentHandle!,
                Path.GetFileName(path),
                isDirectory,
                writableParent: false,
                allowWriteShare: !isTarget,
                allowDeleteShare: false,
                requestDeleteAccess: isTarget,
                out SafeFileHandle opened);
            if (status != NativeMethods.StatusSuccess)
            {
                opened.Dispose();
                return MapStatus(status);
            }
            if (!IsSameIdentity(expectedHandle!, opened))
            {
                opened.Dispose();
                return WindowsStableCustodyIssue.Changed;
            }
            exclusiveHandles.Add(opened);
            if (isTarget)
            {
                deleteHandle = opened;
            }
        }
        return deleteHandle is not null && IsSameIdentity(expectedTarget, deleteHandle)
            ? WindowsStableCustodyIssue.None
            : WindowsStableCustodyIssue.Changed;
    }

    private void ForgetHeldEntry(
        string absolute,
        SafeFileHandle expectedHandle,
        bool directory)
    {
        _ = _handles.Remove(expectedHandle);
        _ = _identities.RemoveAll(pair => ReferenceEquals(pair.Handle, expectedHandle));
        if (directory)
        {
            _ = _topology.Remove(absolute);
        }
        else
        {
            string relative = NormalizeRelativeFile(Path.GetRelativePath(RootPath, absolute));
            _ = _files.Remove(relative);
        }
        string? parent = Path.GetDirectoryName(absolute);
        if (parent is not null && _topology.TryGetValue(parent, out string[]? children))
        {
            _topology[parent] = [.. children.Where(name => !string.Equals(
                name,
                Path.GetFileName(absolute),
                StringComparison.Ordinal))];
        }
        expectedHandle.Dispose();
    }

    private static WindowsStableCustodyResult FailAndDispose(
        List<SafeFileHandle> handles,
        WindowsStableCustodyIssue issue)
    {
        DisposeHandles(handles);
        return WindowsStableCustodyResult.Failure(issue);
    }

    private bool TryFindHeldDirectory(string absolute, out SafeFileHandle? handle)
    {
        handle = _identities
            .Where(static pair => IsDirectory(pair.Handle))
            .Where(pair => PathComparer.Equals(pair.Path, absolute))
            .Select(static pair => pair.Handle)
            .SingleOrDefault();
        return handle is not null && !handle.IsInvalid && !handle.IsClosed;
    }

    private bool TryFindHeldEntry(
        string absolute,
        bool directory,
        out SafeFileHandle? handle)
    {
        if (!directory)
        {
            handle = _files.Values
                .Where(entry => PathComparer.Equals(entry.AbsolutePath, absolute))
                .Select(static entry => entry.Handle)
                .SingleOrDefault();
            return handle is not null && !handle.IsInvalid && !handle.IsClosed;
        }
        return TryFindHeldDirectory(absolute, out handle);
    }

    private WindowsStableCustodyIssue DeleteHeldTree()
    {
        WindowsStableCustodyIssue issue = WindowsStableCustodyIssue.None;
        (string Path, SafeFileHandle Handle)[] directories =
        [
            .. _identities
                .Where(static identity => IsDirectory(identity.Handle))
                .OrderByDescending(static identity => identity.Path.Length),
        ];
        foreach (HeldEntry file in _files.Values.OrderByDescending(
                     static entry => entry.AbsolutePath.Length))
        {
            if (!MarkDeleteOnClose(file.Handle))
            {
                issue = WindowsStableCustodyIssue.Changed;
            }
            file.Handle.Dispose();
        }
        foreach ((string _, SafeFileHandle directory) in directories)
        {
            if (!MarkDeleteOnClose(directory))
            {
                issue = WindowsStableCustodyIssue.Changed;
            }
            directory.Dispose();
        }
        return issue;
    }

    private static bool TryObserveExactMissingDirectory(
        string absoluteRoot,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() ||
            !TryNormalizeAbsoluteLocalPath(absoluteRoot, out string root) ||
            Path.GetDirectoryName(root) is not { } parent ||
            Path.GetPathRoot(root) is not { } volumeRoot)
        {
            return false;
        }

        var handles = new List<SafeFileHandle>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identities = new List<(string Path, SafeFileHandle Handle)>();
            WindowsStableCustodyIssue issue = OpenAncestorChain(
                volumeRoot,
                parent,
                handles,
                identities,
                out SafeFileHandle? parentHandle);
            if (issue != WindowsStableCustodyIssue.None || parentHandle is null)
            {
                return false;
            }
            string name = Path.GetFileName(root);
            int first = OpenRelative(
                parentHandle,
                name,
                directory: true,
                writableParent: false,
                allowWriteShare: false,
                allowDeleteShare: false,
                requestDeleteAccess: false,
                out SafeFileHandle firstHandle);
            firstHandle.Dispose();
            if (!IsExactChildMissingStatus(first))
            {
                return false;
            }
            int second = OpenRelative(
                parentHandle,
                name,
                directory: true,
                writableParent: false,
                allowWriteShare: false,
                allowDeleteShare: false,
                requestDeleteAccess: false,
                out SafeFileHandle secondHandle);
            secondHandle.Dispose();
            return IsExactChildMissingStatus(second) && RevalidateIdentities(identities);
        }
        finally
        {
            DisposeHandles(handles);
        }
    }

    internal static bool TryObserveExactMissingFile(
        string absoluteFile,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() ||
            !TryNormalizeAbsoluteLocalPath(absoluteFile, out string file) ||
            Path.GetDirectoryName(file) is not { } parent ||
            Path.GetPathRoot(file) is not { } volumeRoot)
        {
            return false;
        }

        var handles = new List<SafeFileHandle>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identities = new List<(string Path, SafeFileHandle Handle)>();
            WindowsStableCustodyIssue issue = OpenAncestorChain(
                volumeRoot,
                parent,
                handles,
                identities,
                out SafeFileHandle? parentHandle);
            if (issue != WindowsStableCustodyIssue.None || parentHandle is null)
            {
                return false;
            }
            string name = Path.GetFileName(file);
            int first = OpenRelative(
                parentHandle,
                name,
                directory: false,
                writableParent: false,
                allowWriteShare: false,
                allowDeleteShare: false,
                requestDeleteAccess: false,
                out SafeFileHandle firstHandle);
            firstHandle.Dispose();
            if (!IsExactChildMissingStatus(first))
            {
                return false;
            }
            int second = OpenRelative(
                parentHandle,
                name,
                directory: false,
                writableParent: false,
                allowWriteShare: false,
                allowDeleteShare: false,
                requestDeleteAccess: false,
                out SafeFileHandle secondHandle);
            secondHandle.Dispose();
            return IsExactChildMissingStatus(second) && RevalidateIdentities(identities);
        }
        finally
        {
            DisposeHandles(handles);
        }
    }
}
