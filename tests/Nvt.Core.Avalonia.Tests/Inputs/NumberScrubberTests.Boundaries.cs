// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.Inputs;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Inputs;

/// <summary>Characterizes normalization, boundaries and the source's decimal overflow behavior.</summary>
public sealed partial class NumberScrubberTests
{
    /// <summary>Edits round midpoint steps away from zero before clamping, including non-step boundaries.</summary>
    [AvaloniaTheory]
    [InlineData("1.24", "0.5", "-10", "10", "1", "1")]
    [InlineData("1.25", "0.5", "-10", "10", "1.5", "1.5")]
    [InlineData("-1.24", "0.5", "-10", "10", "-1", "-1")]
    [InlineData("-1.25", "0.5", "-10", "10", "-1.5", "-1.5")]
    [InlineData("0.25", "0.5", "-10", "10", "0.5", "0.5")]
    [InlineData("-0.25", "0.5", "-10", "10", "-0.5", "-0.5")]
    [InlineData("1.26", "0.5", "-1.3", "1.3", "1.3", "1.3")]
    [InlineData("-1.26", "0.5", "-1.3", "1.3", "-1.3", "-1.3")]
    [InlineData("999", "1", "-2", "3", "3", "3")]
    [InlineData("-999", "1", "-2", "3", "-2", "-2")]
    [InlineData("1.25", "0", "-10", "10", "1.25", "1.25")]
    [InlineData("1.25", "-1", "-10", "10", "1.25", "1.25")]
    [InlineData("79228162514264337593543950335", "1", "-79228162514264337593543950335", "79228162514264337593543950335", "79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "1", "-79228162514264337593543950335", "79228162514264337593543950335", "-79228162514264337593543950335", "-79228162514264337593543950335")]
    public void SnapAndClampKeepLiteralBoundaries(string text, string step, string minimum,
        string maximum, string expectedValue, string expectedDisplay)
    {
        var control = new NumberScrubber
        {
            SmallChange = Parse(step), Minimum = Parse(minimum), Maximum = Parse(maximum),
        };
        TextBox input = Input(control);
        FocusInput(input);
        Type(input, text);
        Assert.Equal(Parse(expectedValue), control.Value);
        Assert.Equal(text, input.Text);
        Press(input, Key.Enter);
        Drain();
        Assert.Equal(Parse(expectedValue), control.Value);
        Assert.Equal(expectedDisplay, input.Text);
    }

    /// <summary>External Value changes clamp without snapping; changing a bound reclamps immediately.</summary>
    [AvaloniaFact]
    public void ExternalValuesClampWithoutSnappingAndBoundsReclamp()
    {
        var control = new NumberScrubber { Minimum = -2m, Maximum = 3m, SmallChange = 0.5m };
        control.Value = 1.24m;
        Assert.Equal(1.24m, control.Value);
        Assert.Equal("1.24", Input(control).Text);
        control.Maximum = 1m;
        Assert.Equal(1m, control.Value);
        Assert.Equal("1", Input(control).Text);
        control.Value = decimal.MinValue;
        Assert.Equal(-2m, control.Value);
        Assert.Equal("-2", Input(control).Text);
        control.Minimum = -1m;
        Assert.Equal(-1m, control.Value);
        Assert.Equal("-1", Input(control).Text);
        control.Value = decimal.MaxValue;
        Assert.Equal(1m, control.Value);
        Assert.Equal("1", Input(control).Text);
    }

    /// <summary>Both decimal endpoints roundtrip with snapping disabled or unit steps.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecimalEndpointsRoundtripWithoutSaturation(bool snap)
    {
        var control = new NumberScrubber { SnapToStep = snap };
        TextBox input = Input(control);
        FocusInput(input);
        Type(input, "79228162514264337593543950335");
        Assert.Equal(decimal.MaxValue, control.Value);
        Press(input, Key.Enter);
        Drain();
        Assert.Equal("79228162514264337593543950335", input.Text);
        Type(input, "-79228162514264337593543950335");
        Assert.Equal(decimal.MinValue, control.Value);
        Press(input, Key.Enter);
        Drain();
        Assert.Equal("-79228162514264337593543950335", input.Text);
    }

    /// <summary>Normalization preserves overflow before Clamp, even when a finite bound could fit.</summary>
    [AvaloniaTheory]
    [InlineData("79228162514264337593543950335", "0.1")]
    [InlineData("-79228162514264337593543950335", "0.1")]
    [InlineData("79228162514264337593543950335", "10")]
    [InlineData("-79228162514264337593543950335", "10")]
    public void NormalizationOverflowIsPreserved(string text, string step)
    {
        var control = new NumberScrubber { Value = 7m, Maximum = 10m, SmallChange = Parse(step) };
        Type(Input(control), text);
        Assert.Throws<OverflowException>(() => Press(Input(control), Key.Enter));
        Assert.Equal(7m, control.Value);
        Assert.Equal(text, Input(control).Text);
    }

    /// <summary>Wheel arithmetic overflows before clamping at either decimal endpoint.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void WheelOverflowAtDecimalEndpointsIsPreserved(bool positive)
    {
        var control = new NumberScrubber { Value = positive ? decimal.MaxValue : decimal.MinValue };
        Assert.Throws<OverflowException>(() => Wheel(control, positive ? 1 : -1, KeyModifiers.Alt));
        Assert.Equal(positive ? decimal.MaxValue : decimal.MinValue, control.Value);
        Assert.Equal(positive ? "79228162514264337593543950335" : "-79228162514264337593543950335",
            Input(control).Text);
    }
}
