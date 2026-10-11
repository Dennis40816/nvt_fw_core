// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal static class ConsoleRowName
{
    internal static ConsoleFirstLine FirstLine(ConsoleRow row) => row.GetFirstLine();

    // The only name composer. The message is a plain value published by the painting layout.
    internal static string Build(ConsoleRowPresenter.RowInput input, string message)
    {
        var row = input.Row;
        var view = input.View;
        var fragments = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Level"] = ConsoleListText.Format(view, $"Nvt.Console.List.Level.{row.Level}"),
            ["Time"] = view.TimeMode == ConsoleTimeMode.Hidden ? string.Empty
                : ConsoleTimeFormatter.Format(row.Timestamp, view.Projection!.TimeBase, view.TimeMode,
                    view.TimeOptions.RelativeTimeTemplate, view.TimeOptions.Culture, view.TimeOptions.AbsoluteTimeZone),
            ["Source"] = ConsoleCopyText.Source(row),
            ["Message"] = message,
            ["Repeat"] = row.Count > 1 ? ConsoleListText.Format(view, "Nvt.Console.List.Row.RepeatFormat", row.Count) : string.Empty,
            ["Links"] = input.Links is { Spans.Length: > 0 } links ? ConsoleListText.Format(view,
                links.Spans.Length == 1 ? "Nvt.Console.List.Row.LinkOneFormat" : "Nvt.Console.List.Row.LinksFormat", links.Spans.Length) : string.Empty,
        };
        var template = ConsoleListText.Template(view, "Nvt.Console.List.Row.NameFormat");
        return Compose(template, fragments) ?? Compose(ConsoleListText.Default("Nvt.Console.List.Row.NameFormat"), fragments)!;
    }

    // Literals preceding an omitted fragment are omitted too; translations may reorder named fragments.
    private static string? Compose(string template, Dictionary<string, string> fragments)
    {
        var text = new StringBuilder();
        var offset = 0;
        while (offset < template.Length)
        {
            var start = template.IndexOf('{', offset);
            if (start < 0) return null;
            var end = template.IndexOf('}', start + 1);
            if (end < 0 || !fragments.TryGetValue(template[(start + 1)..end], out var fragment)) return null;
            if (fragment.Length > 0)
            {
                if (text.Length > 0) text.Append(template.AsSpan(offset, start - offset));
                text.Append(fragment);
            }
            offset = end + 1;
        }
        return text.ToString();
    }
}
