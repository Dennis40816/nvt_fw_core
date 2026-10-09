// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Automation;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using IconPath = Avalonia.Controls.Shapes.Path;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Renders the shipped toggle styles with both shared shapes in a headless state sheet.</summary>
public sealed partial class ToggleStylesRenderer(ITestOutputHelper output)
{
    /// <summary>Renders the chosen palette with two independent theme roots and no per-control shape overrides.</summary>
    [AvaloniaFact]
    public void RenderShippedStylesOrCheckLayout()
    {
        string? destination = Environment.GetEnvironmentVariable("NVT_TOGGLE_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(destination);
        foreach (bool dark in new[] { false, true })
        {
            Control sheet = BuildSheet(dark, out Border squareRoot);
            Window host = Create(sheet, dark, width: 1320, height: 2920);
            try
            {
                Show(host);
                ThemeShapes.SetShape(squareRoot.Resources, ThemeShape.Square);
                Restore(sheet);
                Flush(host);
                foreach (double width in new[] { 1200d, 1320d, 1600d })
                {
                    host.Width = width;
                    Flush(host);
                    CheckBounds(host);
                }
                host.Width = 1320;
                Flush(host);
                if (string.IsNullOrWhiteSpace(destination)) continue;
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(1320, 2920), frame.PixelSize);
                string name = $"toggle-src-{(dark ? "dark" : "light")}.png";
                frame.Save(System.IO.Path.Combine(destination, name), PngBitmapEncoderOptions.Default);
                output.WriteLine($"{name} | 1320 x 2920 | Pill / Square | Core font roles | scale 1.0");
            }
            finally { host.Close(); }
        }
    }

