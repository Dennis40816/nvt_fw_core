// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Nvt.Core.Avalonia.Icons;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>The responsive event-scope filter row.</summary>
public sealed partial class ConsoleToolbar : UserControl
{
    // UI-thread-only subscription; exists only while attached.
    private TopLevel? _iconHost;
    /// <summary>Defines the controller supplying this view's state and intents.</summary>
    public static readonly StyledProperty<ConsoleController?> ControllerProperty = AvaloniaProperty.Register<ConsoleToolbar, ConsoleController?>(nameof(Controller));
    /// <summary>Gets or sets the controller. The host owns its lifetime.</summary>
    public ConsoleController? Controller { get => GetValue(ControllerProperty); set => SetValue(ControllerProperty, value); }

    /// <summary>Initializes the view and its local input routing.</summary>
    public ConsoleToolbar()
    {
        InitializeComponent();
        this.FindControl<Button>("ClearSearch")!.Command = new ConsoleClearSearchCommand(Controller);
        SizeChanged += (_, _) => ApplyLayout();
        ApplyLayout();
        var source = this.FindControl<Button>("Sources")!;
        var menu = (MenuFlyout)source.Flyout!;
        menu.Opening += (_, _) =>
        {
            if (Controller is { } controller)
            {
                controller.PropertyChanged -= SourceProjectionChanged;
                controller.PropertyChanged += SourceProjectionChanged;
            }
            ConsoleMenuBuilder.Sources(menu, Controller, this);
        };
        menu.Closed += (_, _) => UnsubscribeSourceMenu(Controller);
    }
    private void SearchChanged(object? sender, TextChangedEventArgs args)
    {
        if (sender is TextBox search && Controller is { } controller && controller.ResetFiltersCommand.CanExecute(null)
            && search.Text != controller.Filter.SearchText)
            controller.SetSearchText(search.Text);
    }
    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _iconHost = TopLevel.GetTopLevel(this);
        if (_iconHost is not null) _iconHost.ScalingChanged += ScalingChanged;
        CenterIcons();
    }
    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearSourceMenu();
        if (_iconHost is not null) _iconHost.ScalingChanged -= ScalingChanged;
        _iconHost = null;
        base.OnDetachedFromVisualTree(e);
    }
    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ConsoleDimensions.NarrowBreakpointProperty) ApplyLayout();
        if (change.Property == ControllerProperty)
        {
            this.FindControl<Button>("ClearSearch")!.Command = new ConsoleClearSearchCommand(Controller);
            UnsubscribeSourceMenu(change.OldValue as ConsoleController);
            ClearSourceMenu();
        }
    }
    private void SourceProjectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ConsoleController.Projection) && Controller is { } controller
            && this.FindControl<Button>("Sources")?.Flyout is MenuFlyout { IsOpen: true } menu)
            ConsoleMenuBuilder.RefreshSources(menu, controller, this);
    }
    private void UnsubscribeSourceMenu(ConsoleController? controller)
    {
        if (controller is not null) controller.PropertyChanged -= SourceProjectionChanged;
    }
    private void ClearSourceMenu()
    {
        UnsubscribeSourceMenu(Controller);
        if (this.FindControl<Button>("Sources")?.Flyout is MenuFlyout menu)
        {
            menu.Hide();
            ConsoleMenuBuilder.Clear(menu);
        }
    }
    private void ScalingChanged(object? sender, EventArgs args) => CenterIcons();
    private void CenterIcons()
    {
        foreach (var level in Enum.GetValues<LogLevel>())
        {
            var icon = (TextBlock)this.FindControl<ToggleButton>("Level" + level)!.Content!;
            TextOptions.SetBaselinePixelAlignment(icon, BaselinePixelAlignment.Unaligned);
            icon.RenderTransform = new TranslateTransform(0, ConsoleGlyphs.VerticalOffset(level, _iconHost?.RenderScaling ?? 1));
        }
    }
    private void ApplyLayout()
    {
        // Resource bindings can publish while InitializeComponent is still populating names.
        if (NarrowSearchRow is null || Dedupe is null) return;
        var narrow = Bounds.Width <= ConsoleDimensions.GetNarrowBreakpoint(this);
        this.FindControl<Grid>("NarrowSearchRow")!.IsVisible = narrow;
        this.FindControl<ToggleButton>("Dedupe")!.IsVisible = !narrow;
    }
}
