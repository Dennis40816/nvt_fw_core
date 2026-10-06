// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Testing;

/// <summary>Characterizes Inter measurements and Skia pixels using the frozen NVT FW Combiner builder chain.</summary>
/// <param name="output">Receives the measured baseline during characterization.</param>
public sealed class AvaloniaRenderingTests(ITestOutputHelper output)
{
    /// <summary>Measures ASCII text at the representative Inter sizes.</summary>
    /// <param name="text">The measured ASCII text.</param>
    /// <param name="fontSize">The Inter font size in device-independent pixels.</param>
    /// <param name="width">The exact frozen desired width.</param>
    /// <param name="height">The exact frozen desired height.</param>
    [AvaloniaTheory]
    [InlineData("Core 123", 11, 46.734375, 13.3125)]
    [InlineData("Core 123", 13, 55.23153409090909, 15.732954545454545)]
    [InlineData("Core 123", 16, 67.97727272727272, 19.363636363636363)]
    [InlineData("Core 123", 24, 101.96590909090907, 29.045454545454547)]
    public void InterTextMeasurementsMatchTheFrozenHost(string text, double fontSize, double width, double height)
    {
        var control = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"),
            FontSize = fontSize,
            UseLayoutRounding = false,
        };
        control.Measure(Size.Infinity);
        output.WriteLine($"{text} | {fontSize} | {control.DesiredSize.Width:R} | {control.DesiredSize.Height:R}");
        Assert.Equal(new Size(width, height), control.DesiredSize);
    }

    /// <summary>Captures a synthetic Inter text frame and hashes its raw Skia pixels.</summary>
    [AvaloniaFact]
    public void CapturedFrameMatchesTheFrozenHost()
    {
        var content = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        foreach (double fontSize in new double[] { 11, 13, 16, 24 })
        {
            content.Children.Add(new TextBlock
            {
                Text = "Core 123",
                FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"),
                FontSize = fontSize,
                Foreground = Brushes.Black,
            });
        }

        var window = new Window { Width = 320, Height = 240, Background = Brushes.White, Content = content };
        try
        {
            window.Show();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(320, 240), frame.PixelSize);
            Assert.Equal(new Vector(96, 96), frame.Dpi);
            Assert.Equal(PixelFormat.Rgba8888, frame.Format);

            int stride = frame.PixelSize.Width * 4;
            byte[] pixels = new byte[stride * frame.PixelSize.Height];
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally
            {
                handle.Free();
            }

            string hash = Convert.ToHexString(SHA256.HashData(pixels));
            output.WriteLine($"Frame | {frame.PixelSize} | {hash}");
            Assert.Equal("19F40303C54B8F27E07EE66C7B8A312EF02BD4A73387F6D9ABB0291929DEE4E6", hash);
        }
        finally
        {
            window.Close();
        }
    }
}
