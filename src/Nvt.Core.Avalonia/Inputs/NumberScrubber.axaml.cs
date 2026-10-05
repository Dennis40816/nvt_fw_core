// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nvt.Core.Avalonia.Inputs;

/// <summary>A decimal input with live text updates, wheel steps and a draggable border.</summary>
public sealed partial class NumberScrubber : UserControl
{
    private Border? _rootBorder;
    private Border? _scrubArea;
    private TextBox? _inputBox;
    private bool _isEditing;
    private bool _isScrubbing;
    private Point _scrubStart;
    private decimal _scrubStartValue;

    /// <summary>Initializes the decimal input and its event handlers.</summary>
    public NumberScrubber()
    {
        InitializeComponent();
        AttachControls();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Bubble);
    }
}
