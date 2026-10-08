// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Inputs;

/// <summary>Examines whether throwing styled coercion can safely reject paired range updates.</summary>
public sealed class StyledRangeRejectionEvidenceTests
{
    /// <summary>Throwing coercion preserves the effective bound for an unbound CLR or styled assignment.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnboundRejectionPreservesEffectiveMinimum(bool useStyledValue)
    {
        var candidate = new RangeCandidate { Maximum = 10m };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (useStyledValue)
            {
                candidate.SetValue(RangeCandidate.MinimumProperty, 11m);
            }
            else
            {
                candidate.Minimum = 11m;
            }
        });
        Assert.Equal(0m, candidate.Minimum);
        Assert.Equal(10m, candidate.Maximum);
        candidate.Minimum = 1m;
        Assert.Equal(1m, candidate.Minimum);
    }

    /// <summary>A rejected CLR or styled assignment preserves the effective bound and its binding.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedLocalAssignmentPreservesBinding(bool useStyledValue)
    {
        var source = new RangeSource();
        var candidate = new RangeCandidate { Maximum = 10m };
        using var binding = candidate.Bind(RangeCandidate.MinimumProperty,
            new Binding(nameof(RangeSource.Minimum)) { Source = source, Mode = BindingMode.TwoWay });
        Assert.Equal(0m, candidate.Minimum);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (useStyledValue)
            {
                candidate.SetValue(RangeCandidate.MinimumProperty, 11m);
            }
            else
            {
                candidate.Minimum = 11m;
            }
        });
        Assert.Equal(0m, candidate.Minimum);
        Assert.Equal(0m, source.Minimum);
        source.Minimum = 1m;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1m, source.Minimum);
        Assert.Equal(1m, candidate.Minimum);
    }

    /// <summary>Rejecting a binding update leaves the source bound different from the effective control bound.</summary>
    [AvaloniaFact]
    public void RejectedBindingUpdateLeavesDifferentSourceAndTarget()
    {
        var source = new RangeSource();
        var candidate = new RangeCandidate { Maximum = 10m };
        using var binding = candidate.Bind(RangeCandidate.MinimumProperty,
            new Binding(nameof(RangeSource.Minimum)) { Source = source, Mode = BindingMode.TwoWay });
        source.Minimum = 11m;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(11m, source.Minimum);
        Assert.Equal(0m, candidate.Minimum);
        candidate.Maximum = 20m;
        candidate.CoerceValue(RangeCandidate.MinimumProperty);
        Assert.Equal(11m, source.Minimum);
        Assert.Equal(0m, candidate.Minimum);
        source.Minimum = 1m;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1m, candidate.Minimum);
    }

    // This candidate deliberately stays outside NumberScrubber's production contract.
    private sealed class RangeCandidate : AvaloniaObject
    {
        public static readonly StyledProperty<decimal> MinimumProperty =
            AvaloniaProperty.Register<RangeCandidate, decimal>(nameof(Minimum), 0m,
                coerce: (owner, value) => value <= ((RangeCandidate)owner).Maximum
                    ? value : throw new ArgumentOutOfRangeException(nameof(value)));

        public static readonly StyledProperty<decimal> MaximumProperty =
            AvaloniaProperty.Register<RangeCandidate, decimal>(nameof(Maximum), decimal.MaxValue,
                coerce: (owner, value) => value >= ((RangeCandidate)owner).Minimum
                    ? value : throw new ArgumentOutOfRangeException(nameof(value)));

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
    }

    private sealed class RangeSource : AvaloniaObject
    {
        public static readonly StyledProperty<decimal> MinimumProperty =
            AvaloniaProperty.Register<RangeSource, decimal>(nameof(Minimum));

        public decimal Minimum
        {
            get => GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }
    }
}
