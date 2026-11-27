// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>
/// Computes the colours the screen shows. Avalonia applies an element opacity to every shape it draws, not to
/// the element as one layer, so each shape is painted in layer order with the alpha of its colour, the opacity of
/// its brush and the opacity of its element and ancestors.
/// </summary>
internal static class ShownColor
{
    /// <summary>The colour shown after painting one colour over an opaque colour already on screen.</summary>
    internal static Color Over(Color paint, Color backdrop, double opacity = 1)
    {
        Assert.Equal(byte.MaxValue, backdrop.A);
        double alpha = paint.A / 255d * opacity;
        byte Mix(byte top, byte under) => (byte)Math.Round(top * alpha + under * (1 - alpha), MidpointRounding.AwayFromZero);
        return Color.FromRgb(Mix(paint.R, backdrop.R), Mix(paint.G, backdrop.G), Mix(paint.B, backdrop.B));
    }

    /// <summary>The colour shown after painting one solid brush, including its brush opacity.</summary>
    internal static Color Over(IBrush? brush, Color backdrop, double opacity = 1)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Over(solid.Color, backdrop, solid.Opacity * opacity);
    }

    /// <summary>The opacity every shape of an element is drawn with: its own and that of each ancestor.</summary>
    internal static double ElementOpacity(Visual element) =>
        element.GetSelfAndVisualAncestors().Aggregate(1d, (opacity, visual) => opacity * visual.Opacity);

    /// <summary>The contrast of a foreground shape on a fill shape that lies on an opaque backdrop.</summary>
    internal static double PairRatio(Color foreground, Color fill, Color backdrop, double opacity = 1)
    {
        Color shownFill = Over(fill, backdrop, opacity);
        return Contrast(Over(foreground, shownFill, opacity), shownFill);
    }

    /// <summary>The sRGB contrast ratio of two shown colours. A translucent input is a mistake of the caller.</summary>
    internal static double Contrast(Color first, Color second)
    {
        Assert.Equal(byte.MaxValue, first.A);
        Assert.Equal(byte.MaxValue, second.A);
        static double Channel(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
