// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Reads one exact Setup marker through stable no-follow Windows custody.</summary>
public sealed class FileSystemManagedSetupRecoveryProbe : IManagedSetupRecoveryProbe
{
    private readonly ManagedSetupMarkerIO _markerIO;
    private readonly Func<string, CancellationToken, WindowsStableCustodyResult> _acquire;
    private readonly Func<WindowsStablePathCustody, bool> _revalidate;

    /// <summary>Creates the production local filesystem adapter.</summary>
    public FileSystemManagedSetupRecoveryProbe(IManagedSetupMarkerCodec markerCodec, int maximumMarkerBytes, int maximumDescriptorBytes)
        : this(markerCodec, maximumMarkerBytes, maximumDescriptorBytes,
            static (path, cancellationToken) =>
                WindowsStablePathCustody.TryAcquireFile(path, cancellationToken: cancellationToken),
            static custody => custody.RevalidateClosedTree())
    {
    }

    internal FileSystemManagedSetupRecoveryProbe(
        IManagedSetupMarkerCodec markerCodec, int maximumMarkerBytes, int maximumDescriptorBytes,
        Func<string, CancellationToken, WindowsStableCustodyResult> acquire,
        Func<WindowsStablePathCustody, bool> revalidate)
    {
        _markerIO = new(markerCodec, maximumMarkerBytes, maximumDescriptorBytes);
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        _revalidate = revalidate ?? throw new ArgumentNullException(nameof(revalidate));
    }

    /// <inheritdoc />
    public ValueTask<ManagedSetupRecoveryFact> ObserveAsync(
        string managedRoot,
        string statePathIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RepositoryPathSafety.TryNormalizeExactAbsolutePath(managedRoot, out string root) ||
            !RepositoryPathSafety.TryNormalizeExactAbsolutePath(statePathIdentity, out string statePath))
        {
            return ValueTask.FromResult(Fact(ManagedSetupRecoveryFactKind.IdentityMismatch));
        }

        string markerPath = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(root);
        WindowsStableCustodyResult acquired = _acquire(markerPath, cancellationToken);
        if (!acquired.IsAcquired)
        {
            ManagedSetupRecoveryFactKind kind = acquired.IsExactChildMissing
                ? ManagedSetupRecoveryFactKind.Absent
                : MapCustodyIssue(acquired.Issue);
            acquired.Custody?.Dispose();
            return ValueTask.FromResult(Fact(kind));
        }

        return ObserveHeldAsync(
            acquired.Custody!,
            Path.GetFileName(markerPath),
            root,
            statePath,
            cancellationToken);
    }

    private async ValueTask<ManagedSetupRecoveryFact> ObserveHeldAsync(
        WindowsStablePathCustody custody,
        string markerName,
        string root,
        string statePath,
        CancellationToken cancellationToken)
    {
        using (custody)
        {
            try
            {
                await using FileStream stream = custody.OpenReadOnlyFile(markerName);
                ManagedSetupMarker? marker = await _markerIO
                    .ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                return marker is null
                    ? Fact(ManagedSetupRecoveryFactKind.Malformed)
                    : !_revalidate(custody)
                    ? Fact(ManagedSetupRecoveryFactKind.Changed)
                    : TryProjectExact(
                        marker,
                        root,
                        statePath,
                        out ManagedSetupRecoveryTransaction? exact)
                            ? new(ManagedSetupRecoveryFactKind.Exact, exact)
                            : Fact(ManagedSetupRecoveryFactKind.IdentityMismatch);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                return Fact(ManagedSetupRecoveryFactKind.AccessDenied);
            }
            catch (Exception exception) when (exception is
                IOException or InvalidDataException or NotSupportedException)
            {
                return Fact(ManagedSetupRecoveryFactKind.Unavailable);
            }
        }
    }

    private static bool TryProjectExact(
        ManagedSetupMarker marker,
        string root,
        string statePath,
        out ManagedSetupRecoveryTransaction? transaction)
    {
        transaction = null;
        if (!RepositoryPathSafety.PathComparer.Equals(marker.ManagedRootIdentity, root) ||
            !RepositoryPathSafety.PathComparer.Equals(marker.StatePathIdentity, statePath) ||
            !TryMapPhase(marker.Phase, out ManagedSetupRecoveryPhase phase))
        {
            return false;
        }

        string[] ownedPaths =
        [
            Path.GetFileName(root),
            Path.GetFileName(FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(root)),
            string.Join(
                '/',
                Path.GetFileName(FileSystemManagedInstallationRootProbe.GetStagingContainerPath(root)),
                marker.TransactionId),
        ];
        if (!marker.OwnedPaths.SequenceEqual(ownedPaths, StringComparer.Ordinal))
        {
            return false;
        }

        transaction = new(
            marker.TransactionId,
            root,
            statePath,
            phase,
            ownedPaths,
            marker.Payload,
            marker.Candidate);
        return true;
    }

    private static bool TryMapPhase(string value, out ManagedSetupRecoveryPhase phase)
    {
        phase = value switch
        {
            ManagedSetupMarkerIO.StagingPhase => ManagedSetupRecoveryPhase.Staging,
            ManagedSetupMarkerIO.RootPromotedPhase => ManagedSetupRecoveryPhase.RootPromoted,
            ManagedSetupMarkerIO.BootstrapLaunchRecordedPhase =>
                ManagedSetupRecoveryPhase.BootstrapLaunchRecorded,
            _ => default,
        };
        return value is ManagedSetupMarkerIO.StagingPhase or
            ManagedSetupMarkerIO.RootPromotedPhase or
            ManagedSetupMarkerIO.BootstrapLaunchRecordedPhase;
    }

    private static ManagedSetupRecoveryFactKind MapCustodyIssue(WindowsStableCustodyIssue issue)
    {
        return issue switch
        {
            WindowsStableCustodyIssue.AccessDenied => ManagedSetupRecoveryFactKind.AccessDenied,
            WindowsStableCustodyIssue.Changed => ManagedSetupRecoveryFactKind.Changed,
            WindowsStableCustodyIssue.Contended => ManagedSetupRecoveryFactKind.Unavailable,
            WindowsStableCustodyIssue.InvalidPath or WindowsStableCustodyIssue.ReparsePoint =>
                ManagedSetupRecoveryFactKind.IdentityMismatch,
            WindowsStableCustodyIssue.Unavailable => ManagedSetupRecoveryFactKind.Unavailable,
            WindowsStableCustodyIssue.None => ManagedSetupRecoveryFactKind.Unavailable,
            _ => ManagedSetupRecoveryFactKind.Unavailable,
        };
    }

    private static ManagedSetupRecoveryFact Fact(ManagedSetupRecoveryFactKind kind)
    {
        return new(kind, transaction: null);
    }
}
