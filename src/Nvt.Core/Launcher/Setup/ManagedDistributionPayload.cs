// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Setup;

/// <summary>Stable issue from inspecting or capturing the distribution Launcher payload.</summary>
public enum ManagedDistributionPayloadIssue
{
    /// <summary>The exact closed payload identity or capture is available.</summary>
    None,
    /// <summary>The running distribution payload could not be observed completely.</summary>
    Unavailable,
    /// <summary>The descriptor, Launcher, or embedded Bootstrap failed strict admission.</summary>
    Invalid,
    /// <summary>The exact payload changed after the user reviewed the install plan.</summary>
    Changed,
}

/// <summary>Closed identity of the running Launcher, descriptor, and embedded Bootstrap.</summary>
public sealed record ManagedDistributionPayloadIdentity
{
    /// <summary>Creates one validated distribution payload identity.</summary>
    public ManagedDistributionPayloadIdentity(
        long maximumDescriptorBytes,
        ManagedAppVersion launcherVersion,
        string sourceCommit,
        long launcherSize,
        string launcherSha256,
        long descriptorSize,
        string descriptorSha256,
        ManagedImmutableBootstrapIdentity bootstrap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDescriptorBytes);
        if (!IsLowerSourceCommit(sourceCommit) ||
            launcherSize <= 0 ||
            !ContractValidation.IsLowerSha256(launcherSha256) ||
            (descriptorSize <= 0 || descriptorSize > maximumDescriptorBytes) ||
            !ContractValidation.IsLowerSha256(descriptorSha256))
        {
            throw new ArgumentException("Distribution Launcher payload identity is invalid.");
        }
        LauncherVersion = launcherVersion;
        SourceCommit = sourceCommit;
        LauncherSize = launcherSize;
        LauncherSha256 = launcherSha256;
        DescriptorSize = descriptorSize;
        DescriptorSha256 = descriptorSha256;
        Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    }

    /// <summary>Gets the distribution Launcher version.</summary>
    public ManagedAppVersion LauncherVersion { get; }
    /// <summary>Gets the exact release source commit.</summary>
    public string SourceCommit { get; }
    /// <summary>Gets the exact running Launcher length.</summary>
    public long LauncherSize { get; }
    /// <summary>Gets the exact running Launcher digest.</summary>
    public string LauncherSha256 { get; }
    /// <summary>Gets the exact embedded descriptor length.</summary>
    public long DescriptorSize { get; }
    /// <summary>Gets the exact embedded descriptor digest.</summary>
    public string DescriptorSha256 { get; }
    /// <summary>Gets the exact descriptor-bound Root Bootstrap identity.</summary>
    public ManagedImmutableBootstrapIdentity Bootstrap { get; }

    private static bool IsLowerSourceCommit(string? value)
    {
        return value is { Length: 40 } &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}

/// <summary>Stable read-only payload inspection result.</summary>
public sealed record ManagedDistributionPayloadInspectionResult(
    ManagedDistributionPayloadIdentity? Identity,
    ManagedDistributionPayloadIssue Issue)
{
    /// <summary>Gets whether one complete payload identity was admitted.</summary>
    public bool IsSuccess => Identity is not null && Issue == ManagedDistributionPayloadIssue.None;
}

/// <summary>
/// Lightweight entry admission projected without reading or hashing Bootstrap content.
/// </summary>
public sealed record ManagedDistributionPayloadEntryAdmissionResult(
    ManagedAppVersion LauncherVersion,
    ManagedImmutableBootstrapIdentity? Bootstrap,
    ManagedDistributionPayloadIssue Issue)
{
    /// <summary>Gets whether the descriptor and Bootstrap resource metadata were admitted.</summary>
    public bool IsSuccess => Bootstrap is not null && Issue == ManagedDistributionPayloadIssue.None;
}

/// <summary>Opaque stable custody over the exact bytes copied into a fresh installation.</summary>
public interface IManagedDistributionPayloadCapture : IDisposable
{
    /// <summary>Gets the exact captured identity.</summary>
    ManagedDistributionPayloadIdentity Identity { get; }
}

/// <summary>Stable exact payload-capture result.</summary>
public sealed record ManagedDistributionPayloadCaptureResult(
    IManagedDistributionPayloadCapture? Capture,
    ManagedDistributionPayloadIssue Issue)
{
    /// <summary>Gets whether the exact expected payload is held for materialization.</summary>
    public bool IsSuccess => Capture is not null && Issue == ManagedDistributionPayloadIssue.None;
}

/// <summary>Infrastructure seam for inspecting and stably capturing the running payload.</summary>
public interface IManagedDistributionPayloadSource
{
    /// <summary>
    /// Admits bounded descriptor and Bootstrap metadata for local entry without reading content.
    /// </summary>
    ValueTask<ManagedDistributionPayloadEntryAdmissionResult> AdmitEntryAsync(
        CancellationToken cancellationToken);

    /// <summary>Inspects the closed payload without retaining installation bytes.</summary>
    ValueTask<ManagedDistributionPayloadInspectionResult> InspectAsync(
        CancellationToken cancellationToken);

    /// <summary>Captures only the exact previously inspected payload through stable custody.</summary>
    ValueTask<ManagedDistributionPayloadCaptureResult> CaptureExactAsync(
        ManagedDistributionPayloadIdentity expected,
        CancellationToken cancellationToken);
}

