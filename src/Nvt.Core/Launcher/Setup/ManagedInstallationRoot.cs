// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Setup;

/// <summary>Complete local observation of the selected first-install root.</summary>
public enum ManagedInstallationRootStatus
{
    /// <summary>The canonical root and every Setup residue path are absent.</summary>
    Absent,
    /// <summary>A root exists and is not owned by the current uninterrupted transaction.</summary>
    Present,
    /// <summary>A transaction marker or staging residue exists.</summary>
    Residue,
    /// <summary>The selected root is not an admitted local non-reparse destination.</summary>
    InvalidDestination,
    /// <summary>The current user cannot inspect the complete root fact.</summary>
    PermissionDenied,
    /// <summary>The complete root fact could not be observed.</summary>
    Unavailable,
}

/// <summary>Read-only root observation used before any Setup source access.</summary>
public sealed record ManagedInstallationRootObservation(ManagedInstallationRootStatus Status);

/// <summary>
/// Read-only infrastructure port for one exact root observation without discovery.
/// Implementations must return their ValueTask promptly and honor cancellation; the entry
/// coordinator additionally isolates and abandons the wait at its hard local-health cutoff.
/// </summary>
public interface IManagedInstallationRootProbe
{
    /// <summary>Observes only the exact normalized root and known transaction paths.</summary>
    ValueTask<ManagedInstallationRootObservation> ObserveAsync(
        string managedRoot,
        CancellationToken cancellationToken);
}

