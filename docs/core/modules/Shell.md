[繁體中文](Shell.zh-TW.md)

# Shell

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`: `_pageHistory` initialization at line 15, `CanGoBack` at line 47, the preceding-entry expression at line 115 exposed as `BackTarget`, and `CompleteNavigation` at lines 171–203, including activation rollback.
- `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`: only `LoadContent` at lines 758–764, renamed `EnsureContent`. Page registration and the `ApplyDeferredShellContent` caller remain in NFC.
- `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.StartupWarmup.cs`: only `MaterializeContent` at lines 42–59. The warmup list, scheduling, run-idle checks, progress, and trace wrappers remain in NFC.

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Shell` contains a synchronous, BCL-only navigation history helper for
host-defined page identities. It has no product, Toolkit, or Avalonia dependency
and exposes no mutable history collection. `Nvt.Core.Avalonia.Shell` contains the
two synchronous `ContentControl` helpers in `PageHost`. The Shell boundary covers
navigation history, activation rollback, and this page-host interface. The caller
registers its own pages and supplies their identities, hosts, templates, and order;
the API assumes no product page list. Workspace composition and preload scheduling
belong to the host application.

The accepted Shell scope is navigation history and the existing PageHost
interface. The retained seams below are product responsibilities within that
scope. This inventory introduces no runtime API, control adoption, or executable
test.

## Core contract provenance

| Contract | Core source | Merge provenance |
| --- | --- | --- |
| `NavigationHistory<TPage>` | `src/Nvt.Core/Shell/NavigationHistory.cs` | Core #54, merge commit `d21934353a87926fb0ab4720f7a5921c48644c76`. |
| `PageHost.EnsureContent` and `PageHost.MaterializeContent` | `src/Nvt.Core.Avalonia/Shell/PageHost.cs` | Core #77, merge commit `968c14c8d3abcd814cb78d376c6d35475bb66160`. |

The frozen forward command at `ShellNavigationViewModel.cs:74–85` and back
command at `107–119` define the callers of history completion. NFC retains their
same-page shortcut, back admission, and guarded requests. Core receives the
captured target, back flag, and optional completion action; the source slices
listed in the opening are the extracted mechanism.

## Public API

```csharp
namespace Nvt.Core.Shell;

public sealed class NavigationHistory<TPage> where TPage : notnull
{
    public NavigationHistory(TPage home, Func<TPage> selectedPage,
        Action<TPage> activate, Action stateChanged);
    public bool CanGoBack { get; }
    public TPage BackTarget { get; }
    public void CompleteNavigation(TPage target, bool isBack,
        Action? afterActivation = null);
}
```

```csharp
namespace Nvt.Core.Avalonia.Shell;

public static class PageHost
{
    public static void EnsureContent(ContentControl host, bool shouldLoad,
        object content);
    public static void MaterializeContent(ContentControl host, object dataContext);
}
```

`ContentControl` is Avalonia's existing control. PageHost adds no control,
registry, dispatcher, or scheduler. The caller owns UI-thread access and decides
when runs are idle enough for materialization.

Construction stores one injected Home entry without reading selection or running
callbacks. Null callbacks are rejected in `selectedPage`, `activate`, then
`stateChanged` order. Page values use `EqualityComparer<TPage>.Default` without
normalization or product page validation. `CanGoBack` is exactly
`history.Count > 1`. `BackTarget` returns the preceding entry and throws
`InvalidOperationException` when that entry is absent.

The frozen history mechanism has no configurable positive limit, page-ID length
limit, or history-size ceiling. The fixed mechanism boundary is one history
entry; zero entries are unreachable because back completion never removes the
Home entry. No product ceiling is relocated to Core.

## Preserved completion and rollback

1. Read the selected source and snapshot the current history before activation.
   For forward completion, read selection again for the append predicate.
   These reads and history mutation occur outside the activation `try` block.
2. Back completion removes one latest entry only when history has more than one
   entry. Forward completion appends the target only when the current selection
   differs from it. The predicate compares selection, not the latest history entry.
3. Activate the supplied target even when it equals selection, then invoke
   `afterActivation` if present. Successful state refresh belongs to `activate`;
   Core does not issue an additional `stateChanged` callback on success.
4. History changes before activation, so a synchronous reentrant refresh sees
   the updated back availability and target. Callbacks may complete nested
   navigation. An outer failure still restores its own original history snapshot.
5. On activation or completion-action failure, read selection and reactivate the
   captured source only when the selected page differs. Rollback activation sees
   the changed history because restoration occurs afterward.
