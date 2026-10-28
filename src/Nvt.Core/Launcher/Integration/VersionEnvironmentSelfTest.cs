// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Integration;

/// <summary>One read-only environment self-test attempt for an automatic source.</summary>
public sealed record VersionEnvironmentSelfTestAttempt
{
    /// <summary>Creates one internally consistent latest/available attempt.</summary>
    internal VersionEnvironmentSelfTestAttempt(
        string sourceRoot,
        UpdateSourceRegistryEntryStatus status,
        UpdateCatalogLoadIssue catalogIssue,
        ManagedVersionInstallIssue? packageIssue,
        ManagedAppVersion? newestVersion,
        bool isVerified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        bool catalogPassed = catalogIssue == UpdateCatalogLoadIssue.None;
        bool packagePassed = packageIssue == ManagedVersionInstallIssue.None;
        if (status is not (UpdateSourceRegistryEntryStatus.Latest or
                UpdateSourceRegistryEntryStatus.Available) ||
            !Enum.IsDefined(catalogIssue) ||
            (packageIssue is { } declaredPackageIssue && !Enum.IsDefined(declaredPackageIssue)) ||
            (catalogPassed != (packageIssue is not null && newestVersion is not null)) ||
            (isVerified != (catalogPassed && packagePassed)))
        {
            throw new ArgumentException("Environment self-test attempt is inconsistent.");
        }
        SourceRoot = sourceRoot;
        Status = status;
        CatalogIssue = catalogIssue;
        PackageIssue = packageIssue;
        NewestVersion = newestVersion;
    }

    /// <summary>Gets the normalized automatic source root. Presentation must not expose it by default.</summary>
    public string SourceRoot { get; }

    /// <summary>Gets whether this was latest or an ordered available source.</summary>
    public UpdateSourceRegistryEntryStatus Status { get; }

    /// <summary>Gets the complete catalog admission outcome.</summary>
    public UpdateCatalogLoadIssue CatalogIssue { get; }

    /// <summary>Gets newest-package admission after catalog success.</summary>
    public ManagedVersionInstallIssue? PackageIssue { get; }

    /// <summary>Gets the newest catalog version when the catalog passed.</summary>
    public ManagedAppVersion? NewestVersion { get; }

    /// <summary>Gets whether the complete catalog and newest package passed.</summary>
    public bool IsVerified => CatalogIssue == UpdateCatalogLoadIssue.None &&
        PackageIssue == ManagedVersionInstallIssue.None;
}

/// <summary>Bounded read-only result for the configured registry environment.</summary>
public sealed class VersionEnvironmentSelfTestResult
{
    /// <summary>Creates one bounded immutable result.</summary>
    internal VersionEnvironmentSelfTestResult(
        VersionManagementLimits limits,
        UpdateSourceRegistryLoadIssue registryIssue,
        IReadOnlyList<VersionEnvironmentSelfTestAttempt> attempts,
        IReadOnlyList<UpdateSourceRegistryReplicaObservation>? replicas = null,
        UpdateSourceRegistryIssue authorityIssue = UpdateSourceRegistryIssue.None,
        long? acceptedRegistryRevision = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(attempts);
        if (!Enum.IsDefined(registryIssue) || attempts.Count > limits.MaximumRegistryEntries ||
            !Enum.IsDefined(authorityIssue) ||
            acceptedRegistryRevision is < 0 ||
            (registryIssue != UpdateSourceRegistryLoadIssue.None &&
                authorityIssue != UpdateSourceRegistryIssue.None) ||
            ((registryIssue == UpdateSourceRegistryLoadIssue.None &&
                authorityIssue == UpdateSourceRegistryIssue.None) != (attempts.Count > 0)))
        {
            throw new ArgumentException("Environment self-test result is inconsistent.");
        }
        RegistryIssue = registryIssue;
        AuthorityIssue = authorityIssue;
        AcceptedRegistryRevision = acceptedRegistryRevision;
        Attempts = Array.AsReadOnly(attempts.ToArray());
        Replicas = Array.AsReadOnly(replicas is null ? [] : replicas.ToArray());
    }

    /// <summary>Gets the fixed-registry load result.</summary>
    public UpdateSourceRegistryLoadIssue RegistryIssue { get; }

    /// <summary>Gets durable Registry-authority admission after a successful replica read.</summary>
    public UpdateSourceRegistryIssue AuthorityIssue { get; }

    /// <summary>Gets the durable accepted revision used to classify replica freshness.</summary>
    public long? AcceptedRegistryRevision { get; }

    /// <summary>Gets latest then available attempts, never deprecated.</summary>
    public IReadOnlyList<VersionEnvironmentSelfTestAttempt> Attempts { get; }

    /// <summary>Gets ordered primary/backup health when replicated Registry discovery was used.</summary>
    public IReadOnlyList<UpdateSourceRegistryReplicaObservation> Replicas { get; }

    /// <summary>Gets whether the registry loaded and at least one automatic source fully verified.</summary>
    public bool IsSuccess => RegistryIssue == UpdateSourceRegistryLoadIssue.None &&
        AuthorityIssue == UpdateSourceRegistryIssue.None &&
        Attempts.Any(static attempt => attempt.IsVerified);
}
