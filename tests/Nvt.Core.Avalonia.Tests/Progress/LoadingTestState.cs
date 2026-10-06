// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Progress;

namespace Nvt.Core.Avalonia.Tests.Progress;

/// <summary>Supplies synthetic loading content without importing product state or commands.</summary>
/// <param name="AccessibleStatus">The tool-owned text and automation name.</param>
/// <param name="Fraction">The optional fraction.</param>
/// <param name="ShouldAnimate">The tool-owned animation policy.</param>
/// <param name="IsRunning">Whether the synthetic progress bar is visible.</param>
/// <param name="CanRetry">Whether the synthetic retry marker is visible.</param>
/// <param name="IsVisible">Whether the surface is visible.</param>
public sealed record LoadingTestState(
    string AccessibleStatus,
    double? Fraction,
    bool ShouldAnimate,
    bool IsRunning,
    bool CanRetry,
    bool IsVisible = true)
{
    /// <summary>Gets the matching Core progress value.</summary>
    public ProgressUpdate Update => new(Fraction, AccessibleStatus);
}