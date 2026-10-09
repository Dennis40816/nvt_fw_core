// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Choice;

/// <summary>Renders every choice state and the Fluent comparison only when the image destination is configured.</summary>
public sealed partial class ChoiceStylesRenderer(ITestOutputHelper output)
{
    /// <summary>Checks layout and optionally exports four 1200-pixel headless sheets at 100 percent scale.</summary>
    [AvaloniaFact]
    public void RenderAllStatesAndFluentComparisonOrCheckLayout()
    {
        string? destination = Environment.GetEnvironmentVariable("NVT_CHOICE_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(destination);
        Render(BuildSheet(false, false), "choice-light.png", false, false, true, 1320);
        Render(BuildSheet(true, false), "choice-dark.png", true, false, true, 1320);
        Render(BuildSheet(false, true), "choice-square-light.png", false, true, true, 1320);
        Render(BuildComparison(), "choice-before-light.png", false, false, false, 1280);

        void Render(Control sheet, string name, bool dark, bool square, bool styles, double height)
        {
            Window host = Create(sheet, dark, styles, width: 1200, height: height);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Restore(sheet);
                if (!styles) ShowFluentFocusAdorners(host);
                Flush(host);
                Assert.Equal(1, host.RenderScaling);
                foreach (ToggleButton control in sheet.GetVisualDescendants().OfType<ToggleButton>())
                {
                    Point point = control.TranslatePoint(default, host)!.Value;
                    Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                    if (!styles && !control.Classes.Contains("coreChoicePreview"))
                    {
                        Assert.NotNull(control.Theme);
                        Assert.Equal(control is RadioButton ? typeof(RadioButton) : typeof(CheckBox), control.Theme.TargetType);
                        Assert.NotEmpty(control.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Shape>());
                    }
                    Assert.True(point.X >= 4 && point.Y >= 4);
                    Assert.True(point.X + control.Bounds.Width + 4 <= host.Width);
                    Assert.True(point.Y + control.Bounds.Height + 4 <= host.Height);
                }
                if (string.IsNullOrWhiteSpace(destination)) return;
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(1200, (int)height), frame.PixelSize);
                string path = System.IO.Path.Combine(destination, name);
                frame.Save(path, PngBitmapEncoderOptions.Default);
                Assert.True(new FileInfo(path).Length <= 1_000_000, name);
                output.WriteLine($"{name}: {frame.PixelSize}, scale 1.0, {new FileInfo(path).Length} bytes");
            }
            finally { host.Close(); }
        }
    }

    private static void ShowFluentFocusAdorners(Window host)
    {
        // A state sheet has several focus samples. Build each from the native Fluent adorner template.
        foreach (ToggleButton control in host.GetVisualDescendants().OfType<ToggleButton>()
            .Where(control => !control.Classes.Contains("coreChoicePreview") && control.Tag is ChoiceState { Focus: true }).ToArray())
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(control)!;
            var template = control.FocusAdorner ?? layer.DefaultFocusAdorner;
            if (template is null) continue;
            Control adorner = template.Build()!;
            adorner.IsHitTestVisible = false;
            AdornerLayer.SetAdornedElement(adorner, control);
            layer.Children.Add(adorner);
        }
    }

    private static Border BuildSheet(bool dark, bool square)
    {
        var stack = new StackPanel { Spacing = 24 };
        stack.Children.Add(Heading("Core / choices", $"{(dark ? "DARK" : "LIGHT")} / {(square ? "SQUARE" : "PILL DEFAULT")} / 100%"));
        stack.Children.Add(Label("Clear selection. A full row hit target. Labels that stay readable.", 13));
        stack.Children.Add(Preview());
        stack.Children.Add(StateSection(false));
        stack.Children.Add(StateSection(true));
        stack.Children.Add(Examples());
        stack.Children.Add(Label("20 DIP indicator · 8 DIP label gap · 32 DIP row · 2 DIP focus ring with a 2 DIP exterior gap", 11));
        return new Border { Padding = new Thickness(32), Child = stack };
    }

    private static Border Preview()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        Add(row, Sample(false, new("Checked", true), "Include archived items"), 0, 0);
        Add(row, Sample(true, new("Checked", true), "Standard review"), 0, 2);
        return Surface(new StackPanel { Spacing = 8, Children = { Label("Review options", 16, true), row } });
    }

    private static Border StateSection(bool radio)
    {
        bool?[] selections = radio ? [false, true] : [false, true, null];
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(radio ? "176,*,*" : "176,*,*,*"),
            RowDefinitions = new RowDefinitions("28,56,56,56,56,56"),
        };
        Add(grid, Label("Interaction", 11, true), 0, 0);
        for (int column = 0; column < selections.Length; column++)
            Add(grid, Label(selections[column] is true ? "Checked" : selections[column] is null ? "Indeterminate" : "Unchecked", 11, true), 0, column + 1);
        for (int row = 0; row < Interactions.Length; row++)
        {
            ChoiceState state = Interactions[row];
            Add(grid, Label(state.Name, 11), row + 1, 0);
            for (int column = 0; column < selections.Length; column++)
            {
                ToggleButton control = Sample(radio, state with { Checked = selections[column] }, radio ? "Standard review" : "Include archive");
                control.Margin = new Thickness(0, 0, 16, 0);
                Add(grid, control, row + 1, column + 1);
            }
        }
        return Surface(new StackPanel { Spacing = 12, Children =
        {
            Label(radio ? "RadioButton" : "CheckBox", 16, true),
            grid,
        } });
    }

    private static Border Examples()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,32,*") };
        ToggleButton compactFirst = Sample(false, new("Checked", true), "Include archive");
        ToggleButton compactSecond = Sample(false, new("Mixed", null), "Include annotations");
        ToggleButton compactRadio = Sample(true, new("Checked", true), "Standard review");
        foreach (ToggleButton control in new[] { compactFirst, compactSecond, compactRadio }) control.Classes.Add("compact");
        Add(grid, new StackPanel { Spacing = 6, Children = { Label("Compact / 24 DIP", 16, true), compactFirst, compactSecond, compactRadio } }, 0, 0);
        ToggleButton wrapped = Sample(false, new("Checked", true), "Include archived items from every collection and retain all original annotations for the next review.");
        wrapped.Width = 360;
        wrapped.HorizontalAlignment = HorizontalAlignment.Left;
        ToggleButton wrappedRadio = Sample(true, new("Checked", true), "Use the extended review mode to retain original annotations across every collection.");
        wrappedRadio.Width = 360;
        wrappedRadio.HorizontalAlignment = HorizontalAlignment.Left;
        Add(grid, new StackPanel { Spacing = 6, Children = { Label("Long labels / top alignment", 16, true), wrapped, wrappedRadio } }, 0, 2);
        return Surface(grid);
    }

    private static Border BuildComparison()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        for (int side = 0; side < 2; side++)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*") };
            var rows = new List<ChoiceState>();
            var radios = new List<bool>();
            foreach (bool radio in new[] { false, true })
            foreach (bool? selected in radio ? new bool?[] { false, true } : [false, true, null])
            foreach (ChoiceState interaction in Interactions)
            {
                rows.Add(interaction with { Checked = selected });
                radios.Add(radio);
            }
            grid.RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("40", rows.Count)));
            for (int row = 0; row < rows.Count; row++)
            {
                ChoiceState state = rows[row];
                string selection = state.Checked is true ? "Checked" : state.Checked is null ? "Mixed" : "Unchecked";
                Add(grid, Label($"{(radios[row] ? "Radio" : "Check")} / {selection}\n{state.Name}", 11), row, 0);
                ToggleButton control = Sample(radios[row], state, "Include archive");
                if (side == 1) control.Classes.Add("coreChoicePreview");
                Add(grid, control, row, 1);
            }
            Border pane = Surface(new StackPanel { Spacing = 16, Children = { Label(side == 0 ? "Before / Fluent default" : "After / Core choices", 16, true), grid } });
            if (side == 1)
            {
                var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/ChoiceStyles.axaml");
                pane.Styles.Add(new StyleInclude(uri) { Source = uri });
            }
            Add(columns, pane, 0, side * 2);
        }
        return new Border { Padding = new Thickness(32), Child = new StackPanel { Spacing = 24, Children =
        {
            Heading("Choice controls / before and after", "LIGHT / 100%"),
            Label("Native controls and behavior. Shared geometry, palette, and keyboard focus.", 13), columns,
        } } };
    }

    private static Grid Heading(string title, string detail)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Add(grid, Label(title, 16, true), 0, 0);
        Add(grid, Label(detail, 11), 0, 1);
        return grid;
    }

    private static Border Surface(Control child)
    {
        var surface = new Border { Padding = new Thickness(24), Child = child };
        surface.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
        return surface;
    }

    private static TextBlock Label(string text, int size, bool strong = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        label.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("NfcUiFontFamily"));
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(strong ? "NfcTextStrongBrush" : "NfcTextSecondaryBrush"));
        return label;
    }

    private static void Add(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
