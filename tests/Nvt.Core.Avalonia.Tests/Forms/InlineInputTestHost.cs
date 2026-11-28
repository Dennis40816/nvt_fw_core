// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;
using Nvt.Core.Avalonia.Tests.Choice;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Creates real Core controls with the optional property-panel styles.</summary>
internal static class InlineInputTestHost
{
    internal static readonly string[] Kinds = ["textbox", "combobox", "editable", "numericupdown", "numericLeft"];

    internal static Window Create(Control content, bool dark = false, double width = 600, double height = 400)
    {
        Window host = FormsTestHost.Create(content, dark, width: width, height: height);
        Include(host);
        return host;
    }

    internal static void Include(Window host)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/InlineInputStyles.axaml");
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
    }

    internal static TemplatedControl Sample(string kind)
    {
        TemplatedControl control = FormsTestHost.Sample(kind switch
        {
            "editable" => "combobox",
            "numericLeft" => "numericupdown",
            _ => kind,
        }, new("Rest"));
        control.Classes.Add("inlineInput");
        control.Classes.Add("reducedMotion");
        if (kind == "editable") ((ComboBox)control).IsEditable = true;
        if (kind == "numericLeft") ((NumericUpDown)control).ButtonSpinnerLocation = Location.Left;
        return control;
    }

    internal static Control Target(TemplatedControl control) => control switch
    {
        NumericUpDown => ChoiceTestHost.Part<TextBox>(control, "PART_TextBox"),
        ComboBox { IsEditable: true } => ChoiceTestHost.Part<TextBox>(control, "PART_EditableTextBox"),
        _ => control,
    };

    internal static void State(TemplatedControl control, FormState state, bool nativeError = false, bool keyboardFocus = true)
    {
        FormsTestHost.SetState(control, state with { Error = state.Error && !nativeError });
        ((IPseudoClasses)control.Classes).Set(":error", state.Error && nativeError);
        ((IPseudoClasses)control.Classes).Set(":focus-visible", state.Focus && keyboardFocus);
        ((IPseudoClasses)control.Classes).Set(":focus", state.Focus);
        ((IPseudoClasses)control.Classes).Set(":focus-within", state.Focus);
        if (Target(control) is TextBox editor && editor != control)
        {
            ((IPseudoClasses)editor.Classes).Set(":focus-visible", state.Focus && keyboardFocus);
            ((IPseudoClasses)editor.Classes).Set(":focus", state.Focus);
            ((IPseudoClasses)editor.Classes).Set(":pointerover", state.Hover);
        }
    }
}
