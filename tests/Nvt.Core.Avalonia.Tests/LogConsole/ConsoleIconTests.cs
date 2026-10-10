// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Measures icon ink against Body-role level labels at actual window scales.</summary>
public sealed class ConsoleIconTests(ITestOutputHelper output)
{
    /// <summary>Preserves optical centering across theme and rendering-scale changes.</summary>
    [AvaloniaTheory]
    [InlineData(false, 1.0)]
    [InlineData(true, 1.0)]
    [InlineData(false, 1.25)]
    [InlineData(true, 1.25)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    [InlineData(false, 2.0)]
    [InlineData(true, 2.0)]
    public void LevelIconInkStaysCenteredAgainstLevelText(bool dark, double scale)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var labels = new Grid { Height = 32, ColumnDefinitions = new ColumnDefinitions("120,120,120,120,120,120"), Margin = new Thickness(16, 0, 0, 0) };
        var levels = Enum.GetValues<LogLevel>();
        for (var index = 0; index < levels.Length; index++)
        {
            var label = new TextBlock { Text = levels[index].ToString(), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
            label.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("Nvt.Font.Body.Family"));
            label.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Nvt.Font.Body.Size"));
            label.Bind(TextBlock.FontWeightProperty, new DynamicResourceExtension("Nvt.Font.Body.Weight"));
            label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("NfcTextBrush"));
            Grid.SetColumn(label, index);
            labels.Children.Add(label);
        }
        var content = new StackPanel { Children = { toolbar, labels } };
        content.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceSubtleBrush"));
        var window = ConsoleTestView.Create(content, dark, height: 80);
        try
        {
            window.SetRenderScaling(scale);
            ConsoleTestView.Pump(window);
            using var frame = new RenderTargetBitmap(new PixelSize((int)(1200 * scale), (int)(80 * scale)), new Vector(96 * scale, 96 * scale));
            frame.Render(window);
            var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { frame.CopyPixels(new PixelRect(frame.PixelSize), pinned.AddrOfPinnedObject(), bytes.Length, frame.PixelSize.Width * 4); }
            finally { pinned.Free(); }
            for (var index = 0; index < levels.Length; index++)
            {
                var iconCenter = InkCenter(bytes, frame.PixelSize.Width, (int)((16 + 40 * index) * scale), (int)(8 * scale), (int)(32 * scale), (int)(32 * scale));
                var textCenter = InkCenter(bytes, frame.PixelSize.Width, (int)((16 + 120 * index) * scale), (int)(48 * scale), (int)(100 * scale), (int)(32 * scale));
                var delta = iconCenter - textCenter + 40 * scale;
                output.WriteLine($"{levels[index]} @ {scale}: icon/text ink delta {delta}");
                Assert.InRange(delta, -0.5, 0.5);
            }
            var evidence = Environment.GetEnvironmentVariable("NVT_CONSOLE_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
                frame.Save(Path.Combine(evidence, $"icons-{(dark ? "dark" : "light")}-{(int)(scale * 100)}.png"), PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    private static double InkCenter(byte[] bytes, int stridePixels, int x, int y, int width, int height)
    {
        var sample = (y * stridePixels + x) * 4;
        var top = int.MaxValue;
        var bottom = -1;
        for (var row = y; row < y + height; row++)
        for (var column = x; column < x + width; column++)
        {
            var pixel = (row * stridePixels + column) * 4;
            var difference = Math.Abs(bytes[pixel] - bytes[sample]) + Math.Abs(bytes[pixel + 1] - bytes[sample + 1]) + Math.Abs(bytes[pixel + 2] - bytes[sample + 2]);
            // Include every antialiased edge, matching the approved non-background ink measurement.
            if (difference == 0) continue;
            top = Math.Min(top, row);
            bottom = Math.Max(bottom, row);
        }
        Assert.True(bottom >= top, "The glyph must have visible ink.");
        return (top + bottom) / 2.0;
    }
}
