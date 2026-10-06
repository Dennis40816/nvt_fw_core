// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Progress;
using Xunit;

namespace Nvt.Core.Tests.Progress;

/// <summary>Characterizes NFC's fraction rule without adding step-text validation.</summary>
public sealed class ProgressUpdateTests
{
    /// <summary>Null means unknown. Valid fractions are retained without clamping.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(0.375)]
    [InlineData(double.Epsilon)]
    public void RetainsValidFractionsAndStepText(double? fraction)
    {
        var update = new ProgressUpdate(fraction, "Step");

        Assert.Equal(fraction, update.Fraction);
        Assert.Equal(fraction is null, update.IsIndeterminate);
        Assert.Equal("Step", update.StepText);
        var (deconstructedFraction, stepText) = update;
        Assert.Equal(fraction, deconstructedFraction);
        Assert.Equal("Step", stepText);
    }

    /// <summary>Negative values, values above one, infinities, and NaN use NFC's rejection rule.</summary>
    [Theory]
    [InlineData(-double.Epsilon)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void RejectsInvalidFractionsWithoutClamping(double fraction)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressUpdate(fraction, "Step"));

        Assert.Equal("fraction", error.ParamName);
        Assert.Equal(fraction, error.ActualValue);
        Assert.StartsWith("Progress must be between 0 and 1.", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Record initialization and copies use the same fraction validation.</summary>
    [Fact]
    public void RecordInitializationAndCopyPreserveTheFractionRule()
    {
        var initial = new ProgressUpdate(0, "Begin");
        var completed = initial with { Fraction = 1, StepText = "Done" };
        var unknown = completed with { Fraction = null };

        Assert.Equal(new ProgressUpdate(1, "Done"), completed);
        Assert.True(unknown.IsIndeterminate);
        Assert.Equal(0, initial.Fraction);
        Assert.Throws<ArgumentOutOfRangeException>(() => initial with { Fraction = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressUpdate { Fraction = 2 });
    }

    /// <summary>Negative zero remains valid and retains its sign bit.</summary>
    [Fact]
    public void RetainsNegativeZeroWithoutClamping()
    {
        double fraction = BitConverter.Int64BitsToDouble(long.MinValue);

        var update = new ProgressUpdate(fraction, "Step");

        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(update.Fraction!.Value));
        Assert.False(update.IsIndeterminate);
    }

    /// <summary>Core retains supplied text, including empty or whitespace text.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void StepTextIsRetained(string stepText)
    {
        Assert.Equal(stepText, new ProgressUpdate(null, stepText).StepText);
    }
}