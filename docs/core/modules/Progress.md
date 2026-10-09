[English](Progress.md) | [繁體中文](Progress.zh-TW.md)

# Progress

## Breaking changes before 0.9.0

`ProgressUpdate.StepText` is now `string?`.
Its positional constructor text parameter and `Deconstruct` text output carry the same annotation.
The uninitialized default has no fraction or step text.
It remains indeterminate.
Supplied text and valid fraction behavior remain unchanged.

Guard absent text before calling string APIs.
For presentation, choose the tool's existing fallback explicitly, such as `update.StepText ?? string.Empty`.
Core adds no text validation or fallback policy.
Tests cover default progress, explicit null text, and retained text and fraction rules.

## Summary

Progress supplies validated fraction data, the single-active background job service from NVT FW UTIL (NFU), and two Freeform Helper (NFH) parts: the progress interval gate and the loading scope coordinator.
The module uses only the .NET base class libraries and targets net10.0.
Its namespace is `Nvt.Core.Progress`.

Each tool keeps its update rate, progress payloads, result payloads, and presentation policy.
The owner approved this boundary on 2026-10-06.
The loading scope waits for its minimum visible time with `Task.Delay` on its `TimeProvider`.
Apart from that wait, the module has no nested progress, queue, scheduler, replacement mode, shutdown framework, timer, trailing report, or UI code.

## Internal cancellation state

The service derives `CancelRequested` from the current status under `gate`.
Completion captures that value before publishing a terminal snapshot.
The captured value preserves result suppression and defers source disposal until the cancellation call finishes.
The lock also protects job identity, active ownership, and completion flags.

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

NFH also supplies the loading scope coordinator in `src/FreeformHelper.UI/Services/LoadingScopeCoordinator.cs`.
Its two source tests are in `tests/FreeformHelper.Tests/UI/Services/LoadingScopeCoordinatorTests.cs`.
The callers, for context only, are `src/FreeformHelper.UI/ViewModels/FreeformHelperViewModel.CadLoadOverlay.cs` and `FreeformHelperViewModel.ModalLoading.cs`.
Both callers use a 120 ms minimum visible time.

## Progress data

[`ProgressUpdate`](../../../src/Nvt.Core/Progress/ProgressUpdate.cs) is a public readonly record struct.
It stores `double? Fraction` and `string? StepText`.
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

## Loading scope

[`LoadingScopeCoordinator`](../../../src/Nvt.Core/Progress/LoadingScopeCoordinator.cs) decides when a loading surface, such as an overlay or a spinner, shows and hides.
It holds no Avalonia, dispatcher, or NFH types.
A surface plugs in through a callback.
For an Avalonia control, pass `visible => surface.IsVisible = visible`.

```csharp
public LoadingScopeCoordinator(
    Func<Task> yieldFrameAsync,
    TimeSpan minimumVisibleDuration,
    TimeProvider? timeProvider = null);
```

- `yieldFrameAsync` yields to the UI so that a visibility change can render.
- A null `yieldFrameAsync` throws `ArgumentNullException`.
- A negative `minimumVisibleDuration` counts as zero.
- `timeProvider` supplies the clock and the wait timer, and null means `TimeProvider.System`.

`Begin(setVisible)` opens a scope.
The first open scope records the start time and calls `setVisible(true)`.
Nested scopes do not call it again.

`EndAsync(setVisible)` closes a scope:

- Without an open scope, it returns at once and does nothing.
- While other scopes stay open, it returns at once.
- When the last scope closes, it waits for the rest of the minimum visible time and then yields a frame.
- Unless a scope is open at that moment, it then calls `setVisible(false)` and yields one more frame.

The check after the wait looks only at the current scope count.
A scope that begins during the wait and is still open keeps the surface visible.
That `Begin` calls `setVisible(true)` again and restarts the minimum visible time.
If the newer scope also ends during the wait, the earlier call still hides the surface at its own time.
The surface then hides before the newer scope's minimum visible time has passed.
The newer call hides it again when its own wait ends.
Core keeps this source behavior.

