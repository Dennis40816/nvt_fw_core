// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Windows;
using Nvt.Core.Files.Windows;
using System.Text.Json;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedFirstInstallationRootMaterializer
{
    private sealed class PromotedInstallation(
        string managedRoot,
        ManagedVersionAdmission admission,
        ManagedImmutableBootstrapIdentity bootstrapIdentity,
        ManagedSetupMarker marker,
        FileStream markerStream,
        ManagedSetupDestinationCustody custody,
        ManagedSetupMarkerIO markerIO, long maximumExecutableBytes, Action<Exception>? observerDiagnostics, Action<ManagedSetupBoundary>? boundaryReached,
        Func<CancellationToken, ValueTask<bool>> revalidateRoot,
        Action? afterMarkerTopologyProof)
        : IManagedPromotedFirstInstallation
    {
        private readonly ManagedImmutableBootstrapIdentity _bootstrapIdentity =
            bootstrapIdentity ?? throw new ArgumentNullException(nameof(bootstrapIdentity));
        private int _launchLeaseClaimed;

        public string ManagedRoot { get; } = managedRoot;
        public ManagedVersionAdmission Admission { get; } = admission;

        public void Dispose()
        {
            try { markerStream.Dispose(); }
            finally { custody.Dispose(); }
        }

        public async ValueTask<ManagedFirstInstallationTransactionIssue> RecordBootstrapLaunchAsync(
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(ManagedRoot) ||
                !string.Equals(marker.Phase, RootPromotedPhase, StringComparison.Ordinal))
            {
                return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
            }
            try
            {
                if (!await revalidateRoot(cancellationToken).ConfigureAwait(false))
                {
                    return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
                }
                if (!custody.RevalidateClosedTree())
                {
                    return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
                }
                ManagedSetupMarker next = marker with
                {
                    Phase = BootstrapLaunchRecordedPhase,
                };
                await ReplaceExactMarkerAsync(markerIO,
                        markerStream,
                        marker,
                        next,
                        custody.RevalidateClosedTree,
                        afterMarkerTopologyProof,
                        boundaryReached, cancellationToken)
                    .ConfigureAwait(false);
                marker = next;
                return ManagedFirstInstallationTransactionIssue.None;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
            }
            catch (Exception exception) when (SetupExceptionPolicy.IsNonFatal(exception))
            {
                SetupExceptionPolicy.ReportDiagnostics(exception, observerDiagnostics);
                return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
            }
        }

        public async ValueTask<ManagedExecutableLaunchLeaseResult> AcquireBootstrapLaunchLeaseAsync(
            ManagedImmutableBootstrapIdentity expectedIdentity,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(expectedIdentity);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(marker.Phase, BootstrapLaunchRecordedPhase, StringComparison.Ordinal) ||
                Interlocked.Exchange(ref _launchLeaseClaimed, 1) != 0)
            {
                return new(null, ManagedExecutableLaunchIssue.Unavailable);
            }
            if (expectedIdentity != _bootstrapIdentity)
            {
                return new(null, ManagedExecutableLaunchIssue.UnsafePath);
            }

            WindowsStableCustodyResult cloned = custody.TryCloneClosedTree(cancellationToken);
            if (!cloned.IsAcquired)
            {
                cloned.Custody?.Dispose();
                return new(null, MapLaunchCustodyIssue(cloned.Issue));
            }
            return await StableManagedExecutableLaunchLease.TryCreateFromVerifiedTreeAsync(
                    cloned.Custody!,
                    expectedIdentity.FileName,
                    expectedIdentity.Length,
                    expectedIdentity.Sha256,
                    maximumExecutableBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ManagedFirstInstallationTransactionIssue> CompleteAsync(
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(ManagedRoot) ||
                !string.Equals(marker.Phase, BootstrapLaunchRecordedPhase, StringComparison.Ordinal))
            {
                return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
            }
            try
            {
                ExactMarkerDeleteResult deleted = await DeleteExactMarkerAsync(markerIO,
                    markerStream,
                    marker,
                    revalidateRoot,
                    custody.RevalidateClosedTree,
                    afterMarkerTopologyProof,
                    cancellationToken).ConfigureAwait(false);
                return deleted switch
                {
                    ExactMarkerDeleteResult.Deleted => ManagedFirstInstallationTransactionIssue.None,
                    ExactMarkerDeleteResult.Mismatch =>
                        ManagedFirstInstallationTransactionIssue.RecoveryRequired,
                    ExactMarkerDeleteResult.Unavailable =>
                        ManagedFirstInstallationTransactionIssue.RecoveryRequired,
                    _ => throw new InvalidOperationException(
                        "Exact marker deletion returned an undefined result."),
                };
            }
            catch (UnauthorizedAccessException)
            {
                return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
            }
            catch (IOException)
            {
                return ManagedFirstInstallationTransactionIssue.RecoveryRequired;
            }
        }

        private static ManagedExecutableLaunchIssue MapLaunchCustodyIssue(
            WindowsStableCustodyIssue issue)
        {
            return issue switch
            {
                WindowsStableCustodyIssue.InvalidPath or
                WindowsStableCustodyIssue.ReparsePoint or
                WindowsStableCustodyIssue.Changed => ManagedExecutableLaunchIssue.UnsafePath,
                WindowsStableCustodyIssue.AccessDenied or
                WindowsStableCustodyIssue.Contended or
                WindowsStableCustodyIssue.Unavailable => ManagedExecutableLaunchIssue.Unavailable,
                WindowsStableCustodyIssue.None => throw new InvalidOperationException(
                    "Successful cloned custody did not return its owner."),
                _ => throw new InvalidOperationException(
                    "Cloned Bootstrap custody returned an undefined issue."),
            };
        }
    }

    private enum ExactMarkerDeleteResult
    {
        Deleted,
        Mismatch,
        Unavailable,
    }

}
