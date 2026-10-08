// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.Inputs;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Inputs;

/// <summary>Pins drag sessions and editing across real focus transitions.</summary>
public sealed partial class NumberScrubberTests
{
    /// <summary>Presses on the text box and outer border cannot start a scrub session.</summary>
    [AvaloniaFact]
    public void DragStartsOnlyOnTheScrubArea()
    {
        var control = new NumberScrubber { Value = 4m };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        foreach (Control target in new Control[] { Input(control), Root(control) })
        {
            var args = new PointerPressedEventArgs(target, pointer, control, new Point(10, 60),
                0, new PointerPointProperties(RawInputModifiers.LeftMouseButton,
                    PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1);
            target.RaiseEvent(args);
            Assert.NotSame(Area(control), pointer.Captured);
            Assert.False(MoveArea(control, pointer, 12).Handled);
            Assert.Equal(4m, control.Value);
            pointer.Capture(null);
        }

        Assert.True(PressArea(control, pointer).Handled);
        Assert.Same(Area(control), pointer.Captured);
        MoveArea(control, pointer, 6);
        Assert.Equal(5m, control.Value);
        ReleaseArea(control, pointer);
    }

    /// <summary>A second pointer press restarts the drag from the current value and captures the second pointer.</summary>
    [AvaloniaFact]
    public void SecondPointerPressRestartsTheDrag()
    {
        var control = new NumberScrubber { Value = 4m };
        using var host = new ScrubberHost(control);
        using var first = new Pointer(1, PointerType.Mouse, true);
        using var second = new Pointer(2, PointerType.Touch, false);
        PressArea(control, first);
        MoveArea(control, first, 12);
        Assert.Equal(6m, control.Value);

        PressArea(control, second);

        Assert.Same(Area(control), second.Captured);
        MoveArea(control, second, 6);
        Assert.Equal(7m, control.Value);
        ReleaseArea(control, second);
    }

    /// <summary>Release and capture loss stop updates, and the next drag starts from the latest value.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinishedDragStopsMovementAndNextDragUsesTheNewValue(bool loseCapture)
    {
        var control = new NumberScrubber { Value = 4m };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        PressArea(control, pointer);
        MoveArea(control, pointer, 12);
        Assert.Equal(6m, control.Value);
        if (loseCapture) pointer.Capture(null);
        else ReleaseArea(control, pointer);

        Assert.Null(pointer.Captured);
        Assert.False(MoveArea(control, pointer, 60).Handled);
        Assert.Equal(6m, control.Value);
        control.Value = 20m;
        Assert.True(PressArea(control, pointer).Handled);
        MoveArea(control, pointer, 6);
        Assert.Equal(21m, control.Value);
        ReleaseArea(control, pointer);
    }

    /// <summary>Disabling preserves captured movement until release, matching the existing control behavior.</summary>
    [AvaloniaFact]
    public void DisablingKeepsTheCapturedDragUntilRelease()
    {
        var control = new NumberScrubber { Value = 4m };
        Window window = Host(control);
        try
        {
            window.Show();
            Drain();
            Point start = Area(control).TranslatePoint(new Point(3, 15), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start - new Vector(0, 6), RawInputModifiers.LeftMouseButton);
            Assert.Equal(5m, control.Value);

            control.IsEnabled = false;
            window.MouseMove(start - new Vector(0, 12), RawInputModifiers.LeftMouseButton);
            Assert.Equal(6m, control.Value);
            control.IsEnabled = true;
            window.MouseMove(start - new Vector(0, 18), RawInputModifiers.LeftMouseButton);
            Assert.Equal(7m, control.Value);
            window.MouseUp(start, MouseButton.Left);
            window.MouseMove(start - new Vector(0, 24));
            Assert.Equal(7m, control.Value);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start - new Vector(0, 6), RawInputModifiers.LeftMouseButton);
            Assert.Equal(8m, control.Value);
            window.MouseUp(start, MouseButton.Left);
        }
        finally { window.Close(); }
    }