6. After successful rollback activation, or when no reactivation is needed,
   restore the prior history, invoke `stateChanged`, and rethrow the original
   exception with a bare `throw`.
7. A failed rollback selection read or activation replaces the original failure
   before history restoration and `stateChanged`. A failed `stateChanged`
   replaces the original failure after history has been restored. There is no
   additional cleanup that changes this ordering.

The host captures `BackTarget` before requesting confirmation and passes that
target to completion. Completion does not recompute it after confirmation-close
notifications reenter. It removes the latest entry according to the original
back predicate even if reentry changed history. Its rollback source is the
selection read at completion time; the host may separately retain the earlier
source for its clear action. Calls are not synchronized and belong to the host's
navigation thread.

## Preserved page hosting

`EnsureContent` checks `shouldLoad` first. When false, it does not access the host.
When true, its only operation is `host.Content ??= content`. Existing non-null
content retains its identity; Core does not explicitly build a template, change
`ContentTemplate`, or assign a control's `DataContext`. Avalonia still handles
the property's normal notifications and presentation.

`MaterializeContent` returns immediately when `host.Content` is non-null at
entry. Otherwise, it calls `host.ContentTemplate?.Build(dataContext)` directly,
without consulting `Match`. A missing template or null build result calls
`EnsureContent(host, true, dataContext)`, retaining the template. The fallback's
null-coalescing assignment also retains content admitted by a reentrant build.

A non-null built control receives the exact `dataContext` first. The host's
`ContentTemplate` is cleared next, and its `Content` is assigned last. The host's
own `DataContext` is unchanged. Later ordinary calls retain the published content
and do not rebuild, including lazy-first and warmup-first calls.

Builds and property notifications are synchronous. There is no reentrancy guard
or second content check after a successful build: a nested materialization while
content is null can build again, and the outer successful call publishes last.
Template-clear notifications can likewise admit interim content before that
final assignment. These are the frozen assignment semantics.

Build and assignment failures propagate unchanged, with no wrapping or rollback.
An assignment failure stops subsequent steps and retains changes already made.
The API adds no argument validation: a runtime null host is ignored by false lazy
admission and otherwise throws `NullReferenceException`. A runtime null content
or context is accepted; a null fallback leaves content null and permits another
materialization call. A successful build receives a null context as supplied.

The frozen PageHost helpers contain no numeric limit, positive limit parameter,
product ceiling, or message literal. Their boundaries are both admission values,
null/non-null content, and missing/null/non-null build results. No NFC ceiling
is relocated, widened, or introduced.

## Source-to-Core test mapping

All Core tests use synthetic identities, callbacks, and deterministic synchronous
gates. Trace assertions compare every recorded selection read, activation,
successful refresh, completion action, and restoration refresh in order.

| Frozen evidence | Core tests in `tests/Nvt.Core.Tests/Shell/` |
| --- | --- |
| `ShellNavigationSystemTests.NavigationTransaction.cs:15`, forward/back cases | `FailedActivationKeepsSourceInputsAndRestoresRetryableHistory`: unchanged source inputs, original failure message, zero clears on failure, one clear on retry, source identity, and subsequent back target. |
| `ShellNavigationSystemTests.NavigationTransaction.cs:86` | `PostActivationSourceClearFailureRollsBackDestinationAndHistory`: source reactivation, retained inputs, original failure message, zero successful clears on failure, one on retry, and restored history. |
| History, same-target completion, and back selection slices | `ForwardAndBackCompletionPreserveHistoryAndCallbackOrder`, `EqualTargetCompletionActivatesWithoutAddingHistory`, `EqualReferencePageValuesStillActivateTheSuppliedTarget`. |
| One-entry mechanism boundary and missing back entry | `ConstructorSeedsHomeWithoutInvokingCallbacks`, `ForwardAndBackCompletionPreserveHistoryAndCallbackOrder`, `BackCompletionWithOnlyHomeStillActivatesCapturedTarget`: one, two, and three entries, then back to one; no zero-entry state. |
| Additional activation and rollback characterization | `PostActivationBackFailureRestoresSourceBeforeHistory`, `FailedActivationAfterSelectionReactivatesSourceAndRestoresHistory`, `FailedEqualTargetActivationRestoresStateWithoutReactivation`, `RollbackUsesDefaultPageEqualityInsteadOfReferenceIdentity`. |
| Exception ordering | `RollbackActivationFailureInterruptsHistoryRestoration` (before/after selection), `StateChangedFailureReplacesOriginalFailureAfterHistoryRestoration`, `SelectionReadFailureBeforeActivationLeavesHistoryUntouched` (first/second reads), `RollbackSelectionReadFailureInterruptsHistoryRestoration`. |
| Reentrant state observation and nested completion | `HostRefreshObservesChangedHistoryBeforeCompletionActions`, `ActivationRefreshCanCompleteNestedNavigation`, `OuterFailureRestoresHistoryFromBeforeNestedNavigation`. |
| Confirmation-close captured target contract | `CapturedBackTargetCompletesAfterConfirmationCloseReentry`, `CapturedBackFailureRestoresSourceObservedAfterConfirmationCloseReentry`. |
| Selection predicate and read order | `ForwardAppendComparesSelectionInsteadOfLatestHistoryEntry`, `ForwardAppendUsesSecondSelectionRead`. |
| New injection and identity edge inputs | `ConstructorRejectsNullCallbacksInParameterOrder`, `StringPageIdentitiesRoundTripWithoutNormalization`, `NumericPageIdentitiesHaveNoPositiveLimitPolicy`: null callbacks, empty/whitespace/control/Unicode IDs, zero, negative IDs, and integer endpoints. |

