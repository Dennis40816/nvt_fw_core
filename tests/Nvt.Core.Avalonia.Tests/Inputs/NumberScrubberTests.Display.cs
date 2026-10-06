// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Inputs;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Inputs;

/// <summary>Ports generic tooltip coverage and pins mixed, read-only and styled display states.</summary>
public sealed partial class NumberScrubberTests
{
    /// <summary>The new hint is opt-in, updates the plain drag border and treats only null or empty as absent.</summary>
    [AvaloniaFact]
    public void ScrubHintIsOptionalAndDragAreaHasNoGlyph()
    {
        var control = new NumberScrubber();
        Border area = Area(control);
        Assert.Null(ToolTip.GetTip(area));
        Assert.Null(area.Child);
        control.ScrubHint = "Synthetic drag hint";
        Assert.Equal("Synthetic drag hint", ToolTip.GetTip(area));
        control.ScrubHint = "";
        Assert.Null(ToolTip.GetTip(area));
        control.ScrubHint = " ";
        Assert.Equal(" ", ToolTip.GetTip(area));
        control.ScrubHint = null;
        Assert.Null(ToolTip.GetTip(area));
    }

    /// <summary>Ports the whole-control tooltip assertions from NFH's target-cap smoke test with synthetic text.</summary>
    [AvaloniaFact]
    public void WholeControlAndDragAreaTooltipsKeepSeparateContent()
    {
        var control = new NumberScrubber { Value = 128m, Maximum = 255m, ScrubHint = "Synthetic drag hint" };
        var tip = new TextBlock { Text = "Synthetic value hint" };
        ToolTip.SetTip(control, tip);
        Window window = Host(control);
        try
        {
            window.Show();
            Drain();
            Assert.Equal("Synthetic value hint", Assert.IsType<TextBlock>(ToolTip.GetTip(control)).Text);
            Assert.Equal("Synthetic drag hint", ToolTip.GetTip(Area(control)));
            ToolTip.SetIsOpen(control, true);
            Drain();
            Assert.True(ToolTip.GetIsOpen(control));
            ToolTip.SetIsOpen(control, false);
            ToolTip.SetIsOpen(Area(control), true);
            Drain();
            Assert.True(ToolTip.GetIsOpen(Area(control)));
        }
        finally
        {
            ToolTip.SetIsOpen(control, false);
            ToolTip.SetIsOpen(Area(control), false);
            window.Close();
        }
    }

    /// <summary>Mixed input clears on focus, updates Value live and keeps the mixed flag through commit and Escape.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedAndReadOnlyDisplaysKeepSourceStates(bool readOnly)
    {
        var control = new NumberScrubber { Value = 7m, IsMixed = true, IsReadOnly = readOnly };
        TextBox input = Input(control);
        Assert.Equal("*", input.Text);
        Assert.Equal(readOnly, input.IsReadOnly);
        Assert.Equal(!readOnly, Area(control).IsHitTestVisible);
        Assert.True(control.IsEnabled);
        Assert.True(input.IsEnabled);
        FocusInput(input);
        Drain();
        Assert.Equal("", input.Text);
        Assert.Equal(7m, control.Value);
        // A programmatic Text change still invokes the live handler when read-only, as in NFH.
        Type(input, "2.5");
        Assert.Equal(3m, control.Value);
        Assert.Equal("2.5", input.Text);
        Press(input, Key.Enter);
        Drain();
        Assert.Equal("*", input.Text);
        Assert.Equal(3m, control.Value);
        Assert.True(control.IsMixed);
        Type(input, "4");
        Press(input, Key.Escape);
        Drain();
        Assert.Equal("*", input.Text);
        Assert.Equal(4m, control.Value);
        input.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Drain();
        Assert.Equal("*", input.Text);
        control.IsMixed = false;
        Drain();
        Assert.Equal("4", input.Text);
        control.IsReadOnly = !readOnly;
        Assert.Equal(!readOnly, input.IsReadOnly);
        Assert.Equal(readOnly, Area(control).IsHitTestVisible);
    }

    /// <summary>Compiled base styles use the existing light and dark tokens and keep the source geometry.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StylesKeepGeometryAndResolveExistingThemeTokens(bool dark)
    {
        var control = new NumberScrubber();
        Window window = Host(control);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            window.Show();
            Drain();
            Assert.Equal(30d, control.Height);
            Assert.Equal(30d, control.MinHeight);
            Assert.Equal(new CornerRadius(10), Root(control).CornerRadius);
            Assert.Equal(new Thickness(1), Root(control).BorderThickness);
            Assert.True(Root(control).ClipToBounds);
            Assert.Equal(6d, Area(control).Width);
            Assert.Equal(new CornerRadius(0, 10, 10, 0), Area(control).CornerRadius);
            Assert.Equal(new Thickness(1, 0, 0, 0), Area(control).BorderThickness);
            Assert.Null(Area(control).Child);
            Assert.Equal(new Thickness(6, 2), Input(control).Padding);
            Assert.Equal(new Thickness(0), Input(control).BorderThickness);
            Assert.Null(Input(control).FocusAdorner);
            Assert.Equal(new CornerRadius(0), Input(control).CornerRadius);
            Root(control).Classes.Remove("focused");
            AssertBrush(Root(control).Background, "NfcSurfaceBrush", dark);
            AssertBrush(Root(control).BorderBrush, "NfcBorderMutedBrush", dark);
            AssertBrush(Area(control).Background, "NfcBorderBrush", dark);
            AssertBrush(Area(control).BorderBrush, "NfcBorderMutedBrush", dark);
            Assert.True(Input(control).Focus());
            FocusInput(Input(control));
            AssertBrush(Root(control).Background, "NfcSelectionSurfaceBrush", dark);
            AssertBrush(Root(control).BorderBrush, "NfcAccentBrush", dark);
            Assert.Equal(new Thickness(0), Input(control).BorderThickness);
            Assert.Null(Input(control).FocusAdorner);
        }
        finally { window.Close(); }
    }

    private static void AssertBrush(IBrush? actual, string key, bool dark)
    {
        Assert.True(Application.Current!.TryGetResource(key, dark ? ThemeVariant.Dark : ThemeVariant.Light,
            out object? expected));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }
}
