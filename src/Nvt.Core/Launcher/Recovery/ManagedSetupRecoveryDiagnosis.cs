// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Setup;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Infrastructure-observed state of the exact managed Setup marker.</summary>
public enum ManagedSetupRecoveryFactKind
{
    /// <summary>No marker exists at the exact transaction path.</summary>
    Absent,
    /// <summary>One strict marker matches every caller-supplied identity.</summary>
    Exact,
    /// <summary>The bounded marker bytes violate the canonical schema.</summary>
    Malformed,
    /// <summary>The marker is valid but belongs to different identities or paths.</summary>
    IdentityMismatch,
    /// <summary>The current user cannot inspect the exact marker.</summary>
    AccessDenied,
    /// <summary>The exact marker or its custody changed during observation.</summary>
    Changed,
    /// <summary>The exact marker could not be observed completely.</summary>
    Unavailable,
}

/// <summary>Declared durable phase of an exact Setup transaction.</summary>
public enum ManagedSetupRecoveryPhase
{
    /// <summary>Setup was still constructing the staging root.</summary>
    Staging,
    /// <summary>Setup promoted the closed root but had not recorded Bootstrap intent.</summary>
    RootPromoted,
    /// <summary>Setup durably recorded Bootstrap launch intent.</summary>
    BootstrapLaunchRecorded,
}

/// <summary>Immutable distribution payload identity recorded by Setup.</summary>
/// <param name="LauncherSize">Exact normalized launcher size.</param>
/// <param name="LauncherSha256">Exact normalized launcher sha256.</param>
/// <param name="DescriptorSize">Exact normalized descriptor size.</param>
/// <param name="DescriptorSha256">Exact normalized descriptor sha256.</param>
/// <param name="BootstrapFileName">Exact normalized bootstrap file name.</param>
/// <param name="BootstrapSize">Exact normalized bootstrap size.</param>
/// <param name="BootstrapSha256">Exact normalized bootstrap sha256.</param>
public sealed record ManagedSetupRecoveryPayloadIdentity(
    long LauncherSize,
    string LauncherSha256,
    long DescriptorSize,
    string DescriptorSha256,
    string BootstrapFileName,
    long BootstrapSize,
    string BootstrapSha256);

/// <summary>Immutable Registry, Catalog, and package candidate identity recorded by Setup.</summary>
/// <param name="RegistryRevision">Exact normalized registry revision.</param>
/// <param name="RegistryDigest">Exact normalized registry digest.</param>
/// <param name="CatalogSchemaVersion">Exact normalized catalog schema version.</param>
/// <param name="CatalogLatestVersion">Exact normalized catalog latest version.</param>
/// <param name="CatalogDigest">Exact normalized catalog digest.</param>
/// <param name="CatalogPath">Exact normalized catalog path.</param>
/// <param name="RegistryId">Exact normalized registry id.</param>
/// <param name="SourceRoot">Exact normalized source root.</param>
/// <param name="SourceStatus">Exact normalized source status.</param>
/// <param name="Version">Exact normalized version.</param>
/// <param name="PackagePath">Exact normalized package path.</param>
/// <param name="PackageSize">Exact normalized package size.</param>
/// <param name="PackageSha256">Exact normalized package sha256.</param>
/// <param name="ReleaseManifestSha256">Exact normalized release manifest sha256.</param>
/// <param name="EntryIdentity">Exact normalized entry identity.</param>
public sealed record ManagedSetupRecoveryCandidateIdentity(
    long RegistryRevision,
    string RegistryDigest,
    int CatalogSchemaVersion,
    string CatalogLatestVersion,
    string CatalogDigest,
    string CatalogPath,
    string RegistryId,
    string SourceRoot,
    string SourceStatus,
    string Version,
    string PackagePath,
    long PackageSize,
    string PackageSha256,
    string ReleaseManifestSha256,
    string EntryIdentity);

/// <summary>Exact immutable transaction fact admitted by the filesystem adapter.</summary>
public sealed class ManagedSetupRecoveryTransaction
{
    /// <summary>Creates one exact transaction fact from an admitted marker.</summary>
    internal ManagedSetupRecoveryTransaction(
        string transactionId,
        string managedRootIdentity,
        string statePathIdentity,
        ManagedSetupRecoveryPhase phase,
        IReadOnlyList<string> ownedPaths,
        ManagedSetupRecoveryPayloadIdentity payload,
        ManagedSetupRecoveryCandidateIdentity candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRootIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(statePathIdentity);
        ArgumentNullException.ThrowIfNull(ownedPaths);
        TransactionId = transactionId;
        ManagedRootIdentity = managedRootIdentity;
        StatePathIdentity = statePathIdentity;
        Phase = phase;
        OwnedPaths = Array.AsReadOnly([.. ownedPaths]);
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    }

