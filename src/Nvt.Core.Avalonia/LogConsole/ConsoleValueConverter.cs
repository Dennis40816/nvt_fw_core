// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal sealed class ConsoleValueConverter : IValueConverter, IMultiValueConverter
{
    internal StyledElement? ResourceHost { get; init; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string ?? string.Empty;
        if (key.StartsWith("Level:", StringComparison.Ordinal))
        {
            var level = Enum.Parse<LogLevel>(key[6..]);
            return value switch
            {
                ConsoleFilter filter => filter.EnabledLevels.Contains(level),
                ConsoleProjection projection => string.Format(culture, System.Text.CompositeFormat.Parse(ConsoleResourceText.Get("Count", ResourceHost)), ConsoleResourceText.Get(LevelKey(level), ResourceHost), projection.LevelCounts[level]),
                _ => null,
            };
        }
        if (key.StartsWith("Time:", StringComparison.Ordinal))
            return value is ConsoleTimeMode mode && mode == Enum.Parse<ConsoleTimeMode>(key[5..]);
        if (key.StartsWith("Source:", StringComparison.Ordinal))
        {
            var id = key[7..];
            return value switch
            {
                ConsoleProjection projection => string.Format(culture, System.Text.CompositeFormat.Parse(ConsoleResourceText.Get("Count", ResourceHost)), projection.Sources.FirstOrDefault(source => source.SourceId == id)?.DisplayName ?? id, projection.SourceCounts.GetValueOrDefault(id)),
                _ => null,
            };
        }
        return key switch
        {
            "HasEvents" => value is int count && count != 0,
            "EmptyText" => string.IsNullOrEmpty(value as string),
            "AllSources" when value is ConsoleFilter filter => filter.SelectedSources.IsEmpty,
            "AllSources" when value is ConsoleProjection projection => string.Format(culture, System.Text.CompositeFormat.Parse(ConsoleResourceText.Get("Count", ResourceHost)), ConsoleResourceText.Get("Sources.All", ResourceHost), projection.SourceCounts.Values.Sum()),
            "SourceSummary" when value is ConsoleFilter filter => filter.SelectedSources.IsEmpty ? ConsoleResourceText.Get("Sources.All", ResourceHost) : string.Format(culture, System.Text.CompositeFormat.Parse(ConsoleResourceText.Get("Sources.Selected", ResourceHost)), filter.SelectedSources.Count),
            _ => value,
        };
    }

    private static string LevelKey(LogLevel level) => level switch
    {
        LogLevel.Trace => "Level.Trace", LogLevel.Debug => "Level.Debug", LogLevel.Info => "Level.Info",
        LogLevel.Warn => "Level.Warn", LogLevel.Error => "Level.Error", LogLevel.Fatal => "Level.Fatal",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string textKey && textKey.StartsWith("SourceText:", StringComparison.Ordinal)
            && values.Count == 3 && values[0] is ConsoleProjection textProjection)
        {
            var all = textKey == "SourceText:All";
            var sourceId = all ? string.Empty : textKey[14..];
            var label = all ? values[2] as string ?? ConsoleResourceText.Get("Sources.All", ResourceHost)
                : textProjection.Sources.FirstOrDefault(source => source.SourceId == sourceId)?.DisplayName ?? sourceId;
            var count = all ? textProjection.SourceCounts.Values.Sum() : textProjection.SourceCounts.GetValueOrDefault(sourceId);
            var template = values[1] as string ?? ConsoleResourceText.Get("Count", ResourceHost);
            return string.Format(culture, System.Text.CompositeFormat.Parse(template), label, count);
        }
        if (values.Count != 2 || values[0] is not ConsoleFilter filter || values[1] is not ConsoleProjection projection)
            return false;
        var key = parameter as string ?? string.Empty;
        if (key == "AllSources") return filter.SelectedSources.IsEmpty;
        if (!key.StartsWith("Source:", StringComparison.Ordinal)) return false;
        var id = key[7..];
        return projection.Sources.Any(source => source.SourceId == id)
            && (filter.SelectedSources.IsEmpty || filter.SelectedSources.Contains(id));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
