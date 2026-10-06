// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.Inputs;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Inputs;

/// <summary>Characterizes wheel input, drag rounding and pointer capture lifecycle.</summary>
public sealed partial class NumberScrubberTests
{
    /// <summary>Wheel input keeps the Alt gate, fractional rounding and handled state.</summary>
    [AvaloniaTheory]
    [InlineData(true, KeyModifiers.None, 1d, true, "4", false)]
    [InlineData(true, KeyModifiers.Alt, 1d, true, "4.5", true)]
    [InlineData(true, KeyModifiers.Alt | KeyModifiers.Shift, -1d, true, "3.5", true)]
    [InlineData(false, KeyModifiers.None, 2d, true, "5", true)]
    [InlineData(false, KeyModifiers.Alt, -2d, true, "3", true)]
    [InlineData(false, KeyModifiers.None, 0d, true, "4", false)]
    [InlineData(false, KeyModifiers.None, 0.49d, true, "4", true)]
    [InlineData(false, KeyModifiers.None, 0.5d, true, "4.5", true)]
    [InlineData(false, KeyModifiers.None, -0.5d, true, "3.5", true)]
    [InlineData(false, KeyModifiers.None, 0.5d, false, "4.25", true)]
    [InlineData(false, KeyModifiers.None, -0.5d, false, "3.75", true)]
    public void WheelKeepsFrozenStepsAndAltRequirement(bool requireAlt, KeyModifiers modifiers,
        double delta, bool snap, string expectedValue, bool handled)
    {
        var control = new NumberScrubber
        {
            Value = 4m, SmallChange = 0.5m, RequireAltForWheel = requireAlt, SnapToStep = snap,
            LargeChange = 100m,
        };
        PointerWheelEventArgs args = Wheel(control, delta, modifiers);
        Assert.Equal(Parse(expectedValue), control.Value);
        Assert.Equal(expectedValue, Input(control).Text);
        Assert.Equal(handled, args.Handled);
    }

    /// <summary>Wheel input bubbles from descendants, respects read-only and clamps finite bounds.</summary>
    [AvaloniaFact]
    public void WheelBubblesClampsAndRespectsReadOnly()
    {
        var control = new NumberScrubber { Value = 2m, Minimum = -1m, Maximum = 3m };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        var args = new PointerWheelEventArgs(Input(control), pointer, control, default, 0,
            new PointerPointProperties(), KeyModifiers.Alt, new Vector(0, 8));
        Input(control).RaiseEvent(args);
        Assert.True(args.Handled);
        Assert.Equal(3m, control.Value);
        Assert.Equal("3", Input(control).Text);
        Wheel(control, -10, KeyModifiers.Alt);
        Assert.Equal(-1m, control.Value);
        Assert.Equal("-1", Input(control).Text);
        control.IsReadOnly = true;
        Assert.False(Wheel(control, 1, KeyModifiers.Alt).Handled);
        Assert.Equal(-1m, control.Value);
    }

    /// <summary>Nonpositive SmallChange disables stepping while still handling eligible wheel events.</summary>
    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonpositiveStepDisablesWheelAndDrag(string step)
    {
        var control = new NumberScrubber { Value = 4m, SmallChange = Parse(step) };
        Assert.True(Wheel(control, 1, KeyModifiers.Alt).Handled);
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        PressArea(control, pointer);
        MoveArea(control, pointer, 12);
        ReleaseArea(control, pointer);
        Assert.Equal(4m, control.Value);
        Assert.Equal("4", Input(control).Text);
    }

    /// <summary>Dragging is measured from the starting value, rounds away from zero, and ignores horizontal motion.</summary>
    [AvaloniaTheory]
    [InlineData(6d, 2.94d, true, "4")]
    [InlineData(6d, 3d, true, "4.5")]
    [InlineData(6d, -3d, true, "3.5")]
    [InlineData(6d, 12d, true, "5")]
    [InlineData(6d, -12d, true, "3")]
    [InlineData(6d, 3d, false, "4.25")]
    [InlineData(12d, 6d, true, "4.5")]
    [InlineData(0d, 0.5d, true, "4.5")]
    [InlineData(-6d, 1d, true, "4.5")]
    public void DragKeepsFrozenPixelSteps(double pixelsPerStep, double verticalPixels,
        bool snap, string expectedValue)
    {
        var control = new NumberScrubber
        {
            Value = 4m, SmallChange = 0.5m, SnapToStep = snap, ScrubPixelsPerStep = pixelsPerStep,
        };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        Assert.True(PressArea(control, pointer).Handled);
        Assert.Same(Area(control), pointer.Captured);
        Assert.Contains("focused", Root(control).Classes);
        Assert.True(MoveArea(control, pointer, verticalPixels).Handled);
        Assert.Equal(Parse(expectedValue), control.Value);
        Assert.Equal(expectedValue, Input(control).Text);
        MoveArea(control, pointer, 0, 40);
        Assert.Equal(4m, control.Value);
        Assert.Equal("4", Input(control).Text);
        ReleaseArea(control, pointer);
        Assert.Null(pointer.Captured);
        Assert.False(MoveArea(control, pointer, 12).Handled);
        Assert.Equal(4m, control.Value);
    }

