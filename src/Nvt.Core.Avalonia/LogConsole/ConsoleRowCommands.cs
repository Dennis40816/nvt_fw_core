// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Windows.Input;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal enum ConsoleRowAction { CopyMessage, CopyRow, CopySource, Toggle, FilterSource, FilterLevel, OpenLink }

// UI-thread-only command lifetime. Commands retain identities, never borrowed content.
internal sealed class ConsoleRowCommands
{
    private Lifetime? _lifetime;
    private readonly List<WeakReference<ConsoleCopySelectionCommand>> _commands = [];
    internal ConsoleRowCommands(ConsoleController controller, Action<IEnumerable<Action>> publish)
    {
        _lifetime = new(controller, publish);
        CopySelection = CreateCommand(CanCopySelection, () => CopySelected());
    }
    internal ICommand CopySelection { get; }
    internal ConsoleCopySelectionCommand Create(ConsoleRowAction action, ConsoleRowId id, LinkTarget? target = null, Action? toggle = null)
        => CreateCommand(() => Reason(action, id, target) is null, () => Execute(action, id, target, toggle));

    private ConsoleCopySelectionCommand CreateCommand(Func<bool> canExecute, Action execute)
    {
        var command = new ConsoleCopySelectionCommand(canExecute, execute, _lifetime?.Publish ?? Deliver);
        _commands.RemoveAll(reference => !reference.TryGetTarget(out _));
        _commands.Add(new(command));
        if (_lifetime is null) command.Release();
        return command;
    }
    private static void Deliver(IEnumerable<Action> notifications)
    {
        foreach (var notification in notifications) notification();
    }

    private bool CanCopySelection()
    {
        if (_lifetime is not { Controller: var controller } || controller.Clipboard is null) return false;
        using var projection = controller.CaptureCommandProjection();
        return projection is not null && projection.Rows.Any(row => row.MemberSequences.Any(controller.ViewState.Selection.Contains));
    }
    private bool CopySelected()
    {
        if (_lifetime is not { Controller: var controller } || controller.Clipboard is null) return false;
        var version = controller.CommandStateVersion;
        using var projection = controller.CaptureCommandProjection();
        if (projection is null) return false;
        var rows = projection.Rows.Where(row => row.MemberSequences.Any(controller.ViewState.Selection.Contains)).ToArray();
        if (rows.Length == 0) return false;
        var text = ConsoleCopyText.Rows(rows, controller.ExportOptions);
        using var current = controller.CaptureCommandProjection();
        if (current is null || !rows.All(row => current.Rows.Any(item => item.Id == row.Id
            && item.MemberSequences.SequenceEqual(row.MemberSequences)
            && item.MemberSequences.Any(controller.ViewState.Selection.Contains)))
            || !controller.IsCommandStateCurrent(version, projection)) return false;
        controller.Clipboard.SetText(text);
        return true;
    }

    internal string? Reason(ConsoleRowAction action, ConsoleRowId id, LinkTarget? target = null)
    {
        if (_lifetime is not { Controller: var controller }) return "Controller is disposed";
        var version = controller.CommandStateVersion;
        using var projection = controller.CaptureCommandProjection();
        var reason = Reason(controller, action, projection?.Rows.FirstOrDefault(row => row.Id == id), target);
        return projection is not null && controller.IsCommandStateCurrent(version, projection)
            ? reason : "Console state changed";
    }

    // The presenter hit-tests links from its already published row; hover only asks the app adapter.
    internal string? HoverReason(LinkTarget target)
    {
        if (_lifetime is not { Controller: var controller }) return "Controller is disposed";
        return controller.LinkOpener is null ? "Link opening is unavailable" : controller.LinkOpener.GetUnavailableReason(target);
    }

    private static string? Reason(ConsoleController controller, ConsoleRowAction action, ConsoleRow? row, LinkTarget? target)
    {
        if (row is null) return "Row is no longer visible";
        if (action == ConsoleRowAction.OpenLink && target is not null
            && !(row.LinkSpans ?? controller.GetCommandLinks(row).Spans).Any(link => link.Target == target))
            return "Link is no longer in this row";
        return action switch
        {
            ConsoleRowAction.CopyMessage or ConsoleRowAction.CopyRow or ConsoleRowAction.CopySource
                => controller.Clipboard is null ? "Clipboard is unavailable" : null,
            ConsoleRowAction.OpenLink => target is null ? "No link in this row"
                : controller.LinkOpener is null ? "Link opening is unavailable" : controller.LinkOpener.GetUnavailableReason(target),
            _ => null,
        };
    }

    private bool Execute(ConsoleRowAction action, ConsoleRowId id, LinkTarget? target, Action? toggle)
    {
        if (_lifetime is not { Controller: var controller }) return false;
        var version = controller.CommandStateVersion;
        using var projection = controller.CaptureCommandProjection();
        var row = projection?.Rows.FirstOrDefault(row => row.Id == id);
        if (Reason(controller, action, row, target) is not null || row is null
            || !controller.IsCommandStateCurrent(version, projection!)) return false;
        switch (action)
        {
            case ConsoleRowAction.CopyMessage:
            case ConsoleRowAction.CopyRow:
            case ConsoleRowAction.CopySource: return Copy(controller, action, row, version, projection!);
            case ConsoleRowAction.Toggle: toggle?.Invoke(); break;
            case ConsoleRowAction.FilterSource: controller.SetSelectedSources([row.SourceId]); break;
            case ConsoleRowAction.FilterLevel: controller.FilterByLevel(row.Level); break;
            case ConsoleRowAction.OpenLink: controller.LinkOpener!.Open(target!); break;
        }
        return true;
    }

    private static bool Copy(ConsoleController controller, ConsoleRowAction action, ConsoleRow row, long version, ConsoleProjection projection)
    {
        var text = action switch
        {
            ConsoleRowAction.CopyMessage => ConsoleCopyText.Message(row),
            ConsoleRowAction.CopyRow => ConsoleCopyText.Rows([row], controller.ExportOptions),
            _ => ConsoleCopyText.Source(row),
        };
        // Content readers can reenter Clear or filtering. Validate at delivery, after all formatting.
        using var current = controller.CaptureCommandProjection();
        if (current is null || !current.Rows.Any(item => item.Id == row.Id)
            || !controller.IsCommandStateCurrent(version, projection)) return false;
        controller.Clipboard!.SetText(text);
        return true;
    }

    internal void Release()
    {
        _lifetime = null;
        foreach (var reference in _commands)
            if (reference.TryGetTarget(out var command)) command.Release();
        _commands.Clear();
    }
    private sealed record Lifetime(ConsoleController Controller, Action<IEnumerable<Action>> Publish);
}
