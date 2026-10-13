// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>The 40 DIP filter and action row.</summary>
public sealed partial class ConsoleToolbar : UserControl
{
    // UI-thread-only subscription; exists only while attached.
    private TopLevel? _iconHost;
    private readonly ConsoleLevelRowModel _levelRow;
    // Runs the open and close delays; the dispatcher in production, a manual clock in tests.
    private Func<TimeSpan, Action, IDisposable> _schedule = ConsoleLevelRowModel.Run;
    // The parts the host has not closed. Every part is shown today; the width rule below is the only input that hides one.
    private readonly ConsoleToolbarParts _hostParts = ConsoleToolbarParts.All;
    // True while any mouse button is down anywhere in the window, so a drag or a selection never opens the row.
    private bool _buttonDown;
    // True when the press that started was outside the level row while the row was open. The row closes after the release.
    private bool _outsidePress;
    // False while InitializeComponent is still populating names; resource bindings can publish before that ends.
    private bool _ready;

    /// <summary>Defines the controller supplying this view's state and intents.</summary>
    public static readonly StyledProperty<ConsoleController?> ControllerProperty = AvaloniaProperty.Register<ConsoleToolbar, ConsoleController?>(nameof(Controller));
    /// <summary>Gets or sets the controller. The host owns its lifetime.</summary>
    public ConsoleController? Controller { get => GetValue(ControllerProperty); set => SetValue(ControllerProperty, value); }
    /// <summary>Defines the app command to copy all filtered rows.</summary>
    public static readonly StyledProperty<ICommand?> CopyVisibleCommandProperty = AvaloniaProperty.Register<ConsoleToolbar, ICommand?>(nameof(CopyVisibleCommand));
    /// <summary>Gets or sets the app command to copy all filtered rows. The host supplies it, as it does for the header.</summary>
    public ICommand? CopyVisibleCommand { get => GetValue(CopyVisibleCommandProperty); set => SetValue(CopyVisibleCommandProperty, value); }
    /// <summary>Defines the list that the zoom and jump buttons act on.</summary>
    public static readonly StyledProperty<ConsoleListView?> ListProperty = AvaloniaProperty.Register<ConsoleToolbar, ConsoleListView?>(nameof(List));
    /// <summary>Gets or sets the list that the zoom and jump buttons act on. The buttons are disabled while it is null.</summary>
    public ConsoleListView? List { get => GetValue(ListProperty); set => SetValue(ListProperty, value); }

    /// <summary>Initializes the view and its local input routing.</summary>
    public ConsoleToolbar()
    {
        InitializeComponent();
        _ready = true;
        _levelRow = new ConsoleLevelRowModel((delay, action) => _schedule(delay, action), HoverAllowed);
        _levelRow.Changed += (_, _) => ApplyLayout();
        _ = new ConsoleControllerAttachment(this, ControllerProperty);
        this.FindControl<Button>("ClearSearch")!.Command = new ConsoleClearSearchCommand(Controller);
        SizeChanged += (_, _) => ApplyLayout();
        InitializeLevelRow();
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

    // Test seam: runs the level row delays on the given clock.
    internal void UseClock(TimeProvider clock)
        => _schedule = (delay, action) => clock.CreateTimer(_ => action(), null, delay, Timeout.InfiniteTimeSpan);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _iconHost = TopLevel.GetTopLevel(this);
        if (_iconHost is not null)
        {
            _iconHost.ScalingChanged += ScalingChanged;
            _iconHost.AddHandler(PointerPressedEvent, HostPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            _iconHost.AddHandler(PointerReleasedEvent, HostPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        CenterIcons();
    }
    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearSourceMenu();
        _levelRow.Close();
        _buttonDown = false;
        _outsidePress = false;
        if (_iconHost is not null)
        {
            _iconHost.ScalingChanged -= ScalingChanged;
            _iconHost.RemoveHandler(PointerPressedEvent, HostPointerPressed);
            _iconHost.RemoveHandler(PointerReleasedEvent, HostPointerReleased);
        }
        _iconHost = null;
        base.OnDetachedFromVisualTree(e);
    }
    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!_ready) return;
        if (change.Property == ConsoleDimensions.NarrowBreakpointProperty
            || change.Property == ConsoleDimensions.WideBreakpointProperty) ApplyLayout();
        if (change.Property == ControllerProperty)
        {
            this.FindControl<Button>("ClearSearch")!.Command = new ConsoleClearSearchCommand(Controller);
            UnsubscribeSourceMenu(change.OldValue as ConsoleController);
            ClearSourceMenu();
            _levelRow?.Close();
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
}
