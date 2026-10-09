// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Data;

namespace Nvt.Core.Avalonia.Inputs;

/// <summary>A decimal input with live text updates, wheel steps and a draggable border.</summary>
public sealed partial class NumberScrubber
{
    /// <summary>Identifies the <see cref="Value"/> styled property.</summary>
    public static readonly StyledProperty<decimal> ValueProperty =
        AvaloniaProperty.Register<NumberScrubber, decimal>(
            nameof(Value),
            0m,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Identifies the <see cref="Minimum"/> styled property.</summary>
    public static readonly StyledProperty<decimal> MinimumProperty =
        AvaloniaProperty.Register<NumberScrubber, decimal>(nameof(Minimum), decimal.MinValue);

    /// <summary>Identifies the <see cref="Maximum"/> styled property.</summary>
    public static readonly StyledProperty<decimal> MaximumProperty =
        AvaloniaProperty.Register<NumberScrubber, decimal>(nameof(Maximum), decimal.MaxValue);

    /// <summary>Identifies the <see cref="SmallChange"/> styled property.</summary>
    public static readonly StyledProperty<decimal> SmallChangeProperty =
        AvaloniaProperty.Register<NumberScrubber, decimal>(nameof(SmallChange), 1m);

    /// <summary>Identifies the <see cref="FormatString"/> styled property.</summary>
    public static readonly StyledProperty<string> FormatStringProperty =
        AvaloniaProperty.Register<NumberScrubber, string>(nameof(FormatString), "0.###");

    /// <summary>Identifies the <see cref="RequireAltForWheel"/> styled property.</summary>
    public static readonly StyledProperty<bool> RequireAltForWheelProperty =
        AvaloniaProperty.Register<NumberScrubber, bool>(nameof(RequireAltForWheel), true);

    /// <summary>Identifies the <see cref="SnapToStep"/> styled property.</summary>
    public static readonly StyledProperty<bool> SnapToStepProperty =
        AvaloniaProperty.Register<NumberScrubber, bool>(nameof(SnapToStep), true);

    /// <summary>Identifies the <see cref="ScrubPixelsPerStep"/> styled property.</summary>
    public static readonly StyledProperty<double> ScrubPixelsPerStepProperty =
        AvaloniaProperty.Register<NumberScrubber, double>(nameof(ScrubPixelsPerStep), 6.0);

    /// <summary>Identifies the <see cref="IsReadOnly"/> styled property.</summary>
    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<NumberScrubber, bool>(nameof(IsReadOnly), false);

    /// <summary>Identifies the <see cref="IsMixed"/> styled property.</summary>
    public static readonly StyledProperty<bool> IsMixedProperty =
        AvaloniaProperty.Register<NumberScrubber, bool>(nameof(IsMixed), false);

    /// <summary>Gets or sets the decimal value, clamped to the current bounds.</summary>
    public decimal Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Gets or sets the inclusive lower bound.</summary>
    /// <remarks>Callers must keep this bound less than or equal to <see cref="Maximum"/> after every assignment.</remarks>
    public decimal Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the inclusive upper bound.</summary>
    /// <remarks>Callers must keep this bound greater than or equal to <see cref="Minimum"/> after every assignment.</remarks>
    public decimal Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the decimal step used by text snapping, wheel input and dragging.</summary>
    public decimal SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>Gets or sets the invariant-culture display format.</summary>
    public string FormatString
    {
        get => GetValue(FormatStringProperty);
        set => SetValue(FormatStringProperty, value);
    }

    /// <summary>Gets or sets whether wheel input requires the Alt modifier.</summary>
    public bool RequireAltForWheel
    {
        get => GetValue(RequireAltForWheelProperty);
        set => SetValue(RequireAltForWheelProperty, value);
    }

    /// <summary>Gets or sets whether text values and wheel or drag step counts are rounded.</summary>
    public bool SnapToStep
    {
        get => GetValue(SnapToStepProperty);
        set => SetValue(SnapToStepProperty, value);
    }

    /// <summary>Gets or sets the vertical pixel distance per drag step, with an effective minimum of one.</summary>
    public double ScrubPixelsPerStep
    {
        get => GetValue(ScrubPixelsPerStepProperty);
        set => SetValue(ScrubPixelsPerStepProperty, value);
    }

    /// <summary>Gets or sets whether user text input, wheel steps and starting a drag are disabled.</summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Gets or sets whether the display shows an asterisk for a mixed value.</summary>
    public bool IsMixed
    {
        get => GetValue(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    /// <summary>Identifies the <see cref="ScrubHint"/> styled property.</summary>
    public static readonly StyledProperty<string?> ScrubHintProperty =
        AvaloniaProperty.Register<NumberScrubber, string?>(nameof(ScrubHint));

    /// <summary>Gets or sets the drag-area tooltip; null or empty removes it.</summary>
    public string? ScrubHint
    {
        get => GetValue(ScrubHintProperty);
        set => SetValue(ScrubHintProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            var newValue = (decimal)change.NewValue!;
            var clamped = Clamp(newValue);
            if (clamped != newValue)
            {
                SetCurrentValue(ValueProperty, clamped);
                return;
            }

            UpdateTextFromValue(force: !_isEditing);
            return;
        }

        if (change.Property == MinimumProperty || change.Property == MaximumProperty)
        {
            SetCurrentValue(ValueProperty, Clamp(Value));
            return;
        }

        if (change.Property == FormatStringProperty)
        {
            UpdateTextFromValue(force: true);
            return;
        }

        if (change.Property == IsReadOnlyProperty)
        {
            UpdateReadOnlyState();
        }

        if (change.Property == ScrubHintProperty)
        {
            UpdateScrubHint();
        }

        if (change.Property == IsMixedProperty)
        {
            UpdateTextFromValue(force: true);
        }
    }
}
