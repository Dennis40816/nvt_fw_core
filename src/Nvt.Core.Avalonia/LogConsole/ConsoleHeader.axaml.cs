// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>The 40 DIP title and app-action row.</summary>
public sealed partial class ConsoleHeader : UserControl
{
    /// <summary>Defines the controller supplying this view's state and intents.</summary>
    public static readonly StyledProperty<ConsoleController?> ControllerProperty = AvaloniaProperty.Register<ConsoleHeader, ConsoleController?>(nameof(Controller));
    /// <summary>Gets or sets the controller. The host owns its lifetime.</summary>
    public ConsoleController? Controller { get => GetValue(ControllerProperty); set => SetValue(ControllerProperty, value); }
    /// <summary>Defines the title text.</summary>
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ConsoleHeader, string>(nameof(Title), "Console");
    /// <summary>Gets or sets the title.</summary>
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    /// <summary>Defines the app command to copy selected rows.</summary>
    public static readonly StyledProperty<ICommand?> CopySelectedCommandProperty = AvaloniaProperty.Register<ConsoleHeader, ICommand?>(nameof(CopySelectedCommand));
    /// <summary>Gets or sets the app command to copy selected rows.</summary>
    public ICommand? CopySelectedCommand { get => GetValue(CopySelectedCommandProperty); set => SetValue(CopySelectedCommandProperty, value); }
    /// <summary>Defines the app command to copy all filtered rows.</summary>
    public static readonly StyledProperty<ICommand?> CopyVisibleCommandProperty = AvaloniaProperty.Register<ConsoleHeader, ICommand?>(nameof(CopyVisibleCommand));
    /// <summary>Gets or sets the app command to copy all filtered rows.</summary>
    public ICommand? CopyVisibleCommand { get => GetValue(CopyVisibleCommandProperty); set => SetValue(CopyVisibleCommandProperty, value); }
    /// <summary>Defines the app command to save a log.</summary>
    public static readonly StyledProperty<ICommand?> SaveLogCommandProperty = AvaloniaProperty.Register<ConsoleHeader, ICommand?>(nameof(SaveLogCommand));
    /// <summary>Gets or sets the app command to save a log.</summary>
    public ICommand? SaveLogCommand { get => GetValue(SaveLogCommandProperty); set => SetValue(SaveLogCommandProperty, value); }

    /// <summary>Initializes the view and its local input routing.</summary>
    public ConsoleHeader()
    {
        InitializeComponent();
        _ = new ConsoleControllerAttachment(this, ControllerProperty);
    }
}