The product assertions in `NavigationClear.cs`, `NavigationClearModal.cs`, and
`SettingsNavigation.cs` remain NFC evidence. Core does not reproduce firmware,
catalog, modal, focus, breadcrumb, or pixel fixtures.

PageHost tests use synthetic objects, controls, and templates through the existing
linked `AvaloniaTestHost` and the test assembly's single registration. Property
callbacks provide deterministic ordering and reentry gates, without timed waits.

| Frozen evidence or boundary | Core tests in `tests/Nvt.Core.Avalonia.Tests/Shell/PageHostTests*.cs` |
| --- | --- |
| `MainWindow.axaml.cs:758–764`, true/false admission and existing-content boundary | `EnsureContentUsesOnlyLazyAdmission`, `EnsureContentRetainsExistingContent`, `HelpersRetainNonNullEdgeValues`, `EnsureContentDoesNotAssignControlDataContext`, `EnsureContentAcceptsNullContentWithoutSealingTheHost`. |
| `MainWindow.StartupWarmup.cs:44–54`, existing content and both fallback branches | `MaterializeContentRetainsExistingContent`, `MaterializeContentPreservesFallbackIdentities`. |
| `MainWindow.StartupWarmup.cs:49,56–58`, exact context identity, one build, and assignment order | `MaterializeContentBuildsOnceAndAssignsInFrozenOrder`, `MaterializeContentDoesNotConsultTemplateMatch`. |
| Lazy-first and warmup-first paths; denied lazy admission | `LazyFirstAndWarmupFirstRetainTheirFirstContent`, `DeniedLazyAdmissionStillAllowsWarmup`. |
| Runtime null host, content, and context edges | `NullHostFollowsTheFrozenPredicateOrder`, `EnsureContentAcceptsNullContentWithoutSealingTheHost`, `MaterializeContentNullFallbackRemainsRetryable`, `MaterializeContentAssignsNullDataContextToBuiltControl`. |
| Build failure and each of the three assignment-failure boundaries | `BuildFailurePropagatesUnchangedAndAllowsRetry`, `AssignmentFailurePreservesPartialStateAndOrder`, `EnsureContentAssignmentFailurePropagatesUnchanged`, `FallbackAssignmentFailurePropagatesUnchanged`. |
| Lazy and materialized publication reentry | `LazyAssignmentReentryRetainsPublishedContent`, `MaterializedContentReentrySeesCompletedAssignments`. |
| Successful/null Build reentry, nested build, context/template notification reentry, and reentrant build failure | `BuildReentryPreservesSuccessfulAndFallbackAssignmentRules`, `BuildReentryCanMaterializeAgainBeforeOuterPublication`, `DataContextReentryRetainsTheNestedContextChange`, `TemplateClearReentryRunsBeforeOuterContentAssignment`, `BuildFailureRetainsReentrantHostChanges`. |
| `XamlControlStyleContractTests.Startup.cs`, `MainWindowDefersInactivePageAndModalContent` and `MainWindowWarmsCommonPagesAfterFirstFrameWithoutLoadingModals` | The helper-specific source checks map to lazy admission and direct template-build/publication tests above. NFC retains page-host registration, deferred resource placement, warmed-page order, first-frame timing, run-idle gating, and trace assertions. |

