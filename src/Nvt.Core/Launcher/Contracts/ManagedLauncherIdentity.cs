// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Contracts;

/// <summary>Exact launcher content identity bound to one owning application admission.</summary>
public sealed record ManagedLauncherIdentity
{
    /// <summary>The supported managed launcher protocol.</summary>
    public const int SupportedProtocolVersion = 1;

    private ManagedLauncherIdentity(
        ManagedAppVersion ownerAppVersion,
        string ownerAdmissionIdentity,
        string ownerReleaseManifestSha256,
        ManagedAppVersion launcherVersion,
        int protocolVersion,
        string executableRelativePath,
        long size,
        string sha256)
    {
        OwnerAppVersion = ownerAppVersion;
        OwnerAdmissionIdentity = ownerAdmissionIdentity;
        OwnerReleaseManifestSha256 = ownerReleaseManifestSha256;
        LauncherVersion = launcherVersion;
        ProtocolVersion = protocolVersion;
        ExecutableRelativePath = executableRelativePath;
        Size = size;
        Sha256 = sha256;
    }

    /// <summary>Gets the exact owning application version.</summary>
    public ManagedAppVersion OwnerAppVersion { get; }

    /// <summary>Gets the exact owning admission identity.</summary>
    public string OwnerAdmissionIdentity { get; }

    /// <summary>Gets the owner's exact release-manifest digest.</summary>
    public string OwnerReleaseManifestSha256 { get; }

    /// <summary>Gets the independent canonical launcher version.</summary>
    public ManagedAppVersion LauncherVersion { get; }

    /// <summary>Gets the exact supported protocol integer.</summary>
    public int ProtocolVersion { get; }

    /// <summary>Gets the descriptor-bound executable path.</summary>
    public string ExecutableRelativePath { get; }

    /// <summary>Gets the exact executable byte length.</summary>
    public long Size { get; }

    /// <summary>Gets the lowercase executable SHA-256 digest.</summary>
    public string Sha256 { get; }

    /// <summary>Creates a guarded launcher identity using explicit product names and an executable bound.</summary>
    /// <param name="descriptor">The runtime product descriptor supplied by the adapter.</param>
    /// <param name="maximumExecutableBytes">Explicit launcher ceiling, at most the immutable Bootstrap ceiling.</param>
    /// <param name="ownerAppVersion">Exact owning application version.</param>
    /// <param name="ownerAdmissionIdentity">Exact nonblank owning admission identity, at most 2,048 characters.</param>
    /// <param name="ownerReleaseManifestSha256">Exact lowercase owning release-manifest digest.</param>
    /// <param name="launcherVersion">Canonical independent launcher version.</param>
    /// <param name="protocolVersion">Exact protocol integer, currently 1.</param>
    /// <param name="executableRelativePath">Exact path matching the descriptor ordinally.</param>
    /// <param name="size">Positive executable byte length within the supplied ceiling.</param>
    /// <param name="sha256">Lowercase executable SHA-256 digest.</param>
    /// <returns>The validated exact launcher identity.</returns>
    public static ManagedLauncherIdentity Create(
        ProductDescriptor descriptor,
        long maximumExecutableBytes,
        ManagedAppVersion ownerAppVersion,
        string ownerAdmissionIdentity,
        string ownerReleaseManifestSha256,
        ManagedAppVersion launcherVersion,
        int protocolVersion,
        string executableRelativePath,
        long size,
        string sha256)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (maximumExecutableBytes is <= 0 or > ManagedImmutableBootstrapIdentity.MaximumExecutableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumExecutableBytes));
        }
        if (string.IsNullOrWhiteSpace(ownerAdmissionIdentity) || ownerAdmissionIdentity.Length > 2048)
        {
            throw new ArgumentException("Launcher owner admission identity is invalid.", nameof(ownerAdmissionIdentity));
        }
        if (!ContractValidation.IsLowerSha256(ownerReleaseManifestSha256))
        {
            throw new ArgumentException("Launcher owner manifest identity is invalid.", nameof(ownerReleaseManifestSha256));
        }
        if (protocolVersion != SupportedProtocolVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion), "Launcher protocol is unsupported.");
        }
        if (!string.Equals(executableRelativePath, descriptor.LauncherExecutableRelativePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("Launcher executable path does not match the descriptor.", nameof(executableRelativePath));
        }
        if (size <= 0 || size > maximumExecutableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        if (!ContractValidation.IsLowerSha256(sha256))
        {
            throw new ArgumentException("Launcher executable identity is invalid.", nameof(sha256));
        }

        return new(ownerAppVersion, ownerAdmissionIdentity, ownerReleaseManifestSha256,
            launcherVersion, protocolVersion, executableRelativePath, size, sha256);
    }

    /// <summary>Checks every exact owner field without inferring admission from a path.</summary>
    /// <param name="admission">The owning admission to compare.</param>
    /// <returns>Whether version, admission identity and manifest digest all match ordinally.</returns>
    public bool MatchesOwner(ManagedVersionAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return admission.Version == OwnerAppVersion &&
            string.Equals(admission.AdmissionIdentity, OwnerAdmissionIdentity, StringComparison.Ordinal) &&
            string.Equals(admission.ReleaseManifestSha256, OwnerReleaseManifestSha256, StringComparison.Ordinal);
    }
}

/// <summary>Exact descriptor-bound immutable root Bootstrap content identity.</summary>
public sealed record ManagedImmutableBootstrapIdentity
{
    /// <summary>The frozen immutable Bootstrap executable byte-length ceiling.</summary>
    public const long MaximumExecutableBytes = 200_000_000;

    private ManagedImmutableBootstrapIdentity(string fileName, long length, string sha256)
    {
        FileName = fileName;
        Length = length;
        Sha256 = sha256;
    }

    /// <summary>Gets the exact descriptor-bound root filename.</summary>
    public string FileName { get; }

    /// <summary>Gets the exact executable byte length.</summary>
    public long Length { get; }

    /// <summary>Gets the lowercase executable SHA-256 digest.</summary>
    public string Sha256 { get; }

    /// <summary>Creates the exact immutable Bootstrap identity using the supplied product descriptor.</summary>
    /// <param name="descriptor">The product-supplied runtime descriptor.</param>
    /// <param name="fileName">The exact single root filename matching the descriptor ordinally.</param>
    /// <param name="length">Positive executable length, at most 200,000,000 bytes.</param>
    /// <param name="sha256">Lowercase executable SHA-256 digest.</param>
    /// <returns>The validated immutable Bootstrap identity.</returns>
    public static ManagedImmutableBootstrapIdentity Create(
        ProductDescriptor descriptor,
        string fileName,
        long length,
        string sha256)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(fileName, descriptor.BootstrapExecutableFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Bootstrap filename does not match the descriptor.", nameof(fileName));
        }
        if (!ContractValidation.IsLowerSha256(sha256))
        {
            throw new ArgumentException("Bootstrap digest must be lowercase SHA-256.", nameof(sha256));
        }
        if (length is <= 0 or > MaximumExecutableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return new(fileName, length, sha256);
    }
}
