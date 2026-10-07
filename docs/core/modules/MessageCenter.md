[繁體中文](MessageCenter.zh-TW.md)

# MessageCenter

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MessageCenterViewModel.cs`: activity filter, row severity flags and accessible text, passive counts, initial modal state, export-context predicate, generation/commit order in `Open`, `Close`, and `SelectSystemInformation`, and generation checks and exception/callback scope in `ExportAsync`. Refresh, bundle capture, activity recording, status text, report composition, and product formatting remain in NFC.
- `src/NvtFwCombiner.Presentation.Avalonia/Views/MessageCenterModal.axaml.cs`: `ExportWithPickerAsync` acceptance, cancellation, and failure predicates. The actual storage picker, local-path conversion and its failure message, default filename, layout, focus, and report composition remain in NFC.
- `src/NvtFwCombiner.Application/Diagnostics/SystemInformationModels.cs`: importance/severity enums and sequence/disclosure/severity metadata from immutable `SystemActivityEntry`. Categories, codes, drafts, diagnostics, snapshots, and bundles remain in NFC.
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.MessageCenter.Startup.cs`: the disclosure and host-text reprojection assertions in `ActivityHistoryUsesTwoDisclosureLevels`.
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellScreenInventoryTests.cs`: row selection/order from `SystemActivityContentFitsAndFilters` and passive history preservation from `SystemActivitySelectionsPreserveNavigationAndHistory`. Layout, rendering, navigation, and focus assertions remain in NFC.
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.MessageCenter.cs`: session and synthetic workflow assertions from `DiagnosticsPickerRejectsClosedAndReopenedContext`, `DiagnosticsPickerFailureIsVisibleAndRetryable`, and `DiagnosticsExportCompletionRejectsReopenedContext`, plus pane selection from `MessageCenterKeepsSystemLifecycleSeparateFromRunReports`. Actual picker, bundle, diagnostics, and report assertions remain in NFC.
- `tests/NvtFwCombiner.UiSmoke.Tests/DiagnosticsExportFailureGuidanceTests.cs`: generic failure/success callback observations. Localized guidance, activity metadata, status reset, layout, and visibility assertions remain in NFC.

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.MessageCenter` supplies a passive display contract, modal session, and export workflow in `Nvt.Core`, targeting `net8.0` with BCL dependencies only. The host supplies admitted activity entries, display strings, counts, view identity, export I/O, and status callbacks.

## Public API

| Type | Contract |
| --- | --- |
| `MessageActivityImportance` | `Important`, `Debug`. |
| `MessageActivitySeverity` | `Information`, `Success`, `Warning`, `Error`. |
| `MessageActivityFilter` | `Important`, `Warnings`, `Errors`. |
| `MessageCenterActivityItem` | Nonsealed row record: `Time`, `Title`, `Detail`, `Category`, `Status`, `Severity`; four severity flags and `AccessibleText`. |
| `MessageCenterActivity` | Sealed metadata record: `long Sequence`, importance, severity, and `Func<MessageCenterActivityItem> ProjectItem`. |
| `IMessageCenterProvider` | Passive `ActiveDiagnosticCount`, unfiltered `ActivityCount`, and `IReadOnlyList<MessageCenterActivity> CaptureActivity()`. |
| `MessageCenterActivityFilter` | Static `Apply(IEnumerable<MessageCenterActivity> activities, MessageActivityFilter filter, bool includeDebug)` returns materialized rows. |
| `MessageCenterSession` | Read-only `IsOpen`, `IsActivitySelected`, `ExportContextGeneration`; `Open(Action? beforeOpen = null)`, `Close(Action? beforeClose = null)`, `SelectActivity(bool selected, Action? beforeSelect = null)`, and `IsExportContextCurrent(long generation)`. |
| `MessageCenterExportWorkflow` | Constructor `(MessageCenterSession session, Func<string, CancellationToken, Task> export, Action succeeded, Action failed)`; `Task ExportAsync(string destinationPath, CancellationToken cancellationToken)`; `Task ExportWithPickerAsync(Func<Task<string?>> pickPathAsync, Func<bool> isViewContextCurrent)`. |

The implementation is in [MessageCenterActivity.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterActivity.cs), [IMessageCenterProvider.cs](../../../src/Nvt.Core/MessageCenter/IMessageCenterProvider.cs), [MessageCenterActivityFilter.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterActivityFilter.cs), [MessageCenterSession.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterSession.cs), and [MessageCenterExportWorkflow.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterExportWorkflow.cs).

## Preserved display behavior

Each severity flag compares the row's severity to its corresponding enum value. The complete accessible string is exactly:

```csharp
$"{Time}. {Title}. {Category}. {Detail}. {Status}."
```

Empty fields retain their punctuation, and strings are neither normalized nor localized by Core. The row remains nonsealed for NFC's typed XAML alias; that alias adds no display behavior.

The provider contract requires an ordered snapshot of already-admitted metadata over immutable host entries. Capture and both counts require no row projection. Each delegate may use the host's current display text when invoked, allowing reprojection without changing the captured entry. Metadata and projected row severity must agree; this is a host invariant, with no new Core validation exception.

`Apply` preserves this sequence:

1. Disclose an entry when `includeDebug` is true or its importance is `Important`.
2. Apply the severity filter. `Important` includes every disclosed severity; `Warnings` matches exactly `Warning`; `Errors` matches exactly `Error`.
3. Sort retained metadata by descending sequence, preserving input order for equal sequences.
4. Invoke `ProjectItem` exactly once per retained row, in that order, and materialize the result.

Hidden rows are never projected. Source enumeration completes before projection through the sorting stage. Enumeration and projection faults propagate unchanged. An undefined filter throws the original parameterless `ArgumentOutOfRangeException` only when evaluated for a disclosed entry; an empty or wholly undisclosed source does not evaluate it. A null source retains LINQ's `ArgumentNullException` with parameter name `source`. Undefined importance and severity values follow the same predicates without extra validation.

A materialized result is independent of subsequent source-list changes. Core does not cache projections between calls. This passive projection is separate from ReportList materialization, Reset publication, and paging.

## Preserved session behavior

A new session is closed, has activity selected, and has generation zero.

| Operation | Order and committed state |
| --- | --- |
| `Open` | Always checked-increment generation, invoke `beforeOpen`, then commit `IsOpen = true`, including repeated opens. |
| `Close` | Always checked-increment generation, invoke `beforeClose`, then commit `IsOpen = false`, including repeated closes. |
| `SelectActivity` | Return immediately for the current pane. Otherwise checked-increment generation, invoke `beforeSelect`, then commit selection. Visibility is preserved. |
| `IsExportContextCurrent` | Require equal generation, then open state, then activity selection. |

Callbacks see the advanced generation and the old committed visibility and selection. Selection preserves the Toolkit property-changing timing through this pre-commit hook. A throwing callback propagates its exception, leaves the operation's state uncommitted, and retains the generation advance. Open and close preserve pane selection.

The fixed mechanism bound is `long.MaxValue`: attempting another advance throws `OverflowException` before any callback or state commit and leaves the generation unchanged. Selecting the current pane remains a no-op at that bound. Overflow tests use an internal constructor; no public generation setter is exposed. The session invokes callbacks synchronously and adds no dispatcher or synchronization mechanism.

## Preserved export workflow behavior

The export delegate is the sole I/O operation. Direct export captures `ExportContextGeneration` before invoking the delegate and passes the destination and cancellation token unchanged. It performs no path validation, normalization, visibility check, or pane check. After the delegate finishes, equal generation permits the success callback. Only `IOException`, `UnauthorizedAccessException`, and `ArgumentException` (including derived exceptions) enter the expected-failure catch; equal generation permits the failure callback. Generation mismatch suppresses either publication without interrupting the write.

The success callback remains inside that catch scope: its expected exception can invoke failure, with generation checked again. The failure callback runs inside the catch body and its faults propagate without another catch. Unexpected exporter/success faults and cancellation propagate, including for stale generations. Concurrent exports with the same generation do not supersede one another; each may publish in completion order.

Picker handling preserves this order:

1. Validate `pickPathAsync`, then the required identity delegate `isViewContextCurrent`. A closed session returns without calling either delegate. An open session on another pane still starts a picker.
2. Capture generation before invoking and awaiting the picker.
3. Return silently for `OperationCanceledException`. For any other picker exception, check view identity first, then `IsExportContextCurrent(capturedGeneration)`, and publish failure only if both pass.
4. For a result, require a non-null, non-whitespace path first, then view identity, then the same open activity generation. Accepted paths pass unchanged to direct export with `CancellationToken.None`.

The host identity delegate compares the current view context to the context captured before the workflow call, preserving DataContext-replacement rejection. Identity and failure callback exceptions are outside the picker try body and propagate. Null, blank, and canceled selections preserve the old host status without invoking a status callback. The workflow adds null guards for its new required constructor and identity inputs; the picker guard retains the original `pickPathAsync` parameter name.

Close/reopen, repeated open, and actual pane changes invalidate the captured generation. Same-pane selection does not. Host language changes and refresh do not advance generation. After picker acceptance, write completion uses generation alone, so later view replacement by itself does not suppress publication. Closing a session never cancels or deletes an already running write. Await continuations retain the caller's context; hosts keep session operations and callbacks on their existing serialized context. Core adds no dispatcher, locking, shutdown timeout, dispose API, or cancellation policy.

## Ownership and consumer contract

Core owns the row contract, passive filter/projection order, modal generation mechanism, and picker/export publication workflow. NFC retains message sources, history, diagnostic transition registration, path-token validation, vocabulary, localization/time formatting, report history, refresh policy, storage pickers, export bytes/schema/paths, and its composition template. NFC's frozen **128-entry activity ceiling** stays in NFC. The display and export contracts have no Core history, path, or size ceiling and no positive limit parameter; the session's fixed generation bound remains `long.MaxValue`.

NFC captures its current diagnostics bundle inside the export delegate at invocation and calls its existing `ISystemDiagnosticsExporter`. The exporter, JSON schema and serializer context, privacy allowlist, filename, directory rules, atomic write and cleanup behavior, actual `StorageProvider`, and picker wording stay in NFC. Core writes no file and creates no directory; this extraction does not route diagnostics export through `AtomicOutput` or create report history.

NFC downloads verified versioned packages at build time through `core-packages.json` and uses exact `[x]` package versions, locked restore, and source mapping restricted to the download folder. The manifest records each package's Release tag and SHA-256. Consumer adoption is a separate change: retarget the extracted filter, row, session, and export workflow bodies, then delete their local executable copies only after complete value/event-trace and exported-byte parity, retained JSON privacy assertions, and identical decoded UI pixels pass under the same recorded environment. Capture view identity before picker delegation and keep the narrow typed alias and all retained product owners. Core tests alone do not establish NFC visual or product parity.

## Source-to-Core test map

The suites use xunit.v3 and synthetic in-memory values in [DisplayContractTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/DisplayContractTests.cs), [SessionTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/SessionTests.cs), and [ExportWorkflowTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/ExportWorkflowTests.cs). Workflow tests use no filesystem or actual picker and assert complete destination/token logs and ordered picker, identity, exporter, and status callback traces.

| Frozen evidence | Core tests and preserved assertions |
| --- | --- |
| `ActivityHistoryUsesTwoDisclosureLevels` | `ActivityHistoryUsesTwoDisclosureLevelsAndReprojectsHostText`: debug hidden by default, important success retained, expansion reveals debug, and a second visibly distinct text set reprojects every field. `BadgeAndSummaryCountsAndCaptureNeedNoRowProjection` preserves passive counts. |
| `SystemActivityContentFitsAndFilters` | `InventoryFiltersPreserveRowsAndHostHistory`: the same warning/error selection and descending debug/important order, warning/error row flags, unchanged host entries after pane changes, and the original 110-character synthetic detail examples. `FiltersProjectOnlyRetainedRowsInExactOrder` covers all six filter/disclosure combinations and callback traces. |
| `SystemActivitySelectionsPreserveNavigationAndHistory` | Empty-filter, disclosure, capture-mutation, and pane-selection tests preserve the passive subset. Product navigation, IC selection, report history, and rendering remain NFC assertions. |
| `DiagnosticsPickerRejectsClosedAndReopenedContext` | `ExportWorkflowTests.DiagnosticsPickerRejectsClosedAndReopenedContext`: the old picker produces no export or status, then the current picker exports and publishes success with `CancellationToken.None`. `SessionTests.ClosedAndReopenedContextRejectsOldGeneration` preserves the underlying predicate. |
| `DiagnosticsPickerFailureIsVisibleAndRetryable` | The same-named workflow test preserves failure publication and unchanged failure after a null retry, and adds a successful retry. `CanceledPickerPreservesOldFailure` covers both synchronous cancellation and canceled tasks. |
| `DiagnosticsExportCompletionRejectsReopenedContext` | The same-named workflow test preserves actual synthetic write completion with empty status after reopen, using deterministic gates and the source's ten-second wait threshold. `SessionTests.DelayedObservationRejectsReopenedContext` preserves delayed predicate observation. |
| `DiagnosticsExportFailureGuidanceTests` callback observations | `ExpectedDirectFaultPublishesFailure`, `DirectCompletionRequiresOnlyUnchangedGeneration`, and the retry regression cover generic failure/success publication. Exact localized messages, activity metadata, layout, and reset assertions stay in NFC adoption tests. |
| `MessageCenterKeepsSystemLifecycleSeparateFromRunReports` | `OpenAndClosePreserveTheSelectedPane`, `PaneChangesPreservePropertyChangingTiming`, and current-context tests cover modal/pane behavior. Diagnostic transitions, Build blockers, and report lifecycle remain NFC assertions. |

Additional characterization covers complete accessible strings including empty fields, a behavior-free binding alias, shuffled sequences and stable ties at signed `long` boundaries, zero hidden projections, projection/enumeration fault order, undefined enum values and deferred invalid-filter checks, null inputs, repeated visibility operations, same-pane no-ops, pre-commit state observation, and callback failures. Generation boundary tests cover `long.MaxValue - 1`, `long.MaxValue`, an attempted increment above it, failure at the maximum, and same-pane no-ops at the maximum for both panes. No native process, handle, or Job behavior is part of this module.

Export characterization additionally covers all expected exception types and derived exceptions, synchronous versus asynchronous faults, cancellation propagation, closed starts, empty/Unicode-whitespace and edge-character destinations, other-pane starts, pane round trips, replaced view identity, stale picker/write failures, generation capture before delegate invocation, identity-before-session ordering, callback fault scope, concurrent completion order, and an accepted write continuing after close with an uncanceled `None` token. Direct exports consume zero, negative, and signed generation extremes without advancing; tests cover `long.MaxValue - 1` and `long.MaxValue`, plus picker acceptance at the maximum. The existing session tests cover the attempt above that fixed bound. No additional workflow limit is introduced.
