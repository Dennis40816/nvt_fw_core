// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ModuleDocumentationTests
{
    private const string SampleHeading = "## Avalonia public API";

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
    public void AvaloniaApiTable_CompleteSample_ReportsNoDifference()
    {
        string[] sample = [SampleHeading, "| Type | Public members |", "| --- | --- |",
            "| `SampleToolbar` | Constructor `()`; get/set `Controller`; static readonly `ControllerProperty`; `Refresh()`. |",
            "| `SampleList` | `Offset`. |"];

        var differences = TableDifferences(sample, SampleHeading, [typeof(SampleToolbar), typeof(SampleList)]);

        Assert.Empty(differences);
    }

    [Fact]
    public void AvaloniaApiTable_MissingSampleType_ReportsMissingType()
    {
        string[] sample = [SampleHeading, "| Type | Public members |", "| --- | --- |",
            "| `SampleToolbar` | `Controller`, `ControllerProperty`, `Refresh()` |", "## Another section", "| `SampleList` | `Offset` |"];

        var differences = TableDifferences(sample, SampleHeading, [typeof(SampleToolbar), typeof(SampleList)]);

        Assert.Equal("Missing public type: SampleList", Assert.Single(differences));
    }

    [Fact]
    public void AvaloniaApiTable_UnknownSampleType_ReportsUnknownType()
    {
        string[] sample = [SampleHeading, "| Type | Public members |", "| --- | --- |",
            "| `SampleToolbar` | `Controller`, `ControllerProperty`, `Refresh()` |", "| `SampleGhost` | Constructor |"];

        var differences = TableDifferences(sample, SampleHeading, [typeof(SampleToolbar)]);

        Assert.Equal("Unknown public type: SampleGhost", Assert.Single(differences));
    }

    [Fact]
    public void AvaloniaApiTable_MissingSampleMember_ReportsMissingMember()
    {
        string[] sample = [SampleHeading, "| Type | Public members |", "| --- | --- |",
            "| `SampleToolbar` | `Controller`, `Refresh()` |"];

        var differences = TableDifferences(sample, SampleHeading, [typeof(SampleToolbar)]);

        Assert.Equal("Missing public member: SampleToolbar.ControllerProperty", Assert.Single(differences));
    }

    [Fact]
    public void AvaloniaApiTable_UnknownSampleMember_ReportsUnknownMember()
    {
        string[] sample = [SampleHeading, "| Type | Public members |", "| --- | --- |",
            "| `SampleToolbar` | `Controller`, `ControllerProperty`, `Refresh()`, `Vanish()` |"];

        var differences = TableDifferences(sample, SampleHeading, [typeof(SampleToolbar)]);

        Assert.Equal("Unknown public member: SampleToolbar.Vanish", Assert.Single(differences));
    }

    private static string[] TableDifferences(IEnumerable<string> lines, string heading, IEnumerable<Type> types)
    {
        var documented = lines.SkipWhile(line => !string.Equals(line, heading, StringComparison.Ordinal)).Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|'))
            .ToDictionary(cells => cells[1].Trim().Trim('`'), cells => DocumentedMembers(cells[2]), StringComparer.Ordinal);
        var actual = types.ToDictionary(type => type.Name, PublicMemberNames, StringComparer.Ordinal);
        return [.. actual.Keys.Except(documented.Keys).Order(StringComparer.Ordinal).Select(name => "Missing public type: " + name),
            .. documented.Keys.Except(actual.Keys).Order(StringComparer.Ordinal).Select(name => "Unknown public type: " + name),
            .. actual.Keys.Intersect(documented.Keys).Order(StringComparer.Ordinal)
                .SelectMany(name => MemberDifferences(name, actual[name], documented[name]))];
    }

    private static IEnumerable<string> MemberDifferences(string type, HashSet<string> actual, HashSet<string> documented)
        => actual.Except(documented).Order(StringComparer.Ordinal).Select(member => $"Missing public member: {type}.{member}")
            .Concat(documented.Except(actual).Order(StringComparer.Ordinal).Select(member => $"Unknown public member: {type}.{member}"));

    // The leading identifier of every code span in the cell; spans such as `(store, sources)` start with a bracket and name no member.
    private static HashSet<string> DocumentedMembers(string cell)
        => cell.Split('`').Where((_, index) => index % 2 == 1)
            .Select(span => new string([.. span.TakeWhile(character => char.IsAsciiLetterOrDigit(character) || character == '_')]))
            .Where(name => name.Length > 0 && !char.IsAsciiDigit(name[0]))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> PublicMemberNames(Type type)
        => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is not MemberTypes.Constructor and not MemberTypes.NestedType)
            .Where(member => member is not MethodBase { IsSpecialName: true })
            .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static string DocumentPath(string name)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        var relative = Path.Combine("docs", "core", "modules", name);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, relative);
    }

    // Public members on private types stand in for a documented API surface.
    private sealed class SampleToolbar
    {
        public static readonly string ControllerProperty = nameof(Controller);
        public int Controller { get; set; }
        public void Refresh() => Controller++;
    }

    private sealed class SampleList
    {
        public int Offset { get; set; }
    }
}
