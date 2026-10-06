[English](Progress.md) | [繁體中文](Progress.zh-TW.md)

# Progress

## Summary

Progress supplies validated fraction data, the single-active background job service from NVT FW UTIL (NFU), and the progress interval gate from Freeform Helper (NFH).
The module uses only the .NET base class libraries and targets net8.0.
Its namespace is `Nvt.Core.Progress`.

Each tool keeps its update rate, progress payloads, result payloads, and presentation policy.
The owner approved this boundary on 2026-10-06.
The module has no nested progress, queue, scheduler, replacement mode, shutdown framework, timer, trailing report, or UI code.

## Frozen baselines

These commits freeze the extraction sources.
The sources were read with `git show <sha>:<path>`.

| Tool | Repository | Ref | Commit |
| --- | --- | --- | --- |
| NVT FW UTIL (NFU) | `nvt-event-buffer-replay` | `origin/0.2.0` | `26d66bd377a4ad051392bd7cc7e9d1c2e6287dba` |
| Freeform Helper (NFH) | `nvt-freeform-helper` | `origin/1.3.x` | `4df72911867ad047b3217195d12223038a5781b7` |
| NVT FW Combiner (NFC) | `nvt_fw_combiner` | `origin/1.2.x` | `a67eaee35b1d7eda9157a82e880e98a70407e913` |

NFU extraction and context paths:

- Engine: `src/Nvt.Replay.Rendering/ExportJobService.cs`, the whole file.
- All seven source tests: `tests/Nvt.Replay.Tests/ExportJobServiceTests.cs`.
- UI context only: `src/Nvt.Replay.Avalonia/MainWindow.Output.cs`, lines 35, 518, and 600–638.

NFU's engine file is identical at `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b`.
The extraction uses the frozen commit in the table.

NFC supplies only the fraction rule from `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ForegroundLoadingState.cs`.
Its `ValidateProgress` method defines the rule.

NFH supplies the interval gate in `src/FreeformHelper.UI/ViewModels/FreeformHelperViewModel.Persistence.NotchExport.Helpers.cs`.
The methods are `CreateNotchGenerationProgressReporter` and `CreateStep5GenerationProgressReporter`.

Both reporters check validity before applying a 120 ms interval gate.
NFH derives `isFinal` from its own payload after clamping the processed count and normalizing the total count.
Final reports always pass and reset the interval.
Dropped reports do not change the last forwarded time.
The source starts `lastTick` at zero, so the first report passes in normal operation.
Core explicitly forwards the first report, including when a test clock starts at zero.

The frozen tests contain no direct tests of either reporter's interval gate.
Related caller tests provide final-delivery and stale-result assertions:

- `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.NotchExportCache.cs` checks that finalization progress reaches the caller.
- `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.NotchGenerationStaleCompletion.cs` checks final-phase handling and stale-result rejection.

Core adapts these generic assertions to synthetic final values and an external validity check.
NFH retains the domain assertions, progress text, revision checks, and UI dispatch.

## Progress data

[`ProgressUpdate`](../../../src/Nvt.Core/Progress/ProgressUpdate.cs) is a public readonly record struct.
It stores `double? Fraction` and `string StepText`.
`IsIndeterminate` is true exactly when `Fraction` is null.

NFC validates the fraction and does not clamp it.
Core uses the same predicate: `fraction is < 0 or > 1 || double.IsNaN(fraction ?? 0)`.

| Input | Behavior |
| --- | --- |
| `null` | Accept. Progress is unknown. |
| Zero through one | Accept and retain the supplied value. |
| Negative zero | Accept and retain its sign bit. |
| Below zero or above one | Throw `ArgumentOutOfRangeException`. |
| NaN or either infinity | Throw `ArgumentOutOfRangeException`. |

The exception message starts with `Progress must be between 0 and 1.`.
Construction, object initialization, and record copies use the same rule.
Core retains the supplied step text without validation.

[`ProgressUpdateTests`](../../../tests/Nvt.Core.Tests/Progress/ProgressUpdateTests.cs) covers null, zero, one, intermediate fractions, and rejected values.
It also covers negative zero, record copies, and unchanged step text.

## Background jobs

[`BackgroundJobService<TProgress, TResult>`](../../../src/Nvt.Core/Progress/BackgroundJobService.cs) owns one active operation.
Both type parameters require reference types.
A tool can keep its existing progress and result classes.
`ProgressUpdate` is independent value data; it is not a valid direct service type argument.

[`BackgroundJobSnapshot<TProgress, TResult>`](../../../src/Nvt.Core/Progress/BackgroundJobSnapshot.cs) stores the job ID, status, progress, result, and error.
[`BackgroundJobHandle<TProgress, TResult>`](../../../src/Nvt.Core/Progress/BackgroundJobHandle.cs) exposes the ID and completion task.

