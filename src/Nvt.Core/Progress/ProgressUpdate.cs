// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Progress;

/// <summary>Stores a fraction from zero to one, or null when progress is unknown, with caller-supplied text.</summary>
/// <remarks>The uninitialized default has no fraction or step text.</remarks>
/// <param name="Fraction">The fraction completed, or null when unknown.</param>
/// <param name="StepText">The text supplied by the operation, or null when absent.</param>
public readonly record struct ProgressUpdate(double? Fraction, string? StepText)
{
    private readonly double? fraction = ValidateFraction(Fraction);

    /// <summary>Gets or initializes the fraction. Values outside zero to one and NaN are rejected.</summary>
    public double? Fraction
    {
        get => fraction;
        init => fraction = ValidateFraction(value);
    }

    /// <summary>Gets whether the operation has not supplied a fraction.</summary>
    public bool IsIndeterminate => Fraction is null;

    private static double? ValidateFraction(double? fraction)
    {
        if (fraction is < 0 or > 1 || double.IsNaN(fraction ?? 0))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Progress must be between 0 and 1.");
        }

        return fraction;
    }
}