    /// <summary>Gets the immutable transaction identifier.</summary>
    public string TransactionId { get; }
    /// <summary>Gets the exact managed-root identity.</summary>
    public string ManagedRootIdentity { get; }
    /// <summary>Gets the exact state-path identity.</summary>
    public string StatePathIdentity { get; }
    /// <summary>Gets the durable Setup phase.</summary>
    public ManagedSetupRecoveryPhase Phase { get; }
    /// <summary>Gets the exact closed set of Setup-owned relative paths.</summary>
    public IReadOnlyList<string> OwnedPaths { get; }
    /// <summary>Gets the immutable distribution payload identity.</summary>
    public ManagedSetupRecoveryPayloadIdentity Payload { get; }
    /// <summary>Gets the immutable Registry, Catalog, and package identity.</summary>
    public ManagedSetupRecoveryCandidateIdentity Candidate { get; }
}

/// <summary>One read-only adapter fact; only Exact carries a transaction.</summary>
public sealed record ManagedSetupRecoveryFact
{
    /// <summary>Creates one internally consistent adapter fact.</summary>
    internal ManagedSetupRecoveryFact(
        ManagedSetupRecoveryFactKind kind,
        ManagedSetupRecoveryTransaction? transaction)
    {
        if (kind == ManagedSetupRecoveryFactKind.Exact != (transaction is not null))
        {
            throw new ArgumentException("Only an exact recovery fact carries a transaction.");
        }
        Kind = kind;
        Transaction = transaction;
    }

    /// <summary>Gets the observed fact category.</summary>
    public ManagedSetupRecoveryFactKind Kind { get; }
    /// <summary>Gets the admitted transaction only for an Exact fact.</summary>
    public ManagedSetupRecoveryTransaction? Transaction { get; }
}

/// <summary>Read-only port for exact managed Setup marker observation.</summary>
public interface IManagedSetupRecoveryProbe
{
    /// <summary>Observes and binds one exact marker without mutation.</summary>
    ValueTask<ManagedSetupRecoveryFact> ObserveAsync(
        string managedRoot,
        string statePathIdentity,
        CancellationToken cancellationToken);
}

/// <summary>
/// Read-only state port bound to the one exact state path used by recovery diagnosis.
/// </summary>
public interface IManagedSetupRecoveryStateReader : IVersionManagerStateReader
{
    /// <summary>Gets the exact absolute state-file identity read by this adapter.</summary>
    string StatePathIdentity { get; }
}

/// <summary>Read-only observation of one managed process role's lifetime authority.</summary>
public interface IManagedProcessLifetimeProbe
{
    /// <summary>Observes one exact role without acquiring or creating lifetime authority.</summary>
    ValueTask<ManagedProcessLifetimeStatus> ObserveAsync(
        string statePath,
        ManagedProcessLifetimeKind kind,
        CancellationToken cancellationToken);
}

/// <summary>Terminal Application decision for the managed Setup recovery experience.</summary>
public enum ManagedSetupRecoveryOutcome
{
    /// <summary>State, root, marker, and process facts are mutually healthy and need no recovery.</summary>
    NoRecoveryNeeded,
    /// <summary>One exact interrupted Setup transaction is eligible for a later approved action.</summary>
    ActionAvailable,
    /// <summary>At least one managed process role still owns lifetime authority.</summary>
    Busy,
    /// <summary>A complete stable local-health diagnosis is temporarily unavailable.</summary>
    HealthUnavailable,
    /// <summary>Malformed, foreign, unsafe, or inconsistent facts require explicit human handling.</summary>
    ManualInterventionRequired,
}

/// <summary>One terminal read-only diagnosis.</summary>
public sealed class ManagedSetupRecoveryDiagnosis
{
    internal ManagedSetupRecoveryDiagnosis(
        ManagedSetupRecoveryOutcome outcome,
        ManagedSetupRecoveryTransaction? transaction,
        ManagedSetupRecoveryPlan? plan)
    {
        bool actionable = outcome == ManagedSetupRecoveryOutcome.ActionAvailable;
        if (actionable != (transaction is not null && plan is not null))
        {
            throw new ArgumentException(
                "Only an actionable recovery diagnosis carries a transaction and immutable plan.");
        }
        Outcome = outcome;
        Transaction = transaction;
        Plan = plan;
    }

