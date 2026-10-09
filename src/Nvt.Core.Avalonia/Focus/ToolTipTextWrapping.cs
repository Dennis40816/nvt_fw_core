// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Nvt.Core.Avalonia.Focus;

/// <summary>Converts nonblank string tooltips to wrapping text with live tooltip resource bindings.</summary>
public static class ToolTipTextWrapping
{
    // RegistrationLock protects the subscription through registration and disposal.
    private static readonly Lock RegistrationLock = new();
    private static IDisposable? _registration;

    /// <summary>Installs the application-wide string tooltip handler once. Call before assigning tooltips.</summary>
    public static void Register()
    {
        lock (RegistrationLock)
        {
            _registration ??= ToolTip.TipProperty.Changed.AddClassHandler<Control, object?>(OnTipChanged);
        }
    }

    /// <summary>Removes the handler for test isolation. Already converted tooltips keep their resource bindings.</summary>
    public static void Unregister()
    {
        lock (RegistrationLock)
        {
            _registration?.Dispose();
            _registration = null;
        }
    }

    private static void OnTipChanged(Control target, AvaloniaPropertyChangedEventArgs<object?> change)
    {
        if (change.NewValue.Value is not string text || string.IsNullOrWhiteSpace(text)) return;

        var textBlock = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "Nvt.ToolTip.ForegroundBrush");
        textBlock.SetResourceReference(TextBlock.MaxWidthProperty, "Nvt.ToolTip.MaxWidth");
        target.SetCurrentValue(ToolTip.TipProperty, textBlock);
    }

    // Avalonia's equivalent of SetResourceReference is Bind with DynamicResourceExtension.
    private static void SetResourceReference(this TextBlock target, AvaloniaProperty property, string key) =>
        _ = target.Bind(property, new DynamicResourceExtension(key));
}
