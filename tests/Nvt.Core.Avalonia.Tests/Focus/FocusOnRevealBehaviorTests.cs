// Copyright (c) 2026 Dennis Liu. All rights reserved.
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Focus;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Focus;

/// <summary>Headless characterization of the frozen reveal-focus behavior.</summary>
public sealed class FocusOnRevealBehaviorTests
{
    /// <summary>The attached property is opt-in and rejects null elements.</summary>
    [AvaloniaFact]
    public void AttachedPropertyDefaultsToFalseAndRejectsNull()
    {
        Assert.False(FocusOnRevealBehavior.GetIsEnabled(new Grid()));
        Assert.Equal("element", Assert.Throws<ArgumentNullException>(
            () => FocusOnRevealBehavior.GetIsEnabled(null!)).ParamName);
        Assert.Equal("element", Assert.Throws<ArgumentNullException>(
            () => FocusOnRevealBehavior.SetIsEnabled(null!, true)).ParamName);
    }

    /// <summary>Ports NFC's loading-surface attachment contract as a runtime focus assertion.</summary>
    [AvaloniaFact]
    public void AttachmentQueuesKeyboardFocusWithTabNavigation()
    {
        var target = new Grid { Focusable = true };
        NavigationMethod? navigation = null;
        target.GotFocus += (_, e) => navigation = e.NavigationMethod;
        FocusOnRevealBehavior.SetIsEnabled(target, true);
        var window = new Window();
        try
        {
            window.Show();
            window.Content = target;
            Assert.False(target.IsFocused);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(target, window.FocusManager?.GetFocusedElement());
            Assert.Equal(NavigationMethod.Tab, navigation);
            Assert.Contains(":focus-visible", target.Classes);
        }
        finally { window.Close(); }
    }

    /// <summary>Queued focus uses the target's current availability when the dispatcher runs.</summary>
    [AvaloniaTheory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void QueuedFocusChecksCurrentVisibilityEnabledStateAndFocusability(
        bool visible, bool enabled, bool focusable, bool expectedFocus)
    {
        var target = new Grid { Focusable = true };
        FocusOnRevealBehavior.SetIsEnabled(target, true);
        var window = new Window();
        try
        {
            window.Show();
            window.Content = target;
            target.IsVisible = visible;
            target.IsEnabled = enabled;
            target.Focusable = focusable;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedFocus, target.IsFocused);
        }
        finally { window.Close(); }
    }

    /// <summary>Hidden or disabled ancestors prevent an otherwise available child from taking focus.</summary>
    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void QueuedFocusChecksEffectiveAncestorAvailability(bool visible, bool enabled)
    {
        var target = new Grid { Focusable = true };
        FocusOnRevealBehavior.SetIsEnabled(target, true);
        var parent = new Border { Child = target, IsVisible = visible, IsEnabled = enabled };
        var window = new Window { Content = parent };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(target.IsFocused);
        }
        finally { window.Close(); }
    }

    /// <summary>Showing the attached target queues focus; changing unrelated properties does not.</summary>
    [AvaloniaFact]
    public void VisibilityRevealQueuesFocus()
    {
        var target = new Grid { Focusable = true, IsVisible = false };
        var other = new Grid { Focusable = true };
        FocusOnRevealBehavior.SetIsEnabled(target, true);
        var window = new Window { Content = new StackPanel { Children = { other, target } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(other.Focus());
            target.IsVisible = true;
            Assert.Same(other, window.FocusManager?.GetFocusedElement());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(target, window.FocusManager?.GetFocusedElement());

            Assert.True(other.Focus());
            target.Width = 100;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(other, window.FocusManager?.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    /// <summary>Enabling an already attached target waits for a reveal; disabling cancels queued focus.</summary>
    [AvaloniaFact]
    public void EnableWaitsForRevealAndDisableCancelsQueuedFocus()
    {
        var target = new Grid { Focusable = true };
        var other = new Grid { Focusable = true };
        var window = new Window { Content = new StackPanel { Children = { other, target } } };
        try
        {
            window.Show();
            Assert.True(other.Focus());
            FocusOnRevealBehavior.SetIsEnabled(target, true);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(other, window.FocusManager?.GetFocusedElement());

            target.IsVisible = false;
            target.IsVisible = true;
            FocusOnRevealBehavior.SetIsEnabled(target, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(other, window.FocusManager?.GetFocusedElement());
            target.IsVisible = false;
            target.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(other, window.FocusManager?.GetFocusedElement());

            FocusOnRevealBehavior.SetIsEnabled(target, true);
            target.IsVisible = false;
            target.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(target, window.FocusManager?.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    /// <summary>An ancestor-only reveal does not queue focus, while reattachment does.</summary>
    [AvaloniaFact]
    public void AncestorRevealWaitsForTargetReattachment()
    {
        var target = new Grid { Focusable = true };
        var other = new Grid { Focusable = true };
        var parent = new Border { Child = target, IsVisible = false };
        FocusOnRevealBehavior.SetIsEnabled(target, true);
        var window = new Window { Content = new StackPanel { Children = { other, parent } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(other.Focus());
            parent.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(other, window.FocusManager?.GetFocusedElement());

            parent.Child = null;
            parent.Child = target;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(target, window.FocusManager?.GetFocusedElement());
        }
        finally { window.Close(); }
    }
}
