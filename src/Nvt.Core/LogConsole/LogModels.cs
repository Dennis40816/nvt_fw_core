// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;

namespace Nvt.Core.LogConsole;

/// <summary>The six supported event levels.</summary>
public enum LogLevel
{
    /// <summary>Fine-grained tracing.</summary>
    Trace,
    /// <summary>Debug information.</summary>
    Debug,
    /// <summary>Information.</summary>
    Info,
    /// <summary>A warning.</summary>
    Warn,
    /// <summary>An error.</summary>
    Error,
    /// <summary>A fatal error.</summary>
    Fatal,
}

/// <summary>The time column mode.</summary>
public enum ConsoleTimeMode
{
    /// <summary>Clock time in the app's explicit display zone; UTC by default.</summary>
    Absolute,
    /// <summary>Seconds before the projection's time base.</summary>
    Relative,
    /// <summary>No time column.</summary>
    Hidden,
}

/// <summary>Event-filter inputs. An empty source set selects all sources.</summary>
public sealed record ConsoleFilter
{
    /// <summary>Gets the enabled levels. An empty set selects no levels.</summary>
    public ImmutableHashSet<LogLevel> EnabledLevels { get; init; } = Enum.GetValues<LogLevel>().ToImmutableHashSet();
    /// <summary>Gets selected source IDs, compared ordinally.</summary>
    public ImmutableHashSet<string> SelectedSources { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    /// <summary>Gets the literal search text.</summary>
    public string SearchText { get; init; } = string.Empty;
    /// <summary>Gets whether search hides rows without a hit.</summary>
    public bool OnlyMatches { get; init; } = true;
    /// <summary>Gets whether equal full messages are grouped.</summary>
    public bool Deduplicate { get; init; }
    /// <summary>Gets the time column mode.</summary>
    public ConsoleTimeMode TimeMode { get; init; } = ConsoleTimeMode.Absolute;
}

/// <summary>A stable row identity. The discriminator separates entry IDs from group IDs.</summary>
/// <param name="Value">The monotonic identity, never reused after Clear.</param>
/// <param name="IsGroup">Whether the identity belongs to a dedupe group.</param>
public readonly record struct ConsoleRowId(long Value, bool IsGroup = false);

/// <summary>An immutable event. Sequence alone determines event order.</summary>
/// <remarks>EntryId equals Sequence for every store-created entry. Projection membership and
/// selection use this invariant; callers constructing entries must preserve it.</remarks>
/// <param name="EntryId">The stable identity.</param>
/// <param name="Generation">The store generation.</param>
/// <param name="Sequence">The globally increasing sequence.</param>
/// <param name="Timestamp">The UTC occurrence time.</param>
/// <param name="Level">The event level.</param>
/// <param name="SourceId">The app-supplied source ID.</param>
/// <param name="TextContent">The immutable, segmented content.</param>
/// <param name="LinkSpans">Optional app-supplied spans. Even an empty array overrides scanning.</param>
/// <param name="GroupId">The stable dedupe identity assigned by the store.</param>
public sealed record LogEntry(long EntryId, long Generation, long Sequence, DateTimeOffset Timestamp,
    LogLevel Level, string SourceId, ILogTextContent TextContent, ImmutableArray<ConsoleLinkSpan>? LinkSpans, long GroupId);

/// <summary>An app input. A successful Add transfers exclusive ownership of its content to the store.</summary>
/// <param name="Level">The event level.</param>
/// <param name="SourceId">The stable source ID.</param>
/// <param name="TextContent">Immutable content owned by this input until accepted.</param>
/// <param name="Timestamp">Optional occurrence time. Otherwise the injected clock supplies it.</param>
/// <param name="LinkSpans">Optional structured links.</param>
public sealed record LogWrite(LogLevel Level, string SourceId, ILogTextContent TextContent,
    DateTimeOffset? Timestamp = null, ImmutableArray<ConsoleLinkSpan>? LinkSpans = null);

/// <summary>A frozen, leased store view. Dispose it when projection or delta consumption finishes.</summary>
public sealed class LogSnapshot : IDisposable
{
    // Content leases are released exactly once through Interlocked. All other state is immutable.
    private int _disposed;

    internal LogSnapshot(long version, long generation, long lastSequence, long evictedCount,
        DateTimeOffset capturedAt, ImmutableArray<LogEntry> entries)
    {
        Version = version;
        Generation = generation;
        LastSequence = lastSequence;
        EvictedCount = evictedCount;
        CapturedAt = capturedAt;
        Entries = entries;
    }

    /// <summary>Gets the batch revision.</summary>
    public long Version { get; }
    /// <summary>Gets the Clear generation.</summary>
    public long Generation { get; }
    /// <summary>Gets the latest assigned sequence, including already evicted entries.</summary>
    public long LastSequence { get; }
    /// <summary>Gets the first retained sequence, or null for an empty store.</summary>
    public long? FirstRetainedSequence => Entries.IsEmpty ? null : Entries[0].Sequence;
    /// <summary>Gets the last retained sequence, or null for an empty store.</summary>
    public long? LastRetainedSequence => Entries.IsEmpty ? null : Entries[^1].Sequence;
    /// <summary>Gets the number of retained raw events.</summary>
    public int EventCount => Entries.Length;
    /// <summary>Gets capacity evictions in this generation. Clear resets this counter.</summary>
    public long EvictedCount { get; }
    /// <summary>Gets the clock instant captured with this snapshot.</summary>
    public DateTimeOffset CapturedAt { get; }
    /// <summary>Gets retained events in sequence order. Content stays readable until disposal.</summary>
    public ImmutableArray<LogEntry> Entries { get; }

    /// <summary>Releases snapshot leases. This does not remove store entries.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            foreach (var entry in Entries)
            {
                entry.TextContent.Dispose();
            }
        }
    }
}

/// <summary>A versioned batch delta. Its entries are leased by Snapshot.</summary>
public sealed class LogChangeSet : IDisposable
{
    internal LogChangeSet(long fromVersion, LogSnapshot snapshot, bool requiresReset,
        ImmutableArray<LogEntry> addedEntries, ImmutableArray<long> removedEntryIds)
    {
        FromVersion = fromVersion;
        Snapshot = snapshot;
        RequiresReset = requiresReset;
        AddedEntries = addedEntries;
        RemovedEntryIds = removedEntryIds;
    }

    /// <summary>Gets the requested prior revision.</summary>
    public long FromVersion { get; }
    /// <summary>Gets the complete latest frozen view, also used for reset recovery.</summary>
    public LogSnapshot Snapshot { get; }
    /// <summary>Gets the ending revision.</summary>
    public long Version => Snapshot.Version;
    /// <summary>Gets the ending generation.</summary>
    public long Generation => Snapshot.Generation;
    /// <summary>Gets whether bounded history was lost or Clear intervened.</summary>
    public bool RequiresReset { get; }
    /// <summary>Gets new retained entries. On reset this contains all retained entries.</summary>
    public ImmutableArray<LogEntry> AddedEntries { get; }
    /// <summary>Gets evicted IDs since FromVersion. Empty on reset.</summary>
    public ImmutableArray<long> RemovedEntryIds { get; }
    /// <summary>Releases the frozen view.</summary>
    public void Dispose() => Snapshot.Dispose();
}

