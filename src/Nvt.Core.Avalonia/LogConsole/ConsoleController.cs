// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
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
    private readonly Lock _workGate = new();
    private DispatcherOperation? _pending;
    private bool _refreshRequested;
    private bool _refreshRetryAvailable;
    private bool _disposed;
    // _workGate protects the one command state version, including cross-thread store notifications.
    private long _stateVersion;
    internal long CommandStateVersion { get { lock (_workGate) return _stateVersion; } }
    // All presentation fields and retired leases are UI-thread-only.
    private int _commandProjectionCaptures;
    internal int CommandProjectionCaptures => _commandProjectionCaptures;
    private ConsoleFilter _filter = new();
    private ConsoleViewState _viewState = new();
    private ProjectionLease _current = null!;
    private ConsoleProjection _projection => _current.Projection;
    private ProjectionLease? _retired;
    private ConsoleLinkCache? _linkCache = new(maxEntries: 512, maxSpans: 4096, maxTargetCharacters: 131072);
    internal object? LinkCacheIdentity => _linkCache;
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
        ValidateSources(sources, options);
        Options = (options ?? new()) with { SourceRegistry = sources };
        ArgumentNullException.ThrowIfNull(Options.RelativeTimeTemplate);
        ArgumentNullException.ThrowIfNull(Options.Culture);
        try
        {
            _ = ConsoleTimeFormatter.Format(default, default, ConsoleTimeMode.Relative,
                Options.RelativeTimeTemplate, Options.Culture);
        }
        catch (FormatException error)
        {
            throw new ArgumentException("RelativeTimeTemplate must be a valid composite format with placeholder 0.", nameof(options), error);
        }
        InitializeCommands();
        RowCommands = new(this, notifications => PublishNotifications(notifications));
        _store.Changed += StoreChanged;
        try
        {
            LogSnapshot? snapshot = _store.CaptureSnapshot();
            try
            {
                _current = new(ConsoleProjector.Project(snapshot, _filter, _viewState, Options), snapshot);
                _linkCache.Synchronize(snapshot);
                snapshot = null; // Ownership moved into the paired lease.
            }
            finally { snapshot?.Dispose(); }
            _store.SetReady(true);
        }
        catch
        {
            _store.Changed -= StoreChanged;
            DispatcherOperation? pending;
            lock (_workGate) { _disposed = true; pending = _pending; _pending = null; }
            pending?.Abort();
            _current?.Dispose();
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
    /// <summary>Gets the app-owned, nonblocking link adapter. Configure before binding the list.</summary>
    public IConsoleLinkOpener? LinkOpener { get; init; }
    /// <summary>Gets the app-owned, nonblocking clipboard adapter. Configure before binding the list.</summary>
    public IConsoleClipboard? Clipboard { get; init; }
    /// <summary>Copies visible selected rows through Clipboard, bounded to 65,536 UTF-16 characters.</summary>
    public ICommand CopySelectionCommand => RowCommands.CopySelection;
    internal ConsoleRowCommands RowCommands { get; }
    /// <summary>Gets the most recent refresh failure; the next successful refresh clears it.</summary>
    public Exception? RefreshError => _refreshError;
    /// <summary>Toggles one level, including levels with zero events.</summary>
    public ICommand ToggleLevelCommand { get; private set; } = null!;
    /// <summary>Toggles search filtering while preserving highlights.</summary>
    public ICommand ToggleOnlyMatchesCommand { get; private set; } = null!;
    /// <summary>Toggles grouping of duplicate events.</summary>
    public ICommand ToggleDedupeCommand { get; private set; } = null!;
    /// <summary>Selects an Absolute, Relative, or Hidden time mode.</summary>
    public ICommand SetTimeModeCommand { get; private set; } = null!;
    /// <summary>Clears the app-owned store through its generation fence.</summary>
    public ICommand ClearCommand { get; private set; } = null!;
    /// <summary>Resets filtering and dedupe, retaining time presentation and reading state.</summary>
    public ICommand ResetFiltersCommand { get; private set; } = null!;
    /// <summary>Toggles inclusion of UTC timestamps in export.</summary>
    public ICommand ToggleExportTimeCommand { get; private set; } = null!;
    /// <summary>Toggles inclusion of levels in export.</summary>
    public ICommand ToggleExportLevelCommand { get; private set; } = null!;

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
        var selectionChanged = !ReferenceEquals(_viewState.Selection, state.Selection);
        _viewState = state;
        InvalidateCommandState();
        Schedule(true);
        Notify(nameof(ViewState));
        if (selectionChanged) ((IRelayCommand)CopySelectionCommand).NotifyCanExecuteChanged();
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

    internal void FilterByLevel(LogLevel level) => SetFilter(_filter with { EnabledLevels = [level] });

    private static void ValidateSources(ImmutableArray<ConsoleSource> sources, ConsoleProjectionOptions? options)
    {
        if (sources.IsDefault) throw new ArgumentException("Sources must be initialized.", nameof(sources));
        if (options is { SourceRegistry.IsDefault: true }) throw new ArgumentException("SourceRegistry must be initialized.", nameof(options));
        if (options is { SourceRegistry.IsEmpty: false } && !sources.SequenceEqual(options.SourceRegistry))
            throw new ArgumentException("Sources and SourceRegistry must agree when both are supplied.", nameof(options));
    }

    private void InitializeCommands()
    {
        ToggleLevelCommand = Command<LogLevel>(level =>
        {
            if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
            var removed = _filter.EnabledLevels.Remove(level);
            SetFilter(_filter with
            {
                EnabledLevels = ReferenceEquals(removed, _filter.EnabledLevels)
                ? _filter.EnabledLevels.Add(level) : removed
            });
        });
        ToggleOnlyMatchesCommand = Command(() => SetFilter(_filter with { OnlyMatches = !_filter.OnlyMatches }));
        ToggleDedupeCommand = Command(() => SetFilter(_filter with { Deduplicate = !_filter.Deduplicate }));
        SetTimeModeCommand = Command<ConsoleTimeMode>(mode =>
        {
            if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            SetFilter(_filter with { TimeMode = mode });
        });
        ClearCommand = Command(() => { InvalidateCommandState(); _store.Clear(); });
        ResetFiltersCommand = Command(() => SetFilter(new ConsoleFilter { TimeMode = _filter.TimeMode }));
        ToggleExportTimeCommand = Command(() => SetExportOptions(_exportOptions with { IncludeTime = !_exportOptions.IncludeTime }));
        ToggleExportLevelCommand = Command(() => SetExportOptions(_exportOptions with { IncludeLevel = !_exportOptions.IncludeLevel }));
    }

    internal ConsoleLinkIndex GetLinks(ConsoleRow row) => _linkCache!.GetLinks(_current.Snapshot, row);

    internal ConsoleLinkIndex GetCommandLinks(ConsoleRow row)
    {
        using var snapshot = _store.CaptureSnapshot();
        return _linkCache!.GetLinks(snapshot, row);
    }

    internal ConsoleProjection? CaptureCommandProjection()
    {
        _dispatcher.VerifyAccess();
        if (!IsActive()) return null;
        var version = CommandStateVersion;
        _commandProjectionCaptures++;
        using var snapshot = _store.CaptureSnapshot();
        var projection = ConsoleProjector.Project(snapshot, _filter, _viewState, Options);
        if (IsCommandStateCurrent(version, projection)) return projection;
        projection.Dispose();
        return null;
    }

    internal bool IsCommandStateCurrent(long version, ConsoleProjection projection)
    {
        lock (_workGate)
            if (_disposed || _stateVersion != version) return false;
        // Snapshot capture performs no content reads. History can publish before Changed reaches us.
        LogSnapshot snapshot;
        try { snapshot = _store.CaptureSnapshot(); }
        catch (ObjectDisposedException error) when (error.ObjectName == typeof(LogStore).FullName)
        {
            // App-owned store shutdown during acquisition cancels validation.
            return false;
        }
        using (snapshot)
        {
            lock (_workGate)
                return !_disposed && _stateVersion == version && snapshot.Version == projection.Version
                    && snapshot.Generation == projection.Generation && _store.IsCurrent(projection.Generation);
        }
    }

    private void InvalidateCommandState() { lock (_workGate) _stateVersion++; }

    private sealed record ProjectionLease(ConsoleProjection Projection, LogSnapshot Snapshot) : IDisposable
    {
        public void Dispose() { Projection.Dispose(); Snapshot.Dispose(); }
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
        InvalidateCommandState();
        Schedule(true);
        Notify(nameof(Filter));
    }
    private void SetExportOptions(ConsoleExportOptions options)
    {
        _exportOptions = options;
        InvalidateCommandState();
        Notify(nameof(ExportOptions));
    }
    private void StoreChanged(object? sender, LogChangeSet changes)
    {
        InvalidateCommandState();
        Schedule(true);
    }
    private void Schedule(bool refresh, bool retry = false)
    {
        lock (_workGate)
        {
            if (_disposed || retry && !_refreshRetryAvailable) return;
            if (refresh)
            {
                _refreshRequested = true;
                // One automatic retry per explicit refresh request; cleanup posts cannot replenish it.
                _refreshRetryAvailable = !retry;
            }
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
        LogSnapshot? snapshot = null;
        List<Exception> notificationErrors = [];
        try
        {
            var version = CommandStateVersion;
            snapshot = _store.CaptureSnapshot();
            next = ConsoleProjector.Project(snapshot, _filter, _viewState, Options);
            if (!IsCommandStateCurrent(version, next))
            {
                Schedule(true, retry: true);
                return;
            }
            var remapped = _viewState.Remap(_projection, next);
            if (_viewState.Follow is ConsoleFollow.Paused && _projection.Deduplicate != next.Deduplicate)
                next = ConsoleProjectionTransfer.WithPausedOrder(next, remapped);
            // Clear advances admission generation before the writer publishes its reset.
            if (!IsActive() || !_store.IsCurrent(snapshot.Generation)) return;
            _linkCache!.Synchronize(snapshot);
            _retired = _current;
            _current = new(next, snapshot);
            next = null;
            snapshot = null; // Adopt the producing snapshot and projection atomically.
            _viewState = remapped;
            InvalidateCommandState();
        }
        // A reader supplied by the host can throw any non-fatal type. Keep the last good projection, report the
        // failure through RefreshError and recover on the next change. Fatal runtime failures are not swallowed.
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (SetRefreshError(error)) Notify(nameof(RefreshError), notificationErrors);
            Schedule(false);
            RethrowNotificationErrors(notificationErrors);
            return;
        }
        finally { next?.Dispose(); snapshot?.Dispose(); }
        var errorChanged = SetRefreshError(null);
        ((ConsoleCopySelectionCommand)CopySelectionCommand).Notify(notifications => PublishNotifications(notifications, notificationErrors));
        Notify(nameof(ViewState), notificationErrors);
        Notify(nameof(Projection), notificationErrors);
        if (errorChanged) Notify(nameof(RefreshError), notificationErrors);
        Schedule(false);
        RethrowNotificationErrors(notificationErrors);
    }
    private void RethrowNotificationErrors(List<Exception> errors)
    {
        if (errors.Count == 0) return;
        var failure = ExceptionDispatchInfo.Capture(errors.Count == 1 ? errors[0] : new AggregateException(errors));
        // InvokeAsync stores exceptions in its task; Post enters the dispatcher's unhandled path.
        _dispatcher.Post(failure.Throw);
    }
    private bool SetRefreshError(Exception? error)
    {
        if (ReferenceEquals(_refreshError, error)) return false;
        _refreshError = error;
        return true;
    }
    private void Notify(string name, List<Exception>? notificationErrors = null)
    {
        if (!IsActive()) return;
        var args = new PropertyChangedEventArgs(name);
        PublishNotifications((PropertyChanged?.GetInvocationList().Cast<PropertyChangedEventHandler>() ?? [])
            .Select(handler => (Action)(() => handler(this, args))), notificationErrors, IsActive, notificationErrors is not null);
    }

    private void PublishNotifications(IEnumerable<Action> notifications, List<Exception>? notificationErrors = null,
        Func<bool>? continuePublishing = null, bool isolateErrors = true)
    {
        var errors = notificationErrors ?? [];
        _notificationDepth++;
        try
        {
            foreach (var notification in notifications)
            {
                if (continuePublishing?.Invoke() == false) break;
                try { notification(); }
                catch (Exception error) when (isolateErrors && error is not OutOfMemoryException)
                {
                    // Finish independent observers, then report every non-fatal failure through the dispatcher.
                    errors.Add(error);
                }
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
        if (notificationErrors is null) RethrowNotificationErrors(errors);
    }

    private void ReleaseProjections()
    {
        _retired?.Dispose();
        _retired = null;
        _current.Dispose();
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
            _stateVersion++;
            pending = _pending;
            _pending = null;
            _refreshRequested = false;
        }
        _store.Changed -= StoreChanged;
        pending?.Abort();
        _linkCache = null;
        RowCommands.Release();
        if (_notificationDepth == 0) ReleaseProjections();
        foreach (var command in new[] { ToggleLevelCommand, ToggleOnlyMatchesCommand, ToggleDedupeCommand,
            SetTimeModeCommand, ClearCommand, ResetFiltersCommand, ToggleExportTimeCommand, ToggleExportLevelCommand, CopySelectionCommand })
            ((IRelayCommand)command).NotifyCanExecuteChanged();
    }
}
