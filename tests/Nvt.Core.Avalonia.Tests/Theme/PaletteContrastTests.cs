// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Checks the approved sRGB contrast floors against compiled Core tokens and all three accents.</summary>
public sealed class PaletteContrastTests
{
    private static readonly string[] NeutralSurfaces =
    [
        "NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush",
        "NfcSelectionSurfaceBrush", "NfcSecondaryActionPressedBrush",
    ];

    private static readonly string[] AccentSurfaces = ["NfcAccentSurfaceBrush", "NfcAccentSurfaceSubtleBrush"];
    private static readonly string[] BodyText = ["NfcTextStrongBrush", "NfcTextBrush", "NfcTextSecondaryBrush"];
    private static readonly string[] FocusRings = ["Nvt.Focus.RingBrush", "Nvt.Focus.DangerRingBrush"];
    private static readonly string[] AccentText = ["NfcAccentBrush", "NfcAccentStrongBrush"];
    private static readonly string[] SemanticText =
    [
        "NfcDangerTextBrush", "NfcWarningTextBrush", "NfcSuccessTextBrush", "NfcInfoTextBrush",
    ];

    /// <summary>Ports every informational, disabled, border, ring, semantic and accent floor without exceptions.</summary>
    [AvaloniaTheory]
    [InlineData("NFC", false, "#1557E9", "#1148BE", "#EFF3FD", "#F7F9FE")]
    [InlineData("NFC", true, "#5FA5FA", "#8FBFFB", "#1A2940", "#162034")]
    [InlineData("NFH", false, "#2967A9", "#225489", "#F0F4F9", "#F8FAFC")]
    [InlineData("NFH", true, "#53A6FF", "#86C0FF", "#192941", "#152134")]
    [InlineData("NFU", false, "#4A6F00", "#3D5B00", "#F2F5ED", "#F9FAF6")]
    [InlineData("NFU", true, "#82B11A", "#97CD1E", "#1F2A25", "#182126")]
    public void AllToolAccentsMeetSharedContrastFloors(string tool, bool dark, string accent,
        string strong, string surface, string subtle)
    {
        var host = new Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            XElement tokens = ThemeContractTests.ReadExtracted("ThemeTokens").Root!;
            XElement theme = Assert.Single(tokens.Descendants(ThemeContractTests.Presentation + "ResourceDictionary"),
                element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == (dark ? "Dark" : "Light"));
            var palette = theme.Elements().Where(element => element.Name.LocalName == "SolidColorBrush")
                .ToDictionary(element => element.Attribute(ThemeContractTests.Xaml + "Key")!.Value,
                    element => ReadColor(host, element.Attribute(ThemeContractTests.Xaml + "Key")!.Value), StringComparer.Ordinal);
            var overrides = new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                ["NfcAccentBrush"] = Color.Parse(accent),
                ["NfcAccentStrongBrush"] = Color.Parse(strong),
                ["NfcAccentSurfaceBrush"] = Color.Parse(surface),
                ["NfcAccentSurfaceSubtleBrush"] = Color.Parse(subtle),
                ["NfcAccentBorderBrush"] = Color.Parse(accent),
                ["NfcAccentBorderStrongBrush"] = Color.Parse(strong),
                ["NfcAccentBorderLightBrush"] = Color.Parse(accent),
            };
            foreach ((string key, Color value) in overrides)
            {
                if (tool == "NFC") Assert.Equal(value, palette[key]);
                palette[key] = value;
            }

            string[] commonSurfaces = [.. palette.Keys.Where(key => !key.StartsWith("NfcAccent", StringComparison.Ordinal) &&
                (key.Contains("Surface", StringComparison.Ordinal) || key is "NfcAppBackgroundBrush" or "NfcSecondaryActionPressedBrush"))];
            foreach (string background in commonSurfaces.Concat(AccentSurfaces))
            {
                foreach (string foreground in BodyText) Check("body/secondary", foreground, background, 4.5);
                Check("muted/placeholder", "NfcTextMutedBrush", background, 4.5);
                Check("disabled", "NfcTextDisabledBrush", background, 3);
                Check("input border", "NfcBorderBrush", background, 3);
                foreach (string ring in FocusRings) Check("focus ring", ring, background, 3);
            }

            foreach (string foreground in AccentText)
            {
                foreach (string background in NeutralSurfaces.Concat(AccentSurfaces))
                    Check("accent text", foreground, background, 4.5);
                Check("filled label", dark ? "NfcAppBackgroundBrush" : "NfcSurfaceBrush", foreground, 4.5);
            }

            foreach (string background in NeutralSurfaces)
            {
                foreach (string foreground in SemanticText) Check("semantic text", foreground, background, 4.5);
                Check("scroll thumb", "NfcTextDisabledBrush", background, 3);
                Check("scroll thumb hover", "NfcTextMutedBrush", background, 3);
            }

            // Semantic family markers carry information and use the same 4.5:1 floor.
            foreach (string foreground in palette.Keys.Where(key => key.StartsWith("NfcSuccess", StringComparison.Ordinal) &&
                (key.Contains("Text", StringComparison.Ordinal) || key.Contains("Accent", StringComparison.Ordinal) || key.Contains("Emphasis", StringComparison.Ordinal))))
                Check("semantic text", foreground, "NfcSuccessSurfaceBrush", 4.5);
            Check("semantic text", "NfcInfoTextBrush", "NfcInfoSurfaceStrongBrush", 4.5);
            foreach (string foreground in palette.Keys.Where(key => key.StartsWith("NfcWarning", StringComparison.Ordinal) &&
                (key.Contains("Text", StringComparison.Ordinal) || key.Contains("Accent", StringComparison.Ordinal))))
            {
                Check("semantic text", foreground, "NfcWarningSurfaceBrush", 4.5);
                Check("semantic text", foreground, "NfcCautionSurfaceBrush", 4.5);
            }
            Check("semantic text", "NfcCautionTextBrush", "NfcCautionSurfaceBrush", 4.5);
            foreach (string background in commonSurfaces.Where(key => key.StartsWith("NfcDanger", StringComparison.Ordinal) || key == "NfcCriticalSurfaceBrush"))
            {
                Check("semantic text", "NfcDangerTextBrush", background, 4.5);
                Check("semantic text", "NfcDangerTextStrongBrush", background, 4.5);
            }

            void Check(string group, string foreground, string background, double floor)
            {
                double ratio = Contrast(palette[foreground], palette[background]);
                Assert.True(ratio + 1e-12 >= floor,
                    $"{tool} {(dark ? "Dark" : "Light")} {group}: {foreground}/{background} = {ratio:F6}:1, floor {floor}:1");
            }
        }
        finally { host.Close(); }
    }

    private static Color ReadColor(Control host, string key)
    {
        Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out object? value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
