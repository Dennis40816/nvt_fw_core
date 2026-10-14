// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks the open, pin and close rules of the level row against a manual clock.</summary>
public sealed class ConsoleLevelRowModelTests
{
    private static (ConsoleLevelRowModel Model, ManualTimeProvider Clock, Func<int> Changes) Create(bool hoverAllowed = true)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var model = new ConsoleLevelRowModel(
            (delay, action) => clock.CreateTimer(_ => action(), null, delay, Timeout.InfiniteTimeSpan), () => hoverAllowed);
        var changes = 0;
        model.Changed += (_, _) => changes++;
        return (model, clock, () => changes);
    }

    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

    /// <summary>The pointer must rest on the label for 300 ms before the row opens, and that open is not pinned.</summary>
    [Fact]
    public void LabelEnteredOpensAfterTheOpenDelayWithoutPinning()
    {
        var (model, clock, changes) = Create();
        model.LabelEntered();
        clock.Advance(Ms(299));
        Assert.False(model.IsOpen);
        clock.Advance(Ms(1));
        Assert.True(model.IsOpen);
        Assert.False(model.IsPinned);
        Assert.Equal(1, changes());
    }

    /// <summary>A guard that refuses hover keeps the row closed when the timer fires.</summary>
    [Fact]
    public void HoverGuardRefusalKeepsTheRowClosed()
    {
        var (model, clock, changes) = Create(hoverAllowed: false);
        model.LabelEntered();
        clock.Advance(Ms(300));
        Assert.False(model.IsOpen);
        Assert.Equal(0, changes());
    }

    /// <summary>A second entry replaces the first timer, so only one open is ever pending and it counts from the last entry.</summary>
    [Fact]
    public void RepeatedLabelEntryRestartsTheOpenDelay()
    {
        var (model, clock, changes) = Create();
        model.LabelEntered();
        clock.Advance(Ms(200));
        model.LabelEntered();
        clock.Advance(Ms(200));
        Assert.False(model.IsOpen);
        clock.Advance(Ms(100));
        Assert.True(model.IsOpen);
        Assert.Equal(1, changes());
    }

    /// <summary>Leaving a hover-opened row closes it after 500 ms, and coming back in time cancels that.</summary>
    [Fact]
    public void RowExitClosesAfterTheCloseDelayAndReentryCancels()
    {
        var (model, clock, _) = Create();
        model.LabelEntered();
        clock.Advance(Ms(300));
        model.RowExited();
        clock.Advance(Ms(499));
        Assert.True(model.IsOpen);
        model.RowEntered();
        clock.Advance(Ms(5000));
        Assert.True(model.IsOpen);
        model.RowExited();
        clock.Advance(Ms(499));
        Assert.True(model.IsOpen);
        clock.Advance(Ms(1));
        Assert.False(model.IsOpen);
    }

    /// <summary>A click on a hover-opened row pins it, so leaving the row schedules nothing.</summary>
    [Fact]
    public void ToggleOnHoverOpenedRowPinsIt()
    {
        var (model, clock, _) = Create();
        model.LabelEntered();
        clock.Advance(Ms(300));
        model.Toggle();
        Assert.True(model.IsOpen);
        Assert.True(model.IsPinned);
        model.RowExited();
        clock.Advance(Ms(5000));
        Assert.True(model.IsOpen);
    }

    /// <summary>A second activation closes a pinned row, and an activation on a closed row opens it pinned.</summary>
    [Fact]
    public void ToggleOpensPinnedThenCloses()
    {
        var (model, _, changes) = Create();
        model.Toggle();
        Assert.True(model.IsOpen);
        Assert.True(model.IsPinned);
        model.Toggle();
        Assert.False(model.IsOpen);
        Assert.False(model.IsPinned);
        Assert.Equal(2, changes());
    }

    /// <summary>Escape closes an open row, reports that it did, and ignores a closed row.</summary>
    [Fact]
    public void EscapeClosesOnlyAnOpenRow()
    {
        var (model, _, _) = Create();
        Assert.False(model.Escape());
        model.Toggle();
        Assert.True(model.Escape());
        Assert.False(model.IsOpen);
    }

    /// <summary>Leaving the label before the delay cancels the open, and an open row keeps its close timer.</summary>
    [Fact]
    public void LabelExitCancelsOnlyAPendingOpen()
    {
        var (model, clock, changes) = Create();
        model.LabelEntered();
        clock.Advance(Ms(200));
        model.LabelExited();
        clock.Advance(Ms(5000));
        Assert.False(model.IsOpen);
        Assert.Equal(0, changes());
        model.LabelEntered();
        clock.Advance(Ms(300));
        model.RowExited();
        model.LabelExited();
        clock.Advance(Ms(500));
        Assert.False(model.IsOpen);
    }

    /// <summary>Pin keeps a hover-opened row open, and does nothing to a closed row.</summary>
    [Fact]
    public void PinKeepsAHoverOpenedRowOpen()
    {
        var (model, clock, changes) = Create();
        model.Pin();
        Assert.False(model.IsOpen);
        model.LabelEntered();
        clock.Advance(Ms(300));
        model.RowExited();
        model.Pin();
        clock.Advance(Ms(5000));
        Assert.True(model.IsOpen);
        Assert.True(model.IsPinned);
        Assert.Equal(2, changes());
    }

    /// <summary>Closing cancels a pending open, so a late timer cannot reopen the row.</summary>
    [Fact]
    public void CloseCancelsAPendingOpen()
    {
        var (model, clock, changes) = Create();
        model.LabelEntered();
        model.Close();
        clock.Advance(Ms(5000));
        Assert.False(model.IsOpen);
        Assert.Equal(0, changes());
    }
}
