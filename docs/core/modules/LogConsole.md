[English](LogConsole.md) | [繁體中文](LogConsole.zh-TW.md)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

# LogConsole

`Nvt.Core.LogConsole` is the shared non-UI console model for NFC, NFH, and NFU.
It targets `net8.0`.
It adds no package.
The Avalonia control and its commands come in K2.

## Design and provenance

This is a new implementation of the design approved on 2026-10-07 in Core PR #82.
It does not port a product console.
The behavior reference is the NFH console baseline.
The baseline took 9714.908 ms for a synchronous 10,000-event burst.
That number includes product projection and UI work.
The Core projection measurement below measures only projection.
It is not a UI comparison.
No UI or product logic is included here.

## State structure

Inputs live in one `ConsoleFilter` record.
It contains levels, sources, literal search, only-matches, dedupe, and time mode.
An empty source set means all sources.
An empty level set means no levels.
An empty search means no search condition.
Source IDs use ordinal identity.
Search uses ordinal case-insensitive matching.

`LogStore` is the sole event owner.
It publishes a leased immutable `LogSnapshot`.
The snapshot contains the version, generation, retained range, event count, eviction total, and entries.
Sequence determines order.
Time never determines order.
Entry IDs and group IDs are never reused after Clear.

View state lives in one `ConsoleViewState` record.
`ConsoleFollow` is closed to `Following` and `Paused(anchor, pausedAt)`.
The base class has only a private constructor.
Both nested immutable classes are sealed.
Selection uses an immutable set of raw EntryIds.
A visible group is selected when any retained member is selected.
Expansion uses stable row IDs.
Collapse does not resume following.
`Pause` captures the current presentation order.
`Resume` explicitly restores latest-sequence order.
`Remap` preserves the selected raw events through dedupe changes.
It maps expansion, the paused anchor, and frozen reading order through raw event membership.
Dedupe changes preserve the pause time, watermark, and text and pixel offsets.
It keeps hidden retained selections.
It removes evicted selections.
Copy still excludes hidden rows.

`ConsoleProjector.Project(snapshot, filter, viewState)` is pure.
It reads no clock and accesses no file system itself.
Its only time source is the snapshot capture instant or the pause instant.
It returns content handles, search ranges, counts, and row membership.
It never copies an entire message into a row string.
Counts and empty-state values belong only to this output.
The host must reject background projections when snapshot or filter inputs have changed.
K2 routes menus and shortcuts through the same commands.
K1 contains mechanisms and immutable values, not another command or controller implementation.

## Storage, budgets, and lifetime

The default limits are 10,000 entries, 4 Mi retained UTF-16 characters, and 256 Ki pending UTF-16 characters.
Each limit acts independently.
Character limits charge `ILogTextContent.ResidentCharacterCount`.
The in-memory implementation charges its full length.
Apps can inject immutable segmented content for larger messages.
They own spill location and cleanup policy.
Core provides no disk content store.

A successful enqueue transfers exclusive content ownership to the store.
A stale batch is not enumerated.
Inputs rejected before enqueue remain caller-owned.
`Add` returns a positive stable ID on acceptance and the invalid ID `0` on capacity rejection.
`AddBatch` returns `false` on capacity or generation rejection.
It prepares outside locks and admits the whole batch in one lock hold, or transfers nothing.
Preparation stops at the first input exceeding the pending character or handle limit.
It inspects at most `maxEntries + 1` inputs, including the first excess handle.
Prepared inputs and any unenumerated suffix stay caller-owned on rejection.
Enumeration or preparation exceptions also transfer nothing.
Rejection consumes no ID, raises no `Changed` event, and throws no exception.
The store never disposes rejected content.
Invalid arguments, faulty preparation and use after disposal retain their exception behavior.
The read-only `RejectedCount` counts rejected calls over the store lifetime.
Each rejected batch counts once, including a stale batch; Clear does not reset it.
Do not reuse one content instance in multiple inputs.
Content length, resident charge, and version must remain fixed.
Content reads must fill the requested range.
Implementations must support concurrent reads.

