// Copyright (c) 2026 Dennis Liu. All rights reserved.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Nvt.Core.Avalonia.Focus;

/// <summary>Moves keyboard focus to a control when its focused child surface is revealed.</summary>
public sealed class FocusOnRevealBehavior : AvaloniaObject
{
    /// <summary>Enables focus transfer when the attached control becomes visible.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<FocusOnRevealBehavior, Control, bool>("IsEnabled");

    static FocusOnRevealBehavior()
    {
        _ = IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private FocusOnRevealBehavior()
    {
    }

    /// <summary>Gets whether reveal focus is enabled.</summary>
    public static bool GetIsEnabled(AvaloniaObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsEnabledProperty);
    }

    /// <summary>Sets whether reveal focus is enabled.</summary>
    public static void SetIsEnabled(AvaloniaObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.PropertyChanged -= Control_OnPropertyChanged;
        control.AttachedToVisualTree -= Control_OnAttachedToVisualTree;
        if (e.NewValue is true)
        {
            control.PropertyChanged += Control_OnPropertyChanged;
            control.AttachedToVisualTree += Control_OnAttachedToVisualTree;
        }
    }

    private static void Control_OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            QueueFocus(control);
        }
    }

    private static void Control_OnPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is Control control &&
            e.Property == Visual.IsVisibleProperty &&
            e.NewValue is true)
        {
            QueueFocus(control);
        }
    }

    private static void QueueFocus(Control control)
    {
        Dispatcher.UIThread.Post(
            () => FocusIfAvailable(control),
            DispatcherPriority.Input);
    }

    private static void FocusIfAvailable(Control control)
    {
        if (GetIsEnabled(control) &&
            control.IsEffectivelyVisible &&
            control.IsEffectivelyEnabled &&
            control.Focusable)
        {
            _ = control.Focus(NavigationMethod.Tab);
        }
    }
}
