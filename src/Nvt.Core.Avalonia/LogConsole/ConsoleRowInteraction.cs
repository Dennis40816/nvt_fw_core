// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// Attachment-scoped UI-thread state: focus/range origin are local; selected raw identities belong to ViewState.
internal sealed class ConsoleRowInteraction
{
    private readonly ConsoleListView _view;
    private readonly ConsoleItemsHost _host;
    private readonly RelayCommand<SelectionIntent> _select;
    private readonly RelayCommand<KeyEventArgs> _keys;
    private FocusPosition? _focus;
    private MenuFlyout? _menu;
    private readonly List<WeakReference<ConsoleCopySelectionCommand>> _commands = [];
    internal long? FocusedEntry => _focus?.Entry;
    internal MenuFlyout? OpenMenu => _menu;

    internal ConsoleRowInteraction(ConsoleListView view, ConsoleItemsHost host)
    {
        _view = view;
        _host = host;
        _select = new(Select);
        _keys = new(HandleKey);
        view.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        view.GotFocus += GotFocus;
        view.Focusable = true;
    }

    internal ConsoleLinkIndex Links(ConsoleRow row) => _view.Controller?.GetLinks(row)
        ?? new ConsoleLinkIndex(row.LinkSpans ?? [], row.TextContent.Length);

    internal void Activate(ConsoleRow row, KeyModifiers modifiers, bool toggle, NavigationMethod method = NavigationMethod.Pointer)
        => _select.Execute(new(row, modifiers, toggle, method));