    /// <summary>Actual capture loss ends dragging, keeps the last value and prevents later movement updates.</summary>
    [AvaloniaFact]
    public void CaptureLossEndsDragAndNextDragStartsFromCurrentValue()
    {
        var control = new NumberScrubber { Value = 4m, Minimum = 0m, Maximum = 6m };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        PressArea(control, pointer);
        MoveArea(control, pointer, 60);
        Assert.Equal(6m, control.Value);
        pointer.Capture(null);
        Assert.Null(pointer.Captured);
        Assert.False(MoveArea(control, pointer, -60).Handled);
        Assert.Equal(6m, control.Value);
        Assert.Contains("focused", Root(control).Classes);
        PressArea(control, pointer);
        MoveArea(control, pointer, -6);
        Assert.Equal(5m, control.Value);
        ReleaseArea(control, pointer);
        Assert.Equal("5", Input(control).Text);
    }

    /// <summary>Read-only or non-left-button presses never capture or start a drag.</summary>
    [AvaloniaFact]
    public void ReadOnlyAndNonLeftPressDoNotStartDrag()
    {
        var control = new NumberScrubber { Value = 4m, IsReadOnly = true };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        Assert.False(PressArea(control, pointer).Handled);
        Assert.Null(pointer.Captured);
        Assert.False(MoveArea(control, pointer, 12).Handled);
        Assert.Equal(4m, control.Value);
        control.IsReadOnly = false;
        Assert.False(PressArea(control, pointer, false).Handled);
        Assert.Null(pointer.Captured);
        MoveArea(control, pointer, 12);
        Assert.Equal(4m, control.Value);
    }

    /// <summary>The source only checks read-only at press; changing it during capture keeps the active drag.</summary>
    [AvaloniaFact]
    public void ReadOnlyChangeDuringCapturedDragKeepsSourceBehavior()
    {
        var control = new NumberScrubber { Value = 4m };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        PressArea(control, pointer);
        control.IsReadOnly = true;
        MoveArea(control, pointer, 6);
        Assert.Equal(5m, control.Value);
        Assert.Equal("5", Input(control).Text);
        ReleaseArea(control, pointer);
        Assert.Null(pointer.Captured);
    }

    /// <summary>Drag arithmetic also preserves overflow before clamping at the decimal endpoints.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void DragOverflowAtDecimalEndpointsIsPreserved(bool positive)
    {
        var control = new NumberScrubber { Value = positive ? decimal.MaxValue : decimal.MinValue };
        using var host = new ScrubberHost(control);
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        PressArea(control, pointer);
        Assert.Throws<OverflowException>(() => MoveArea(control, pointer, positive ? 6 : -6));
        Assert.Equal(positive ? decimal.MaxValue : decimal.MinValue, control.Value);
        ReleaseArea(control, pointer);
    }

    private sealed class ScrubberHost : IDisposable
    {
        private readonly Window _window;
        public ScrubberHost(NumberScrubber control)
        {
            _window = Host(control);
            _window.Show();
            Drain();
        }
        public void Dispose() => _window.Close();
    }

    private static PointerWheelEventArgs Wheel(NumberScrubber control, double delta, KeyModifiers modifiers)
    {
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        var args = new PointerWheelEventArgs(control, pointer, control, default, 0,
            new PointerPointProperties(), modifiers, new Vector(0, delta));
        control.RaiseEvent(args);
        return args;
    }
    private static PointerPressedEventArgs PressArea(NumberScrubber control, Pointer pointer, bool left = true)
    {
        var properties = new PointerPointProperties(left ? RawInputModifiers.LeftMouseButton :
            RawInputModifiers.RightMouseButton, left ? PointerUpdateKind.LeftButtonPressed :
            PointerUpdateKind.RightButtonPressed);
        var args = new PointerPressedEventArgs(Area(control), pointer, control, new Point(10, 60),
            0, properties, KeyModifiers.None, 1);
        Area(control).RaiseEvent(args);
        return args;
    }
    private static PointerEventArgs MoveArea(NumberScrubber control, Pointer pointer, double pixels,
        double horizontalPixels = 0)
    {
        var args = new PointerEventArgs(InputElement.PointerMovedEvent, Area(control), pointer, control,
            new Point(10 + horizontalPixels, 60 - pixels), 0, new PointerPointProperties(), KeyModifiers.None);
        Area(control).RaiseEvent(args);
        return args;
    }
    private static void ReleaseArea(NumberScrubber control, Pointer pointer)
    {
        var args = new PointerReleasedEventArgs(Area(control), pointer, control, new Point(10, 60),
            0, new PointerPointProperties(), KeyModifiers.None, MouseButton.Left);
        Area(control).RaiseEvent(args);
    }
}
