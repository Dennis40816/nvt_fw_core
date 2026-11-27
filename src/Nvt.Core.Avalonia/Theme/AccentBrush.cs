// Copyright (c) 2026 Dennis Liu
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Media;

namespace Nvt.Core.Avalonia.Theme;

// DynamicResource accepts brush sources, but cannot convert those brushes to Color.
// Keep token values as SolidColorBrush and let Avalonia own resource scope and theme lookup.
internal sealed class AccentBrush : AvaloniaObject
{
    public static readonly AttachedProperty<ISolidColorBrush?> FromProperty =
        AvaloniaProperty.RegisterAttached<AccentBrush, SolidColorBrush, ISolidColorBrush?>("From");

    public static readonly AttachedProperty<ISolidColorBrush?> ToProperty =
        AvaloniaProperty.RegisterAttached<AccentBrush, SolidColorBrush, ISolidColorBrush?>("To");

    public static readonly AttachedProperty<double> AmountProperty =
        AvaloniaProperty.RegisterAttached<AccentBrush, SolidColorBrush, double>("Amount");

    public static readonly AttachedProperty<bool> TransparentProperty =
        AvaloniaProperty.RegisterAttached<AccentBrush, SolidColorBrush, bool>("Transparent");

    static AccentBrush()
    {
        FromProperty.Changed.AddClassHandler<SolidColorBrush>((brush, _) => Update(brush));
        ToProperty.Changed.AddClassHandler<SolidColorBrush>((brush, _) => Update(brush));
        AmountProperty.Changed.AddClassHandler<SolidColorBrush>((brush, _) => Update(brush));
        TransparentProperty.Changed.AddClassHandler<SolidColorBrush>((brush, _) => Update(brush));
    }

    public static ISolidColorBrush? GetFrom(SolidColorBrush brush) => brush.GetValue(FromProperty);

    public static void SetFrom(SolidColorBrush brush, ISolidColorBrush? value) => brush.SetValue(FromProperty, value);

    public static ISolidColorBrush? GetTo(SolidColorBrush brush) => brush.GetValue(ToProperty);

    public static void SetTo(SolidColorBrush brush, ISolidColorBrush? value) => brush.SetValue(ToProperty, value);

    public static double GetAmount(SolidColorBrush brush) => brush.GetValue(AmountProperty);

    public static void SetAmount(SolidColorBrush brush, double value) => brush.SetValue(AmountProperty, value);

    public static bool GetTransparent(SolidColorBrush brush) => brush.GetValue(TransparentProperty);

    public static void SetTransparent(SolidColorBrush brush, bool value) => brush.SetValue(TransparentProperty, value);

    private static void Update(SolidColorBrush brush)
    {
        if (GetFrom(brush) is not { } from) return;
        Color color = from.Color;
        if (GetTo(brush) is { } to)
        {
            double amount = GetAmount(brush);
            Color target = to.Color;
            color = Color.FromArgb(Blend(color.A, target.A), Blend(color.R, target.R),
                Blend(color.G, target.G), Blend(color.B, target.B));

            byte Blend(byte start, byte end) => (byte)Math.Round(start + amount * (end - start));
        }
        brush.Color = GetTransparent(brush) ? Color.FromArgb(0, color.R, color.G, color.B) : color;
        brush.Opacity = from.Opacity;
    }
}
