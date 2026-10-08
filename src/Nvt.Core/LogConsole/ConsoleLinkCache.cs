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
    // _cacheGate protects the cache, live revisions, and the revision stamp.
    private readonly Dictionary<long, CachedLinks> _cache = [];
    private ImmutableDictionary<long, long> _liveVersions = ImmutableDictionary<long, long>.Empty;
    private CacheRevision? _revision;

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
        var textVersion = entry.TextContent.Version;
        var textLength = entry.TextContent.Length;
        var requested = new CacheRevision(snapshot.Version, snapshot.Generation);
        lock (_cacheGate)
        {
            SynchronizeCore(snapshot);
            var current = _revision == requested
                && _liveVersions.TryGetValue(entry.EntryId, out var version) && version == textVersion
                && entry.Generation == snapshot.Generation;
            if (current && _cache.TryGetValue(entry.EntryId, out var cached)) return cached.Index;
        }
        var spans = entry.LinkSpans ?? ConsoleLinkScanner.Scan(entry.TextContent);
        var index = new ConsoleLinkIndex(spans, textLength);
        var characters = spans.Sum(span => (long)span.Target.Path.Length);
        lock (_cacheGate)
        {
            var current = _revision == requested
                && _liveVersions.TryGetValue(entry.EntryId, out var version) && version == textVersion
                && entry.Generation == snapshot.Generation;
            if (current && _cache.TryGetValue(entry.EntryId, out var cached)) return cached.Index;
            if (current && spans.Length <= _maxSpans && characters <= _maxTargetCharacters)
            {
                while (_cache.Count >= _maxEntries || _cache.Values.Sum(c => (long)c.Index.Spans.Length) + spans.Length > _maxSpans
                    || _cache.Values.Sum(c => c.Index.Spans.Sum(span => (long)span.Target.Path.Length)) + characters > _maxTargetCharacters)
                    _cache.Remove(_cache.First().Key);
                _cache[entry.EntryId] = new CachedLinks(textVersion, index);
            }
            return index;
        }
    }

    private void SynchronizeCore(LogSnapshot snapshot)
    {
        if (_revision is { } prior && snapshot.Version < prior.Version) return;
        var next = new CacheRevision(snapshot.Version, snapshot.Generation);
        if (next == _revision) return;
        if (_revision?.Generation != snapshot.Generation) _cache.Clear();
        _liveVersions = snapshot.Entries.ToImmutableDictionary(e => e.EntryId, e => e.TextContent.Version);
        foreach (var pair in _cache.ToArray())
            if (!_liveVersions.TryGetValue(pair.Key, out var version) || pair.Value.TextVersion != version) _cache.Remove(pair.Key);
        _revision = next;
    }

    private sealed record CacheRevision(long Version, long Generation);
    private sealed record CachedLinks(long TextVersion, ConsoleLinkIndex Index);
}