`Add` and `AddBatch` validate metadata, take timestamps, and fingerprint outside locks.
A short admission lock checks total capacity before assigning IDs and enqueuing in sequence order.
Producers never wait for capacity, the writer, or a callback.
Publication is asynchronous.
`CaptureSnapshot` waits outside locks for every previously admitted Add and Clear to be published.
Its admission fence is derived under `_gate` from the next sequence plus the generation.
Rejected writes and empty batches do not advance that fence.
Exports acquire this latest snapshot before projecting their frozen data.
Capture on the writer thread returns the current publication, including inside Changed and content Dispose callbacks.
It cannot wait for operations queued by its own callback.
The writer runs independently of notification readiness.
`GetChangesSince(version)` and `IsCurrent` read the published state without waiting.
Disposing the store releases waiting captures with `ObjectDisposedException`.

Only the writer changes the ring, groups, version, bounded history, and eviction total.
It takes queued work, compares full text, applies additions and eviction, then publishes one immutable state.
Readers pin that state under a constant-time lock and build their leased view outside it.
Readers see either the preceding step or the whole committed step.
No reservation, capacity wait, early eviction, or optimistic retry exists.
The ring uses `Queue<T>`'s circular array.
It never shifts the retained head or rebuilds formatted console text.
Delta history bounds both marker count and removal IDs by the entry limit.
History loss causes an explicit reset from the latest snapshot.

Pending capacity charges all unpublished owned content, independently of retained capacity.
It includes queued writes, discarded writes, active writer work, and deferred unpublished cleanup.
Content left by Clear stays charged until its actual writer disposal returns.
Admission rejects new writes when either the character or handle limit would be exceeded.
Accepted queued writes are never dropped for overflow.
Unpublished content has a handle limit equal to `maxEntries`, including zero-charge content.
At most one reset marker is queued in addition to those writes.
It does not remove unrelated retained events.
`EvictedCount` counts ring eviction only; rejected calls increment `RejectedCount`.
Producers can enqueue while a writer callback is busy if capacity remains.
Pending usage is derived under the admission lock from the ownership collections.
No independent pending character total is stored.
Caller-owned preparation is outside the store ownership bound.
Retained capacity removes the oldest ring entry in the same publication as its causative addition.
A write larger than pending capacity is rejected; an admitted write larger than retained capacity evicts itself.

`Clear` advances the generation fence immediately and enqueues a reset.
It replaces pending resets and moves obsolete queued writes to writer cleanup.
The writer discards queued writes from older generations before applying the reset.
Reset clears retained content, group membership, history, and the eviction total.
The reset and later additions have distinct versions.
`AddBatch` checks its generation before enumeration, at enqueue, and in the writer.
A generation change during writer comparison discards that step without retrying it.
`IsCurrent` checks both the immediate fence and the published state.
`Dispose` closes admission immediately and wakes the writer to release queued and retained leases.
Already captured snapshots and projections remain readable until their own disposal.
Published content uses the ring capacity, rather than pending capacity.
Evicted published content held by snapshot or projection leases remains alive until those leases end.
The lease holder is responsible for disposing snapshots and projections.
Final release and writer cleanup of previously published content are excluded from the pending bound.

App content disposal runs on the writer after publication and outside every lock.
Reference counting delays disposal until the last lease and active read finish.
A final lease release queues writer cleanup, including after store disposal.
The data writer stops after store cleanup; later leases schedule only their remaining cleanup.
A snapshot or projection release never invokes app disposal on its caller.
Dedupe comparisons also run on the writer outside locks.
Projection, scanner, export, metadata, clock, and fingerprint reads retain their calling-thread contracts.

One interlocked flag covers queued and active writer work, including all app callbacks.
The existing `schedule` delegate starts the writer when work arrives.
A thread-pool dispatch invokes it off the producer thread, so inline schedulers cannot block producers.
A throwing scheduler must not enqueue its callback.
A scheduler failure falls back to writing on that dispatch thread.
If the notification clock throws, the writer rearms outstanding Changed delivery.
App subscriber, comparison, and disposal exceptions are isolated so later work and cleanup continue.
Failed comparisons keep the accepted write in a separate group and do not count as ring eviction.

