// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Renders the soft role headlessly with both shapes, content forms, and interaction states.</summary>
public sealed class ToggleSoftRenderer(ITestOutputHelper output)
{
    /// <summary>Checks the state sheet and exports images only when an explicit destination is configured.</summary>
    [AvaloniaFact]
    public void RenderSoftStylesOrCheckLayout()
    {
        string? destination = Environment.GetEnvironmentVariable("NVT_TOGGLE_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(destination);
        foreach (bool dark in new[] { false, true })
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
            Border pill = Column(ThemeShape.Pill);
            Border square = Column(ThemeShape.Square);
            columns.Children.Add(pill);
            Grid.SetColumn(square, 2);
            columns.Children.Add(square);
            var sheet = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 24,
                Children =
                {
                    Label($"toggleSoft · {(dark ? "Dark" : "Light")} · 32 DIP · 100% scale", 20),
                    columns,
                    Label("Tonal selection · 150 ms brush motion · keyboard focus ring with a 2 px gap", 13),
                },
            };
            Window host = Create(sheet, dark, width: 1200, height: 900);
            try
            {
                Show(host);
                ThemeShapes.SetShape(pill.Resources, ThemeShape.Pill);
                ThemeShapes.SetShape(square.Resources, ThemeShape.Square);
                Restore(sheet);
                Flush(host);
                Assert.Equal(60, host.GetVisualDescendants().OfType<ToggleButton>().Count());
                ToggleStylesRenderer.CheckBounds(host);
                Assert.All(host.GetVisualDescendants().OfType<ToggleButton>(), button =>
                {
                    Assert.Equal(32, button.Bounds.Height);
                    Assert.Null(button.Transitions);
                });
                if (string.IsNullOrWhiteSpace(destination)) continue;
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(1, host.RenderScaling);
                Assert.Equal(new PixelSize(1200, 900), frame.PixelSize);
                string name = $"toggle-soft-{(dark ? "dark" : "light")}.png";
                frame.Save(System.IO.Path.Combine(destination, name), PngBitmapEncoderOptions.Default);
                output.WriteLine($"{name} | 1200 x 900 | Pill / Square | text / icon and text | scale 1.0");
            }
            finally { host.Close(); }
        }
    }

    private static Border Column(ThemeShape shape)
    {
        ToggleState[] states =
        [
            new("Off"), new("Off · pointer over", Hover: true),
            new("On", Checked: true), new("On · pointer over", Checked: true, Hover: true),
            new("Pressed", Hover: true, Pressed: true),
            new("On · pressed", Checked: true, Hover: true, Pressed: true),
            new("Keyboard focus", Checked: true, Focus: true),
            new("Off · keyboard focus", Focus: true),
            new("Disabled", Disabled: true), new("Disabled · on", Checked: true, Disabled: true),
        ];
        var rows = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("132,120,*"),
            RowDefinitions = new RowDefinitions("32," + string.Join(',', Enumerable.Repeat("64", states.Length))),
        };
        string[] headings = ["State", "Text only", "Icon and text"];
        for (int column = 0; column < headings.Length; column++) Add(rows, Label(headings[column], 11), 0, column);
        for (int index = 0; index < states.Length; index++)
        {
            ToggleState state = states[index];
            Add(rows, Label(state.Name, 11), index + 1, 0);
            Add(rows, ToggleSoftTests.SampleContent(state, false), index + 1, 1);
            Add(rows, new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    ToggleSoftTests.SampleContent(state, true),
                    ToggleSoftTests.SampleContent(state, true, "Dedupe ×N", "\ue14d"),
                },
            }, index + 1, 2);
        }
        var root = new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel { Spacing = 16, Children = { Label(shape.ToString(), 16), rows } },
        };
        root.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceSubtleBrush"));
        return root;
    }

    private static TextBlock Label(string text, double size)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, Height = size == 20 ? 32 : size == 16 ? 24 : 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("NfcUiFontFamily"));
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("NfcTextSecondaryBrush"));
        return label;
    }

    private static void Add(Grid grid, Control control, int row, int column)
    {
        if (control is ToggleButton) control.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