    /// <summary>Focus, value, and range changes preserve the captured drag's origin until release.</summary>
    [AvaloniaTheory]
    [InlineData("focus")]
    [InlineData("value")]
    [InlineData("range")]
    public void FocusValueAndRangeChangesKeepTheCapturedDrag(string change)
    {
        var control = new NumberScrubber { Value = 4m, Minimum = 0m, Maximum = 100m };
        var other = new TextBox();
        Window window = Host(new StackPanel { Children = { control, other } });
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        try
        {
            window.Show();
            Drain();
            PressArea(control, pointer);
            MoveArea(control, pointer, 12);
            Assert.Equal(6m, control.Value);
            switch (change)
            {
                case "focus":
                    Assert.True(other.Focus());
                    break;
                case "value":
                    control.Value = 20m;
                    break;
                case "range":
                    control.Maximum = 5m;
                    break;
            }

            Assert.Same(Area(control), pointer.Captured);
            Assert.True(MoveArea(control, pointer, 6).Handled);
            Assert.Equal(5m, control.Value);
            MoveArea(control, pointer, -6);
            Assert.Equal(3m, control.Value);
            ReleaseArea(control, pointer);
            Assert.False(MoveArea(control, pointer, 12).Handled);
            Assert.Equal(3m, control.Value);
        }
        finally { window.Close(); }
    }

    /// <summary>Editing follows real focus, stays active after wheel input, and stops after focus loss.</summary>
    [AvaloniaFact]
    public void EditingFollowsFocusBeforeAndAfterWheelValueChanges()
    {
        var control = new NumberScrubber { Value = 4m, SnapToStep = false };
        var other = new TextBox();
        Window window = Host(new StackPanel { Children = { control, other } });
        TextBox input = Input(control);
        try
        {
            window.Show();
            Assert.True(other.Focus());
            Assert.False(input.IsFocused);
            Type(input, "7");
            Assert.Equal(4m, control.Value);
            Assert.True(input.Focus());
            Type(input, "2.3456");
            Assert.Equal(2.3456m, control.Value);
            Assert.Equal("2.3456", input.Text);

            Assert.True(Wheel(control, 1, KeyModifiers.Alt).Handled);
            Drain();
            Assert.True(input.IsFocused);
            Assert.Equal(3.346m, control.Value);
            Type(input, "8.7654");
            Assert.Equal(8.7654m, control.Value);
            Assert.Equal("8.7654", input.Text);

            Assert.True(other.Focus());
            Drain();
            Assert.False(input.IsFocused);
            Assert.Equal(8.7654m, control.Value);
            Assert.Equal("8.765", input.Text);
            control.Value = 10m;
            Assert.Equal("10", input.Text);
            Type(input, "12");
            Assert.Equal(10m, control.Value);
            Assert.True(input.Focus());
            Type(input, "14");
            Assert.Equal(14m, control.Value);
        }
        finally { window.Close(); }
    }

    /// <summary>A focus-loss commit publishes the value before formatting the pending edit.</summary>
    [AvaloniaFact]
    public void FocusLossCommitPreservesTextDuringValueNotification()
    {
        var control = new NumberScrubber { Value = 1m, SnapToStep = false };
        var other = new TextBox();
        Window window = Host(new StackPanel { Children = { control, other } });
        TextBox input = Input(control);
        string? textAtValueChange = null;
        bool? focusedAtValueChange = null;
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property != NumberScrubber.ValueProperty) return;
            textAtValueChange = input.Text;
            focusedAtValueChange = input.IsFocused;
        };
        try
        {
            window.Show();
            Assert.True(input.Focus());
            input.Text = "2.3456";
            Assert.True(other.Focus());
            Drain();

            Assert.False(focusedAtValueChange);
            Assert.Equal("2.3456", textAtValueChange);
            Assert.Equal(2.3456m, control.Value);
            Assert.Equal("2.346", input.Text);
        }
        finally { window.Close(); }
    }
}
