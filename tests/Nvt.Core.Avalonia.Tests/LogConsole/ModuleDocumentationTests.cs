// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ModuleDocumentationTests
{
    [Theory]
    [InlineData("LogConsole.md", "## Avalonia public API")]
    [InlineData("LogConsole.zh-TW.md", "## Avalonia 公開 API")]
    public async Task AvaloniaApiTable_PublicTypes_MatchesCompiledSurfaceAsync(string name, string heading)
    {
        var lines = await TestFiles.ReadLinesAsync(DocumentPath(name), TestContext.Current.CancellationToken);
        var types = typeof(ConsoleController).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(ConsoleController).Namespace);

        var differences = TableDifferences(lines, heading, types);

        Assert.Empty(differences);
    }

    [Fact]
    public void AvaloniaApiTable_MissingSampleType_ReportsMissingType()
    {
        string[] sample = ["## Avalonia public API", "| Type | Public members |", "| --- | --- |",
            "| `ConsoleToolbar` | Constructor |", "## Another section", "| `ConsoleListView` | Outside table |"];

        var differences = TableDifferences(sample, sample[0], [typeof(ConsoleToolbar), typeof(ConsoleListView)]);

        Assert.Equal("Missing public type: ConsoleListView", Assert.Single(differences));
    }

    [Fact]
    public void AvaloniaApiTable_UnknownSampleType_ReportsUnknownType()
    {
        string[] sample = ["## Avalonia public API", "| Type | Public members |", "| --- | --- |",
            "| `ConsoleToolbar` | Constructor |", "| `ConsoleGhost` | Constructor |"];

        var differences = TableDifferences(sample, sample[0], [typeof(ConsoleToolbar)]);

        Assert.Equal("Unknown public type: ConsoleGhost", Assert.Single(differences));
    }

    private static string[] TableDifferences(IEnumerable<string> lines, string heading, IEnumerable<Type> types)
    {
        var documented = lines.SkipWhile(line => !string.Equals(line, heading, StringComparison.Ordinal)).Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|')[1].Trim().Trim('`')).ToHashSet(StringComparer.Ordinal);
        var actual = types.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        return [.. actual.Except(documented).Order(StringComparer.Ordinal).Select(name => "Missing public type: " + name),
            .. documented.Except(actual).Order(StringComparer.Ordinal).Select(name => "Unknown public type: " + name)];
    }

    private static string DocumentPath(string name)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        var relative = Path.Combine("docs", "core", "modules", name);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, relative);
    }
}
