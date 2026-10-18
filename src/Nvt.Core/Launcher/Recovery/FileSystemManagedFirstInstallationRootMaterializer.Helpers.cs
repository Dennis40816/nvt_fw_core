// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Files.Windows;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedFirstInstallationRootMaterializer
{
    internal WindowsStableTreeLimits CreateSetupTreeLimits(
        string root,
        ManagedDistributionPayloadIdentity payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return CreateSetupTreeLimits(
            root,
            payload.LauncherSize,
            payload.Bootstrap.Length);
    }

    internal WindowsStableTreeLimits CreateSetupTreeLimits(
        string root,
        ManagedSetupRecoveryPayloadIdentity payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return CreateSetupTreeLimits(
            root,
            payload.LauncherSize,
            payload.BootstrapSize);
    }

    private WindowsStableTreeLimits CreateSetupTreeLimits(
        string root,
        long launcherSize,
        long bootstrapSize)
    {
        long seedBytes = new FileInfo(Path.Combine(root, _layout.SeedFileName)).Length;
        return new WindowsStableTreeLimits(
            checked(_payloadTreeLimits.MaximumFiles + 3),
            checked(_payloadTreeLimits.MaximumDirectories + 2),
            checked(
                _payloadTreeLimits.MaximumBytes +
                launcherSize +
                bootstrapSize +
                seedBytes));
    }

    private ValueTask<bool> VerifyClosedRootAsync(
        string root,
        ManagedDistributionPayloadIdentity payload,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        return new ManagedSetupClosedRootVerifier(_repository, _layout, _seedStoreFactory).VerifyAsync(
            root,
            payload,
            admission,
            cancellationToken);
    }

    private ValueTask<bool> VerifyStagedRootAsync(
        string root,
        ManagedDistributionPayloadIdentity payload,
        ManagedVersionAdmission admission,
        CancellationToken cancellationToken)
    {
        return ManagedSetupClosedRootVerifier.VerifyPayloadAndSeedAsync(
            _layout, _seedStoreFactory, root,
            payload,
            admission,
            cancellationToken);
    }

    private static ValueTask<bool> MatchesAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        return ManagedSetupClosedRootVerifier.MatchesAsync(
            path,
            expectedSize,
            expectedSha256,
            cancellationToken);
    }

    private static ManagedFirstInstallationMaterializationIssue MapInstallIssue(
        ManagedVersionInstallIssue issue)
    {
        return issue switch
        {
            ManagedVersionInstallIssue.PackageUnavailable =>
                ManagedFirstInstallationMaterializationIssue.SourceUnavailable,
            ManagedVersionInstallIssue.PackageMismatch or
            ManagedVersionInstallIssue.UnsafeArchive or
            ManagedVersionInstallIssue.InvalidPayload or
            ManagedVersionInstallIssue.IdentityConflict =>
                ManagedFirstInstallationMaterializationIssue.SourceChanged,
            ManagedVersionInstallIssue.PromotionFailed =>
                ManagedFirstInstallationMaterializationIssue.PromotionFailed,
            ManagedVersionInstallIssue.CleanupIncomplete =>
                ManagedFirstInstallationMaterializationIssue.RecoveryRequired,
            ManagedVersionInstallIssue.StateUnavailable =>
                ManagedFirstInstallationMaterializationIssue.StateUnavailable,
            ManagedVersionInstallIssue.None =>
                ManagedFirstInstallationMaterializationIssue.SourceChanged,
            _ => throw new InvalidOperationException("Repository returned an undefined install issue."),
        };
    }

    internal static ManagedFirstInstallationMaterializationIssue MapObservedRoot(
        ManagedInstallationRootStatus status)
    {
        return status switch
        {
            ManagedInstallationRootStatus.InvalidDestination =>
                ManagedFirstInstallationMaterializationIssue.InvalidDestination,
            ManagedInstallationRootStatus.PermissionDenied =>
                ManagedFirstInstallationMaterializationIssue.PermissionDenied,
            ManagedInstallationRootStatus.Unavailable =>
                ManagedFirstInstallationMaterializationIssue.StateUnavailable,
            ManagedInstallationRootStatus.Absent or
            ManagedInstallationRootStatus.Present or
            ManagedInstallationRootStatus.Residue =>
                ManagedFirstInstallationMaterializationIssue.RecoveryRequired,
            _ => throw new InvalidOperationException("Root probe returned an undefined status."),
        };
    }

    private static async ValueTask<bool> RemoveEmptyRepositoryStagingAsync(
        ManagedSetupDestinationCustody custody,
        string staging,
        Action<string>? beforeDelete,
        Action<int, WindowsSetupStagingCleanupState>? attemptObserved,
        Func<TimeSpan, CancellationToken, ValueTask>? cleanupDelay,
        CancellationToken cancellationToken)
    {
        WindowsSetupStagingCleanupResult observed = custody.ObserveEmptyStagingChild(staging);
        if (observed.State == WindowsSetupStagingCleanupState.Absent)
        {
            return true;
        }
        if (observed.State != WindowsSetupStagingCleanupState.Observed)
        {
            return false;
        }

        const int maximumAttempts = 20;
        bool ownedDeletionPending = false;
        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WindowsSetupStagingCleanupResult result = ownedDeletionPending
                ? custody.ObserveOwnedStagingDeletion(staging, observed.Identity)
                : custody.DeleteObservedEmptyStagingChild(
                    staging,
                    observed.Identity,
                    beforeDelete);
            attemptObserved?.Invoke(attempt + 1, result.State);
            if (result.State is WindowsSetupStagingCleanupState.Deleted or
                WindowsSetupStagingCleanupState.Absent)
            {
                return true;
            }
            if (result.State == WindowsSetupStagingCleanupState.OwnedDeletionPending)
            {
                ownedDeletionPending = true;
            }
            else if (result.State != WindowsSetupStagingCleanupState.RetryableContention)
            {
                return false;
            }
            if (attempt + 1 < maximumAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(250);
                if (cleanupDelay is null)
                {
                    await Task.Delay(delay, TimeProvider.System, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await cleanupDelay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        return false;
    }

    // A failed write can leave buffered bytes that fail again here. The root may already be promoted, so the
    // typed result already chosen must survive. Fatal exceptions still propagate.
    internal static async ValueTask DisposeMarkerStreamAsync(Stream stream, Action<Exception>? observerDiagnostics)
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (SetupExceptionPolicy.IsNonFatal(exception))
        {
            SetupExceptionPolicy.ReportDiagnostics(exception, observerDiagnostics);
        }
    }

    private static async ValueTask<MarkerCreateResult> CreateNewMarkerAsync(
        ManagedSetupDestinationCustody custody,
        string markerName,
        ReadOnlyMemory<byte> bytes,
        Action<ManagedSetupBoundary>? boundaryReached,
        CancellationToken cancellationToken)
    {
        ManagedFirstInstallationMaterializationIssue created = custody.CreateMarker(
            markerName,
            out SafeFileHandle handle);
        if (created != ManagedFirstInstallationMaterializationIssue.None)
        {
            handle.Dispose();
            return new(null, created);
        }
        SafeFileHandle? ownedHandle = handle;
        FileStream? stream = null;
        try
        {
            stream = new FileStream(ownedHandle, FileAccess.ReadWrite, bufferSize: 16 * 1024, isAsync: false);
            ownedHandle = null;
            boundaryReached?.Invoke(ManagedSetupBoundary.BeforeStagingMarkerWrite);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            FlushToDisk(stream);
            stream.Position = 0;
            boundaryReached?.Invoke(ManagedSetupBoundary.AfterStagingMarkerWrite);
            var result = new MarkerCreateResult(null, ManagedFirstInstallationMaterializationIssue.None) { Stream = stream };
            stream = null;
            return result;
        }
        finally
        {
            ownedHandle?.Dispose();
            if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);

    private sealed record MarkerCreateResult(
        FileStream? Stream,
        ManagedFirstInstallationMaterializationIssue Issue)
    {
        internal bool IsSuccess => Stream is not null &&
            Issue == ManagedFirstInstallationMaterializationIssue.None;
    }

    private static async ValueTask<ExactMarkerDeleteResult> DeleteExactMarkerAsync(
        ManagedSetupMarkerIO markerIO,
        FileStream stream,
        ManagedSetupMarker expected,
        Func<CancellationToken, ValueTask<bool>> revalidateRoot,
        Func<bool> revalidateTopology,
        Action? afterTopologyProof,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsStablePathCustody.HasExpectedType(stream.SafeFileHandle, directory: false))
        {
            return ExactMarkerDeleteResult.Mismatch;
        }

        ManagedSetupMarker? actual;
        try
        {
            actual = await markerIO.ReadAsync(stream, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or InvalidOperationException)
        {
            return ExactMarkerDeleteResult.Mismatch;
        }
        if (actual is null || !ManagedSetupMarker.Equivalent(actual, expected))
        {
            return ExactMarkerDeleteResult.Mismatch;
        }
        if (!await revalidateRoot(cancellationToken).ConfigureAwait(false) ||
            !revalidateTopology())
        {
            return ExactMarkerDeleteResult.Mismatch;
        }
        if (afterTopologyProof is not null)
        {
            afterTopologyProof();
            if (!revalidateTopology())
            {
                return ExactMarkerDeleteResult.Mismatch;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return WindowsStablePathCustody.MarkDeleteOnClose(stream.SafeFileHandle)
                ? ExactMarkerDeleteResult.Deleted
                : ExactMarkerDeleteResult.Unavailable;
    }

    private static async ValueTask ReplaceExactMarkerAsync(
        ManagedSetupMarkerIO markerIO,
        FileStream stream,
        ManagedSetupMarker expected,
        ManagedSetupMarker replacement,
        Func<bool> revalidateTopology,
        Action? afterTopologyProof,
        Action<ManagedSetupBoundary>? boundaryReached,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> bytes = markerIO.Serialize(replacement);
        ManagedSetupMarker? actual = await markerIO.ReadAsync(
            stream,
            cancellationToken).ConfigureAwait(false);
        if (actual is null || !ManagedSetupMarker.Equivalent(actual, expected))
        {
            throw new InvalidDataException("Setup marker identity or phase changed.");
        }
        if (!revalidateTopology())
        {
            throw new InvalidDataException("Setup root topology changed after verification.");
        }
        if (afterTopologyProof is not null)
        {
            afterTopologyProof();
            if (!revalidateTopology())
            {
                throw new InvalidDataException("Setup root topology changed at marker mutation.");
            }
        }
        boundaryReached?.Invoke(replacement.Phase == ManagedSetupMarkerIO.RootPromotedPhase
            ? ManagedSetupBoundary.BeforeRootPromotedMarkerWrite : ManagedSetupBoundary.BeforeBootstrapLaunchMarkerWrite);
        stream.Position = 0;
        stream.SetLength(0);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        FlushToDisk(stream);
        boundaryReached?.Invoke(replacement.Phase == ManagedSetupMarkerIO.RootPromotedPhase
            ? ManagedSetupBoundary.AfterRootPromotedMarkerWrite : ManagedSetupBoundary.AfterBootstrapLaunchMarkerWrite);
    }

    private static ManagedFirstInstallationMaterializationResult Failure(
        ManagedFirstInstallationMaterializationIssue issue)
    {
        return new(null, issue);
    }
}
