// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal sealed class ConsoleValueConverter : IValueConverter, IMultiValueConverter
{
    private readonly ConsoleTemplateFormatter _formatter = new();
    internal StyledElement? ResourceHost { get; init; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string ?? string.Empty;
        if (key.StartsWith("Level:", StringComparison.Ordinal))
        {
            var level = Enum.Parse<LogLevel>(key[6..]);
            return value is ConsoleFilter filter ? filter.EnabledLevels.Contains(level) : null;
        }
        if (key.StartsWith("Time:", StringComparison.Ordinal))
            return value is ConsoleTimeMode mode && mode == Enum.Parse<ConsoleTimeMode>(key[5..]);
        return key switch
        {
            "HasEvents" => value is int count && count != 0,
            "EmptyText" => string.IsNullOrEmpty(value as string),
            "AllSources" when value is ConsoleFilter filter => filter.SelectedSources.IsEmpty,
            _ => value,
        };
    }

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
            return _formatter.Format("Count", template, culture, label, count);
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
