// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

internal sealed class ConsoleInteractionAdapters : IConsoleClipboard, IConsoleLinkOpener
{
    // Headless UI thread only; no platform clipboard, resolver or process is touched.
    internal List<string> Copied { get; } = [];
    internal List<LinkTarget> Opened { get; } = [];
    internal string? UnavailableReason { get; set; }
    public void SetText(string text) => Copied.Add(text);
    internal Action? OnAvailability { private get; set; }
    public string? GetUnavailableReason(LinkTarget target)
    {
        var callback = OnAvailability;
        OnAvailability = null;
        callback?.Invoke();
        return UnavailableReason;
    }
    public void Open(LinkTarget target) => Opened.Add(target);
}

internal static class ConsoleInteractionWindow
{
    internal static Window Create(Control view)
    {
        var window = new Window { Content = view, Width = 1200, Height = 280 };
        var tokens = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
        window.Resources.MergedDictionaries.Add(new ResourceInclude(tokens) { Source = tokens });
        var fonts = new Uri("avares://Nvt.Core.Fonts/FontRoles.axaml");
        window.Resources.MergedDictionaries.Add(new ResourceInclude(fonts) { Source = fonts });
        window.Styles.Add(new FluentTheme());
        var styles = new Uri("avares://Nvt.Core.Avalonia/LogConsole/ConsoleListStyles.axaml");
        window.Styles.Add(new StyleInclude(styles) { Source = styles });
        window.Show();
        Pump(window);
        return window;
    }
    internal static void Pump(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            window.Dispatcher.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
    internal static ConsoleItemsHost Host(ConsoleListView view)
        => view.GetVisualDescendants().OfType<ConsoleItemsHost>().First();
}

internal sealed class ConsoleInteractionScene : IDisposable
{
    private readonly ConsoleTestStore _store;
    // UI thread only. Null means the caller owns the shared store; the surface owns host bindings.
    private readonly ConsoleTestStore? _ownedStore;
    private readonly ConsoleController? _ownedController;
    private readonly Control _surface;
    internal ConsoleInteractionAdapters Adapters { get; } = new();
    internal ConsoleController Controller { get; }
    internal ConsoleListView View { get; }
    internal Window Window { get; }
    internal ConsoleHeader? Header { get; }
    internal ConsoleToolbar? Toolbar { get; }
    internal ConsoleEmptyState? EmptyState { get; }
    internal ConsoleInteractionScene(int count = 4, string? message = null, bool clipboard = true, bool opener = true,
        ILogTextContent? content = null, bool composed = false, ConsoleTestStore? store = null, ConsoleController? controller = null)
    {
        _ownedStore = store is null ? new(count + 10) : null;
        _store = store ?? _ownedStore!;
        for (var index = 1; index <= count; index++)
        {
            if (index == 1 && content is not null) _store.Store.Add(new LogWrite(LogLevel.Info, "app", content));
            else _store.Store.Add(LogLevel.Info, index % 2 == 0 ? "dxf" : "app", message ?? $"https://example.test/{index}\nsecond line");
        }
        _store.Fence();
        _ownedController = controller is null ? _store.Controller(clipboard: clipboard ? Adapters : null, opener: opener ? Adapters : null) : null;
        Controller = controller ?? _ownedController!;
        View = new() { Controller = Controller, Projection = Controller.Projection, ViewState = Controller.ViewState };
        View.ViewStateRequested += Requested;
        Control surface = View;
        if (composed)
        {
            Header = new() { Controller = Controller };
            Toolbar = new() { Controller = Controller };
            EmptyState = new() { Controller = Controller };
            var grid = new Grid { RowDefinitions = new("Auto,Auto,*") };
            grid.Children.Add(Header);
            grid.Children.Add(Toolbar);
            grid.Children.Add(View);
            grid.Children.Add(EmptyState);
            Grid.SetRow(Toolbar, 1);
            Grid.SetRow(View, 2);
            Grid.SetRow(EmptyState, 2);
            surface = grid;
        }
        _surface = surface;
        surface.AttachedToVisualTree += Attached;
        surface.DetachedFromVisualTree += Detached;
        Window = ConsoleInteractionWindow.Create(surface);
    }
    private void Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Controller.PropertyChanged += Changed;
        Changed(null, new(null));
    }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => Controller.PropertyChanged -= Changed;
    internal void Detach() => Window.Content = null;
    internal void Attach()
    {
        Window.Content = _surface;
        ConsoleInteractionWindow.Pump(Window);
    }
    internal void Publish(string message)
    {
        _store.Store.Add(LogLevel.Info, "app", message);
        _store.Fence();
    }
    internal int PendingOperationCount => Controller.PendingOperationCount
        + (View.AttachmentSession?.PendingOperation is null ? 0 : 1)
        + (View.AttachmentSession?.DeferredOperation is null ? 0 : 1);
    private void Requested(object? sender, ConsoleViewState state) => Controller.RequestViewState(state);
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        View.ViewState = Controller.ViewState;
        View.Projection = Controller.Projection;
    }
    internal ConsoleRowPresenter Row(long id)
        => ConsoleInteractionWindow.Host(View).Children.OfType<ConsoleRowPresenter>().First(row => row.RowId == new ConsoleRowId(id));
    internal Point MessagePoint(long id, double x = 8)
        => Row(id).MessageText.TranslatePoint(new Point(x, 10), Window)!.Value;
    internal void Click(long id, RawInputModifiers modifiers = RawInputModifiers.None, double x = 8)
    {
        var point = MessagePoint(id, x);
        Window.MouseDown(point, MouseButton.Left, modifiers);
        Window.MouseUp(point, MouseButton.Left, modifiers);
        ConsoleInteractionWindow.Pump(Window);
    }
    internal void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        View.Focus(NavigationMethod.Directional);
        SendKey(key, modifiers);
    }
    internal void SendKey(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        ConsoleInteractionWindow.Pump(Window);
    }
    public void Dispose()
    {
        Window.Close();
        Controller.PropertyChanged -= Changed;
        View.ViewStateRequested -= Requested;
        _ownedController?.Dispose();
        _ownedStore?.Dispose();
    }
}

