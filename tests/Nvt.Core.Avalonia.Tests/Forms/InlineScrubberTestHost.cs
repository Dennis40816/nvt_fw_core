// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Nvt.Core.Avalonia.Inputs;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Loads all four style layers and drives independent scrubber visual states.</summary>
internal static class InlineScrubberTestHost
{
    internal static NumberScrubber Sample() => new() { Value = 4.125m, SnapToStep = false, Classes = { "inlineInput" } };

    internal static Window Create(Control content, bool dark = false, bool inputsLast = false, bool inline = true)
    {
        Window host = FormsTestHost.Create(content, dark);
        if (inline && inputsLast) InlineInputTestHost.Include(host);
        IncludeInputs(host);
        if (inline && !inputsLast) InlineInputTestHost.Include(host);
        return host;
    }

    internal static void IncludeInputs(Window host)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Inputs/InputsStyles.axaml");
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
    }

    internal static Border Body(NumberScrubber control) => Part<Border>(control, "RootBorder");
    internal static Border Ring(NumberScrubber control) => Part<Border>(control, "InlineFocusRing");
    internal static TextBox Editor(NumberScrubber control) => Part<TextBox>(control, "InputBox");

    internal static void State(NumberScrubber control, FormState state, bool keyboard = false, bool drag = false, int errorSource = 0)
    {
        TextBox editor = Editor(control);
        FormsTestHost.SetState(control, state with { Error = state.Error && errorSource == 0 });
        control.IsReadOnly = state.ReadOnly;
        ((IPseudoClasses)control.Classes).Set(":error", state.Error && errorSource == 1);
        DataValidationErrors.SetError(control, state.Error && errorSource == 2 ? new InvalidOperationException("Outer error") : null);
        DataValidationErrors.SetError(editor, state.Error && errorSource == 3 ? new InvalidOperationException("Editor error") : null);
        Body(control).Classes.Set("focused", state.Focus || drag);
        Body(control).Classes.Set("scrubbing", drag);
        ((IPseudoClasses)editor.Classes).Set(":focus", state.Focus);
        ((IPseudoClasses)editor.Classes).Set(":focus-visible", state.Focus && keyboard);
        ((IPseudoClasses)editor.Classes).Set(":pointerover", state.Hover);
    }
}