`RunAsync(action, setVisible)` begins a scope, yields a frame, runs the action, and ends the scope.
The scope also ends when the action throws, and the exception reaches the caller.
Null `action` or `setVisible` arguments throw `ArgumentNullException` with their parameter names.

A lock guards only the scope count.
The callbacks run outside the lock, and the coordinator does not marshal calls.
Callers must serialize `Begin` and `EndAsync`, for example by calling them on the UI thread.
Unserialized calls on two threads can hide the surface before a concurrent `Begin` shows it.
The surface then stays visible with no open scope.
The awaits keep the caller's synchronization context, so calls made on the UI thread continue there.

### Verification for the loading scope

Run these commands after packages have been restored:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Progress.LoadingScopeCoordinatorTests"
```

The two ported source tests check the show-and-hide order and the minimum visible time.
The source measured the minimum time with a `Stopwatch` against the wall clock.
The port runs that test on a manual `TimeProvider`, because a system clock change could skip the wait.
A separate smoke test runs on the system clock without a time threshold.
The manual clock is `tests/Nvt.Core.Tests/Progress/ManualTimeProvider.cs`.

Characterization tests cover these cases:

- The remaining wait, and no timer once the minimum has passed.
- A negative minimum.
- Nested scopes, and an end without an open scope.
- A new scope that is still open when the wait ends.
- An earlier end that hides the surface although a newer scope began and ended during its wait.
- Unserialized calls that leave the surface visible with no open scope.
- The frame-yield order, a failing action, and null arguments.

### Zero-difference verification for the NFH loading scope

NFH adoption is a separate change.
NFH passes no time provider, so it keeps the system clock.
Before and after replacing NFH's class, run these tests from the NFH repository root with the same environment:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build FreeformHelper.sln --no-restore
dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.LoadingScopeCoordinatorTests|FullyQualifiedName~FreeformHelper.Tests.FreeformHelperViewModelTests|FullyQualifiedName~FreeformHelper.Tests.OpenDxfSinglePassTests|FullyQualifiedName~FreeformHelper.Tests.HeadlessUiSmokeTests"
```

The view-model filter includes the load-overlay and modal-spinner scope tests in `FreeformHelperViewModelTests.Basics.LoadOverlay.cs`.
Compare test names, counts, and results with the frozen baseline.
Keep the frozen expected values instead of updating them to accept a difference.

### Loading scope differences

The class is public.
It has a Core namespace, a copyright header, and API documentation.
The source read `DateTimeOffset.UtcNow` and waited with `Task.Delay(remaining)`.
Core reads `TimeProvider.GetUtcNow()` and waits with `Task.Delay(remaining, timeProvider)`.
With the default `TimeProvider.System`, both calls behave as before.
`GetUtcNow()` is still the wall clock, so a system clock change during a scope affects the wait as it did in NFH.
A source comparison that excludes comments and whitespace shows no other change in the method bodies.

NFH keeps these parts:

- The overlay and spinner view-model properties.
- The 120 ms minimum visible time.
- The frame-yield implementation, which uses the dispatcher.
- The separate scope counters behind `BeginCadLoadCanvasOverlayScope` and `BeginModalLoadingSpinnerScope`.

## Progress UI

The UI controls preserve NVT FW Combiner's loading structure and caller-owned animation.
The owner approved this boundary on 2026-10-06.
The library targets net10.0 with Avalonia 12.1.1.
Its namespace is `Nvt.Core.Avalonia.Progress`.

### Frozen UI baselines

Read these files with `git show <sha>:<path>`.
These baselines also supply the provenance for the host's commit message.

| Tool | Repository | Ref | Commit |
| --- | --- | --- | --- |
| NVT FW Combiner (NFC) | `nvt_fw_combiner` | `origin/1.2.x` | `a67eaee35b1d7eda9157a82e880e98a70407e913` |
| NVT FW UTIL (NFU) | `nvt-event-buffer-replay` | `origin/0.2.0` | `26d66bd377a4ad051392bd7cc7e9d1c2e6287dba` |
| Freeform Helper (NFH) | `nvt-freeform-helper` | `origin/1.3.x` | `4df72911867ad047b3217195d12223038a5781b7` |

