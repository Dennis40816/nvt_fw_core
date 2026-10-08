// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Nvt.Core.LogConsole;

/// <summary>A bounded single-writer store with immutable published versions.</summary>
public sealed class LogStore : IDisposable
{
    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly long _maxCharacters;
    private readonly long _maxPendingCharacters;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _schedule;
    private readonly Func<ILogTextContent, ulong> _fingerprint;
    // _gate protects admission, ownership collections, reader fences, generation, subscriptions,
    // readiness and the published reference. Ownership moves from queues to _inFlight until
    // successful publication or completed disposal, including deferred unpublished cleanup.
    private LinkedList<Work> _pending = new();
    private Queue<Work> _discarded = new();
    private readonly HashSet<ContentOwner> _inFlight = [];
    private long _rejectedCount;
    private readonly LinkedList<SnapshotWaiter> _snapshotWaiters = new();
    private Queue<(ContentOwner Owner, Action Dispose)> _cleanup = new();
    private readonly ConsoleGeneration _generation = new();
    private long _nextSequence;
    private bool _disposed;
    private bool _ready;
    private EventHandler<LogChangeSet>? _changed;
    private PublishedState _published = new(0, 0, 0, 0, [], []);
    // Interlocked owns scheduling. It remains set through all writer callbacks and cleanup.
    private int _writerScheduled;
    // Volatile identifies synchronous writer callbacks, which must not wait for this writer.
    private int _writerThreadId;
    // Only the writer accesses the ring, groups, history and publication cursor below.
    private readonly Queue<StoredEntry> _entries = new();
    private readonly Dictionary<GroupKey, List<Group>> _groups = [];
    private readonly Queue<ChangeMarker> _history = new();
    private long _writerGeneration;
    private long _version;
    private long _lastSequence;
    private long _evictedCount;
    private long _notifiedVersion;

    /// <summary>Creates a store. Writing starts immediately; SetReady gates only Changed.</summary>
    /// <param name="maxEntries">The independent retained event limit.</param>
    /// <param name="maxCharacters">The independent retained resident UTF-16 limit.</param>
    /// <param name="maxPendingCharacters">The unpublished owned UTF-16 limit, including discarded
    /// content until writer disposal completes. Full admission rejects new writes. Unpublished handles
    /// also have a limit of maxEntries. Published content uses the ring limits; final release of
    /// published snapshot content is excluded. Lease holders must dispose their snapshots and projections.</param>
    /// <param name="clock">The timestamp clock. Defaults to TimeProvider.System.</param>
    /// <param name="schedule">A nonblocking writer scheduler, invoked off the producer thread.</param>
    public LogStore(int maxEntries = 10_000, long maxCharacters = 4 * 1024 * 1024,
        long maxPendingCharacters = 256 * 1024, TimeProvider? clock = null, Action<Action>? schedule = null)
        : this(maxEntries, maxCharacters, maxPendingCharacters, clock, schedule, LogText.Fingerprint) { }

