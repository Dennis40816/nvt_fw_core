// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>The 48 DIP title and app-action row.</summary>
public sealed partial class ConsoleHeader : UserControl
{
    /// <summary>Defines the controller supplying this view's state and intents.</summary>
    public static readonly StyledProperty<ConsoleController?> ControllerProperty = AvaloniaProperty.Register<ConsoleHeader, ConsoleController?>(nameof(Controller));
    /// <summary>Gets or sets the controller. The host owns its lifetime.</summary>
    public ConsoleController? Controller { get => GetValue(ControllerProperty); set => SetValue(ControllerProperty, value); }
    /// <summary>Defines the title text.</summary>
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ConsoleHeader, string>(nameof(Title), ConsoleResourceText.Get("Title"));
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
        this.FindControl<Button>("ClearSearch")!.Command = new ConsoleClearSearchCommand(Controller);
        SizeChanged += (_, _) => ApplyLayout();
        ApplyLayout();
    }
    private void SearchChanged(object? sender, TextChangedEventArgs args)
    {
        if (sender is TextBox search && Controller is { } controller && controller.ResetFiltersCommand.CanExecute(null)
            && search.Text != controller.Filter.SearchText)
            controller.SetSearchText(search.Text);
    }
    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ConsoleDimensions.NarrowBreakpointProperty) ApplyLayout();
        if (change.Property == ControllerProperty && this.FindControl<Button>("ClearSearch") is { } button)
            button.Command = new ConsoleClearSearchCommand(Controller);
    }
    private void ApplyLayout()
    {
        // Resource bindings can publish while InitializeComponent is still populating names.
        if (WideSearch is null || WideActions is null || NarrowActions is null) return;
        var narrow = Bounds.Width <= ConsoleDimensions.GetNarrowBreakpoint(this);
        this.FindControl<Grid>("WideSearch")!.IsVisible = !narrow;
        this.FindControl<StackPanel>("WideActions")!.IsVisible = !narrow;
        this.FindControl<StackPanel>("NarrowActions")!.IsVisible = narrow;
    }
}
