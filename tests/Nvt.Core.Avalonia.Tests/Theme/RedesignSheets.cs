// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Theme;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

// Uses existing control APIs and resource names so the same renderer can run on the base styles.
internal static class RedesignSheets
{
    internal static Window Create(Control content, bool dark = false)
    {
        Window host = ListMenuTestHost.Create(content, dark, width: 1200, height: 2600);
        foreach (string file in new[] { "ToggleStyles", "ChoiceStyles", "ExpanderStyles", "ProgressStyles", "DividerStyles" })
            ListMenuTestHost.Include(host, file);
        return host;
    }

    internal static TextBlock Label(string text, double size = 13) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    internal static void Render(string variable, string name, string description, Func<Control> build,
        Action<Control> restore, ITestOutputHelper output, bool single = false)
    {
        string? destination = Environment.GetEnvironmentVariable(variable);
        foreach (bool dark in single ? new[] { false } : new[] { false, true })
        foreach (bool square in single ? new[] { false } : new[] { false, true })
        {
            Control content = build();
            var surface = new Border { Padding = new Thickness(24), Child = content };
            surface.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
            var root = new Border
            {
                Padding = new Thickness(32),
                Child = new StackPanel
                {
                    Spacing = 20,
                    Children =
                    {
                        Label(single ? $"CORE / {name.ToUpperInvariant()} / LIGHT + DARK" : $"CORE / {name.ToUpperInvariant()} / {(square ? "SQUARE" : "PILL")} / {(dark ? "DARK" : "LIGHT")}", 24),
                        Label(description), surface,
                        Label("100% scale / synthetic examples / 150 ms transitions / keyboard focus only", 11),
                    },
                },
            };
            Window host = Create(root, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                ListMenuTestHost.Show(host);
                restore(content);
                ListMenuTestHost.Flush(host);
                host.Height = Math.Ceiling(root.DesiredSize.Height);
                ListMenuTestHost.Flush(host);
                Assert.Equal(1, host.RenderScaling);
                foreach (Control control in content.GetVisualDescendants().OfType<Control>())
                {
                    if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) continue;
                    Point point = control.TranslatePoint(default, host)!.Value;
                    Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= host.Width + 1
                        && point.Y + control.Bounds.Height <= host.Height + 1, $"{name}/{control.Name}: {point}, {control.Bounds}");
                }
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(1200, frame.PixelSize.Width);
                if (string.IsNullOrWhiteSpace(destination)) continue;
                Directory.CreateDirectory(destination);
                string file = single ? $"{name}.png" : $"{name}-{(square ? "square" : "pill")}-{(dark ? "dark" : "light")}.png";
                string path = System.IO.Path.Combine(destination, file);
                frame.Save(path, PngBitmapEncoderOptions.Default);
                Assert.True(new FileInfo(path).Length <= 1_000_000, file);
                output.WriteLine($"{file}: {frame.PixelSize}, {new FileInfo(path).Length} bytes");
            }
            finally { host.Close(); }
        }
    }
}
