// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>App source names, deterministic catalog order, and ID-based filtering.</summary>
public sealed class SourceRegistryTests
{
    private static readonly string[] DeclaredIds = ["idle", "app", "runtime"];
    private static readonly string[] DeclaredNames = ["Idle service", "Application", "Runtime queries"];
    private static readonly string[] UnknownIds = ["declared", "z", "a"];
    private static readonly string[] UnknownNames = ["Declared", "z", "a"];
    private static readonly string[] ChangedIds = ["new", "app"];
    /// <summary>Declared metadata seeds zero counts and display order independently of event order.</summary>
    [Fact]
    public void RegistrySuppliesNamesOrderAndZeroEventSources()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "runtime", "first");
        store.Add(LogLevel.Info, "app", "second");
        using var snapshot = LogStoreTests.Capture(store);
        var options = new ConsoleProjectionOptions
        {
            SourceRegistry = [new("runtime", "Runtime queries", 20), new("idle", "Idle service", 10), new("app", "Application", 10)],
        };
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState(), options);
        Assert.Equal(DeclaredIds, projection.Sources.Select(source => source.SourceId));
        Assert.Equal(DeclaredNames, projection.Sources.Select(source => source.DisplayName));
        Assert.Equal(0, projection.SourceCounts["idle"]);
        Assert.Equal(1, projection.SourceCounts["app"]);
        Assert.Equal(1, projection.SourceCounts["runtime"]);
    }

    /// <summary>Unknown sources follow all declarations and retain first retained sequence order.</summary>
    [Fact]
    public void UnknownSourcesFollowRegistryInFirstAppearanceOrder()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "z", "first");
        store.Add(LogLevel.Error, "a", "second");
        store.Add(LogLevel.Info, "z", "third");
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot,
            new ConsoleFilter { EnabledLevels = [], SearchText = "absent", Deduplicate = true }, new ConsoleViewState(),
            new ConsoleProjectionOptions { SourceRegistry = [new("declared", "Declared", int.MaxValue)] });
        Assert.Equal(UnknownIds, projection.Sources.Select(source => source.SourceId));
        Assert.Equal(UnknownNames, projection.Sources.Select(source => source.DisplayName));
        Assert.All(projection.SourceCounts.Values, count => Assert.Equal(0, count));
        Assert.Empty(projection.Rows);
    }

    /// <summary>Names never replace ordinal IDs in filters or count keys.</summary>
    [Fact]
    public void SourceSelectionUsesIdsAndCountsUseOnlyLevelFilter()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "same");
        store.Add(LogLevel.Info, "app", "same");
        store.Add(LogLevel.Error, "app", "error");
        store.Add(LogLevel.Info, "App", "case distinct");
        using var snapshot = LogStoreTests.Capture(store);
        var options = new ConsoleProjectionOptions { SourceRegistry = [new("app", "Application"), new("idle", "Idle")] };
        var filter = new ConsoleFilter { SelectedSources = ["app"], EnabledLevels = [LogLevel.Info], Deduplicate = true };
        using var projection = ConsoleProjector.Project(snapshot, filter, new ConsoleViewState(), options);
        Assert.Equal("app", Assert.Single(projection.Rows).SourceId);
        Assert.Equal(2, projection.SourceCounts["app"]);
        Assert.Equal(1, projection.SourceCounts["App"]);
        Assert.Equal(0, projection.SourceCounts["idle"]);
        Assert.Equal(1, projection.LevelCounts[LogLevel.Error]);
        using var byName = ConsoleProjector.Project(snapshot, filter with { SelectedSources = ["Application"] }, new ConsoleViewState(), options);
        Assert.Empty(byName.Rows);
    }

    /// <summary>A replacement registry changes the next projection of the same snapshot only.</summary>
    [Fact]
    public void RegistryChangesBetweenProjectionsWithoutChangingStore()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "message");
        using var snapshot = LogStoreTests.Capture(store);
        var options = new ConsoleProjectionOptions { SourceRegistry = [new("app", "Old name"), new("removed", "Removed")] };
        using var first = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState(), options);
        using var second = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState(), options with
        {
            SourceRegistry = [new("new", "New zero source", -1), new("app", "New name", 1)],
        });
        Assert.Equal(first.Version, second.Version);
        Assert.Equal("Old name", first.Sources[0].DisplayName);
        Assert.Equal(ChangedIds, second.Sources.Select(source => source.SourceId));
        Assert.Equal("New name", second.Sources[1].DisplayName);
        Assert.Equal(0, second.SourceCounts["new"]);
        Assert.False(second.SourceCounts.ContainsKey("removed"));
    }

    /// <summary>An empty event snapshot still includes the complete declared catalog.</summary>
    [Fact]
    public void EmptySnapshotKeepsDeclaredSources()
    {
        using var store = LogStoreTests.CreateStore();
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState(),
            new ConsoleProjectionOptions { SourceRegistry = [new("idle", "Idle")] });
        Assert.Equal("Idle", Assert.Single(projection.Sources).DisplayName);
        Assert.Equal(0, projection.SourceCounts["idle"]);
    }

    /// <summary>Ambiguous duplicate IDs cannot silently overwrite catalog metadata.</summary>
    [Fact]
    public void DuplicateRegistryIdsAreRejected()
    {
        using var store = LogStoreTests.CreateStore();
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Throws<ArgumentException>(() => ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState(),
            new ConsoleProjectionOptions { SourceRegistry = [new("app", "First"), new("app", "Second")] }));
    }
}
