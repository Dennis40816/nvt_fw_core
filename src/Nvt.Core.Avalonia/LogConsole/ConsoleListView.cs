// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Threading;
using Nvt.Core.LogConsole;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Nvt.Core.Avalonia.Tests")]

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>A recycling console list. The caller owns projection leases and accepts state requests.</summary>
/// <remarks>Load LogConsole/ConsoleListStyles.axaml. Set inputs on the UI thread; this view never projects data.</remarks>
public sealed class ConsoleListView : TemplatedControl
{
    /// <summary>Defines <see cref="Projection"/>.</summary>
    public static readonly StyledProperty<ConsoleProjection?> ProjectionProperty =
        AvaloniaProperty.Register<ConsoleListView, ConsoleProjection?>(nameof(Projection));
    /// <summary>Defines <see cref="ViewState"/>.</summary>
    public static readonly StyledProperty<ConsoleViewState> ViewStateProperty =
        AvaloniaProperty.Register<ConsoleListView, ConsoleViewState>(nameof(ViewState), new());
    /// <summary>Defines <see cref="TimeOptions"/>.</summary>
    public static readonly StyledProperty<ConsoleProjectionOptions> TimeOptionsProperty =
        AvaloniaProperty.Register<ConsoleListView, ConsoleProjectionOptions>(nameof(TimeOptions), new());
    /// <summary>Defines <see cref="TimeMode"/>.</summary>
    public static readonly StyledProperty<ConsoleTimeMode> TimeModeProperty =
        AvaloniaProperty.Register<ConsoleListView, ConsoleTimeMode>(nameof(TimeMode), ConsoleTimeMode.Absolute);

    // UI thread only. Template parts outlive attachment; Session owns attachment-scoped handlers and work.
    private Parts? _parts;
    private Session? _session;

    /// <summary>Gets or sets the caller-owned, frozen projection. Keep it alive until the next assignment.</summary>
    public ConsoleProjection? Projection { get => GetValue(ProjectionProperty); set => SetValue(ProjectionProperty, value); }
    /// <summary>Gets or sets the single caller-owned reading and expansion state.</summary>
    public ConsoleViewState ViewState { get => GetValue(ViewStateProperty); set => SetValue(ViewStateProperty, value); }
    /// <summary>Gets or sets explicit time culture, template and zone.</summary>
    public ConsoleProjectionOptions TimeOptions { get => GetValue(TimeOptionsProperty); set => SetValue(TimeOptionsProperty, value); }
    /// <summary>Gets or sets the time column mode. Relative text always uses Projection.TimeBase.</summary>
    public ConsoleTimeMode TimeMode { get => GetValue(TimeModeProperty); set => SetValue(TimeModeProperty, value); }
    /// <summary>Requests a replacement state without assigning ViewState. The caller decides whether to accept it.</summary>
    public event EventHandler<ConsoleViewState>? ViewStateRequested;

    /// <summary>Requests explicit resume. A controller may bind its Ctrl+End command to this method.</summary>
    public void JumpToLatest() => Request(ViewState.Resume());

    internal Session? AttachmentSession => _session;
    internal ConsoleReadingAnchor? PendingReadingAnchor => _session?.ReadingAnchor;
    internal bool UserScrollPending => _session?.UserScrollPending == true;

