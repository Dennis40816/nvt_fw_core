// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Native = Nvt.Core.Files.Windows.WindowsStablePathCustody.NativeMethods;

namespace Nvt.Core.Files.Windows;

// Setup holds a parent, a staging container and a promotable child. All native operations
// use the existing stable-custody primitives. This owner never interprets a Launcher marker.
internal sealed partial class WindowsSetupPathCustody : IDisposable
{
    private readonly WindowsStablePathCustody _parent;
    private readonly string _parentPath;
    private SafeFileHandle? _container;
    private SafeFileHandle? _child;
    private string? _childPath;
    private WindowsStablePathCustody? _closedTree;
    private bool _disposed;

    private WindowsSetupPathCustody(string parentPath, WindowsStablePathCustody parent)
    {
        _parentPath = parentPath;
        _parent = parent;
    }

    internal SafeFileHandle ParentHandle => _parent.RootDirectoryHandle ??
        throw new InvalidOperationException("Writable parent custody did not retain its root handle.");

    internal static WindowsStableCustodyIssue TryAcquire(string parentPath, out WindowsSetupPathCustody? custody)
    {
        custody = null;
        WindowsStableCustodyResult result = WindowsStablePathCustody.TryAcquireWritableParent(parentPath);
        if (!result.IsAcquired)
        {
            result.Custody?.Dispose();
            return result.Issue;
        }
        WindowsStablePathCustody? owned = result.Custody;
        try
        {
            custody = new(parentPath, owned!);
            owned = null;
            return WindowsStableCustodyIssue.None;
        }
        finally { owned?.Dispose(); }
    }

    internal int ObserveEntry(string name)
    {
        ThrowIfDisposed();
        int status = Open(ParentHandle, name, Native.ReadAttributes | Native.Synchronize,
            Native.ShareRead | Native.ShareWrite | Native.ShareDelete,
            Native.SynchronousIoNonAlert | Native.OpenReparsePoint, Native.FileOpen, out SafeFileHandle handle);
        handle.Dispose();
        return status;
    }

    internal int CreateMarker(string name, out SafeFileHandle handle)
    {
        ThrowIfDisposed();
        return Open(ParentHandle, name,
            Native.ReadData | Native.WriteData | Native.ReadAttributes | Native.WriteAttributes |
            Native.Delete | Native.Synchronize, Native.ShareRead,
            Native.NonDirectoryFile | Native.SynchronousIoNonAlert | Native.WriteThrough | Native.OpenReparsePoint,
            Native.FileCreate, out handle);
    }

    internal int CreateStaging(string containerPath, string childPath)
    {
        ThrowIfDisposed();
        if (!IsDirectChild(containerPath, _parentPath) || !IsDirectChild(childPath, containerPath))
        {
            return Native.StatusObjectNameInvalid;
        }
        int status = CreateDirectory(ParentHandle, Path.GetFileName(containerPath), out SafeFileHandle container);
        if (status != Native.StatusSuccess)
        {
            container.Dispose();
            return status;
        }
        _container = container;
        if (!WindowsStablePathCustody.HasExpectedType(container, directory: true))
        {
            return Native.StatusReparsePointEncountered;
        }
        status = CreateDirectory(container, Path.GetFileName(childPath), out SafeFileHandle child);
        if (status != Native.StatusSuccess)
        {
            child.Dispose();
            return status;
        }
        _child = child;
        _childPath = childPath;
        return WindowsStablePathCustody.HasExpectedType(child, directory: true)
            ? Native.StatusSuccess : Native.StatusReparsePointEncountered;
    }

    internal WindowsStableRelativeWriteRoot OpenWriteRoot()
    {
        ThrowIfDisposed();
        return _child is { IsInvalid: false } && _childPath is not null
            ? WindowsStableRelativeWriteRoot.FromHeldDirectory(_childPath, _child)
            : throw new IOException("The managed setup package root is unavailable.");
    }

    internal int Promote(string finalName, out bool containerDeleteMarked)
    {
        ThrowIfDisposed();
        containerDeleteMarked = false;
        if (_child is null || _container is null || _child.IsInvalid || _container.IsInvalid ||
            !WindowsStablePathCustody.IsSingleComponent(finalName))
        {
            return Native.StatusReparsePointEncountered;
        }
        int status = WindowsStablePathCustody.RenameRelative(_child, ParentHandle, finalName);
        if (status == Native.StatusSuccess)
        {
            containerDeleteMarked = WindowsStablePathCustody.MarkDeleteOnClose(_container);
        }
        return status;
    }

    internal WindowsStableCustodyIssue CaptureClosedTree(string root, WindowsStableTreeLimits limits,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_closedTree is not null || _child is null || _child.IsInvalid)
        {
            return WindowsStableCustodyIssue.Unavailable;
        }
        WindowsStableCustodyResult result = WindowsStablePathCustody.TryTransitionPromotedTreeToImmutableCustody(
            root, ParentHandle, ref _child, limits, cancellationToken);
        if (result.IsAcquired)
        {
            _closedTree = result.Custody;
        }
        else
        {
            result.Custody?.Dispose();
        }
        return result.Issue;
    }

    internal bool RevalidateClosedTree()
    {
        ThrowIfDisposed();
        return _closedTree?.RevalidateClosedTree() == true;
    }

    internal WindowsStableCustodyResult TryCloneClosedTree(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _closedTree is null ? WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Unavailable)
            : _closedTree.TryClone(cancellationToken: cancellationToken);
    }

    internal int ProbeWriteAccess(out bool residue)
    {
        ThrowIfDisposed();
        string name = $".managed-setup-probe-{Guid.NewGuid():N}.tmp";
        residue = false;
        int status = Open(ParentHandle, name, Native.WriteData | Native.WriteAttributes | Native.Delete | Native.Synchronize,
            0, Native.NonDirectoryFile | Native.SynchronousIoNonAlert | Native.DeleteOnClose |
            Native.WriteThrough | Native.OpenReparsePoint, Native.FileCreate, out SafeFileHandle handle);
        if (status != Native.StatusSuccess)
        {
            handle.Dispose();
            return status;
        }
        SafeFileHandle? owned = handle;
        FileStream stream;
        try
        {
            stream = new FileStream(owned, FileAccess.Write, bufferSize: 1, isAsync: false);
            owned = null;
        }
        finally
        {
            owned?.Dispose();
        }
        using (stream)
        {
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        residue = File.Exists(Path.Combine(_parentPath, name));
        return status;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _closedTree?.Dispose(); }
        finally
        {
            try { _child?.Dispose(); }
            finally
            {
                try { _container?.Dispose(); }
                finally { _parent.Dispose(); }
            }
        }
    }

    private static bool IsDirectChild(string child, string parent) => WindowsStablePathCustody.PathComparer.Equals(
        Path.GetDirectoryName(Path.GetFullPath(child)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)));

    private static int CreateDirectory(SafeFileHandle parent, string name, out SafeFileHandle handle) =>
        Open(parent, name, Native.ReadAttributes | Native.AddFile | Native.AddSubdirectory | Native.Delete | Native.Synchronize,
            Native.ShareRead | Native.ShareWrite,
            Native.DirectoryFile | Native.SynchronousIoNonAlert | Native.OpenReparsePoint, Native.FileCreate, out handle);

    private static int Open(SafeFileHandle parent, string name, uint access, uint share, uint options,
        uint disposition, out SafeFileHandle handle) => WindowsStablePathCustody.CreateRelative(
            parent, name, access, share, options, disposition, out handle);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
