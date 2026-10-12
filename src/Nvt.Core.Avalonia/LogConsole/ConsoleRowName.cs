// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

internal static class ConsoleRowName
{
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

    // Prefix/inter-fragment literals bind right; a suffix binds the final fragment. Escaped braces are literal.
    private static string? Compose(string template, Dictionary<string, string> fragments)
    {
        var text = new StringBuilder();
        var literal = new StringBuilder();
        var offset = 0;
        var hasFragment = false;
        var lastPresent = false;
        while (offset < template.Length)
        {
            var character = template[offset++];
            if (character is not ('{' or '}')) { literal.Append(character); continue; }
            if (offset < template.Length && template[offset] == character)
            {
                literal.Append(character);
                offset++;
                continue;
            }
            if (character == '}') return null;
            var end = template.IndexOf('}', offset);
            if (end < 0 || !fragments.TryGetValue(template[offset..end], out var fragment)) return null;
            hasFragment = true;
            lastPresent = fragment.Length > 0;
            if (lastPresent)
            {
                text.Append(literal);
                text.Append(fragment);
            }
            literal.Clear();
            offset = end + 1;
        }
        if (!hasFragment || lastPresent) text.Append(literal);
        return text.ToString();
    }
}
