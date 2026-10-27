// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Performs one bounded, local-only observation of a managed installation root.</summary>
public sealed class FileSystemManagedInstallationRootProbe : IManagedInstallationRootProbe
{
    internal const string TransactionMarkerSuffix = ".managed-setup-transaction.v1.json";
    internal const string StagingContainerSuffix = ".managed-setup-staging";
    private readonly Func<string, ManagedInstallationRootObservation> _observe;

    /// <summary>Creates the production bounded local filesystem probe.</summary>
    public FileSystemManagedInstallationRootProbe()
        : this(ObserveCore)
    {
    }

    internal FileSystemManagedInstallationRootProbe(
        Func<string, ManagedInstallationRootObservation> observe)
    {
        _observe = observe ?? throw new ArgumentNullException(nameof(observe));
    }

    /// <inheritdoc />
    public async ValueTask<ManagedInstallationRootObservation> ObserveAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<ManagedInstallationRootObservation> observation = Task.Factory.StartNew(
            () => _observe(managedRoot),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
        return await observation.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ManagedInstallationRootObservation ObserveCore(string managedRoot)
    {
        ManagedInstallationRootStatus admission = AdmitRoot(managedRoot, out string root);
        if (admission != ManagedInstallationRootStatus.Absent)
        {
            return new(admission);
        }

        try
        {
            FileSystemEntryState marker = ObserveExactEntry(GetTransactionMarkerPath(root));
            FileSystemEntryState staging = ObserveExactEntry(GetStagingContainerPath(root));
            if (marker != FileSystemEntryState.Absent || staging != FileSystemEntryState.Absent)
            {
                return new(ManagedInstallationRootStatus.Residue);
            }

            FileSystemEntryState destination = ObserveExactEntry(root);
            return new(destination switch
            {
                FileSystemEntryState.Absent => ManagedInstallationRootStatus.Absent,
                FileSystemEntryState.Directory => ManagedInstallationRootStatus.Present,
                FileSystemEntryState.File or FileSystemEntryState.Unsafe =>
                    ManagedInstallationRootStatus.InvalidDestination,
                _ => throw new InvalidOperationException("Undefined filesystem entry state."),
            });
        }
        catch (UnauthorizedAccessException)
        {
            return new(ManagedInstallationRootStatus.PermissionDenied);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            return new(ManagedInstallationRootStatus.Unavailable);
        }
    }

    internal static string GetTransactionMarkerPath(string managedRoot)
    {
        return NormalizeRoot(managedRoot) + TransactionMarkerSuffix;
    }

    internal static string GetStagingContainerPath(string managedRoot)
    {
        return NormalizeRoot(managedRoot) + StagingContainerSuffix;
    }

    internal static ManagedInstallationRootStatus AdmitRoot(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;
        if (!RepositoryPathSafety.TryNormalizeExactAbsolutePath(value, out string root))
        {
            return ManagedInstallationRootStatus.InvalidDestination;
        }

        try
        {
            string? driveRoot = Path.GetPathRoot(root);
            if (string.IsNullOrWhiteSpace(driveRoot) ||
                RepositoryPathSafety.PathComparer.Equals(NormalizeRoot(driveRoot), NormalizeRoot(root)) ||
                new DriveInfo(driveRoot).DriveType != DriveType.Fixed)
            {
                return ManagedInstallationRootStatus.InvalidDestination;
            }

            string? existing = root;
            while (existing is not null && !EntryExists(existing))
            {
                existing = Path.GetDirectoryName(existing);
            }
            if (existing is null ||
                (File.GetAttributes(existing) & FileAttributes.Directory) == 0 ||
                RepositoryPathSafety.HasReparseComponent(existing))
            {
                return ManagedInstallationRootStatus.InvalidDestination;
            }

            normalized = NormalizeRoot(root);
            return ManagedInstallationRootStatus.Absent;
        }
        catch (UnauthorizedAccessException)
        {
            return ManagedInstallationRootStatus.PermissionDenied;
        }
        catch (IOException)
        {
            return ManagedInstallationRootStatus.Unavailable;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ManagedInstallationRootStatus.InvalidDestination;
        }
    }

    private static bool EntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static FileSystemEntryState ObserveExactEntry(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0
                ? FileSystemEntryState.Unsafe
                : (attributes & FileAttributes.Directory) != 0
                    ? FileSystemEntryState.Directory
                    : FileSystemEntryState.File;
        }
        catch (FileNotFoundException)
        {
            return FileSystemEntryState.Absent;
        }
        catch (DirectoryNotFoundException)
        {
            return FileSystemEntryState.Absent;
        }
    }

    private static string NormalizeRoot(string managedRoot)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(managedRoot));
    }

    private enum FileSystemEntryState
    {
        Absent,
        File,
        Directory,
        Unsafe,
    }
}
