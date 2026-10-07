// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Repository;

public sealed partial class FileSystemManagedVersionRepository
{
    /// <inheritdoc />
    public async ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(
        string managedRoot,
        IReadOnlyList<ManagedVersionAdmission> admissions,
        ManagedAppVersion? activeVersion,
        ManagedAppVersion? lastKnownGoodVersion,
        ManagedAppVersion? failedActivationVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(admissions);
        try
        {
            string versionsRoot = Path.Combine(Path.GetFullPath(managedRoot), VersionsDirectoryName);
            bool versionsRootExists;
            try
            {
                FileAttributes attributes = File.GetAttributes(versionsRoot);
                versionsRootExists = (attributes & FileAttributes.Directory) != 0;
                if (!versionsRootExists)
                {
                    return ManagedVersionInventoryReadResult.Unavailable();
                }
            }
            catch (FileNotFoundException)
            {
                versionsRootExists = false;
            }
            catch (DirectoryNotFoundException)
            {
                versionsRootExists = false;
            }
            var rows = new List<InstalledVersionSnapshot>();
            foreach (ManagedVersionAdmission admission in admissions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = RepositoryPathSafety.GetExactVersionDirectory(versionsRoot, admission.Version);
                ManagedVersionDamageReason? damage = failedActivationVersion == admission.Version
                    ? ManagedVersionDamageReason.FailedActivation
                    : await proof.VerifyAsync(
                        target,
                        admission,
                        cancellationToken).ConfigureAwait(false);
                rows.Add(new(
                    admission.Version,
                    admission.AdmissionIdentity,
                    damage is null ? ManagedVersionIntegrity.Healthy : ManagedVersionIntegrity.Damaged,
                    damage,
                    activeVersion == admission.Version,
                    lastKnownGoodVersion == admission.Version,
                    ManagedVersionAdmissionState.Admitted,
                    admission));
            }

            if (versionsRootExists)
            {
                if (!RepositoryPathSafety.IsSafeExistingDirectory(versionsRoot))
                {
                    return ManagedVersionInventoryReadResult.Unavailable();
                }

                string[] directories = [.. operations.EnumerateDirectories(versionsRoot)];
                HashSet<ManagedAppVersion> known = [.. admissions.Select(admission => admission.Version)];
                foreach (string directory in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(directory);
                    if (ManagedAppVersion.TryParse(name, out ManagedAppVersion version) && known.Add(version))
                    {
                        ManagedVersionAdmission? observed = await ReadAdmissionAsync(
                            directory,
                            cancellationToken).ConfigureAwait(false);
                        if (observed is null && !operations.DirectoryExists(directory))
                        {
                            return ManagedVersionInventoryReadResult.Unavailable();
                        }
                        bool hasMatchingSelfAdmission = observed?.Version == version;
                        ManagedVersionDamageReason? damage = hasMatchingSelfAdmission
                            ? await proof.VerifyAsync(
                                directory,
                                observed!,
                                cancellationToken).ConfigureAwait(false)
                            : ManagedVersionDamageReason.UnexpectedPath;
                        if (!operations.DirectoryExists(directory))
                        {
                            return ManagedVersionInventoryReadResult.Unavailable();
                        }
                        rows.Add(new(
                            version,
                            observed?.AdmissionIdentity ?? $"unadmitted:{version}",
                            damage is null ? ManagedVersionIntegrity.Healthy : ManagedVersionIntegrity.Damaged,
                            damage,
                            activeVersion == version,
                            lastKnownGoodVersion == version,
                            ManagedVersionAdmissionState.Unadmitted,
                            observed));
                    }
                }
            }

            return ManagedVersionInventoryReadResult.Success(ManagedVersionInventory.Create(rows));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManagedVersionInventoryReadResult.Unavailable();
        }
    }

    /// <inheritdoc />
    public async ValueTask<ManagedVersionDeleteIssue> DeleteAsync(
        string managedRoot,
        ManagedVersionAdmission admission,
        ManagedAppVersion? activeVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(admission);
        cancellationToken.ThrowIfCancellationRequested();
        if (activeVersion == admission.Version)
        {
            return ManagedVersionDeleteIssue.ActiveVersion;
        }

        try
        {
            string versionsRoot = Path.Combine(Path.GetFullPath(managedRoot), VersionsDirectoryName);
            string target = RepositoryPathSafety.GetExactVersionDirectory(versionsRoot, admission.Version);
            if (!Directory.Exists(target))
            {
                return ManagedVersionDeleteIssue.NotInstalled;
            }
            if (!RepositoryPathSafety.IsSafeOwnedTree(target))
            {
                return ManagedVersionDeleteIssue.UnsafeTarget;
            }

            ManagedVersionAdmission? installed = await ReadAdmissionAsync(target, cancellationToken).ConfigureAwait(false);
            if (installed != admission)
            {
                return ManagedVersionDeleteIssue.UnsafeTarget;
            }

            Directory.Delete(target, recursive: true);
            return ManagedVersionDeleteIssue.None;
        }
        catch (UnauthorizedAccessException)
        {
            return ManagedVersionDeleteIssue.DeleteFailed;
        }
        catch (IOException)
        {
            return ManagedVersionDeleteIssue.DeleteFailed;
        }
    }

}