/// <summary>The work of one full traversal of the Console list, as counts.</summary>
/// <param name="RowVisits">Row visits of height rebuilding, synchronization, measure and arrange.</param>
/// <param name="HostMeasurePasses">Measure passes of the items host.</param>
/// <param name="MaxStepMeasurePasses">The most host measure passes that one scroll step needed.</param>
/// <param name="RowMeasures">Measure passes of row presenters, where text layout happens.</param>
/// <param name="Steps">Scroll steps of the traversal.</param>
internal readonly record struct ConsoleTraversalWork(long RowVisits, int HostMeasurePasses, int MaxStepMeasurePasses, long RowMeasures, int Steps);

internal sealed class ConsoleScrollScene : IDisposable
{
    private readonly InMemoryLogTextContent _content = new("Message");
    internal ConsoleProjection Projection { get; }
    internal ConsoleListView View { get; }
    internal Window Window { get; }
    internal ConsoleItemsHost Host => ConsoleInteractionWindow.Host(View);
    internal ConsoleScrollScene(int count, ILogTextContent? firstContent = null)
    {
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var rows = Enumerable.Range(1, count).Select(index => new ConsoleRow(new(index), LogLevel.Info, "app",
            index == 1 ? firstContent ?? _content : _content, 0, null, [index], instant, instant, index, [], "")).ToImmutableArray();
        Projection = new()
        {
            Version = 1,
            Generation = 1,
            LastSequence = count,
            CapturedAt = instant,
            TimeBase = instant,
            Rows = rows,
            LevelCounts = ImmutableDictionary<LogLevel, int>.Empty,
            Sources = [],
            SourceCounts = ImmutableDictionary<string, int>.Empty,
            RetainedMembership = rows.ToImmutableDictionary(row => row.Id, row => row.MemberSequences),
            EventCount = count,
            NewSincePauseCount = 0,
            EvictedCount = 0,
            Deduplicate = false,
        };
        View = new() { Projection = Projection };
        Window = ConsoleInteractionWindow.Create(View);
    }
    internal void Pause()
    {
        View.ViewState = View.ViewState.Pause(Projection, new(1));
        ConsoleInteractionWindow.Pump(Window);
    }
    internal void OneUserScroll() => Host.Offset = new(0, Host.Offset.Y == 40 ? 60 : 40);
    internal void Traverse()
    {
        var step = Host.PageScrollSize.Height;
        for (var y = 0d; y < Host.Extent.Height; y += step)
        {
            Host.Offset = new(0, y);
            Window.UpdateLayout();
        }
    }
    /// <summary>Walks the whole list once and returns counts of work. No clock enters the result.</summary>
    /// <remarks>
    /// Call it on a scene that was only created and paused. Row measures are the growth of the pass count of each presenter
    /// that the host holds after a step. A new presenter starts at zero. A presenter that is measured and recycled inside one
    /// layout is not seen, and one that sat in the pool before the first step adds its earlier passes. The error is a few
    /// measures in 10,000 and far below the margin of the limit.
    /// </remarks>
    internal ConsoleTraversalWork MeasureTraversal()
    {
        Host.Offset = default;
        Window.UpdateLayout();
        var seen = new Dictionary<ConsoleRowPresenter, int>();
        long rowMeasures = 0;
        void SeeRowPresenters()
        {
            foreach (var presenter in Host.Children.OfType<ConsoleRowPresenter>())
            {
                rowMeasures += presenter.MeasurePasses - seen.GetValueOrDefault(presenter);
                seen[presenter] = presenter.MeasurePasses;
            }
        }
        SeeRowPresenters();
        rowMeasures = 0;
        var visitsBefore = Host.RowVisits;
        var passesBefore = Host.MeasurePasses;
        var maxStepPasses = 0;
        var step = Host.PageScrollSize.Height;
        var steps = 0;
        for (var y = 0d; y < Host.Extent.Height; y += step)
        {
            var stepPassesBefore = Host.MeasurePasses;
            Host.Offset = new(0, y);
            Window.UpdateLayout();
            maxStepPasses = Math.Max(maxStepPasses, Host.MeasurePasses - stepPassesBefore);
            SeeRowPresenters();
            steps++;
        }
        return new(Host.RowVisits - visitsBefore, Host.MeasurePasses - passesBefore, maxStepPasses, rowMeasures, steps);
    }
    internal void CopyOldOrderAndScroll()
    {
        var oldOrder = Projection.Rows.Select(row => row.Id).ToImmutableArray();
        OneUserScroll();
        GC.KeepAlive(oldOrder);
    }
    internal int ScrollMeasures()
    {
        var before = Host.MeasurePasses;
        OneUserScroll();
        Window.UpdateLayout();
        return Host.MeasurePasses - before;
    }
    internal int ThemeMeasures()
    {
        var before = Host.MeasurePasses;
        Window.RequestedThemeVariant = ThemeVariant.Dark;
        ConsoleInteractionWindow.Pump(Window);
        return Host.MeasurePasses - before;
    }
    internal int StableRowMeasures()
    {
        Host.Offset = new(0, 40);
        Window.UpdateLayout();
        return CountRowMeasures(() => { Host.Offset = new(0, 41); Window.UpdateLayout(); });
    }
    internal int RepeatedRowMeasures() => CountRowMeasures(() =>
    {
        foreach (var row in Host.Children.OfType<ConsoleRowPresenter>()) row.InvalidateMeasure();
        Host.InvalidateMeasure();
        Window.UpdateLayout();
    });
    private int CountRowMeasures(Action change)
    {
        var before = Host.Children.OfType<ConsoleRowPresenter>().ToDictionary(row => row, row => row.MeasurePasses);
        change();
        return Host.Children.OfType<ConsoleRowPresenter>().Sum(row => row.MeasurePasses - before.GetValueOrDefault(row));
    }
    internal int RedundantMeasures()
    {
        var before = Host.MeasurePasses;
        for (var pass = 0; pass < 5; pass++) { Host.InvalidateMeasure(); Window.UpdateLayout(); }
        return Host.MeasurePasses - before;
    }
    public void Dispose() { Window.Close(); Projection.Dispose(); _content.Dispose(); }
}

internal sealed class ConsoleMeasureContent(string text) : ILogTextContent
{
    // UI-thread-only one-shot injection at the first actual content read.
    internal Action? OnRead { private get; set; }
    // Interlocked writes and Volatile reads protect observations across the store writer and headless UI.
    private int _readCount;
    internal int ReadCount => Volatile.Read(ref _readCount);
    public int Length => text.Length;
    public int ResidentCharacterCount => text.Length;
    public long Version => 0;
    public void Read(int offset, Span<char> destination)
    {
        Interlocked.Increment(ref _readCount);
        var callback = OnRead;
        OnRead = null;
        callback?.Invoke();
        text.AsSpan(offset, destination.Length).CopyTo(destination);
    }
    public void Dispose() { }
}
