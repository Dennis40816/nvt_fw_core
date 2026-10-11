// Copyright (c) 2026 Dennis Liu. All rights reserved.

using CommunityToolkit.Mvvm.Input;

namespace Nvt.Core.Avalonia.LogConsole;

// UI-thread-only subscriptions. The controller publishes each observer with its lease and exception protection.
internal sealed class ConsoleCopySelectionCommand(Func<bool> canExecute, Action execute,
    Action<IEnumerable<Action>> publish) : IRelayCommand
{
    private Lifetime? _lifetime = new(canExecute, execute, publish);
    private EventHandler? _handlers;
    public event EventHandler? CanExecuteChanged { add { if (_lifetime is not null) _handlers += value; } remove => _handlers -= value; }
    public bool CanExecute(object? parameter) => _lifetime?.CanExecute() == true;
    public void Execute(object? parameter) { if (_lifetime is { } lifetime && lifetime.CanExecute()) lifetime.Execute(); }
    public void NotifyCanExecuteChanged() { if (_lifetime is { } lifetime) Notify(lifetime.Publish); }
    internal void Notify(Action<IEnumerable<Action>> delivery)
        => delivery((_handlers?.GetInvocationList().Cast<EventHandler>() ?? [])
            .Select(handler => (Action)(() => handler(this, EventArgs.Empty))));

    internal void Release()
    {
        if (_lifetime is not { } lifetime) return;
        _lifetime = null;
        try { Notify(lifetime.Publish); }
        finally { _handlers = null; }
    }

    private sealed record Lifetime(Func<bool> CanExecute, Action Execute, Action<IEnumerable<Action>> Publish);
}
