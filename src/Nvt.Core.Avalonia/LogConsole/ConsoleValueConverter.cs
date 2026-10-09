// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia.Data.Converters;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal sealed class ConsoleValueConverter : IValueConverter, IMultiValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string ?? string.Empty;
        if (key.StartsWith("Level:", StringComparison.Ordinal))
        {
            var level = Enum.Parse<LogLevel>(key[6..]);
            return value switch
            {
                ConsoleFilter filter => filter.EnabledLevels.Contains(level),
                ConsoleProjection projection => $"{level} · {projection.LevelCounts[level].ToString(CultureInfo.InvariantCulture)}",
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
                ConsoleProjection projection => $"{projection.Sources.FirstOrDefault(source => source.SourceId == id)?.DisplayName ?? id} · {projection.SourceCounts.GetValueOrDefault(id).ToString(CultureInfo.InvariantCulture)}",
                _ => null,
            };
        }
        return key switch
        {
            "EmptyText" => string.IsNullOrEmpty(value as string),
            "AllSources" when value is ConsoleFilter filter => filter.SelectedSources.IsEmpty,
            "AllSources" when value is ConsoleProjection projection => $"All sources · {projection.SourceCounts.Values.Sum().ToString(CultureInfo.InvariantCulture)}",
            "SourceSummary" when value is ConsoleFilter filter => filter.SelectedSources.IsEmpty ? "All sources" : $"Sources ({filter.SelectedSources.Count})",
            _ => value,
        };
    }

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
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
