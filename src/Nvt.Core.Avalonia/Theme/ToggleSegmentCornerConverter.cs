// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Retains only the exterior corners of a joined segment group.</summary>
internal sealed class ToggleSegmentCornerConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CornerRadius radius) return new CornerRadius(0);
        return (parameter as string) switch
        {
            "first" => new CornerRadius(radius.TopLeft, 0, 0, radius.BottomLeft),
            "last" => new CornerRadius(0, radius.TopRight, radius.BottomRight, 0),
            _ => radius,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
