// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Nvt.Core.SourceFileNavigation;

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

/// <summary>A bounded app resolution request. The resolver must honor both positive limits and cancellation.</summary>
/// <param name="Target">The syntactic target to resolve.</param>
/// <param name="MaxCandidates">The maximum returned candidates.</param>
/// <param name="MaxProbes">The maximum app lookup operations.</param>
public sealed record ConsolePathResolutionRequest(LinkTarget Target, int MaxCandidates = 16, int MaxProbes = 128);

/// <summary>An app-owned asynchronous, bounded path policy. Core supplies no repository search implementation.</summary>
public interface IConsolePathResolver
{
    /// <summary>Gets the policy revision used to invalidate cached results.</summary>
    long PolicyVersion { get; }
    /// <summary>Returns candidates without choosing an ambiguous result. Limits and cancellation must be honored.</summary>
    ValueTask<ImmutableArray<LinkTarget>> ResolveAsync(ConsolePathResolutionRequest request, CancellationToken cancellationToken);
}

/// <summary>Capabilities for one full target.</summary>
/// <param name="CanOpen">Whether the target can be opened.</param>
/// <param name="CanOpenContainingFolder">Whether a file's parent can be opened.</param>
/// <param name="SupportsLine">Whether the opener can navigate to a line.</param>
/// <param name="SupportsColumn">Whether the opener can navigate to a column.</param>
public sealed record ConsoleLinkCapabilities(bool CanOpen, bool CanOpenContainingFolder, bool SupportsLine, bool SupportsColumn);

/// <summary>The app-owned open adapter. No disk or process implementation is provided here.</summary>
public interface IConsoleLinkOpener
{
    /// <summary>Reports capabilities for the full target, including line and column.</summary>
    ConsoleLinkCapabilities GetCapabilities(LinkTarget target);
    /// <summary>Opens the full target and returns Core's existing navigation result.</summary>
    ValueTask<SourceFileOpenResult> OpenAsync(LinkTarget target, CancellationToken cancellationToken);
}

/// <summary>A pure Unicode scanner. It performs no file-system, resolver, or process operations.</summary>
public static partial class ConsoleLinkScanner
{
    /// <summary>Finds URLs first, quoted paths second, then absolute and relative paths.</summary>
    /// <remarks>A trailing slash denotes a folder. Ambiguous extensionless paths remain files;
    /// apps identify other folders with structured spans or resolver candidates.</remarks>
    public static ImmutableArray<ConsoleLinkSpan> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Delegate to the segmented engine so IsPathName and IsPathStart have one definition.
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
        var kind = line is null && value.Length != 0 && value[^1] is '/' or '\\' ? LinkKind.Folder : LinkKind.File;
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
        Spans = spans.OrderBy(s => s.Start).ToImmutableArray();
        var end = 0;
        foreach (var span in Spans)
        {
            if (span.Start < end || span.Length <= 0 || span.Start > textLength - span.Length)
                throw new ArgumentException("Link spans must be in range and nonoverlapping.", nameof(spans));
            end = span.Start + span.Length;
        }
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
