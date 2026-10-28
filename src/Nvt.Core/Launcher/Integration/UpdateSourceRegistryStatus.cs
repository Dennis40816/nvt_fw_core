// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Integration;

/// <summary>Visible terminal state of fixed-registry resolution.</summary>
public enum VersionRegistryStatus
{
    /// <summary>No registry adapter is configured.</summary>
    NotConfigured,
    /// <summary>A user-confirmed source remains durably pinned.</summary>
    ManualPin,
    /// <summary>The unique latest source was selected.</summary>
    LatestSelected,
    /// <summary>One ordered available fallback was selected.</summary>
    FallbackSelected,
    /// <summary>The registry could not be read.</summary>
    Unavailable,
    /// <summary>No automatic candidate passed admission.</summary>
    Exhausted,
    /// <summary>Registry authority was rejected before candidate selection.</summary>
    Rejected,
    /// <summary>The prior source is deprecated and no permitted replacement admitted.</summary>
    DeprecatedRetained,
}

/// <summary>Stable Application-owned fixed-registry issue.</summary>
public enum UpdateSourceRegistryIssue
{
    /// <summary>No issue occurred.</summary>
    None,
    /// <summary>No registry locator is configured.</summary>
    NotConfigured,
    /// <summary>The registry file is absent or unavailable.</summary>
    Unavailable,
    /// <summary>The registry exists but cannot be read by the current user.</summary>
    PermissionDenied,
    /// <summary>The remote Registry requires credentials unavailable to this client.</summary>
    AuthenticationRequired,
    /// <summary>The remote Registry did not complete within its bounded deadline.</summary>
    TimedOut,
    /// <summary>The registry contract or locator is invalid or unsafe.</summary>
    Invalid,
    /// <summary>The observed revision is lower than the last accepted revision.</summary>
    RevisionRollback,
    /// <summary>The same revision was republished with different exact bytes.</summary>
    RevisionConflict,
    /// <summary>Every latest/available candidate failed catalog or package admission.</summary>
    CandidatesExhausted,
    /// <summary>The registry changed before the source commit could be serialized.</summary>
    RegistryChanged,
    /// <summary>The exact state writer or durable state was unavailable.</summary>
    StateUnavailable,
    /// <summary>A newer check superseded this resolution.</summary>
    Superseded,
    /// <summary>The retained prior source is declared deprecated.</summary>
    CurrentSourceDeprecated,
}

/// <summary>Visible configured-source connection state.</summary>
public enum VersionSourceStatus
{
    /// <summary>No source folder is configured.</summary>
    NotConfigured,
    /// <summary>One current-generation discovery is running.</summary>
    Checking,
    /// <summary>The catalog loaded successfully.</summary>
    Connected,
    /// <summary>The source is missing or unavailable.</summary>
    Offline,
    /// <summary>The configured source exists but the current user cannot read it.</summary>
    PermissionDenied,
    /// <summary>The source catalog or package failed verification.</summary>
    Invalid,
}
