// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.IntegrationTestData;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Registry factories preserve frozen consistency checks and own published collections.</summary>
public sealed class RegistryContractTests
{
    /// <summary>Canonical versions are parsed before schema and digest validation.</summary>
    [Fact]
    public void PublicationChecksVersionBeforeSchemaAndDigest()
    {
        Assert.Throws<FormatException>(() => UpdateCatalogPublicationAssertion.Create("01.0.1", 0, "invalid"));
    }

    /// <summary>The positive schema check precedes digest validation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PublicationChecksSchemaBeforeDigest(int schema)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpdateCatalogPublicationAssertion.Create("1.0.1", schema, "invalid"));
        Assert.Equal("catalogSchemaVersion", exception.ParamName);
    }

    /// <summary>The publication digest check preserves the frozen literal.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void PublicationRejectsInvalidDigest(string? digest)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            UpdateCatalogPublicationAssertion.Create("1.0.1", 1, digest!));
        Assert.Equal("catalogSha256", exception.ParamName);
        Assert.StartsWith("Catalog SHA-256 is invalid.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Registry entry factories preserve exact catalog and parent paths.</summary>
    [Fact]
    public void EntryPreservesAdmittedPathsAndRole()
    {
        UpdateSourceRegistryEntry entry = Entry();
        Assert.Equal(Path.GetDirectoryName(entry.CatalogPath), entry.SourceRoot);
        Assert.Equal(UpdateSourceRegistryEntryStatus.Latest, entry.Status);
    }

    /// <summary>Undefined roles retain the original exception parameter.</summary>
    [Fact]
    public void EntryRejectsUndefinedStatus()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpdateSourceRegistryEntry.Create(Entry().CatalogPath, (UpdateSourceRegistryEntryStatus)999));
        Assert.Equal("status", exception.ParamName);
    }

    /// <summary>The checked public entry factory requires canonical absolute file paths.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative/catalog.json")]
    public void EntryRejectsMissingOrRelativePaths(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => UpdateSourceRegistryEntry.Create(path!, UpdateSourceRegistryEntryStatus.Latest));
    }

    /// <summary>Path normalization and directory-only inputs cannot bypass entry admission.</summary>
    [Fact]
    public void EntryRejectsNoncanonicalAndDirectoryPaths()
    {
        string root = Path.GetFullPath("registry-fixture");
        Assert.Throws<ArgumentException>(() => UpdateSourceRegistryEntry.Create(
            Path.Combine(root, "..", "catalog.json"), UpdateSourceRegistryEntryStatus.Latest));
        Assert.Throws<ArgumentException>(() => UpdateSourceRegistryEntry.Create(
            root + Path.DirectorySeparatorChar, UpdateSourceRegistryEntryStatus.Latest));
    }

    /// <summary>The original argument checks precede the snapshot consistency predicate.</summary>
    [Fact]
    public void SnapshotRetainsArgumentExceptionOrder()
    {
        Assert.Throws<ArgumentNullException>(() => UpdateSourceRegistrySnapshot.Create(null!, null!, "", 0,
            PublishedAt, null!, "", null!));
        ArgumentException id = Assert.Throws<ArgumentException>(() => UpdateSourceRegistrySnapshot.Create(
            Limits, Descriptor(), " ", 0, PublishedAt, null!, "", null!));
        Assert.Equal("registryId", id.ParamName);
        ArgumentOutOfRangeException revision = Assert.Throws<ArgumentOutOfRangeException>(() => UpdateSourceRegistrySnapshot.Create(
            Limits, Descriptor(), "foreign", 0, PublishedAt, null!, "", null!));
        Assert.Equal("registryRevision", revision.ParamName);
        ArgumentNullException publication = Assert.Throws<ArgumentNullException>(() => UpdateSourceRegistrySnapshot.Create(
            Limits, Descriptor(), "foreign", 1, PublishedAt, null!, "", null!));
        Assert.Equal("catalogPublication", publication.ParamName);
        ArgumentNullException entries = Assert.Throws<ArgumentNullException>(() => UpdateSourceRegistrySnapshot.Create(
            Limits, Descriptor(), "foreign", 1, PublishedAt, UpdateCatalogPublicationAssertion.Create("1.0.1", 1, Hash), "", null!));
        Assert.Equal("entries", entries.ParamName);
    }

    /// <summary>Each inconsistent normalized registry fact rejects the complete publication.</summary>
    [Theory]
    [InlineData("identity")]
    [InlineData("utc")]
    [InlineData("digest")]
    [InlineData("null-entry")]
    [InlineData("no-latest")]
    [InlineData("two-latest")]
    [InlineData("duplicate-path")]
    [InlineData("duplicate-root")]
    public void SnapshotRejectsInconsistentFacts(string fact)
    {
        UpdateSourceRegistryEntry entry = Entry();
        UpdateSourceRegistryEntry sameRoot = UpdateSourceRegistryEntry.Create(
            Path.Combine(entry.SourceRoot, "other.json"), UpdateSourceRegistryEntryStatus.Available);
        UpdateSourceRegistryEntry[] entries = fact switch
        {
            "null-entry" => [null!],
            "no-latest" => [Entry(0, UpdateSourceRegistryEntryStatus.Available)],
            "two-latest" => [entry, Entry(1)],
            "duplicate-path" => [entry, UpdateSourceRegistryEntry.Create(entry.CatalogPath, UpdateSourceRegistryEntryStatus.Available)],
            "duplicate-root" => [entry, sameRoot],
            _ => [entry],
        };
        ArgumentException exception = Assert.Throws<ArgumentException>(() => UpdateSourceRegistrySnapshot.Create(
            Limits, Descriptor(), fact == "identity" ? "FIXTURE-REGISTRY" : "fixture-registry", 1,
            fact == "utc" ? PublishedAt.ToOffset(TimeSpan.FromHours(1)) : PublishedAt,
            UpdateCatalogPublicationAssertion.Create("1.0.1", 1, Hash), fact == "digest" ? "invalid" : Hash, entries));
        Assert.Equal("entries", exception.ParamName);
        Assert.StartsWith("Update-source registry snapshot is inconsistent.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Latest is followed by available entries in declared order, excluding deprecated entries.</summary>
    [Fact]
    public void AutomaticCandidatesPreserveFrozenOrder()
    {
        UpdateSourceRegistryEntry available = Entry(1, UpdateSourceRegistryEntryStatus.Available);
        UpdateSourceRegistryEntry latest = Entry(2);
        UpdateSourceRegistryEntry fallback = Entry(3, UpdateSourceRegistryEntryStatus.Available);
        UpdateSourceRegistrySnapshot snapshot = Snapshot(entries: [available, Entry(0, UpdateSourceRegistryEntryStatus.Deprecated), latest, fallback]);
        Assert.Equal(new[] { latest, available, fallback }, snapshot.AutomaticCandidates());
    }

    /// <summary>Registry rows and replica observations own read-only copies, including record initialization.</summary>
    [Fact]
    public void PublishedCollectionsOwnReadOnlyCopies()
    {
        UpdateSourceRegistryEntry original = Entry();
        UpdateSourceRegistryEntry[] entries = [original];
        UpdateSourceRegistrySnapshot snapshot = Snapshot(entries: entries);
        entries[0] = Entry(1);
        Assert.Same(original, snapshot.Entries[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<UpdateSourceRegistryEntry>)snapshot.Entries)[0] = entries[0]);
        UpdateSourceRegistryReplicaObservation first = new(1, UpdateSourceRegistryLoadIssue.None, 1, true);
        UpdateSourceRegistryReplicaObservation[] observations = [first];
        var loaded = new UpdateSourceRegistryLoadResult(snapshot, UpdateSourceRegistryLoadIssue.None, observations);
        UpdateSourceRegistryLoadResult copied = loaded with { Replicas = observations };
        observations[0] = new(2, UpdateSourceRegistryLoadIssue.RegistryMissing, null, false);
        Assert.Same(first, loaded.Replicas![0]);
        Assert.Same(first, copied.Replicas![0]);
        Assert.Throws<NotSupportedException>(() => ((IList<UpdateSourceRegistryReplicaObservation>)loaded.Replicas)[0] = observations[0]);
    }

    /// <summary>Both a snapshot and no issue are required for a successful registry load.</summary>
    [Fact]
    public void RegistryLoadResultRetainsFrozenSuccessPredicate()
    {
        UpdateSourceRegistrySnapshot snapshot = Snapshot();
        Assert.True(Success(snapshot).IsSuccess);
        Assert.False(new UpdateSourceRegistryLoadResult(null, UpdateSourceRegistryLoadIssue.None).IsSuccess);
        Assert.False(new UpdateSourceRegistryLoadResult(snapshot, UpdateSourceRegistryLoadIssue.InvalidManifest).IsSuccess);
        Assert.False(new UpdateSourceRegistryLoadResult(snapshot, (UpdateSourceRegistryLoadIssue)999).IsSuccess);
    }
}
