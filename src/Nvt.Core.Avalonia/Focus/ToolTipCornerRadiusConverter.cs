// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Nvt.Core.Avalonia.Focus;

/// <summary>Caps each shared shape corner at the corresponding tooltip corner token.</summary>
internal sealed class ToolTipCornerRadiusConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2 || values[0] is not CornerRadius shape || values[1] is not CornerRadius cap)
            return AvaloniaProperty.UnsetValue;

        return new CornerRadius(Math.Min(shape.TopLeft, cap.TopLeft), Math.Min(shape.TopRight, cap.TopRight),
            Math.Min(shape.BottomRight, cap.BottomRight), Math.Min(shape.BottomLeft, cap.BottomLeft));
    }
}