    internal void ScrollProgrammatically(Action scroll)
    {
        if (_session is { } session) session.ScrollProgrammatically(scroll);
        else scroll();
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _session?.Detach();
        _session = null;
        _parts?.Host.Release();
        base.OnApplyTemplate(e);
        _parts = new(e.NameScope.Find<ConsoleItemsHost>("PART_ItemsHost")!,
            e.NameScope.Find<ScrollViewer>("PART_ScrollViewer")!,
            e.NameScope.Find<Button>("PART_Jump")!, e.NameScope.Find<TextBlock>("PART_Retention")!);
        if (TopLevel.GetTopLevel(this) is not null) AttachSession();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachSession();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _session?.Detach();
        _session = null;
        _parts?.Host.Release();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewStateProperty)
        {
            var previous = change.GetOldValue<ConsoleViewState>();
            var current = change.GetNewValue<ConsoleViewState>();
            _session?.Apply(ReferenceEquals(previous.Follow, current.Follow)
                || _session.AcceptRequestedAnchor(current.Follow));
        }
        else if (change.Property == ProjectionProperty || change.Property == TimeOptionsProperty || change.Property == TimeModeProperty)
            _session?.Apply(true);
    }

    private void AttachSession()
    {
        if (_parts is null || _session is not null) return;
        UiThread.RegisterRunningDispatcher(Dispatcher);
        _session = new(this, _parts);
        _session.Apply();
    }

    private void Request(ConsoleViewState state)
    {
        _session?.CancelDeferredScroll();
        _session?.RecordRequest(state);
        ViewStateRequested?.Invoke(this, state);
    }

    private void Toggle(ConsoleRowId id)
    {
        var state = _session?.CaptureReadingState() ?? ViewState;
        var collapsed = state.ExpandedIds.Remove(id);
        Request(state with { ExpandedIds = ReferenceEquals(collapsed, state.ExpandedIds) ? state.ExpandedIds.Add(id) : collapsed });
    }

    internal sealed record Parts(ConsoleItemsHost Host, ScrollViewer Scroll, Button Jump, TextBlock Retention);

    // UI-thread-only attachment state. Queued work captures a weak Session, never a view or projection lease.
    internal sealed class Session
    {
        private const int RequestedAnchorLimit = 32;
        private readonly ConsoleListView _view;
        private readonly Parts _parts;
        private bool _detached;
        private ConsoleReadingAnchor? _restore;
        private DispatcherOperation? _pending;
        private int _suppress;
        private Vector _lastObservedOffset;
        private readonly List<ConsoleReadingAnchor> _requestedAnchors = [];
        // Pending user intent only; capture both coordinates and caller state when delivered.
        private DeferredScroll? _deferredScroll;
        internal DispatcherOperation? PendingOperation => _pending;
        internal ConsoleReadingAnchor? ReadingAnchor => _restore;
        internal bool UserScrollPending => _deferredScroll is not null;

        internal void ScrollProgrammatically(Action scroll)
        {
            _suppress++;
            try { scroll(); }
            finally
            {
                _lastObservedOffset = _parts.Host.Offset;
                _suppress--;
            }
        }

        internal Session(ConsoleListView view, Parts parts)
        {
            _view = view;
            _parts = parts;
            _lastObservedOffset = parts.Host.Offset;
            parts.Host.ScrollInvalidated += Scrolled;
            parts.Jump.Click += Jump;
            parts.Host.ToggleRequested += Toggle;
            parts.Host.LayoutUpdated += LaidOut;
            view.ResourcesChanged += ResourcesUpdated;
            view.ActualThemeVariantChanged += ResourcesUpdated;
        }

        internal void RecordRequest(ConsoleViewState state)
        {
            if (state.Follow is not ConsoleFollow.Paused paused) return;
            if (_requestedAnchors.Count == RequestedAnchorLimit) _requestedAnchors.RemoveAt(0);
            _requestedAnchors.Add(paused.Anchor);
        }

        internal bool AcceptRequestedAnchor(ConsoleFollow follow)
        {
            // ImmutableArray equality compares backing-array identity before inspecting copied orders.
            var index = follow is ConsoleFollow.Paused paused
                ? _requestedAnchors.FindIndex(requested => requested with { RowOrder = paused.Anchor.RowOrder } == paused.Anchor
                    && (requested.RowOrder == paused.Anchor.RowOrder
                        || !requested.RowOrder.IsDefault && !paused.Anchor.RowOrder.IsDefault
                            && requested.RowOrder.SequenceEqual(paused.Anchor.RowOrder)))
                : -1;
            if (index < 0)
            {
                _requestedAnchors.Clear();
                CancelDeferredScroll();
                return false;
            }
            _requestedAnchors.RemoveRange(0, index + 1);
            return true;
        }

        internal void CancelDeferredScroll()
        {
            _deferredScroll?.Operation.Abort();
            _deferredScroll = null;
        }

        private bool DeferScroll(ScrollIntent intent)
        {
            if (_deferredScroll is { } pending)
            {
                _deferredScroll = pending with { Intent = intent };
                return true;
            }
            if (!_parts.Host.IsMeasuring) return false;
            var weak = new WeakReference<Session>(this);
            var operation = _view.Dispatcher.InvokeAsync(() =>
            {
                if (!weak.TryGetTarget(out var session) || session._detached
                    || session._deferredScroll is not { } request) return;
                // Finish pending projection/reflow restoration before capturing the screen.
                session._parts.Host.UpdateLayout();
                session.Restore();
                if (session._deferredScroll is not { } current || current.Operation != request.Operation) return;
                session._pending?.Abort();
                session._pending = null;
                session._deferredScroll = null;
                session.RequestScrollState(current.Intent);
            }, DispatcherPriority.Loaded);
            _deferredScroll = new(intent, operation);
            return true;
        }

        private void RequestScrollState(ScrollIntent intent)
        {
            if (_view.Projection is null) return;
            if (intent == ScrollIntent.Resume)
            {
                if (_view.ViewState.Follow is ConsoleFollow.Paused || _requestedAnchors.Count != 0) _view.JumpToLatest();
            }
            else if (!_view.Projection.Rows.IsEmpty) _view.Request(CaptureReadingState());
        }

        internal ConsoleViewState CaptureReadingState()
        {
            var state = _view.ViewState;
            if (_view.Projection is not { } projection) return state;
            // One capture path for scrolling, toggles and projection application. A pending layout
            // anchor wins until a user scroll explicitly replaces it with fresh coordinates.
            var anchor = _restore ?? _parts.Host.CaptureAnchor();
            if (state.Follow is ConsoleFollow.Paused)
                return anchor is { } current ? WithReadingAnchor(state, current) : state;
            return anchor is { } reading
                ? state with { Follow = new ConsoleFollow.Paused(reading, projection.CapturedAt) }
                : state.Pause(projection);
        }

        private static ConsoleViewState WithReadingAnchor(ConsoleViewState state, ConsoleReadingAnchor current)
        {
            var paused = (ConsoleFollow.Paused)state.Follow;
            // Reading coordinates move; the pause snapshot and relative-time base remain frozen.
            return state with
            {
                Follow = new ConsoleFollow.Paused(paused.Anchor with
                {
                    RowId = current.RowId,
                    Sequence = current.Sequence,
                    TextOffset = current.TextOffset,
                    PixelOffset = current.PixelOffset,
                }, paused.PausedAt)
            };
        }

        /// <param name="preservePosition">True keeps the live reading position instead of the pause anchor.</param>
        internal void Apply(bool preservePosition = false)
        {
            if (_detached) return;
            _suppress++;
            try
            {
                _restore = preservePosition && UserScrollPending && _view.Projection is not null
                    ? ((ConsoleFollow.Paused)CaptureReadingState().Follow).Anchor
                    : _view.ViewState.Follow is ConsoleFollow.Paused paused
                        ? preservePosition ? ((ConsoleFollow.Paused)CaptureReadingState().Follow).Anchor : paused.Anchor : null;
                _parts.Host.Synchronize(_view);
                _parts.Jump.IsVisible = _view.ViewState.Follow is ConsoleFollow.Paused;
                _parts.Retention.IsVisible = _view.ViewState.Follow is ConsoleFollow.Paused
                    && _view.Projection is { EvictedCount: > 0 };
                UpdateText();
                Schedule();
            }
            finally { _suppress--; }
        }

        private void UpdateText()
        {
            var count = _view.Projection?.NewSincePauseCount ?? 0;
            var key = count == 1 ? "Nvt.Console.List.JumpToLatestOne" : "Nvt.Console.List.JumpToLatestMany";
            _parts.Jump.Content = ConsoleListText.Format(_view, key, count);
            var evicted = _view.Projection?.EvictedCount ?? 0;
            var retentionKey = evicted == 1 ? "Nvt.Console.List.RetentionOne" : "Nvt.Console.List.RetentionFormat";
            _parts.Retention.Text = ConsoleListText.Format(_view, retentionKey, evicted);
        }

        private void ResourcesUpdated(object? sender, EventArgs e) => UpdateText();
        private void ResourcesUpdated(object? sender, ResourcesChangedEventArgs e) => UpdateText();

        private void Schedule()
        {
            if (_pending is not null || !UiThread.TryGetRunningDispatcher(out var dispatcher)) return;
            var weak = new WeakReference<Session>(this);
            _pending = dispatcher!.InvokeAsync(() =>
            {
                if (!weak.TryGetTarget(out var session) || session._detached) return;
                session._parts.Host.UpdateLayout();
                session.Restore();
                session._pending = null;
            }, DispatcherPriority.Loaded);
        }

        private void LaidOut(object? sender, EventArgs e) => Restore();

        private void Restore()
        {
            if (_detached || _parts.Host.Viewport.Height <= 0) return;
            _suppress++;
            try
            {
                if (_view.ViewState.Follow is ConsoleFollow.Following && !UserScrollPending)
                {
                    // Only a projection/state application follows; ordinary user scrolling is never undone.
                    if (_pending is not null) _parts.Host.ScrollToEnd();
                }
                else if (_restore is { } anchor)
                {
                    var restored = _parts.Host.RestoreAnchor(anchor, _view.Projection?.ResolvedAnchorId);
                    _restore = null;
                    if (restored is { } current && current.RowId != anchor.RowId
                        && !UserScrollPending && _view.ViewState.Follow is ConsoleFollow.Paused)
                        _view.Request(WithReadingAnchor(_view.ViewState, current));
                }
            }
            finally { _suppress--; }
        }

        private void Scrolled(object? sender, EventArgs e)
        {
            // Observe every notification, including geometry and suppressed writes. The host reports
            // user offsets before layout can correct heights; repeated geometry never creates intent.
            var offset = _parts.Host.Offset;
            var changed = offset != _lastObservedOffset;
            _lastObservedOffset = offset;
            if (_suppress != 0 || !changed || _view.Projection is null) return;
            _restore = null;
            _parts.Host.PreserveUserScrollDuringMeasure();
            if (_parts.Host.AtEnd && _view.ViewState.Follow is ConsoleFollow.Following && _requestedAnchors.Count == 0)
            {
                CancelDeferredScroll();
                return;
            }
            var intent = _parts.Host.AtEnd ? ScrollIntent.Resume : ScrollIntent.Pause;
            if (!DeferScroll(intent)) RequestScrollState(intent);
        }

        private void Jump(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => _view.JumpToLatest();
        private void Toggle(ConsoleRowId id) => _view.Toggle(id);

        internal void Detach()
        {
            if (_detached) return;
            _detached = true;
            _pending?.Abort();
            _pending = null;
            CancelDeferredScroll();
            _requestedAnchors.Clear();
            _parts.Host.ScrollInvalidated -= Scrolled;
            _parts.Jump.Click -= Jump;
            _parts.Host.ToggleRequested -= Toggle;
            _parts.Host.LayoutUpdated -= LaidOut;
            _view.ResourcesChanged -= ResourcesUpdated;
            _view.ActualThemeVariantChanged -= ResourcesUpdated;
        }

        private enum ScrollIntent { Pause, Resume }
        private sealed record DeferredScroll(ScrollIntent Intent, DispatcherOperation Operation);
    }
}