| Status | Meaning | `IsActive` |
| --- | --- | --- |
| `Idle` | No job has started. Its ID is zero. | False |
| `Running` | The operation is active. | True |
| `Cancelling` | Cancellation was requested. The operation remains active. | True |
| `Succeeded` | The operation returned without cancellation. | False |
| `Cancelled` | The operation finished after a cancellation request. | False |
| `Failed` | The operation threw without a cancellation request. | False |

`Start` rejects a null operation before admission.
It throws `InvalidOperationException` while a job is active.
Each admitted job gets the next checked ID, starting at one.
Rejected starts do not consume an ID.

`Start` stores the supplied initial progress, which defaults to null.
It reports the initial running snapshot before it starts the operation with `Task.Run`.
The operation receives the job token and an inline progress reporter.

`Cancel` changes the status to `Cancelling` and records the request before it reports the snapshot or calls cancellation callbacks.
It returns false when no job is active or cancellation was already requested.
It returns true for the first request when callbacks finish normally.
A throwing cancellation callback causes `Cancel` to throw `AggregateException`; the request remains in effect.

The operation must finish before the service clears its active job.
Cancellation suppresses both returned results and operation errors.
An `OperationCanceledException` becomes a failure unless the job's token has been cancelled.
The exception's own token does not control this decision.

Progress remains accepted during `Cancelling`.
Terminal snapshots retain the latest progress.
Reports from completed jobs cannot change state or notify their old observers.

The service calls snapshot observers and cancellation callbacks outside its lock.
Observer exceptions do not change the job outcome.
Observers can read state, request cancellation, and start another job after a terminal publication.
The completion task finishes after the terminal observer returns.

The service preserves NFU's cancellation-source disposal coordination.
Completion can finish while a cancellation callback runs, or before token cancellation starts.
A completed job cannot dispose the source while its pending `Cancel` call still needs it.

### Zero-difference verification for background jobs

[`BackgroundJobServiceTests`](../../../tests/Nvt.Core.Tests/Progress/BackgroundJobServiceTests.cs) ports all seven NFU source tests.
Small synthetic reference types replace the replay payloads.

The port preserves these source scenarios:

- Background execution, progress reporting, and rejection of a second active job.
- Idempotent cancellation and completion of a cooperative job.
- Cancellation callbacks that read state outside the service lock.
- Suppression of a result returned after cancellation.
- Rejection of late progress from an old job.
- Observable failure and recovery with the next job.
- Observer exception isolation.

Characterization adds controlled cancellation and observer schedules:

- A throwing cancellation callback, with completion before and after the callback exits.
- Cancellation from an unrelated token, with and without a job cancellation request.
- Progress during `Cancelling` and rejection after completion.
- Observer reads from another thread, reentrant cancellation, and admission from a terminal observer.
- Completion before token cancellation and suppression of a later operation failure.
- Exact initial, progress, and terminal snapshots, job IDs, null values, and observer order.

Tests use `TaskCompletionSource` gates.
They do not use `Thread.Sleep`, `Task.Delay`, or scheduling guesses.
The service reads no clock.
The same gate sequence therefore gives exact snapshots under a fake clock without adding a clock API.

A source comparison confirmed that the engine body matches frozen NFU after the changes listed below.
The comparison excludes comments and whitespace.
The two frozen NFU engine versions also matched exactly.

Run Core checks with existing restored packages:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
```

Verification on 2026-10-06 passed with zero build warnings and errors.
All 149 Core tests passed, including 55 Progress cases.
No tests failed or were skipped.

For NFU adoption, run these suites before and after switching its wrapper to Core:

- `ExportJobServiceTests` and `ReplayExportTests` in `tests/Nvt.Replay.Tests/`.
- `AdvancedWorkspaceSnapshotTests`, including `Output_export_progress_uses_the_info_rail_without_reducing_preview_controls`.
- `MainWindowLayoutTests` and the full UI snapshot suite in `tests/Nvt.Replay.Avalonia.Tests/`.

In each NFU checkout, use its existing restored packages:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-restore
dotnet build tests/Nvt.Replay.Avalonia.Tests/Nvt.Replay.Avalonia.Tests.csproj --no-restore
dotnet test tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-build --filter "FullyQualifiedName~ExportJobServiceTests|FullyQualifiedName~ReplayExportTests"
dotnet test tests/Nvt.Replay.Avalonia.Tests/Nvt.Replay.Avalonia.Tests.csproj --no-build
```