A new store is not ready for notifications.
Subscribe, capture startup state, and track its version.
`SetReady(true)` gates `Changed`; it does not gate writing or pending-capacity release.
Delivery stays serialized until every subscriber and scoped lease release completes.
A reentrant add leaves work for the writer's next step.
`Changed` data is scoped to the callback.
Acquire a separate snapshot to retain it.
Consumers check generation before applying a callback.

## Dedupe and projection

The key is source, level, and full unchanged message.
Time and text version are not keys.
Hash collisions compare full content in chunks.
Different original line breaks stay distinct.
Groups keep stable IDs while any member survives.
Rows contain retained sequences, first and last occurrence time, count, and last sequence.
Eviction updates this projection through retained membership.
Following sorts groups by last sequence.
Pause keeps existing row positions fixed.
New rows append after those positions.
They sort by their stable EntryId or GroupId.
Duplicates and eviction of a group's first member keep this insertion order.
New matching raw events include duplicate increments.
The new-event count covers retained events after the pause watermark.
Evicted events are reported separately.
An evicted reading anchor resolves to the first surviving successor in the frozen paused order.
Relative time freezes at `PausedAt`.

Level counts apply only the source filter.
Source counts apply only the level filter.
Both count retained raw events.
Search and dedupe do not change those counts.
Search covers the full message and source ID.
Message reads use chunks of at most 1,024 UTF-16 characters.
Search retains overlap proportional to the literal length for boundary-spanning matches.
Hit ranges use UTF-16 offsets with exclusive ends.
Only-matches off preserves rows and their highlights.
The event count is the snapshot's unfiltered retained count.
The matching event count sums the visible row memberships.
The row count counts projected rows.
Multiline text does not create extra events.

## Links and app interfaces

`ConsoleLinkScanner` is pure.
It checks URLs before quoted and unquoted paths.
It supports drive paths, UNC paths, relative paths, Unicode, and quoted spaces.
Both entry points use one scalar name rule and one path-start rule.
Unquoted component names accept all Unicode letters and marks, decimal digits, and `_`, `-`, and `.`.
Separators and location suffixes are separate grammar tokens.
Dot-prefixed components such as `.config` and `.github` retain their leading dot.
Decomposed names retain their combining marks and their whole first component.
The scanner preserves the supplied Unicode form and never normalizes a target.
Recognized drive and UNC prefixes may follow CJK prose directly.
Closing quotes must match opening quotes.
Apostrophes inside double-quoted paths retain the full path and location.
It preserves `(line,column)` and `:line:column` suffixes.
It keeps balanced URL parentheses.
It stops at outer CJK punctuation.
This includes the fullwidth colon.
Filename-only relative paths with location suffixes are accepted.
This includes quoted filenames with spaces.
Quoted names without a separator or location suffix are not links.
A trailing separator explicitly denotes a folder.
An extensionless path can still be a file.
Other ambiguous folder targets require structured spans or app resolution.
There is no extension allowlist.
There are no existence checks or repository scans.

App-supplied spans replace scanning completely.
An explicitly empty span array also wins.
`ConsoleLinkIndex` validates ranges and provides binary hit testing.
`ConsoleLinkCache` keys results by entry ID, text revision, and resolver policy revision.
Search is not a cache key.
`Synchronize` is its only semantic invalidation point.
Call it for every accepted snapshot, including Clear.
Older snapshots cannot refill the live cache.
Scanning runs outside the cache lock.
The cache scans segmented content with a fixed 1,024-character read buffer.
Candidate offsets survive read boundaries.
Only confirmed link targets become strings.
Plain message text is never materialized in full.
Publication checks the current snapshot and policy revision again.
Entry count, span count, and target characters each bound cache retention.
Oversized results are returned without caching.

`IConsolePathResolver` is asynchronous.
It returns candidates without selecting an ambiguous target.
The app honors cancellation, `MaxCandidates`, and `MaxProbes`.
Its policy version invalidates cached links.
Core provides no resolver implementation.

`IConsoleLinkOpener` receives the complete `LinkTarget`.
It reports open, parent-folder, line, and column capabilities.
Its return value reuses `SourceFileNavigation.SourceFileOpenResult`.
Core provides no opener implementation in this module.
Apps supply source names, adapters, path policy, opening, clipboard, and save destination.
Apps also supply storage for large content.

