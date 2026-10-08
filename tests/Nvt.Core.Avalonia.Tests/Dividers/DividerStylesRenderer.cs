// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Progress;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.Progress;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Dividers.DividerTestHost;

namespace Nvt.Core.Avalonia.Tests.Dividers;

/// <summary>Exports the shipped divider family and the actual Fluent baseline using headless rendering only.</summary>
public sealed partial class DividerStylesRenderer(ITestOutputHelper output)
{
    private static readonly DividerState[] VisibleStates = [.. States.Where(state => state.Name != "Disabled interaction")];
    private readonly record struct ExpanderSnapshot(DividerState State);

    /// <summary>Checks layout at scale one and writes four PNGs only when the image destination variable is set.</summary>
    [AvaloniaFact]
    public void RenderStateSheetsOrCheckLayout()
    {
        string? directory = Environment.GetEnvironmentVariable("NVT_DIVIDER_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        Render(BuildSheet(), "divider-light.png", dark: false, square: false);
        Render(BuildSheet(), "divider-dark.png", dark: true, square: false);
        Render(BuildSheet(), "divider-square-light.png", dark: false, square: true);
        Render(BuildComparison(), "divider-before-light.png", dark: false, square: false, comparison: true);

        void Render(Control sheet, string name, bool dark, bool square, bool comparison = false)
        {
            Window host = Create(sheet, dark, styles: !comparison, width: 1200, height: 2200, defaultThemes: false);
            try
            {
                using var clock = new DividerSnapshotClock();
                foreach (ProgressBar bar in sheet.GetVisualDescendants().OfType<ProgressBar>()) clock.Attach(bar);
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Restore(sheet);
                Flush(host);
                host.Height = Math.Ceiling(sheet.DesiredSize.Height);
                Flush(host);
                foreach (ProgressBar bar in sheet.GetVisualDescendants().OfType<ProgressBar>()
                    .Where(bar => bar.IsIndeterminate && !bar.Classes.Contains("reducedMotion"))) clock.Prepare(bar, host);
                clock.Tick(TimeSpan.Zero);
                clock.Tick(TimeSpan.FromMilliseconds(800));
                Flush(host);
                foreach (ProgressBar bar in sheet.GetVisualDescendants().OfType<ProgressBar>()
                    .Where(bar => bar.IsIndeterminate && !bar.Classes.Contains("reducedMotion")))
                    Assert.Contains(bar.GetVisualDescendants().OfType<Border>(), part =>
                    {
                        if (part.Name is not ("IndeterminateProgressBarIndicator" or "IndeterminateProgressBarIndicator2")) return false;
                        Point point = part.TranslatePoint(default, bar)!.Value;
                        return part.Opacity > 0 && part.IsVisible && point.X < bar.Bounds.Width && point.X + part.Bounds.Width > 0;
                    });
                Assert.Equal(1, host.RenderScaling);
                foreach (Control control in sheet.GetVisualDescendants().OfType<Control>()
                    .Where(control => control is Expander or ProgressBar or GridSplitter))
                {
                    Point point = control.TranslatePoint(default, host)!.Value;
                    Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, control.GetType().Name);
                    Assert.True(point.X >= 4 && point.Y >= 4, control.GetType().Name);
                    Assert.True(point.X + control.Bounds.Width + 4 <= host.Width, control.GetType().Name);
                    Assert.True(point.Y + control.Bounds.Height + 4 <= host.Height, control.GetType().Name);
                }
                if (string.IsNullOrWhiteSpace(directory)) return;
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(1200, frame.PixelSize.Width);
                Assert.Equal((int)host.Height, frame.PixelSize.Height);
                string path = System.IO.Path.Combine(directory, name);
                frame.Save(path, PngBitmapEncoderOptions.Default);
                Assert.True(new FileInfo(path).Length <= 1_000_000, name);
                output.WriteLine($"{name}: {frame.PixelSize.Width} x {frame.PixelSize.Height}, {new FileInfo(path).Length} bytes, scale 1.0");
            }
            finally { host.Close(); }
        }
    }

