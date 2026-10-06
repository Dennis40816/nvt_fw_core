// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Threading;

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Reads theme resources for controls that draw in code, with a caller-supplied fallback.</summary>
/// <remarks>
/// A lookup first searches the owner and its styling parents with the owner's actual theme variant,
/// or <see cref="ThemeVariant.Default"/> when the owner has none.
/// When that fails and <see cref="UiThread.IsCurrent"/> succeeds, it searches the current application
/// with the same variant. Otherwise the method returns the fallback.
/// Call these methods on the UI thread. The resolver does not cache values or watch theme changes.
/// </remarks>
public static class UiResourceResolver
{
    /// <summary>Gets a brush resource, or creates one from a color resource.</summary>
    /// <param name="owner">The control whose resources and theme variant are used.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The brush returned when the key is missing or holds another type.</param>
    /// <param name="brushFactory">Creates a brush when the resource is a <see cref="Color"/>.</param>
    /// <returns>The resolved brush, a brush created from the resolved color, or <paramref name="fallback"/>.</returns>
    public static IBrush GetBrush(
        Control owner,
        string key,
        IBrush fallback,
        Func<Color, ISolidColorBrush> brushFactory)
    {
        if (TryResolve(owner, key, out var value))
        {
            if (value is IBrush brush)
            {
                return brush;
            }

            if (value is Color color)
            {
                return brushFactory(color);
            }
        }

        return fallback;
    }

    /// <summary>Gets a color resource, or the color of a <see cref="SolidColorBrush"/> resource.</summary>
    /// <param name="owner">The control whose resources and theme variant are used.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The color returned when the key is missing or holds another type.</param>
    /// <returns>The resolved color, or <paramref name="fallback"/>.</returns>
    public static Color GetColor(Control owner, string key, Color fallback = default)
    {
        if (TryGetColor(owner, key, out var color))
        {
            return color;
        }

        return fallback;
    }

    /// <summary>Tries to get a color resource, or the color of a <see cref="SolidColorBrush"/> resource.</summary>
    /// <param name="owner">The control whose resources and theme variant are used.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="color">The resolved color on success; otherwise the default color.</param>
    /// <returns>Whether the key holds a <see cref="Color"/> or a <see cref="SolidColorBrush"/>.</returns>
    public static bool TryGetColor(Control owner, string key, out Color color)
    {
        if (TryResolve(owner, key, out var value))
        {
            if (value is Color resolvedColor)
            {
                color = resolvedColor;
                return true;
            }

            if (value is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }
        }

        color = default;
        return false;
    }

    /// <summary>Gets a <see cref="double"/>, <see cref="float"/> or <see cref="int"/> resource as a double.</summary>
    /// <param name="owner">The control whose resources and theme variant are used.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The value returned when the key is missing or holds another type.</param>
    /// <returns>The resolved number, or <paramref name="fallback"/>.</returns>
    public static double GetDouble(Control owner, string key, double fallback = 0.0)
    {
        if (TryResolve(owner, key, out var value))
        {
            switch (value)
            {
                case double doubleValue:
                    return doubleValue;
                case float floatValue:
                    return floatValue;
                case int intValue:
                    return intValue;
            }
        }

        return fallback;
    }

    /// <summary>Gets a <see cref="CornerRadius"/> resource.</summary>
    /// <param name="owner">The control whose resources and theme variant are used.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The value returned when the key is missing or holds another type.</param>
    /// <returns>The resolved corner radius, or <paramref name="fallback"/>.</returns>
    public static CornerRadius GetCornerRadius(Control owner, string key, CornerRadius fallback)
    {
        return TryResolve(owner, key, out var value) && value is CornerRadius cornerRadius
            ? cornerRadius
            : fallback;
    }

    /// <summary>Gets a <see cref="Thickness"/> resource.</summary>
    /// <param name="owner">The control whose resources and theme variant are used.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The value returned when the key is missing or holds another type.</param>
    /// <returns>The resolved thickness, or <paramref name="fallback"/>.</returns>
    public static Thickness GetThickness(Control owner, string key, Thickness fallback)
    {
        return TryResolve(owner, key, out var value) && value is Thickness thickness
            ? thickness
            : fallback;
    }

    private static bool TryResolve(Control owner, string key, out object? value)
    {
        var theme = owner.ActualThemeVariant ?? ThemeVariant.Default;
        if (owner.TryFindResource(key, theme, out value))
        {
            return true;
        }

        if (UiThread.IsCurrent(out _, out var app) && app!.TryFindResource(key, theme, out value))
        {
            return true;
        }

        value = null;
        return false;
    }
}
