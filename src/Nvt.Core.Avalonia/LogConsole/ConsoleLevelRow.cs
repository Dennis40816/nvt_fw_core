// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Nvt.Core.Avalonia.LogConsole;

// Open, pin and close rules of the level row. It holds state only; the toolbar supplies the clock and the guards.
// UI thread only.
internal sealed class ConsoleLevelRowModel(Func<TimeSpan, Action, IDisposable> schedule, Func<bool> hoverAllowed)
{
    // The pointer must rest on the label this long before the row opens, so passing over it opens nothing.
    internal static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(300);
    // The pointer may leave the row this long before it closes, so a move between the label and the items never flickers.
    internal static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(500);

    private IDisposable? _timer;

    internal bool IsOpen { get; private set; }
    // A pinned row stays open until Escape, a press outside, a second activation or focus leaving.
    internal bool IsPinned { get; private set; }
    internal event EventHandler? Changed;

    // A mouse or pen pointer entered the label. Touch never opens by hover, so the toolbar does not call this for it.
    internal void LabelEntered()
    {
        CancelTimer();
        if (IsOpen) return;
        _timer = schedule(OpenDelay, () =>
        {
            _timer = null;
            if (!IsOpen && hoverAllowed()) Set(true, false);
        });
    }

    // The pointer left the label before the open delay ended: the pending open is cancelled.
    // An open row keeps its close timer, which belongs to the toolbar exit.
    internal void LabelExited()
    {
        if (!IsOpen) CancelTimer();
    }

    // Keyboard entry into an open row pins it, so a focused toggle cannot vanish when the pointer leaves.
    internal void Pin()
    {
        if (!IsOpen) return;
        CancelTimer();
        Set(true, true);
    }

    // The pointer is back inside the row: a pending close is cancelled.
    internal void RowEntered()
    {
        if (IsOpen) CancelTimer();
    }

    internal void RowExited()
    {
        CancelTimer();
        if (!IsOpen || IsPinned) return;
        _timer = schedule(CloseDelay, () =>
        {
            _timer = null;
            if (IsOpen && !IsPinned) Set(false, false);
        });
    }

    // Click, tap, Enter, Space or Down on the label: a hover-opened row is pinned, a pinned row closes, a closed row opens pinned.
    internal void Toggle()
    {
        CancelTimer();
        if (IsOpen && IsPinned) Set(false, false);
        else Set(true, true);
    }

    // Returns true when the key closed the row.
    internal bool Escape()
    {
        if (!IsOpen) return false;
        Close();
        return true;
    }

    // Closes the row and cancels any pending timer. The toolbar calls this when it detaches, so no timer outlives the view.
    internal void Close()
    {
        CancelTimer();
        if (IsOpen) Set(false, false);
    }

    private void CancelTimer()
    {
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    private void Set(bool open, bool pinned)
    {
        if (IsOpen == open && IsPinned == pinned) return;
        IsOpen = open;
        IsPinned = pinned;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal static IDisposable Run(TimeSpan delay, Action action) => DispatcherTimer.RunOnce(action, delay);
}

// The label button of the level row. It is a button that can report expanded or collapsed to assistive technology.
internal sealed class ConsoleLevelLabel : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    internal static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<ConsoleLevelLabel, bool>(nameof(IsOpen));
    internal bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    // Assistive technology asked to open or close the row.
    internal event EventHandler<bool>? OpenRequested;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty && change.OldValue is bool before && change.NewValue is bool after)
            (ControlAutomationPeer.FromElement(this) as Peer)?.Refresh(before, after);
    }

    internal void RequestOpen(bool open) => OpenRequested?.Invoke(this, open);

    private sealed class Peer(ConsoleLevelLabel owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public bool ShowsMenu => false;
        public void Expand() => owner.RequestOpen(true);
        public void Collapse() => owner.RequestOpen(false);
        protected override object? GetProviderCore(Type providerType)
            => providerType == typeof(IExpandCollapseProvider) ? this : base.GetProviderCore(providerType);
        internal void Refresh(bool before, bool after)
            => RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                before ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                after ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }
}
