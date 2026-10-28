// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Integration;

/// <summary>Selects one complete authoritative publication from ordered replicas.</summary>
public sealed class ReplicatedUpdateSourceRegistry : IUpdateSourceRegistry
{
    private static readonly TimeSpan _defaultReplicaTimeout = TimeSpan.FromSeconds(45);
    private readonly IReadOnlyList<ReplicaReadSlot> _replicas;

    /// <summary>Creates an ordered replica set; index zero is the primary.</summary>
    public ReplicatedUpdateSourceRegistry(IReadOnlyList<IUpdateSourceRegistry> replicas)
        : this(replicas, _defaultReplicaTimeout)
    {
    }

    internal ReplicatedUpdateSourceRegistry(
        IReadOnlyList<IUpdateSourceRegistry> replicas,
        TimeSpan replicaTimeout,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(replicas);
        if (replicas.Count is < 1 or > 8 ||
            replicas.Any(static replica => replica is null) ||
            replicaTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Registry replica set is invalid.", nameof(replicas));
        }
        TimeProvider effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        _replicas =
        [
            .. replicas.Select(
                replica => new ReplicaReadSlot(replica, replicaTimeout, effectiveTimeProvider)),
        ];
    }

    /// <inheritdoc />
    public async ValueTask<UpdateSourceRegistryLoadResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<UpdateSourceRegistryLoadResult>[] reads =
        [.. _replicas.Select(replica => replica.LoadAsync(cancellationToken))];
        UpdateSourceRegistryLoadResult[] results = await Task.WhenAll(reads).ConfigureAwait(false);
        UpdateSourceRegistrySnapshot[] valid =
        [.. results.Where(static result => result.IsSuccess).Select(static result => result.Snapshot!)];
        if (valid.Length == 0)
        {
            return Publish(results, snapshot: null, results[0].Issue);
        }
        if (valid.Select(static snapshot => snapshot.RegistryId)
            .Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            return Publish(results, snapshot: null, UpdateSourceRegistryLoadIssue.ReplicaConflict);
        }

        long highestRevision = valid.Max(static snapshot => snapshot.RegistryRevision);
        UpdateSourceRegistrySnapshot[] newest =
        [.. valid.Where(snapshot => snapshot.RegistryRevision == highestRevision)];
        return newest.Select(static snapshot => snapshot.ContentDigest)
            .Distinct(StringComparer.Ordinal).Skip(1).Any()
                ? Publish(results, snapshot: null, UpdateSourceRegistryLoadIssue.ReplicaConflict)
                : Publish(results, newest[0], UpdateSourceRegistryLoadIssue.None);
    }

    private static UpdateSourceRegistryLoadResult Publish(
        IReadOnlyList<UpdateSourceRegistryLoadResult> results,
        UpdateSourceRegistrySnapshot? snapshot,
        UpdateSourceRegistryLoadIssue issue)
    {
        return new(
            snapshot,
            issue,
            [.. results.Select((result, index) => new UpdateSourceRegistryReplicaObservation(
                index + 1,
                result.Issue,
                result.Snapshot?.RegistryRevision,
                snapshot is not null && ReferenceEquals(result.Snapshot, snapshot))) ]);
    }
}
