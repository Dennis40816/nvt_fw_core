// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;

namespace Nvt.Core.LogConsole;

/// <summary>A reading position and the presentation order frozen at pause.</summary>
/// <param name="RowId">The first visible row, if any.</param>
/// <param name="Sequence">Its sequence, used to find a retained successor after eviction.</param>
/// <param name="TextOffset">The position within expanded text.</param>
/// <param name="PixelOffset">The offset within the row in host units.</param>
/// <param name="Generation">The snapshot generation.</param>
/// <param name="ThroughSequence">The newest event at pause.</param>
/// <param name="RowOrder">The fixed row order at pause.</param>
public sealed record ConsoleReadingAnchor(ConsoleRowId? RowId, long Sequence, int TextOffset, double PixelOffset,
    long Generation, long ThroughSequence, ImmutableArray<ConsoleRowId> RowOrder);

/// <summary>The closed following state. Only Following and Paused can be constructed.</summary>
public abstract class ConsoleFollow
{
    private ConsoleFollow() { }
    /// <summary>New events follow the latest sequence order.</summary>
    public sealed class Following : ConsoleFollow { }
    /// <summary>The host preserves this reading position until explicit resume.</summary>
    /// <param name="Anchor">The fixed order and reading position.</param>
    /// <param name="PausedAt">The fixed relative-time base.</param>
    public sealed class Paused(ConsoleReadingAnchor Anchor, DateTimeOffset PausedAt) : ConsoleFollow
    {
        /// <summary>Gets the fixed order and reading position.</summary>
        public ConsoleReadingAnchor Anchor { get; } = Anchor;
        /// <summary>Gets the fixed relative-time base.</summary>
        public DateTimeOffset PausedAt { get; } = PausedAt;
    }
}

/// <summary>The single immutable console view state. Hosts replace this value through their commands.</summary>
public sealed record ConsoleViewState
{
    /// <summary>Gets the follow mode.</summary>
    public ConsoleFollow Follow { get; init; } = new ConsoleFollow.Following();
    /// <summary>Gets expanded row identities.</summary>
    public ImmutableHashSet<ConsoleRowId> ExpandedIds { get; init; } = [];
    /// <summary>Gets canonical selected raw EntryIds. A group is selected when any member is selected.</summary>
    public ImmutableHashSet<long> Selection { get; init; } = [];
    /// <summary>Gets whether the console is expanded. This does not control Follow.</summary>
    public bool IsExpanded { get; init; } = true;

    /// <summary>Captures a pause without changing expansion or selection.</summary>
    public ConsoleViewState Pause(ConsoleProjection projection, ConsoleRowId? rowId = null,
        int textOffset = 0, double pixelOffset = 0, DateTimeOffset? pausedAt = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var row = projection.Rows.FirstOrDefault(r => r.Id == rowId);
        var anchor = new ConsoleReadingAnchor(rowId, row?.LastSequence ?? projection.LastSequence,
            textOffset, pixelOffset, projection.Generation, projection.LastSequence,
            projection.Rows.Select(r => r.Id).ToImmutableArray());
        return this with { Follow = new ConsoleFollow.Paused(anchor, pausedAt ?? projection.CapturedAt) };
    }

    /// <summary>Explicitly resumes following. Collapse and expansion never call this implicitly.</summary>
    public ConsoleViewState Resume() => this with { Follow = new ConsoleFollow.Following() };

    /// <summary>Preserves selected raw events, maps expansion, and removes evicted identities.</summary>
    /// <remarks>Hidden retained selections survive. Export still selects only visible rows.</remarks>
    public ConsoleViewState Remap(ConsoleProjection previous, ConsoleProjection current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ImmutableHashSet<ConsoleRowId> Map(ImmutableHashSet<ConsoleRowId> ids)
        {
            var events = ids.Where(previous.RetainedMembership.ContainsKey)
                .SelectMany(id => previous.RetainedMembership[id]).ToHashSet();
            return current.RetainedMembership.Where(pair => pair.Key.IsGroup == current.Deduplicate
                && pair.Value.Any(events.Contains)).Select(pair => pair.Key).ToImmutableHashSet();
        }
        var retained = current.RetainedMembership.Keys.Where(id => !id.IsGroup).Select(id => id.Value).ToHashSet();
        var follow = Follow;
        if (Follow is ConsoleFollow.Paused paused && paused.Anchor.Generation == current.Generation)
        {
            var rowsByEntry = current.RetainedMembership.Where(pair => pair.Key.IsGroup == current.Deduplicate)
                .SelectMany(pair => pair.Value.Select(entry => (entry, pair.Key))).ToDictionary(pair => pair.entry, pair => pair.Key);
            IEnumerable<ConsoleRowId> MapReadingRow(ConsoleRowId id)
                => previous.RetainedMembership.TryGetValue(id, out var members)
                    ? members.Where(rowsByEntry.ContainsKey).Select(entry => rowsByEntry[entry]).Distinct()
                    : current.RetainedMembership.ContainsKey(id) ? [id] : [];
            var anchor = paused.Anchor;
            ConsoleRowId? rowId = anchor.RowId is { } requested
                ? previous.RetainedMembership.TryGetValue(requested, out var members) && members.Contains(anchor.Sequence)
                    && rowsByEntry.TryGetValue(anchor.Sequence, out var sameEntryRow)
                    ? sameEntryRow : MapReadingRow(requested).Select(id => (ConsoleRowId?)id).FirstOrDefault() ?? requested
                : null;
            var order = anchor.RowOrder.SelectMany(id => MapReadingRow(id).DefaultIfEmpty(id)).Distinct().ToImmutableArray();
            follow = new ConsoleFollow.Paused(anchor with { RowId = rowId, RowOrder = order }, paused.PausedAt);
        }
        return this with
        {
            Follow = follow,
            Selection = Selection.Where(retained.Contains).ToImmutableHashSet(),
            ExpandedIds = Map(ExpandedIds),
        };
    }
}

