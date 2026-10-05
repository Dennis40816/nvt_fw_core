// Copyright (c) 2026 Dennis Liu. All rights reserved.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Nvt.Core.Avalonia.Focus;

/// <summary>Exposes an existing pointer tooltip from keyboard focus without moving focus.</summary>
public sealed class FocusToolTipBehavior : AvaloniaObject
{
    /// <summary>Enables focus and Escape handling for the attached control's existing tooltip.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<FocusToolTipBehavior, Control, bool>("IsEnabled");

    static FocusToolTipBehavior()
    {
        _ = IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private FocusToolTipBehavior()
    {
    }

    /// <summary>Gets whether focus tooltip behavior is enabled.</summary>
    public static bool GetIsEnabled(AvaloniaObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsEnabledProperty);
    }

    /// <summary>Sets whether focus tooltip behavior is enabled.</summary>
    public static void SetIsEnabled(AvaloniaObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.GotFocus -= Control_OnGotFocus;
        control.LostFocus -= Control_OnLostFocus;
        control.KeyDown -= Control_OnKeyDown;
        control.PointerEntered -= Control_OnPointerEntered;
        control.PointerExited -= Control_OnPointerExited;
        if (control is ComboBox comboBox)
        {
            comboBox.SelectionChanged -= ComboBox_OnSelectionChanged;
            comboBox.DropDownClosed -= ComboBox_OnDropDownClosed;
        }

        if (e.NewValue is true)
        {
            control.GotFocus += Control_OnGotFocus;
            control.LostFocus += Control_OnLostFocus;
            control.KeyDown += Control_OnKeyDown;
            control.PointerEntered += Control_OnPointerEntered;
            control.PointerExited += Control_OnPointerExited;
            if (control is ComboBox enabledComboBox)
            {
                enabledComboBox.SelectionChanged += ComboBox_OnSelectionChanged;
                enabledComboBox.DropDownClosed += ComboBox_OnDropDownClosed;
            }
        }
        else
        {
            CloseAndRestoreTooltip(control);
        }
    }

    private static void Control_OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && e is FocusChangedEventArgs { NavigationMethod: NavigationMethod.Tab or NavigationMethod.Directional })
        {
            if (ToolTip.GetServiceEnabled(control))
            {
                ToolTip.SetIsOpen(control, true);
            }
        }
    }

    private static void Control_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            ToolTip.SetIsOpen(control, false);
            ToolTip.SetServiceEnabled(control, true);
        }
    }

    private static void Control_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Control control && e.Key == Key.Escape && ToolTip.GetIsOpen(control))
        {
            CloseAndSuppressTooltip(control);
            e.Handled = true;
        }
    }

    private static void Control_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { IsFocused: false } control)
        {
            ToolTip.SetServiceEnabled(control, true);
        }
    }

    private static void Control_OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control control)
        {
            ToolTip.SetServiceEnabled(control, true);
        }
    }

    private static void ComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            CloseAndSuppressTooltip(comboBox);
        }
    }

    private static void ComboBox_OnDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            CloseAndSuppressTooltip(comboBox);
        }
    }

    private static void CloseAndSuppressTooltip(Control control)
    {
        ToolTip.SetIsOpen(control, false);
        ToolTip.SetServiceEnabled(control, false);
    }

    private static void CloseAndRestoreTooltip(Control control)
    {
        ToolTip.SetIsOpen(control, false);
        ToolTip.SetServiceEnabled(control, true);
    }
}
