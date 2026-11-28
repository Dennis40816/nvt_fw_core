// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Inputs;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;
using static Nvt.Core.Avalonia.Tests.Forms.InlineScrubberTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Checks complete scrubber state precedence and both InputsStyles include orders.</summary>
public sealed class InlineScrubberStylesTests
{
    /// <summary>Every independent state bit is resolved on real controls in both palettes and shapes.</summary>
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void StateMatrix(bool dark, bool square, bool inputsLast)
    {
        NumberScrubber control = Sample();
        Window host = InlineScrubberTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { control } }, dark, inputsLast);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            TextPresenter presenter = Part<TextPresenter>(control, "PART_TextPresenter");
            var rest = (presenter.Bounds, presenter.TranslatePoint(default, control));
            for (int bits = 0; bits < 1024; bits++)
            {
                var state = new FormState("Matrix", Hover: (bits & 1) != 0, Pressed: (bits & 2) != 0,
                    Focus: (bits & 4) != 0, Disabled: (bits & 32) != 0, ReadOnly: (bits & 64) != 0, Error: (bits & 128) != 0);
                bool drag = (bits & 8) != 0, keyboard = (bits & 16) != 0;
                State(control, state, keyboard, drag, bits >> 8);
                Flush(host);
                AssertPaint(control, state, keyboard, drag);
                Assert.Equal(rest, (presenter.Bounds, presenter.TranslatePoint(default, control)));
            }
        }
        finally { host.Close(); }
    }

    internal static void AssertPaint(NumberScrubber control, FormState state, bool keyboard, bool drag)
    {
        string fill = state.Disabled ? "DisabledFillBrush" : state.ReadOnly ? "ReadOnlyFillBrush"
            : state.Focus || drag ? "EditingFillBrush" : state.Hover ? "HoverFillBrush" : "FillBrush";
        string frame = state.Error ? "ErrorBorderBrush" : state.Disabled || state.ReadOnly ? "BorderBrush"
            : state.Focus || drag ? "EditingBorderBrush" : "BorderBrush";
        string text = state.Disabled ? "DisabledTextBrush" : state.ReadOnly ? "ReadOnlyTextBrush" : "TextBrush";
        Assert.Equal(ResourceColor(control, "Nvt.Input.Inline." + fill), ColorOf(Body(control).Background));
        Assert.Equal(ResourceColor(control, "Nvt.Input.Inline." + frame), ColorOf(Body(control).BorderBrush));
        Assert.Equal(ResourceColor(control, "Nvt.Input.Inline." + text), ColorOf(Editor(control).Foreground));
        TextPresenter presenter = Part<TextPresenter>(control, "PART_TextPresenter");
        Assert.Equal(ColorOf(Editor(control).Foreground), ColorOf(presenter.Foreground));
        Assert.Equal(1, Nvt.Core.Avalonia.Tests.Theme.ShownColor.ElementOpacity(presenter));
        Assert.Equal(new Thickness(1), Body(control).BorderThickness);
        Assert.Equal(new Thickness(8, 0), Editor(control).Padding);
        Assert.Equal(new CornerRadius(6), Body(control).CornerRadius);
        Assert.Equal(32, control.Bounds.Height);
        Assert.Equal(state.Focus && keyboard && !drag && !state.Disabled && !state.ReadOnly, Ring(control).IsVisible);
        Assert.False(Part<Border>(control, "FormFocusRing").IsVisible);
        Border inner = Part<Border>(control, "FormBody");
        Assert.Equal(0, ColorOf(inner.Background).A);
        Assert.Equal(0, ColorOf(inner.BorderBrush).A);
        Border area = Part<Border>(control, "ScrubArea");
        Assert.Equal(0, ColorOf(area.Background).A);
        Assert.Equal(0, ColorOf(area.BorderBrush).A);
        Assert.Equal(new Thickness(0), area.BorderThickness);
        Border grip = Part<Border>(control, "InlineGrip");
        Assert.Equal(!state.ReadOnly, grip.IsVisible);
        Assert.Equal(ResourceColor(control, state.Disabled ? "NfcTextDisabledBrush" : "NfcTextSecondaryBrush"), ColorOf(grip.Background));
        Assert.False(grip.IsHitTestVisible);
        Assert.Equal(6, area.Bounds.Width);
        if (!state.ReadOnly)
        {
            Assert.Equal(new Size(4, 12), grip.Bounds.Size);
            Assert.Equal(new CornerRadius(2), grip.CornerRadius);
            Assert.Equal(new Point(area.Bounds.Width - 5, (area.Bounds.Height - 12) / 2), grip.TranslatePoint(default, area));
        }
    }

    /// <summary>Opt-in style loading and class removal preserve the unclassified control's existing values.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnclassifiedScrubberKeepsItsValues(bool dark)
    {
        NumberScrubber control = Sample();
        control.Classes.Remove("inlineInput");
        Window host = InlineScrubberTestHost.Create(new StackPanel { Children = { control } }, dark, inline: false);
        try
        {
            FormsTestHost.Show(host);
            foreach (FormState state in FormsTestHost.States.Concat(FormsTestHost.ReadOnlyStates))
            {
                State(control, state, keyboard: true);
                Flush(host);
                var expected = Snapshot(control);
                InlineInputTestHost.Include(host);
                Flush(host);
                Assert.Equal(expected, Snapshot(control));
                control.Classes.Add("inlineInput");
                Flush(host);
                control.Classes.Remove("inlineInput");
                Flush(host);
                Assert.Equal(expected, Snapshot(control));
                host.Styles.RemoveAt(host.Styles.Count - 1);
            }
            Assert.Equal(30, control.Height);
            Assert.Equal(new CornerRadius(10), Body(control).CornerRadius);
            Assert.Equal(new Thickness(6, 2), Editor(control).Padding);
            Assert.Null(Part<Border>(control, "InlineGrip").Background);
            Assert.Null(Part<Border>(control, "InlineGrip").BorderBrush);
        }
        finally { host.Close(); }
    }

    /// <summary>InputsStyles loaded last overrides role setters, so applications must include the inline sheet last.</summary>
    [AvaloniaFact]
    public void IncludeOrderRequiresInlineAfterInputs()
    {
        NumberScrubber control = Sample();
        Window host = InlineScrubberTestHost.Create(new StackPanel { Children = { control } }, inputsLast: true);
        try
        {
            FormsTestHost.Show(host);
            Assert.Equal(32, control.Height);
            Assert.Equal(ResourceColor(control, "NfcSurfaceBrush"), ColorOf(Body(control).Background));
            Assert.Equal(new Thickness(6, 2), Editor(control).Padding);
            InlineInputTestHost.Include(host);
            Flush(host);
            AssertPaint(control, new FormState("Rest"), keyboard: false, drag: false);
        }
        finally { host.Close(); }
    }

    private static object[] Snapshot(NumberScrubber control)
        => [control.Value, control.Bounds, control.Height, control.MinHeight, Body(control).Background!,
            Body(control).BorderBrush!, Body(control).BorderThickness, Body(control).CornerRadius, Body(control).ClipToBounds,
            Editor(control).Padding, Editor(control).Foreground!, Editor(control).Template!, Editor(control).IsReadOnly,
            Part<Border>(control, "ScrubArea").Background!, Part<Border>(control, "ScrubArea").BorderBrush!,
            Part<Border>(control, "ScrubArea").Width, Part<Border>(control, "FormFocusRing").IsVisible, Ring(control).IsVisible,
            Part<Border>(control, "InlineGrip").Background!, Part<Border>(control, "InlineGrip").Bounds];
}
