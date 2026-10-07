// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Toggle;

internal readonly record struct ToggleState(string Name, bool Checked = false, bool Hover = false,
    bool Pressed = false, bool Disabled = false, bool Focus = false);

internal static class ToggleTestHost
{
    internal static readonly string[] Roles = ["toggleSegment", "toggleTab", "toggleIcon"];
    internal static readonly ToggleState[] States =
    [
        new("Rest"), new("Pointer over", Hover: true), new("Pressed", Hover: true, Pressed: true),
        new("Checked", Checked: true), new("Checked + over", Checked: true, Hover: true),
        new("Checked + press", Checked: true, Hover: true, Pressed: true),
        new("Disabled", Disabled: true), new("Disabled + checked", Checked: true, Disabled: true),
        new("Keyboard focus", Focus: true),
    ];

    // The seven accent overrides follow the shared palette's adoption contract.
    private static readonly string[] AccentKeys =
    [
        "NfcAccentBrush", "NfcAccentStrongBrush", "NfcAccentSurfaceBrush", "NfcAccentSurfaceSubtleBrush",
        "NfcAccentBorderBrush", "NfcAccentBorderStrongBrush", "NfcAccentBorderLightBrush",
    ];

    private static readonly string[][] Accents =
    [
        ["#1557E9", "#1148BE", "#EFF3FD", "#F7F9FE", "#1557E9", "#1148BE", "#1557E9"],
        ["#5FA5FA", "#8FBFFB", "#1A2940", "#162034", "#5FA5FA", "#8FBFFB", "#5FA5FA"],
        ["#2967A9", "#225489", "#F0F4F9", "#F8FAFC", "#2967A9", "#225489", "#2967A9"],
        ["#53A6FF", "#86C0FF", "#192941", "#152134", "#53A6FF", "#86C0FF", "#53A6FF"],
        ["#4A6F00", "#3D5B00", "#F2F5ED", "#F9FAF6", "#4A6F00", "#3D5B00", "#4A6F00"],
        ["#82B11A", "#97CD1E", "#1F2A25", "#182126", "#82B11A", "#97CD1E", "#82B11A"],
    ];

    internal static Window Create(Control content, bool dark, int tool = 0, bool proposal = true,
        double width = 500, double height = 240)
    {
        var host = new Window
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Width = width, Height = height, Content = content,
            FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"), FontSize = 13,
        };
        host.Styles.Add(new FluentTheme());
        if (proposal) Include(host, "avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml");
        for (int variant = 0; variant < 2; variant++)
        {
            var dictionary = new ResourceDictionary();
            for (int key = 0; key < AccentKeys.Length; key++)
                dictionary[AccentKeys[key]] = new SolidColorBrush(Color.Parse(Accents[tool * 2 + variant][key]));
            host.Resources.ThemeDictionaries[variant == 0 ? ThemeVariant.Light : ThemeVariant.Dark] = dictionary;
        }
        host.Bind(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("NfcAppBackgroundBrush"));
        return host;
    }

    internal static void Include(Window host, string path)
    {
        var uri = new Uri(path);
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
    }

    internal static void Show(Window host)
    {
        host.Show();
        Flush(host);
    }

    internal static void Flush(Window host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }

    internal static StateToggleButton Button(string role, string label = "Option")
    {
        var button = new StateToggleButton
        {
            Content = label, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add(role);
        return button;
    }

    internal static Border Group(params StateToggleButton[] buttons)
    {
        var panel = new StackPanel();
        foreach (StateToggleButton button in buttons) panel.Children.Add(button);
        var group = new Border { Child = panel, HorizontalAlignment = HorizontalAlignment.Center };
        group.Classes.Add("toggleSegmentGroup");
        return group;
    }

    internal static Border Part(ToggleButton button, string name) =>
        Assert.Single(button.GetVisualDescendants().OfType<Border>(), part => part.Name == name);

    internal static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    internal static Color ResourceColor(Control owner, string key)
    {
        if (key == "Transparent") return Colors.Transparent;
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out object? resource), key);
        return ColorOf(Assert.IsAssignableFrom<IBrush>(resource));
    }

    internal sealed class StateToggleButton : ToggleButton
    {
        protected override Type StyleKeyOverride => typeof(ToggleButton);

        internal void SetState(ToggleState state)
        {
            IsEnabled = !state.Disabled;
            IsChecked = state.Checked;
            PseudoClasses.Set(":pointerover", state.Hover);
            PseudoClasses.Set(":pressed", state.Pressed);
            PseudoClasses.Set(":focus-visible", state.Focus);
        }
    }
}
