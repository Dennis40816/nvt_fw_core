// Copyright (c) 2026 Dennis Liu. All rights reserved.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Focus;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Focus;

/// <summary>Ports NFC's generic tooltip assertions and pins keyboard and pointer lifecycle behavior.</summary>
public sealed class FocusToolTipBehaviorTests
{
    /// <summary>The attached property is opt-in and rejects null elements.</summary>
    [AvaloniaFact]
    public void AttachedPropertyDefaultsToFalseAndRejectsNull()
    {
        Assert.False(FocusToolTipBehavior.GetIsEnabled(new Grid()));
        Assert.Equal("element", Assert.Throws<ArgumentNullException>(
            () => FocusToolTipBehavior.GetIsEnabled(null!)).ParamName);
        Assert.Equal("element", Assert.Throws<ArgumentNullException>(
            () => FocusToolTipBehavior.SetIsEnabled(null!, true)).ParamName);
    }

    /// <summary>Ports the behavior portion of NFC's noninteractive ComboBox tooltip contract.</summary>
    [AvaloniaFact]
    public void ComboBoxSelectionSuppressesTooltipUntilFocusLeaves()
    {
        var control = new ComboBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 0 };
        var tip = new ToolTip { IsHitTestVisible = false };
        ToolTip.SetTip(control, tip);
        FocusToolTipBehavior.SetIsEnabled(control, true);
        Assert.True(FocusToolTipBehavior.GetIsEnabled(control));
        Assert.Same(tip, ToolTip.GetTip(control));
        Assert.False(tip.IsHitTestVisible);

        ToolTip.SetIsOpen(control, true);
        control.SelectedIndex = 1;
        Assert.False(ToolTip.GetIsOpen(control));
        Assert.False(ToolTip.GetServiceEnabled(control));
        RaiseFocus(control, NavigationMethod.Unspecified);
        Assert.False(ToolTip.GetIsOpen(control));
        RaiseFocus(control, NavigationMethod.Tab);
        Assert.False(ToolTip.GetIsOpen(control));

