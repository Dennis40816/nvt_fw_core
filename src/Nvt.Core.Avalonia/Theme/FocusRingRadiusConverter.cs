// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Expands a control's corners by the focus ring's four-pixel outset.</summary>
internal sealed class FocusRingRadiusConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CornerRadius radius)
        {
            return new CornerRadius(4);
        }

        static double Expand(double corner) => corner >= 999 ? corner : corner + 4;
        return new CornerRadius(Expand(radius.TopLeft), Expand(radius.TopRight),
            Expand(radius.BottomRight), Expand(radius.BottomLeft));
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
