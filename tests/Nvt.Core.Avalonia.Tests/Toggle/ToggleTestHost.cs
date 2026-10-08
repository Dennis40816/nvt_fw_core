// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Animation;
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
    internal static readonly string[] Roles = ["toggleSegment", "toggleTab", "toggleIcon", "toggleSwitch", "nativeSwitch"];
    internal static readonly ToggleState[] States =
    [
        new("Rest"), new("Pointer over", Hover: true), new("Pressed", Hover: true, Pressed: true),
        new("Checked", Checked: true), new("Checked + over", Checked: true, Hover: true),
        new("Checked + press", Checked: true, Hover: true, Pressed: true),
        new("Disabled", Disabled: true), new("Disabled + checked", Checked: true, Disabled: true),
        new("Keyboard focus", Focus: true),
    ];

    internal static Window Create(Control content, bool dark, bool styles = true,
        double width = 600, double height = 300, bool snapshot = true)
    {
        var host = new Window
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Width = width, Height = height, Content = content,
            FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"), FontSize = 13,
        };
        host.Styles.Add(new FluentTheme());
        Include(host, "avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml");
        Include(host, "avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml");
        if (styles) Include(host, "avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml");
        host.Tag = snapshot;
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
        if (host.Tag is true)
        {
            // Resolve endpoints locally in tests without adding snapshot selectors to production styles.
            foreach (ToggleSwitch native in host.GetVisualDescendants().OfType<ToggleSwitch>()) native.KnobTransitions = [];
            foreach (Animatable visual in host.GetVisualDescendants().OfType<Animatable>()) visual.Transitions = null;
            Flush(host);
        }
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

    internal static ToggleButton Sample(string role, ToggleState state, string label = "Overview")
    {
        ToggleButton button = role == "nativeSwitch" ? new StateToggleSwitch() : Button(role, label);
        button.Tag = state;
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        if (role == "toggleSwitch") button.Content = null;
        AutomationProperties.SetName(button, role is "toggleSwitch" or "nativeSwitch" ? "Enable option" : label);
        SetState(button, state);
        return button;
    }

    internal static void SetState(ToggleButton button, ToggleState state)
    {
        if (button is StateToggleButton regular) regular.SetState(state);
        else if (button is StateToggleSwitch native) native.SetState(state);
        else
        {
            button.IsEnabled = !state.Disabled;
            button.IsChecked = state.Checked;
            var pseudo = (IPseudoClasses)button.Classes;
            pseudo.Set(":pointerover", state.Hover);
            pseudo.Set(":pressed", state.Pressed);
            pseudo.Set(":focus-visible", state.Focus);
        }
    }

    internal static void Restore(Control content)
    {
        foreach (ToggleButton button in content.GetVisualDescendants().OfType<ToggleButton>())
            if (button.Tag is ToggleState state) SetState(button, state);
    }

    internal sealed class StateToggleSwitch : ToggleSwitch
    {
        protected override Type StyleKeyOverride => typeof(ToggleSwitch);

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
