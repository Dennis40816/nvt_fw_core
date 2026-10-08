// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.ListMenu.ListMenuTestHost;

namespace Nvt.Core.Avalonia.Tests.ListMenu;

/// <summary>Renders headless list and menu state sheets and a side-by-side Fluent comparison.</summary>
public sealed class ListMenuStylesRenderer(ITestOutputHelper output)
{
    /// <summary>Checks the state-sheet geometry and writes exactly four images only when explicitly enabled.</summary>
    [AvaloniaFact]
    public void RenderStateSheetsAndFluentComparison()
    {
        string? destination = Environment.GetEnvironmentVariable("NVT_LIST_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(destination);
        foreach ((bool dark, bool square, string name) in new[]
        {
            (false, false, "list-light.png"), (true, false, "list-dark.png"),
            (false, true, "list-square-light.png"), (true, true, "list-square-dark.png"),
        })
        {
            Control sheet = Sheet(dark, square);
            Window host = Create(sheet, dark, width: 1200, height: 1130);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Restore(sheet);
                Flush(host);
                CheckBounds(host);
                Export(host, destination, name);
            }
            finally { host.Close(); }
        }
        var comparison = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        for (int index = 0; index < 2; index++)
        {
            Border scope = Comparison(index == 1);
            if (index == 1)
            {
                var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/ListStyles.axaml");
                scope.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(uri) { Source = uri });
                uri = new Uri("avares://Nvt.Core.Avalonia/Theme/MenuStyles.axaml");
                scope.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(uri) { Source = uri });
            }
            Grid.SetColumn(scope, index * 2);
            comparison.Children.Add(scope);
        }
        Window before = Create(new Border { Padding = new Thickness(32), Child = comparison }, core: false, width: 1200, height: 1560);
        try
        {
            Show(before);
            Restore(comparison);
            Flush(before);
            CheckBounds(before);
            Export(before, destination, "list-before-light.png");
        }
        finally { before.Close(); }
    }

    private void Export(Window host, string? destination, string name)
    {
        using var frame = host.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(1200, frame.PixelSize.Width);
        if (string.IsNullOrWhiteSpace(destination)) return;
        string path = System.IO.Path.Combine(destination, name);
        frame.Save(path, PngBitmapEncoderOptions.Default);
        Assert.True(new FileInfo(path).Length <= 1_000_000);
        output.WriteLine($"{name}: {frame.PixelSize.Width} x {frame.PixelSize.Height}, scale 1, {new FileInfo(path).Length} bytes");
    }

    private static Border Sheet(bool dark, bool square)
    {
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*,*,*") };
        rows.RowDefinitions.Add(new RowDefinition(36, GridUnitType.Pixel));
        Add(rows, Label("STATE", 11), 0, 0);
        Add(rows, Label("LIST ITEM", 11), 0, 1);
        Add(rows, Label("DROPDOWN ITEM", 11), 0, 2);
        Add(rows, Label("MENU COMMAND", 11), 0, 3);
        for (int index = 0; index < States.Length; index++)
        {
            rows.RowDefinitions.Add(new RowDefinition(46, GridUnitType.Pixel));
            ItemState state = States[index];
            Add(rows, Label(state.Name.Replace("Selected", "Selected / checked", StringComparison.Ordinal), 11), index + 1, 0);
            Add(rows, Sample(0, state), index + 1, 1);
            Add(rows, Sample(1, state), index + 1, 2);
            Add(rows, Sample(2, state), index + 1, 3);
        }
        var examples = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*,24,*") };
        Add(examples, ListPreview(false), 0, 0);
        Add(examples, ListPreview(true), 0, 2);
        Add(examples, MenuPreview(), 0, 4);
        var bar = new Menu
        {
            Items =
            {
                new MenuItem { Header = "_File", Items = { new MenuItem { Header = "_Open sample" }, new MenuItem { Header = "_Save copy" } } },
                new MenuItem { Header = "_Edit", Items = { new MenuItem { Header = "_Copy" } } },
                new MenuItem { Header = "_View", Items = { new MenuItem { Header = "_Details" } } },
                new MenuItem { Header = "Unavailable", IsEnabled = false },
            },
        };
        var content = new StackPanel
        {
            Spacing = 24,
            Children =
            {
                new StackPanel { Spacing = 8, Children =
                {
                    Label($"CORE / {(dark ? "DARK" : "LIGHT")} / {(square ? "SQUARE" : "PILL")}", 11, accent: true),
                    Label("Lists and menus", 24, strong: true),
                    Label("Soft accent selection. Shared dropdown rows. Native keyboard navigation.", 13),
                }},
                Surface(rows, 20),
                examples,
                Surface(new StackPanel { Spacing = 12, Children = { Label("MENU BAR", 11), bar } }, 16),
                Label("32 DIP rows · compact 24 DIP · inset list focus · 2 px menu ring with a 2 px gap · 150 ms transitions", 11),
            },
        };
        return new Border { Padding = new Thickness(32), Child = content };
    }

    private static Border Comparison(bool core)
    {
        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(Label(core ? "CORE / PILL" : "DEFAULT FLUENT", 11, accent: core));
        content.Children.Add(Label(core ? "One shared look" : "Before adoption", 24, strong: true));
        content.Children.Add(Label("Same controls, labels, states, and scale.", 13));
        ItemState[] states = [States[0], States[1], States[2], States[3], States[6], States[8]];
        foreach (int family in new[] { 0, 1, 2 })
        {
            content.Children.Add(Label(family == 0 ? "ListBoxItem" : family == 1 ? "ComboBoxItem" : "MenuItem", 16, strong: true));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("144,*") };
            for (int index = 0; index < states.Length; index++)
            {
                grid.RowDefinitions.Add(new RowDefinition(family == 2 ? 80 : 52, GridUnitType.Pixel));
                Add(grid, Label(states[index].Name, 11), index, 0);
                TemplatedControl sample = Sample(family, states[index]);
                Add(grid, family == 2 ? new ContextMenu { Items = { sample }, VerticalAlignment = VerticalAlignment.Center } : sample, index, 1);
            }
            content.Children.Add(grid);
        }
        content.Children.Add(Label(core ? "Token colors and corners. Soft selected fill." : "Host theme defaults before Core style includes.", 11));
        return Surface(content, 20);
    }

    private static TemplatedControl Sample(int family, ItemState state)
    {
        TemplatedControl item = family == 0 ? new ListBoxItem { Content = "Sample item" }
            : family == 1 ? new ComboBoxItem { Content = "Sample item" }
            : new MenuItem { Header = "_Open sample", InputGesture = new KeyGesture(global::Avalonia.Input.Key.O, KeyModifiers.Control),
                ToggleType = MenuItemToggleType.CheckBox, IsChecked = state.Selected };
        item.Tag = state;
        item.Margin = new Thickness(8, 0);
        item.VerticalAlignment = VerticalAlignment.Center;
        return item;
    }

    private static Border ListPreview(bool compact)
    {
        var list = new ListBox { SelectionMode = SelectionMode.Multiple };
        if (compact) list.Classes.Add("compact");
        foreach ((string text, bool selected) in new[] { ("Overview", true), ("Details", false), ("History", true), ("Attachments", false) })
        {
            var item = new ListBoxItem { Content = text };
            item.Tag = new ItemState(text, Selected: selected);
            list.Items.Add(item);
        }
        return Surface(new StackPanel { Spacing = 12, Children =
        {
            Label(compact ? "COMPACT / 24 DIP" : "MULTI-SELECTION / 32 DIP", 11),
            list,
            Label("The host supplies the surface.", 11),
        }}, 16);
    }

    private static StackPanel MenuPreview()
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "_Open sample", Icon = new TextBlock { Text = "+" }, InputGesture = new KeyGesture(global::Avalonia.Input.Key.O, KeyModifiers.Control) },
                new MenuItem { Header = "_Copy", InputGesture = new KeyGesture(global::Avalonia.Input.Key.C, KeyModifiers.Control) },
                new Separator(),
                new MenuItem { Header = "Show details", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true },
                new MenuItem { Header = "_More", Items = { new MenuItem { Header = "History" } } },
                new MenuItem { Header = "Unavailable", IsEnabled = false },
            },
        };
        return new StackPanel { Spacing = 12, Children = { Label("POPUP SURFACE / 4 DIP PADDING", 11), menu } };
    }

    private static TextBlock Label(string text, double size, bool strong = false, bool accent = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(accent ? "NfcAccentStrongBrush" : strong ? "NfcTextBrush" : "NfcTextSecondaryBrush"));
        return label;
    }

    private static Border Surface(Control child, double padding)
    {
        var border = new Border { Child = child, Padding = new Thickness(padding), VerticalAlignment = VerticalAlignment.Top };
        border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
        return border;
    }

    private static void Add(Grid grid, Control child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static void Restore(Control root)
    {
        foreach (TemplatedControl item in root.GetVisualDescendants().OfType<TemplatedControl>())
            if (item.Tag is ItemState state)
            {
                Freeze(item);
                SetState(item, state);
            }
    }

    private static void CheckBounds(Window host)
    {
        foreach (Control control in host.GetVisualDescendants().OfType<Control>()
                     .Where(control => control is TextBlock or ListBoxItem or MenuItem or ContextMenu))
        {
            if (!control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) continue;
            Point point = control.TranslatePoint(default, host)!.Value;
            Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= host.Width + 0.1
                && point.Y + control.Bounds.Height <= host.Height + 0.1, $"{control.GetType().Name}: {point} / {control.Bounds}");
        }
    }
}
