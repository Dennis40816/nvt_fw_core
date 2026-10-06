// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Nvt.Core.Progress;

namespace Nvt.Core.Avalonia.Progress;

/// <summary>Applies a known progress fraction while retaining the caller's animation policy and ProgressBar styles.</summary>
public class ProgressIndicator : ProgressBar
{
    /// <summary>Defines the optional progress update.</summary>
    public static readonly StyledProperty<ProgressUpdate?> ProgressProperty =
        AvaloniaProperty.Register<ProgressIndicator, ProgressUpdate?>(nameof(Progress));

    /// <summary>Gets or sets progress. A missing fraction leaves the current value unchanged.</summary>
    public ProgressUpdate? Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ProgressBar);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProgressProperty && Progress?.Fraction is { } fraction)
        {
            Value = fraction;
        }
    }
}
