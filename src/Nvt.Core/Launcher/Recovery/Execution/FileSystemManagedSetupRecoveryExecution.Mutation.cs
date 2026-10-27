// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedSetupRecoveryExecution
{
    /// <inheritdoc />
    public async ValueTask<ManagedSetupRecoveryExecutionPortOutcome> ExecuteAsync(
        ManagedSetupRecoveryExecutionRequest request,
        VersionManagerWriteLeaseResult writerLease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writerLease);
        var progress = new RecoveryMutationProgress();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!writerLease.HoldsStatePath(_statePath))
            {
                return ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable;
            }
            if (request.ExecutionToken is not FileSystemRecoveryToken token ||
                !RepositoryPathSafety.PathComparer.Equals(token.StatePath, _statePath))
            {
                return ManagedSetupRecoveryExecutionPortOutcome.SourceChanged;
            }

            bool rollback = request.Action ==
                ManagedSetupRecoveryAction.RemoveIncompleteInstallation;
            ManagedSetupRecoveryExecutionPortOutcome? preflight = rollback
                ? await RevalidateStaticSnapshotAsync(
                    token,
                    token.ApplicationStateIdentity,
                    token.LauncherStateIdentity,
                    cancellationToken,
                    validateTreeAndStaging: false).ConfigureAwait(false)
                : await RevalidateTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (preflight is not null)
            {
                return preflight.Value;
            }
            return await ExecuteOrderedAsync(token, request.Action, rollback, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return progress.Mutated
                ? ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired
                : ManagedSetupRecoveryExecutionPortOutcome.Cancelled;
        }
        catch (UnauthorizedAccessException)
        {
            return ManagedSetupRecoveryExecutionPortOutcome.PermissionDenied;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return progress.Mutated
                ? ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired
                : ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable;
        }
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome> ExecuteOrderedAsync(
        FileSystemRecoveryToken token, ManagedSetupRecoveryAction action, bool rollback,
        RecoveryMutationProgress progress, CancellationToken cancellationToken)
    {
        WindowsStablePathCustody? custody = null;
        try
        {
            if (rollback)
            {
                PreparedRollbackTree prepared = await PrepareRollbackTreeAsync(token, cancellationToken).ConfigureAwait(false);
                if (prepared.Issue is { } issue) return issue;
                custody = prepared.Custody;
                var states = await DeleteRollbackStatesAsync(token, progress, cancellationToken).ConfigureAwait(false);
                if (states is { } stateIssue) return stateIssue;
                var tree = await DeleteTreeAsync(token, custody, progress, cancellationToken).ConfigureAwait(false);
                if (tree is { } treeIssue) return treeIssue;
                custody?.Dispose();
                custody = null;
            }
            return await CompleteResidueAsync(token, action, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            custody?.Dispose();
        }
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome?> DeleteRollbackStatesAsync(
        FileSystemRecoveryToken token, RecoveryMutationProgress progress, CancellationToken cancellationToken)
    {
        ManagedSetupRecoveryExecutionPortOutcome? launcherDelete =
            DeleteExactFileIfPresent(
                _launcherStatePath,
                token.LauncherStateIdentity,
                progress,
                cancellationToken);
        if (launcherDelete is not null)
        {
            return launcherDelete.Value;
        }
        ManagedSetupRecoveryExecutionPortOutcome? afterLauncher =
            await RevalidateStaticSnapshotAsync(
                token,
                token.ApplicationStateIdentity,
                "missing",
                cancellationToken,
                validateTreeAndStaging: false).ConfigureAwait(false);
        if (afterLauncher is not null)
        {
            return afterLauncher.Value;
        }
        ManagedSetupRecoveryExecutionPortOutcome? appDelete = DeleteExactFileIfPresent(
            _statePath,
            token.ApplicationStateIdentity,
            progress,
            cancellationToken);
        if (appDelete is not null)
        {
            return appDelete.Value;
        }
        ManagedSetupRecoveryExecutionPortOutcome? afterApplication =
            await RevalidateStaticSnapshotAsync(
                token,
                "missing",
                "missing",
                cancellationToken,
                validateTreeAndStaging: false).ConfigureAwait(false);
        if (afterApplication is not null)
        {
            return afterApplication.Value;
        }
        return null;
    }

    private async ValueTask<ManagedSetupRecoveryExecutionPortOutcome> CompleteResidueAsync(
        FileSystemRecoveryToken token, ManagedSetupRecoveryAction action,
        RecoveryMutationProgress progress, CancellationToken cancellationToken)
    {
        ManagedSetupRecoveryExecutionPortOutcome? stagingDelete =
            await DeleteEmptyStagingAsync(
                token,
                progress,
                cancellationToken).ConfigureAwait(false);
        if (stagingDelete is not null)
        {
            return stagingDelete.Value;
        }
        ManagedSetupRecoveryExecutionPortOutcome? postcondition =
            await RevalidateFinalPostconditionAsync(
                token,
                action,
                cancellationToken).ConfigureAwait(false);
        if (postcondition is not null)
        {
            return postcondition.Value;
        }
        ManagedSetupRecoveryExecutionPortOutcome? markerDelete = DeleteExactFileIfPresent(
            FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(token.ManagedRoot),
            token.MarkerIdentity,
            progress,
            cancellationToken,
            missingIsComplete: false);
        if (markerDelete is not null)
        {
            return markerDelete.Value;
        }
        ManagedSetupRecoveryExecutionPortOutcome? terminal =
            await RevalidateActionPostconditionAsync(
                token,
                action,
                CancellationToken.None).ConfigureAwait(false);
        return terminal is not null ||
            !WindowsStablePathCustody.TryObserveExactMissingFile(
                FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(
                    token.ManagedRoot),
                CancellationToken.None)
            ? terminal ?? ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired
            : ManagedSetupRecoveryExecutionPortOutcome.Completed;
    }
}
