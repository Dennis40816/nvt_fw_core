// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.IntegrationTestData;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Frozen replica arbitration and public construction contracts.</summary>
public sealed class ReplicatedUpdateSourceRegistryTests
{
    /// <summary>The highest valid revision wins independently of order.</summary>
    [Fact]
    public async Task HighestValidRevisionWins()
    {
        UpdateSourceRegistrySnapshot primary = Snapshot(7, 'a');
        UpdateSourceRegistrySnapshot backup = Snapshot(8, 'b');
        var registry = new ReplicatedUpdateSourceRegistry([Stub(Success(primary)), Stub(Success(backup))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Same(backup, outcome.Snapshot);
        Assert.Equal(UpdateSourceRegistryLoadIssue.None, outcome.Issue);
    }

    /// <summary>A newer primary wins and observations retain the stale revision.</summary>
    [Fact]
    public async Task NewerPrimaryWinsOverOlderBackup()
    {
        UpdateSourceRegistrySnapshot primary = Snapshot(8, 'b');
        var registry = new ReplicatedUpdateSourceRegistry([Stub(Success(primary)), Stub(Success(Snapshot(7)))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Same(primary, outcome.Snapshot);
        Assert.Collection(outcome.Replicas!, replica => Assert.True(replica.IsSelected), replica =>
        {
            Assert.False(replica.IsSelected);
            Assert.Equal(7, replica.RegistryRevision);
        });
        Assert.Equal([1, 2], outcome.Replicas!.Select(static replica => replica.Position));
    }

    /// <summary>One invalid replica cannot hide a valid complete backup.</summary>
    [Fact]
    public async Task OneInvalidReplicaFallsBackToOtherValidReplica()
    {
        UpdateSourceRegistrySnapshot backup = Snapshot(4, 'c');
        var registry = new ReplicatedUpdateSourceRegistry(
            [Stub(new(null, UpdateSourceRegistryLoadIssue.InvalidManifest)), Stub(Success(backup))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Same(backup, outcome.Snapshot);
        Assert.Equal(UpdateSourceRegistryLoadIssue.None, outcome.Issue);
    }

    /// <summary>A failure carrying a higher revision cannot participate in authority selection.</summary>
    [Fact]
    public async Task FailedReceiptCannotSelectItsSnapshot()
    {
        UpdateSourceRegistrySnapshot backup = Snapshot(4, 'c');
        var registry = new ReplicatedUpdateSourceRegistry([
            Stub(new(Snapshot(99), UpdateSourceRegistryLoadIssue.InvalidManifest)), Stub(Success(backup))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Same(backup, outcome.Snapshot);
        Assert.Equal(99, outcome.Replicas![0].RegistryRevision);
        Assert.False(outcome.Replicas[0].IsSelected);
        Assert.True(outcome.Replicas[1].IsSelected);
    }

    /// <summary>With no valid publication the primary issue remains deterministic.</summary>
    [Fact]
    public async Task NoValidReplicaReturnsPrimaryFailure()
    {
        var registry = new ReplicatedUpdateSourceRegistry([
            Stub(new(null, UpdateSourceRegistryLoadIssue.RegistryMissing)),
            Stub(new(null, UpdateSourceRegistryLoadIssue.InvalidManifest))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Null(outcome.Snapshot);
        Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryMissing, outcome.Issue);
        Assert.Equal([UpdateSourceRegistryLoadIssue.RegistryMissing, UpdateSourceRegistryLoadIssue.InvalidManifest],
            outcome.Replicas!.Select(static replica => replica.Issue));
        Assert.All(outcome.Replicas!, static replica => Assert.False(replica.IsSelected));
    }

    /// <summary>Equal publications select the original primary snapshot.</summary>
    [Fact]
    public async Task SameRevisionAndDigestSelectPrimary()
    {
        UpdateSourceRegistrySnapshot primary = Snapshot(9, 'd');
        var registry = new ReplicatedUpdateSourceRegistry([Stub(Success(primary)), Stub(Success(Snapshot(9, 'd')))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Same(primary, outcome.Snapshot);
    }

    /// <summary>Digest conflicts at the highest revision reject the publication.</summary>
    [Fact]
    public async Task SameRevisionDifferentDigestFailsClosed()
    {
        var registry = new ReplicatedUpdateSourceRegistry(
            [Stub(Success(Snapshot(9, 'd'))), Stub(Success(Snapshot(9, 'e')))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Null(outcome.Snapshot);
        Assert.Equal(UpdateSourceRegistryLoadIssue.ReplicaConflict, outcome.Issue);
    }

    /// <summary>Conflicts confined to an older revision do not reject the newest publication.</summary>
    [Fact]
    public async Task OlderDigestConflictDoesNotOverrideHighestRevision()
    {
        UpdateSourceRegistrySnapshot newest = Snapshot(10, 'f');
        var registry = new ReplicatedUpdateSourceRegistry([
            Stub(Success(Snapshot(9, 'd'))), Stub(Success(Snapshot(9, 'e'))), Stub(Success(newest))]);
        Assert.Same(newest, (await registry.LoadAsync(TestContext.Current.CancellationToken)).Snapshot);
    }

    /// <summary>Registry identity conflicts reject even an older different authority.</summary>
    [Fact]
    public async Task ConflictingRegistryIdsRejectBeforeRevisionSelection()
    {
        var registry = new ReplicatedUpdateSourceRegistry([
            Stub(Success(Snapshot(1, registryId: "other-registry"))), Stub(Success(Snapshot(9)))]);
        UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Null(outcome.Snapshot);
        Assert.Equal(UpdateSourceRegistryLoadIssue.ReplicaConflict, outcome.Issue);
        Assert.All(outcome.Replicas!, static replica => Assert.False(replica.IsSelected));
    }

    /// <summary>The runtime rejects authority that differs from the explicit product descriptor.</summary>
    [Fact]
    public void ForeignRegistryAuthorityCannotEnterTheRuntimeModel()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => UpdateSourceRegistrySnapshot.Create(
            Limits, Descriptor(), "foreign-registry", 10, PublishedAt,
            UpdateCatalogPublicationAssertion.Create("1.0.1", 1, Hash), Hash, [Entry()]));
        Assert.StartsWith("Update-source registry snapshot is inconsistent.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Only one through eight nonnull replica ports are admitted.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task ReplicaCountIncludesBothBoundaries(int count)
    {
        var registry = new ReplicatedUpdateSourceRegistry(
            Enumerable.Range(0, count).Select(_ => Stub(Success(Snapshot()))).ToArray());
        Assert.Equal(count, (await registry.LoadAsync(TestContext.Current.CancellationToken)).Replicas!.Count);
    }

    /// <summary>Empty and excessive replica collections retain the frozen message.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ReplicaCountOutsideBoundsIsRejected(int count)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new ReplicatedUpdateSourceRegistry(
            Enumerable.Range(0, count).Select(_ => Stub(Success(Snapshot()))).ToArray()));
        Assert.StartsWith("Registry replica set is invalid.", exception.Message, StringComparison.Ordinal);
        Assert.Equal("replicas", exception.ParamName);
    }

    /// <summary>Null collections and ports cannot supply default admission.</summary>
    [Fact]
    public void MissingReplicaPortsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ReplicatedUpdateSourceRegistry(null!));
        Assert.Throws<ArgumentException>(() => new ReplicatedUpdateSourceRegistry([null!]));
    }

    /// <summary>The internal timeout rejects zero and negative ticks.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveTimeoutIsRejected(long ticks)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new ReplicatedUpdateSourceRegistry(
            [Stub(Success(Snapshot()))], TimeSpan.FromTicks(ticks), Clock()));
        Assert.Equal("replicas", exception.ParamName);
        Assert.StartsWith("Registry replica set is invalid.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Replica order is captured during construction instead of retaining the caller's array.</summary>
    [Fact]
    public async Task RegistryOwnsConfiguredReplicaSet()
    {
        UpdateSourceRegistrySnapshot original = Snapshot();
        IUpdateSourceRegistry[] ports = [Stub(Success(original))];
        var registry = new ReplicatedUpdateSourceRegistry(ports);
        ports[0] = Stub(new(null, UpdateSourceRegistryLoadIssue.RegistryMissing));
        Assert.Same(original, (await registry.LoadAsync(TestContext.Current.CancellationToken)).Snapshot);
    }
}
