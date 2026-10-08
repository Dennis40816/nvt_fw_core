// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Caps popup corners using two live resources while preserving runtime shape changes.</summary>
internal sealed class MenuCornerRadiusConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2 || values[0] is not CornerRadius radius || values[1] is not CornerRadius maximum)
            return AvaloniaProperty.UnsetValue;
        return new CornerRadius(Math.Min(radius.TopLeft, maximum.TopLeft), Math.Min(radius.TopRight, maximum.TopRight),
            Math.Min(radius.BottomRight, maximum.BottomRight), Math.Min(radius.BottomLeft, maximum.BottomLeft));
    }
}