        control.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Assert.True(ToolTip.GetServiceEnabled(control));
        RaiseFocus(control, NavigationMethod.Unspecified);
        Assert.False(ToolTip.GetIsOpen(control));
        control.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        RaiseFocus(control, NavigationMethod.Tab);
        Assert.True(ToolTip.GetIsOpen(control));
        FocusToolTipBehavior.SetIsEnabled(control, false);
    }

    /// <summary>Keyboard navigation opens the existing tooltip; pointer or unspecified focus does not.</summary>
    [AvaloniaTheory]
    [InlineData(NavigationMethod.Tab, true)]
    [InlineData(NavigationMethod.Directional, true)]
    [InlineData(NavigationMethod.Unspecified, false)]
    [InlineData(NavigationMethod.Pointer, false)]
    public void FocusNavigationUsesExistingTooltipWithoutMovingFocus(
        NavigationMethod navigation, bool expectedOpen)
    {
        var target = new Grid { Focusable = true };
        var other = new Grid { Focusable = true };
        var tip = new ToolTip { IsHitTestVisible = false };
        ToolTip.SetTip(target, tip);
        FocusToolTipBehavior.SetIsEnabled(target, true);
        var window = CreateWindow(new StackPanel { Children = { other, target } });
        try
        {
            window.Show();
            Assert.True(other.Focus());
            Assert.True(target.Focus(navigation));
            Assert.Equal(expectedOpen, ToolTip.GetIsOpen(target));
            Assert.Same(target, window.FocusManager?.GetFocusedElement());
            Assert.Same(tip, ToolTip.GetTip(target));
            Assert.False(tip.IsHitTestVisible);

            Assert.True(other.Focus());
            Assert.False(ToolTip.GetIsOpen(target));
            Assert.True(ToolTip.GetServiceEnabled(target));
        }
        finally { window.Close(); }
    }

    /// <summary>Ports NFC's Tab, Shift+Tab, Escape and focus-restoration assertions on synthetic rows.</summary>
    [AvaloniaFact]
    public void KeyboardTraversalOpensOneTooltipAndEscapeKeepsFocus()
    {
        var before = new Grid { Focusable = true, Height = 20 };
        var first = new Grid { Focusable = true, Height = 20 };
        var second = new Grid { Focusable = true, Height = 20 };
        var after = new Grid { Focusable = true, Height = 20 };
        foreach (Grid row in new[] { first, second })
        {
            ToolTip.SetTip(row, new ToolTip { IsHitTestVisible = false });
            FocusToolTipBehavior.SetIsEnabled(row, true);
        }
        var window = CreateWindow(new StackPanel { Children = { before, first, second, after } });
        bool escapeReachedWindow = false;
        window.KeyDown += (_, e) => escapeReachedWindow |= e.Key == Key.Escape;
        try
        {
            window.Show();
            Assert.True(before.Focus());
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.Same(first, window.FocusManager?.GetFocusedElement());
            Assert.True(ToolTip.GetIsOpen(first));
            Assert.False(ToolTip.GetIsOpen(second));
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.Same(second, window.FocusManager?.GetFocusedElement());
            Assert.True(ToolTip.GetIsOpen(second));
            Assert.False(ToolTip.GetIsOpen(first));

            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(escapeReachedWindow);
            Assert.Same(second, window.FocusManager?.GetFocusedElement());
            Assert.False(ToolTip.GetIsOpen(second));
            Assert.False(ToolTip.GetServiceEnabled(second));
            RaiseFocus(second, NavigationMethod.Tab);
            Assert.False(ToolTip.GetIsOpen(second));

            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.Same(first, window.FocusManager?.GetFocusedElement());
            Assert.True(ToolTip.GetIsOpen(first));
            Assert.True(ToolTip.GetServiceEnabled(second));
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(ToolTip.GetIsOpen(second));
            Assert.False(ToolTip.GetIsOpen(first));
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.Same(after, window.FocusManager?.GetFocusedElement());
            Assert.False(ToolTip.GetIsOpen(first));
            Assert.False(ToolTip.GetIsOpen(second));
        }
        finally { window.Close(); }
    }

    /// <summary>Only Escape with an open tooltip is handled and suppresses the tooltip service.</summary>
    [AvaloniaTheory]
    [InlineData(Key.Escape, true, true)]
    [InlineData(Key.Escape, false, false)]
    [InlineData(Key.Enter, true, false)]
    public void KeyHandlingRequiresEscapeAndAnOpenTooltip(Key key, bool open, bool expectedHandled)
    {
        var target = new Grid();
        ToolTip.SetTip(target, new ToolTip());
        FocusToolTipBehavior.SetIsEnabled(target, true);
        ToolTip.SetIsOpen(target, open);
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        target.RaiseEvent(args);
        Assert.Equal(expectedHandled, args.Handled);
        Assert.Equal(open && !expectedHandled, ToolTip.GetIsOpen(target));
        Assert.Equal(!expectedHandled, ToolTip.GetServiceEnabled(target));
        FocusToolTipBehavior.SetIsEnabled(target, false);
    }

    /// <summary>Closing the ComboBox dropdown suppresses keyboard reopening until focus leaves.</summary>
    [AvaloniaFact]
    public void ComboBoxDropDownCloseSuppressesTooltip()
    {
        var target = new ComboBox { ItemsSource = new[] { "First", "Second" } };
        var popup = new Popup { Name = "PART_Popup", PlacementTarget = target, Child = new Border() };
        target.Template = new FuncControlTemplate<ComboBox>((_, scope) => popup.RegisterInNameScope(scope));
        ToolTip.SetTip(target, new ToolTip());
        FocusToolTipBehavior.SetIsEnabled(target, true);
        var window = CreateWindow(target);
        try
        {
            window.Show();
            target.ApplyTemplate();
            popup.IsOpen = true;
            ToolTip.SetIsOpen(target, true);
            popup.IsOpen = false;
            Assert.False(ToolTip.GetIsOpen(target));
            Assert.False(ToolTip.GetServiceEnabled(target));
            RaiseFocus(target, NavigationMethod.Directional);
            Assert.False(ToolTip.GetIsOpen(target));
        }
        finally { window.Close(); }
    }

    /// <summary>Pointer exit restores the service; pointer entry restores it only for an unfocused target.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerRestoresServiceAccordingToFocus(bool focused)
    {
        var target = new Grid { Focusable = true };
        FocusToolTipBehavior.SetIsEnabled(target, true);
        var window = new Window { Content = target };
        try
        {
            window.Show();
            if (focused) { Assert.True(target.Focus()); }
            ToolTip.SetServiceEnabled(target, false);
            using var pointer = new Pointer(1, PointerType.Mouse, true);
            target.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, target, pointer,
                target, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.Equal(!focused, ToolTip.GetServiceEnabled(target));
            ToolTip.SetServiceEnabled(target, false);
            target.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, target, pointer,
                target, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.True(ToolTip.GetServiceEnabled(target));
        }
        finally { window.Close(); }
    }

    /// <summary>Disabling restores the service and detaches handlers; re-enabling restores the behavior.</summary>
    [AvaloniaFact]
    public void DisableRestoresServiceAndUnsubscribesHandlers()
    {
        var target = new ComboBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 0 };
        ToolTip.SetTip(target, new ToolTip());
        FocusToolTipBehavior.SetIsEnabled(target, true);
        ToolTip.SetIsOpen(target, true);
        ToolTip.SetServiceEnabled(target, false);
        FocusToolTipBehavior.SetIsEnabled(target, false);
        Assert.False(ToolTip.GetIsOpen(target));
        Assert.True(ToolTip.GetServiceEnabled(target));
        RaiseFocus(target, NavigationMethod.Tab);
        Assert.False(ToolTip.GetIsOpen(target));

        ToolTip.SetIsOpen(target, true);
        target.SelectedIndex = 1;
        target.IsDropDownOpen = true;
        target.IsDropDownOpen = false;
        var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        target.RaiseEvent(escape);
        Assert.False(escape.Handled);
        Assert.True(ToolTip.GetIsOpen(target));
        ToolTip.SetServiceEnabled(target, false);
        target.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Assert.False(ToolTip.GetServiceEnabled(target));

        ToolTip.SetIsOpen(target, false);
        ToolTip.SetServiceEnabled(target, true);
        FocusToolTipBehavior.SetIsEnabled(target, true);
        RaiseFocus(target, NavigationMethod.Tab);
        Assert.True(ToolTip.GetIsOpen(target));
        target.SelectedIndex = 0;
        Assert.False(ToolTip.GetIsOpen(target));
        Assert.False(ToolTip.GetServiceEnabled(target));
        FocusToolTipBehavior.SetIsEnabled(target, false);
    }

    // Headless windows need this template part for real tooltip and dropdown popup lifecycles.
    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Content = content,
            Width = 320,
            Height = 200,
            Template = new FuncControlTemplate<Window>((_, scope) => new VisualLayerManager
            {
                Name = "PART_VisualLayerManager",
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    Content = content,
                }.RegisterInNameScope(scope),
            }.RegisterInNameScope(scope)),
        };
    }

    private static void RaiseFocus(Control target, NavigationMethod navigation)
    {
        target.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent)
        {
            NavigationMethod = navigation,
        });
    }

    private static void Press(Window window, Key key, PhysicalKey physical,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }
}