    private static Border BuildSheet(bool dark, out Border squareRoot)
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        squareRoot = null!;
        for (int shape = 0; shape < 2; shape++)
        {
            var column = new StackPanel { Spacing = 22 };
            column.Children.Add(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Label(shape == 0 ? "PILL / DEFAULT" : "SQUARE / OPTIONAL", 11, accent: true),
                    Label(shape == 0 ? "Full pill corners" : "6 px corners", 16, strong: true),
                    Label("Solid accent selection. White labels.", 13),
                },
            });
            column.Children.Add(Preview());
            foreach (string role in new[] { "toggleSegment", "toggleTab", "toggleIcon", "toggleSwitch" })
                column.Children.Add(RoleSection(role));
            column.Children.Add(DangerSection());
            column.Children.Add(Label("One 2 px focus ring · 2 px gap · 150 ms motion", 11));
            Border root = new Border { Child = column };
            root.Padding = new Thickness(24);
            root.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
            Add(columns, root, 0, shape * 2);
            if (shape == 1) squareRoot = root;
        }
        return new Border
        {
            Padding = new Thickness(32),
            Child = new StackPanel
            {
                Spacing = 24,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children = { Label("Chosen toggle / one palette, two shapes", 16, strong: true), ThemeLabel(dark) },
                    },
                    columns,
                },
            },
        };
    }

    private static StackPanel RoleSection(string role)
    {
        var section = RoleRows(role);
        if (role == "toggleSwitch") ((TextBlock)((StackPanel)section.Children[0]).Children[1]).Text = "ToggleButton left / ToggleSwitch right";
        if (role != "toggleTab") return section;
        var rows = (Grid)section.Children[1];
        foreach (ToggleButton sample in rows.Children.OfType<ToggleButton>().ToArray())
        {
            int row = Grid.GetRow(sample);
            rows.Children.Remove(sample);
            sample.Width = 110;
            var peer = Sample(role, new ToggleState("Rest"), "Details");
            peer.Width = 100;
            var navigation = new Border
            {
                Padding = new Thickness(0, 0, 0, 5), BorderThickness = new Thickness(0, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sample, peer } },
            };
            navigation.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("NfcDividerBrush"));
            Add(rows, navigation, row, 1);
        }
        return section;
    }

    private static StackPanel DangerSection()
    {
        ToggleState[] states =
        [
            new("Rest"), new("Checked", Checked: true),
            new("Checked + pointer over", Checked: true, Hover: true),
            new("Checked + pressed", Checked: true, Hover: true, Pressed: true),
            new("Disabled + checked", Checked: true, Disabled: true),
            new("Keyboard focus", Checked: true, Focus: true),
        ];
        var rows = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("128,100,100,48,*"),
            RowDefinitions = new RowDefinitions($"32,{string.Join(',', Enumerable.Repeat("50", states.Length))}"),
        };
        string[] headers = ["State", "Segment", "Tab", "Icon", "Switch / native"];
        for (int column = 0; column < headers.Length; column++)
        {
            TextBlock heading = Label(headers[column], 11);
            if (column > 0) heading.HorizontalAlignment = HorizontalAlignment.Center;
            Add(rows, heading, 0, column);
        }
        for (int row = 0; row < states.Length; row++)
        {
            ToggleState state = states[row];
            Add(rows, Label(state.Name, 11), row + 1, 0);
            for (int roleIndex = 0; roleIndex < 4; roleIndex++)
            {
                string role = Roles[roleIndex];
                ToggleButton sample = role == "toggleIcon" ? Icon(state) : Sample(role, state, "Danger");
                sample.Classes.Add("danger");
                Control cell = sample;
                if (role == "toggleSegment")
                {
                    sample.Width = 90;
                    cell = Group((StateToggleButton)sample);
                }
                else if (role == "toggleTab") sample.Width = 90;
                else if (role == "toggleSwitch")
                {
                    ToggleButton native = Sample("nativeSwitch", state);
                    native.Classes.Add("danger");
                    cell = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 16,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Children = { sample, native },
                    };
                }
                Add(rows, cell, row + 1, roleIndex + 1);
            }
        }
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Label("danger", 14, strong: true),
                Label("Red when checked. Standard keyboard focus.", 11),
                rows,
            },
        };
    }
    private static Border Preview()
    {
        var selected = (StateToggleButton)Sample("toggleSegment", new ToggleState("Checked", Checked: true), "Overview");
        var peer = (StateToggleButton)Sample("toggleSegment", new ToggleState("Rest"), "Details");
        selected.Width = peer.Width = 100;
        var rail = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { Group(selected, peer), Icon(new ToggleState("Checked", Checked: true)), Sample("toggleSwitch", new ToggleState("Checked", Checked: true)) },
        };
        var surface = new Border { Padding = new Thickness(16, 24), Child = rail };
        surface.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcAppBackgroundBrush"));
        return surface;
    }

    private static StackPanel RoleRows(string role)
    {
        string detail = role switch
        {
            "toggleSegment" => "Grouped choices",
            "toggleTab" => "Navigation",
            "toggleIcon" => "Pin action",
            _ => "ToggleButton / ToggleSwitch",
        };
        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(new StackPanel { Spacing = 4, Children = { Label(role, 14, strong: true), Label(detail, 11) } });
        var rows = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("152,*"),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("50", States.Length))),
        };
        for (int index = 0; index < States.Length; index++)
        {
            ToggleState state = States[index];
            string label = state.Name.Replace("Checked + over", "Checked + pointer over", StringComparison.Ordinal)
                .Replace("Checked + press", "Checked + pressed", StringComparison.Ordinal);
            Add(rows, Label(label, 11), index, 0);
            ToggleButton sample = role == "toggleIcon" ? Icon(state) : Sample(role, state);
            Control cell = sample;
            if (role == "toggleSegment")
            {
                sample.Width = 100;
                var peer = (StateToggleButton)Sample(role, new ToggleState("Rest"), "Details");
                peer.Width = 100;
                cell = Group((StateToggleButton)sample, peer);
            }
            else if (role == "toggleTab") sample.Width = 120;
            else if (role == "toggleSwitch")
            {
                cell = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 32,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Children = { sample, Sample("nativeSwitch", state) },
                };
            }
            Add(rows, cell, index, 1);
        }
        section.Children.Add(rows);
        return section;
    }

    internal static ToggleButton Icon(ToggleState state)
    {
        ToggleButton button = Sample("toggleIcon", state);
        var glyph = new IconPath
        {
            Width = 18, Height = 18, Stretch = Stretch.Uniform,
            Data = Geometry.Parse("M6 2H12 M7 2V7L4 10V12H14V10L11 7V2 M9 12V17"),
            StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        glyph.Bind(Shape.StrokeProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
        button.Content = glyph;
        AutomationProperties.SetName(button, "Pin view");
        return button;
    }

    private static TextBlock ThemeLabel(bool dark)
    {
        TextBlock label = Label(dark ? "DARK / BLUE ACCENT" : "LIGHT / BLUE ACCENT", 11);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(label, 1);
        return label;
    }

    private static TextBlock Label(string text, int size, bool strong = false, bool accent = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal };
        label.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("NfcUiFontFamily"));
        label.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension($"NfcFontSize{size}"));
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(accent ? "NfcAccentStrongBrush" : strong ? "NfcTextStrongBrush" : "NfcTextSecondaryBrush"));
        return label;
    }

    internal static void CheckBounds(Window host)
    {
        foreach (ToggleButton button in host.GetVisualDescendants().OfType<ToggleButton>())
        {
            Point point = button.TranslatePoint(default, host)!.Value;
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            Assert.True(point.X >= 4 && point.Y >= 4);
            Assert.True(point.X + button.Bounds.Width + 4 <= host.Width);
            Assert.True(point.Y + button.Bounds.Height + 4 <= host.Height);
        }
    }
    private static void Add(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
