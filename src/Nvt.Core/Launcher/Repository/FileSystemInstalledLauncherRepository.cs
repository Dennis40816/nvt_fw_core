// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Launcher.Windows;

namespace Nvt.Core.Launcher.Repository;

/// <summary>Verifies one launcher against its exact admitted application owner.</summary>
public sealed class FileSystemInstalledLauncherRepository : IInstalledLauncherRepository
{
    private readonly ProductDescriptor descriptor;
    private readonly PackageVerificationLimits limits;
    private readonly FileSystemManagedVersionRepository repository;
    private readonly RepositoryOperations operations;

    /// <summary>Creates a launcher repository with explicit product identity, strict adapters and positive limits.</summary>
    /// <param name="descriptor">The caller's exact application and launcher identity.</param>
    /// <param name="productPolicy">The mandatory strict manifest and relative payload adapter.</param>
    /// <param name="limits">Explicit positive package and installed-tree ceilings.</param>
    /// <param name="admissionCodec">The mandatory strict installed-admission codec.</param>
    public FileSystemInstalledLauncherRepository(ProductDescriptor descriptor, IProductPackagePolicy productPolicy,
        PackageVerificationLimits limits, IManagedVersionAdmissionCodec admissionCodec)
        : this(descriptor, productPolicy, limits, admissionCodec, new RepositoryOperations())
    {
    }

    internal FileSystemInstalledLauncherRepository(ProductDescriptor descriptor, IProductPackagePolicy productPolicy,
        PackageVerificationLimits limits, IManagedVersionAdmissionCodec admissionCodec, RepositoryOperations operations)
    {
        repository = new(descriptor, productPolicy, limits, admissionCodec, operations);
        this.descriptor = descriptor;
        this.limits = limits;
        this.operations = operations;
    }

    /// <inheritdoc />
    public async ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(string managedRoot,
        ManagedVersionAdmission admission, CancellationToken cancellationToken)
    {
        WindowsStableCustodyResult acquiredTree = AcquireVersionTree(managedRoot, admission, cancellationToken);
        if (!acquiredTree.IsAcquired)
        {
            return new(null, null, MapCustodyIssue(acquiredTree.Issue));
        }
        WindowsStablePathCustody? custody = acquiredTree.Custody!;
        try
        {
            InstalledLauncherResult verified = await ReadIdentityAsync(custody, admission,
                verifyExecutableBytes: false, cancellationToken).ConfigureAwait(false);
            if (!verified.IsVerified)
            {
                return new(null, null, verified.Issue);
            }
            ManagedLauncherIdentity identity = verified.Identity!;
            operations.BeforeLeaseCreation?.Invoke();
            WindowsStablePathCustody ownedCustody = custody;
            custody = null;
            var acquired = await StableManagedExecutableLaunchLease.TryCreateAsync(ownedCustody,
                identity.ExecutableRelativePath, identity.Size, identity.Sha256, limits.MaximumExecutableBytes,
                cancellationToken).ConfigureAwait(false);
            InstalledLauncherIssue issue = acquired.Issue switch
            {
                ManagedExecutableLaunchIssue.None => InstalledLauncherIssue.None,
                ManagedExecutableLaunchIssue.UnsafePath => InstalledLauncherIssue.UnsafePath,
                ManagedExecutableLaunchIssue.Tampered => InstalledLauncherIssue.Tampered,
                ManagedExecutableLaunchIssue.Unavailable => InstalledLauncherIssue.Unavailable,
                _ => throw new InvalidOperationException("Managed executable lease returned an undefined issue."),
            };
            return acquired.IsAcquired ? new(identity, acquired.Lease, issue) : new(null, null, issue);
        }
        finally
        {
            custody?.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask<InstalledLauncherResult> VerifyAsync(string managedRoot, ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        WindowsStableCustodyResult acquired = AcquireVersionTree(managedRoot, admission, cancellationToken);
        if (!acquired.IsAcquired)
        {
            return new(null, MapCustodyIssue(acquired.Issue));
        }
        using WindowsStablePathCustody custody = acquired.Custody!;
        return await ReadIdentityAsync(custody, admission, verifyExecutableBytes: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<InstalledLauncherResult> ReadIdentityAsync(WindowsStablePathCustody custody,
        ManagedVersionAdmission admission, bool verifyExecutableBytes, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? bytes = await RepositoryPathSafety.ReadBoundedFileAsync(
                custody.GetAbsoluteFilePath(ManagedPackageVerifier.ManifestFileName), limits.MaximumManifestBytes,
                cancellationToken).ConfigureAwait(false);
            if (bytes is null || !string.Equals(InstalledPayloadProof.Hash(bytes), admission.ReleaseManifestSha256,
                StringComparison.Ordinal) || !repository.InstalledProof.TryReadManifest(bytes, admission, out PackageManifest? manifest))
            {
                return new(null, InstalledLauncherIssue.InvalidManifest);
            }
            if (manifest!.Launcher is not { } launcher)
            {
                return new(null, InstalledLauncherIssue.ProtocolMismatch);
            }
            if (!InstalledPayloadProof.HasExactTopology(custody, manifest.Files))
            {
                return new(null, InstalledLauncherIssue.Tampered);
            }
            ManagedLauncherIdentity identity = ManagedLauncherIdentity.Create(descriptor, limits.MaximumExecutableBytes,
                admission.Version, admission.AdmissionIdentity, admission.ReleaseManifestSha256,
                launcher.LauncherVersion, launcher.ProtocolVersion, launcher.ExecutableRelativePath, launcher.Size, launcher.Sha256);
            if (!verifyExecutableBytes)
            {
                return new(identity, InstalledLauncherIssue.None);
            }
            if (await repository.InstalledProof.VerifyAsync(custody.RootPath, admission,
                cancellationToken).ConfigureAwait(false) is not null)
            {
                return new(null, InstalledLauncherIssue.Tampered);
            }
            using FileStream stream = custody.OpenReadOnlyFile(identity.ExecutableRelativePath);
            if (stream.Length != identity.Size)
            {
                return new(null, InstalledLauncherIssue.Tampered);
            }
            BoundedReadResult read = await BoundedFileReader.ReadAndHashAsync(stream, identity.Size,
                FileCaptureMode.IdentityOnly, cancellationToken).ConfigureAwait(false);
            return read.Sha256 is not null && string.Equals(Convert.ToHexString(read.Sha256).ToLowerInvariant(), identity.Sha256, StringComparison.Ordinal)
                ? new(identity, InstalledLauncherIssue.None) : new(null, InstalledLauncherIssue.Tampered);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(null, InstalledLauncherIssue.Unavailable);
        }
    }

    private WindowsStableCustodyResult AcquireVersionTree(string managedRoot, ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(admission);
        if (!Path.IsPathFullyQualified(managedRoot))
        {
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.InvalidPath);
        }
        string target = RepositoryPathSafety.GetExactVersionDirectory(
            Path.Combine(Path.GetFullPath(managedRoot), FileSystemManagedVersionRepository.VersionsDirectoryName), admission.Version);
        return WindowsStablePathCustody.TryAcquireImmutableTree(target, repository.TreeLimits, cancellationToken, operations.CustodyHook);
    }

    private static InstalledLauncherIssue MapCustodyIssue(WindowsStableCustodyIssue issue) =>
        FileSystemManagedVersionRepository.MapCustodyIssue(issue) switch
        {
            ManagedExecutableLaunchIssue.UnsafePath => InstalledLauncherIssue.UnsafePath,
            _ => InstalledLauncherIssue.Unavailable,
        };
}
