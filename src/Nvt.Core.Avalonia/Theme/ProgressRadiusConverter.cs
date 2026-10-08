// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Clamps shape corners to half the progress track's shorter dimension.</summary>
internal sealed class ProgressRadiusConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2 || values[0] is not CornerRadius shape || values[1] is not Rect bounds)
            return AvaloniaProperty.UnsetValue;
        double maximum = Math.Min(bounds.Width, bounds.Height) / 2;
        return new CornerRadius(Math.Min(shape.TopLeft, maximum), Math.Min(shape.TopRight, maximum),
            Math.Min(shape.BottomRight, maximum), Math.Min(shape.BottomLeft, maximum));
    }
}
