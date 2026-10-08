// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Globalization;

namespace Nvt.Core.LogConsole;

/// <summary>Pure projection over one frozen version, one filter, and one view state.</summary>
public static class ConsoleProjector
{
    /// <summary>Computes rows, hits, counts, membership, and paused order without clock or file-system access.</summary>
    public static ConsoleProjection Project(LogSnapshot snapshot, ConsoleFilter filter, ConsoleViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(viewState);
        var pause = viewState.Follow as ConsoleFollow.Paused;
        var currentPause = pause?.Anchor.Generation == snapshot.Generation ? pause : null;
        var timeBase = currentPause?.PausedAt ?? snapshot.CapturedAt;
        var levels = Enum.GetValues<LogLevel>().ToDictionary(level => level, _ => 0);
        var sources = new Dictionary<string, int>(StringComparer.Ordinal);
        var membership = ImmutableDictionary.CreateBuilder<ConsoleRowId, ImmutableArray<long>>();
        foreach (var entry in snapshot.Entries)
        {
            if (MatchesSource(entry.SourceId, filter)) levels[entry.Level]++;
            sources.TryAdd(entry.SourceId, 0);
            if (filter.EnabledLevels.Contains(entry.Level)) sources[entry.SourceId]++;
            membership[new ConsoleRowId(entry.EntryId)] = [entry.Sequence];
        }
        var groups = snapshot.Entries.GroupBy(entry => entry.GroupId).ToArray();
        foreach (var group in groups)
            membership[new ConsoleRowId(group.Key, true)] = group.Select(e => e.Sequence).ToImmutableArray();

        var rows = new List<ConsoleRow>();
        var newEvents = 0;
        IEnumerable<IEnumerable<LogEntry>> candidates = filter.Deduplicate
            ? groups : snapshot.Entries.Select(entry => new[] { entry });
        foreach (var candidate in candidates)
        {
            var entries = candidate.ToArray();
            var first = entries[0];
            var last = entries[^1];
            if (!filter.EnabledLevels.Contains(last.Level) || !MatchesSource(last.SourceId, filter)) continue;
            var hits = FindHits(last.TextContent, last.SourceId, filter.SearchText);
            if (filter.OnlyMatches && filter.SearchText.Length != 0 && hits.IsEmpty) continue;
            var id = filter.Deduplicate ? new ConsoleRowId(last.GroupId, true) : new ConsoleRowId(last.EntryId);
            rows.Add(new ConsoleRow(id, last.Level, last.SourceId, last.TextContent, last.TextContent.Version, last.LinkSpans,
                entries.Select(e => e.Sequence).ToImmutableArray(), first.Timestamp, last.Timestamp, last.Sequence,
                hits, FormatTime(last.Timestamp, timeBase, filter.TimeMode)));
            if (currentPause is not null)
                newEvents += entries.Count(e => e.Sequence > currentPause.Anchor.ThroughSequence);
        }
        if (currentPause is not null)
        {
            var order = currentPause.Anchor.RowOrder.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
            rows = rows.OrderBy(row => order.TryGetValue(row.Id, out var index) ? index : int.MaxValue)
                .ThenBy(row => row.Id.Value).ToList();
        }
        else rows.Sort(static (a, b) => a.LastSequence.CompareTo(b.LastSequence));

        ConsoleRowId? anchor = null;
        if (currentPause?.Anchor.RowId is { } requested)
        {
            var surviving = rows.Select(row => row.Id).ToHashSet();
            var anchorIndex = currentPause.Anchor.RowOrder.IndexOf(requested);
            anchor = rows.Find(r => r.Id == requested)?.Id
                ?? currentPause.Anchor.RowOrder.Skip(anchorIndex + 1).Where(surviving.Contains)
                    .Select(id => (ConsoleRowId?)id).FirstOrDefault()
                ?? rows.Where(r => r.LastSequence >= currentPause.Anchor.Sequence)
                    .MinBy(r => r.LastSequence)?.Id
                ?? rows.LastOrDefault()?.Id;
        }
        // Acquire leases only after all app reads and projection validation succeed.
        var leased = new List<ConsoleRow>();
        try
        {
            foreach (var row in rows) leased.Add(row with { TextContent = ContentOwner.Retain(row.TextContent) });
        }
        catch
        {
            foreach (var row in leased) row.TextContent.Dispose();
            throw;
        }
        return new ConsoleProjection
        {
            Version = snapshot.Version, Generation = snapshot.Generation, LastSequence = snapshot.LastSequence,
            CapturedAt = snapshot.CapturedAt, TimeBase = timeBase, Rows = leased.ToImmutableArray(),
            LevelCounts = levels.ToImmutableDictionary(), SourceCounts = sources.ToImmutableDictionary(StringComparer.Ordinal),
            RetainedMembership = membership.ToImmutable(), EventCount = snapshot.EventCount,
            NewSincePauseCount = newEvents, EvictedCount = snapshot.EvictedCount, Deduplicate = filter.Deduplicate,
            ResolvedAnchorId = anchor,
        };
    }

    private static bool MatchesSource(string source, ConsoleFilter filter)
        => filter.SelectedSources.IsEmpty || filter.SelectedSources.Contains(source);

    private static ImmutableArray<ConsoleSearchHit> FindHits(ILogTextContent message, string source, string query)
    {
        if (query.Length == 0) return [];
        var hits = ImmutableArray.CreateBuilder<ConsoleSearchHit>();
        if (message.Length >= query.Length)
        {
            // Retain only enough overlap to find a literal spanning any chunk boundary.
            var buffer = new char[checked(1024 + query.Length - 1)];
            var carry = 0;
            var nextStart = 0;
            for (var offset = 0; offset < message.Length;)
            {
                var read = Math.Min(1024, message.Length - offset);
                message.Read(offset, buffer.AsSpan(carry, read));
                var length = carry + read;
                var baseOffset = offset - carry;
                var start = Math.Max(0, nextStart - baseOffset);
                while (start <= length - query.Length)
                {
                    var found = buffer.AsSpan(start, length - start).IndexOf(query.AsSpan(), StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    start += found;
                    hits.Add(new ConsoleSearchHit(ConsoleSearchArea.Message, baseOffset + start, query.Length));
                    start += query.Length;
                    nextStart = baseOffset + start;
                }
                carry = Math.Min(query.Length - 1, length);
                buffer.AsSpan(length - carry, carry).CopyTo(buffer);
                offset += read;
            }
        }
        Add(source, ConsoleSearchArea.Source);
        return hits.ToImmutable();
        void Add(string text, ConsoleSearchArea area)
        {
            for (var offset = 0; offset <= text.Length - query.Length;)
            {
                var start = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                hits.Add(new ConsoleSearchHit(area, start, query.Length));
                offset = start + query.Length;
            }
        }
    }

    private static string FormatTime(DateTimeOffset timestamp, DateTimeOffset timeBase, ConsoleTimeMode mode) => mode switch
    {
        ConsoleTimeMode.Absolute => timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
        ConsoleTimeMode.Relative => (timeBase - timestamp).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s ago",
        ConsoleTimeMode.Hidden => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
