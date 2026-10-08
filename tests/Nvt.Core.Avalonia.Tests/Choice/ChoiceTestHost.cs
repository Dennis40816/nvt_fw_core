// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Choice;

internal readonly record struct ChoiceState(string Name, bool? Checked = false, bool Hover = false,
    bool Pressed = false, bool Focus = false, bool Disabled = false);

internal static class ChoiceTestHost
{
    private sealed record HostOptions(bool Snapshot, bool Fluent);

    internal static readonly ChoiceState[] Interactions =
    [
        new("Rest"), new("Pointer over", Hover: true), new("Pressed", Hover: true, Pressed: true),
        new("Keyboard focus", Focus: true), new("Disabled", Disabled: true),
    ];

    internal static Window Create(Control content, bool dark = false, bool styles = true,
        double width = 600, double height = 300, bool snapshot = true)
    {
        var host = new Window
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Width = width, Height = height, Content = content, Tag = new HostOptions(snapshot, !styles),
        };
        var tokens = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
        host.Resources.MergedDictionaries.Add(new ResourceInclude(tokens) { Source = tokens });
        host.Styles.Add(new FluentTheme());
        if (styles) Include(host);
        host.Bind(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("NfcAppBackgroundBrush"));
        host.Bind(TemplatedControl.FontFamilyProperty, new DynamicResourceExtension("NfcUiFontFamily"));
        host.FontSize = 13;
        return host;
    }

    internal static void Include(Window host)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/ChoiceStyles.axaml");
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
    }

    internal static void Show(Window host)
    {
        if (host.Tag is HostOptions { Fluent: true })
            foreach (ToggleButton control in host.GetLogicalDescendants().OfType<ToggleButton>()
                .Where(control => control is CheckBox or RadioButton && !control.Classes.Contains("coreChoicePreview")))
                control.Theme = (ControlTheme)host.FindResource(control is RadioButton ? typeof(RadioButton) : typeof(CheckBox))!;
        host.Show();
        Flush(host);
        if (host.Tag is HostOptions { Snapshot: true })
        {
            foreach (Animatable visual in host.GetVisualDescendants().OfType<Animatable>()) visual.Transitions = null;
            Restore(host);
            Flush(host);
        }
    }

    internal static void Flush(Window host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }

    internal static ToggleButton Sample(bool radio, ChoiceState state, string label = "Include archived items")
    {
        ToggleButton control = radio
            ? new RadioButton { GroupName = Guid.NewGuid().ToString("N") }
            : new CheckBox { IsThreeState = true };
        control.Content = label;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Tag = state;
        SetState(control, state);
        return control;
    }

    internal static void SetState(ToggleButton control, ChoiceState state)
    {
        control.IsEnabled = !state.Disabled;
        control.IsChecked = state.Checked;
        var pseudo = (IPseudoClasses)control.Classes;
        pseudo.Set(":pointerover", state.Hover);
        pseudo.Set(":pressed", state.Pressed);
        pseudo.Set(":focus-visible", state.Focus);
    }

    internal static void Restore(Control root)
    {
        foreach (ToggleButton control in root.GetVisualDescendants().OfType<ToggleButton>())
            if (control.Tag is ChoiceState state) SetState(control, state);
    }

    internal static T Part<T>(Control control, string name) where T : Control =>
        Assert.Single(control.GetVisualDescendants().OfType<T>(), part => part.Name == name);

    internal static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    internal static Color ResourceColor(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out object? value), key);
        return ColorOf(Assert.IsAssignableFrom<IBrush>(value));
    }

    internal static void KeyStroke(Window host, Key key)
    {
        host.KeyPress(key, RawInputModifiers.None, default, null);
        host.KeyRelease(key, RawInputModifiers.None, default, null);
        Flush(host);
    }

    internal static double Contrast(Color a, Color b)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
}