    private void Select(SelectionIntent? intent)
    {
        if (!CanInteract || intent is null || _view.Projection is not { } projection) return;
        var row = intent.Row;
        var origin = _focus?.Anchor ?? row.MemberSequences[0];
        var state = _view.AttachmentSession!.CaptureReadingState();
        var selected = row.MemberSequences.ToImmutableHashSet();
        if (intent.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            var end = projection.Rows.IndexOf(row);
            var start = FindEntry(origin);
            if (start < 0) { start = end; origin = row.MemberSequences[0]; }
            selected = projection.Rows.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1)
                .SelectMany(item => item.MemberSequences).ToImmutableHashSet();
        }
        else if (intent.Modifiers.HasFlag(KeyModifiers.Control))
            selected = row.MemberSequences.Any(state.Selection.Contains)
                ? state.Selection.Except(row.MemberSequences) : state.Selection.Union(row.MemberSequences);
        _focus = new(row.MemberSequences[0], intent.Modifiers.HasFlag(KeyModifiers.Shift) ? origin : row.MemberSequences[0]);
        var expanded = state.ExpandedIds;
        if (intent.Toggle)
            expanded = expanded.Contains(row.Id) ? expanded.Remove(row.Id) : expanded.Add(row.Id);
        _view.Request(state with { Selection = selected, ExpandedIds = expanded });
        FocusRow(row, intent.Method);
        foreach (var container in _host.Children.OfType<ConsoleRowPresenter>()) container.RefreshInteraction();
    }

    private int FindEntry(long entry)
    {
        var rows = _view.Projection!.Rows;
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].MemberSequences.Contains(entry)) return index;
        return -1;
    }

    private bool CanInteract => _view.Controller?.ResetFiltersCommand.CanExecute(null) ?? true;

    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        _keys.Execute(e);
    }

    private void GotFocus(object? sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, _view)) return;
        var presenter = _host.Children.OfType<ConsoleRowPresenter>()
            .FirstOrDefault(row => _focus is { } focus && row.Row?.MemberSequences.Contains(focus.Entry) == true)
            ?? _host.Children.OfType<ConsoleRowPresenter>().Where(row => row.Bounds.Bottom > 0).MinBy(row => row.Bounds.Y);
        if (presenter?.Row is not { } current) return;
        if (_focus is not { } active || !current.MemberSequences.Contains(active.Entry))
            _focus = new(current.MemberSequences[0], current.MemberSequences[0]);
        presenter.Focus(e is FocusChangedEventArgs change ? change.NavigationMethod : NavigationMethod.Unspecified);
    }

    private void HandleKey(KeyEventArgs? e)
    {
        if (!CanInteract || e is null) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.End) _view.JumpToLatest();
        else if (e.Key == Key.Escape)
        {
            if (_menu?.IsOpen == true) _menu.Hide();
            else _view.Request(_view.ViewState with { Selection = [] });
        }
        else { HandleRowKey(e, control); return; }
        e.Handled = true;
    }

    private void HandleRowKey(KeyEventArgs e, bool control)
    {
        if (_view.Projection is not { Rows.IsEmpty: false } projection) return;
        var index = _focus is { } focus ? Math.Max(0, FindEntry(focus.Entry)) : 0;
        var row = projection.Rows[index];
        if (control && e.Key == Key.C) Execute(_view.Controller?.CopySelectionCommand);
        else if (control && e.Key == Key.A)
            _view.Request(_view.AttachmentSession!.CaptureReadingState() with
            { Selection = projection.Rows.SelectMany(item => item.MemberSequences).ToImmutableHashSet() });
        else if (control && e.Key == Key.Enter) ShowLinks(row);
        else if (!control && e.Key is Key.Enter or Key.Space) Activate(row, KeyModifiers.None, true, NavigationMethod.Directional);
        else if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ShowMenu(row, null);
        else if (!control && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            Navigate(index, e);
        else return;
        e.Handled = true;
    }

    private void Navigate(int index, KeyEventArgs e)
    {
        var page = Math.Max(1, (int)(_host.Viewport.Height / _host.ScrollSize.Height));
        var next = e.Key switch
        {
            Key.Up => index - 1,
            Key.Down => index + 1,
            Key.PageUp => index - page,
            Key.PageDown => index + page,
            Key.Home => 0,
            _ => _view.Projection!.Rows.Length - 1,
        };
        next = Math.Clamp(next, 0, _view.Projection!.Rows.Length - 1);
        var row = _view.Projection.Rows[next];
        Activate(row, e.KeyModifiers, false, NavigationMethod.Directional);
        _host.Reveal(next);
        _host.UpdateLayout();
        FocusRow(row, NavigationMethod.Directional);
    }

    private void FocusRow(ConsoleRow row, NavigationMethod method)
    {
        var presenter = _host.Children.OfType<ConsoleRowPresenter>().FirstOrDefault(item => item.RowId == row.Id);
        if (presenter is not null) presenter.Focus(method);
        else _view.Focus(method);
    }

    internal void Open(ConsoleRow row, LinkTarget target)
        => Execute(_view.Controller?.RowCommands.Create(ConsoleRowAction.OpenLink, row.Id, target));
    private static void Execute(System.Windows.Input.ICommand? command)
    {
        if (command?.CanExecute(null) == true) command.Execute(null);
    }
    private void ShowLinks(ConsoleRow row)
    {
        var links = Links(row).Spans;
        if (links.Length == 1) Open(row, links[0].Target);
        else if (links.Length > 1) ShowMenu(row, null, links);
    }
    internal ConsoleCopySelectionCommand? CreateCommand(ConsoleRowAction action, ConsoleRowId id, LinkTarget? target, Action? toggle)
    {
        var command = _view.Controller?.RowCommands.Create(action, id, target, toggle);
        if (command is null) return null;
        _commands.RemoveAll(reference => !reference.TryGetTarget(out _));
        _commands.Add(new(command));
        return command;
    }

    private void CloseMenu()
    {
        if (_menu is not { } menu) return;
        _menu = null;
        menu.Hide();
        ConsoleMenuBuilder.Clear(menu);
    }

    internal void ShowMenu(ConsoleRow row, LinkTarget? target, ImmutableArray<ConsoleLinkSpan> choices = default)
    {
        _focus = new(row.MemberSequences[0], row.MemberSequences[0]);
        FocusRow(row, NavigationMethod.Directional);
        CloseMenu();
        _menu = ConsoleMenuBuilder.Row(_view, row, target, choices);
        var presenter = _host.Children.OfType<ConsoleRowPresenter>().FirstOrDefault(item => item.RowId == row.Id);
        if (presenter is not null) { presenter.InvalidateVisual(); _menu.ShowAt(presenter); }
    }
    internal void Detach()
    {
        _view.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        _view.GotFocus -= GotFocus;
        CloseMenu();
        foreach (var reference in _commands)
            if (reference.TryGetTarget(out var command)) command.Release();
        _commands.Clear();
        _focus = null;
    }
    private sealed record FocusPosition(long Entry, long Anchor);
    private sealed record SelectionIntent(ConsoleRow Row, KeyModifiers Modifiers, bool Toggle, NavigationMethod Method);
}
