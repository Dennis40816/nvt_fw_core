// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.ListMenu;

internal readonly record struct ItemState(string Name, bool Selected = false, bool Hover = false,
    bool Pressed = false, bool Disabled = false, bool Focus = false);

internal static class ListMenuTestHost
{
    internal static readonly ItemState[] States =
    [
        new("Rest"), new("Pointer over", Hover: true), new("Pressed", Hover: true, Pressed: true),
        new("Selected", Selected: true), new("Selected + over", Selected: true, Hover: true),
        new("Selected + press", Selected: true, Hover: true, Pressed: true),
        new("Keyboard focus", Focus: true), new("Selected + focus", Selected: true, Focus: true),
        new("Disabled", Disabled: true), new("Disabled + selected", Selected: true, Disabled: true),
    ];

    internal static Window Create(Control content, bool dark = false, bool core = true,
        double width = 600, double height = 400)
    {
        var fluent = new FluentTheme();
        Application.Current!.Styles.Add(fluent);
        var host = new Window
        {
            Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"), FontSize = 13,
        };
        host.Closed += (_, _) => Application.Current.Styles.Remove(fluent);
        host.Styles.Add(new FluentTheme());
        if (core)
        {
            Include(host, "ListStyles");
            Include(host, "MenuStyles");
        }
        host.Bind(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("NfcAppBackgroundBrush"));
        host.Content = content;
        return host;
    }

    internal static void Include(Window host, string name)
    {
        var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{name}.axaml");
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
    }

    internal static void Show(Window host, bool snapshot = true)
    {
        host.Show();
        Flush(host);
        if (snapshot) Freeze(host);
    }

    internal static void Freeze(Control root)
    {
        root.Transitions = null;
        foreach (Animatable child in root.GetVisualDescendants().OfType<Animatable>()) child.Transitions = null;
    }

    internal static void Flush(Window host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }

    internal static void SetState(TemplatedControl item, ItemState state)
    {
        item.IsEnabled = !state.Disabled;
        if (item is ListBoxItem list) list.IsSelected = state.Selected;
        var pseudo = (IPseudoClasses)item.Classes;
        pseudo.Set(":selected", item is MenuItem ? state.Hover || state.Focus : state.Selected);
        pseudo.Set(":pointerover", state.Hover);
        pseudo.Set(":pressed", state.Pressed);
        pseudo.Set(":focus-visible", state.Focus);
        if (item is MenuItem)
        {
            Border? body = item.GetVisualDescendants().OfType<Border>().FirstOrDefault(part => part.Name == "PART_LayoutRoot");
            if (body is not null) ((IPseudoClasses)body.Classes).Set(":pointerover", state.Hover);
        }
    }

    internal static Border Part(Control owner, string name) =>
        Assert.Single(owner.GetVisualDescendants().OfType<Border>(), part => part.Name == name);

    internal static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    internal static Color ResourceColor(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out object? value), key);
        return ColorOf(Assert.IsAssignableFrom<IBrush>(value));
    }

    internal static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            double s = value / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    internal static (string Fill, string Text) Expected(ItemState state)
    {
        if (state.Disabled) return (state.Selected ? "NfcSelectionSurfaceBrush" : "Nvt.List.TransparentBrush", "NfcTextDisabledBrush");
        if (state.Selected) return (state.Pressed ? "Nvt.List.SelectedPressedBrush"
            : state.Hover ? "Nvt.List.SelectedPointerOverBrush" : "Nvt.List.SelectedBrush", "Nvt.List.SelectedLabelBrush");
        return (state.Pressed ? "NfcSecondaryActionPressedBrush"
            : state.Hover ? "NfcSelectionSurfaceBrush" : "Nvt.List.TransparentBrush", "NfcTextBrush");
    }

    internal static void Key(Window host, global::Avalonia.Input.Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(host, key, modifiers, PhysicalKey.None, null);
        global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(host, key, modifiers, PhysicalKey.None, null);
        Flush(host);
    }
}
