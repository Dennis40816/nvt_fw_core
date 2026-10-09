// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Nvt.Core.Avalonia.Tests.Dividers;

/// <summary>Pins the installed native animation clock without introducing production snapshot styles.</summary>
internal sealed class DividerSnapshotClock : IDisposable
{
    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    // UI-thread-only private clock for the snapshot's native template parts. The global render clock stays untouched.
    private readonly object _clock = Activator.CreateInstance(
        typeof(Animatable).Assembly.GetType("Avalonia.Animation.Clock", throwOnError: true)!, nonPublic: true)!;

    internal DividerSnapshotClock() => SetPlayState(PlayState.Pause);

    internal void Attach(Animatable target) =>
        typeof(Animatable).GetProperty("Clock", Access)!.SetValue(target, _clock);

    internal void Prepare(ProgressBar bar, Window host)
    {
        bar.IsIndeterminate = false;
        DividerTestHost.Flush(host);
        Attach(bar);
        foreach (Animatable part in bar.GetVisualDescendants().OfType<Animatable>()) Attach(part);
        bar.IsIndeterminate = true;
        DividerTestHost.Flush(host);
    }

    internal void Tick(TimeSpan time)
    {
        SetPlayState(PlayState.Run);
        _clock.GetType().GetMethod("Pulse", Access)!.Invoke(_clock, [time]);
        SetPlayState(PlayState.Pause);
    }

    private void SetPlayState(PlayState state) =>
        _clock.GetType().GetProperty("PlayState", Access)!.SetValue(_clock, state);

    /// <inheritdoc />
    public void Dispose() => SetPlayState(PlayState.Stop);
}
