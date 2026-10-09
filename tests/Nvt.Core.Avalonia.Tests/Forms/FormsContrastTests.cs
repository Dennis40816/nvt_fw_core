// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Measures every documented form, tab, and text color pair from compiled resources.</summary>
public sealed class FormsContrastTests(ITestOutputHelper output)
{
    private static readonly (string Name, string Foreground, string Background, double Minimum)[] Pairs =
    [
        ("Form text, caret and spinner glyph / rest / read-only", "Nvt.Form.TextBrush", "Nvt.Form.FillBrush", 4.5),
        ("Form placeholder / rest / read-only", "Nvt.Form.PlaceholderBrush", "Nvt.Form.FillBrush", 4.5),
        ("Form border / rest / read-only", "Nvt.Form.BorderBrush", "Nvt.Form.FillBrush", 3),
        ("Form error border / rest / read-only", "Nvt.Form.ErrorBorderBrush", "Nvt.Form.FillBrush", 3),
        ("Form focus / rest / read-only", "Nvt.Form.FocusBrush", "Nvt.Form.FillBrush", 3),
        ("Form text, caret and spinner glyph / hover", "Nvt.Form.TextBrush", "Nvt.Form.HoverFillBrush", 4.5),
        ("Form placeholder / hover", "Nvt.Form.PlaceholderBrush", "Nvt.Form.HoverFillBrush", 4.5),
        ("Form border / hover", "Nvt.Form.HoverBorderBrush", "Nvt.Form.HoverFillBrush", 3),
        ("Form error border / hover", "Nvt.Form.ErrorBorderBrush", "Nvt.Form.HoverFillBrush", 3),
        ("Form focus / hover", "Nvt.Form.FocusBrush", "Nvt.Form.HoverFillBrush", 3),
        ("Form text, caret and spinner glyph / pressed / open", "Nvt.Form.TextBrush", "Nvt.Form.PressedFillBrush", 4.5),
        ("Form placeholder / pressed / open", "Nvt.Form.PlaceholderBrush", "Nvt.Form.PressedFillBrush", 4.5),
        ("Form border / pressed / open", "Nvt.Form.PressedBorderBrush", "Nvt.Form.PressedFillBrush", 3),
        ("Form error border / pressed / open", "Nvt.Form.ErrorBorderBrush", "Nvt.Form.PressedFillBrush", 3),
        ("Form focus / pressed / open", "Nvt.Form.FocusBrush", "Nvt.Form.PressedFillBrush", 3),
        ("Form disabled text, placeholder, border and spinner glyph", "Nvt.Form.DisabledTextBrush", "Nvt.Form.DisabledFillBrush", 3),
        ("Form error border / disabled fill", "Nvt.Form.ErrorBorderBrush", "Nvt.Form.DisabledFillBrush", 3),
        ("Form text selection", "Nvt.Form.SelectionTextBrush", "Nvt.Form.SelectionBrush", 4.5),
        ("Form and tab focus / application", "Nvt.Form.FocusBrush", "NfcAppBackgroundBrush", 3),
        ("Form rest border / application", "Nvt.Form.BorderBrush", "NfcAppBackgroundBrush", 3),
        ("Form hover border / application", "Nvt.Form.HoverBorderBrush", "NfcAppBackgroundBrush", 3),
        ("Form pressed border / application", "Nvt.Form.PressedBorderBrush", "NfcAppBackgroundBrush", 3),
        ("Form error border / application", "Nvt.Form.ErrorBorderBrush", "NfcAppBackgroundBrush", 3),
        ("Form disabled border / application", "Nvt.Form.DisabledTextBrush", "NfcAppBackgroundBrush", 3),
        ("Form and tab focus / surface", "Nvt.Form.FocusBrush", "NfcSurfaceBrush", 3),
        ("Form rest border / surface", "Nvt.Form.BorderBrush", "NfcSurfaceBrush", 3),
        ("Form hover border / surface", "Nvt.Form.HoverBorderBrush", "NfcSurfaceBrush", 3),
        ("Form pressed border / surface", "Nvt.Form.PressedBorderBrush", "NfcSurfaceBrush", 3),
        ("Form error border / surface", "Nvt.Form.ErrorBorderBrush", "NfcSurfaceBrush", 3),
        ("Form disabled border / surface", "Nvt.Form.DisabledTextBrush", "NfcSurfaceBrush", 3),
        ("Tab text / rest", "Nvt.Tab.TextBrush", "Nvt.Tab.StripBrush", 4.5),
        ("Tab focus / rest", "Nvt.Tab.FocusBrush", "Nvt.Tab.StripBrush", 3),
        ("Tab text / hover", "Nvt.Tab.HoverTextBrush", "Nvt.Tab.HoverFillBrush", 4.5),
        ("Tab focus / hover", "Nvt.Tab.FocusBrush", "Nvt.Tab.HoverFillBrush", 3),
        ("Tab text / pressed", "Nvt.Tab.PressedTextBrush", "Nvt.Tab.PressedFillBrush", 4.5),
        ("Tab focus / pressed", "Nvt.Tab.FocusBrush", "Nvt.Tab.PressedFillBrush", 3),
        ("Tab text and indicator / selected", "Nvt.Tab.SelectedTextBrush", "Nvt.Tab.SelectedFillBrush", 4.5),
        ("Tab focus / selected", "Nvt.Tab.FocusBrush", "Nvt.Tab.SelectedFillBrush", 3),
        ("Tab text and indicator / selected hover", "Nvt.Tab.SelectedTextBrush", "Nvt.Tab.SelectedHoverFillBrush", 4.5),
        ("Tab focus / selected hover", "Nvt.Tab.FocusBrush", "Nvt.Tab.SelectedHoverFillBrush", 3),
        ("Tab text and indicator / selected pressed", "Nvt.Tab.SelectedTextBrush", "Nvt.Tab.SelectedPressedFillBrush", 4.5),
        ("Tab focus / selected pressed", "Nvt.Tab.FocusBrush", "Nvt.Tab.SelectedPressedFillBrush", 3),
        ("Tab text / disabled", "Nvt.Tab.DisabledTextBrush", "Nvt.Tab.DisabledFillBrush", 3),
        ("Tab text and indicator / selected disabled", "Nvt.Tab.DisabledTextBrush", "Nvt.Tab.DisabledSelectedFillBrush", 3),
        ("All text classes / application", "NfcTextBrush", "NfcAppBackgroundBrush", 4.5),
        ("Muted class / application", "NfcTextMutedBrush", "NfcAppBackgroundBrush", 4.5),
        ("All text classes / surface", "NfcTextBrush", "NfcSurfaceBrush", 4.5),
        ("Muted class / surface", "NfcTextMutedBrush", "NfcSurfaceBrush", 4.5),
        ("All text classes / subtle", "NfcTextBrush", "NfcSurfaceSubtleBrush", 4.5),
        ("Muted class / subtle", "NfcTextMutedBrush", "NfcSurfaceSubtleBrush", 4.5),
        ("All text classes / hover", "NfcTextBrush", "NfcSelectionSurfaceBrush", 4.5),
        ("Muted class / hover", "NfcTextMutedBrush", "NfcSelectionSurfaceBrush", 4.5),
        ("All text classes / pressed", "NfcTextBrush", "NfcSecondaryActionPressedBrush", 4.5),
        ("Muted class / pressed", "NfcTextMutedBrush", "NfcSecondaryActionPressedBrush", 4.5),
        ("All text classes / selected", "NfcTextBrush", "Nvt.Controls.SelectedBrush", 4.5),
        ("Muted class / selected", "NfcTextMutedBrush", "Nvt.Controls.SelectedBrush", 4.5),
        ("All text classes / selected hover", "NfcTextBrush", "Nvt.Controls.SelectedPointerOverBrush", 4.5),
        ("Muted class / selected hover", "NfcTextMutedBrush", "Nvt.Controls.SelectedPointerOverBrush", 4.5),
        ("All text classes / selected pressed", "NfcTextBrush", "Nvt.Controls.SelectedPressedBrush", 4.5),
        ("Muted class / selected pressed", "NfcTextMutedBrush", "Nvt.Controls.SelectedPressedBrush", 4.5),
    ];

    /// <summary>Preserves enabled text at 4.5:1 and disabled text, borders, indicators, and focus at 3:1.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AllDocumentedPairsMeetContrastFloors(bool dark, bool square)
    {
        Window host = FormsTestHost.Create(new TextBlock { Text = "Contrast" }, dark);
        try
        {
            global::Nvt.Core.Avalonia.Theme.ThemeShapes.SetShape(host.Resources, square
                ? global::Nvt.Core.Avalonia.Theme.ThemeShape.Square : global::Nvt.Core.Avalonia.Theme.ThemeShape.Pill);
            foreach (var pair in Pairs)
            {
                double ratio = Contrast(ResourceColor(host, pair.Foreground), ResourceColor(host, pair.Background));
                Assert.True(ratio >= pair.Minimum, $"{pair.Name}: {ratio:F3}:1 < {pair.Minimum}:1");
                output.WriteLine($"{(dark ? "Dark" : "Light")} {(square ? "Square" : "Pill")} / {pair.Name}: {ratio:F3}:1");
            }
        }
        finally { host.Close(); }
    }
}
