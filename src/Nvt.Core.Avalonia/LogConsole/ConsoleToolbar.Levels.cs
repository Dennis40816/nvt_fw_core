// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// The level row: hover, pin and keyboard.
public sealed partial class ConsoleToolbar
{
    private void InitializeLevelRow()
    {
        var label = this.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
        label.PointerEntered += (_, e) =>
        {
            if (e.Pointer.Type is not (PointerType.Mouse or PointerType.Pen)) return;
            // The flag follows the host presses; this entry resyncs it when a release never arrived.
            var buttons = e.GetCurrentPoint(this).Properties;
            _buttonDown = buttons.IsLeftButtonPressed || buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed;
            _levelRow.LabelEntered();
        };
        label.PointerExited += (_, _) => _levelRow.LabelExited();
        label.Click += (_, _) => _levelRow.Toggle();
        label.OpenRequested += (_, open) =>
        {
            if (open && !_levelRow.IsOpen) _levelRow.Toggle();
            else if (open) _levelRow.Pin();
            else _levelRow.Close();
        };
        // The counts grow with the log, so an open row re-checks that its names still fit.
        this.FindControl<StackPanel>("LevelItems")!.SizeChanged += (_, _) => ApplyLayout();
        PointerEntered += (_, _) => _levelRow.RowEntered();
        PointerExited += (_, _) => _levelRow.RowExited();
        var filter = this.FindControl<StackPanel>("LevelFilter")!;
        filter.AddHandler(KeyDownEvent, LevelKeyDown, RoutingStrategies.Bubble);
        // A press moves focus too; that case closes after the release, so the controls do not move under the pointer.
        filter.AddHandler(LostFocusEvent, (_, _) =>
        {
            if (!_buttonDown) Dispatcher.Post(CloseWhenFocusLeft, DispatcherPriority.Input);
        }, RoutingStrategies.Bubble);
    }

    // Guards for the hover open only. Click, tap and keyboard are never blocked.
    private bool HoverAllowed()
        => Controller?.IsActive == true && IsEffectivelyVisible && !_buttonDown
            && !this.FindControl<TextBox>("SearchBox")!.IsKeyboardFocusWithin
            && this.FindControl<Button>("Sources")!.Flyout is not { IsOpen: true };

    private void HostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _buttonDown = true;
        _outsidePress = _levelRow.IsOpen && !(e.Source is Visual source && InsideLevelFilter(source));
    }

    private void HostPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _buttonDown = false;
        if (!_outsidePress) return;
        _outsidePress = false;
        // Closing moves the other controls, so it waits until the click that caused it has finished.
        Dispatcher.Post(_levelRow.Close, DispatcherPriority.Input);
    }

    private bool InsideLevelFilter(Visual visual)
    {
        var filter = this.FindControl<StackPanel>("LevelFilter")!;
        return ReferenceEquals(visual, filter) || filter.IsVisualAncestorOf(visual);
    }

    private void CloseWhenFocusLeft()
    {
        if (!_levelRow.IsOpen) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null || !InsideLevelFilter(focused)) _levelRow.Close();
    }

    private ToggleButton[] VisibleItems()
        => Enum.GetValues<LogLevel>().Select(level => this.FindControl<ToggleButton>("Level" + level)!)
            .Where(item => item.IsEffectivelyVisible).ToArray();

    private void LevelKeyDown(object? sender, KeyEventArgs e)
    {
        var label = this.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
        // The row opens toward the end of the line, so right-to-left layouts swap the two arrow keys.
        var forward = FlowDirection == FlowDirection.RightToLeft ? Key.Left : Key.Right;
        if (e.Key == Key.Escape && _levelRow.Escape())
        {
            label.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
        else if ((e.Key == Key.Down || e.Key == forward) && ReferenceEquals(e.Source, label))
        {
            // Down or Right opens the row pinned, or pins a hover-opened row, then moves into it.
            if (!_levelRow.IsOpen) _levelRow.Toggle();
            else _levelRow.Pin();
            Dispatcher.Post(() =>
            {
                var items = VisibleItems();
                if (items.Length > 0) items[0].Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Input);
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right && e.Source is ToggleButton current)
        {
            var items = VisibleItems();
            var index = -1;
            for (var i = 0; i < items.Length; i++) if (ReferenceEquals(items[i], current)) index = i;
            if (index < 0) return;
            var next = index + (e.Key == forward ? 1 : -1);
            if (next < 0) label.Focus(NavigationMethod.Directional);
            else if (next < items.Length) items[next].Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }
}