NFC supplies the wrapper, padding binding, animation policy, and delivery ordering:

- `src/NvtFwCombiner.Presentation.Avalonia/Views/ForegroundLoadingSurface.axaml`.
- `src/NvtFwCombiner.Presentation.Avalonia/Views/ForegroundLoadingSurface.axaml.cs`.
- `src/NvtFwCombiner.Presentation.Avalonia/Resources/MainWindowSharedTemplates.axaml`, specifically `ForegroundLoadingStatusTemplate`.
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ForegroundLoadingState.cs`.
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowInspectionLifecycle.cs`, specifically its progress delivery.

The other UI sources establish caller-owned policies:

- NFU: `src/Nvt.Replay.Avalonia/MainWindow.Output.cs`, lines 600–638.
- NFH: `src/FreeformHelper.UI/Views/WorkflowSteps/RightWorkflowStep5View.axaml`, specifically its progress bar.

Reviewed NFC test sources:

- `tests/NvtFwCombiner.UiSmoke.Tests/ForegroundLoadingStateTests.cs`.
- `tests/NvtFwCombiner.UiSmoke.Tests/WorkflowInspectionLifecycleTests.cs`.
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Startup.cs`.
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.StandardFeedback.cs`.
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.Presentation.cs`.
- `tests/NvtFwCombiner.UiSmoke.Tests/StartupFocusTests.cs`.

### UI delivery rule

[`UiProgress<T>`](../../../src/Nvt.Core.Avalonia/Progress/UiProgress.cs) takes `Action<T> publish`.
A null callback throws `ArgumentNullException`.
`Report` runs `publish(value)` inline when `Dispatcher.UIThread.CheckAccess()` returns true.
Otherwise, it posts the callback to `Dispatcher.UIThread` at the default priority.

NFC calls `Deliver` inline when its captured presentation context is null or equals the current context.
Otherwise, NFC posts `Deliver` to that context.
Core preserves NFC's UI-context ordering while requiring every callback to run on the UI thread.
Core does not retain NFC's context-free background delivery path.

Posted reports keep their enqueue order.
An inline UI report can overtake an earlier background report that still awaits dispatch.
The adapter adds no queue or throttle.

NFU constructs `Progress<ExportJobSnapshot>`, which posts through its captured synchronization context.
NFU then presents the current snapshot directly after start or cancellation.
Its callback rereads the authoritative snapshot and rejects a different job ID.
These snapshot checks and direct presentations remain with NFU.
NFU must retain its always-post observer if inline delivery would change its existing ordering.

### Progress bar

[`ProgressIndicator`](../../../src/Nvt.Core.Avalonia/Progress/ProgressIndicator.cs) inherits `ProgressBar`.
Its only new property is `ProgressUpdate? Progress`.
A known fraction sets `Value` to that fraction.
A null update or null fraction leaves `Value` unchanged.

The control never sets `IsIndeterminate`.
NFC's `ShouldAnimate` stays true during running progress unless reduced motion is enabled.
This rule also applies when NFC knows the fraction.
Keep NFC's `IsIndeterminate="{Binding ShouldAnimate}"` binding.

The style key remains `typeof(ProgressBar)`.
Existing `ProgressBar` selectors and control themes therefore apply.
The default `Maximum` remains Avalonia's default.
NFC and NFH must retain their explicit `Maximum="1"`.

NFC can retain its existing `Value` binding when it uses `ProgressIndicator`.
Bind `Progress` when the tool supplies a `ProgressUpdate`.
NFU retains its own unknown-total rule and range.

### Loading surface

[`LoadingSurface`](../../../src/Nvt.Core.Avalonia/Progress/LoadingSurface.cs) inherits `ContentControl` and adds no properties.
Load `avares://Nvt.Core.Avalonia/Progress/ProgressStyles.axaml` through `StyleInclude`.

