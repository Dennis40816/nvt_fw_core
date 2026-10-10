// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;

namespace Nvt.Core.LogConsole;

/// <summary>A bounded scan cache. Synchronize is the sole invalidation point.</summary>
public sealed class ConsoleLinkCache
{
    private readonly object _cacheGate = new();
    private readonly int _maxEntries;
    private readonly int _maxSpans;
    private readonly long _maxTargetCharacters;
    // _cacheGate protects the cache and its atomic live-revision state.
    private readonly Dictionary<ConsoleRowId, CachedLinks> _cache = [];
    private CacheState? _state;

    /// <summary>Creates an independently bounded cache. Oversized results are returned without caching.</summary>
    public ConsoleLinkCache(int maxEntries = 10_000, int maxSpans = 65_536, long maxTargetCharacters = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpans);
        _maxEntries = maxEntries;
        _maxSpans = maxSpans;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTargetCharacters);
        _maxTargetCharacters = maxTargetCharacters;
    }

    /// <summary>Invalidates by snapshot version, live membership, and text revision.</summary>
    /// <remarks>Call for every accepted snapshot, including Clear. Older snapshots cannot repopulate the cache.</remarks>
    public void Synchronize(LogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_cacheGate) SynchronizeCore(snapshot);
    }

    /// <summary>Gets app spans or scans this entry once for the current content revision.</summary>
    public ConsoleLinkIndex GetLinks(LogSnapshot snapshot, LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(entry);
        return GetLinksCore(snapshot, new ConsoleRowId(entry.EntryId), entry.EntryId, entry.Generation,
            entry.TextContent.Version, entry.TextContent, entry.LinkSpans);
    }

    /// <summary>Gets bounded, cached links directly from a projected entry or dedupe row's leased content.</summary>
    /// <remarks>Use the snapshot that produced the row. Group scans use the latest retained representative,
    /// never look up an evicted original member, and share all cache budgets with raw-entry scans.</remarks>
    public ConsoleLinkIndex GetLinks(LogSnapshot snapshot, ConsoleRow row)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(row);
        // A row always belongs to the snapshot that produced it, so the generation check is true by construction.
        // The text version and representative checks still reject a stale row.
        return GetLinksCore(snapshot, row.Id, row.LastSequence, snapshot.Generation,
            row.TextVersion, row.TextContent, row.LinkSpans);
    }

    private ConsoleLinkIndex GetLinksCore(LogSnapshot snapshot, ConsoleRowId id, long representativeId,
        long generation, long textVersion, ILogTextContent content, ImmutableArray<ConsoleLinkSpan>? suppliedSpans)
    {
        var textLength = content.Length;
        var requested = new CacheRevision(snapshot.Version, snapshot.Generation);
        var text = new LiveText(textVersion, representativeId);
        bool IsCurrent() => _state is { } state && state.Revision == requested && generation == snapshot.Generation
            && state.LiveVersions.TryGetValue(id, out var live) && live == text;
        lock (_cacheGate)
        {
            SynchronizeCore(snapshot);
            if (IsCurrent() && _cache.TryGetValue(id, out var cached)) return cached.Index;
        }
        // Both public entry points use the same segmented scanner and publication path.
        var spans = suppliedSpans ?? ConsoleLinkScanner.Scan(content);
        var index = new ConsoleLinkIndex(spans, textLength);
        var characters = spans.Sum(span => (long)span.Target.Path.Length);
        lock (_cacheGate)
        {
            var current = IsCurrent();
            if (current && _cache.TryGetValue(id, out var cached)) return cached.Index;
            if (current && spans.Length <= _maxSpans && characters <= _maxTargetCharacters)
            {
                while (_cache.Count >= _maxEntries || _cache.Values.Sum(c => (long)c.Index.Spans.Length) + spans.Length > _maxSpans
                    || _cache.Values.Sum(c => c.Index.Spans.Sum(span => (long)span.Target.Path.Length)) + characters > _maxTargetCharacters)
                    _cache.Remove(_cache.First().Key);
                _cache[id] = new CachedLinks(text, index);
            }
            return index;
        }
    }

    private void SynchronizeCore(LogSnapshot snapshot)
    {
        var prior = _state?.Revision;
        if (prior is not null && snapshot.Version < prior.Version) return;
        var next = new CacheRevision(snapshot.Version, snapshot.Generation);
        if (next == prior) return;
        if (prior?.Generation != snapshot.Generation) _cache.Clear();
        var live = ImmutableDictionary.CreateBuilder<ConsoleRowId, LiveText>();
        foreach (var entry in snapshot.Entries)
        {
            var text = new LiveText(entry.TextContent.Version, entry.EntryId);
            live[new ConsoleRowId(entry.EntryId)] = text;
            // Sequence order makes the last retained member the group's representative, as in projection.
            live[new ConsoleRowId(entry.GroupId, true)] = text;
        }
        var versions = live.ToImmutable();
        foreach (var pair in _cache.ToArray())
            if (!versions.TryGetValue(pair.Key, out var version) || pair.Value.Text != version) _cache.Remove(pair.Key);
        _state = new CacheState(next, versions);
    }

    private sealed record CacheState(CacheRevision Revision, ImmutableDictionary<ConsoleRowId, LiveText> LiveVersions);
    private sealed record CacheRevision(long Version, long Generation);
    private readonly record struct LiveText(long Version, long RepresentativeId);
    private sealed record CachedLinks(LiveText Text, ConsoleLinkIndex Index);
}

