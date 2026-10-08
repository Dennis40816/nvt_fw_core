// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Choice;

namespace Nvt.Core.Avalonia.Tests.Forms;

internal readonly record struct FormState(string Name, bool Hover = false, bool Pressed = false,
    bool Focus = false, bool Disabled = false, bool ReadOnly = false, bool Error = false, bool Open = false);

internal static class FormsTestHost
{
    internal static readonly FormState[] States =
    [
        new("Rest"), new("Hover", Hover: true), new("Pressed", Hover: true, Pressed: true),
        new("Keyboard focus", Focus: true), new("Disabled", Disabled: true), new("Read only", ReadOnly: true),
        new("Error", Error: true), new("Error + hover", Error: true, Hover: true),
        new("Error + focus", Error: true, Focus: true), new("Error + disabled", Error: true, Disabled: true),
    ];

    internal static Window Create(Control content, bool dark = false, bool core = true, double width = 600, double height = 400)
    {
        Window host = ChoiceTestHost.Create(content, dark, styles: false, width: width, height: height, snapshot: false);
        var fonts = new Uri("avares://Nvt.Core.Fonts/FontRoles.axaml");
        host.Resources.MergedDictionaries.Add(new ResourceInclude(fonts) { Source = fonts });
        if (core)
            foreach (string file in new[] { "FormStyles", "TabStyles", "TextStyles", "ListStyles" })
            {
                var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{file}.axaml");
                host.Styles.Add(new StyleInclude(uri) { Source = uri });
            }
        host.Foreground = new SolidColorBrush(ChoiceTestHost.ResourceColor(host, "NfcTextBrush"));
        return host;
    }

    internal static void Show(Window host)
    {
        host.Show();
        ChoiceTestHost.Flush(host);
        foreach (Animatable visual in host.GetVisualDescendants().OfType<Animatable>()) visual.Transitions = null;
        Restore(host);
        ChoiceTestHost.Flush(host);
    }

    internal static TemplatedControl Sample(string kind, FormState state)
    {
        TemplatedControl control = kind switch
        {
            "textbox" => new TextBox { Text = "Sample value", PlaceholderText = "Enter a value" },
            "numericupdown" => new NumericUpDown { Value = 42, Minimum = 0, Maximum = 100, Increment = 1 },
            "combobox" => new ComboBox { ItemsSource = new[] { "Option alpha", "Option beta", "Option gamma" }, SelectedIndex = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        control.Tag = state;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        SetState(control, state);
        return control;
    }

    internal static void SetState(TemplatedControl control, FormState state)
    {
        control.IsEnabled = !state.Disabled;
        control.Classes.Set("error", state.Error);
        control.Classes.Set("readOnly", state.ReadOnly);
        if (control is TextBox text) text.IsReadOnly = state.ReadOnly;
        if (control is NumericUpDown numeric) numeric.IsReadOnly = state.ReadOnly;
        var pseudo = (IPseudoClasses)control.Classes;
        pseudo.Set(":pointerover", state.Hover);
        pseudo.Set(":pressed", state.Pressed);
        pseudo.Set(":dropdownopen", state.Open);
        if (control is NumericUpDown && control.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c => c.Name == "PART_TextBox") is TextBox input)
        {
            ((IPseudoClasses)input.Classes).Set(":focus-visible", state.Focus);
            if (control.GetVisualDescendants().OfType<RepeatButton>().FirstOrDefault(c => c.Name == "PART_IncreaseButton") is RepeatButton increase)
            {
                ((IPseudoClasses)increase.Classes).Set(":pointerover", state.Hover);
                ((IPseudoClasses)increase.Classes).Set(":pressed", state.Pressed);
            }
        }
        else pseudo.Set(":focus-visible", state.Focus);
    }

    internal static Border Body(Control control) => control.GetVisualDescendants().OfType<Border>()
        .Single(part => part.Name == "FormBody" && part.TemplatedParent == control);

    internal static Border Ring(Control control)
    {
        Control owner = control is NumericUpDown ? ChoiceTestHost.Part<TextBox>(control, "PART_TextBox") : control;
        return control.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "FormFocusRing" && part.TemplatedParent == owner);
    }

    internal static void Restore(Control root)
    {
        foreach (TemplatedControl control in root.GetVisualDescendants().OfType<TemplatedControl>().Prepend(root as TemplatedControl).OfType<TemplatedControl>())
            if (control.Tag is FormState state) SetState(control, state);
    }
}
