// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Input;

namespace Nvt.Core.Avalonia.Inputs;

/// <summary>A decimal input with live text updates, wheel steps and a draggable border.</summary>
public sealed partial class NumberScrubber
{
    private void OnScrubAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsReadOnly || _scrubArea is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _scrubStart = e.GetPosition(this);
        _scrubStartValue = Value;
        _isScrubbing = true;
        e.Pointer.Capture(_scrubArea);
        _inputBox?.Focus();
        SetFocused(true);
        e.Handled = true;
    }

    private void OnScrubAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isScrubbing)
        {
            return;
        }

        var current = e.GetPosition(this);
        var deltaPixels = _scrubStart.Y - current.Y;
        var stepPixels = Math.Max(1.0, ScrubPixelsPerStep);
        var steps = (decimal)(deltaPixels / stepPixels);
        ApplyScrubSteps(steps);
        e.Handled = true;
    }

    private void OnScrubAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_scrubArea is not null && e.Pointer.Captured == _scrubArea)
        {
            e.Pointer.Capture(null);
        }

        _isScrubbing = false;
        SetFocused(_inputBox?.IsFocused ?? false);
    }

    private void OnScrubAreaPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isScrubbing = false;
        SetFocused(_inputBox?.IsFocused ?? false);
    }

    private void StepBy(decimal steps)
    {
        if (SmallChange <= 0)
        {
            return;
        }

        if (SnapToStep)
        {
            steps = Math.Round(steps, 0, MidpointRounding.AwayFromZero);
        }

        var delta = steps * SmallChange;
        if (delta == 0)
        {
            return;
        }

        SetCurrentValue(ValueProperty, Clamp(Value + delta));
        UpdateTextFromValue(force: true);
    }

    private void ApplyScrubSteps(decimal steps)
    {
        if (SmallChange <= 0)
        {
            return;
        }

        if (SnapToStep)
        {
            steps = Math.Round(steps, 0, MidpointRounding.AwayFromZero);
        }

        var newValue = _scrubStartValue + steps * SmallChange;
        SetCurrentValue(ValueProperty, Clamp(newValue));
        UpdateTextFromValue(force: true);
    }
}