    /// <summary>Gets the terminal read-only diagnosis outcome.</summary>
    public ManagedSetupRecoveryOutcome Outcome { get; }
    /// <summary>Gets the exact transaction only when an action is available.</summary>
    public ManagedSetupRecoveryTransaction? Transaction { get; }
    /// <summary>Gets the immutable execution plan only when an action is available.</summary>
    public ManagedSetupRecoveryPlan? Plan { get; }
}

/// <summary>
/// Owns the complete read-only Setup recovery decision without filesystem or process-start access.
/// </summary>
public sealed class ManagedInstallationRecoveryExperience
{
    private readonly IManagedProcessLifetimeProbe _lifetimeProbe;
    private readonly IManagedSetupRecoveryProbe _markerProbe;
    private readonly IManagedInstallationRootProbe _rootProbe;
    private readonly IManagedSetupRecoveryStateReader _stateReader;
    private readonly IManagedSetupRecoveryEvidenceProbe _evidenceProbe;
    private readonly string _statePathIdentity;

    /// <summary>Creates the sole Application recovery diagnosis owner.</summary>
    public ManagedInstallationRecoveryExperience(
        IManagedSetupRecoveryStateReader stateReader,
        IManagedInstallationRootProbe rootProbe,
        IManagedSetupRecoveryProbe markerProbe,
        IManagedProcessLifetimeProbe lifetimeProbe,
        IManagedSetupRecoveryEvidenceProbe evidenceProbe)
    {
        _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
        string statePathIdentity = stateReader.StatePathIdentity;
        if (string.IsNullOrWhiteSpace(statePathIdentity) ||
            !Path.IsPathFullyQualified(statePathIdentity))
        {
            throw new ArgumentException(
                "Recovery state reader identity must be an absolute path.",
                nameof(stateReader));
        }
        _statePathIdentity = Path.GetFullPath(statePathIdentity);
        _rootProbe = rootProbe ?? throw new ArgumentNullException(nameof(rootProbe));
        _markerProbe = markerProbe ?? throw new ArgumentNullException(nameof(markerProbe));
        _lifetimeProbe = lifetimeProbe ?? throw new ArgumentNullException(nameof(lifetimeProbe));
        _evidenceProbe = evidenceProbe ?? throw new ArgumentNullException(nameof(evidenceProbe));
    }

    /// <summary>Returns one terminal read-only diagnosis for the exact managed-root identity.</summary>
    public async ValueTask<ManagedSetupRecoveryDiagnosis> DiagnoseAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        var lifetimes = new ManagedProcessLifetimeStatus[ManagedSetupRecoveryPolicy._lifetimeKinds.Length];
        for (int index = 0; index < ManagedSetupRecoveryPolicy._lifetimeKinds.Length; index++)
        {
            lifetimes[index] = await _lifetimeProbe.ObserveAsync(
                _statePathIdentity,
                ManagedSetupRecoveryPolicy._lifetimeKinds[index],
                cancellationToken).ConfigureAwait(false);
        }
        if (lifetimes.Contains(ManagedProcessLifetimeStatus.Active))
        {
            return Terminal(ManagedSetupRecoveryOutcome.Busy);
        }
        if (lifetimes.Any(static status => status != ManagedProcessLifetimeStatus.Exited))
        {
            return Terminal(ManagedSetupRecoveryOutcome.HealthUnavailable);
        }

        VersionManagerStateLoadResult state = await _stateReader.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ManagedInstallationRootObservation root = await _rootProbe.ObserveAsync(
            managedRoot,
            cancellationToken).ConfigureAwait(false);
        ManagedSetupRecoveryFact marker = await _markerProbe.ObserveAsync(
            managedRoot,
            _statePathIdentity,
            cancellationToken).ConfigureAwait(false);

        if (state.Issue == VersionManagerStateLoadIssue.Unavailable ||
            root.Status is ManagedInstallationRootStatus.PermissionDenied or
                ManagedInstallationRootStatus.Unavailable ||
            marker.Kind is ManagedSetupRecoveryFactKind.AccessDenied or
                ManagedSetupRecoveryFactKind.Unavailable)
        {
            return Terminal(ManagedSetupRecoveryOutcome.HealthUnavailable);
        }

