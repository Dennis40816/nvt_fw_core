// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Nvt.Core.LogConsole;

/// <summary>The syntactic target kind.</summary>
public enum LinkKind
{
    /// <summary>An external URL.</summary>
    Url,
    /// <summary>A file path, including extensionless paths.</summary>
    File,
    /// <summary>A folder path.</summary>
    Folder,
}

/// <summary>A full target. Core does not normalize paths or query existence.</summary>
/// <param name="Kind">The target kind.</param>
/// <param name="Path">The unchanged path or URL.</param>
/// <param name="Line">The optional one-based line.</param>
/// <param name="Column">The optional one-based column.</param>
public sealed record LinkTarget(LinkKind Kind, string Path, int? Line = null, int? Column = null);

/// <summary>A UTF-16 link span.</summary>
/// <param name="Start">The zero-based start.</param>
/// <param name="Length">The span length.</param>
/// <param name="Target">The complete target.</param>
public sealed record ConsoleLinkSpan(int Start, int Length, LinkTarget Target);

/// <summary>A pure Unicode scanner. It performs no file-system or process operations.</summary>
public static partial class ConsoleLinkScanner
{
    /// <summary>Finds URLs first, quoted paths second, then absolute and relative paths.</summary>
    /// <remarks>A trailing slash denotes a folder. Ambiguous extensionless paths remain files;
    /// apps identify other folders with structured spans. Separator-free
    /// location targets require a non-leading dot followed by a letter. Candidates exceeding
    /// 4,096 UTF-16 characters, including location suffixes and excluding quotes, are skipped.</remarks>
    public static ImmutableArray<ConsoleLinkSpan> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Delegate to the segmented engine so path names, starts and quote boundaries have one definition.
        // Both entry points preserve the same scalar-aware grammar and UTF-16 span offsets.
        return Scan(new InMemoryLogTextContent(text));
    }

    private static LinkTarget ParsePath(string value)
    {
        var suffix = SuffixPattern().Match(value);
        int? line = null;
        int? column = null;
        if (suffix.Success && int.TryParse(suffix.Groups["line"].Value, out var parsedLine))
        {
            line = parsedLine;
            if (int.TryParse(suffix.Groups["column"].Value, out var parsedColumn)) column = parsedColumn;
            value = value[..suffix.Index];
        }
        var kind = value.Length != 0 && value[^1] is '/' or '\\' ? LinkKind.Folder : LinkKind.File;
        return new LinkTarget(kind, value, line, column);
    }

    private static string TrimTarget(string value)
    {
        value = value.TrimEnd('.', ',', ';', '!', '?');
        while (value.Length != 0)
        {
            var closing = value[^1];
            var opening = closing switch { ')' => '(', ']' => '[', '}' => '{', _ => '\0' };
            if (opening == '\0' || value.Count(c => c == closing) <= value.Count(c => c == opening)) break;
            value = value[..^1];
        }
        return value;
    }

    [GeneratedRegex("(?:\\((?<line>[1-9][0-9]*)(?:,(?<column>[1-9][0-9]*))?\\)|:(?<line>[1-9][0-9]*)(?::(?<column>[1-9][0-9]*))?)$", RegexOptions.CultureInvariant)]
    private static partial Regex SuffixPattern();
}

/// <summary>An immutable sorted interval index for row-local hit testing.</summary>
public sealed class ConsoleLinkIndex
{
    /// <summary>Creates an index. Spans must be positive, nonoverlapping, and within the supplied text length.</summary>
    public ConsoleLinkIndex(IEnumerable<ConsoleLinkSpan> spans, int textLength)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);
        var supplied = spans is ImmutableArray<ConsoleLinkSpan> array ? array : spans.ToImmutableArray();
        var order = ValidateOrder(supplied, textLength, out _);
        Spans = order is null ? supplied : order.Select(index => supplied[index]).ToImmutableArray();
    }

    // Admission validates without an index. The result counts order, sort and overlap comparisons.
    internal static int Validate(ImmutableArray<ConsoleLinkSpan> spans, int textLength)
    {
        ValidateOrder(spans, textLength, out var comparisons);
        return comparisons;
    }

    private static int[]? ValidateOrder(ImmutableArray<ConsoleLinkSpan> spans, int textLength, out int comparisons)
    {
        if (spans.IsDefault) throw new ArgumentException("Link spans must be initialized.", nameof(spans));
        var count = 0;
        var sorted = true;
        for (var i = 0; i < spans.Length; i++)
        {
            var span = spans[i];
            if (span is null || span.Target is null || span.Start < 0 || span.Length <= 0
                || span.Start > textLength - span.Length)
                throw new ArgumentException("Link spans must have targets and valid ranges.", nameof(spans));
            if (i != 0)
            {
                count++;
                sorted &= spans[i - 1].Start <= span.Start;
            }
        }
        int[]? order = null;
        if (!sorted)
        {
            order = Enumerable.Range(0, spans.Length).ToArray();
            Array.Sort(order, (left, right) =>
            {
                count++;
                return spans[left].Start.CompareTo(spans[right].Start);
            });
        }
        for (var i = 1; i < spans.Length; i++)
        {
            var previous = spans[order is null ? i - 1 : order[i - 1]];
            var current = spans[order is null ? i : order[i]];
            count++;
            if (current.Start < previous.Start + previous.Length)
                throw new ArgumentException("Link spans must be nonoverlapping.", nameof(spans));
        }
        comparisons = count;
        return order;
    }

    /// <summary>Gets ordered spans.</summary>
    public ImmutableArray<ConsoleLinkSpan> Spans { get; }

    /// <summary>Finds a span in logarithmic time. The end offset is exclusive.</summary>
    public ConsoleLinkSpan? HitTest(int offset)
    {
        var low = 0;
        var high = Spans.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var span = Spans[middle];
            if (offset < span.Start) high = middle - 1;
            else if (offset >= span.Start + span.Length) low = middle + 1;
            else return span;
        }
        return null;
    }
}
