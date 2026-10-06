// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Activation;

/// <summary>Durable anti-rollback and manual-pin state.</summary>
public sealed record VersionSourceRegistryState
{
    /// <summary>Creates one validated durable registry authority.</summary>
    public VersionSourceRegistryState(
        long acceptedRevision,
        string? acceptedDigest,
        bool isManualPin)
    {
        bool hasNoRevision = acceptedRevision == 0;
        bool hasNoDigest = acceptedDigest is null;
        if (acceptedRevision < 0 ||
            hasNoRevision != hasNoDigest ||
            (hasNoRevision && !isManualPin) ||
            (acceptedDigest is not null && !ContractValidation.IsLowerSha256(acceptedDigest)))
        {
            throw new ArgumentException("Registry revision and digest are inconsistent.");
        }
        AcceptedRevision = acceptedRevision;
        AcceptedDigest = acceptedDigest;
        IsManualPin = isManualPin;
    }

    /// <summary>Gets the last accepted monotonic registry revision, or zero before first admission.</summary>
    public long AcceptedRevision { get; }

    /// <summary>Gets the digest bound to the accepted revision.</summary>
    public string? AcceptedDigest { get; }

    /// <summary>Gets whether automatic resolution is suspended by a durable manual source.</summary>
    public bool IsManualPin { get; }
}
