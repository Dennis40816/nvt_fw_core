// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Nvt.Core.Avalonia.Inputs;

/// <summary>A decimal input with live text updates, wheel steps and a draggable border.</summary>
public sealed partial class NumberScrubber
{
    private void AttachControls()
    {
        _rootBorder = this.FindControl<Border>("RootBorder");
        _scrubArea = this.FindControl<Border>("ScrubArea");
        _inputBox = this.FindControl<TextBox>("InputBox");

        if (_inputBox is null || _scrubArea is null)
        {
            return;
        }

        _inputBox.GotFocus += OnInputFocus;
        _inputBox.LostFocus += OnInputLostFocus;
        _inputBox.KeyDown += OnInputKeyDown;
        _inputBox.TextChanged += OnInputTextChanged;

        _scrubArea.PointerPressed += OnScrubAreaPointerPressed;
        _scrubArea.PointerMoved += OnScrubAreaPointerMoved;
        _scrubArea.PointerReleased += OnScrubAreaPointerReleased;
        _scrubArea.PointerCaptureLost += OnScrubAreaPointerCaptureLost;

        UpdateReadOnlyState();
        UpdateScrubHint();
        UpdateTextFromValue(force: true);
    }

    private void UpdateScrubHint()
    {
        if (_scrubArea is not null)
        {
            ToolTip.SetTip(_scrubArea, string.IsNullOrEmpty(ScrubHint) ? null : ScrubHint);
        }
    }

    private void UpdateReadOnlyState()
    {
        if (_inputBox is null)
        {
            return;
        }

        _inputBox.IsReadOnly = IsReadOnly;
        if (_scrubArea is not null)
        {
            _scrubArea.IsHitTestVisible = !IsReadOnly;
        }
    }

    private void OnInputFocus(object? sender, FocusChangedEventArgs e)
    {
        _isEditing = true;
        if (IsMixed && _inputBox is not null)
        {
            _inputBox.Text = string.Empty;
        }

        SetFocused(true);
    }

    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitText();
        _isEditing = false;
        SetFocused(_isScrubbing);
        UpdateTextFromValue(force: true);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitText();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            UpdateTextFromValue(force: true);
            e.Handled = true;
        }
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isEditing || _inputBox is null)
        {
            return;
        }

        var text = _inputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            var normalized = NormalizeToStep(parsed);
            SetCurrentValue(ValueProperty, Clamp(normalized));
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsReadOnly)
        {
            return;
        }

        if (RequireAltForWheel && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        if (e.Delta.Y == 0)
        {
            return;
        }

        StepBy((decimal)e.Delta.Y);
        e.Handled = true;
    }
}