## Export

All three actions use `ConsoleExportFormatter` over one frozen projection version.
`FormatSelection` accepts canonical raw EntryIds and derives visible group selection.
`FormatVisible` includes all filtered rows beyond the viewport.
`WriteLogAsync` writes the same text to an app-owned stream.
The stream stays open.
Stream export reads and writes bounded chunks.
It never creates a complete export string or byte array.
The string-returning copy methods allocate only their requested final output.
UTF-8 has no BOM.
Time and level options default to enabled.
Export time is UTC clock time, independent of the screen's time mode.
Source and full message are always included.
Original message line breaks stay unchanged.
Rows are separated by LF.
Repeated rows retain `×N`.
No display truncation or stale formatted-text cache is used.
After publication of a later Add, the host captures the latest snapshot for export, including while collapsed.

## Public API

| Type | Public members |
| --- | --- |
| `LogLevel` | `Trace`, `Debug`, `Info`, `Warn`, `Error`, `Fatal`. |
| `ConsoleTimeMode` | `Absolute`, `Relative`, `Hidden`. |
| `ConsoleFilter` | `EnabledLevels`, `SelectedSources`, `SearchText`, `OnlyMatches`, `Deduplicate`, `TimeMode`. |
| `ConsoleRowId` | `Value`, `IsGroup`. |
| `ILogTextContent` | `Length`, `ResidentCharacterCount`, `Version`, `Read(offset, destination)`, `Dispose()`. |
| `InMemoryLogTextContent` | Constructor `(text, version = 0)` and the content interface members. |
| `LogWrite` | `Level`, `SourceId`, `TextContent`, optional `Timestamp`, optional `LinkSpans`. |
| `LogEntry` | `EntryId`, `Generation`, `Sequence`, `Timestamp`, `Level`, `SourceId`, `TextContent`, `LinkSpans`, `GroupId`. |
| `LogSnapshot` | `Version`, `Generation`, `LastSequence`, `FirstRetainedSequence`, `LastRetainedSequence`, `EventCount`, `EvictedCount`, `CapturedAt`, `Entries`, `Dispose()`. |
| `LogChangeSet` | `FromVersion`, `Snapshot`, `Version`, `Generation`, `RequiresReset`, `AddedEntries`, `RemovedEntryIds`, `Dispose()`. |
| `LogStore` | Constructor `(maxEntries, maxCharacters, maxPendingCharacters, clock, schedule)` with defaults; `Generation`, read-only `RejectedCount`, `Changed`, `Add(level, sourceId, message, timestamp)`, `Add(write)`, `AddBatch(generation, writes)`, `Clear()`, `CaptureSnapshot()`, `GetChangesSince(version)`, `IsCurrent(generation)`, `IsCurrent(generation, entryId, textVersion)`, `SetReady(ready)`, `Dispose()`. |
| `ConsoleReadingAnchor` | `RowId`, `Sequence`, `TextOffset`, `PixelOffset`, `Generation`, `ThroughSequence`, `RowOrder`. |
| `ConsoleFollow` | Closed nested `Following` and `Paused`; `Paused.Anchor`, `Paused.PausedAt`. |
| `ConsoleViewState` | `Follow`, `ExpandedIds`, `Selection` (`ImmutableHashSet<long>` of raw EntryIds), `IsExpanded`, `Pause(projection, rowId, textOffset, pixelOffset, pausedAt)`, `Resume()`, `Remap(previous, current)`. |
| `ConsoleSearchArea` | `Message`, `Source`. |
| `ConsoleSearchHit` | `Area`, `Start`, `Length`. |
| `ConsoleRow` | `Id`, `Level`, `SourceId`, `TextContent` (`ILogTextContent`), `TextVersion`, `LinkSpans`, `MemberSequences`, `FirstTimestamp`, `Timestamp`, `LastSequence`, `SearchHits`, `TimeText`, `Count`. |
| `ConsoleProjection` | `Version`, `Generation`, `LastSequence`, `CapturedAt`, `TimeBase`, `Rows`, `LevelCounts`, `SourceCounts`, `RetainedMembership`, `EventCount`, `NewSincePauseCount`, `EvictedCount`, `Deduplicate`, `ResolvedAnchorId`, `RowCount`, `MatchingEventCount`, `DuplicatesMerged`, `IsEmpty`, `Dispose()`. |
| `ConsoleProjector` | `Project(snapshot, filter, viewState)`. |
| `LinkKind` | `Url`, `File`, `Folder`. |
| `LinkTarget` | `Kind`, `Path`, optional `Line`, optional `Column`. |
| `ConsoleLinkSpan` | `Start`, `Length`, `Target`. |
| `ConsolePathResolutionRequest` | `Target`, `MaxCandidates = 16`, `MaxProbes = 128`. |
| `IConsolePathResolver` | `PolicyVersion`, `ResolveAsync(request, cancellationToken)`. |
| `ConsoleLinkCapabilities` | `CanOpen`, `CanOpenContainingFolder`, `SupportsLine`, `SupportsColumn`. |
| `IConsoleLinkOpener` | `GetCapabilities(target)`, `OpenAsync(target, cancellationToken)`. |
| `ConsoleLinkScanner` | `Scan(text)`. |
| `ConsoleLinkIndex` | Constructor `(spans, textLength)`, `Spans`, `HitTest(offset)`. |
| `ConsoleLinkCache` | Constructor `(maxEntries = 10000, maxSpans = 65536, maxTargetCharacters = 4194304)`, `Synchronize(snapshot, resolverPolicyVersion = 0)`, `GetLinks(snapshot, entry, resolverPolicyVersion = 0)`. |
| `ConsoleExportOptions` | `IncludeTime = true`, `IncludeLevel = true`. |
| `ConsoleExportFormatter` | `FormatVisible(projection, options)`, `FormatSelection(projection, selection, options)`, `WriteLogAsync(destination, projection, options, cancellationToken)`. |

