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
            // One control row plus the vertical padding above and below it.
            "RowHeight" => sizes[0] + sizes[1] + sizes[1],
            // A control that sits inside a bordered field: the row minus a padding step above and below.
            "Inset" => sizes[0] - sizes[1] - sizes[1],
            // Room for the leading glyph and for the trailing clear and only-matches buttons.
            "Search" => new Thickness(sizes[0] + sizes[1], sizes[1], sizes[0] + sizes[0], sizes[1]),
            // One value spaces a control horizontally only; two values are horizontal and vertical.
            _ => sizes.Length == 1 ? new Thickness(sizes[0], 0) : new Thickness(sizes[0], sizes[1]),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
