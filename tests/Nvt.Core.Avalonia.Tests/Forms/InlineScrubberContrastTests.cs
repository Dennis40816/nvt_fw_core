// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Nvt.Core.Avalonia.Inputs;
using Nvt.Core.Avalonia.Tests.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;
using static Nvt.Core.Avalonia.Tests.Forms.InlineScrubberTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Measures the actual scrubber body, editor text and exterior ring over both panel surfaces.</summary>
public sealed class InlineScrubberContrastTests(ITestOutputHelper output)
{
    /// <summary>Text meets 4.5:1, disabled text 3:1, and visible frames and rings 3:1 after compositing.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaintedContrastRows(bool dark)
    {
        NumberScrubber control = Sample();
        var panel = new Border { Padding = new global::Avalonia.Thickness(24), Child = control };
        Window host = InlineScrubberTestHost.Create(panel, dark);
        try
        {
            FormsTestHost.Show(host);
            foreach (string parent in new[] { "NfcSurfaceBrush", "NfcSurfaceSubtleBrush" })
            {
                panel.Bind(Border.BackgroundProperty, new DynamicResourceExtension(parent));
                Flush(host);
                Color surface = ShownColor.Over(panel.Background!, ResourceColor(host, "NfcAppBackgroundBrush"));
                foreach (FormState state in new FormState[]
                {
                    new("Rest"), new("Hover", Hover: true), new("Editing", Focus: true),
                    new("Read-only", ReadOnly: true), new("Disabled", Disabled: true),
                })
                {
                    State(control, state, keyboard: true);
                    Flush(host);
                    Color fill = ShownColor.Over(Body(control).Background!, surface);
                    Measure(dark, parent, state.Name + " fill", fill, surface, 1);
                    Measure(dark, parent, state.Name + " text", ShownColor.Over(Editor(control).Foreground!, fill), fill, state.Disabled ? 3 : 4.5);
                    Border grip = Part<Border>(control, "InlineGrip");
                    if (!state.ReadOnly)
                    {
                        Color shownGrip = ShownColor.Over(grip.Background!, fill, ShownColor.ElementOpacity(grip));
                        Measure(dark, parent, state.Name + " grip / fill", shownGrip, fill, 3);
                        Measure(dark, parent, state.Name + " grip / surface", shownGrip, surface, 3);
                    }
                    if (state.Focus)
                    {
                        Measure(dark, parent, "Editing frame", ShownColor.Over(Body(control).BorderBrush!, fill), fill, 3);
                        Measure(dark, parent, "Keyboard ring", ShownColor.Over(Ring(control).BorderBrush!, surface), surface, 3);
                    }
                    State(control, state with { Error = true }, keyboard: true);
                    Flush(host);
                    Measure(dark, parent, state.Name + " error frame", ShownColor.Over(Body(control).BorderBrush!, fill), fill, 3);
                    Assert.Equal(0, ColorOf(Part<Border>(control, "ScrubArea").Background).A);
                    Assert.Equal(0, ColorOf(Part<Border>(control, "FormBody").Background).A);
                }
            }
        }
        finally { host.Close(); }
    }

    private void Measure(bool dark, string parent, string paint, Color foreground, Color background, double floor)
    {
        double ratio = ShownColor.Contrast(foreground, background);
        output.WriteLine($"{(dark ? "Dark" : "Light")} {paint} / {parent}: {ratio:F3}:1");
        Assert.True(ratio >= floor, $"{paint}: {ratio:F3}:1 < {floor}:1");
    }
}
