// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Setup;

/// <summary>Stable issue from atomically materializing one fresh managed root.</summary>
public enum ManagedFirstInstallationMaterializationIssue
{
    /// <summary>The exact closed root was atomically promoted.</summary>
    None,
    /// <summary>The destination is not an admitted local non-reparse root.</summary>
    InvalidDestination,
    /// <summary>The current user cannot materialize the selected root.</summary>
    PermissionDenied,
    /// <summary>The exact source package could not be observed completely.</summary>
    SourceUnavailable,
    /// <summary>The captured package no longer matches its admitted identity.</summary>
    SourceChanged,
    /// <summary>Staging or same-volume promotion did not complete.</summary>
    PromotionFailed,
    /// <summary>Existing or residual root facts require the recovery owner.</summary>
    RecoveryRequired,
    /// <summary>The complete transaction fact could not be persisted or verified.</summary>
    StateUnavailable,
}

/// <summary>Stable marker-operation result for one already promoted installation.</summary>
public enum ManagedFirstInstallationTransactionIssue
{
    /// <summary>The exact transaction phase was durably advanced or completed.</summary>
    None,
    /// <summary>The marker/root facts differ and require recovery.</summary>
    RecoveryRequired,
    /// <summary>The exact transaction fact could not be observed or persisted.</summary>
    StateUnavailable,
}

/// <summary>Opaque promoted-root transaction retained until Bootstrap READY finalization.</summary>
/// <remarks>Disposal releases custody and retains the durable recovery marker. Only qualified completion removes it.</remarks>
public interface IManagedPromotedFirstInstallation : IDisposable
{
    /// <summary>Gets the exact promoted managed root.</summary>
    string ManagedRoot { get; }
    /// <summary>Gets the exact admitted seed version.</summary>
    ManagedVersionAdmission Admission { get; }
    /// <summary>Records the irreversible pre-process phase under the state writer lease.</summary>
    ValueTask<ManagedFirstInstallationTransactionIssue> RecordBootstrapLaunchAsync(
        CancellationToken cancellationToken);
    /// <summary>
    /// Consumes the one post-record launch opportunity and returns independently owned custody
    /// for the exact promoted Bootstrap without reacquiring custody from its managed path.
    /// </summary>
    ValueTask<ManagedExecutableLaunchLeaseResult> AcquireBootstrapLaunchLeaseAsync(
        ManagedImmutableBootstrapIdentity expectedIdentity,
        CancellationToken cancellationToken);
    /// <summary>Removes only the exact marker after durable bound READY was proved.</summary>
    ValueTask<ManagedFirstInstallationTransactionIssue> CompleteAsync(
        CancellationToken cancellationToken);
}

/// <summary>Typed result from one atomic first-install root materialization.</summary>
public sealed record ManagedFirstInstallationMaterializationResult(
    IManagedPromotedFirstInstallation? Installation,
    ManagedFirstInstallationMaterializationIssue Issue)
{
    /// <summary>Gets whether one complete promoted root and exact marker are available.</summary>
    public bool IsSuccess =>
        Installation is not null && Issue == ManagedFirstInstallationMaterializationIssue.None;
}

/// <summary>Closed stage projected by the single first-install execution owner.</summary>
public enum ManagedFirstInstallationProgressStage
{
    /// <summary>The exact Registry, Catalog, and package token is being revalidated.</summary>
    RevalidatingSource,
    /// <summary>The admitted package bytes are being read and hashed from the update source.</summary>
    ReadingPackage,
    /// <summary>The admitted archive members are being verified without mutation.</summary>
    VerifyingPackage,
    /// <summary>The verified archive members are being copied into the held staging tree.</summary>
    InstallingPackage,
    /// <summary>The staged installed payload bytes are being verified.</summary>
    VerifyingInstallation,
    /// <summary>The verified root transaction is being finalized.</summary>
    FinalizingInstallation,
    /// <summary>The immutable Bootstrap and installed application are starting.</summary>
    StartingApplication,
}

/// <summary>
/// One immutable current first-install progress fact. Determinate work is stage-local and comes
/// from the authoritative operation; unrelated stages are never aggregated into an estimate.
/// </summary>
public readonly record struct ManagedFirstInstallationProgress
{
    /// <summary>Creates one validated determinate or indeterminate progress snapshot.</summary>
    public ManagedFirstInstallationProgress(
        ManagedFirstInstallationProgressStage stage,
        long completedWork,
        long? totalWork)
    {
        if (!Enum.IsDefined(stage) ||
            completedWork < 0 ||
            totalWork is <= 0 ||
            (totalWork is null && completedWork != 0) ||
            (totalWork is { } total && completedWork > total))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedWork),
                "First-install progress requires a defined stage and valid completed/total work.");
        }

        Stage = stage;
        CompletedWork = completedWork;
        TotalWork = totalWork;
    }

    /// <summary>Gets the current closed stage.</summary>
    public ManagedFirstInstallationProgressStage Stage { get; }

    /// <summary>Gets completed bytes/items within the current stage.</summary>
    public long CompletedWork { get; }

    /// <summary>Gets total bytes/items when the authoritative operation knows that total.</summary>
    public long? TotalWork { get; }

    /// <summary>Gets one stage-local integer percentage, or null when the total is unknown.</summary>
    public int? Percent => TotalWork is { } total
        ? decimal.ToInt32(decimal.Floor(CompletedWork * 100m / total))
        : null;

    /// <summary>Creates one truthful unknown-total stage.</summary>
    public static ManagedFirstInstallationProgress Indeterminate(
        ManagedFirstInstallationProgressStage stage)
    {
        return new(stage, 0, null);
    }
}

/// <summary>Infrastructure-owned whole-root transaction that reuses the package repository.</summary>
public interface IManagedFirstInstallationRootMaterializer
{
    /// <summary>
    /// Proves that the exact destination parent is a writable local non-reparse directory.
    /// This operation leaves no persistent filesystem entry.
    /// </summary>
    ValueTask<ManagedFirstInstallationMaterializationIssue> AdmitDestinationAsync(
        string managedRoot,
        CancellationToken cancellationToken);

    /// <summary>Stages, verifies, and atomically promotes one exact fresh root.</summary>
    ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        string managedRoot,
        string statePathIdentity,
        IManagedDistributionPayloadCapture payload,
        FreshInstallationCandidate candidate,
        VersionManagerState seed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages, verifies, and atomically promotes one exact fresh root while projecting only
    /// authoritative progress from that same operation.
    /// </summary>
    ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
        string managedRoot,
        string statePathIdentity,
        IManagedDistributionPayloadCapture payload,
        FreshInstallationCandidate candidate,
        VersionManagerState seed,
        IProgress<ManagedFirstInstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return MaterializeAsync(
            managedRoot,
            statePathIdentity,
            payload,
            candidate,
            seed,
            cancellationToken);
    }
}