        bool stateMissing = state.State is null && state.Issue == VersionManagerStateLoadIssue.Missing;
        bool stateExact = state.IsSuccess &&
            state.State!.ManagedRootIdentity is { } boundRoot &&
            IsExactRoot(boundRoot, managedRoot);
        bool factsAreUnsafe = (!stateMissing && !stateExact) ||
            root.Status == ManagedInstallationRootStatus.InvalidDestination ||
            marker.Kind is ManagedSetupRecoveryFactKind.Malformed or
                ManagedSetupRecoveryFactKind.Changed or
                ManagedSetupRecoveryFactKind.IdentityMismatch;

        return factsAreUnsafe
            ? Terminal(ManagedSetupRecoveryOutcome.ManualInterventionRequired)
            : marker.Kind switch
            {
                ManagedSetupRecoveryFactKind.Absent =>
                    DiagnoseAbsent(stateMissing, stateExact, root.Status),
                ManagedSetupRecoveryFactKind.Exact when marker.Transaction is { } transaction =>
                    await DiagnoseExactAsync(
                        managedRoot,
                        state,
                        root.Status,
                        transaction,
                        cancellationToken).ConfigureAwait(false),
                ManagedSetupRecoveryFactKind.Exact => throw new InvalidOperationException(
                    "An exact recovery fact omitted its transaction."),
                ManagedSetupRecoveryFactKind.Malformed or
                    ManagedSetupRecoveryFactKind.IdentityMismatch or
                    ManagedSetupRecoveryFactKind.AccessDenied or
                ManagedSetupRecoveryFactKind.Changed or
                    ManagedSetupRecoveryFactKind.Unavailable => throw new InvalidOperationException(
                    "Recovery facts were not completely classified."),
                _ => throw new InvalidOperationException(
                    "Recovery fact kind was outside the closed contract."),
            };
    }

    private static ManagedSetupRecoveryDiagnosis DiagnoseAbsent(
        bool stateMissing,
        bool stateExact,
        ManagedInstallationRootStatus root)
    {
        return ((stateMissing && root == ManagedInstallationRootStatus.Absent) ||
            (stateExact && root == ManagedInstallationRootStatus.Present))
                ? Terminal(ManagedSetupRecoveryOutcome.NoRecoveryNeeded)
                : Terminal(ManagedSetupRecoveryOutcome.ManualInterventionRequired);
    }

    private async ValueTask<ManagedSetupRecoveryDiagnosis> DiagnoseExactAsync(
        string managedRoot,
        VersionManagerStateLoadResult state,
        ManagedInstallationRootStatus root,
        ManagedSetupRecoveryTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (root != ManagedInstallationRootStatus.Residue)
        {
            return Terminal(ManagedSetupRecoveryOutcome.ManualInterventionRequired);
        }
        ManagedSetupRecoveryEvidenceObservation evidence = await _evidenceProbe.ObserveAsync(
            transaction,
            cancellationToken).ConfigureAwait(false);
        if (evidence.Issue != ManagedSetupRecoveryEvidenceIssue.None)
        {
            return Terminal(evidence.Issue is
                ManagedSetupRecoveryEvidenceIssue.StateUnavailable or
                ManagedSetupRecoveryEvidenceIssue.PermissionDenied
                    ? ManagedSetupRecoveryOutcome.HealthUnavailable
                    : ManagedSetupRecoveryOutcome.ManualInterventionRequired);
        }
        if (evidence.LauncherState?.Issue == LauncherBootstrapStateLoadIssue.Unavailable)
        {
            return Terminal(ManagedSetupRecoveryOutcome.HealthUnavailable);
        }
        ManagedSetupRecoveryAction? action = ManagedSetupRecoveryPolicy.SelectAction(
            state,
            transaction,
            evidence,
            managedRoot);
        return action is { } selected
            ? new ManagedSetupRecoveryDiagnosis(
                ManagedSetupRecoveryOutcome.ActionAvailable,
                transaction,
                new ManagedSetupRecoveryPlan(
                    ManagedRootPathIdentity.Normalize(managedRoot),
                    transaction,
                    selected,
                    state,
                    evidence))
            : Terminal(ManagedSetupRecoveryOutcome.ManualInterventionRequired);
    }

    private static ManagedSetupRecoveryDiagnosis Terminal(ManagedSetupRecoveryOutcome outcome)
    {
        return new(outcome, transaction: null, plan: null);
    }

    private static bool IsExactRoot(string boundRoot, string requestedRoot)
    {
        try
        {
            return ManagedRootPathIdentity.Equals(boundRoot, requestedRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