## Synchronization and reused Core mechanisms

`LogStore._gate` protects admission queues, unpublished writer ownership, cleanup queues, rejection count, the next sequence, generation, readiness, disposal, subscriptions, and the published reference.
The writer alone owns the ring, group index, bounded history, version, eviction total, and notification cursor.
One interlocked flag owns writer scheduling.
Published-state reference counts use Interlocked.
`ContentOwner._contentGate` protects reference lifetime only.
Active reads pin the content before invoking app code outside the lock.
Each snapshot disposes its leases once through `Interlocked`.
`ConsoleLinkCache._cacheGate` protects its cache and revision stamp.
All other model state is immutable.
Hosts own view-state replacement and command access on one thread.

The time API is BCL `TimeProvider`.
Tests reuse Core's `Time.DelegateTimeProvider`.
Open results reuse `SourceFileNavigation.SourceFileOpenResult`.
Lifecycle in the implementation baseline has no reusable generation helper.
It has only `CoalescedRefresh` and `UndoService`.
The former cannot atomically combine ready state with the store's pending work.
MessageCenter's generation belongs to its modal session.
Persistence's generation belongs to its save coordinator.
Neither fits this store.
One internal `ConsoleGeneration` helper provides all store generation advances and stale-work checks.
It is protected by the store lock.

## Verification and adoption

