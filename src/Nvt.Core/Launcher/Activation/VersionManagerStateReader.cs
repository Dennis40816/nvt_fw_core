// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Stable launcher-state read result category.</summary>
public enum VersionManagerStateLoadIssue
{
    /// <summary>State loaded and validated.</summary>
    None,
    /// <summary>No state file exists.</summary>
    Missing,
    /// <summary>State exists but is malformed or inconsistent.</summary>
    Invalid,
    /// <summary>State exists but could not be read.</summary>
    Unavailable,
    /// <summary>State is unbound or belongs to another normalized managed root.</summary>
    ManagedRootMismatch,
}

/// <summary>Fail-closed launcher-state load result.</summary>
public sealed record VersionManagerStateLoadResult(
    VersionManagerState? State,
    VersionManagerStateLoadIssue Issue)
{
    /// <summary>Gets whether a validated state snapshot was published.</summary>
    public bool IsSuccess => State is not null && Issue == VersionManagerStateLoadIssue.None;
}

/// <summary>Stable result from one atomic launcher-state write.</summary>
public enum VersionManagerStateSaveIssue
{
    /// <summary>The complete state snapshot was atomically committed.</summary>
    None,
    /// <summary>The destination could not durably accept the snapshot.</summary>
    Unavailable,
}

/// <summary>Typed state persistence result used at cross-resource transaction seams.</summary>
public readonly record struct VersionManagerStateSaveResult(VersionManagerStateSaveIssue Issue)
{
    /// <summary>Gets whether the atomic state write committed.</summary>
    public bool IsSuccess => Issue == VersionManagerStateSaveIssue.None;
}

/// <summary>Read-only persistence port for one validated managed-version state snapshot.</summary>
public interface IVersionManagerStateReader
{
    /// <summary>
    /// Loads state without guessing missing version identities. Implementations must return
    /// their ValueTask promptly and honor cancellation; callers may isolate and abandon a
    /// read-only load after their own hard deadline.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fail-closed state result.</returns>
    ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken);
}
