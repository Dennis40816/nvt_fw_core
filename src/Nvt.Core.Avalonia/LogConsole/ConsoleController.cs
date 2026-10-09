// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Nvt.Core.Avalonia.Threading;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>Owns console inputs and leased projections on the registered Core UI thread.</summary>
/// <remarks>Create, invoke intents, and dispose on the UI thread. The app owns the store.
/// Consumers borrow Projection until replacement; bindings receive the replacement before old leases retire.</remarks>
public sealed class ConsoleController : INotifyPropertyChanged, IDisposable
{
    private readonly LogStore _store;
    private readonly Dispatcher _dispatcher;
    // _workGate protects notification scheduling, disposal, and the single pending operation.
    private readonly object _workGate = new();
    private DispatcherOperation? _pending;
    private bool _refreshRequested;
    private bool _disposed;
    // All presentation fields and retired leases are UI-thread-only.
    private ConsoleFilter _filter = new();
    private ConsoleViewState _viewState = new();
    private ConsoleProjection _projection;
    private ConsoleProjection? _retired;
    private ConsoleExportOptions _exportOptions = new();
    private Exception? _refreshError;
    // UI-thread-only nesting; Dispose defers lease release until all active callbacks return.
    private int _notificationDepth;

    /// <summary>Subscribes before startup capture and enables notifications; does not take store ownership.</summary>
    public ConsoleController(LogStore store, ImmutableArray<ConsoleSource> sources,
        ConsoleProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!UiThread.IsCurrent(out var dispatcher, out _))
            throw new InvalidOperationException("Create the console on the registered Core UI thread.");
        _dispatcher = dispatcher!;
        _store = store;
        Options = (options ?? new()) with { SourceRegistry = sources };
        ArgumentNullException.ThrowIfNull(Options.RelativeTimeTemplate);
        ArgumentNullException.ThrowIfNull(Options.Culture);
        try { _ = string.Format(Options.Culture, Options.RelativeTimeTemplate, 0.0); }
        catch (FormatException error)
        {
            throw new ArgumentException("RelativeTimeTemplate must be a valid composite format with placeholder 0.", nameof(options), error);
        }
        ToggleLevelCommand = Command<LogLevel>(level =>
        {
            if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
            var removed = _filter.EnabledLevels.Remove(level);
            SetFilter(_filter with { EnabledLevels = ReferenceEquals(removed, _filter.EnabledLevels)
                ? _filter.EnabledLevels.Add(level) : removed });
        });
        ToggleOnlyMatchesCommand = Command(() => SetFilter(_filter with { OnlyMatches = !_filter.OnlyMatches }));
        ToggleDedupeCommand = Command(() => SetFilter(_filter with { Deduplicate = !_filter.Deduplicate }));
        SetTimeModeCommand = Command<ConsoleTimeMode>(mode =>
        {
            if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            SetFilter(_filter with { TimeMode = mode });
        });
        ClearCommand = Command(_store.Clear);
        ResetFiltersCommand = Command(() => SetFilter(new ConsoleFilter { TimeMode = _filter.TimeMode }));
        ToggleExportTimeCommand = Command(() => SetExportOptions(_exportOptions with { IncludeTime = !_exportOptions.IncludeTime }));
        ToggleExportLevelCommand = Command(() => SetExportOptions(_exportOptions with { IncludeLevel = !_exportOptions.IncludeLevel }));
        _store.Changed += StoreChanged;
        try
        {
            using var snapshot = _store.CaptureSnapshot();
            _projection = ConsoleProjector.Project(snapshot, _filter, _viewState, Options);
            _store.SetReady(true);
        }
        catch
        {
            _store.Changed -= StoreChanged;
            DispatcherOperation? pending;
            lock (_workGate) { _disposed = true; pending = _pending; _pending = null; }
            pending?.Abort();
            _projection?.Dispose();
            throw;
        }
    }

    /// <summary>Publishes input and projection replacements on the UI thread.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Gets the current immutable filter. Time mode lives here, as in the data contract.</summary>
    public ConsoleFilter Filter => _filter;
    /// <summary>Gets the current immutable reading, expansion, and selection state.</summary>
    public ConsoleViewState ViewState => _viewState;
    /// <summary>Gets injected source, template, culture, and zone inputs.</summary>
    public ConsoleProjectionOptions Options { get; }
    /// <summary>Gets the current borrowed projection; consumers must not dispose it.</summary>
    public ConsoleProjection Projection => _projection;
    /// <summary>Gets the independent export flags used by app adapters.</summary>
    public ConsoleExportOptions ExportOptions => _exportOptions;
    /// <summary>Gets the most recent refresh failure; the next successful refresh clears it.</summary>
    public Exception? RefreshError => _refreshError;
    /// <summary>Toggles one level, including levels with zero events.</summary>
    public ICommand ToggleLevelCommand { get; }
    /// <summary>Toggles search filtering while preserving highlights.</summary>
    public ICommand ToggleOnlyMatchesCommand { get; }
    /// <summary>Toggles grouping of duplicate events.</summary>
    public ICommand ToggleDedupeCommand { get; }
    /// <summary>Selects an Absolute, Relative, or Hidden time mode.</summary>
    public ICommand SetTimeModeCommand { get; }
    /// <summary>Clears the app-owned store through its generation fence.</summary>
    public ICommand ClearCommand { get; }
    /// <summary>Resets filtering and dedupe, retaining time presentation and reading state.</summary>
    public ICommand ResetFiltersCommand { get; }
    /// <summary>Toggles inclusion of UTC timestamps in export.</summary>
    public ICommand ToggleExportTimeCommand { get; }
    /// <summary>Toggles inclusion of levels in export.</summary>
    public ICommand ToggleExportLevelCommand { get; }

    /// <summary>Selects ordinal source IDs. An empty set selects all sources.</summary>
    public void SetSelectedSources(IEnumerable<string> sources)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(sources);
        SetFilter(_filter with { SelectedSources = sources.ToImmutableHashSet(StringComparer.Ordinal) });
    }

    /// <summary>Sets the literal query; whitespace alone clears the search condition.</summary>
    public void SetSearchText(string? text)
    {
        VerifyActive();
        SetFilter(_filter with { SearchText = string.IsNullOrWhiteSpace(text) ? string.Empty : text });
    }

    /// <summary>Accepts immutable intents from the list. The list never writes controller state directly.</summary>
    public void RequestViewState(ConsoleViewState state)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(state);
        if (_viewState == state) return;
        _viewState = state;
        Schedule(true);
        Notify(nameof(ViewState));
    }

    /// <summary>Captures a pause at the current projection without changing selection or expansion.</summary>
    public void Pause(ConsoleRowId? rowId = null, int textOffset = 0, double pixelOffset = 0)
    {
        VerifyActive();
        using var snapshot = _store.CaptureSnapshot();
        RequestViewState(_viewState.Pause(_projection, rowId, textOffset, pixelOffset, snapshot.CapturedAt));
    }

    /// <summary>Resumes following; this is the controller intent for jump to latest.</summary>
    public void Resume()
    {
        VerifyActive();
        RequestViewState(_viewState.Resume());
    }

    private RelayCommand Command(Action execute) => new(() => { VerifyActive(); execute(); }, IsActive);
    private RelayCommand<T> Command<T>(Action<T> execute) where T : struct
        => new(value => { VerifyActive(); execute(value); }, _ => IsActive());
    private bool IsActive() { lock (_workGate) return !_disposed; }
    private void VerifyActive()
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(!IsActive(), this);
    }
    private void SetFilter(ConsoleFilter filter)
    {
        if (_filter == filter) return;
        _filter = filter;
        Schedule(true);
        Notify(nameof(Filter));
    }
    private void SetExportOptions(ConsoleExportOptions options)
    {
        _exportOptions = options;
        Notify(nameof(ExportOptions));
    }
    private void StoreChanged(object? sender, LogChangeSet changes) => Schedule(true);
    private void Schedule(bool refresh)
    {
        lock (_workGate)
        {
            if (_disposed) return;
            _refreshRequested |= refresh;
            _pending ??= _dispatcher.InvokeAsync(Drain, DispatcherPriority.Background);
        }
    }
    private void Drain()
    {
        bool refresh;
        lock (_workGate)
        {
            _pending = null;
            if (_disposed) return;
            // A subscriber may pump the dispatcher. Keep its borrowed leases pinned.
            if (_notificationDepth != 0) return;
            refresh = _refreshRequested;
            _refreshRequested = false;
        }
        // Background priority lets binding/layout/render work consume the replacement first.
        _retired?.Dispose();
        _retired = null;
        if (!refresh) return;
        ConsoleProjection? next = null;
        try
        {
            using var snapshot = _store.CaptureSnapshot();
            next = ConsoleProjector.Project(snapshot, _filter, _viewState, Options);
            if (!IsActive()) { next.Dispose(); return; }
            var remapped = _viewState.Remap(_projection, next);
            if (_viewState.Follow is ConsoleFollow.Paused && _projection.Deduplicate != next.Deduplicate)
                next = ConsoleProjectionTransfer.WithPausedOrder(next, remapped);
            // Clear advances admission generation before the writer publishes its reset.
            if (!IsActive() || !_store.IsCurrent(snapshot.Generation)) { next.Dispose(); return; }
            _retired = _projection;
            _projection = next;
            _viewState = remapped;
        }
        catch (Exception error)
        {
            next?.Dispose();
            try
            {
                if (SetRefreshError(error)) Notify(nameof(RefreshError));
            }
            finally { Schedule(false); }
            return;
        }
        var errorChanged = SetRefreshError(null);
        try { Notify(nameof(ViewState)); }
        finally
        {
            try { Notify(nameof(Projection)); }
            finally
            {
                try
                {
                    if (errorChanged) Notify(nameof(RefreshError));
                }
                finally { Schedule(false); }
            }
        }
    }
    private bool SetRefreshError(Exception? error)
    {
        if (ReferenceEquals(_refreshError, error)) return false;
        _refreshError = error;
        return true;
    }
    private void Notify(string name)
    {
        if (!IsActive()) return;
        _notificationDepth++;
        try
        {
            var args = new PropertyChangedEventArgs(name);
            foreach (PropertyChangedEventHandler handler in PropertyChanged?.GetInvocationList() ?? [])
            {
                if (!IsActive()) break;
                handler(this, args);
            }
        }
        finally
        {
            if (--_notificationDepth == 0)
            {
                if (IsActive()) Schedule(false);
                else ReleaseProjections();
            }
        }
    }

    private void ReleaseProjections()
    {
        _retired?.Dispose();
        _retired = null;
        _projection.Dispose();
    }

    /// <summary>Unsubscribes, aborts queued work, and releases both owned projections. Safe to repeat.</summary>
    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        DispatcherOperation? pending;
        lock (_workGate)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _pending;
            _pending = null;
            _refreshRequested = false;
        }
        _store.Changed -= StoreChanged;
        pending?.Abort();
        if (_notificationDepth == 0) ReleaseProjections();
        foreach (var command in new[] { ToggleLevelCommand, ToggleOnlyMatchesCommand, ToggleDedupeCommand,
            SetTimeModeCommand, ClearCommand, ResetFiltersCommand, ToggleExportTimeCommand, ToggleExportLevelCommand })
            ((IRelayCommand)command).NotifyCanExecuteChanged();
    }
}
