// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// Text is derived from immutable state and resolved resource templates, never cached in the controller.
internal sealed class ConsoleTextConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string ?? string.Empty;
        string[] resources = key switch
        {
            "Time" => ["", "Time.Absolute", "Time.Relative", "Time.Hidden"],
            "Dedupe" => ["", "Dedupe.Count"],
            "Sources" => ["", "Sources.All", "Sources.Selected"],
            "Empty" => ["", "", "Empty.NoEvents", "Empty.NoMatches", "Filter.NoLevels", "Sources.All",
                "Filter.Search", "Filter.MatchesOnly", "Filter.Highlight", "Filter.Dedupe", "Filter.Separator", "Filter.Summary",
                "Level.Trace", "Level.Debug", "Level.Info", "Level.Warn", "Level.Error", "Level.Fatal"],
            _ when key.StartsWith("Level:", StringComparison.Ordinal) => ["", "Level." + key[6..], "Count"],
            _ => [],
        };
        values = values.Select((value, index) => index < resources.Length && resources[index].Length != 0
            && (value is null || ReferenceEquals(value, AvaloniaProperty.UnsetValue))
                ? ConsoleResourceText.Get(resources[index]) : value).ToArray();
        if (values.Any(value => value is null || ReferenceEquals(value, AvaloniaProperty.UnsetValue)))
            return AvaloniaProperty.UnsetValue;
        string Text(int index) => (string)values[index]!;
        return parameter switch
        {
            "Time" => (ConsoleTimeMode)values[0]! switch
            {
                ConsoleTimeMode.Absolute => Text(1), ConsoleTimeMode.Relative => Text(2), ConsoleTimeMode.Hidden => Text(3),
                _ => AvaloniaProperty.UnsetValue,
            },
            string levelKey when levelKey.StartsWith("Level:", StringComparison.Ordinal) => string.Format(culture, Text(2), Text(1), values[0]),
            "Dedupe" => string.Format(culture, Text(1), values[0]),
            "Sources" when values[0] is ConsoleFilter filter => filter.SelectedSources.IsEmpty
                ? Text(1) : string.Format(culture, Text(2), filter.SelectedSources.Count),
            "Empty" when values[0] is ConsoleFilter filter && values[1] is ConsoleProjection projection =>
                projection.EventCount == 0 ? Text(2) : string.Format(culture, Text(3), Summary(filter, values, culture)),
            _ => AvaloniaProperty.UnsetValue,
        };
    }

    private static string Summary(ConsoleFilter filter, IList<object?> values, CultureInfo culture)
    {
        string Text(int index) => (string)values[index]!;
        var levels = filter.EnabledLevels.IsEmpty ? Text(4)
            : string.Join(Text(10), filter.EnabledLevels.Order().Select(level => Text(level switch
            {
                LogLevel.Trace => 12, LogLevel.Debug => 13, LogLevel.Info => 14,
                LogLevel.Warn => 15, LogLevel.Error => 16, LogLevel.Fatal => 17,
                _ => throw new ArgumentOutOfRangeException(nameof(level)),
            })));
        var sources = filter.SelectedSources.IsEmpty ? Text(5)
            : string.Join(Text(10), filter.SelectedSources.Order(StringComparer.Ordinal));
        var search = filter.SearchText.Length == 0 ? string.Empty
            : string.Format(culture, Text(6), filter.SearchText, filter.OnlyMatches ? Text(7) : Text(8));
        return string.Format(culture, Text(11), levels, sources, search, filter.Deduplicate ? Text(9) : string.Empty);
    }
}
