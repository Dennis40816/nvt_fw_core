// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Stable result from first-run managed-state seeding.</summary>
public enum ManagedVersionSeedOutcome
{
    /// <summary>A valid per-user managed state already exists.</summary>
    ExistingState,
    /// <summary>The explicit packaged seed was verified and persisted.</summary>
    Seeded,
    /// <summary>No per-user state or packaged seed exists.</summary>
    MissingSeed,
    /// <summary>Existing per-user state is invalid or unavailable and was not overwritten.</summary>
    InvalidExistingState,
    /// <summary>The packaged seed state is malformed or violates the first-run shape.</summary>
    InvalidSeed,
    /// <summary>The seeded payload is absent, damaged, or contains unexpected managed versions.</summary>
    DamagedSeedPayload,
    /// <summary>The verified seed could not be durably committed.</summary>
    StateUnavailable,
    /// <summary>The durable destination belongs to another root or lacks its required binding.</summary>
    ManagedRootMismatch,
    /// <summary>Another process owns the destination writer lease.</summary>
    Busy,
}

/// <summary>Single owner for the canonical first-install seed shape.</summary>
public static class ManagedVersionSeedPolicy
{
    /// <summary>Creates one unbound seed containing exactly one Active=LKG admission.</summary>
    public static VersionManagerState CreateCanonicalFirstRunSeed(
        ManagedVersionAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return VersionManagerState.Create(
            updateSource: null,
            activeVersion: admission.Version,
            lastKnownGoodVersion: admission.Version,
            [admission],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false);
    }

    /// <summary>Checks the exact immutable shape accepted by runtime seed import.</summary>
    public static bool IsCanonicalFirstRunSeed(VersionManagerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ManagedVersionAdmission? admission = state.Admissions.Count == 1
            ? state.Admissions[0]
            : null;
        return state.UpdateSource is null &&
               state.ManagedRootIdentity is null &&
               state.ActiveVersion is { } active &&
               state.LastKnownGoodVersion == active &&
               admission?.Version == active &&
               state.PendingActivation is null &&
               state.PendingMutation is null &&
               state.FailedActivationVersion is null &&
               !state.RetentionReviewDue &&
               state.SourceRegistryState is null;
    }

    /// <summary>Checks the exact bound state produced when one canonical seed reaches READY.</summary>
    public static bool IsCanonicalBoundFirstRunState(
        VersionManagerState state,
        string managedRoot,
        ManagedVersionAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(admission);
        return state.IsBoundToManagedRoot(managedRoot) &&
               state.UpdateSource is null &&
               state.ActiveVersion == admission.Version &&
               state.LastKnownGoodVersion == admission.Version &&
               state.Admissions is [var only] &&
               only == admission &&
               state.PendingActivation is null &&
               state.PendingMutation is null &&
               state.FailedActivationVersion is null &&
               !state.RetentionReviewDue &&
               state.SourceRegistryState is null;
    }
}

/// <summary>Creates first-run launcher state only from one explicit verified packaged seed.</summary>
public sealed class ManagedVersionSeedBootstrapper
{
    private readonly IVersionManagerStateStore _destinationStateStore;
    private readonly string _managedRoot;
    private readonly IManagedVersionRepository _repository;
    private readonly IVersionManagerStateStore _seedStateStore;

    /// <summary>Creates a bootstrapper over explicit destination and seed state ports.</summary>
    public ManagedVersionSeedBootstrapper(
        string managedRoot,
        IVersionManagerStateStore destinationStateStore,
        IVersionManagerStateStore seedStateStore,
        IManagedVersionRepository repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        _managedRoot = ManagedRootPathIdentity.Normalize(managedRoot);
        _destinationStateStore = destinationStateStore ??
            throw new ArgumentNullException(nameof(destinationStateStore));
        _seedStateStore = seedStateStore ?? throw new ArgumentNullException(nameof(seedStateStore));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>Ensures state exists without replacing malformed or unavailable user state.</summary>
    public async ValueTask<ManagedVersionSeedOutcome> EnsureInitializedAsync(
        TimeSpan writerLeaseTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(writerLeaseTimeout, TimeSpan.Zero);
        VersionManagerStateLoadResult preflight = await _destinationStateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (preflight.IsSuccess)
        {
            return preflight.State!.IsBoundToManagedRoot(_managedRoot)
                ? ManagedVersionSeedOutcome.ExistingState
                : ManagedVersionSeedOutcome.ManagedRootMismatch;
        }
        if (preflight.Issue != VersionManagerStateLoadIssue.Missing)
        {
            return ManagedVersionSeedOutcome.InvalidExistingState;
        }

        using VersionManagerWriteLeaseResult lease = await _destinationStateStore.TryAcquireWriteLeaseAsync(
            writerLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            return lease.Issue == VersionManagerWriteLeaseIssue.Busy
                ? ManagedVersionSeedOutcome.Busy
                : ManagedVersionSeedOutcome.StateUnavailable;
        }
        VersionManagerStateLoadResult existing = await _destinationStateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing.IsSuccess)
        {
            return existing.State!.IsBoundToManagedRoot(_managedRoot)
                ? ManagedVersionSeedOutcome.ExistingState
                : ManagedVersionSeedOutcome.ManagedRootMismatch;
        }
        if (existing.Issue != VersionManagerStateLoadIssue.Missing)
        {
            return ManagedVersionSeedOutcome.InvalidExistingState;
        }

        VersionManagerStateLoadResult seed = await _seedStateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!seed.IsSuccess)
        {
            return seed.Issue == VersionManagerStateLoadIssue.Missing
                ? ManagedVersionSeedOutcome.MissingSeed
                : ManagedVersionSeedOutcome.InvalidSeed;
        }
        VersionManagerState state = seed.State!;
        if (!ManagedVersionSeedPolicy.IsCanonicalFirstRunSeed(state))
        {
            return ManagedVersionSeedOutcome.InvalidSeed;
        }

        ManagedVersionInventoryReadResult inventoryResult = await _repository.InventoryAsync(
            _managedRoot,
            state.Admissions,
            state.ActiveVersion,
            state.LastKnownGoodVersion,
            state.FailedActivationVersion,
            cancellationToken).ConfigureAwait(false);
        if (!inventoryResult.IsSuccess)
        {
            return ManagedVersionSeedOutcome.StateUnavailable;
        }
        ManagedVersionInventory inventory = inventoryResult.Inventory!;
        if (inventory.Versions.Count != 1 ||
            inventory.HealthyCount != 1 ||
            inventory.DamagedCount != 0)
        {
            return ManagedVersionSeedOutcome.DamagedSeedPayload;
        }

        VersionManagerState bound = state.BindToManagedRoot(_managedRoot);
        VersionManagerStateSaveResult saved = await _destinationStateStore.TrySaveAsync(
            bound,
            cancellationToken).ConfigureAwait(false);
        return saved.IsSuccess
            ? ManagedVersionSeedOutcome.Seeded
            : ManagedVersionSeedOutcome.StateUnavailable;
    }
}
