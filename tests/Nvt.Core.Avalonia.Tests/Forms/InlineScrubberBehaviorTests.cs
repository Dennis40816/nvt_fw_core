// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Inputs;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;
using static Nvt.Core.Avalonia.Tests.Forms.InlineScrubberTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Exercises native input with the complete Fluent, form, inputs and inline style stack.</summary>
public sealed class InlineScrubberBehaviorTests(ITestOutputHelper output)
{
    /// <summary>Measures the step 1 TextBox ring when a pointer clicks the editor already focused by Tab.</summary>
    [AvaloniaFact]
    public void InlineTextBoxRetainsKeyboardRingAfterPointerClick()
    {
        var before = new Grid { Focusable = true, Height = 20 };
        var text = (TextBox)InlineInputTestHost.Sample("textbox");
        Window host = InlineInputTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { before, text } });
        try
        {
            FormsTestHost.Show(host);
            Assert.True(before.Focus());
            KeyStroke(host, Key.Tab);
            Flush(host);
            Assert.True(text.IsFocused);
            bool tabRing = FormsTestHost.Ring(text).IsVisible;
            Point point = text.TranslatePoint(new Point(20, 12), host)!.Value;
            host.MouseDown(point, MouseButton.Left);
            Flush(host);
            bool pressedRing = FormsTestHost.Ring(text).IsVisible;
            host.MouseUp(point, MouseButton.Left);
            Flush(host);
            bool releasedRing = FormsTestHost.Ring(text).IsVisible;
            output.WriteLine($"Step 1 TextBox ring: Tab={tabRing}, pointer down={pressedRing}, pointer up={releasedRing}; focus-visible={text.Classes.Contains(":focus-visible")}");
            Assert.True(tabRing);
            Assert.True(pressedRing);
            Assert.True(releasedRing);
        }
        finally { host.Close(); }
    }
    /// <summary>Real pointer and Tab focus keep text still and draw exactly one unclipped exterior ring.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeFocusAndValidationKeepGeometry(bool dark, bool square)
    {
        var before = new Grid { Focusable = true, Height = 20 };
        NumberScrubber control = Sample();
        Window host = InlineScrubberTestHost.Create(new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { before, control } }, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            TextPresenter presenter = Part<TextPresenter>(control, "PART_TextPresenter");
            var rest = (presenter.Bounds, presenter.TranslatePoint(default, control));
            Point point = Editor(control).TranslatePoint(new Point(20, 12), host)!.Value;
            host.MouseDown(point, MouseButton.Left);
            host.MouseUp(point, MouseButton.Left);
            Flush(host);
            Assert.True(Editor(control).IsFocused);
            AssertPaintEditing(control, ring: false);
            Assert.Equal(rest, (presenter.Bounds, presenter.TranslatePoint(default, control)));
            Assert.True(before.Focus());
            KeyStroke(host, Key.Tab);
            Assert.True(Editor(control).IsFocused);
            AssertPaintEditing(control, ring: true);
            AssertRingGeometry(control, host);
            Assert.Equal(rest, (presenter.Bounds, presenter.TranslatePoint(default, control)));
            foreach (Control owner in new Control[] { control, Editor(control) })
            {
                DataValidationErrors.SetError(owner, new InvalidOperationException("Invalid value"));
                Flush(host);
                Assert.Equal(ResourceColor(control, "NfcDangerBorderBrush"), ColorOf(Body(control).BorderBrush));
                Assert.Equal(rest, (presenter.Bounds, presenter.TranslatePoint(default, control)));
                AssertRingGeometry(control, host);
                DataValidationErrors.ClearErrors(owner);
            }
            control.IsReadOnly = true;
            Flush(host);
            Assert.False(Ring(control).IsVisible);
            control.IsReadOnly = false;
            control.IsEnabled = false;
            Flush(host);
            Assert.False(Ring(control).IsVisible);
        }
        finally { host.Close(); }
    }

    private static void AssertPaintEditing(NumberScrubber control, bool ring)
    {
        Assert.Equal(0, ColorOf(Body(control).Background).A);
        Assert.Equal(ResourceColor(control, "NfcBorderBrush"), ColorOf(Body(control).BorderBrush));
        Assert.Equal(ring, Ring(control).IsVisible);
        Assert.False(Part<Border>(control, "FormFocusRing").IsVisible);
    }

    private static void AssertRingGeometry(NumberScrubber control, Window host)
    {
        Border ring = Ring(control);
        Assert.True(ring.IsVisible);
        Assert.Equal(new Thickness(2), ring.BorderThickness);
        Assert.Equal(new Thickness(-4), ring.Margin);
        Assert.Equal(new CornerRadius(10), ring.CornerRadius);
        Assert.Equal(new Point(-4, -4), ring.TranslatePoint(default, control));
        Assert.Equal(control.Bounds.Width + 8, ring.Bounds.Width);
        Assert.Equal(control.Bounds.Height + 8, ring.Bounds.Height);
        Assert.Single(control.GetVisualDescendants().OfType<Border>(), border =>
            border.Name is "InlineFocusRing" or "FormFocusRing" or "SpinnerFocusRing" && border.IsEffectivelyVisible);
        foreach (Visual ancestor in ring.GetVisualAncestors().TakeWhile(ancestor => ancestor != host))
            Assert.False(ancestor.ClipToBounds);
    }

    /// <summary>Dragging from keyboard focus suppresses the ring, preserves capture updates and stops on release or capture loss.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DraggingSuppressesRingAndRestoresExistingKeyboardFocus(bool loseCapture, bool loseFocus)
    {
        NumberScrubber control = Sample();
        var other = new Grid { Focusable = true, Height = 20 };
        Window host = InlineScrubberTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { control, other } });
        try
        {
            FormsTestHost.Show(host);
            Assert.True(Editor(control).Focus(NavigationMethod.Tab));
            Flush(host);
            Assert.True(Ring(control).IsVisible);
            Border area = Part<Border>(control, "ScrubArea");
            IPointer? captured = null;
            area.AddHandler(InputElement.PointerPressedEvent, (_, e) => captured = e.Pointer, global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            Point start = area.TranslatePoint(new Point(3, 15), host)!.Value;
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(start - new Vector(0, 6), RawInputModifiers.LeftMouseButton);
            Flush(host);
            Assert.Contains("scrubbing", Body(control).Classes);
            AssertPaintEditing(control, ring: false);
            Assert.Equal(5.125m, control.Value);
            if (loseFocus) Assert.True(other.Focus());
            host.MouseMove(start - new Vector(0, 12), RawInputModifiers.LeftMouseButton);
            Flush(host);
            Assert.Contains("focused", Body(control).Classes);
            Assert.Equal(6.125m, control.Value);
            AssertPaintEditing(control, ring: false);
            if (loseCapture) captured!.Capture(null);
            else host.MouseUp(start, MouseButton.Left);
            Flush(host);
            Assert.DoesNotContain("scrubbing", Body(control).Classes);
            Assert.Equal(!loseFocus, Body(control).Classes.Contains("focused"));
            Assert.Equal(!loseFocus, Ring(control).IsVisible);
            host.MouseMove(start - new Vector(0, 24));
            Assert.Equal(6.125m, control.Value);
            Assert.True(other.Focus());
            Assert.True(Editor(control).Focus(NavigationMethod.Tab));
            Flush(host);
            Assert.True(Ring(control).IsVisible);
        }
        finally { host.Close(); }
    }

    /// <summary>Live edits, commit, cancel, wheel gating and read-only input retain the existing scrubber semantics.</summary>
    [AvaloniaFact]
    public void EditingWheelAndReadOnlyKeepBehavior()
    {
        NumberScrubber control = Sample();
        Window host = InlineScrubberTestHost.Create(new StackPanel { Margin = new Thickness(24), Children = { control } });
        try
        {
            FormsTestHost.Show(host);
            Assert.True(Editor(control).Focus(NavigationMethod.Tab));
            host.KeyTextInput("2.25");
            Flush(host);
            Assert.Equal(2.25m, control.Value);
            KeyStroke(host, Key.Enter);
            Assert.Equal("2.25", Editor(control).Text);
            host.KeyTextInput("invalid");
            KeyStroke(host, Key.Escape);
            Assert.Equal("2.25", Editor(control).Text);
            Assert.False(Wheel(control, KeyModifiers.None).Handled);
            Assert.True(Wheel(control, KeyModifiers.Alt).Handled);
            Assert.Equal(3.25m, control.Value);
            control.IsReadOnly = true;
            Flush(host);
            host.KeyTextInput("8");
            Assert.False(Wheel(control, KeyModifiers.Alt).Handled);
            Assert.Equal(3.25m, control.Value);
            Assert.False(Part<Border>(control, "ScrubArea").IsHitTestVisible);
            Assert.False(Ring(control).IsVisible);
        }
        finally { host.Close(); }
    }

    /// <summary>The embedded editor retains its automation peer, value provider, name and read-only behavior.</summary>
    [AvaloniaFact]
    public void AutomationKeepsTheExistingEditorContract()
    {
        NumberScrubber control = Sample();
        control.Classes.Remove("inlineInput");
        Window host = InlineScrubberTestHost.Create(new StackPanel { Children = { control } });
        try
        {
            FormsTestHost.Show(host);
            TextBox editor = Editor(control);
            AutomationProperties.SetName(editor, "Scrub gain");
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(editor)!;
            var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetProvider<IValueProvider>());
            var expected = (peer.GetAutomationControlType(), peer.GetName(), value.Value, value.IsReadOnly);
            control.Classes.Add("inlineInput");
            Flush(host);
            Assert.Same(peer, ControlAutomationPeer.CreatePeerForElement(editor));
            Assert.Equal(expected, (peer.GetAutomationControlType(), peer.GetName(), value.Value, value.IsReadOnly));
            control.IsReadOnly = true;
            Assert.True(value.IsReadOnly);
            Assert.Equal("4.125", value.Value);
        }
        finally { host.Close(); }
    }

    private static PointerWheelEventArgs Wheel(NumberScrubber control, KeyModifiers modifiers)
    {
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        var args = new PointerWheelEventArgs(control, pointer, control, default, 0,
            new PointerPointProperties(), modifiers, new Vector(0, 1));
        control.RaiseEvent(args);
        return args;
    }
}
