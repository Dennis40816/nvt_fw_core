// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>A single muted filter summary and reset intent, visible only for an empty projection.</summary>
public sealed partial class ConsoleEmptyState : UserControl
{
    /// <summary>Defines the controller supplying this view's state and intents.</summary>
    public static readonly StyledProperty<ConsoleController?> ControllerProperty = AvaloniaProperty.Register<ConsoleEmptyState, ConsoleController?>(nameof(Controller));
    /// <summary>Gets or sets the controller. The host owns its lifetime.</summary>
    public ConsoleController? Controller { get => GetValue(ControllerProperty); set => SetValue(ControllerProperty, value); }

    /// <summary>Initializes the view and its local input routing.</summary>
    public ConsoleEmptyState()
    {
        InitializeComponent();
    }
}
