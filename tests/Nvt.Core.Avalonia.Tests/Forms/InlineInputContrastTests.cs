// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Nvt.Core.Avalonia.Tests.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Measures composited inline text, fills, frames and rings on both panel surfaces.</summary>
public sealed class InlineInputContrastTests(ITestOutputHelper output)
{
    /// <summary>Enforces text and graphic floors while recording the deliberately low rest-fill contrast.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineContrastRowsMeetTheirFloors(bool dark)
    {
        Window host = InlineInputTestHost.Create(new TextBlock(), dark);
        try
        {
            foreach (string parent in new[] { "NfcSurfaceBrush", "NfcSurfaceSubtleBrush" })
            {
                Color surface = ShownColor.Over(Brush(host, parent), ResourceColor(host, "NfcAppBackgroundBrush"));
                foreach (string fill in new[] { "FillBrush", "HoverFillBrush", "EditingFillBrush", "ReadOnlyFillBrush", "DisabledFillBrush" })
                {
                    Color background = ShownColor.Over(Brush(host, "Nvt.Input.Inline." + fill), surface);
                    double fillRatio = ShownColor.Contrast(background, surface);
                    output.WriteLine($"{(dark ? "Dark" : "Light")} {fill} / {parent}: {fillRatio:F3}:1");
                    if (fill == "HoverFillBrush" && parent == "NfcSurfaceBrush") Assert.True(fillRatio >= (dark ? 1.25 : 1.2));
                    string text = fill == "DisabledFillBrush" ? "DisabledTextBrush" : fill == "ReadOnlyFillBrush" ? "ReadOnlyTextBrush" : "TextBrush";
                    Measure(host, text, background, fill == "DisabledFillBrush" ? 3 : 4.5, $"{fill} over {parent}");
                    Measure(host, "SecondaryTextBrush", background, 4.5, $"{fill} over {parent}");
                    Measure(host, "ErrorBorderBrush", background, 3, $"{fill} over {parent}");
                    if (fill == "EditingFillBrush") Measure(host, "EditingBorderBrush", background, 3, parent);
                }
                Measure(host, "FocusBrush", surface, 3, parent);
            }
        }
        finally { host.Close(); }
    }

    private void Measure(Control owner, string token, Color background, double floor, string context)
    {
        double ratio = ShownColor.Contrast(ShownColor.Over(Brush(owner, "Nvt.Input.Inline." + token), background), background);
        output.WriteLine($"{owner.ActualThemeVariant} {token} on {context}: {ratio:F3}:1");
        Assert.True(ratio >= floor, $"{token} on {context}: {ratio:F3}:1 < {floor}:1");
    }

    private static IBrush Brush(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out object? value), key);
        return Assert.IsAssignableFrom<IBrush>(value);
    }
}
