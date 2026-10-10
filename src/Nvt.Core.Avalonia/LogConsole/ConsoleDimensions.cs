// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.LogConsole;

// Structural resources are shared by both responsive rows; there are no duplicated default dimensions here.
internal sealed class ConsoleDimensions : AvaloniaObject
{
    public static readonly AttachedProperty<double> NarrowBreakpointProperty =
        AvaloniaProperty.RegisterAttached<ConsoleDimensions, Control, double>("NarrowBreakpoint");
    public static double GetNarrowBreakpoint(Control control) => control.GetValue(NarrowBreakpointProperty);
    public static void SetNarrowBreakpoint(Control control, double value) => control.SetValue(NarrowBreakpointProperty, value);
}