The style makes the surface focusable and enables Core's existing `FocusOnRevealBehavior`.
The template contains a scrim `Grid` and one centered inner `ContentControl`.
The scrim uses `{DynamicResource NfcModalScrimBrush}` and blocks pointer input.
The inner control receives the surface's `Content` and `ContentTemplate`.

The named inner part is `PART_Content`.
The tool supplies width and padding through a template-part style.
NFC retains these values:

```xml
<Style Selector="progress|LoadingSurface /template/ ContentControl#PART_Content">
  <Setter Property="Width" Value="430" />
  <Setter Property="Padding" Value="28,26" />
</Style>
```

The `progress` prefix names `Nvt.Core.Avalonia.Progress`.
This method adds no size or padding property to Core.
The surface's inherited `Padding` is not forwarded to the inner part.

NFC's data template retains `Padding="{Binding $parent[ContentControl].Padding}"`.
Its nearest `ContentControl` remains the centered inner part.
The binding therefore reads the same element and `28,26` value as the frozen wrapper.

The tool sets `AutomationProperties.Name` on the surface.
The tool also owns visibility, text, buttons, commands, announcements, sizes, shadows, and product styles.
Core supplies none of these.

### UI verification

Use the existing restored packages:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Progress"
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

The tests preserve NFC's applicable animation and wrapper assertions.
Product lifecycle, text, and command assertions remain in NFC.

[`UiProgressTests`](../../../tests/Nvt.Core.Avalonia.Tests/Progress/UiProgressTests.cs) check UI-thread delivery, posted ordering, and inline overtaking.
[`ProgressIndicatorTests`](../../../tests/Nvt.Core.Avalonia.Tests/Progress/ProgressIndicatorTests.cs) check values, null updates, animation, defaults, and inherited styles.
[`LoadingSurfaceTests`](../../../tests/Nvt.Core.Avalonia.Tests/Progress/LoadingSurfaceTests.cs) check the tree, dynamic scrim, pointer blocking, padding, content, and reveal focus.

The test assembly uses `AvaloniaTestHost` with real Skia rendering.
`FrozenNfcLoadingSurface.axaml` preserves the frozen wrapper with test namespaces and synthetic data.
Paired synthetic templates use `ProgressBar` for the baseline and `ProgressIndicator` for Core.
Both versions use the same synthetic themes without an animation clock.
Fourteen comparisons check every RGBA byte at 640 × 360 pixels and 96 DPI.

Verification on 2026-10-06 passed with zero build warnings and errors.
All 25 Progress UI cases and all 285 Avalonia tests passed.
No tests failed or were skipped.

### Zero-difference adoption in NFC

Replace `ForegroundLoadingSurface` with `LoadingSurface`.
Keep NFC's data context, content, visibility binding, automation name, and status template.
Pass the state through `Content="{Binding}"` and keep its existing `ContentTemplate`.
Load the Core styles and apply NFC's inner width and padding above.
Keep all status-template styles, actions, announcements, and the `ShouldAnimate` binding.

Run these NFC suites before and after adoption:

- `ForegroundLoadingStateTests`.
- `WorkflowInspectionLifecycleTests`.
- `ShellPreloadSessionTests` and `BuiltInBundlePreloadTests`.
- `XamlControlStyleContractTests`, including `CatalogWarmupUsesAccessibleRetryableForegroundLoadingSurface`.
- `StartupFocusTests` and `NavigationFocusIndicatorTests`.

Compare unknown progress, a known fraction, failure, retry, collapse, and completion pixel for pixel.
Also compare known progress with reduced motion enabled.
Keep the same OS, fonts, DPI, theme, inputs, and animation capture position.
Confirm the same focus target, padding, automation names, and stale-progress rejection.
Do not update approved snapshots to accept a difference.

The synthetic Core comparisons do not replace NFC's product snapshot checks.
Adoption and product test execution remain outside this task.
NFH and NFU may change appearance, but their adoption pull requests must include before and after images.
