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
using Nvt.Core.Hosting;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Renders English property panels with identical data before and after opting in.</summary>
public sealed class InlineInputRenderer(ITestOutputHelper output)
{
    private static readonly string[] _headings = ["TextBox", "ComboBox", "Editable ComboBox", "Spinner right", "Spinner left"];
    /// <summary>Exports Light/Dark and Pill/Square comparisons using the existing headless form-sheet approach.</summary>
    [AvaloniaFact]
    public void RenderInlineInputs()
    {
        string? destination = HostEnvironment.GetVariable("NVT_INLINE_IMAGES_DIR");
        foreach (bool dark in new[] { false, true })
            foreach (ThemeShape shape in new[] { ThemeShape.Pill, ThemeShape.Square })
            {
                var comparison = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 28 };
                comparison.Children.Add(PropertyPanel(false));
                Control after = PropertyPanel(true);
                Grid.SetColumn(after, 1);
                comparison.Children.Add(after);
                var root = new StackPanel
                {
                    Margin = new Thickness(32),
                    Spacing = 22,
                    Children =
                    {
                        FormSheets.Label($"Property panel / {(dark ? "Dark" : "Light")} / {shape}", 24),
                        FormSheets.Label("Variant A: soft fill, no frame. Editing reveals a frame; keyboard focus adds one exterior ring."),
                        comparison, FormSheets.Label("Inline input states", 19), StateStrip(),
                        FormSheets.Label("32 DIP controls / 8 DIP horizontal padding / 1 DIP editing frame / 2 DIP keyboard ring + 2 DIP gap", 12),
                    },
                };
                Window host = InlineInputTestHost.Create(root, dark, width: 1320, height: 1400);
                try
                {
                    ThemeShapes.SetShape(host.Resources, shape);
                    FormsTestHost.Show(host);
                    foreach (TemplatedControl control in root.GetVisualDescendants().OfType<TemplatedControl>())
                        if (control.Tag is FormState state)
                        {
                            if (control.Classes.Contains("inlineInput"))
                                InlineInputTestHost.State(control, state, keyboardFocus: state.Name != "Pointer editing");
                            else FormsTestHost.SetState(control, state);
                        }
                    Flush(host);
                    host.Height = Math.Ceiling(root.DesiredSize.Height + 64);
                    Flush(host);
                    foreach (TemplatedControl control in root.GetVisualDescendants().OfType<TemplatedControl>().Where(c => c.Classes.Contains("inlineInput")))
                    {
                        Assert.Equal(32, control.Bounds.Height);
                        Point point = control.TranslatePoint(default, host)!.Value;
                        Assert.True(point.X >= 4 && point.Y >= 4 && point.X + control.Bounds.Width + 4 <= host.Width && point.Y + control.Bounds.Height + 4 <= host.Height);
                    }
                    using var frame = host.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.Equal(1, host.RenderScaling);
                    if (string.IsNullOrWhiteSpace(destination)) continue;
                    Directory.CreateDirectory(destination);
                    string path = System.IO.Path.Combine(destination, $"inline-input-before-after-{shape.ToString().ToLowerInvariant()}-{(dark ? "dark" : "light")}.png");
                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    output.WriteLine($"{path}: {frame.PixelSize}");
                }
                finally { host.Close(); }
            }
    }

    private static Border PropertyPanel(bool inline)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(FormSheets.Label(inline ? "After / inlineInput" : "Before / current Core style", 19));
        panel.Children.Add(FormSheets.Label("Step 3 allocation", 15));
        AddRow(panel, "Compensation model", Field("combobox", inline, "Current gain"));
        AddRow(panel, "Multi-owner strict overlap", Number(inline, 0.5m, "%"));
        AddRow(panel, "Auto play interval", Number(inline, 800, "ms"));
        AddRow(panel, "To Full layer stage", Number(inline, 3, ""));
        panel.Children.Add(FormSheets.Label("Pad Info", 15));
        TemplatedControl readOnly = Field("textbox", inline, "R12 C07");
        readOnly.Tag = new FormState("Read-only", ReadOnly: true);
        AddRow(panel, "Match (read-only)", readOnly);
        AddRow(panel, "Pad width", Number(inline, 4.125m, "mm"));
        AddRow(panel, "Pad height", Number(inline, 3.8m, "mm"));
        var border = new Border { Padding = new Thickness(20), Child = panel, CornerRadius = new CornerRadius(8) };
        border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NfcSurfaceBrush"));
        return border;
    }

    private static void AddRow(Panel panel, string label, Control value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), Height = 32 };
        row.Children.Add(FormSheets.Label(label));
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        panel.Children.Add(row);
    }

    private static TemplatedControl Field(string kind, bool inline, string value)
    {
        TemplatedControl control = InlineInputTestHost.Sample(kind);
        if (!inline) control.Classes.Remove("inlineInput");
        control.SetValue(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        if (control is TextBox text) text.Text = value;
        if (control is ComboBox combo) { combo.ItemsSource = new[] { value, "Voltage gain" }; combo.SelectedIndex = 0; }
        return control;
    }

    private static NumericUpDown Number(bool inline, decimal value, string unit)
    {
        var numeric = (NumericUpDown)Field("numericupdown", inline, "");
        numeric.Maximum = 10000;
        numeric.Value = value;
        numeric.FormatString = "0.###";
        numeric.InnerRightContent = new TextBlock { Text = unit, Classes = { "inlineInputUnit" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        return numeric;
    }

    private static StackPanel StateStrip()
    {
        var panel = new StackPanel { Spacing = 16 };
        var headings = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*,*,*,*,*"), ColumnSpacing = 18 };
        for (int i = 0; i < InlineInputTestHost.Kinds.Length; i++)
        {
            var heading = FormSheets.Label(_headings[i], 12);
            Grid.SetColumn(heading, i + 1);
            headings.Children.Add(heading);
        }
        panel.Children.Add(headings);
        foreach (FormState state in new FormState[]
        {
            new("Rest"), new("Hover", Hover: true), new("Pressed / open", Pressed: true, Open: true),
            new("Pointer editing", Focus: true), new("Keyboard focus", Focus: true),
            new("Disabled", Disabled: true), new("Read-only + hover", ReadOnly: true, Hover: true),
            new("Error", Error: true), new("Error + keyboard", Error: true, Focus: true),
        })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*,*,*,*,*"), ColumnSpacing = 18, Height = 32 };
            row.Children.Add(FormSheets.Label(state.Name));
            for (int i = 0; i < InlineInputTestHost.Kinds.Length; i++)
            {
                TemplatedControl control = Field(InlineInputTestHost.Kinds[i], true, "Current gain");
                control.Tag = state;
                Grid.SetColumn(control, i + 1);
                row.Children.Add(control);
            }
            panel.Children.Add(row);
        }
        return panel;
    }
}