`ShellPreloadSessionTests.Presentation.cs`, `ShellPreloadSessionTests.Cancellation.cs`,
`WindowLifetimeTests.Ready.cs`, and `ShellScreenInventoryTests.cs` retain their
product preload, cancellation, window lifetime, page composition, layout, focus,
and pixel assertions in NFC. Their thresholds and scheduling budgets are outside
PageHost. Core characterizes the extracted helpers;
it does not relocate these product test suites.

The immutable NFC parent evidence covers navigation, guard and confirmation
reentry, focus, deferred content identities, warmup order, and startup sections.
NFC adoption compares history and PageHost through the actual product adapters
using those fixed parent observations and unchanged scenario drivers. Product
tests continue to own the retained seams; helper evidence does not certify a
broader workspace framework.

## Verification

Run from the solution root after a locked restore:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Core tests cover history and PageHost mechanisms. NFC proves product and pixel parity against the frozen parent in its
adoption.

## Retained NFC workspace and preload inventory

Every path below is relative to the frozen NFC repository named in the opening.
The owner column names the retained NFC component responsible for the seam.
All predicates, limits, messages, and product test expectations remain with their
existing owners. NFC retains firmware, product wording, schemas, trust, and
release authority.

| Retained seam | NFC owner | Source path and slice | Reason it stays in NFC |
| --- | --- | --- | --- |
| Firmware guards and command admission | NFC navigation adapter: `ShellNavigationViewModel` and `MainWindowViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`: `NavigateToPage`, `GoBack`, `RequestNavigation`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs`: `HasPageSelectedFiles`. | Selected firmware/editor inputs, mismatch invalidation, the same-page shortcut, and the Home back no-op are product policy. |
| Confirmation, exit, source clearing, and close-notification reentry | NFC `ShellNavigationViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`: `RequestExitConfirmation`, `RefreshConfirmation`, `ConfirmNavigationAndClear`, `CancelNavigationClear`, `PendingNavigation`. | NFC captures the destination before confirmation, captures the clear-action source before closing it, and owns ignored later requests, exit override, cancel reactivation, wording, and notifications. Core completes the supplied destination after reentry. |
| Breadcrumbs, labels, and commands | NFC navigation presentation | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`: `RefreshNavigationTrail`, `NavigationPath`, `UpdateState`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationEntryViewModel.cs`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs`: `PageLabel`. | The Home/current-page trail, label validation, localized labels, Toolkit commands, and command notifications are product presentation. |
| Page identities and factories | NFC shell composition | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.cs`: `ShellPage`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellViewModelFactory.cs`: `Create`; `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.StartupFactory.cs`: `CreateStartupViewModel`. | NFC defines its pages, host services, version labels, startup language, and preference application. Core accepts host-defined identities. |
| Product page composition and activation | NFC `MainWindowViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Construction.cs`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Context.cs`: `ApplySelectedPage`. | Firmware/workflow services, reports, settings, run sessions, and MessageCenter are composed here. Product activation validates and restores workflow context and performs the successful `Navigation.UpdateState()` refresh. |
| Workspace layout and product templates | NFC `MainWindow` XAML | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml`: resource includes, shell grid, page templates, action rail, loading/status surfaces, and modal hosts. | Geometry, ancestry, visibility, resource placement, gestures, focus, and accessibility are part of NFC's unchanged UI contract. |
| Page-host registration and lazy admission | NFC `MainWindow` | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`: `ApplyDeferredShellContent` at lines 734–756 and `ViewModel_OnPropertyChanged`. | NFC selects each host, content object, and product visibility predicate. Only `LoadContent` at lines 758–764 moves to `PageHost.EnsureContent`. |
| Deferred settings and retained editor state | NFC `DeferredShellState` and shell adapters | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/DeferredShellState.cs`: `EnsureSettings`, `GetHexEditorWorkspace`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs`: `OpenSettings`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.HexEditor.cs`: `ShowHexEditor`. | NFC owns when settings load, the cached editor identity, its file-session factory, subscriptions, and retained inputs. |
| Preload session and scheduling policy | NFC `ShellPreloadSession` | `src/NvtFwCombiner.Presentation.Avalonia/ShellPreloadSession.cs`: stage/attempt records, `RunCatalogAsync`, `RunOptionalStagesCoreAsync`, report and environment/diagnostics chains, retry/skip/cancel/drain, progress validation, and publication. | Required catalog admission, optional dependencies, generations, worker budget, drain timeout, reduced motion, and localized status are product lifecycle policy. |
| Startup preload work and readiness | NFC `MainWindow` and launch composition | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`: `RunStartupPreloadAsync`, `RunRequiredPreloadAsync`, `PresentPreloadStage`, `ApplyPreloadStage`, retry/skip/cancel handlers, and `ApplyShellInteractionState`. | NFC supplies history/report/diagnostics/view work, enables the shell after required publication, applies launch options, and owns status and focus. |
| Warmed-page list and run-idle scheduling | NFC `MainWindow` warmup | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.StartupWarmup.cs`: `WarmDeferredShellAsync` at lines 10–40 and scheduling/timing wrappers at lines 61–142. | The ordered five hosts, contexts, progress, UI dispatcher priority, run-idle wait, generation check, cancellation, and trace names stay local. Only `MaterializeContent` at lines 42–59 moves. |
| Application startup wrapper | NFC `App` | `src/NvtFwCombiner.Presentation.Avalonia/App.axaml.cs`: `SetStartup`, `Initialize`, `OnFrameworkInitializationCompleted`. | NFC owns startup state, compiled application resources, preferences, desktop lifetime, main-window creation, and capture shutdown policy. |
| Desktop startup wrapper | NFC `DesktopApplication` | `src/NvtFwCombiner.Presentation.Avalonia/DesktopApplication.cs`: `Run`, `DispatchLaunch`, `PrepareStartup`, `BuildAvaloniaApp`. | Command-line validation, public completion, protected startup inputs, local-state composition, trace opt-in, fonts, and desktop lifetime are application choices. |
| Window startup and lifetime wrapper | NFC `MainWindow` | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`: constructor, `OnOpened`, `RunStartupAfterOpenedAsync`, `OnClosing`, `OnClosed`, `Dispose`. | NFC owns first-frame deferral, window publication/admission, input loading, exit confirmation, cancellation, and close/drain coordination. |
| Startup section builders and timing adapters | NFC startup diagnostics | `src/NvtFwCombiner.Presentation.Avalonia/StartupTraceSession.cs`: `Create`, `MarkProfileAdmission`, `Mark`, `Complete`, provider adapters; `src/NvtFwCombiner.Presentation.Avalonia/StartupTraceFileSink.cs`: `TryWrite`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.SystemActivity.cs`: `RecordStartupDuration`. | NFC supplies milestone/profile mappings, terminal and preload sections, diagnostic schema, time/allocation adapters, and activity wording. These wrappers remain outside Shell's two content helpers. |
| MessageCenter typed facade | NFC `MessageCenterViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MessageCenterViewModel.cs`: typed service/report bindings, localized projections, startup refresh, environment publication, and export context. | The facade joins product diagnostics, firmware readiness, report ownership, wording, and commands without adding those concepts to Shell API. |
| MessageCenter product template and wording | NFC `MessageCenterModal` and `ShellTextResources` | `src/NvtFwCombiner.Presentation.Avalonia/Views/MessageCenterModal.axaml`; `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml`: `MessageCenterModalHost`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.MessageCenter.cs`; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Localized.cs`: MessageCenter labels. | The report table, storage-picker event, activity layout, product labels/formatters, modal ancestry, and accessibility stay in NFC. |
| Report composition and file-picker adapters | NFC report presentation and `MainWindow` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Construction.cs`: `Reports` construction; `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Report.cs`; `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.Report.cs`: `LoadReportJsonCoreAsync`, `ApplyStartupReportAsync`, `ApplyLaunchPage`. | Report meaning, modal/toast integration, file admission, startup arguments, projection generations, and focus return are product behavior. |
| Report and preference persistence adapters | NFC local-state stores and window composition | `src/NvtFwCombiner.Presentation.Avalonia/ReportHistoryFileStore.cs`; `src/NvtFwCombiner.Presentation.Avalonia/ShellPreferenceFileStore.cs`; `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`: persistence construction, `Reports_OnPropertyChanged`, `PostLocalStateSaveOutcome`, preference-save wiring. | NFC owns schemas, paths, byte/retention budgets, fallback, snapshot capture, retry notices, and shutdown save policy. Generic codec/save coordination has its separate Persistence boundary. |

The firmware callbacks remain owned by NFC's
`WorkflowSessionPresentationViewModel`:
`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowSessionPresentationViewModel.Slots.cs`
contains `HasSelectedInputs` and `ClearSelectedInputs`;
`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowSessionPresentationViewModel.FirmwareNumberMismatch.cs`
contains `InvalidateFirmwareNumberMismatch`; and
`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowSessionPresentationViewModel.WorkflowContext.cs`
contains `ValidatePageActivation` and workflow activation/restoration. The
navigation bindings and `ApplySelectedPage` delegate to these product owners.

The retained preload evidence is
`tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.cs`,
`tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.Presentation.cs`, and
`tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.Cancellation.cs`.
These cover typed terminal publication, required-stage interaction, dependencies,
retry generations, cancellation, late progress, and actual settlement after a
bounded drain. Their source remains NFC evidence alongside the navigation,
window-lifetime, composition, layout, and focus suites described above.

## Existing Panels comparison

The comparison source is Core's cached `origin/main`, full commit
`b98099a43553f4d04f084a3a33bc027bd9a8c95c`, which contains the Panels merge
from Core #30 (`2af6b30f181a1c0006d6070339edc90366afbf1e`). Its controls,
styles, tests, and [Panels module contract](Panels.md) provide comparison
candidates for retained NFC composition.

| Candidate | Source at the comparison commit | Applicable contract and limit of comparison |
| --- | --- | --- |
| `Panels.WorkspaceShell` | `src/Nvt.Core.Avalonia/Panels/WorkspaceShell.cs`; `src/Nvt.Core.Avalonia/Panels/PanelsStyles.axaml`; `tests/Nvt.Core.Avalonia.Tests/Panels/WorkspaceShellTests.cs`. | A templated header, summary, toolbar, two main columns, and footer with caller-supplied content; default widths are `2.2*` and `*`. Tests describe slots, row order, and live width updates. This layout does not supply NFC page identity, guards, deferred hosts, overlays, or preload scheduling. |
| `Panels.CollapsiblePanel` | `src/Nvt.Core.Avalonia/Panels/CollapsiblePanel.cs`; `src/Nvt.Core.Avalonia/Panels/PanelsStyles.axaml`; `tests/Nvt.Core.Avalonia.Tests/Panels/CollapsiblePanelTests.cs`. | A flat toggle header and body: unset initial expansion uses `DefaultExpanded`, an explicit value wins, and disabling collapse forces expansion. Tests describe keyboard/pointer toggles, visibility, and header styling. Its geometry, vector chevron, and style choices do not establish equality with NFC's product panels or preload status `Expander`. |

Shell adopts no Panels control and replaces no NFC workspace markup.
Any later Panels adoption requires separate explicit scope and zero differences
in behavior, identities, focus, accessibility, layout, and decoded UI pixels
against the same frozen NFC parent. Panels' source provenance and its synthetic
tests cannot provide that product proof. Its original host's acceptance of visual
differences does not relax NFC's unchanged-snapshot contract.

## NFC adoption rules

History and PageHost form the accepted current move. A broader move requires a
new explicit owner scope with exact source slices, write paths, and dependencies
fixed before implementation. The inventory retains product ownership and
authorizes no broader deletion. Shared projects, root plans, component lists,
versions, references, and lock files remain under their existing integration
owners.

NFC adopts this module in its own pull request. It downloads verified, versioned
`Nvt.Core` and `Nvt.Core.Avalonia` nupkg files at build time through
`core-packages.json` (Core #61), with exact `[x]` versions, locked restore, and source mapping
restricted to the download folder. The manifest records each package's Release
tag and SHA-256. Core and
NFC retain independent versioned releases. NFC owns its shared package references,
pins, source mapping, and lock files and adds no ProjectReference to a Core checkout.

NFC waits for the exact reviewed module package provenance and behavior evidence before
retargeting these helpers. Core extraction and NFC adoption keep their separate
module pull requests and independent releases; NFC adoption targets its next
compatible 1.2.x patch. This inventory changes no package closure.

NFC retargets the two helper callers and deletes only their local method bodies,
generic history storage, and completion rollback body after package provenance
and complete executable values, identities, and event traces match the frozen
parent. It retains the product adapters and proves zero changed decoded UI pixels
in the same recorded OS, resolved fonts, DPI, theme, renderer, viewport, motion,
input, time, and IDs, recording comparison-artifact SHA-256 hashes. The eight
legacy font values remain unchanged; new Core font roles are outside this adoption.
Pixel comparisons require equal decoded dimensions and zero maximum channel
difference, without cropping, masking, normalization, or tolerance.
