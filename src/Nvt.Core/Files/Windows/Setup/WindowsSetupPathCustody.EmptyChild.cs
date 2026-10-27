// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Native = Nvt.Core.Files.Windows.WindowsStablePathCustody.NativeMethods;

namespace Nvt.Core.Files.Windows;

internal enum WindowsSetupStagingCleanupState
{
    Absent, Observed, Deleted, RetryableContention, OwnedDeletionPending, ChangedOrUnsafe,
}

internal readonly record struct WindowsSetupStagingCleanupResult(
    WindowsSetupStagingCleanupState State, WindowsStablePathIdentity Identity = default);

// Cleanup is restricted to one observed empty direct child. A pending owned deletion
// is observed without issuing another delete against a possible replacement.
internal sealed partial class WindowsSetupPathCustody
{
    internal WindowsSetupStagingCleanupResult ObserveEmptyStagingChild(string path)
    {
        ThrowIfDisposed();
        if (_child is null || _child.IsInvalid ||
            _childPath is null || !IsDirectChild(path, _childPath))
        {
            return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
        }

        int status = Open(
            _child,
            Path.GetFileName(path),
            Native.ReadData | Native.ReadAttributes |
                Native.Synchronize,
            Native.ShareRead | Native.ShareWrite | Native.ShareDelete,
            Native.DirectoryFile |
                Native.SynchronousIoNonAlert |
                Native.OpenReparsePoint,
            Native.FileOpen,
            out SafeFileHandle handle);
        using (handle)
        {
            if (status is Native.StatusObjectNameNotFound or
                Native.StatusObjectPathNotFound)
            {
                return new(WindowsSetupStagingCleanupState.Absent);
            }
            if (status != Native.StatusSuccess || !WindowsStablePathCustody.HasExpectedType(handle, directory: true) ||
                !WindowsStablePathCustody.TryGetIdentity(
                    handle,
                    out WindowsStablePathIdentity identity))
            {
                return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
            }
            try
            {
                return Directory.EnumerateFileSystemEntries(path).Any()
                    ? new(WindowsSetupStagingCleanupState.ChangedOrUnsafe)
                    : new(WindowsSetupStagingCleanupState.Observed, identity);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
            }
        }
    }

    internal WindowsSetupStagingCleanupResult DeleteObservedEmptyStagingChild(
        string path,
        WindowsStablePathIdentity expectedIdentity,
        Action<string>? beforeDelete,
        Func<int, int>? stagingDeleteOpenStatusOverride = null,
        Func<int, int>? ownedDeletionObservationStatusOverride = null)
    {
        ThrowIfDisposed();
        if (_child is null || _child.IsInvalid ||
            _childPath is null || !IsDirectChild(path, _childPath))
        {
            return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
        }

        int status = Open(
            _child,
            Path.GetFileName(path),
            Native.ReadData |
                Native.ReadAttributes |
                Native.Delete |
                Native.Synchronize,
            Native.ShareRead | Native.ShareWrite,
            Native.DirectoryFile |
                Native.SynchronousIoNonAlert |
                Native.OpenReparsePoint,
            Native.FileOpen,
            out SafeFileHandle handle);
        bool deleteMarked = false;
        using (handle)
        {
            status = stagingDeleteOpenStatusOverride?.Invoke(status) ?? status;
            if (status == Native.StatusSharingViolation)
            {
                return new(WindowsSetupStagingCleanupState.RetryableContention);
            }
            bool plain = status == Native.StatusSuccess && WindowsStablePathCustody.HasExpectedType(handle, directory: true);
            WindowsStablePathIdentity actualIdentity = default;
            bool hasIdentity = plain && WindowsStablePathCustody.TryGetIdentity(
                handle,
                out actualIdentity);
            if (status != Native.StatusSuccess || !plain || !hasIdentity ||
                actualIdentity != expectedIdentity)
            {
                return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
            }
            try
            {
                beforeDelete?.Invoke(path);
                if (Directory.EnumerateFileSystemEntries(path).Any())
                {
                    return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
                }
                deleteMarked = WindowsStablePathCustody.MarkDeleteOnClose(handle);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
            }
        }
        if (!deleteMarked)
        {
            return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
        }
        WindowsSetupStagingCleanupResult observed = ObserveOwnedStagingDeletion(
            path,
            expectedIdentity, ownedDeletionObservationStatusOverride);
        return observed.State == WindowsSetupStagingCleanupState.Absent
            ? new(WindowsSetupStagingCleanupState.Deleted, expectedIdentity)
            : observed.State == WindowsSetupStagingCleanupState.OwnedDeletionPending
                ? observed
                : new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
    }

    internal WindowsSetupStagingCleanupResult ObserveOwnedStagingDeletion(
        string path,
        WindowsStablePathIdentity expectedIdentity,
        Func<int, int>? ownedDeletionObservationStatusOverride = null)
    {
        ThrowIfDisposed();
        if (_child is null || _child.IsInvalid ||
            _childPath is null || !IsDirectChild(path, _childPath))
        {
            return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
        }

        int status = Open(
            _child,
            Path.GetFileName(path),
            Native.ReadData | Native.ReadAttributes | Native.Synchronize,
            Native.ShareRead | Native.ShareWrite | Native.ShareDelete,
            Native.DirectoryFile |
                Native.SynchronousIoNonAlert |
                Native.OpenReparsePoint,
            Native.FileOpen,
            out SafeFileHandle handle);
        using (handle)
        {
            status = ownedDeletionObservationStatusOverride?.Invoke(status) ?? status;
            if (status is Native.StatusObjectNameNotFound or
                Native.StatusObjectPathNotFound)
            {
                return new(WindowsSetupStagingCleanupState.Absent, expectedIdentity);
            }
            if (status == Native.StatusDeletePending)
            {
                return new(WindowsSetupStagingCleanupState.OwnedDeletionPending, expectedIdentity);
            }
            if (status != Native.StatusSuccess || !WindowsStablePathCustody.HasExpectedType(handle, directory: true) ||
                !WindowsStablePathCustody.TryGetIdentity(
                    handle,
                    out WindowsStablePathIdentity actualIdentity) ||
                actualIdentity != expectedIdentity)
            {
                return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
            }
            try
            {
                return Directory.EnumerateFileSystemEntries(path).Any()
                    ? new(WindowsSetupStagingCleanupState.ChangedOrUnsafe)
                    : new(WindowsSetupStagingCleanupState.OwnedDeletionPending, expectedIdentity);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(WindowsSetupStagingCleanupState.ChangedOrUnsafe);
            }
        }
    }

}