    private static Border BuildSheet()
    {
        return new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 20,
                Children =
                {
                    new StackPanel { Spacing = 6, Children = { Label("Core / disclosure, progress and dividers", 24, true),
                        Label("32 / 44 DIP headers · 3 / 6 / 10 DIP progress · 1 DIP lines · 6 DIP splitter targets", 13) } },
                    Surface("Expander", "One chevron family. Flat headers. Section headers add a top divider.", ExpanderSheet()),
                    Surface("ProgressBar", "Pill ends at every thickness. ProgressIndicator shares the same track and indicator tokens.", ProgressSheet()),
                    Surface("Separator", "Native Separator and Border.divider use the same one-pixel geometry.", SeparatorSheet()),
                    Surface("GridSplitter", "Vertical and horizontal variants. Accent lines on pointer over and while dragging.", SplitterSheet()),
                    Label("Keyboard focus: one 2 DIP ring, 2 DIP outside · Color and chevron transitions: 150 ms · Synthetic data only", 11),
                },
            },
        };
    }

    private static Grid ExpanderSheet()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,*,*,*"), ColumnSpacing = 14,
            RowDefinitions = new RowDefinitions("28,96,96,96,96,96,96") };
        string[] headings = ["State", "Default / closed", "Default / open", "Section / closed", "Section / open"];
        for (int column = 0; column < headings.Length; column++) Add(grid, Label(headings[column], 11, true), 0, column);
        for (int index = 0; index < VisibleStates.Length; index++)
        {
            DividerState state = VisibleStates[index];
            var label = Label(state.Name, 11);
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, 19, 0, 0);
            Add(grid, label, index + 1, 0);
            for (int variant = 0; variant < 4; variant++)
                Add(grid, Disclosure(state, section: variant >= 2, expanded: variant % 2 == 1), index + 1, variant + 1);
        }
        Add(grid, Label("Expand up", 11), 6, 0);
        for (int variant = 0; variant < 4; variant++)
            Add(grid, Disclosure(new DividerState("Rest"), variant >= 2, variant % 2 == 1, up: true), 6, variant + 1);
        return grid;
    }

    private static Expander Disclosure(DividerState state, bool section = false, bool expanded = false, bool up = false)
    {
        var expander = new Expander { Header = "Details", Content = Label("Synthetic details", 11), IsExpanded = expanded,
            ExpandDirection = up ? ExpandDirection.Up : ExpandDirection.Down,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, section ? 4 : 10, 0, 0),
            Tag = new ExpanderSnapshot(state) };
        if (section) expander.Classes.Add("section");
        return expander;
    }

    private static Grid ProgressSheet()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,*"), ColumnSpacing = 28,
            RowDefinitions = new RowDefinitions("28,42,42,42,42,42,42,42,42") };
        Add(grid, Label("Role / state", 11, true), 0, 0);
        Add(grid, Label("Determinate", 11, true), 0, 1);
        Add(grid, Label("Indeterminate", 11, true), 0, 2);
        (string Label, string Role, double Value, bool Disabled, bool Reduced)[] rows =
        [
            ("Default / 6 DIP", "", 42, false, false), ("thin / 3 DIP", "thin", 42, false, false),
            ("thick / 10 DIP", "thick", 42, false, false), ("Empty / 0%", "", 0, false, false),
            ("Complete / 100%", "", 100, false, false), ("Disabled", "", 42, true, false),
            ("Reduced motion", "", 42, false, true), ("ProgressIndicator / 42%", "", 42, false, false),
        ];
        for (int index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            Add(grid, Label(row.Label, 11), index + 1, 0);
            for (int mode = 0; mode < 2; mode++)
            {
                ProgressBar bar = index == rows.Length - 1
                    ? new ProgressIndicator { Maximum = 1, Progress = new ProgressUpdate(0.42, "Synthetic step") }
                    : new ProgressBar { Value = row.Value };
                bar.IsEnabled = !row.Disabled;
                bar.IsIndeterminate = mode == 1;
                bar.VerticalAlignment = VerticalAlignment.Center;
                if (row.Role.Length > 0) bar.Classes.Add(row.Role);
                if (row.Reduced) bar.Classes.Add("reducedMotion");
                Add(grid, bar, index + 1, mode + 1);
            }
        }
        return grid;
    }

    private static Grid SeparatorSheet()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 28,
            RowDefinitions = new RowDefinitions("24,80") };
        string[] titles = ["Horizontal / soft", "Horizontal / strong", "Vertical / soft", "Vertical / strong"];
        for (int column = 0; column < 4; column++)
        {
            bool vertical = column >= 2, strong = column % 2 == 1;
            Add(grid, Label(titles[column], 11, true), 0, column);
            var separator = new Separator();
            var border = new Border { Classes = { "divider" } };
            foreach (Control line in new Control[] { separator, border })
            {
                if (vertical) line.Classes.Add("vertical");
                if (strong) line.Classes.Add("strong");
            }
            var cell = new Grid { RowDefinitions = new RowDefinitions("*,*"), ColumnDefinitions = new ColumnDefinitions("100,*"), ColumnSpacing = 12 };
            Add(cell, Label("Separator", 11), 0, 0);
            Add(cell, Label("Border.divider", 11), 1, 0);
            Add(cell, separator, 0, 1);
            Add(cell, border, 1, 1);
            if (vertical) cell.RowSpacing = 8;
            Add(grid, cell, 1, column);
        }
        return grid;
    }

    private static Grid SplitterSheet()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*,*,*,*,*"), ColumnSpacing = 28,
            RowDefinitions = new RowDefinitions("28,68,32") };
        Add(grid, Label("State", 11, true), 0, 0);
        Add(grid, Label("Vertical", 11), 1, 0);
        Add(grid, Label("Horizontal", 11), 2, 0);
        for (int index = 0; index < VisibleStates.Length; index++)
        {
            DividerState state = VisibleStates[index];
            Add(grid, Label(state.Pressed ? "Dragging / pressed" : state.Name, 11, true), 0, index + 1);
            Add(grid, Splitter(state, horizontal: false), 1, index + 1);
            Add(grid, Splitter(state, horizontal: true), 2, index + 1);
        }
        return grid;
    }

    private static GridSplitter Splitter(DividerState state, bool horizontal)
    {
        var splitter = new GridSplitter { Tag = state, Margin = new Thickness(8) };
        splitter.Classes.Add(horizontal ? "horizontal" : "vertical");
        return splitter;
    }

    private static Border BuildComparison()
    {
        var before = Preview("Fluent / before");
        var after = Preview("Core / after");
        ApplyFluent(before, new FluentTheme());
        foreach (string file in StyleFiles)
        {
            var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{file}.axaml");
            after.Styles.Add(new StyleInclude(uri) { Source = uri });
        }
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        Add(columns, before, 0, 0);
        Add(columns, after, 0, 1);
        return new Border { Padding = new Thickness(24), Child = new StackPanel { Spacing = 20,
            Children = { Label("Core / the same controls before and after", 24, true),
                Label("Actual Fluent templates on the left. Core styles on the right. Light theme, scale 1.0, synthetic labels.", 13), columns } } };
    }

    private static Border Preview(string title)
    {
        var panel = new StackPanel { Spacing = 20 };
        panel.Children.Add(Label(title, 16, true));
        panel.Children.Add(Label("Expander / every header state", 13, true));
        foreach (DividerState state in VisibleStates)
        {
            var cell = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), Height = 104, ColumnSpacing = 16 };
            var label = Label(state.Name, 11);
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, 19, 0, 0);
            Add(cell, label, 0, 0);
            Add(cell, Disclosure(state, section: true, expanded: true), 0, 1);
            panel.Children.Add(cell);
        }
        panel.Children.Add(Label("Progress / default, thin, thick", 13, true));
        foreach (string role in new[] { "", "thin", "thick" })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,*"), ColumnSpacing = 16, Height = 36 };
            Add(row, Label(role.Length == 0 ? "Default" : role, 11), 0, 0);
            for (int mode = 0; mode < 2; mode++)
            {
                var bar = new ProgressBar { Value = 42, IsIndeterminate = mode == 1, VerticalAlignment = VerticalAlignment.Center };
                if (role.Length > 0) bar.Classes.Add(role);
                Add(row, bar, 0, mode + 1);
            }
            panel.Children.Add(row);
        }
        panel.Children.Add(Label("Separator / soft and strong", 13, true));
        var separators = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24, Height = 24,
            Children = { new Separator(), new Separator { Classes = { "strong" } } } };
        Grid.SetColumn(separators.Children[1], 1);
        panel.Children.Add(separators);
        panel.Children.Add(Label("GridSplitter / every state", 13, true));
        var splitters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), ColumnSpacing = 12, Height = 112 };
        for (int index = 0; index < VisibleStates.Length; index++)
        {
            var cell = new Grid { RowDefinitions = new RowDefinitions("28,*"), RowSpacing = 12 };
            var splitter = Splitter(VisibleStates[index], horizontal: false);
            splitter.Width = 6;
            Add(cell, Label(VisibleStates[index].Pressed ? "Dragging" : VisibleStates[index].Focus ? "Focus" : VisibleStates[index].Over ? "Over" : VisibleStates[index].Name, 11), 0, 0);
            Add(cell, splitter, 1, 0);
            Add(splitters, cell, 0, index);
        }
        panel.Children.Add(splitters);
        var root = new Border { Padding = new Thickness(24), Child = panel };
        root.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
        return root;
    }

    private static void Restore(Control sheet)
    {
        foreach (Expander expander in sheet.GetVisualDescendants().OfType<Expander>())
            if (expander.Tag is ExpanderSnapshot snapshot)
            {
                expander.IsEnabled = !snapshot.State.Disabled;
                SetState(Header(expander), snapshot.State);
            }
        foreach (GridSplitter splitter in sheet.GetVisualDescendants().OfType<GridSplitter>())
            if (splitter.Tag is DividerState state) SetState(splitter, state);
    }

    private static Border Surface(string heading, string description, Control content)
    {
        var root = new Border { Padding = new Thickness(24), Child = new StackPanel { Spacing = 16,
            Children = { new StackPanel { Spacing = 6, Children = { Label(heading, 16, true), Label(description, 13) } }, content } } };
        root.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
        return root;
    }

    private static TextBlock Label(string text, int size, bool strong = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal };
        label.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("NfcUiFontFamily"));
        label.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension($"NfcFontSize{size}"));
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(strong ? "NfcTextStrongBrush" : "NfcTextSecondaryBrush"));
        return label;
    }

    private static void Add(Grid grid, Control child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
