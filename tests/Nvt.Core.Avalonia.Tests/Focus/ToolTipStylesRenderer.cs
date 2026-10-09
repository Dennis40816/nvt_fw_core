// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Focus;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Focus.ToolTipTestHost;

namespace Nvt.Core.Avalonia.Tests.Focus;

/// <summary>Renders synthetic tooltip content and a Fluent comparison without opening native windows.</summary>
[Collection("ToolTip text wrapping registration")]
public sealed class ToolTipStylesRenderer
{
    private const string LongText = "A longer tooltip explains the option and its effects. " +
        "Text wraps inside the shared maximum width so the complete explanation stays readable. " +
        "Changing the theme updates the existing text and surface together.";

    /// <summary>Checks preview layout and writes 1200-pixel images only when explicitly enabled.</summary>
    [AvaloniaFact]
    public void RenderTooltipsOrCheckLayout()
    {
        string? destination = Environment.GetEnvironmentVariable("NVT_TOOLTIP_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(destination);
        ToolTipTextWrapping.Register();
        try
        {
            foreach (int preview in new[] { 0, 1, 2, 3 })
            {
                bool dark = preview == 1, square = preview == 2, comparison = preview == 3;
                var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,32,*") };
                for (int column = 0; column < 2; column++)
                {
                    bool core = !comparison || column == 1;
                    var panel = new StackPanel { Spacing = 22 };
                    panel.Children.Add(new TextBlock
                    {
                        Text = comparison ? (core ? "CORE / THEMED AND WRAPPING" : "FLUENT / DEFAULT") :
                            (column == 0 ? "STRING TIPS" : "CUSTOM CONTENT AND KEYBOARD HELP"),
                        FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
                    });
                    if (comparison || column == 0)
                    {
                        panel.Children.Add(Sample("Short string", "Open details", core));
                        panel.Children.Add(Sample(core ? "Long string / maximum width 320 DIP" : "Long string / Fluent defaults",
                            comparison ? "Tooltip text can wrap to keep the explanation easy to read." : LongText, core));
                    }
                    else
                    {
                        panel.Children.Add(Sample("Custom control / content remains unchanged", new StackPanel
                        {
                            Spacing = 4,
                            Children =
                            {
                                new TextBlock { Text = "Structured help", FontWeight = global::Avalonia.Media.FontWeight.SemiBold },
                                new TextBlock { Text = "The host keeps control of custom content." },
                            },
                        }, core));
                        panel.Children.Add(Sample("Keyboard help / shared tooltip surface",
                            "Tab opens the tooltip. Escape closes it and keeps focus on the target.", core));
                    }
                    var root = new Border { Child = panel, Padding = new Thickness(24) };
                    root.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceSubtleBrush"));
                    if (core)
                    {
                        var uri = new Uri("avares://Nvt.Core.Avalonia/Focus/ToolTipStyles.axaml");
                        root.Styles.Add(new StyleInclude(uri) { Source = uri });
                    }
                    Grid.SetColumn(root, column * 2);
                    columns.Children.Add(root);
                }
                var sheet = new StackPanel
                {
                    Margin = new Thickness(32), Spacing = 24,
                    Children =
                    {
                        new TextBlock { Text = "Tooltip / wrapping and themed text", FontSize = 24 },
                        new TextBlock { Text = $"{(dark ? "Dark" : "Light")} / {(square ? "Square, 6 DIP corners" : "Pill, corners capped at 8 DIP")} / 100% scale" },
                        columns,
                        new TextBlock { Text = "Blank tips remain unchanged. Theme and width tokens update existing text." },
                    },
                };
                var window = Create(sheet, dark, styles: false, width: 1200, height: 600);
                window.Bind(Window.BackgroundProperty, new DynamicResourceExtension("NfcAppBackgroundBrush"));
                window.Bind(Window.ForegroundProperty, new DynamicResourceExtension("NfcTextBrush"));
                try
                {
                    ThemeShapes.SetShape(window.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                    window.Show();
                    Flush(window);
                    foreach (ToolTip tip in sheet.GetVisualDescendants().OfType<ToolTip>())
                    {
                        Rect bounds = tip.Bounds;
                        Assert.True(bounds.Width > 0 && bounds.Height > 0);
                        Point? origin = tip.TranslatePoint(default, window);
                        Assert.NotNull(origin);
                        Assert.InRange(origin.Value.X + bounds.Width, 0, 1200);
                        Assert.InRange(origin.Value.Y + bounds.Height, 0, 600);
                    }
                    if (string.IsNullOrWhiteSpace(destination)) continue;
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.Equal(new PixelSize(1200, 600), frame.PixelSize);
                    Assert.Equal(new Vector(96, 96), frame.Dpi);
                    string name = preview switch
                    {
                        0 => "tooltip-light.png", 1 => "tooltip-dark.png",
                        2 => "tooltip-square-light.png", _ => "tooltip-before-light.png",
                    };
                    string path = Path.Combine(destination, name);
                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    Assert.True(new FileInfo(path).Length <= 1_000_000);
                }
                finally { window.Close(); }
            }
        }
        finally { ToolTipTextWrapping.Unregister(); }
    }

    private static StackPanel Sample(string label, object content, bool core)
    {
        if (core && content is string)
        {
            var owner = new Button();
            ToolTip.SetTip(owner, content);
            content = ToolTip.GetTip(owner)!;
        }
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label },
                new ToolTip { Content = content, HorizontalAlignment = HorizontalAlignment.Left },
            },
        };
    }
}
