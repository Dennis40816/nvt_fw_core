// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Renders anonymous before/after comparisons with the shared Inter and Skia headless host.</summary>
public sealed class ToggleComparisonRenderer(ITestOutputHelper output)
{
    /// <summary>Always checks layout and writes eight PNGs only when the image destination is explicitly set.</summary>
    [AvaloniaFact]
    public void RenderComparisonsOrCheckLayout()
    {
        string? destination = Environment.GetEnvironmentVariable("NVT_TOGGLE_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(destination);
        foreach (bool dark in new[] { false, true })
        {
            for (int tool = 0; tool < 3; tool++)
                Render(BuildComparison(tool, dark), dark, tool, $"toggle-{(char)('a' + tool)}-{(dark ? "dark" : "light")}.png", destination);
            Render(BuildStateSheet(dark), dark, 0, $"toggle-states-{(dark ? "dark" : "light")}.png", destination);
        }
    }

    private void Render(Control content, bool dark, int tool, string name, string? destination)
    {
        Window host = Create(content, dark, tool, width: 1900, height: content.Tag as string == "states" ? 600 : 860);
        Include(host, $"avares://Nvt.Core.Avalonia.Tests/Toggle/Frozen/Tool{(char)('A' + tool)}.axaml");
        try
        {
            Show(host);
            // Showing a window may focus its first control. Restore all displayed states after attachment.
            foreach (var button in content.GetVisualDescendants().OfType<StateToggleButton>())
                if (button.Tag is ToggleState state) button.SetState(state);
            Flush(host);
            Assert.Equal(1, host.RenderScaling);
            Assert.Equal(new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"), host.FontFamily);
            foreach (var button in content.GetVisualDescendants().OfType<StateToggleButton>())
            {
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0, name);
                var location = button.TranslatePoint(default, host)!.Value;
                Assert.True(location.X >= 4 && location.Y >= 4, name);
                Assert.True(location.X + button.Bounds.Width + 4 <= host.Width, name);
                Assert.True(location.Y + button.Bounds.Height + 4 <= host.Height, name);
            }
            if (string.IsNullOrWhiteSpace(destination)) return;
            using var frame = host.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize((int)host.Width, (int)host.Height), frame.PixelSize);
            Assert.Equal(new Vector(96, 96), frame.Dpi);
            frame.Save(Path.Combine(destination, name), PngBitmapEncoderOptions.Default);
            output.WriteLine($"{name} | {frame.PixelSize.Width} x {frame.PixelSize.Height} | Inter | scale 1.0");
        }
        finally { host.Close(); }
    }

