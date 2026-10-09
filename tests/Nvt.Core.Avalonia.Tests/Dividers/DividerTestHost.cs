// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Dividers;

internal readonly record struct DividerState(string Name, bool Over = false, bool Pressed = false,
    bool Focus = false, bool Disabled = false);

internal static class DividerTestHost
{
    internal static readonly string[] StyleFiles = ["ExpanderStyles", "ProgressStyles", "DividerStyles"];
    internal static readonly string[] SurfaceKeys = ["NfcSurfaceBrush", "NfcAppBackgroundBrush", "NfcSelectionSurfaceBrush"];
    internal static readonly DividerState[] States =
    [
        new("Rest"), new("Pointer over", Over: true), new("Pressed", Over: true, Pressed: true),
        new("Keyboard focus", Focus: true), new("Disabled", Disabled: true),
        new("Disabled interaction", Over: true, Pressed: true, Focus: true, Disabled: true),
    ];

    internal static Window Create(Control content, bool dark = false, bool styles = true,
        double width = 600, double height = 360, bool defaultThemes = true)
    {
        var host = new Window { Content = content, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"), FontSize = 13 };
        var fluent = new FluentTheme();
        host.Styles.Add(fluent);
        if (styles)
            foreach (string file in StyleFiles) Include(host, file);
        else if (defaultThemes)
            ApplyFluent(content, fluent);
        host.Bind(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("NfcAppBackgroundBrush"));
        return host;
    }

    internal static void ApplyFluent(Control control, FluentTheme fluent)
    {
        if (control is TemplatedControl templated && fluent.TryGetResource(control.StyleKey, null, out object? theme))
            templated.Theme = Assert.IsType<ControlTheme>(theme);
        if (control is Panel panel) foreach (Control child in panel.Children) ApplyFluent(child, fluent);
        else if (control is Decorator { Child: { } child }) ApplyFluent(child, fluent);
    }

    internal static void Include(Window host, string file)
    {
        var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{file}.axaml");
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
    }

    internal static void Show(Window host, bool freeze = true)
    {
        host.Show();
        Flush(host);
        if (!freeze) return;
        foreach (Animatable visual in host.GetVisualDescendants().OfType<Animatable>()) visual.Transitions = null;
        Flush(host);
    }

    internal static void Flush(Window host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }

    internal static void SetState(Control control, DividerState state)
    {
        control.IsEnabled = !state.Disabled;
        var pseudo = (IPseudoClasses)control.Classes;
        pseudo.Set(":pointerover", state.Over);
        pseudo.Set(":pressed", state.Pressed);
        pseudo.Set(":focus-visible", state.Focus);
    }

    internal static T Part<T>(Control control, string name) where T : Control =>
        Assert.Single(control.GetVisualDescendants().OfType<T>(), part => part.Name == name);

    internal static ToggleButton Header(Expander expander) => Part<ToggleButton>(expander, "ExpanderHeader");
    internal static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    internal static object Resource(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, control.ActualThemeVariant, out object? value), key);
        Assert.NotNull(value);
        return value;
    }

    internal static Color ResourceColor(Control control, string key) => ColorOf(Assert.IsAssignableFrom<IBrush>(Resource(control, key)));

    internal static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    internal static (Grid Grid, GridSplitter Splitter, Border Before, Border After) SplitGrid(bool horizontal)
    {
        var splitter = new GridSplitter { ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        splitter.Classes.Add(horizontal ? "horizontal" : "vertical");
        var before = new Border();
        var after = new Border();
        var grid = new Grid { Margin = new Thickness(16) };
        if (horizontal)
        {
            grid.RowDefinitions = new RowDefinitions("*,Auto,*");
            Grid.SetRow(splitter, 1);
            Grid.SetRow(after, 2);
        }
        else
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*,Auto,*");
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(after, 2);
        }
        grid.Children.Add(before);
        grid.Children.Add(splitter);
        grid.Children.Add(after);
        return (grid, splitter, before, after);
    }
}
