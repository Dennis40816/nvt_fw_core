// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// Text is derived from immutable state and resolved resource templates, never cached in the controller.
internal sealed class ConsoleTextConverter : IMultiValueConverter
{
    private readonly ConsoleTemplateFormatter _formatter = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string ?? string.Empty;
        string[] resources = key switch
        {
            "Dedupe" => ["", "Dedupe.Count"],
            "Sources" => ["", "Sources.All", "Sources.Selected"],
            "Levels" => LevelResources(false),
            "LevelsName" => LevelResources(true),
            "ErrorsHidden" => ["", "", "Levels.ErrorsHidden"],
            "Zoom" => ["", "Zoom.Value"],
            "OnlyMatchesState" => ["", "OnlyMatches.On", "OnlyMatches.Off"],
            "ZoomName" => ["", "Zoom.Name"],
            "Empty" => ["", "", "Empty.NoEvents", "Empty.NoMatches", "Filter.NoLevels", "Sources.All",
                "Filter.Search", "Filter.MatchesOnly", "Filter.Highlight", "Filter.Dedupe", "Filter.Separator", "Filter.Summary",
                "Level.Trace", "Level.Debug", "Level.Info", "Level.Warn", "Level.Error", "Level.Fatal"],
            _ when key.StartsWith("Level:", StringComparison.Ordinal) => ["", "Level." + key[6..], "Count"],
            _ => [],
        };
        values = values.Select((value, index) => index < resources.Length && resources[index].Length != 0
            && (value is null || ReferenceEquals(value, AvaloniaProperty.UnsetValue))
                ? ConsoleResourceText.Get(resources[index]) : value).ToArray();
        // Without a list the zoom buttons read 100 percent, so their text and accessible name never go empty.
        if (parameter is "Zoom" or "ZoomName" && values[0] is not double) values[0] = 1.0;
        if (values.Any(value => value is null || ReferenceEquals(value, AvaloniaProperty.UnsetValue)))
            return AvaloniaProperty.UnsetValue;
        string Text(int index) => (string)values[index]!;
        return parameter switch
        {
            string levelKey when levelKey.StartsWith("Level:", StringComparison.Ordinal) => _formatter.Format("Count", Text(2), culture, Text(1), values[0]),
            "Dedupe" => _formatter.Format("Dedupe.Count", Text(1), culture, values[0]),
            "OnlyMatchesState" when values[0] is bool on => on ? Text(1) : Text(2),
            "Zoom" when values[0] is double zoom => _formatter.Format("Zoom.Value", Text(1), culture, zoom),
            "ZoomName" when values[0] is double zoom => _formatter.Format("Zoom.Name", Text(1), culture, zoom),
            "Levels" when values[0] is ConsoleFilter filter => LevelText(filter, values, culture),
            "LevelsName" when values[0] is ConsoleFilter filter && values[1] is ConsoleProjection projection
                => ConsoleLevelSummary.HiddenErrors(filter, projection) is var hidden && hidden > 0
                    ? _formatter.Format("Levels.NameHidden", Text(14), culture, LevelText(filter, values, culture), hidden.ToString("N0", culture))
                    : _formatter.Format("Levels.Name", Text(13), culture, LevelText(filter, values, culture)),
            "ErrorsHidden" when values[0] is ConsoleFilter filter && values[1] is ConsoleProjection projection
                => _formatter.Format("Levels.ErrorsHidden", Text(2), culture, ConsoleLevelSummary.HiddenErrors(filter, projection).ToString("N0", culture)),
            "Sources" when values[0] is ConsoleFilter filter => filter.SelectedSources.IsEmpty
                ? Text(1) : _formatter.Format("Sources.Selected", Text(2), culture, filter.SelectedSources.Count),
            "Empty" when values[0] is ConsoleFilter filter && values[1] is ConsoleProjection projection =>
                projection.EventCount == 0 ? Text(2) : _formatter.Format("Empty.NoMatches", Text(3), culture, Summary(filter, projection, values, culture)),
            _ => AvaloniaProperty.UnsetValue,
        };
    }

    // Positions: 0 filter, 1 projection, 2 to 6 label templates, 7 to 12 level names, 13 and 14 accessible-name templates.
    private static string[] LevelResources(bool withName) =>
    [
        "", "", "Levels.All", "Levels.AndAbove", "Levels.FatalOnly", "Levels.Some", "Levels.None",
        "Level.Trace", "Level.Debug", "Level.Info", "Level.Warn", "Level.Error", "Level.Fatal",
        .. withName ? new[] { "Levels.Name", "Levels.NameHidden" } : [],
    ];

    private string LevelText(ConsoleFilter filter, IList<object?> values, CultureInfo culture)
    {
        string Text(int index) => (string)values[index]!;
        var summary = ConsoleLevelSummary.Describe(filter);
        return summary.Kind switch
        {
            ConsoleLevelSummary.Kind.All => Text(2),
            ConsoleLevelSummary.Kind.None => Text(6),
            ConsoleLevelSummary.Kind.AndAbove => _formatter.Format("Levels.AndAbove", Text(3), culture, Text(7 + (int)summary.Floor)),
            ConsoleLevelSummary.Kind.FatalOnly => Text(4),
            _ => _formatter.Format("Levels.Some", Text(5), culture, summary.Enabled, Enum.GetValues<LogLevel>().Length),
        };
    }

    private string Summary(ConsoleFilter filter, ConsoleProjection projection, IList<object?> values, CultureInfo culture)
    {
        string Text(int index) => (string)values[index]!;
        var levels = filter.EnabledLevels.IsEmpty ? Text(4)
            : string.Join(Text(10), filter.EnabledLevels.Order().Select(level => Text(level switch
            {
                LogLevel.Trace => 12,
                LogLevel.Debug => 13,
                LogLevel.Info => 14,
                LogLevel.Warn => 15,
                LogLevel.Error => 16,
                LogLevel.Fatal => 17,
                _ => throw new ArgumentOutOfRangeException(nameof(level)),
            })));
        var sources = filter.SelectedSources.IsEmpty ? Text(5)
            : string.Join(Text(10), filter.SelectedSources.Order(StringComparer.Ordinal)
                .Select(id => projection.Sources.FirstOrDefault(source => source.SourceId == id)?.DisplayName ?? id));
        var search = filter.SearchText.Length == 0 ? string.Empty
            : _formatter.Format("Filter.Search", Text(6), culture, filter.SearchText, filter.OnlyMatches ? Text(7) : Text(8));
        return _formatter.Format("Filter.Summary", Text(11), culture, levels, sources, search, filter.Deduplicate ? Text(9) : string.Empty);
    }
}