Use the same gated operations and observer transport for both versions.
Supply NFU's existing initial progress through the Core parameter.
Compare the following evidence:

- Initial, progress, cancelling, and terminal snapshots.
- Job IDs, rejected starts, and cancellation return values.
- Result values, result identity, error types, error identity, and suppressed outcomes.
- Observer call order, accepted progress during cancellation, and ignored late reports.
- Export output, manifest content, cancellation cleanup, and the existing UI snapshots.

Keep the same OS, runtime, fonts, DPI, theme, and snapshot inputs for UI comparisons.
Preserve NFU's update rate, observer dispatch, status text, and stale-snapshot checks.
Do not refresh approved snapshots to accept a difference.

These NFU adoption checks remain for the adopting tool.
This task did not modify NFU or NFC and did not run their export or UI suites.

### Known differences

Core changes the namespace and job type names.
It replaces replay payload types with constrained generic reference types.
It adds the required copyright headers and public API documentation.

Core takes initial progress as a parameter instead of creating NFU's export-specific initial value.
NFU keeps `StartReplayExport`, its replay payloads, `Preparing export`, and all export error text.
Core's active-job message is `A background job is already active.`.
NFU can retain its original message in its wrapper.

Core adds the requested fraction record rather than extracting NFC's foreground UI state.
The fraction exception parameter is `fraction`; NFC names it `progress`.
The predicate, rejection behavior, and message text stay the same.
NFC's title and detail validation remain outside Core.

Core test names use PascalCase to satisfy repository analyzers.
Completion gates replace NFU's infinite-delay waits and remove timing races from the ported assertions.
The extraction introduces no further job lifecycle behavior.

## Throttled progress

`ThrottledProgress<T>` implements `IProgress<T>` in `src/Nvt.Core/Progress/ThrottledProgress.cs`.

```csharp
public ThrottledProgress(
    IProgress<T> target,
    TimeSpan minimumInterval,
    Func<T, bool> bypass,
    TimeProvider? timeProvider = null);
```

`Report` forwards a value when one of these conditions holds:

- `bypass(value)` returns true.
- No report has passed yet.
- At least `minimumInterval` has elapsed since the last forwarded report.

Every forwarded report updates the timestamp, including a bypassing report.
A dropped report is lost.
The class creates no timer, trailing report, or queue.
A zero interval forwards every report.
Null `target` or `bypass` arguments throw `ArgumentNullException`.
A negative interval throws `ArgumentOutOfRangeException`.

The default clock is `TimeProvider.System`.
The gate measures time with `GetTimestamp()` and `GetElapsedTime()`.
A lock protects the gate and its timestamp.
The target runs outside that lock.
Target calls can overlap, so the target controls its own synchronization and dispatch.

### Verification

Run these commands after packages have been restored:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Progress.ThrottledProgressTests"
```

The test file is `tests/Nvt.Core.Tests/Progress/ThrottledProgressTests.cs`.
Its manual `TimeProvider` requires no package.
Tests check first delivery, the exact 119/120 ms boundary, timestamp precision, final delivery, interval resets, dropped values, and zero intervals.
Concurrent tests check the forwarding count and prove that a blocked target does not hold the gate lock.
Timeouts limit concurrency-test failures; the manual clock determines every interval decision.
Argument tests check exception types and parameter names.

### Zero-difference verification for NFH

Keep the existing validity check around calls to `ThrottledProgress`.
Reject invalid reports before they reach the throttle, including final reports.
Use `TimeSpan.FromMilliseconds(120)` and pass NFH's existing `isFinal` calculation as `bypass`.
Keep the existing payload, UI dispatch, and target callback.

Compare both frozen reporter gates with Core under the same fake-clock timeline.
Start the source clock above 120 ms to reproduce its normal first-report behavior.
Compare the exact forwarded payload sequence and the time of each forwarded report.
Include first reports, 119 ms drops, exact 120 ms passes, final reports, invalid reports, and reports after each final report.
Confirm that dropped and invalid reports never change the next passing boundary.
Also run the Core tests above.

Before and after adoption, run NFH's existing caller tests without changing their expected results:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build FreeformHelper.sln --no-restore
dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.FreeformHelperViewModelTests"
```

This filter includes the reviewed cache and stale-completion tests.
Compare finalization delivery, stale-result rejection, progress text, dialog counts, and output assertions.
Adoption in NFH and the other tools remains outside this extraction.

### Known clock difference

The source uses `Environment.TickCount64`, which has millisecond resolution.
Core uses `TimeProvider` timestamps.
On a real machine, a report near the 120 ms boundary can pass or drop differently.
Use exact fake-clock timelines for the zero-difference comparison.
