// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.Time;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.Avalonia.Threading;
using Nvt.Core.LogConsole;
using Xunit;
using Nvt.Core.TestSupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

internal sealed class ConsoleTestStore : IDisposable
{
    // Cross-thread scheduler queue; ConcurrentQueue protects enqueue/dequeue. Tests explicitly run the writer.
    private readonly ConcurrentQueue<Action> _writer = new();
    // UI-thread-owned safety lifetime; cancellation is safe for background writer callbacks.
    private readonly CancellationTokenSource _safety = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    // The UI test advances the fake clock; the writer reads it through atomic access.
    private long _utcTicks = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).UtcTicks;
    internal DateTimeOffset UtcNow
    {
        get => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);
    }
    internal LogStore Store { get; }
    internal ConsoleTestStore(int maxEntries = 100)
    {
        _safety.CancelAfter(TimeSpan.FromSeconds(60));
        Store = new LogStore(maxEntries: maxEntries, clock: new DelegateTimeProvider(() => UtcNow), schedule: callback => _writer.Enqueue(callback));
    }
    internal void AddPublishedEntries(int count)
    {
        for (var index = 0; index < count; index++)
        {
            Store.Add(LogLevel.Info, "app", "entry " + index);
            Fence();
        }
    }
    internal void Fence()
    {
        var capture = CaptureAndReleaseAsync(Store, _safety.Token);
        Until(() => capture.IsCompleted);
        TaskBlock.UntilComplete(capture);
    }
    private static async Task CaptureAndReleaseAsync(LogStore store, CancellationToken token)
    {
        using var snapshot = await store.CaptureLatestAsync(token).ConfigureAwait(false);
    }
    internal void Until(Func<bool> condition)
    {
        while (!condition())
        {
            _safety.Token.ThrowIfCancellationRequested();
            if (_writer.TryDequeue(out var callback)) TaskBlock.UntilComplete(Task.Run(callback, _safety.Token));
            else SpinWait.SpinUntil(() => { _safety.Token.ThrowIfCancellationRequested(); return !_writer.IsEmpty || condition(); });
        }
    }
    internal ConsoleController Controller(ConsoleProjectionOptions? options = null)
    {
        UiThread.RegisterRunningDispatcher(global::Avalonia.Application.Current!.Dispatcher);
        return new ConsoleController(Store, [new("app", "Application"), new("dxf", "Drawing"), new("idle", "Idle")], options);
    }
    public void Dispose()
    {
        Store.Dispose();
        // Waiting for queue availability must not run the very callback being awaited.
        SpinWait.SpinUntil(() => { _safety.Token.ThrowIfCancellationRequested(); return !_writer.IsEmpty; });
        while (_writer.TryDequeue(out var callback)) TaskBlock.UntilComplete(Task.Run(callback, _safety.Token));
        _safety.Dispose();
    }
}

internal static class ConsoleTestView
{
    internal static System.ComponentModel.PropertyChangedEventHandler OnProperty(string name, Action callback)
        => (_, args) => { if (args.PropertyName == name) callback(); };

    internal static System.ComponentModel.PropertyChangedEventHandler OnFirstProperty(string name, Action count, Action first)
    {
        var called = false; // UI-thread-only callback lifetime.
        return OnProperty(name, () => { count(); if (called) return; called = true; first(); });
    }
    internal static Window Create(Control content, bool dark = false, ThemeShape shape = ThemeShape.Pill, double width = 1200, double height = 144)
    {
        var window = new Window
        {
            Content = content,
            Width = width,
            Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        var uri = new Uri("avares://Nvt.Core.Fonts/FontRoles.axaml");
        window.Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
        window.Styles.Add(new FluentTheme());
        ThemeShapes.SetShape(window.Resources, shape);
        window.Show();
        Pump(window);
        return window;
    }
    internal static void Pump(Window? window = null)
    {
        (window?.Dispatcher ?? global::Avalonia.Application.Current!.Dispatcher).RunJobs();
        window?.UpdateLayout();
    }
    internal static void Click(Window window, Button button)
    {
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Pump(window);
    }
    internal static void ClickMenu(Window window, MenuItem item)
    {
        Assert.True(item.IsEffectivelyEnabled);
        Assert.True(item.Bounds.Width > 0 && item.Bounds.Height > 0);
        var root = TopLevel.GetTopLevel(item)!;
        var center = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
        root.MouseDown(center, MouseButton.Left);
        root.MouseUp(center, MouseButton.Left);
        Pump(window);
    }
    internal static StackPanel Surface(ConsoleController controller) => new()
    {
        Children = { new ConsoleHeader { Controller = controller }, new ConsoleToolbar { Controller = controller }, new ConsoleEmptyState { Controller = controller } },
    };
}

internal sealed class ConsoleDispatcherExceptionScope : IDisposable
{
    // Headless UI-thread-only event capture, restored before the next test.
    private readonly Dispatcher _dispatcher = global::Avalonia.Application.Current!.Dispatcher;
    private readonly List<Exception> _errors = [];
    internal IReadOnlyList<Exception> Errors => _errors;
    internal ConsoleDispatcherExceptionScope() => _dispatcher.UnhandledException += OnUnhandledException;
    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        _errors.Add(args.Exception);
        args.Handled = true;
    }
    public void Dispose() => _dispatcher.UnhandledException -= OnUnhandledException;
}

internal sealed class ConsoleBindingLogScope : ILogSink, IDisposable
{
    // Headless UI tests own the global sink for this scope and restore it before the next test.
    private readonly ILogSink? _previous = Logger.Sink;
    // Binding reports can originate on any thread; the queue protects captured messages.
    private readonly ConcurrentQueue<string> _errors = new();
    internal IEnumerable<string> Errors => _errors;
    internal ConsoleBindingLogScope() => Logger.Sink = this;
    public bool IsEnabled(LogEventLevel level, string area)
        => (area == LogArea.Binding && level >= LogEventLevel.Warning) || _previous?.IsEnabled(level, area) == true;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Capture(level, area, messageTemplate);
        if (_previous?.IsEnabled(level, area) == true) _previous.Log(level, area, source, messageTemplate);
    }
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        Capture(level, area, messageTemplate + " " + string.Join(" | ", propertyValues));
        if (_previous?.IsEnabled(level, area) == true) _previous.Log(level, area, source, messageTemplate, propertyValues);
    }
    private void Capture(LogEventLevel level, string area, string message)
    {
        if (area == LogArea.Binding && level >= LogEventLevel.Warning) _errors.Enqueue(message);
    }
    public void Dispose() => Logger.Sink = _previous;
}
