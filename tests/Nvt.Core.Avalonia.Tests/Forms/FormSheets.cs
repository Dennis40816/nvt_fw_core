// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

internal static class FormSheets
{
    internal static TextBlock Label(string text, double size = 13) => new()
    {
        Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
    };

    internal static void Render(string name, string description, Func<Control> build, ITestOutputHelper output,
        bool textOnly = false, bool comparison = false)
    {
        string? destination = Environment.GetEnvironmentVariable("NVT_FORMS_IMAGES_DIR");
        foreach (bool dark in comparison ? new[] { false } : new[] { false, true })
        foreach (ThemeShape shape in textOnly || comparison ? new[] { ThemeShape.Pill } : new[] { ThemeShape.Pill, ThemeShape.Square })
        {
            var section = new Border { Padding = new Thickness(24), Child = build() };
            section.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
            var root = new Border
            {
                Padding = new Thickness(32),
                Child = new StackPanel
                {
                    Spacing = 20,
                    Children =
                    {
                        Label($"CORE / {name.ToUpperInvariant()} / {(textOnly ? "FONT ROLES" : shape.ToString().ToUpperInvariant())} / {(dark ? "DARK" : "LIGHT")}", 24),
                        Label(description), section,
                        Label("100% scale / synthetic examples / 32 DIP rows / keyboard focus: 2 DIP ring + 2 DIP gap", 11),
                    },
                },
            };
            Window host = FormsTestHost.Create(root, dark, core: !comparison, width: 1200, height: 2600);
            try
            {
                ThemeShapes.SetShape(host.Resources, shape);
                if (comparison)
                    foreach (TemplatedControl control in root.GetVisualDescendants().OfType<TemplatedControl>())
                        if (control.GetVisualAncestors().OfType<StackPanel>().Any(panel => panel.Name == "FluentReferencePane"))
                            control.Theme = (global::Avalonia.Styling.ControlTheme)host.FindResource(control.GetType())!;
                FormsTestHost.Show(host);
                Restore(root);
                Flush(host);
                host.Height = Math.Ceiling(root.DesiredSize.Height);
                Flush(host);
                Assert.Equal(1, host.RenderScaling);
                foreach (Control control in root.GetVisualDescendants().OfType<Control>())
                {
                    if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0
                        || control.GetVisualAncestors().OfType<Viewbox>().Any()) continue;
                    Point point = control.TranslatePoint(default, host)!.Value;
                    Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= host.Width + 1
                        && point.Y + control.Bounds.Height <= host.Height + 1, $"{name}/{control.Name}: {point}, {control.Bounds}");
                }
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(1200, frame.PixelSize.Width);
                if (string.IsNullOrWhiteSpace(destination)) continue;
                Directory.CreateDirectory(destination);
                string file = comparison ? $"{name}-before-light.png" : textOnly ? $"{name}-{(dark ? "dark" : "light")}.png"
                    : $"{name}-{shape.ToString().ToLowerInvariant()}-{(dark ? "dark" : "light")}.png";
                string path = System.IO.Path.Combine(destination, file);
                frame.Save(path, PngBitmapEncoderOptions.Default);
                Assert.True(new FileInfo(path).Length <= 1_000_000, file);
                output.WriteLine($"{file}: {frame.PixelSize}, {new FileInfo(path).Length} bytes");
            }
            finally { host.Close(); }
        }
    }

    internal static void Restore(Control root)
    {
        FormsTestHost.Restore(root);
        foreach (TabItem tab in root.GetVisualDescendants().OfType<TabItem>())
            if (tab.Tag is FormState state)
            {
                tab.IsEnabled = !state.Disabled;
                var pseudo = (IPseudoClasses)tab.Classes;
                pseudo.Set(":pointerover", state.Hover);
                pseudo.Set(":pressed", state.Pressed);
                pseudo.Set(":focus-visible", state.Focus);
            }
    }

    internal static Control FormSection(string kind)
    {
        var panel = new StackPanel { Spacing = 16 };
        FormState[] states = kind == "combobox" ? [.. FormsTestHost.States, new("Open", Open: true)] : FormsTestHost.States;
        foreach (FormState state in states)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,220"), Height = 40 };
            row.Children.Add(Label(state.Name));
            TemplatedControl control = FormsTestHost.Sample(kind, state);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            var note = Label(state.Error ? "Danger border" : state.Focus ? "Keyboard only" : state.ReadOnly ? "Value retained" : "");
            note.Margin = new Thickness(24, 0, 0, 0);
            Grid.SetColumn(note, 2);
            row.Children.Add(note);
            panel.Children.Add(row);
        }
        if (kind == "textbox")
        {
            var field = new TextBox { PlaceholderText = "Placeholder text", Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(field);
        }
        if (kind == "combobox")
        {
            panel.Children.Add(Label("Popup rows keep the shared List styles", 11));
            panel.Children.Add(new ComboBoxItem { Content = "Option alpha", IsSelected = true });
            panel.Children.Add(new ComboBoxItem { Content = "Option beta" });
        }
        return panel;
    }

    internal static Control TabSection()
    {
        var panel = new StackPanel { Spacing = 16 };
        var headings = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,*") };
        var plain = Label("Unselected"); Grid.SetColumn(plain, 1); headings.Children.Add(plain);
        var selected = Label("Selected"); Grid.SetColumn(selected, 2); headings.Children.Add(selected);
        panel.Children.Add(headings);
        foreach (FormState state in FormsTestHost.States.Take(5))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,*"), Height = 48 };
            row.Children.Add(Label(state.Name));
            for (int column = 1; column <= 2; column++)
            {
                var tab = new TabItem { Header = column == 1 ? "Details" : "Overview", IsSelected = column == 2,
                    Tag = state, HorizontalAlignment = HorizontalAlignment.Left, Width = 180 };
                Grid.SetColumn(tab, column);
                row.Children.Add(tab);
            }
            panel.Children.Add(row);
        }
        panel.Children.Add(Label("Native strip and selected content"));
        panel.Children.Add(new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Overview", Content = new TextBlock { Text = "The selected page appears below the strip." } },
                new TabItem { Header = "Details", Content = "Details page" },
                new TabItem { Header = "Unavailable", IsEnabled = false },
            },
        });
        return panel;
    }

    internal static Control TextSection()
    {
        var panel = new StackPanel { Spacing = 18 };
        foreach (string name in new[] { "title", "heading", "body", "caption", "mono", "numbers", "bodyStrong", "captionStrong", "monoStrong", "monoCaption" })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), MinHeight = 34 };
            row.Children.Add(Label(name));
            string sample = name.StartsWith("mono", StringComparison.Ordinal) ? "0x0123ABCD / build 1.0.0 / sample-id-42"
                : name == "numbers" ? "0123456789  /  42 items  /  98.6%" : "A clear sample line for your workspace";
            var text = new TextBlock { Text = sample, VerticalAlignment = VerticalAlignment.Center };
            text.Classes.Add(name);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        var muted = new TextBlock { Text = "muted + body / Secondary notes use the same font role." };
        muted.Classes.Add("body"); muted.Classes.Add("muted");
        panel.Children.Add(muted);
        return panel;
    }

    internal static Control Comparison(bool tabs)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        for (int column = 0; column < 2; column++)
        {
            var panel = new StackPanel { Spacing = 16, Name = column == 0 ? "FluentReferencePane" : "CorePreviewPane" };
            panel.Children.Add(Label(column == 0 ? "Default Fluent" : "Core Astra", 16));
            if (tabs)
                panel.Children.Add(new TabControl { ItemsSource = new[] { new TabItem { Header = "Overview", Content = "Overview content" },
                    new TabItem { Header = "Details" }, new TabItem { Header = "Disabled", IsEnabled = false } } });
            else
                foreach (string kind in new[] { "textbox", "numericupdown", "combobox" })
                    panel.Children.Add(FormsTestHost.Sample(kind, new("Rest")));
            if (column == 1)
                foreach (string file in tabs ? new[] { "TabStyles" } : new[] { "FormStyles", "ListStyles" })
                {
                    var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{file}.axaml");
                    panel.Styles.Add(new StyleInclude(uri) { Source = uri });
                }
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
        }
        return grid;
    }
}
