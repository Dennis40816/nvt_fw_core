// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Setup;
using Native = Nvt.Core.Files.Windows.WindowsStablePathCustody.NativeMethods;

namespace Nvt.Core.Launcher.Recovery;

internal sealed class ManagedSetupDestinationCustody(string root, WindowsSetupPathCustody files,
    Func<int, int>? stagingDeleteOpenStatusOverride, Func<int, int>? ownedDeletionObservationStatusOverride) : IDisposable
{
    internal static ManagedFirstInstallationMaterializationIssue Admit(string managedRoot)
    {
        ManagedFirstInstallationMaterializationIssue issue = TryAcquire(managedRoot, out var custody);
        using (custody) return issue != ManagedFirstInstallationMaterializationIssue.None ? issue : custody!.AdmitFreshDestination();
    }

    internal static ManagedFirstInstallationMaterializationIssue TryAcquire(string managedRoot,
        out ManagedSetupDestinationCustody? custody, Func<int, int>? stagingDeleteOpenStatusOverride = null,
        Func<int, int>? ownedDeletionObservationStatusOverride = null)
    {
        custody = null;
        if (!OperatingSystem.IsWindows()) return ManagedFirstInstallationMaterializationIssue.InvalidDestination;
        ManagedInstallationRootStatus status = FileSystemManagedInstallationRootProbe.AdmitRoot(managedRoot, out string root);
        if (status != ManagedInstallationRootStatus.Absent)
            return FileSystemManagedFirstInstallationRootMaterializer.MapObservedRoot(status);
        string? parent = Path.GetDirectoryName(root);
        if (parent is null || !Directory.Exists(parent)) return ManagedFirstInstallationMaterializationIssue.InvalidDestination;
        WindowsSetupPathCustody? files = null;
        try
        {
            WindowsStableCustodyIssue issue = WindowsSetupPathCustody.TryAcquire(parent, out files);
            if (issue != WindowsStableCustodyIssue.None) return MapCustodyIssue(issue);
            custody = new(root, files!, stagingDeleteOpenStatusOverride, ownedDeletionObservationStatusOverride);
            files = null;
            return ManagedFirstInstallationMaterializationIssue.None;
        }
        catch (UnauthorizedAccessException) { return ManagedFirstInstallationMaterializationIssue.PermissionDenied; }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        { return ManagedFirstInstallationMaterializationIssue.StateUnavailable; }
        finally { files?.Dispose(); }
    }

    internal ManagedFirstInstallationMaterializationIssue AdmitFreshDestination()
    {
        string[] names = [Path.GetFileName(root),
            Path.GetFileName(FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(root)),
            Path.GetFileName(FileSystemManagedInstallationRootProbe.GetStagingContainerPath(root))];
        foreach (string name in names)
        {
            int status = files.ObserveEntry(name);
            var issue = status switch
            {
                Native.StatusObjectNameNotFound or Native.StatusObjectPathNotFound => ManagedFirstInstallationMaterializationIssue.None,
                Native.StatusSuccess or Native.StatusReparsePointEncountered or Native.StatusSharingViolation or
                    Native.StatusDeletePending or Native.StatusNotADirectory or Native.StatusFileIsADirectory => ManagedFirstInstallationMaterializationIssue.RecoveryRequired,
                Native.StatusAccessDenied => ManagedFirstInstallationMaterializationIssue.PermissionDenied,
                Native.StatusObjectNameInvalid or Native.StatusObjectPathInvalid => ManagedFirstInstallationMaterializationIssue.InvalidDestination,
                _ => ManagedFirstInstallationMaterializationIssue.StateUnavailable,
            };
            if (issue != ManagedFirstInstallationMaterializationIssue.None) return issue;
        }
        try
        {
            int status = files.ProbeWriteAccess(out bool residue);
            return status == Native.StatusSuccess ? residue ? ManagedFirstInstallationMaterializationIssue.StateUnavailable
                : ManagedFirstInstallationMaterializationIssue.None : status switch
                {
                    Native.StatusAccessDenied => ManagedFirstInstallationMaterializationIssue.PermissionDenied,
                    Native.StatusObjectNameInvalid or Native.StatusObjectPathInvalid or Native.StatusObjectPathNotFound or
                        Native.StatusNotADirectory => ManagedFirstInstallationMaterializationIssue.InvalidDestination,
                    _ => ManagedFirstInstallationMaterializationIssue.StateUnavailable,
                };
        }
        catch (UnauthorizedAccessException) { return ManagedFirstInstallationMaterializationIssue.PermissionDenied; }
        catch (IOException) { return ManagedFirstInstallationMaterializationIssue.StateUnavailable; }
    }

    internal ManagedFirstInstallationMaterializationIssue CreateMarker(string name, out SafeFileHandle handle)
    {
        int status = files.CreateMarker(name, out handle);
        return status == Native.StatusSuccess ? ManagedFirstInstallationMaterializationIssue.None : MapCreateStatus(status);
    }

    internal ManagedFirstInstallationMaterializationIssue CreateStaging(string container, string child)
    {
        int status = files.CreateStaging(container, child);
        return status == Native.StatusSuccess ? ManagedFirstInstallationMaterializationIssue.None : MapCreateStatus(status);
    }

    internal WindowsStableRelativeWriteRoot OpenPackageWriteRoot() => files.OpenWriteRoot();
    internal ManagedFirstInstallationMaterializationIssue CaptureClosedTree(string path, WindowsStableTreeLimits limits,
        CancellationToken cancellationToken) => files.CaptureClosedTree(path, limits, cancellationToken) is var issue &&
        issue == WindowsStableCustodyIssue.None ? ManagedFirstInstallationMaterializationIssue.None : MapCustodyIssue(issue);
    internal bool RevalidateClosedTree() => files.RevalidateClosedTree();
    internal WindowsStableCustodyResult TryCloneClosedTree(CancellationToken cancellationToken) => files.TryCloneClosedTree(cancellationToken);

    internal ManagedFirstInstallationMaterializationIssue Promote(string finalName)
    {
        int status = files.Promote(finalName, out bool marked);
        return status switch
        {
            Native.StatusSuccess => marked ? ManagedFirstInstallationMaterializationIssue.None : ManagedFirstInstallationMaterializationIssue.RecoveryRequired,
            Native.StatusObjectNameCollision => ManagedFirstInstallationMaterializationIssue.RecoveryRequired,
            Native.StatusAccessDenied => ManagedFirstInstallationMaterializationIssue.PermissionDenied,
            _ => ManagedFirstInstallationMaterializationIssue.PromotionFailed,
        };
    }

    internal WindowsSetupStagingCleanupResult ObserveEmptyStagingChild(string path) => files.ObserveEmptyStagingChild(path);
    internal WindowsSetupStagingCleanupResult ObserveOwnedStagingDeletion(string path, WindowsStablePathIdentity identity) =>
        files.ObserveOwnedStagingDeletion(path, identity, ownedDeletionObservationStatusOverride);
    internal WindowsSetupStagingCleanupResult DeleteObservedEmptyStagingChild(string path, WindowsStablePathIdentity identity,
        Action<string>? beforeDelete) => files.DeleteObservedEmptyStagingChild(path, identity, beforeDelete,
            stagingDeleteOpenStatusOverride, ownedDeletionObservationStatusOverride);

    public void Dispose() => files.Dispose();

    private static ManagedFirstInstallationMaterializationIssue MapCreateStatus(int status) => status switch
    {
        Native.StatusAccessDenied => ManagedFirstInstallationMaterializationIssue.PermissionDenied,
        Native.StatusObjectNameCollision or Native.StatusSharingViolation or Native.StatusDeletePending or
            Native.StatusNotADirectory or Native.StatusFileIsADirectory => ManagedFirstInstallationMaterializationIssue.RecoveryRequired,
        Native.StatusObjectNameInvalid or Native.StatusObjectPathInvalid or Native.StatusObjectPathNotFound => ManagedFirstInstallationMaterializationIssue.InvalidDestination,
        _ => ManagedFirstInstallationMaterializationIssue.PromotionFailed,
    };

    private static ManagedFirstInstallationMaterializationIssue MapCustodyIssue(WindowsStableCustodyIssue issue) => issue switch
    {
        WindowsStableCustodyIssue.InvalidPath or WindowsStableCustodyIssue.ReparsePoint => ManagedFirstInstallationMaterializationIssue.InvalidDestination,
        WindowsStableCustodyIssue.AccessDenied => ManagedFirstInstallationMaterializationIssue.PermissionDenied,
        WindowsStableCustodyIssue.Contended or WindowsStableCustodyIssue.Changed => ManagedFirstInstallationMaterializationIssue.RecoveryRequired,
        WindowsStableCustodyIssue.Unavailable => ManagedFirstInstallationMaterializationIssue.StateUnavailable,
        WindowsStableCustodyIssue.None => throw new InvalidOperationException("Successful custody did not return its owner."),
        _ => throw new InvalidOperationException("Stable custody returned an undefined issue."),
    };
}
