// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.LogConsole;

// Structural resources of the toolbar width tiers; there are no duplicated default dimensions here.
internal sealed class ConsoleDimensions : AvaloniaObject
{
    public static readonly AttachedProperty<double> NarrowBreakpointProperty =
        AvaloniaProperty.RegisterAttached<ConsoleDimensions, Control, double>("NarrowBreakpoint");
    public static double GetNarrowBreakpoint(Control control) => control.GetValue(NarrowBreakpointProperty);
    public static void SetNarrowBreakpoint(Control control, double value) => control.SetValue(NarrowBreakpointProperty, value);
    public static readonly AttachedProperty<double> WideBreakpointProperty =
        AvaloniaProperty.RegisterAttached<ConsoleDimensions, Control, double>("WideBreakpoint");
    public static double GetWideBreakpoint(Control control) => control.GetValue(WideBreakpointProperty);
    public static void SetWideBreakpoint(Control control, double value) => control.SetValue(WideBreakpointProperty, value);
}
