// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Nvt.Core.LogConsole;

/// <summary>Independent export choices. Time and level are included by default.</summary>
/// <param name="IncludeTime">Include UTC clock time.</param>
/// <param name="IncludeLevel">Include the level.</param>
public sealed record ConsoleExportOptions(bool IncludeTime = true, bool IncludeLevel = true);

/// <summary>One formatter for selected rows, all visible rows, and UTF-8 log streams.</summary>
public static class ConsoleExportFormatter
{
    /// <summary>Formats all projected rows, including rows outside the viewport.</summary>
    public static string FormatVisible(ConsoleProjection projection, ConsoleExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Format(projection.Rows, options ?? new ConsoleExportOptions());
    }

    /// <summary>Formats visible selected stable identities in projection order. Hidden rows are never copied.</summary>
    public static string FormatSelection(ConsoleProjection projection, ImmutableHashSet<long> selection,
        ConsoleExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(selection);
        return Format(projection.Rows.Where(row => row.MemberSequences.Any(selection.Contains)), options ?? new ConsoleExportOptions());
    }

    /// <summary>Writes the same visible text as UTF-8 without BOM. Leaves the app-owned destination open.</summary>
    public static async Task WriteLogAsync(Stream destination, ConsoleProjection projection,
        ConsoleExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(projection);
        options ??= new ConsoleExportOptions();
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), 1024, leaveOpen: true);
        var chunk = new char[1024];
        var first = true;
        foreach (var row in projection.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!first) await writer.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            first = false;
            await writer.WriteAsync(Prefix(row, options).AsMemory(), cancellationToken).ConfigureAwait(false);
            for (var offset = 0; offset < row.TextContent.Length;)
            {
                var length = Math.Min(chunk.Length, row.TextContent.Length - offset);
                row.TextContent.Read(offset, chunk.AsSpan(0, length));
                await writer.WriteAsync(chunk.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                offset += length;
            }
            await writer.WriteAsync(Suffix(row).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Format(IEnumerable<ConsoleRow> rows, ConsoleExportOptions options)
    {
        var text = new StringBuilder();
        Span<char> chunk = stackalloc char[1024];
        foreach (var row in rows)
        {
            if (text.Length != 0) text.Append('\n');
            text.Append(Prefix(row, options));
            for (var offset = 0; offset < row.TextContent.Length;)
            {
                var length = Math.Min(chunk.Length, row.TextContent.Length - offset);
                row.TextContent.Read(offset, chunk[..length]);
                text.Append(chunk[..length]);
                offset += length;
            }
            text.Append(Suffix(row));
        }
        return text.ToString();
    }

    private static string Prefix(ConsoleRow row, ConsoleExportOptions options)
        => (options.IncludeTime ? row.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " : "")
            + (options.IncludeLevel ? "[" + row.Level + "] " : "") + "[" + row.SourceId + "] ";

    private static string Suffix(ConsoleRow row)
        => row.Count > 1 ? " ×" + row.Count.ToString(CultureInfo.InvariantCulture) : "";
}
