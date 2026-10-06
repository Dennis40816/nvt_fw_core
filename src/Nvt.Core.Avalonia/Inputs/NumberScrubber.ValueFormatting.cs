// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia.Markup.Xaml;

namespace Nvt.Core.Avalonia.Inputs;

/// <summary>A decimal input with live text updates, wheel steps and a draggable border.</summary>
public sealed partial class NumberScrubber
{
    private void CommitText()
    {
        if (_inputBox is null)
        {
            return;
        }

        var text = _inputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateTextFromValue(force: true);
            return;
        }

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            var normalized = NormalizeToStep(parsed);
            SetCurrentValue(ValueProperty, Clamp(normalized));
            UpdateTextFromValue(force: true);
            return;
        }

        UpdateTextFromValue(force: true);
    }

    private void UpdateTextFromValue(bool force)
    {
        if (_inputBox is null)
        {
            return;
        }

        if (_isEditing && !force)
        {
            return;
        }

        if (IsMixed)
        {
            if (_inputBox.Text != "*")
            {
                _inputBox.Text = "*";
            }

            return;
        }

        var formatted = FormatValue(Value);
        if (_inputBox.Text != formatted)
        {
            _inputBox.Text = formatted;
        }
    }

    private string FormatValue(decimal value)
    {
        if (string.IsNullOrWhiteSpace(FormatString))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString(FormatString, CultureInfo.InvariantCulture);
    }

    private decimal Clamp(decimal value)
    {
        if (value < Minimum)
        {
            return Minimum;
        }

        if (value > Maximum)
        {
            return Maximum;
        }

        return value;
    }

    private decimal NormalizeToStep(decimal value)
    {
        if (!SnapToStep || SmallChange <= 0)
        {
            return value;
        }

        var steps = Math.Round(value / SmallChange, 0, MidpointRounding.AwayFromZero);
        return steps * SmallChange;
    }

    private void SetFocused(bool focused)
    {
        _rootBorder?.Classes.Set("focused", focused);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
