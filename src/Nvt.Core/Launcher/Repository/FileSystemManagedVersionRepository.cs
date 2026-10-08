// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;

namespace Nvt.Core.Launcher.Repository;

/// <summary>Stages, inventories and deletes exact admitted application payloads.</summary>
/// <remarks>Roots, package identity, strict codecs and payload policy are supplied by the caller. Installation requires Windows held write custody.</remarks>
public sealed partial class FileSystemManagedVersionRepository : IManagedVersionRepository
{
    internal const string VersionsDirectoryName = "versions";
    internal const string StagingDirectoryName = ".staging";
    internal const string AdmissionFileName = ManagedPackageVerifier.AdmissionFileName;
    private readonly ProductDescriptor descriptor;
    private readonly IProductPackagePolicy policy;
    private readonly PackageVerificationLimits limits;
    private readonly IManagedVersionAdmissionCodec admissionCodec;
    private readonly ManagedPackageVerifier verifier;
    private readonly InstalledPayloadProof proof;
    private readonly RepositoryOperations operations;

    /// <summary>Creates an application repository with mandatory product adapters and explicit positive ceilings.</summary>
    /// <param name="descriptor">The caller's exact application and runtime identity.</param>
    /// <param name="productPolicy">The mandatory strict manifest and relative payload adapter.</param>
    /// <param name="limits">Explicit positive package and installed-tree ceilings.</param>
    /// <param name="admissionCodec">The mandatory strict installed-admission codec.</param>
    public FileSystemManagedVersionRepository(ProductDescriptor descriptor, IProductPackagePolicy productPolicy,
        PackageVerificationLimits limits, IManagedVersionAdmissionCodec admissionCodec)
        : this(descriptor, productPolicy, limits, admissionCodec, new RepositoryOperations())
    {
    }

    internal FileSystemManagedVersionRepository(ProductDescriptor descriptor, IProductPackagePolicy productPolicy,
        PackageVerificationLimits limits, IManagedVersionAdmissionCodec admissionCodec, RepositoryOperations operations)
    {
        verifier = new ManagedPackageVerifier(descriptor, productPolicy, limits);
        ArgumentNullException.ThrowIfNull(admissionCodec);
        ArgumentNullException.ThrowIfNull(operations);
        this.descriptor = descriptor;
        policy = productPolicy;
        this.limits = limits;
        this.admissionCodec = admissionCodec;
        this.operations = operations;
        proof = new InstalledPayloadProof(this, descriptor, productPolicy, limits, verifier);
    }

    internal WindowsStableTreeLimits TreeLimits => WindowsStableTreeLimits.ForInstalledVersion(
        checked(limits.MaximumArchiveEntries + 1), limits.MaximumInstalledDirectories,
        limits.MaximumExpandedBytes, limits.MaximumAdmissionBytes);

    /// <inheritdoc />
    public async ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(string sourceRoot,
        UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(package);
        try
        {
            string source = Path.GetFullPath(sourceRoot);
            if (!RepositoryPathSafety.IsSafeExistingDirectory(source) ||
                package.PackagePath.Value is not { } relativePath ||
                !RepositoryPathSafety.TryResolveRelativeFile(source, relativePath, out string packagePath))
            {
                return new(null, ManagedVersionInstallIssue.PackageUnavailable);
            }
            await using FileStream stream = OpenStablePackage(packagePath, package.PackageSize);
            return await verifier.VerifyAsync(stream, package, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(null, ManagedVersionInstallIssue.PackageUnavailable);
        }
    }

    internal async ValueTask<ManagedVersionAdmission?> ReadAdmissionAsync(string versionRoot,
        CancellationToken cancellationToken)
    {
        byte[]? bytes = await RepositoryPathSafety.ReadBoundedFileAsync(Path.Combine(versionRoot, AdmissionFileName),
            limits.MaximumAdmissionBytes, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }
        try
        {
            return admissionCodec.TryDecode(bytes, out ManagedVersionAdmission? admission) &&
                admission is not null && !string.IsNullOrWhiteSpace(admission.AdmissionIdentity) &&
                ContractValidation.IsLowerSha256(admission.ReleaseManifestSha256)
                ? admission : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FileStream OpenStablePackage(string path, long admittedLength)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != admittedLength)
        {
            stream.Dispose();
            throw new FileNotFoundException("Package length differs from the admitted catalog entry.", path);
        }
        return stream;
    }
}

internal sealed class RepositoryOperations
{
    internal Func<string, IEnumerable<string>> EnumerateDirectories { get; init; } = Directory.EnumerateDirectories;
    internal Func<string, bool> DirectoryExists { get; init; } = Directory.Exists;
    internal Action<WindowsStableCustodyStage>? CustodyHook { get; init; }
    internal Action? BeforeLeaseCreation { get; init; }
    internal Action<string>? BeforePackagePromotion { get; init; }
    internal Action<string>? AfterPackageDirectoryCreated { get; init; }
    internal Action<string>? AfterPromotion { get; init; }
    internal Action? BeforeExtraction { get; init; }
    internal Func<string, Stream, Stream>? WrapExtractionDestination { get; init; }
}