    internal LogStore(int maxEntries, long maxCharacters, long maxPendingCharacters, TimeProvider? clock,
        Action<Action>? schedule, Func<ILogTextContent, ulong> fingerprint)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPendingCharacters);
        _maxEntries = maxEntries;
        _maxCharacters = maxCharacters;
        _maxPendingCharacters = maxPendingCharacters;
        _clock = clock ?? TimeProvider.System;
        _schedule = schedule ?? (static action => action());
        _fingerprint = fingerprint;
    }

    /// <summary>Delivers a scoped delta after publication, outside every lock.</summary>
    public event EventHandler<LogChangeSet> Changed
    {
        add { lock (_gate) { ThrowIfDisposed(); _changed += value; } }
        remove { lock (_gate) _changed -= value; }
    }

    /// <summary>Gets the immediate generation fence for queued and asynchronous work.</summary>
    public long Generation { get { lock (_gate) return _generation.Value; } }

    /// <summary>Gets the lifetime number of rejected Add or AddBatch calls, including stale batches.
    /// Each rejected batch counts once. Clear does not reset this count. Ring evictions are separate.</summary>
    public long RejectedCount { get { lock (_gate) return _rejectedCount; } }

    /// <summary>Enqueues in-memory text and returns its stable ID, or 0 if capacity rejects it.</summary>
    /// <remarks>Capacity rejection consumes no ID, raises no Changed event, and throws no exception.</remarks>
    public long Add(LogLevel level, string sourceId, string message, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Add(new LogWrite(level, sourceId, new InMemoryLogTextContent(message), timestamp));
    }

    /// <summary>Prepares and enqueues content, transferring ownership only on acceptance.</summary>
    /// <returns>The stable positive ID, or the invalid ID 0 on capacity rejection.</returns>
    /// <remarks>Rejection consumes no ID, raises no Changed event, and throws no exception.
    /// Rejected content remains caller-owned; the store never disposes it.</remarks>
    public long Add(LogWrite write)
    {
        lock (_gate) ThrowIfDisposed();
        var prepared = Prepare(write);
        Enqueue(null, [prepared], out var id);
        return id;
    }

    /// <summary>Prepares a bounded batch outside locks, then admits the whole batch in one lock hold.</summary>
    /// <returns>True on acceptance; false on capacity or generation rejection.</returns>
    /// <remarks>A stale batch is not enumerated. Preparation stops at the first input exceeding a
    /// pending limit. Rejection transfers no ownership, consumes no IDs, raises no Changed event,
    /// and throws no exception. The caller retains all content, including any unenumerated suffix.
    /// Enumeration or preparation exceptions also transfer nothing.</remarks>
    public bool AddBatch(long generation, IEnumerable<LogWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_generation.IsCurrent(generation)) { _rejectedCount++; return false; }
        }
        var prepared = new List<PreparedWrite>();
        long characters = 0;
        foreach (var write in writes)
        {
            if (prepared.Count == _maxEntries) return RejectBatch();
            var item = Prepare(write);
            if (item.Owner.ResidentCharacters > _maxPendingCharacters - characters) return RejectBatch();
            characters += item.Owner.ResidentCharacters;
            prepared.Add(item);
        }
        return Enqueue(generation, prepared, out _);

        bool RejectBatch()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _rejectedCount++;
                return false;
            }
        }
    }

    private bool Enqueue(long? generation, IReadOnlyList<PreparedWrite> prepared, out long id)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            id = 0;
            var usage = PendingUsage;
            if (generation.HasValue && !_generation.IsCurrent(generation.Value)
                || prepared.Count > _maxEntries - usage.Handles
                || prepared.Sum(item => (long)item.Owner.ResidentCharacters) > _maxPendingCharacters - usage.Characters)
            {
                _rejectedCount++;
                return false;
            }
            foreach (var item in prepared)
            {
                id = checked(++_nextSequence);
                _pending.AddLast(new Work(_generation.Value, id, AdmissionFence, item));
            }
        }
        if (prepared.Count != 0) WakeWriter();
        return true;
    }

    // Read only under _gate. Each accepted write advances sequence; each Clear advances generation.
    private long AdmissionFence => checked(_nextSequence + _generation.Value);

    // Derived diagnostic also used by admission. Never call app metadata under _gate.
    internal (int Handles, long Characters) PendingUsage
    {
        get
        {
            lock (_gate)
            {
                var owners = _pending.Concat(_discarded).Where(item => item.Write is not null)
                    .Select(item => item.Write!.Owner).Concat(_inFlight);
                return (owners.Count(), owners.Sum(owner => (long)owner.ResidentCharacters));
            }
        }
    }

    /// <summary>Advances generation immediately and queues an atomic writer reset.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _generation.Advance();
            foreach (var item in _pending) if (item.Write is not null) _discarded.Enqueue(item);
            _pending.Clear();
            _pending.AddLast(new Work(_generation.Value, _nextSequence, AdmissionFence, null));
        }
        WakeWriter();
    }

    /// <summary>Captures the last published version without waiting for the writer.</summary>
    /// <remarks>Accepted writes and Clear may not yet be published. Use CaptureLatestAsync
    /// before export or copy when all previously admitted operations must be covered.</remarks>
    public LogSnapshot CaptureSnapshot()
    {
        var capturedAt = _clock.GetUtcNow().ToUniversalTime();
        var state = AcquirePublished();
        try { return state.Capture(capturedAt); }
        finally { state.Release(); }
    }

    /// <summary>Asynchronously captures a publication covering every Add and Clear admitted before this call.</summary>
    /// <remarks>Never blocks a thread. Cancellation or store disposal cancels the wait. A writer failure
    /// faults outstanding waits with its original exception. On the writer thread, including Changed
    /// and content Dispose callbacks, returns the current publication without waiting for itself.
    /// The caller must dispose the returned snapshot.</remarks>
    public async ValueTask<LogSnapshot> CaptureLatestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PublishedState state;
        SnapshotWaiter? waiter = null;
        lock (_gate)
        {
            if (_disposed) throw new OperationCanceledException("The log store was disposed.", new CancellationToken(true));
            if (_published.Admission >= AdmissionFence || Volatile.Read(ref _writerThreadId) == Environment.CurrentManagedThreadId)
            {
                state = _published;
                state.Retain();
            }
            else
            {
                waiter = new SnapshotWaiter(AdmissionFence, new(TaskCreationOptions.RunContinuationsAsynchronously));
                _snapshotWaiters.AddLast(waiter);
                state = null!;
            }
        }
        if (waiter is not null)
        {
            await using var registration = cancellationToken.Register(() =>
            {
                lock (_gate)
                    if (_snapshotWaiters.Remove(waiter)) waiter.Completion.TrySetCanceled(cancellationToken);
            });
            state = await waiter.Completion.Task.ConfigureAwait(false);
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return state.Capture(_clock.GetUtcNow().ToUniversalTime());
        }
        finally { state.Release(); }
    }

    /// <summary>Gets retained additions and removals from the last published version.</summary>
    public LogChangeSet GetChangesSince(long version)
    {
        var capturedAt = _clock.GetUtcNow().ToUniversalTime();
        var state = AcquirePublished();
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegative(version);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(version, state.Version);
            return state.Changes(version, capturedAt);
        }
        finally { state.Release(); }
    }

    private PublishedState AcquirePublished()
    {
        lock (_gate) { ThrowIfDisposed(); _published.Retain(); return _published; }
    }

    /// <summary>Checks the generation fence and last published state.</summary>
    public bool IsCurrent(long generation)
    {
        lock (_gate) return !_disposed && _generation.IsCurrent(generation) && _published.Generation == generation;
    }

    /// <summary>Also checks retained identity and text revision in the published state.</summary>
    public bool IsCurrent(long generation, long entryId, long textVersion)
    {
        lock (_gate) return !_disposed && _generation.IsCurrent(generation) && _published.Generation == generation
            && _published.TextVersions.TryGetValue(entryId, out var version) && version == textVersion;
    }

    /// <summary>Gates Changed delivery. Queued writes are processed regardless of readiness.</summary>
    public void SetReady(bool ready)
    {
        bool wake;
        lock (_gate)
        {
            ThrowIfDisposed();
            _ready = ready;
            wake = ready && (_pending.Count != 0 || _published.Version != _notifiedVersion);
        }
        if (wake) WakeWriter();
    }

    /// <summary>Closes admission and queues writer cleanup. Earlier snapshots remain valid.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation.Advance();
            _changed = null;
            foreach (var waiter in _snapshotWaiters) waiter.Completion.TrySetCanceled();
            _snapshotWaiters.Clear();
        }
        WakeWriter();
    }

    private void WakeWriter()
    {
        if (Interlocked.CompareExchange(ref _writerScheduled, 1, 0) != 0) return;
        // Even an inline scheduler must never put writer callbacks on an Add or Dispose caller.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { _schedule(WriteLoop); }
            catch { WriteLoop(); } // A failed scheduler must not enqueue; accepted content still needs cleanup.
        });
    }

    private void QueueCleanup(ContentOwner owner, Action cleanup)
    {
        lock (_gate) _cleanup.Enqueue((owner, cleanup));
        WakeWriter();
    }

    private void WriteLoop()
    {
        LinkedList<Work> work = new();
        Queue<Work> discarded = new();
        Queue<(ContentOwner Owner, Action Dispose)> cleanup = new();
        var released = new List<ILogTextContent>();
        Volatile.Write(ref _writerThreadId, Environment.CurrentManagedThreadId);
        try
        {
            while (true)
            {
                long generation, admission;
                bool disposed;
                lock (_gate)
                {
                    work = _pending; _pending = new();
                    discarded = _discarded; _discarded = new();
                    foreach (var item in work.Concat(discarded)) if (item.Write is { } write) _inFlight.Add(write.Owner);
                    cleanup = _cleanup; _cleanup = new();
                    generation = _generation.Value;
                    admission = AdmissionFence;
                    disposed = _disposed;
                }
                // Read app time after taking ownership, before changing the writer's state.
                var capturedAt = disposed ? default : _clock.GetUtcNow().ToUniversalTime();
                if (disposed)
                {
                    foreach (var item in work.Concat(discarded)) if (item.Write is { } write) released.Add(write.Owner.TakeInitialLease());
                    work.Clear();
                    discarded.Clear();
                    DetachEntries(released);
                    _history.Clear();
                    PublishedState old;
                    lock (_gate) { old = _published; _published = new(_version, generation, _lastSequence, 0, [], []); }
                    old.Release();
                }
                else Apply(work, discarded, generation, admission, released);
                // Publication always precedes app cleanup and notification.
                foreach (var content in released) RunCallback(content.Dispose);
                released.Clear();
                while (cleanup.TryDequeue(out var callback))
                {
                    RunCallback(callback.Dispose);
                    lock (_gate) _inFlight.Remove(callback.Owner);
                }
                if (!disposed) Notify(capturedAt);
                lock (_gate)
                {
                    if (_pending.Count != 0 || _discarded.Count != 0 || _cleanup.Count != 0 || _disposed && !disposed
                        || _ready && !_disposed && _published.Version != _notifiedVersion) continue;
                    Volatile.Write(ref _writerThreadId, 0);
                    Interlocked.Exchange(ref _writerScheduled, 0);
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            foreach (var content in released) RunCallback(content.Dispose);
            ReportError(exception);
            lock (_gate)
            {
                // Return unconsumed ownership ahead of newer admissions. In-flight owners already
                // transferred to ring/cleanup remain charged until publication or final disposal.
                foreach (var item in work.Concat(discarded)) if (item.Write is { } write) _inFlight.Remove(write.Owner);
                while (work.Last is { } last) { _pending.AddFirst(last.Value); work.RemoveLast(); }
                _discarded = new Queue<Work>(discarded.Concat(_discarded));
                _cleanup = new Queue<(ContentOwner Owner, Action Dispose)>(cleanup.Concat(_cleanup));
                Interlocked.Exchange(ref _writerScheduled, 0);
                Volatile.Write(ref _writerThreadId, 0);
                if (_disposed || _pending.Count != 0 || _discarded.Count != 0 || _cleanup.Count != 0
                    || _ready && !_disposed && _generation.IsCurrent(_published.Generation)
                    && _published.Version != _notifiedVersion) WakeWriter();
            }
        }
    }

    private void Apply(LinkedList<Work> work, Queue<Work> discarded, long generation, long admission, List<ILogTextContent> released)
    {
        while (discarded.TryDequeue(out var item))
        {
            _lastSequence = Math.Max(_lastSequence, item.Id);
            released.Add(item.Write!.Owner.TakeInitialLease());
        }
        for (var node = work.First; node is not null;)
        {
            var next = node.Next;
            if (node.Value.Generation != generation)
            {
                if (node.Value.Write is { } stale)
                {
                    _lastSequence = Math.Max(_lastSequence, node.Value.Id);
                    released.Add(stale.Owner.TakeInitialLease());
                }
                work.Remove(node);
            }
            node = next;
        }
        if (work.Count == 0) return;
        if (work.First!.Value.Write is null)
        {
            var reset = work.First.Value;
            work.RemoveFirst();
            _lastSequence = Math.Max(_lastSequence, reset.Id);
            DetachEntries(released);
            _history.Clear();
            _writerGeneration = generation;
            _evictedCount = 0;
            Publish([], true, reset.Admission);
        }
        var hasWrites = work.Count != 0;
        if (hasWrites) _lastSequence = Math.Max(_lastSequence, work.Last!.Value.Id);
        var removed = new List<long>();
        var retained = _entries.Sum(item => (long)item.Owner.ResidentCharacters);
        while (work.First is { } node)
        {
            var item = node.Value;
            var prepared = item.Write!;
            Group? group = null;
            try
            {
                if (_groups.TryGetValue(prepared.Key, out var bucket))
                    group = bucket.Find(candidate => LogText.Equal(candidate.Members.Peek().Entry.TextContent, prepared.Write.TextContent));
            }
            catch (Exception exception)
            {
                // A faulty comparison cannot drop an accepted write or count as ring eviction.
                // Keep it in a separate group when equality cannot be established.
                ReportError(exception);
            }
            if (group is null)
            {
                group = new Group(item.Id, prepared.Key);
                if (!_groups.TryGetValue(prepared.Key, out var bucket)) _groups.Add(prepared.Key, bucket = []);
                bucket.Add(group);
            }
            var input = prepared.Write;
            var entry = new LogEntry(item.Id, generation, item.Id, prepared.Timestamp, input.Level, input.SourceId,
                prepared.Owner.TakeInitialLease(), input.LinkSpans, group.Id);
            Debug.Assert(entry.EntryId == entry.Sequence, "EntryId equals Sequence for store-created entries.");
            var stored = new StoredEntry(entry, _version + 1, group, prepared.Owner);
            _entries.Enqueue(stored);
            group.Members.Enqueue(stored);
            work.RemoveFirst();
            retained += prepared.Owner.ResidentCharacters;
            while (_entries.Count > _maxEntries || retained > _maxCharacters)
            {
                var evicted = _entries.Dequeue();
                retained -= evicted.Owner.ResidentCharacters;
                evicted.Group.Members.Dequeue();
                if (evicted.Group.Members.Count == 0)
                {
                    var bucket = _groups[evicted.Group.Key];
                    bucket.Remove(evicted.Group);
                    if (bucket.Count == 0) _groups.Remove(evicted.Group.Key);
                }
                released.Add(evicted.Entry.TextContent);
                if (removed.Count <= _maxEntries) removed.Add(evicted.Entry.EntryId);
                _evictedCount++;
            }
        }
        if (hasWrites) Publish(removed, false, admission);
    }

    private void Publish(List<long> removed, bool reset, long admission)
    {
        var version = _version + 1;
        var marker = new ChangeMarker(version, reset || removed.Count > _maxEntries,
            removed.Count > _maxEntries ? [] : removed.ToImmutableArray());
        var history = _history.Append(marker).ToList();
        while (history.Count > _maxEntries || history.Sum(m => (long)m.Removed.Length) > _maxEntries) history.RemoveAt(0);
        var state = new PublishedState(version, _writerGeneration, _lastSequence, _evictedCount,
            _entries.Select(e => new PublishedEntry(e.Entry with { TextContent = e.Owner.Lease() }, e.AddedVersion)).ToImmutableArray(),
            history.ToImmutableArray(), admission);
        PublishedState? old = null;
        lock (_gate)
        {
            // Clear/Dispose may occur inside a content comparison. Discard this step; never retry it.
            if (!_disposed && _generation.IsCurrent(_writerGeneration))
            {
                old = _published; _published = state;
                foreach (var entry in _entries) _inFlight.Remove(entry.Owner);
                while (_snapshotWaiters.First is { Value: var waiter } && waiter.Admission <= admission)
                {
                    _snapshotWaiters.RemoveFirst();
                    state.Retain();
                    waiter.Completion.SetResult(state);
                }
            }
        }
        if (old is null) { state.Release(); return; }
        _version = version;
        _history.Clear();
        foreach (var change in history) _history.Enqueue(change);
        old.Release();
    }

    private void Notify(DateTimeOffset capturedAt)
    {
        PublishedState state;
        EventHandler<LogChangeSet>? changed;
        lock (_gate)
        {
            if (_disposed || !_ready || !_generation.IsCurrent(_published.Generation) || _published.Version == _notifiedVersion) return;
            state = _published;
            state.Retain();
            changed = _changed;
        }
        try
        {
            using var changes = state.Changes(_notifiedVersion, capturedAt);
            lock (_gate) _notifiedVersion = state.Version;
            if (changed is not null)
                foreach (EventHandler<LogChangeSet> callback in changed.GetInvocationList())
                    RunCallback(() => { if (IsCurrent(changes.Generation)) callback(this, changes); });
        }
        finally { state.Release(); }
    }

    private void DetachEntries(List<ILogTextContent> released)
    {
        foreach (var entry in _entries) released.Add(entry.Entry.TextContent);
        _entries.Clear();
        _groups.Clear();
    }

    private void RunCallback(Action callback)
    {
        try { callback(); }
        catch (Exception exception) { ReportError(exception); }
    }

    private void ReportError(Exception exception)
    {
        lock (_gate)
        {
            foreach (var waiter in _snapshotWaiters) waiter.Completion.TrySetException(exception);
            _snapshotWaiters.Clear();
        }
        try { Trace.TraceError("LogStore writer failed: {0}", exception); }
        catch { /* A diagnostic listener cannot prevent recovery or cleanup. */ }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private PreparedWrite Prepare(LogWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.SourceId);
        ArgumentNullException.ThrowIfNull(write.TextContent);
        if (!Enum.IsDefined(write.Level)) throw new ArgumentOutOfRangeException(nameof(write), "Unknown log level.");
        var length = write.TextContent.Length;
        var resident = write.TextContent.ResidentCharacterCount;
        var textVersion = write.TextContent.Version;
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(resident);
        if (write.LinkSpans is { } spans) _ = new ConsoleLinkIndex(spans, length);
        var timestamp = (write.Timestamp ?? _clock.GetUtcNow()).ToUniversalTime();
        var key = new GroupKey(write.SourceId, write.Level, _fingerprint(write.TextContent));
        return new PreparedWrite(write, timestamp, key, new ContentOwner(write.TextContent, length, resident, textVersion, QueueCleanup));
    }

    private sealed record Work(long Generation, long Id, long Admission, PreparedWrite? Write);
    private sealed record SnapshotWaiter(long Admission, TaskCompletionSource<PublishedState> Completion);
    private readonly record struct GroupKey(string SourceId, LogLevel Level, ulong Hash);
    private sealed record PreparedWrite(LogWrite Write, DateTimeOffset Timestamp, GroupKey Key, ContentOwner Owner);
    private sealed record ChangeMarker(long Version, bool Reset, ImmutableArray<long> Removed);
    private sealed record PublishedEntry(LogEntry Entry, long AddedVersion);
    private sealed record StoredEntry(LogEntry Entry, long AddedVersion, Group Group, ContentOwner Owner);
    private sealed class Group(long id, GroupKey key)
    {
        internal long Id { get; } = id;
        internal GroupKey Key { get; } = key;
        internal Queue<StoredEntry> Members { get; } = new();
    }

    private sealed class PublishedState(long version, long generation, long lastSequence, long evictedCount,
        ImmutableArray<PublishedEntry> entries, ImmutableArray<ChangeMarker> history, long admission = 0)
    {
        // Interlocked pins this immutable publication. Acquiring it under _gate takes constant time.
        private int _references = 1;
        internal long Version => version;
        internal long Generation => generation;
        internal long Admission => admission;
        internal ImmutableDictionary<long, long> TextVersions { get; } = entries.ToImmutableDictionary(e => e.Entry.EntryId, e => e.Entry.TextContent.Version);
        internal void Retain() => Interlocked.Increment(ref _references);
        internal void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0)
                foreach (var entry in entries) entry.Entry.TextContent.Dispose();
        }
        internal LogSnapshot Capture(DateTimeOffset capturedAt) => new(version, generation, lastSequence, evictedCount, capturedAt,
            entries.Select(e => e.Entry with { TextContent = ContentOwner.Retain(e.Entry.TextContent) }).ToImmutableArray());
        internal LogChangeSet Changes(long fromVersion, DateTimeOffset capturedAt)
        {
            var snapshot = Capture(capturedAt);
            var reset = !history.IsEmpty && (fromVersion < history[0].Version - 1 || history.Any(m => m.Version > fromVersion && m.Reset));
            var ids = entries.Where(e => e.AddedVersion > fromVersion).Select(e => e.Entry.EntryId).ToHashSet();
            var added = reset ? snapshot.Entries : snapshot.Entries.Where(e => ids.Contains(e.EntryId)).ToImmutableArray();
            var removed = reset ? [] : history.Where(m => m.Version > fromVersion).SelectMany(m => m.Removed).Distinct().ToImmutableArray();
            return new LogChangeSet(fromVersion, snapshot, reset, added, removed);
        }
    }
}
