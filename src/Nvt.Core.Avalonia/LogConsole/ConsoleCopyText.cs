// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// One bounded builder for all clipboard paths; it never materializes a complete large message.
internal static class ConsoleCopyText
{
    internal const int MaximumCharacters = 65_536;
    internal static string Source(ConsoleRow row) => row.SourceId[..Math.Min(row.SourceId.Length, MaximumCharacters)];
    internal static string Message(ConsoleRow row)
    {
        var text = new StringBuilder();
        AppendContent(text, row.TextContent);
        return text.ToString();
    }
    internal static string Rows(IEnumerable<ConsoleRow> rows, ConsoleExportOptions options)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            if (text.Length == MaximumCharacters) break;
            if (text.Length != 0) Append(text, "\n");
            if (options.IncludeTime) Append(text, row.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " ");
            if (options.IncludeLevel) Append(text, "[" + row.Level + "] ");
            Append(text, "[");
            Append(text, row.SourceId);
            Append(text, "] ");
            AppendContent(text, row.TextContent);
            if (row.Count > 1) Append(text, " ×" + row.Count.ToString(CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }
    private static void Append(StringBuilder text, string value)
        => text.Append(value.AsSpan(0, Math.Min(value.Length, MaximumCharacters - text.Length)));
    private static void AppendContent(StringBuilder text, ILogTextContent content)
    {
        Span<char> chunk = stackalloc char[1024];
        for (var offset = 0; offset < content.Length && text.Length < MaximumCharacters;)
        {
            var length = Math.Min(chunk.Length, Math.Min(content.Length - offset, MaximumCharacters - text.Length));
            content.Read(offset, chunk[..length]);
            text.Append(chunk[..length]);
            offset += length;
        }
    }
}
