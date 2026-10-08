// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Nvt.Core.Avalonia.Inputs;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Inputs;

/// <summary>Characterizes valid paired range updates through CLR, styled, binding and compiled XAML paths.</summary>
public sealed partial class NumberScrubberTests
{
    /// <summary>Moving disjoint ranges in a valid order keeps every observed pair valid and clamps without snapping.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedRangeUpdatesKeepClrAndStyledPathsEquivalent(bool useStyledValue)
    {
        var control = new NumberScrubber { Minimum = 0m, Maximum = 10m, Value = 4m };
        var values = new List<decimal>();
        control.PropertyChanged += (_, change) =>
        {
            Assert.True(control.Minimum <= control.Maximum);
            if (change.Property == NumberScrubber.ValueProperty)
            {
                values.Add(control.Value);
            }
        };

        SetMaximum(20m);
        SetMinimum(15.25m);
        Assert.Equal(15.25m, control.Value);
        Assert.Equal("15.25", Input(control).Text);
        SetMinimum(-20m);
        SetMaximum(-10.25m);
        Assert.Equal(-20m, control.Minimum);
        Assert.Equal(-10.25m, control.Maximum);
        Assert.Equal(-10.25m, control.Value);
        Assert.Equal("-10.25", Input(control).Text);
        Assert.Equal(new[] { 15.25m, -10.25m }, values);

        void SetMinimum(decimal value)
        {
            if (useStyledValue) { control.SetValue(NumberScrubber.MinimumProperty, value); }
            else { control.Minimum = value; }
        }

        void SetMaximum(decimal value)
        {
            if (useStyledValue) { control.SetValue(NumberScrubber.MaximumProperty, value); }
            else { control.Maximum = value; }
        }
    }

    /// <summary>Ordered binding updates retain both bound bindings and write clamped values back to the source.</summary>
    [AvaloniaFact]
    public void OrderedBindingUpdatesKeepSourceAndControlCoherent()
    {
        var source = new BoundRangeSource();
        var control = new NumberScrubber();
        using var minimumBinding = control.Bind(NumberScrubber.MinimumProperty,
            new Binding(nameof(BoundRangeSource.Minimum)) { Source = source, Mode = BindingMode.TwoWay });
        using var maximumBinding = control.Bind(NumberScrubber.MaximumProperty,
            new Binding(nameof(BoundRangeSource.Maximum)) { Source = source, Mode = BindingMode.TwoWay });
        using var valueBinding = control.Bind(NumberScrubber.ValueProperty,
            new Binding(nameof(BoundRangeSource.Value)) { Source = source, Mode = BindingMode.TwoWay });

        source.Maximum = 20m;
        source.Minimum = 15m;
        Drain();
        Assert.Equal(source.Minimum, control.Minimum);
        Assert.Equal(source.Maximum, control.Maximum);
        Assert.Equal(15m, control.Value);
        Assert.Equal(control.Value, source.Value);
        source.Minimum = -20m;
        source.Maximum = -10m;
        Drain();
        Assert.Equal(source.Minimum, control.Minimum);
        Assert.Equal(source.Maximum, control.Maximum);
        Assert.Equal(-10m, control.Value);
        Assert.Equal(control.Value, source.Value);
        source.Value = -12.25m;
        Drain();
        Assert.Equal(-12.25m, control.Value);
        Assert.Equal("-12.25", Input(control).Text);
    }

    /// <summary>Compiled XAML initializes ordered finite bounds and clamps an external value without snapping.</summary>
    [AvaloniaFact]
    public void CompiledXamlKeepsFiniteRangeBehavior()
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia.Tests/Inputs/NumberScrubberRangeExamples.axaml");
        var resources = new ResourceInclude(uri) { Source = uri };
        var control = Assert.IsType<NumberScrubber>(resources.Loaded["FiniteRange"]);
        Assert.Equal(0m, control.Minimum);
        Assert.Equal(10m, control.Maximum);
        Assert.Equal(4m, control.Value);
        control.Value = 12.25m;
        Assert.Equal(10m, control.Value);
        Assert.Equal("10", Input(control).Text);
    }

    private sealed class BoundRangeSource : AvaloniaObject
    {
        public static readonly StyledProperty<decimal> MinimumProperty =
            AvaloniaProperty.Register<BoundRangeSource, decimal>(nameof(Minimum));
        public static readonly StyledProperty<decimal> MaximumProperty =
            AvaloniaProperty.Register<BoundRangeSource, decimal>(nameof(Maximum), 10m);
        public static readonly StyledProperty<decimal> ValueProperty =
            AvaloniaProperty.Register<BoundRangeSource, decimal>(nameof(Value), 4m);

        public decimal Minimum
        {
            get => GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public decimal Maximum
        {
            get => GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public decimal Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
