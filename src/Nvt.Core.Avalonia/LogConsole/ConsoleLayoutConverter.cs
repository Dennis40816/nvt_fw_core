// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Nvt.Core.Avalonia.LogConsole;

// Composes existing dimension resources; owns no layout state or new design tokens.
internal sealed class ConsoleLayoutConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var size = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return (parameter as string) switch
        {
            "Focus" => new Thickness(-size),
            "Top" => new Thickness(0, size, 0, 0),
            _ => new Thickness(size, 0),
        };
    }

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Any(value => value is null || ReferenceEquals(value, AvaloniaProperty.UnsetValue)))
            return AvaloniaProperty.UnsetValue;
        var sizes = values.Select(value => System.Convert.ToDouble(value, CultureInfo.InvariantCulture)).ToArray();
        return (parameter as string) switch
        {
            "Focus" => new Thickness(-sizes[0]),
            "Top" => new Thickness(0, sizes[0], 0, 0),
            "HeaderHeight" => sizes[0] + sizes[1] + sizes[1],
            "ToolbarHeight" => sizes[2] != 0
                ? sizes[0] + sizes[0] + sizes[1] + sizes[1] + sizes[1]
                : sizes[0] + sizes[1] + sizes[1],
            "Search" => new Thickness(sizes[0], sizes[1], sizes[2] + sizes[1], sizes[1]),
            _ => new Thickness(sizes[0], sizes[1]),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