Tests cover startup deltas, Clear, reachable drain interleavings, each budget, content leases, collisions, dedupe, counts, search, pause, selection, scanner syntax, scanner IL, cache invalidation, interval hit testing, and exact export bytes.
Regressions cover reentrant app callbacks, serialized delivery, frozen anchor successors, canonical selection, and closed follow constructors.
They also cover collapsed latest-snapshot export, absolute and hidden time text, and documentation hygiene.
A generated 16 Mi-character message charges 64 resident characters.
Its test checks projection allocations below 256 KiB, reads of at most 1,024 characters, and streamed writes of at most 4,096 bytes.
It verifies search across chunk boundaries and UTF-8 surrogate pairs without retaining the full export.
Link scanning of the same content also stays below 256 KiB of allocations and 1,024 characters per read.
It covers both plain text and a short URL inside a very long quoted candidate.
Lazy oversized batches stop preparation at the first excess input and transfer no prefix.
An unending zero-charge batch verifies the preparation handle bound.
Held-writer tests compare derived queued, discarded and active ownership against accepted content lifetimes.
They cover character and handle peaks, repeated Clear, deferred disposal, atomic batch rejection, and ID order under concurrent rejection.
Rejection tests check counters, silent failure, caller ownership, no consumed IDs and no publication.
Published snapshot cleanup has a separate regression for its exclusion from pending capacity.
Latest capture and export tests enter their publication barrier before releasing the writer.
Capture inside writer callbacks and disposal of a waiting reader have timeout regressions.
Both scanner entry points share a Unicode scalar grammar with UTF-16 spans.
Tests cover supplementary path characters across read boundaries and URLs adjoining CJK prose.
Named dot-prefix and mark regressions cover both entry points and every interior name and suffix boundary.
The deterministic differential corpus has 115 distinct lines, including every scanner example from fix-round-4 through fix-round-8 reviews.
It checks every UTF-16 split and every pair of interior splits for lines of at most 32 characters.
Physical content splits also cover surrogate pairs and all three Unicode mark categories.
Padding moves each split onto the scanner's actual 1,024-character read boundary.
Its 13,181 comparisons include 10,971 three-way splits.
Independent expected spans require whole-component starts and unchanged relative targets, as well as parity.
Mark categories also share the URL and absolute-prefix word boundary.
Regressions reject embedded drive and URL prefixes after spacing and enclosing marks inside a word.
The required regressions cover callbacks that log, eight concurrent producers, pending overflow, concurrent Clear and Dispose, and ascending returned IDs.
They also cover empty and faulted empty batches, nested preparation callbacks, and blocked inline scheduling.
Reservation and queued-drop tests were removed or rewritten for admission ownership and rejection.
Pause regressions cover post-pause duplicates, eviction of a group's original member, and dedupe anchor remapping.
Link tests cover matching quotes and prefixes, paths, quotes, and location suffixes across read boundaries.
Quoted filenames with spaces and Unicode need no separator when they have a location suffix.
Both scanner entry points cover every interior read boundary in the quoted name and suffix.
Quoted names without a separator or suffix remain plain text.
Mixed Add, rejected Add, AddBatch and Clear calls each have a latest-capture regression.
The admission fence has no independently stored counter.
The fix-round-6 drive and UNC prefix regressions beside CJK prose remain unchanged and pass.
The deterministic writer explorer covers every reachable enqueue, ready-switch, post, and writer step for its bounded scenario.
The performance test skips unless `NVT_CORE_PERF=1`.
It projects 10,000 retained events with dedupe and search after one warm-up.
It prints elapsed time without a timing assertion.

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
$env:NVT_CORE_PERF = '1'
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~ConsolePerformanceTests" --logger "console;verbosity=detailed"
```

Verified on 2026-10-08 in Debug configuration after fix round 9.
The solution build completed with zero warnings and zero errors.
Solution tests passed 3,706 cases and skipped 16 cases, with zero failures.

| Test project | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Nvt.Core.Tests | 3,077 | 16 | 0 |
| Nvt.Core.Avalonia.Tests | 604 | 0 | 0 |
| Nvt.Core.Fonts.Tests | 25 | 0 | 0 |

LogConsole passed 181 cases and skipped its opt-in performance case.
All 32 fix-round-5, 9 fix-round-6, 10 fix-round-7 and 11 fix-round-8 regression cases passed.
All 18 fix-round-9 regression cases passed, including the differential corpus and embedded-prefix mark cases.
The six store failures recorded in the fix-round-6 review now pass.
The separate performance run passed one case with no skips or failures.
The 10,000-event projection took 25.604 ms and produced 1,000 rows.
This is one measured projection after one warm-up, not a timing bound.
In the restricted sandbox, TEMP and TMP use a generated directory inside the worktree.
This lets existing native Windows custody tests use a permitted temporary root.

UI virtualization, pointer coordinates, keyboard commands, accessibility, and visual evidence belong to K2.
Host adoption remains separate.
Real resolver, opener, clipboard, and spill-store integration remain app responsibilities.