    private static Border BuildComparison(int tool, bool dark)
    {
        char letter = (char)('A' + tool);
        var stack = new StackPanel { Spacing = 18 };
        stack.Children.Add(Title($"Tool {letter} / {(dark ? "Dark" : "Light")}", "ToggleButton roles · visual proposal · look awaits owner approval"));
        var before = new Border { MinHeight = 312, Padding = new Thickness(20, 14), Child = Matrix(BeforePatterns(tool), tool, before: true) };
        before.Bind(Border.BackgroundProperty, new DynamicResourceExtension($"Tool{letter}.Background"));
        stack.Children.Add(new StackPanel { Spacing = 8, Children = { Label("Before", 20, strong: true), before } });
        stack.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children = { Label("After", 20, strong: true), new Border { Padding = new Thickness(20, 14), Child = Matrix(AfterPatterns, tool, before: false) } },
        });
        stack.Children.Add(Label("Core defines After appearance. Frozen values define Before appearance, with Fluent for unspecified states.", 12));
        return new Border { Padding = new Thickness(28), Child = stack };
    }

    private static Border BuildStateSheet(bool dark)
    {
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 36 };
        foreach (string role in Roles)
        {
            var button = Sample(role, new ToggleState("Danger focus", Checked: true, Focus: true));
            button.Classes.Add("danger");
            Control sample = role == "toggleSegment" ? Group(button, Sample(role, new ToggleState("Peer"), "Two")) : button;
            extras.Children.Add(new StackPanel { Spacing = 8, Children = { Label(role + " / danger focus", 12), sample } });
        }
        var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        for (int count = 2; count <= 5; count++)
        {
            var buttons = Enumerable.Range(0, count).Select(index => Sample("toggleSegment", new ToggleState("Choice", Checked: index == 0), (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            foreach (var button in buttons) button.Width = 40;
            counts.Children.Add(new StackPanel { Spacing = 8, Children = { Label($"{count} joined options", 12), Group(buttons) } });
        }
        return new Border
        {
            Tag = "states", Padding = new Thickness(28),
            Child = new StackPanel
            {
                Spacing = 20,
                Children =
                {
                    Title($"Core states / {(dark ? "Dark" : "Light")}", "32 px controls · 8 px group corners · 6 px icon corners · 2 px ring + 2 px gap"),
                    Matrix(AfterPatterns, 0, before: false), counts, extras,
                    Label("Styles define appearance. The host owns single selection, commands, icon content and accessible names.", 12),
                },
            },
        };
    }

    private static readonly (string Role, string Metric)[] AfterPatterns =
    [
        ("toggleSegment", "Joined choices / 32 px"), ("toggleTab", "Underline / 32 px"), ("toggleIcon", "Square / 32 × 32 px"),
    ];

    private static (string Role, string Metric)[] BeforePatterns(int tool) => tool switch
    {
        0 => [("toolATab", "Underline navigation"), ("toolASegment", "Pills / minimum 44 px"), ("toolAPill", "State pill / minimum 24 px"), ("toolASwitch", "Switch-shaped toggle")],
        1 => [("toolBTab", "Capsule navigation"), ("toolBAction", "Neutral action"), ("toolBIcon", "Circle / 30 × 30 px"), ("toolBActionIcon", "Action icon / 30 × 30 px")],
        _ => [("toolCAxis", "Choice / 34 px"), ("toolCCanvas", "Canvas / 34 px")],
    };

    private static Grid Matrix((string Role, string Metric)[] patterns, int tool, bool before)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("176," + string.Join(',', Enumerable.Repeat("176", States.Length))),
            RowDefinitions = new RowDefinitions("28," + string.Join(',', Enumerable.Repeat("64", patterns.Length))),
        };
        Add(grid, Label(before ? $"Tool {(char)('A' + tool)} patterns" : "Core roles", 12), 0, 0);
        for (int index = 0; index < States.Length; index++) Add(grid, Label(States[index].Name, 11), 0, index + 1);
        for (int row = 0; row < patterns.Length; row++)
        {
            var pattern = patterns[row];
            Add(grid, new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                Children = { Label(before ? pattern.Metric : pattern.Role, 13, strong: true), Label(before ? "Frozen appearance" : pattern.Metric, 11) } }, row + 1, 0);
            for (int column = 0; column < States.Length; column++)
            {
                ToggleState state = States[column];
                var button = Sample(pattern.Role, state);
                Control cell = button;
                if (pattern.Role == "toggleSegment")
                {
                    button.Content = "One"; button.Width = 64;
                    var peer = Sample(pattern.Role, new ToggleState("Peer"), "Two"); peer.Width = 64;
                    cell = Group(button, peer);
                }
                if (pattern.Role == "toolATab")
                {
                    var underline = new Border { IsVisible = state.Checked };
                    underline.Classes.Add("toolAUnderline");
                    cell = new Grid { Children = { button, underline } };
                }
                if (pattern.Role == "toolBTab")
                {
                    var strip = new Border { Child = button };
                    strip.Classes.Add("toolBTabStrip");
                    cell = strip;
                }
                Add(grid, new Border { Child = cell, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, row + 1, column + 1);
            }
        }
        return grid;
    }

    private static StateToggleButton Sample(string role, ToggleState state, string label = "Option")
    {
        var button = Button(role, label);
        button.Tag = state;
        if (role == "toolBAction" || role == "toolBActionIcon") button.Classes.Add("toolBNeutral");
        if (role.Contains("Icon", StringComparison.Ordinal) || role == "toolBIcon" || role == "toolCCanvas")
        {
            var glyph = new TextBlock { Text = "+", FontSize = 20 };
            glyph.Classes.Add("nvtIcon");
            button.Content = glyph;
        }
        if (role == "toolASwitch") button.Content = null;
        button.SetState(state);
        return button;
    }

    private static StackPanel Title(string title, string subtitle) =>
        new() { Spacing = 6, Children = { Label(title, 26, strong: true), Label(subtitle, 13) } };

    private static TextBlock Label(string text, double size, bool strong = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal };
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(strong ? "NfcTextStrongBrush" : "NfcTextSecondaryBrush"));
        return label;
    }

    private static void Add(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control);
    }
}