/// <summary>The searchable part of a row.</summary>
public enum ConsoleSearchArea
{
    /// <summary>The full message.</summary>
    Message,
    /// <summary>The source ID.</summary>
    Source,
}

/// <summary>A UTF-16 literal search hit.</summary>
/// <param name="Area">The text containing the hit.</param>
/// <param name="Start">The zero-based offset.</param>
/// <param name="Length">The matched UTF-16 length.</param>
public readonly record struct ConsoleSearchHit(ConsoleSearchArea Area, int Start, int Length);

/// <summary>A frozen entry or dedupe row. Content is leased by its projection.</summary>
/// <param name="Id">The stable row identity.</param>
/// <param name="Level">The level.</param>
/// <param name="SourceId">The source.</param>
/// <param name="TextContent">The full unchanged segmented message.</param>
/// <param name="TextVersion">The representative text revision.</param>
/// <param name="LinkSpans">App-supplied spans, if any.</param>
/// <param name="MemberSequences">Retained raw event sequences, in sequence order.</param>
/// <param name="FirstTimestamp">The first retained occurrence time.</param>
/// <param name="Timestamp">The last retained occurrence time.</param>
/// <param name="LastSequence">The last retained occurrence sequence.</param>
/// <param name="SearchHits">Message and source hit ranges.</param>
/// <param name="TimeText">Time text formatted with projection options, or empty when hidden.</param>
public sealed record ConsoleRow(ConsoleRowId Id, LogLevel Level, string SourceId, ILogTextContent TextContent, long TextVersion,
    ImmutableArray<ConsoleLinkSpan>? LinkSpans, ImmutableArray<long> MemberSequences, DateTimeOffset FirstTimestamp,
    DateTimeOffset Timestamp, long LastSequence, ImmutableArray<ConsoleSearchHit> SearchHits, string TimeText)
{
    /// <summary>Gets the retained occurrence count.</summary>
    public int Count => MemberSequences.Length;

    /// <summary>Derives a bounded collapsed preview without storing newline or truncation state.</summary>
    /// <param name="maxCharacters">The UTF-16 output cap, from 0 through 4,096; default 1,024.</param>
    public ConsoleFirstLine GetFirstLine(int maxCharacters = 1024) => ConsoleFirstLine.Read(TextContent, maxCharacters);
}

/// <summary>The sole computed output used by display, copy, and export.</summary>
// All owned leases are reachable exclusively through Rows.TextContent; other members are immutable metadata.
// An internal row-order copy carrying every row once transfers those leases without retaining them again.
// Such a transfer drops the original projection without disposing it; the copy becomes the sole lease owner.
public sealed class ConsoleProjection : IDisposable
{
    /// <summary>Gets the frozen revision.</summary>
    public required long Version { get; init; }
    /// <summary>Gets the shared generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the latest assigned event sequence.</summary>
    public required long LastSequence { get; init; }
    /// <summary>Gets the snapshot clock instant.</summary>
    public required DateTimeOffset CapturedAt { get; init; }
    /// <summary>Gets the relative-time base.</summary>
    public required DateTimeOffset TimeBase { get; init; }
    /// <summary>Gets every filtered row, including rows outside the viewport.</summary>
    public required ImmutableArray<ConsoleRow> Rows { get; init; }
    /// <summary>Gets counts after source filtering only, including zero for each level.</summary>
    public required ImmutableDictionary<LogLevel, int> LevelCounts { get; init; }
    /// <summary>Gets declared sources followed by unknown sources in first retained appearance order.</summary>
    public required ImmutableArray<ConsoleSource> Sources { get; init; }
    /// <summary>Gets counts after level filtering only, including zero for declared and retained sources.</summary>
    public required ImmutableDictionary<string, int> SourceCounts { get; init; }
    /// <summary>Gets all retained event and group memberships for selection remapping.</summary>
    public required ImmutableDictionary<ConsoleRowId, ImmutableArray<long>> RetainedMembership { get; init; }
    /// <summary>Gets the number of retained raw events before filters.</summary>
    public required int EventCount { get; init; }
    /// <summary>Gets matching new raw events, including dedupe increments.</summary>
    public required int NewSincePauseCount { get; init; }
    /// <summary>Gets the generation's capacity eviction total.</summary>
    public required long EvictedCount { get; init; }
    /// <summary>Gets whether this projection groups duplicates.</summary>
    public required bool Deduplicate { get; init; }
    /// <summary>Gets the surviving anchor or nearest retained successor.</summary>
    public ConsoleRowId? ResolvedAnchorId { get; init; }
    /// <summary>Gets the visible row count.</summary>
    public int RowCount => Rows.Length;
    /// <summary>Gets the raw event count represented by visible rows.</summary>
    public int MatchingEventCount => Rows.Sum(row => row.Count);
    /// <summary>Gets the number of merged visible duplicates.</summary>
    public int DuplicatesMerged => MatchingEventCount - RowCount;
    /// <summary>Gets whether no row matches.</summary>
    public bool IsEmpty => Rows.IsEmpty;

    /// <summary>Releases the row content leases. Dispose after display and export consumption.</summary>
    public void Dispose()
    {
        foreach (var row in Rows) row.TextContent.Dispose();
    }
}
