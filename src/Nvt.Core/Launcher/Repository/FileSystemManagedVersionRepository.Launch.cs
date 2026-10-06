// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Launcher.Windows;

namespace Nvt.Core.Launcher.Repository;

public sealed partial class FileSystemManagedVersionRepository
{
    /// <inheritdoc />
    public async ValueTask<ManagedExecutableLaunchLeaseResult> AcquireApplicationLaunchLeaseAsync(
        string managedRoot, ManagedVersionAdmission admission, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(admission);
        WindowsStablePathCustody? custody = null;
        try
        {
            if (!Path.IsPathFullyQualified(managedRoot))
            {
                return new(null, ManagedExecutableLaunchIssue.UnsafePath);
            }
            string versionRoot = RepositoryPathSafety.GetExactVersionDirectory(
                Path.Combine(Path.GetFullPath(managedRoot), VersionsDirectoryName), admission.Version);
            WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTree(versionRoot,
                TreeLimits, cancellationToken, operations.CustodyHook);
            if (!acquired.IsAcquired)
            {
                return new(null, MapCustodyIssue(acquired.Issue));
            }
            custody = acquired.Custody!;
            if (await proof.VerifyAsync(versionRoot, admission, cancellationToken).ConfigureAwait(false) is not null)
            {
                return new(null, ManagedExecutableLaunchIssue.Tampered);
            }
            byte[]? bytes = await ReadHeldFileAsync(custody, ManagedPackageVerifier.ManifestFileName,
                limits.MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
            if (bytes is null || !string.Equals(InstalledPayloadProof.Hash(bytes), admission.ReleaseManifestSha256,
                StringComparison.Ordinal) || !proof.TryReadManifest(bytes, admission, out PackageManifest? manifest))
            {
                return new(null, ManagedExecutableLaunchIssue.Tampered);
            }
            PackageFile[] applications = [.. manifest!.Files.Where(file =>
                string.Equals(file.Path, descriptor.ApplicationExecutableRelativePath, StringComparison.Ordinal))];
            if (applications is not [var application])
            {
                return new(null, ManagedExecutableLaunchIssue.Tampered);
            }
            operations.BeforeLeaseCreation?.Invoke();
            WindowsStablePathCustody ownedCustody = custody;
            custody = null;
            return await StableManagedExecutableLaunchLease.TryCreateFromVerifiedTreeAsync(ownedCustody,
                application.Path, application.Size, application.Sha256, limits.MaximumExpandedBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new(null, ManagedExecutableLaunchIssue.Unavailable);
        }
        finally
        {
            custody?.Dispose();
        }
    }

    internal static async ValueTask<byte[]?> ReadHeldFileAsync(WindowsStablePathCustody custody, string relativePath,
        int maximumBytes, CancellationToken cancellationToken)
    {
        using FileStream stream = custody.OpenReadOnlyFile(relativePath);
        if (stream.Length < 0 || stream.Length > maximumBytes)
        {
            return null;
        }
        var result = await Nvt.Core.Files.BoundedFileReader.ReadAndHashAsync(stream, stream.Length,
            Nvt.Core.Files.FileCaptureMode.CaptureBytes, cancellationToken).ConfigureAwait(false);
        return result.Bytes;
    }

    internal static ManagedExecutableLaunchIssue MapCustodyIssue(WindowsStableCustodyIssue issue) => issue switch
    {
        WindowsStableCustodyIssue.InvalidPath or WindowsStableCustodyIssue.ReparsePoint or WindowsStableCustodyIssue.Changed
            => ManagedExecutableLaunchIssue.UnsafePath,
        WindowsStableCustodyIssue.AccessDenied or WindowsStableCustodyIssue.Contended or WindowsStableCustodyIssue.Unavailable
            => ManagedExecutableLaunchIssue.Unavailable,
        WindowsStableCustodyIssue.None => throw new InvalidOperationException("Successful custody did not return its owner."),
        _ => throw new InvalidOperationException("Stable custody returned an undefined issue."),
    };

    internal InstalledPayloadProof InstalledProof => proof;
}
