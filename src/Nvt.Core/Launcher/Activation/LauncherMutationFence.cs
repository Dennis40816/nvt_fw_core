// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Stable launcher-journal read category used by app mutation policy.</summary>
public enum LauncherMutationFenceIssue
{
    /// <summary>The journal is readable or absent and the typed projection is available.</summary>
    None,
    /// <summary>The journal is malformed or violates its protocol contract.</summary>
    Invalid,
    /// <summary>The journal cannot be read or durably changed.</summary>
    Unavailable,
}

/// <summary>Exact launcher-owner admissions that app mutation must protect.</summary>
public sealed record LauncherMutationProtection(
    LauncherMutationFenceIssue Issue,
    bool HasPendingActivation,
    ManagedVersionAdmission? ActiveOwner,
    ManagedVersionAdmission? LastKnownGoodOwner,
    IReadOnlyList<ManagedVersionAdmission> PendingOwners)
{
    /// <summary>Gets whether ordinary app mutation may proceed.</summary>
    public bool IsClear => Issue == LauncherMutationFenceIssue.None && !HasPendingActivation;

    /// <summary>Gets whether an exact admission owns active or pending launcher authority.</summary>
    public bool IsHardProtected(ManagedVersionAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return admission == ActiveOwner || PendingOwners.Contains(admission);
    }

    /// <summary>Gets whether an exact admission owns only rollback authority.</summary>
    public bool IsLastKnownGoodOnly(ManagedVersionAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return admission == LastKnownGoodOwner && !IsHardProtected(admission);
    }

}

/// <summary>Port projecting and retiring launcher ownership under the caller-held app-state lease.</summary>
public interface ILauncherMutationFence
{
    /// <summary>Loads one complete fail-closed launcher protection snapshot.</summary>
    ValueTask<LauncherMutationProtection> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Durably rehomes a last-known-good-only owner to the current active launcher.</summary>
    ValueTask<LauncherMutationFenceIssue> RetireLastKnownGoodOwnerAsync(
        ManagedVersionAdmission expectedOwner,
        CancellationToken cancellationToken);
}
