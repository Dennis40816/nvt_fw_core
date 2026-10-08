// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;

namespace Nvt.Core.Launcher.Repository;

public sealed partial class FileSystemManagedVersionRepository
{
    /// <inheritdoc />
    public async ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot,
        UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(package);
        WindowsStableRelativeWriteRoot? ownedRoot = null;
        try
        {
            WindowsStableCustodyIssue acquired = WindowsStableRelativeWriteRoot.TryAcquire(managedRoot, out ownedRoot);
            if (acquired != WindowsStableCustodyIssue.None || ownedRoot is null)
            {
                return Failure(ManagedVersionInstallIssue.PromotionFailed);
            }
            return await MaterializeVerifiedPayloadWithinHeldRootAsync(ownedRoot, sourceRoot, package,
                requireNew: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ownedRoot?.Dispose();
        }
    }

    internal async ValueTask<ManagedVersionInstallResult> MaterializeVerifiedPayloadWithinHeldRootAsync(
        WindowsStableRelativeWriteRoot writeRoot, string sourceRoot, UpdateCatalogVersionSnapshot package,
        bool requireNew, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string source = Path.GetFullPath(sourceRoot);
            if (!RepositoryPathSafety.IsSafeExistingDirectory(source) ||
                package.PackagePath.Value is not { } relativePath ||
                !RepositoryPathSafety.TryResolveRelativeFile(source, relativePath, out string packagePath))
            {
                return Failure(ManagedVersionInstallIssue.PackageUnavailable);
            }
            await using FileStream packageStream = OpenStablePackage(packagePath, package.PackageSize);
            ManagedPackagePlanResult result = await verifier.CreatePlanAsync(packageStream, package,
                cancellationToken, propagateReadFailures: true).ConfigureAwait(false);
            using ManagedPackagePlan? plan = result.Plan;
            if (!result.IsSuccess)
            {
                return Failure(result.Issue);
            }
            var admission = new ManagedVersionAdmission(package.Version, package.Identity, package.ReleaseManifestSha256);
            ReadOnlyMemory<byte> admissionBytes = admissionCodec.Encode(admission);
            if (!plan!.AdmitsAdmissionLength(admissionBytes.Length))
            {
                throw new InvalidDataException("Managed admission metadata exceeded its declared bound.");
            }
            admissionBytes = admissionBytes.ToArray();
            string target = RepositoryPathSafety.GetExactVersionDirectory(
                Path.Combine(writeRoot.RootPath, VersionsDirectoryName), package.Version);
            if (Directory.Exists(target))
            {
                return requireNew ? Failure(ManagedVersionInstallIssue.IdentityConflict)
                    : await VerifyExistingPayloadAsync(target, admission, wasAlreadyInstalled: true,
                        heldCustody: null, cancellationToken).ConfigureAwait(false);
            }
            var reservation = new WindowsStableTreeReservation(checked(plan!.FileCount + 1), plan.ImplicitDirectoryCount,
                checked(plan.ExpandedBytes + admissionBytes.Length), TreeLimits);
            WindowsStableRelativeWriteTree? ownedTree = null;
            try
            {
                WindowsStableCustodyIssue created = writeRoot.TryCreateVersionTree(package.Version.ToString(),
                    StagingDirectoryName, VersionsDirectoryName, reservation, ContractValidation.MaximumRelativePathCharacters,
                    policy.IsSafeRelativePayloadPath, operations.AfterPackageDirectoryCreated, out ownedTree);
                if (created != WindowsStableCustodyIssue.None || ownedTree is null)
                {
                    ManagedVersionInstallIssue issue = created == WindowsStableCustodyIssue.InvalidPath
                        ? ManagedVersionInstallIssue.UnsafeArchive : ManagedVersionInstallIssue.PromotionFailed;
                    return ownedTree is null ? Failure(issue) : FailureAfterCleanup(ownedTree, issue);
                }
                operations.BeforeExtraction?.Invoke();
                await RepositoryExtraction.ExtractAsync(plan, ownedTree, operations.WrapExtractionDestination,
                    cancellationToken).ConfigureAwait(false);
                await using (FileStream destination = ownedTree.CreateFile(AdmissionFileName))
                {
                    await destination.WriteAsync(admissionBytes, cancellationToken).ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                WindowsStableCustodyIssue prepared = ownedTree.PrepareForPromotion(operations.BeforePackagePromotion);
                if (prepared != WindowsStableCustodyIssue.None)
                {
                    return FailureAfterCleanup(ownedTree, ManagedVersionInstallIssue.InvalidPayload);
                }
                WindowsStableCustodyIssue promoted = ownedTree.Promote();
                if (promoted != WindowsStableCustodyIssue.None)
                {
                    return FailureAfterCleanup(ownedTree, promoted == WindowsStableCustodyIssue.Changed
                        ? ManagedVersionInstallIssue.IdentityConflict : ManagedVersionInstallIssue.PromotionFailed);
                }
                operations.AfterPromotion?.Invoke(target);
                WindowsStableCustodyResult captured = ownedTree.CapturePromotedImmutableTree(target, cancellationToken);
                if (!captured.IsAcquired)
                {
                    return FailureAfterRollback(ownedTree, ManagedVersionInstallIssue.IdentityConflict);
                }
                ManagedVersionInstallResult verified;
                using (WindowsStablePathCustody custody = captured.Custody!)
                {
                    verified = await VerifyExistingPayloadAsync(target, admission, wasAlreadyInstalled: false,
                        custody, cancellationToken).ConfigureAwait(false);
                }
                if (!verified.IsSuccess)
                {
                    return FailureAfterRollback(ownedTree, verified.Issue);
                }
                ownedTree.Dispose();
                ownedTree = null;
                return verified;
            }
            catch (OperationCanceledException)
            {
                if (ownedTree is not null && ownedTree.RollbackPromotionAndCleanup() != WindowsStableCustodyIssue.None)
                {
                    return Failure(ManagedVersionInstallIssue.CleanupIncomplete);
                }
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                if (ownedTree is not null && ownedTree.RollbackPromotionAndCleanup() != WindowsStableCustodyIssue.None)
                {
                    return Failure(ManagedVersionInstallIssue.CleanupIncomplete);
                }
                throw;
            }
            finally
            {
                ownedTree?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return Failure(ManagedVersionInstallIssue.PackageUnavailable);
        }
        catch (InvalidDataException)
        {
            return Failure(ManagedVersionInstallIssue.InvalidPayload);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(ManagedVersionInstallIssue.PromotionFailed);
        }
    }

    private async ValueTask<ManagedVersionInstallResult> VerifyExistingPayloadAsync(string target,
        ManagedVersionAdmission admission, bool wasAlreadyInstalled, WindowsStablePathCustody? heldCustody,
        CancellationToken cancellationToken)
    {
        WindowsStablePathCustody? acquiredCustody = null;
        if (heldCustody is null)
        {
            WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTree(target,
                TreeLimits, cancellationToken);
            if (!acquired.IsAcquired)
            {
                return Failure(ManagedVersionInstallIssue.IdentityConflict);
            }
            acquiredCustody = acquired.Custody!;
            heldCustody = acquiredCustody;
        }
        try
        {
            if (await ReadAdmissionAsync(target, cancellationToken).ConfigureAwait(false) != admission)
            {
                return Failure(ManagedVersionInstallIssue.IdentityConflict);
            }
            var damage = await proof.VerifyAsync(target, admission, cancellationToken).ConfigureAwait(false);
            return damage is null && heldCustody.RevalidateClosedTree()
                ? new(admission, ManagedVersionInstallIssue.None, wasAlreadyInstalled)
                : Failure(ManagedVersionInstallIssue.IdentityConflict);
        }
        finally
        {
            acquiredCustody?.Dispose();
        }
    }

    private static ManagedVersionInstallResult FailureAfterCleanup(WindowsStableRelativeWriteTree tree,
        ManagedVersionInstallIssue issue) => Failure(tree.Cleanup() == WindowsStableCustodyIssue.None
            ? issue : ManagedVersionInstallIssue.CleanupIncomplete);

    private static ManagedVersionInstallResult FailureAfterRollback(WindowsStableRelativeWriteTree tree,
        ManagedVersionInstallIssue issue) => Failure(tree.RollbackPromotionAndCleanup() == WindowsStableCustodyIssue.None
            ? issue : ManagedVersionInstallIssue.CleanupIncomplete);

    private static ManagedVersionInstallResult Failure(ManagedVersionInstallIssue issue) => new(null, issue, false);
}

internal static class RepositoryExtraction
{
    internal static ValueTask ExtractAsync(ManagedPackagePlan plan, WindowsStableRelativeWriteTree tree,
        Func<string, Stream, Stream>? wrapDestination, CancellationToken cancellationToken) =>
        plan.ExtractAsync(path =>
        {
            FileStream stream = tree.CreateFile(path);
            try
            {
                return wrapDestination?.Invoke(path, stream) ?? stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }, cancellationToken);
}
