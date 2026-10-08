// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nvt.Core.Avalonia.Inputs;

/// <summary>A decimal input with live text updates, wheel steps and a draggable border.</summary>
public sealed partial class NumberScrubber : UserControl
{
    // Control references, editing state, and drag state are accessed only on the UI thread.
    private Border? _rootBorder;
    private Border? _scrubArea;
    private TextBox? _inputBox;
    // Routed focus events own editing, which remains active through the LostFocus commit.
    // IsFocused is already false during that commit; deriving it would format before value observers run.
    private bool _isEditing;
    private ScrubSession? _scrubSession;

    private sealed record ScrubSession(Point StartPoint, decimal StartValue);

    /// <summary>Initializes the decimal input and its event handlers.</summary>
    public NumberScrubber()
    {
        InitializeComponent();
        AttachControls();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Bubble);
    }
}
