// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Pins the filled-track contrast that WCAG 1.4.11 asks for: 3:1 against the page, and the 4.5:1 knob and label floor.</summary>
public sealed class SwitchTrackContrastTests(ITestOutputHelper output)
{
    private static readonly string[] _pages = ["NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush"];

    /// <summary>A checked switch track and a danger fill stay visible against every page surface and keep a white knob or label readable.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilledTracksSeparateFromThePageAndKeepWhiteReadable(bool dark)
    {
        var content = new Border();
        Window host = Create(content, dark);
        try
        {
            Show(host);
            Color white = ResourceColor(content, "Nvt.Toggle.SelectedLabelBrush");
            foreach (string key in new[] { "SwitchOnBrush", "SwitchPointerOverBrush", "SwitchPressedBrush", "DangerFillBrush", "DangerFillPointerOverBrush", "DangerFillPressedBrush" })
            {
                Color fill = ResourceColor(content, "Nvt.Toggle." + key);
                double label = ToggleStylesTests.Contrast(white, fill);
                Assert.True(label >= 4.5, $"{dark}/{key}: white on fill {label:F3}:1");
                foreach (string page in _pages)
                {
                    double edge = ToggleStylesTests.Contrast(fill, ResourceColor(content, page));
                    Assert.True(edge >= 3, $"{dark}/{key} against {page}: {edge:F3}:1");
                    output.WriteLine($"{(dark ? "Dark" : "Light")} {key} / {page}: {edge:F3}:1, white {label:F3}:1");
                }
            }
        }
        finally { host.Close(); }
    }

    /// <summary>The border that bounds an input field on a plain page is the strong border token, not the muted divider token.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldBorderSeparatesFromThePage(bool dark)
    {
        var content = new Border();
        Window host = Create(content, dark);
        try
        {
            Show(host);
            foreach (string page in _pages)
            {
                double ratio = ToggleStylesTests.Contrast(ResourceColor(content, "NfcBorderBrush"), ResourceColor(content, page));
                Assert.True(ratio >= 3, $"{dark}: NfcBorderBrush against {page}: {ratio:F3}:1");
            }
        }
        finally { host.Close(); }
    }
}
