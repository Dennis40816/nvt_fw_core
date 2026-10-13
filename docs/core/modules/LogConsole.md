[English](LogConsole.md) | [繁體中文](LogConsole.zh-TW.md)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

# LogConsole

`Nvt.Core.LogConsole` is the shared non-UI console model for NFC, NFH, and NFU.
It targets `net10.0`.
It adds no package.
The controller and controls live in `Nvt.Core.Avalonia.LogConsole`. The controller owns projection and durable state; the list is described under [List view](#list-view).

## Design and provenance

This implements the approved Console design.
It does not port a product console.
The behavior reference is the earlier NFH console and the approved console redesign proposal.
An NFH measurement included product projection and UI work; the Core performance test measures only projection.
These workloads do not establish a comparable UI speedup.
The non-UI layer includes no UI or product logic.

## State structure

Event-filter inputs live in one `ConsoleFilter` record.
It contains levels, sources, literal search, only-matches, dedupe, and time mode.
An empty source set means all sources.
An empty level set means no levels.
An empty search means no search condition.
Source IDs use ordinal identity.
Search uses ordinal case-insensitive matching.

`LogStore` is the sole event owner.
It publishes a leased immutable `LogSnapshot`.
For every store-created `LogEntry`, `EntryId` equals `Sequence`; selection and projection membership use this invariant.
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
The host must reject background projections when snapshot, filter, or presentation inputs have changed.
The Avalonia controller owns command routing for the header and toolbar.
The non-UI layer supplies immutable inputs and projection mechanisms.
The Avalonia list supplies virtualization, row interaction, keyboard routing and automation peers.

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
Structured span validation checks neighbours in linear time for sorted starts and sorts an index copy for unsorted starts in O(n log n) time.
A short admission lock checks total capacity before assigning IDs and enqueuing in sequence order.
Producers never wait for capacity, the writer, or a callback.
Publication is asynchronous.
`CaptureSnapshot()` immediately returns the last published version. It never waits for the writer.
The published version can lag behind accepted writes and Clear.
`CaptureLatestAsync(cancellationToken)` asynchronously covers every Add and Clear admitted before its call.
Its admission fence is derived under `_gate` from the next sequence plus the generation.
Rejected writes and empty batches do not advance that fence.
Cancellation or store disposal cancels a pending barrier without blocking a thread.
Calling CaptureLatestAsync after disposal throws ObjectDisposedException.
Writer failures fault outstanding barriers with the original exception and requeue unconsumed ownership.
The diagnostic trace also reports writer failures.
A callback that disposes the store leaves cleanup for the writer before it stops.
A throwing content Dispose does not prevent other cleanup; each content disposal is invoked once.
On the writer thread, including Changed and content Dispose callbacks, either capture API returns the current publication.
The writer runs independently of notification readiness.
`GetChangesSince(version)` and `IsCurrent` also read the published state without waiting.

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
The writer reads the notification clock after publication, comparison, and cleanup, only for ready delivery with subscribers.
A clock exception leaves only Changed delivery pending until the next explicit writer wake, such as Add, Clear, or SetReady(true).
Wakes for work still awaiting publication, or arriving during or after the failed attempt, are coalesced into a notification request for the next writer turn.
The clock fault alone causes no retry or data requeue; publication and pending-capacity release still complete.
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

## Presentation contracts

These contracts follow the approved Console redesign, **v3 更新**, **由 app 注入 / SourceRegistry**,
**增量連結解析**, and **去重與篩選的投影**. They add no UI, product type, resource lookup,
thread-culture read, local-zone lookup, or file-system access. Store admission, ownership,
rejection, dedupe, and scanner grammar remain unchanged.

### Source registry

The app supplies `ConsoleProjectionOptions.SourceRegistry`, an immutable array of `ConsoleSource` values.
Each value supplies a stable ordinal `SourceId`, a `DisplayName`, and ascending `DisplayOrder`.
IDs must be unique; null entries, IDs, or names and an uninitialized array are invalid.
Equal display orders preserve input order. Empty IDs remain compatible with store IDs.
`ConsoleProjection.Sources` is the authoritative display order: all declarations first,
then unknown IDs in their first retained sequence order. Unknown names equal their IDs, and unknown sources use `int.MaxValue` as their display order.
The dictionary `SourceCounts` is seeded with zero for every declared source and still counts
raw events after the level filter only. Filtering and search continue to use IDs.
No count is stored in source metadata, and no display name is copied into every row.
Replacing the registry changes the next projection, even with the same snapshot version.
A host must include registry/presentation replacement in its stale-result check.

### Projected links

`ConsoleLinkCache.GetLinks(snapshot, row)` accepts the snapshot used to
produce the row. The caller passes a `ConsoleRow` directly, with no raw-entry lookup.
The row's existing projection lease supplies segmented text and structured spans.
For dedupe, this is the latest retained member, even after the original member is evicted.
No new text owner or full-message copy is introduced. Entry and group IDs have distinct cache keys.
The overload shares the raw-entry overload's scanner, index, lock, synchronization, and all three
retention budgets. Search and source-name changes do not invalidate links.
Call `Synchronize` for each accepted snapshot, including Clear. Old rows remain readable through
their leases, but a stale snapshot or representative cannot populate the current cache.
A representative change invalidates group results even at the same text version because supplied
spans are not part of the dedupe key. Oversized results are complete but uncached.

### Explicit time presentation

The additive `Project(snapshot, filter, viewState, options)` overload uses one pure
`ConsoleTimeFormatter.Format(timestamp, timeBase, mode, relativeTimeTemplate, culture, absoluteTimeZone)` path.
The original overload uses invariant culture, the approved default `Console.Timestamp.Ago`
template `"{0} s ago"`, and UTC. The app supplies its resolved resource template and explicit
`CultureInfo`; it must not mutate that culture during a projection.
Placeholder 0 receives seconds formatted as `"0.0"` in that culture, such as `2.3` or `2,3`.
The template is formatted only in Relative mode: a malformed template throws `FormatException` from the projection in that mode and is ignored in Absolute and Hidden modes.
Pause still uses `PausedAt`, independent of later captures or presentation changes.
Hidden time stays empty. Absolute time keeps invariant `HH:mm:ss.fff`, converted through the
explicit `TimeZoneInfo`; the app may pass its chosen local zone or UTC. Core never chooses local time.
Export and copy continue to use the store's UTC occurrence timestamps, independently of screen options.

### First-line metadata

`ConsoleRow.GetFirstLine(maxCharacters = 1024)` delegates to pure `ConsoleFirstLine.Read(content, maxCharacters)`.
It returns `Text` and `HasMoreContent` on demand, without storing a newline or truncation flag on a row.
CR, LF, and CRLF end the first line and are excluded from the preview.
The cap is in UTF-16 characters, allows 0 through 4,096, and never splits a valid surrogate pair.
At most cap + 1 characters are read, in requests of at most 1,024, and only the bounded prefix becomes a string.
`HasMoreContent` means the preview omits any original content, including a terminal line break.
The UI determines additional truncation caused by its available width.

### Assumptions

The design leaves these details open; this module assumes stable input order for equal declared orders,
first appearance within the retained snapshot for unknown sources (no historical registry in the store),
a 4,096-character hard preview ceiling, and omitted terminal line breaks as more content.
The latest retained member remains the group's link representative; replacing it invalidates its
cache slot to honor differing structured spans. A matching snapshot accompanies row link access
for the existing live-cache generation and version fence.

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
Known limitation: paired quoted prose that contains a separator is treated as one path, including quotes of the other type nested inside it. `"Read/write error"` and `"could not open 'C:\My Docs\a.txt'"` each produce one file link over the whole quoted text, not over the inner path.
With `"unpaired load/save then 開啟"資料/a.txt"`, the CJK closing rule produces the quoted prose path and the following unquoted `資料/a.txt` path.
An app that needs the exact target supplies structured spans.
A trailing separator explicitly denotes a folder, including targets with location suffixes.
An extensionless path can still be a file.
Other ambiguous folder targets require structured spans.
There is no extension allowlist.
There are no existence checks or repository scans.

Quoted targets require an opening quote at the start of text, after whitespace, or after a non-name or CJK scalar, followed by content that can start a path.
Quoted content can also start with `~`, `%`, or `$`. Leading dots must lead to a name character or separator; whitespace, a lone prose dot, location-only punctuation, and end of text do not start quoted content.
Inside a candidate, a matching quote after a CJK scalar or path separator and before CJK prose closes the target, including a path whose final component is CJK or whose trailing separator denotes a folder.
Otherwise, a later valid opening quote of the same type abandons the earlier candidate before any closing check.
Otherwise, a matching quote closes before whitespace, CJK prose, punctuation (including location suffix openers), or end of text.
Abandoned or unpaired candidates resume scanning immediately after their original opening quote. Both entry points use this single quote grammar. Unpaired quotes and prose apostrophes do not hide later targets.
Separator-free location targets require a non-leading dot followed by a letter.
Candidates over 4,096 UTF-16 characters (including location suffixes, excluding surrounding quotes) are skipped before materialization.
The whole skipped quoted span, including its quotes, is excluded from further scanning. Later independent targets remain eligible.
The quoted candidate's cap is evaluated before overlap; an embedded URL already accepted by the URL pass remains a link.
App-supplied `LinkSpans` are validated during Add preparation, including whole-batch validation before admission.
Default arrays, null spans or targets, invalid ranges, and overlaps throw ArgumentException without transferring ownership.

App-supplied spans replace scanning completely.
An explicitly empty span array also wins.
`ConsoleLinkIndex` validates ranges and provides binary hit testing.
`ConsoleLinkCache` keys results by discriminated entry/group identity and text revision.
A group result is also bound to its latest retained representative, so changed app spans cannot reuse a previous representative's result at the same text version.
Search is not a cache key.
`Synchronize` is its only semantic invalidation point.
Call it for every accepted snapshot, including Clear.
Older snapshots cannot refill the live cache.
Scanning runs outside the cache lock.
The cache scans segmented content with a fixed 1,024-character read buffer.
Candidate offsets survive read boundaries.
Only confirmed link targets become strings.
Plain message text is never materialized in full.
Publication checks the current snapshot generation, version, live membership and text revision again.
Entry count, span count, and target characters each bound cache retention.
Oversized results are returned without caching.

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
Host-provided copy-visible and file-export commands await `CaptureLatestAsync(cancellationToken)` and project that frozen snapshot.
The controller's bounded selection copy validates the latest published snapshot synchronously; accepted writes can still await publication.
Formatting methods accept an existing projection and do not acquire or refresh store state.

## Hosting and lifecycle

- [ ] Register the running dispatcher with `UiThread.RegisterRunningDispatcher` on the UI thread. Load Core theme tokens, font roles, `LogConsole/LogConsoleStyles.axaml` and `LogConsole/ConsoleListStyles.axaml`; configure fonts with `WithNvtCoreFonts()`.
- [ ] Keep an app-owned `LogStore` alive for the controller's lifetime. Construct `ConsoleController(store, sources, options)` on that dispatcher. Supply an immutable `ConsoleSource` registry through `sources`, and explicit template, culture and absolute time zone through `ConsoleProjectionOptions`. A nonempty `options.SourceRegistry` must agree with `sources`.
- [ ] Set the controller's init properties `LinkOpener` and `Clipboard` before binding. Supply the host's path resolver, cached availability and opening policy through `IConsoleLinkOpener`. Supply platform clipboard dispatch through `IConsoleClipboard`. Adapter methods run on the UI thread and must return promptly; perform IO and asynchronous work in the host.
- [ ] Compose a `ConsoleToolbar`, `ConsoleListView` and `ConsoleEmptyState`, with an optional `ConsoleHeader`. Assign the same `Controller` to each. Bind the list's `Projection`, `ViewState`, `TimeOptions` and `TimeMode` to that controller's `Projection`, `ViewState`, `Options` and `Filter.TimeMode`. Route `ViewStateRequested` to `RequestViewState` and return accepted state through the binding. The empty state derives its visibility from the projection.
- [ ] Let the controller own filters, following or the paused anchor, expansion, selection and projection leases. Controls borrow those inputs and own only attachment-local interaction, handlers and display caches. The host owns the store, injected adapters, theme, app log directory, copy-visible/file commands and storage dialog. An optional header can use `CopySelectionCommand` for `CopySelectedCommand`; the host supplies `CopyVisibleCommand` and `SaveLogCommand`.
- [ ] Keep the same controller when hiding a page. Construction subscribes before startup capture. Once Console controls are hosted, they share the controller's store subscription while any remain attached. Detaching the last control unsubscribes and aborts its queued refresh; it retains durable controller state. List detach clears hover, pressed links, pointer capture, focus/range origin, menus and deferred scroll/drag intent. Reattaching restores accepted reading state and refreshes from the latest published store snapshot. Independent controllers over the same store have independent attachment lifetimes. A view that is dropped without leaving the visual tree never detaches, so the controller stays subscribed until disposal. Attaching after the app disposed the store leaves the controller detached and shows the last projection.
- [ ] Release any host-owned event subscriptions when abandoning a view. Dispose the controller on the UI thread when the page is permanently discarded; repeated `Dispose()` is harmless and releases its owned projection/snapshot leases and commands. Dispose the app-owned store separately after its consumers finish. A controller that has never had a hosted control observes until disposal. Controller construction calls `SetReady(true)` for the store; detach and disposal leave that store-wide readiness unchanged.
- [ ] Create controls, assign their inputs, accept state requests, invoke controller commands and dispose on the UI thread. Producers may add to the store from other threads; controller notifications coalesce onto the registered dispatcher. Do not dispose a borrowed controller projection. For host-owned copy-visible and file export, await `CaptureLatestAsync(cancellationToken)` and own/dispose the frozen snapshot and projection used by `ConsoleExportFormatter`.

## Avalonia controller and toolbar

`Nvt.Core.Avalonia.LogConsole.ConsoleController` is the single writer of the immutable
filter and view-state inputs. Construct it on Core's registered `UiThread`, passing
an app-owned `LogStore`, an immutable `ConsoleSource` catalog, and optional
`ConsoleProjectionOptions`. The catalog becomes `Options.SourceRegistry`; time mode
remains in `Filter.TimeMode`. Template, culture, and absolute time zone are explicit
inputs. Keep the supplied culture unchanged during use.

The controller subscribes before startup capture and enables store notifications.
Producer callbacks request one coalesced UI operation. Each refresh captures a leased
snapshot and calls `ConsoleProjector.Project` once. The controller remaps canonical
selection and expanded identities, including paused order across dedupe changes,
then publishes `Projection` through `INotifyPropertyChanged`. Counts are read from
that projection. A later background-priority dispatcher turn retires the previous
projection after bindings and layout have consumed its replacement. Owned content
is bounded to the current projection and one retiring projection. Before publication,
the refresh validates the snapshot generation with `LogStore.IsCurrent`, so Clear's
admission fence also rejects UI work queued before its writer reset. Disposal stops
the active notification sequence and defers lease release until its callbacks return.
A nested dispatcher pump cannot retire projections borrowed by those callbacks.
A state-fence rejection requeues the requested refresh with a single automatic retry;
cleanup notifications do not replenish that retry. Command validation checks controller
lifetime before and after snapshot acquisition. Store shutdown cancels validation and
delivers nothing to app adapters.
Command availability (menu items and the `CanExecute` of the copy commands) reads the
published `Projection` and `ViewState` and captures nothing, so a refresh or a menu build
costs no projection. Availability can be true for a moment while the store is ahead of the
published projection. Executing a command validates against a fresh capture and delivers
nothing when the state changed. `ConsoleListView.Projection` and `ViewState` must come from
the controller assigned to `Controller`; a mismatch makes the commands act on rows the view
does not show.

The list borrows `Projection` and reports immutable intents through
`RequestViewState(state)`. It does not dispose that projection or mutate controller
inputs. `Pause(rowId, textOffset, pixelOffset)` captures the reading anchor and samples
the injected store clock through a fresh nonblocking snapshot for the pause instant;
`Resume()` is the jump-to-latest intent. Collapse is a view-state change and preserves
follow state. Page hiding detaches controls and preserves the controller. The host calls
`Dispose()` when the page is permanently discarded; it unsubscribes, aborts queued
work, and releases owned projections. Repeated disposal is safe. The app retains store
ownership. The controller calls `SetReady(true)` for the whole store during construction;
`Dispose()` does not restore its previous readiness. This also affects other subscribers.
The host must account for that persistent store-wide notification state.

Toolbar actions use `ToggleLevelCommand` (a `LogLevel` parameter),
`ToggleOnlyMatchesCommand`, `ToggleDedupeCommand`, `SetTimeModeCommand` (a
`ConsoleTimeMode` parameter), `ClearCommand`, and `ResetFiltersCommand`.
`SetSelectedSources(ids)` selects ordinal IDs; an empty set selects all sources.
In the source menu, All sources checks every source and includes future sources.
Clicking a checked source excludes it from the current catalog; subsequent clicks
toggle explicit membership. The last checked source is disabled, preserving the
empty-set-means-all contract. All sources restores the unrestricted selection.
Detach and controller replacement close and clear the source menu, release its
bindings, and invalidate retained item commands.
While the source menu is open, projection catalog changes rebuild its items in place
through the same builder, preserving the open popup. Membership is compared directly
with the projection; the view keeps no separate source catalog. Missing source IDs
resolve as unchecked with a zero count, using the ID as the fallback label.
`SetSearchText(text)` treats whitespace alone as an empty search. Reset restores the
default filters and dedupe while preserving time presentation and reading state.
All intents and disposal require the UI thread. Export flags belong to
`ExportOptions`, with `ToggleExportTimeCommand` and `ToggleExportLevelCommand`.

`ConsoleHeader`, `ConsoleToolbar`, and `ConsoleEmptyState` receive a `Controller`
styled property. Load `LogConsole/LogConsoleStyles.axaml` like the other Core style
entry points; the controls include `LogConsole/ConsoleControlStyles.axaml` locally. Hosts load Core theme tokens and
Fonts roles and keep the console at least 640 DIP wide. The wide layout places
search beside the title at wide widths, with levels and sources in the filter row.
At 960 DIP and below, search and Only matches move to the second filter row; Display
contains time and dedupe, and More contains export and clear. Header height is 48 DIP;
the filter row is 48 DIP wide and 88 DIP narrow. Resting actions have no fill or border;
level buttons show semantic icons with complete raw counts in Core tooltips. Shared
icon corrections follow rendering scale and unsubscribe on detach. The empty view
is visible when `Projection.IsEmpty`. With no retained events (`Projection.EventCount == 0`),
it shows the no-events message and hides Reset filters. A filtered empty result shows
a localized filter summary and Reset filters.
Console action buttons and menu items bind their family, size, and weight directly
to the Core Body role through local dynamic-resource styles, including Chinese source
labels. Other controls retain their existing font roles.

The header's `Title` defaults to Console. The app binds `CopySelectedCommand`,
`CopyVisibleCommand`, and `SaveLogCommand`; the menu supplies the independent Include
time and Include level options. Header menu bindings follow the header's current
controller directly, so popup dismissal does not disconnect an executing command.
The host provides clipboard and file adapters; `ConsoleListView` provides row navigation and rendering.

The constructor validates `RelativeTimeTemplate`, so malformed composite formatting is rejected
as an `ArgumentException` naming `options`, with the original `FormatException` as its cause.
A refresh failure keeps the previous projection and publishes the exception through read-only
`RefreshError`. The next successful accepted refresh clears it. Failed projections release their
fresh leases. Refresh requests are queued before input notifications, including when an observer
throws. Search input and clear-search actions stop invoking a disposed controller.
Notifications raised from `SetFilter` (including search, source, and command inputs) and
`RequestViewState` propagate subscriber exceptions synchronously to the caller.
Accepted refreshes update all state before notifying `ViewState`, `Projection`, then a changed
`RefreshError`. Refresh notifications collect every subscriber exception and complete the sequence
before posting a preserved rethrow on the UI dispatcher, where it enters the dispatcher's unhandled
exception path. A single failure is rethrown directly; multiple failures use one `AggregateException`.
The previous projection retires only after replacement notification is attempted. A throwing failure
observer leaves the previous projection and later refreshes usable.

### Resource keys

`LogConsole/ConsoleResources.axaml` defines English defaults and structural dimensions and is
merged by `LogConsoleStyles.axaml`. Controls include the resource-free control styles locally;
their dynamic resource bindings use the same dictionary only as a fallback for missing keys.
Hosts can override `Nvt.Console.*` in application, window, or console ancestor resources,
including at runtime. Malformed host templates fall back to the built-in English template on `FormatException`.
`MinimumWidth` and `NarrowBreakpoint` are the single shared definitions
for all console surfaces. Composed heights follow the existing dynamic control and spacing tokens.

Time modes and level labels are mapped through keys. The controller exposes filter data;
`FilterSummary` is no longer public API. The view derives summary text from resource templates.
`Count` accepts a label and raw count; `Sources.Selected` and `Dedupe.Count` accept a count.
`Empty.NoMatches` accepts the derived filter summary. `Filter.Search` accepts the literal query
and search-mode label; `Filter.Summary` accepts levels, sources, search suffix, and dedupe suffix.
Keep these composite placeholders valid. `Title` is the default title; an explicit host title wins.
The separate injected `ConsoleProjectionOptions.RelativeTimeTemplate` continues to format event time.

| Key | English default / DIP value |
| --- | --- |
| `Nvt.Console.MinimumWidth` | `640` |
| `Nvt.Console.NarrowBreakpoint` | `960` |
| `Nvt.Console.Title` | `Console` |
| `Nvt.Console.Search.Placeholder` | `Search messages, sources or paths` |
| `Nvt.Console.Search.Name` | `Search console` |
| `Nvt.Console.Search.Clear` | `Clear search` |
| `Nvt.Console.OnlyMatches` | `Only matches` |
| `Nvt.Console.Export` | `Export` |
| `Nvt.Console.Export.Name` | `Export console` |
| `Nvt.Console.Display` | `Display` |
| `Nvt.Console.Display.Name` | `Display console` |
| `Nvt.Console.More` | `More` |
| `Nvt.Console.More.Name` | `More console actions` |
| `Nvt.Console.Time.Name` | `Time display` |
| `Nvt.Console.Time.Absolute` | `Absolute time` |
| `Nvt.Console.Time.Relative` | `Relative time` |
| `Nvt.Console.Time.Hidden` | `Hidden time` |
| `Nvt.Console.CopySelected` | `Copy selected` |
| `Nvt.Console.CopyVisible` | `Copy visible rows` |
| `Nvt.Console.SaveLog` | `Save as .log` |
| `Nvt.Console.IncludeTime` | `Include time` |
| `Nvt.Console.IncludeLevel` | `Include level` |
| `Nvt.Console.Clear` | `Clear console` |
| `Nvt.Console.Sources.Name` | `Select sources` |
| `Nvt.Console.Sources.All` | `All sources` |
| `Nvt.Console.Sources.Selected` | `Sources ({0})` |
| `Nvt.Console.Dedupe` | `Dedupe` |
| `Nvt.Console.Dedupe.Count` | `Dedupe ×{0}` |
| `Nvt.Console.Count` | `{0} · {1}` |
| `Nvt.Console.Empty.NoEvents` | `No events yet` |
| `Nvt.Console.Empty.NoMatches` | `No matching events · {0}` |
| `Nvt.Console.ResetFilters` | `Reset filters` |
| `Nvt.Console.Filter.NoLevels` | `no levels` |
| `Nvt.Console.Filter.Search` | `; search ‘{0}’ ({1})` |
| `Nvt.Console.Filter.MatchesOnly` | `matches only` |
| `Nvt.Console.Filter.Highlight` | `highlight` |
| `Nvt.Console.Filter.Dedupe` | `; dedupe` |
| `Nvt.Console.Filter.Separator` | `, ` |
| `Nvt.Console.Filter.Summary` | `{0}; {1}{2}{3}` |
| `Nvt.Console.Level.Trace` | `Trace` |
| `Nvt.Console.Level.Debug` | `Debug` |
| `Nvt.Console.Level.Info` | `Info` |
| `Nvt.Console.Level.Warn` | `Warn` |
| `Nvt.Console.Level.Error` | `Error` |
| `Nvt.Console.Level.Fatal` | `Fatal` |

## Public API

| Type | Public members |
| --- | --- |
| `LogLevel` | `Trace`, `Debug`, `Info`, `Warn`, `Error`, `Fatal`. |
| `ConsoleTimeMode` | `Absolute`, `Relative`, `Hidden`. |
| `ConsoleSource` | Constructor `(SourceId, DisplayName, DisplayOrder = 0)`, `SourceId`, `DisplayName`, `DisplayOrder`, positional `Deconstruct`: app source metadata without product types. |
| `ConsoleProjectionOptions` | Parameterless constructor, `SourceRegistry`, `RelativeTimeTemplate`, `Culture`, `AbsoluteTimeZone`: explicit app presentation inputs. |
| `ConsoleTimeFormatter` | `Format(timestamp, timeBase, mode, relativeTimeTemplate, culture, absoluteTimeZone = null)`: one pure time formatting path. |
| `ConsoleFirstLine` | Constructor `(Text, HasMoreContent)`, `Text`, `HasMoreContent`, positional `Deconstruct`, `Read(content, maxCharacters = 1024)`: bounded derived preview. |
| `ConsoleFilter` | `EnabledLevels`, `SelectedSources`, `SearchText`, `OnlyMatches`, `Deduplicate`, `TimeMode`. |
| `ConsoleRowId` | `Value`, `IsGroup`. |
| `ILogTextContent` | `Length`, `ResidentCharacterCount`, `Version`, `Read(offset, destination)`, `Dispose()`. |
| `InMemoryLogTextContent` | Constructor `(text, version = 0)` and the content interface members. |
| `LogWrite` | `Level`, `SourceId`, `TextContent`, optional `Timestamp`, optional `LinkSpans`. |
| `LogEntry` | `EntryId`, `Generation`, `Sequence`, `Timestamp`, `Level`, `SourceId`, `TextContent`, `LinkSpans`, `GroupId`. |
| `LogSnapshot` | `Version`, `Generation`, `LastSequence`, `FirstRetainedSequence`, `LastRetainedSequence`, `EventCount`, `EvictedCount`, `CapturedAt`, `Entries`, `Dispose()`. |
| `LogChangeSet` | `FromVersion`, `Snapshot`, `Version`, `Generation`, `RequiresReset`, `AddedEntries`, `RemovedEntryIds`, `Dispose()`. |
| `LogStore` | Constructor `(maxEntries, maxCharacters, maxPendingCharacters, clock, schedule)` with defaults; `Generation`, read-only `RejectedCount`, `Changed`, `Add(level, sourceId, message, timestamp)`, `Add(write)`, `AddBatch(generation, writes)`, `Clear()`, `CaptureSnapshot()`, `CaptureLatestAsync(cancellationToken = default)`, `GetChangesSince(version)`, `IsCurrent(generation)`, `IsCurrent(generation, entryId, textVersion)`, `SetReady(ready)`, `Dispose()`. |
| `ConsoleReadingAnchor` | `RowId`, `Sequence`, `TextOffset`, `PixelOffset`, `Generation`, `ThroughSequence`, `RowOrder`. |
| `ConsoleFollow` | Closed nested `Following` and `Paused`; `Paused.Anchor`, `Paused.PausedAt`. |
| `ConsoleViewState` | `Follow`, `ExpandedIds`, `Selection` (`ImmutableHashSet<long>` of raw EntryIds), `IsExpanded`, `Pause(projection, rowId, textOffset, pixelOffset, pausedAt)`, `Resume()`, `Remap(previous, current)`. |
| `ConsoleSearchArea` | `Message`, `Source`. |
| `ConsoleSearchHit` | `Area`, `Start`, `Length`. |
| `ConsoleRow` | `Id`, `Level`, `SourceId`, `TextContent` (`ILogTextContent`), `TextVersion`, `LinkSpans`, `MemberSequences`, `FirstTimestamp`, `Timestamp`, `LastSequence`, `SearchHits`, `TimeText`, `Count`, additive `GetFirstLine(maxCharacters = 1024)` for derived collapsed previews. |
| `ConsoleProjection` | `Version`, `Generation`, `LastSequence`, `CapturedAt`, `TimeBase`, `Rows`, `LevelCounts`, `SourceCounts`, additive `Sources` for ordered display metadata, `RetainedMembership`, `EventCount`, `NewSincePauseCount`, `EvictedCount`, `Deduplicate`, `ResolvedAnchorId`, `RowCount`, `MatchingEventCount`, `DuplicatesMerged`, `IsEmpty`, `Dispose()`. |
| `ConsoleProjector` | `Project(snapshot, filter, viewState)`, additive `Project(snapshot, filter, viewState, options)` for app inputs. |
| `LinkKind` | `Url`, `File`, `Folder`. |
| `LinkTarget` | `Kind`, `Path`, optional `Line`, optional `Column`. |
| `ConsoleLinkSpan` | `Start`, `Length`, `Target`. |
| `ConsoleLinkScanner` | `Scan(text)`. |
| `ConsoleLinkIndex` | Constructor `(spans, textLength)`, `Spans`, `HitTest(offset)`. |
| `ConsoleLinkCache` | Constructor `(maxEntries = 10000, maxSpans = 65536, maxTargetCharacters = 4194304)`, `Synchronize(snapshot)`, `GetLinks(snapshot, entry)`, additive `GetLinks(snapshot, row)` for projected links without raw-entry reconstruction. |
| `ConsoleExportOptions` | `IncludeTime = true`, `IncludeLevel = true`. |
| `ConsoleExportFormatter` | `FormatVisible(projection, options)`, `FormatSelection(projection, selection, options)`, `WriteLogAsync(destination, projection, options, cancellationToken)`. |

## Avalonia public API

Declared public types and members of `Nvt.Core.Avalonia.LogConsole` are listed below, including generated members and styled-property fields. Inherited Avalonia members are omitted. `get/set` means read-write, `get/init` means settable only at initialization, and `static readonly` means a static read-only field.

| Type | Public members |
| --- | --- |
| `ConsoleController` | Constructor `(store, sources, options = null)`; `PropertyChanged`; read-only `Filter`, `ViewState`, `Options`, `Projection`, `ExportOptions`, `RefreshError`, `CopySelectionCommand`, `ToggleLevelCommand`, `ToggleOnlyMatchesCommand`, `ToggleDedupeCommand`, `SetTimeModeCommand`, `ClearCommand`, `ResetFiltersCommand`, `ToggleExportTimeCommand`, `ToggleExportLevelCommand`; get/init `Clipboard`, `LinkOpener`; `SetSelectedSources(sources)`, `SetSearchText(text)`, `RequestViewState(state)`, `Pause(rowId = null, textOffset = 0, pixelOffset = 0)`, `Resume()`, `Dispose()`. |
| `ConsoleHeader` | Constructor `()`; generated `InitializeComponent(loadXaml = true)`; get/set `Controller`, `Title`, `CopySelectedCommand`, `CopyVisibleCommand`, `SaveLogCommand`; static readonly `ControllerProperty`, `TitleProperty`, `CopySelectedCommandProperty`, `CopyVisibleCommandProperty`, `SaveLogCommandProperty`. |
| `ConsoleToolbar` | Constructor `()`; generated `InitializeComponent(loadXaml = true)`; get/set `Controller`; static readonly `ControllerProperty`. |
| `ConsoleEmptyState` | Constructor `()`; generated `InitializeComponent(loadXaml = true)`; get/set `Controller`; static readonly `ControllerProperty`. |
| `ConsoleListView` | Constructor `()`; get/set `Controller`, `Projection`, `ViewState`, `TimeOptions`, `TimeMode`; static readonly `ControllerProperty`, `ProjectionProperty`, `ViewStateProperty`, `TimeOptionsProperty`, `TimeModeProperty`; `ViewStateRequested`; `JumpToLatest()`. |
| `IConsoleClipboard` | `SetText(text)`. |
| `IConsoleLinkOpener` | `GetUnavailableReason(target)`, `Open(target)`. |

## Synchronization and reused Core mechanisms

`LogStore._gate` protects admission queues, unpublished writer ownership, cleanup queues, rejection count, the next sequence, generation, readiness, disposal, subscriptions, and the published reference.
The writer alone owns the ring, group index, bounded history, version, eviction total, and notification cursor.
One interlocked flag owns writer scheduling.
Published-state reference counts use Interlocked.
`ContentOwner._contentGate` protects reference lifetime only.
Active reads pin the content before invoking app code outside the lock.
Each snapshot disposes its leases once through `Interlocked`.
`ConsoleLinkCache._cacheGate` protects its cache and one immutable state containing the live entry/group revisions and revision stamp; synchronization replaces this state together.
All other model state is immutable.
The Avalonia controller replaces filter and view-state inputs and owns command access on
Core's registered UI thread. Hosts and the list submit immutable view-state intents
through `RequestViewState`; they do not replace the controller's state directly.

The time API is BCL `TimeProvider`.
Tests reuse Core's `Time.DelegateTimeProvider`.
The shared lifecycle utilities have no store-compatible generation helper.
It has only `CoalescedRefresh` and `UndoService`.
The former cannot atomically combine ready state with the store's pending work.
MessageCenter's generation belongs to its modal session.
Persistence's generation belongs to its save coordinator.
Neither fits this store.
One internal `ConsoleGeneration` helper provides all store generation advances and stale-work checks.
It is protected by the store lock.

## Verification and adoption

Regressions cover bounded admission, all-or-nothing batches, reentrant callbacks, generation resets,
snapshot leases, writer failure recovery, nonblocking dispatcher reads, cancellable async barriers, isolated callback faults, notification clock recovery,
Unicode and quote grammar across read boundaries, target length limits, structured span validation,
and async-only stream export. Search uses one chunk buffer per projection.
The deterministic scanner corpus checks every split against independent expected spans.
The generated quote oracle constructs expected links from text parts, including adjacent CJK prose and later quotes, and checks both entry points at every storage and scanner read split.
The non-UI projection benchmark is opt-in with `NVT_CORE_PERF=1` and has no timing assertion.

```powershell
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build --no-restore
```

The Console controls, link and selection interaction, keyboard commands, accessibility and visual evidence are implemented.
The host supplies path policy, opening, clipboard and file adapters, the storage dialog, theme and the app log directory.
NFH adoption of the Console is separate and planned for Core 0.9.1.

## List view

`Nvt.Core.Avalonia.LogConsole.ConsoleListView` is an opt-in templated list control.
Load `avares://Nvt.Core.Avalonia/LogConsole/ConsoleListStyles.axaml`, the Core theme tokens,
and the font roles; configure the host with `WithNvtCoreFonts()`.
The list style composes Core's existing button, scroll and icon styles.
It loads `ConsoleListGeometry.axaml` for structural dimensions and uses the shared Core icon size,
spacing and control height tokens. Level glyphs all come from `NvtIcons`.
It has no dependency on a panel controller or an app type.

| Member | Caller contract |
|---|---|
| `Projection` / `ProjectionProperty` | Borrowed `ConsoleProjection`; its owner keeps leases alive until replacement. A controller owns disposal of its own projection. |
| `ViewState` / `ViewStateProperty` | The caller's immutable `ConsoleViewState`; expansion, pause and resume are never assigned by the list. |
| `TimeOptions` / `TimeOptionsProperty` | Explicit `ConsoleProjectionOptions` culture, relative template and absolute zone. `SourceRegistry` is ignored; the source column displays the source ID. |
| `TimeMode` / `TimeModeProperty` | `ConsoleTimeMode`, default Absolute; Hidden removes the time column. |
| `ViewStateRequested` | `EventHandler<ConsoleViewState>` carrying the requested replacement state. |
| `JumpToLatest()` | Requests Resume; a host command can bind Ctrl+End here. |

Assign inputs and accept requests on the UI thread. With a controller, route requests to `RequestViewState`
and bind its accepted `ViewState`. A standalone list caller owns and replaces that state.
The list never invokes `ConsoleProjector`, subscribes to a store, or disposes a projection.
Projection assignment synchronously rebinds surviving presenters, so the caller can dispose the preceding
projection immediately after the assignment returns, even before layout or another replacement.
Collapse of the containing console preserves the externally owned state.

Collapsed rows use `ConsoleRow.GetFirstLine` and have a fixed 20 DIP height.
Columns are time 104, level 84, source 120, remaining message, repeat 48, and arrow 24 DIP,
with 16 DIP content insets. Level has both a shared Material Symbols glyph and a name.
Long sources use character ellipsis with the complete source ID in a tooltip.
The arrow appears for omitted content or actual width truncation; repeat text appears only for duplicates.
Expanded rows preserve newlines, wrap and keep metadata on the first line.
Message uses Body, time/source MonoCaption, repeat Numbers and glyphs Icon, including each role's family, size and weight.
Time text is produced by `ConsoleTimeFormatter` from `Projection.TimeBase`, with no timer or implicit local clock.

User text resolves through the host's resources, with English defaults in `ConsoleListGeometry.axaml`.
Override these keys on the list or a resource ancestor to localize it. Jump and retention formats use
`TimeOptions.Culture`; `{0}` is the projection's new-message or eviction count. Exactly one message or
eviction selects its singular key; zero and other counts select the plural key. Resource changes refresh the text.
Every listed text key falls back to its built-in English default when missing, non-string, blank or a malformed
composite format, including an unavailable argument. Text overrides never throw from measure or resource handlers.
Unknown level keys fall back to their suffix. Level labels take no format arguments; escape literal braces as `{{` and `}}`.

| Resource key | English default |
|---|---|
| `Nvt.Console.List.JumpToLatestOne` | `Jump to latest ({0} new message)` |
| `Nvt.Console.List.JumpToLatestMany` | `Jump to latest ({0} new messages)` |
| `Nvt.Console.List.RetentionOne` | `Retention changed · {0} message evicted` |
| `Nvt.Console.List.RetentionFormat` | `Retention changed · {0} messages evicted` |
| `Nvt.Console.List.Name` | `Console messages` |
| `Nvt.Console.List.Row.NameFormat` | `{Level}, {Time}, {Source}, {Message}, {Repeat}, {Links}` |
| `Nvt.Console.List.Row.RepeatFormat` | `repeated {0} times` |
| `Nvt.Console.List.Row.LinkOneFormat` | `{0} link` |
| `Nvt.Console.List.Row.LinksFormat` | `{0} links` |
| `Nvt.Console.List.Level.Trace` | `Trace` |
| `Nvt.Console.List.Level.Debug` | `Debug` |
| `Nvt.Console.List.Level.Info` | `Info` |
| `Nvt.Console.List.Level.Warn` | `Warn` |
| `Nvt.Console.List.Level.Error` | `Error` |
| `Nvt.Console.List.Level.Fatal` | `Fatal` |

The source column and its tooltip display `ConsoleRow.SourceId`. `TimeOptions.SourceRegistry`
is ignored by the list; registry display names remain a projection or host concern.

The persistent item source borrows the current projection. A pixel-scrolling recycling host retains
realized containers by `ConsoleRowId`; projection application updates identities without resetting the source.
Only viewport rows and a small overscan are realized. Expanded heights begin as estimates and are updated
by measurement. Huge messages are read in bounded segments ending on wrapped-line boundaries.
The host stores compact offset/height indexes and retains text layouts only for visible segments;
it creates no text visual per line and never materializes more than the clipboard text bound.
Row width follows the logical viewport and horizontal scrolling is disabled.

Error/Fatal backgrounds use the existing danger surface. Message/source search hits use the existing
warning surface and strong warning text. Painting proceeds through search background, text, link decoration
and hover, then search foreground last. Hovered link text uses the app accent outside search hits.
Theme/resource changes invalidate the measured display cache.
Paused reading keeps the same first visible row and DIP inset while measured heights are rebuilt.

User scroll-away requests Pause with the first visible row, sequence, text offset and DIP offset.
Expansion reports one combined expansion/pause state; pointer movement beyond 4 DIP cancels activation.
The message column and arrow receive pointer activation across their hit areas.
Press captures the pointer for the gesture, so excursions outside the row cancel activation even after returning.
Capture loss clears the gesture; release, cancellation and detach release its capture.
While paused, a changed user scroll offset away from the end requests updated reading coordinates without
changing the pause time, generation, sequence boundary or frozen row order. Repeated notifications for the same
offset, viewport changes, resource invalidation and the list's own height corrections do not request state.
Only scroll-origin requests are deferred during measure. They coalesce as pending user intent; after the pass,
the request captures the current screen position and caller-owned state. Pending reading survives projection
updates, height corrections and width reflow; viewport clamping does not change the user's pause/resume intent.
Delivery finishes layout and restoration, then retires outstanding restore work before publishing the request,
so delayed host acceptance cannot let an earlier queued restore undo the published reading position.
A later toggle or `JumpToLatest()`
cancels that pending scroll request and is delivered immediately. Height corrections and width reflow preserve
a user scroll received during the pass instead of restoring its start-of-pass position.
Unrelated state changes that reuse the same Follow instance preserve the live reading position,
even when the caller still holds an earlier anchor. Issued anchors retain the most recent 32 requests; issuing Resume clears this history.
Accepting an anchor still in that history preserves live coordinates and retires only the accepted prefix,
so later queued requests can be accepted in order without replaying old positions.
A different Follow outside that history clears it, cancels pending scroll intent and restores the caller's explicit position.
Reattachment uses the accepted anchor.
While paused, projection application saves the current anchor and restores it after layout;
coalesced replacements and width changes reuse the pending anchor until restoration completes or a user scroll replaces it.
Programmatic scroll calls use nested suppression, and queued work still permits user scroll-away pause requests.
Width changes remap the text offset after reflow. An evicted anchor uses `ResolvedAnchorId` and reports the successor.
If the projection supplies none while Following and a scroll is pending, the live anchor retains its sequence and row order to resolve
the first surviving successor, falling back to sequence and then the last retained row;
a retention notice displays `EvictedCount`.
The bottom-right jump button displays `NewSincePauseCount`.
Reaching the end, activating the button, or calling `JumpToLatest()` requests Resume.
Attach creates handlers and cancellable coalesced dispatcher work; detach releases them, borrowed content and display caches.
Attachment lifetime disposal is idempotent. All host state is accessed on the UI thread.

The host and companion controls compose header/toolbar content, empty states and file export.
The list provides row links, selection, bounded clipboard copy, context menus and row keyboard routing.

### Row interaction

Set `ConsoleListView.Controller` to the app-owned controller that supplies its projection. Continue binding
`Projection` and `ViewState`, and route `ViewStateRequested` to `ConsoleController.RequestViewState`.
The list reports intents and never adopts its own requested state. `sources` is the authoritative constructor
registry; a nonempty `options.SourceRegistry` must match it in identity, display name and order or construction throws.
The header uses the existing `NvtIcons.DataObject` catalog glyph; console optical corrections live in LogConsole.

Selection lives only in `ConsoleViewState.Selection`, as canonical raw event identities. Single click replaces it,
Ctrl-click outside a link toggles a row, Shift-click or Shift-navigation selects an inclusive range, and Ctrl+A selects
all projected rows. A grouped row contributes all its retained members. Controller remapping preserves retained
selections through grouping, filtering and appends, and removes evicted identities. Copy enumerates selected rows
only in current projection order, so hidden retained selections are excluded. Focus and the range origin are
attachment-local raw identities; detach releases them without clearing host selection.

Inject `IConsoleLinkOpener` and `IConsoleClipboard` through the controller's init properties before binding.
Their UI-thread methods must return promptly: the app owns asynchronous resolution, cached availability,
TopLevel clipboard dispatch and opening. `GetUnavailableReason` returns null for an available target or a
user-facing reason, including pending resolution. Availability is sampled for hover, menu creation and activation;
the app must keep the query free of IO. Core never queries existence, resolves relative paths or opens a target.
The existing bounded link cache consumes structured spans first and otherwise scans segmented content lazily.
Every adopted snapshot synchronizes the cache, including an empty Clear snapshot; Dispose releases the cache.
Its producing snapshot and projection retire together; no complete large message is created for parsing.
Unavailable links remain decorated and show their full target and reason in the tooltip.
Ctrl-left press and release must hit the same target, with no excursion over the configured drag threshold.
Resting underlines use `NfcBorderSoftBrush`; hover uses `NfcAccentBrush`, and search-hit foreground paints last. Ctrl+Enter opens a unique link or offers a choice menu for multiple links.
File line/column and folder kind pass unchanged to the opener.

Row menus use `ConsoleMenuBuilder` and controller commands: copy message, row or source; expand/collapse;
filter by this source or level; and open link (with a submenu for multiple links). Disabled entries show a reason
through the existing tooltip service, including while disabled. Commands retain identities and revalidate the
row, generation, target and adapter against a fresh snapshot and current filter/selection before executing,
without waiting for display refresh. A single controller state version fences every copy and open operation,
including projection reads, formatting and adapter availability callbacks. Changes to filter, search, selection,
reading state, export options or history cancel delivery, even if a callback restores the original inputs.
Selection copy rechecks delivered identities against current visibility and selection after formatting.
Detach releases row-menu delegates and subscribers; Dispose also releases
the copy-selection command. Menu labels use `Nvt.Console.List.Menu.*` English defaults
and the same host resource override/fallback path as other list text.

Clipboard paths share one bounded builder. At most 65,536 UTF-16 characters are sent, with messages read in
bounded chunks; row copy matches the existing export prefix, original newlines and dedupe suffix until the cap.
`ConsoleController.CopySelectionCommand` can also supply the header's copy-selected command.
The host owns failures of its queued platform operations.

The list has one Tab entry. Up/Down, PageUp/PageDown and Home/End move row focus; Shift extends selection.
Enter/Space toggles expansion, Ctrl+C copies, Escape closes the row menu before clearing selection,
and Ctrl+End requests Resume. Shift+F10 and Menu open the focused row menu. Keys route through commands
and do not intercept TextBox editing or toolbar shortcuts. Realized rows use Core's keyboard-only Focus B
adorner and stay out of the Tab sequence. Recycled focus falls back to the list entry.
Peer creation is lazy: a peer exists only after assistive technology asks for it. The automation tree
contains a List peer and the ListItem peers of realized rows, including the host's realization buffer,
followed by the visible template Jump to latest button and retention notice. Hidden template parts are
omitted. Unrealized rows and the row's painting controls are not automation children. `GetChildren`
builds the list on each call, without a child cache. Realization changes and a visibility change of the
template parts invalidate children at most once per completed layout pass, only when the view already has
a peer. The list supports multiple, optional selection and vertical scrolling; rows support selection, and
expandable rows also expose expand/collapse. A grouped row is selected when any retained member is selected. Selection
and expansion actions share the pointer/keyboard command path and request caller-owned state through
`ViewStateRequested` and `RequestViewState`. Pattern expansion changes only `ExpandedIds`, preserving
selection, reading anchor, following and keyboard focus. Pattern AddToSelection and RemoveFromSelection
change selection without moving focus or its range origin. Pattern Select replaces the selection with
the row and focuses it, the same selection result as a pointer click. A selection change from a pattern
pauses following like any other selection change. Enter and Space keep their existing selection/expansion
behavior.

`Nvt.Console.List.Name` names the list. `Nvt.Console.List.Row.NameFormat` orders the named fragments
`{Level}`, `{Time}`, `{Source}`, `{Message}`, `{Repeat}` and `{Links}`. Its default order is level, time,
source, message, repeat and links. Translations can reorder fragments and change their separators.
The grammar is case-sensitive named fragments plus literal text. Each literal run before a fragment,
including a leading run or a run between fragments, belongs to the fragment immediately to its right:
keep the run exactly when that fragment is nonempty, even if no earlier fragment was emitted; otherwise
omit both. A trailing literal run belongs to the final template fragment and is kept only when that
fragment is nonempty; an earlier present fragment does not retain an absent final fragment's suffix.
Thus `{Level}, {Time}, {Source}` with hidden time yields `Info, app`, and `{Level} ({Time})` yields
`Info` with hidden time or `Info (12:00:00.000)` with present time. A template consisting entirely of
literal text is kept. `{{` emits `{` and `}}` emits `}` in literal text. Unknown fragment names and
unbalanced single braces make the entire template fall back to the built-in English template.
A translation should not put paired punctuation around a fragment that can be absent: `({Time}) {Level}`
with hidden time yields `) Info`.
Level labels use `Nvt.Console.List.Level.*`,
repeat uses `Nvt.Console.List.Row.RepeatFormat`, and link counts use `Nvt.Console.List.Row.LinkOneFormat`
or `Nvt.Console.List.Row.LinksFormat`. List text uses the host resource override and English fallback path;
invalid name templates fall back to the built-in named template. Hidden time, a single occurrence and
absent links omit their fragments. Selection and expansion are exposed by patterns rather than repeated
in the name.

The painting layout publishes plain bounded text and its truncation result. Collapsed rows use the
painter's bounded first-line preview; expanded rows collect text from the existing paint scan under the
clipboard text bound. A row that has never been laid out omits the message fragment and has no expand
pattern until presentation. Rebinding to an equal row (same identifier, text version, length and source)
keeps the published values. Rebinding to another row or toggling expansion drops them, and the peer
reports the message-free name until the next layout publishes them again. Releasing the presenter drops
them as well. Pointer hit testing returns no link while no values are published.
Published link metadata supplies the link count. Peer property and pattern reads perform no retained
content reads, so they take no content-owner lock, and never call a controller content reader or app adapter,
capture a projection, wait, or declare a live region.
Following can add rows frequently, so announcing each row would obscure deliberate navigation.
Reads use the UI thread and only the published `Projection` and `ViewState`. The host passes the row's
projection index when binding a presenter; position and set size use direct indexed reads. Recycled
presenters update their peer input and notify only changed name, selection and expansion values.
Position and set-size automation properties are initialized with the peer and updated on input changes
only while that presenter has a peer. Release clears the metadata; ordinary realization and recycling
without peers perform no automation property writes. Text notifications are sent after the painting
measure publishes its text, within the presenter's measure pass.
Detached views and disposed controllers produce neutral peer values. Lifecycle checks observe the
controller's existing disposal flag with a volatile read, without entering its scheduling lock.

Focus B uses the existing inset focus-ring margin so viewport-edge rings remain within the row.
Layout uses Avalonia's render-scaling seam: row bounds follow layout rounding, while the focus stroke
retains its DIP thickness. A fractional device-pixel stroke is antialiased; its outward raster envelope
rounds up. The dense row's expand target covers the full reserved arrow column, including its centered
glyph's side spaces: 24 DIP wide and 20 DIP high. The 20 DIP height is the row height approved with the
Console geometry. Existing interactive controls use `NfcControlHeight` (32 DIP), so this target is 12 DIP
shorter. Its height is also below 24 DIP. Core has no written minimum for it, so this is an explicit
recorded exception. Enter on the focused row and the row peer's expand pattern both change
expansion (their selection/focus effects differ as described above), so no function depends on
hitting the arrow. Raising the height to 24 DIP changes the row height of the whole console and needs its
own approved row-geometry change.
Headless peer tests do not establish native UIA/AT-SPI or Narrator/NVDA output.

Reading-only state changes reuse layout inputs. Realized rows are configured on input/resource changes and
first realization rather than each measure pass. Height rebuilding reads common geometry once. Row order is
cached once per projection and omitted from paused coordinate captures; a new projection invalidates that cache.
A measure-time Following scroll to the end discards any pending measure position. Deferred delivery clears its
pending intent even when layout/restoration throws, and Resume retires unaccepted pause history.
