// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Pins the contrast computation against rendered pixels and against measured downstream values.</summary>
public sealed class ShownColorTests
{
    // Freeform Helper Light theme, resting values (pull request 711 of that repository).
    private static readonly Color _canvas = Color.Parse("#FFFFFF");
    private static readonly Color _buttonFill = Color.Parse("#475569");
    private static readonly Color _buttonGlyph = Color.Parse("#FFFFFF");
    private static readonly Color _checkedFill = Color.Parse("#2967A9");
    private static readonly Color _checkedLabel = Color.Parse("#FFFFFF");

    /// <summary>The floating button glyph failed the 3:1 floor before the colour fix and passes after it.</summary>
    [Theory]
    [InlineData(0.72, false, 2.7)]
    [InlineData(0.8, true, 3.6)]
    public void FadedButtonGlyphMatchesTheMeasuredRatio(double opacity, bool passes, double measured)
    {
        double ratio = ShownColor.PairRatio(_buttonGlyph, _buttonFill, _canvas, opacity);
        Assert.Equal(passes, ratio >= 3);
        Assert.Equal(measured, ratio, 0.1);
    }

    /// <summary>The checked toggle fill on the canvas and its label failed before the colour fix and pass after it.</summary>
    [Theory]
    [InlineData(0.3, false, 1.6)]
    [InlineData(0.85, true, 4.3)]
    public void FadedCheckedFillMatchesTheMeasuredRatio(double opacity, bool passes, double measured)
    {
        double fill = ShownColor.Contrast(ShownColor.Over(_checkedFill, _canvas, opacity), _canvas);
        double label = ShownColor.PairRatio(_checkedLabel, _checkedFill, _canvas, opacity);
        Assert.Equal(passes, fill >= 3);
        Assert.Equal(passes, label >= 3);
        Assert.Equal(measured, fill, 0.1);
    }

    /// <summary>The computed colours are the rendered pixels of a faded element, also when its child draws on its fill.</summary>
    [AvaloniaTheory]
    [InlineData(0.3, 1.0)]
    [InlineData(0.72, 1.0)]
    [InlineData(0.8, 0.5)]
    public void ComputedColoursMatchRenderedPixels(double opacity, double brushOpacity)
    {
        var glyphBrush = new SolidColorBrush(_buttonGlyph, brushOpacity);
        var glyph = new Border
        {
            Width = 20,
            Height = 20,
            Background = glyphBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Border
        {
            Width = 60,
            Height = 60,
            Opacity = opacity,
            Background = new SolidColorBrush(_buttonFill),
            Child = glyph,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window { Width = 80, Height = 80, Background = new SolidColorBrush(_canvas), Content = button };
        try
        {
            window.Show();
            ListMenuTestHost.Flush(window);

            Color shownFill = ShownColor.Over(_buttonFill, _canvas, ShownColor.ElementOpacity(button));
            Color shownGlyph = ShownColor.Over(glyphBrush, shownFill, ShownColor.ElementOpacity(glyph));
            AssertNear(shownFill, Pixel(window, 5, 5));
            AssertNear(shownGlyph, Pixel(window, 30, 30));
        }
        finally { window.Close(); }
    }

    // The renderer blends in premultiplied integers, so allow a rounding step per channel.
    private static void AssertNear(Color expected, Color actual) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= 2 && Math.Abs(expected.G - actual.G) <= 2 && Math.Abs(expected.B - actual.B) <= 2,
            $"computed {expected}, rendered {actual}");

    private static Color Pixel(Window window, int x, int y)
    {
        var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(window);
        var bytes = new byte[size.Width * size.Height * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { pinned.Free(); }
        int offset = (y * size.Width + x) * 4;
        return Color.FromArgb(bytes[offset + 3], bytes[offset + 2], bytes[offset + 1], bytes[offset]);
    }
}
