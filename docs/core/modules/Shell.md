[繁體中文](Shell.zh-TW.md)

# Shell

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`: history initialization at line 15, `CanGoBack` at line 47, preceding target at line 115, and completion/rollback at lines 171–203. The command paths at lines 74–85 and 107–119 establish the adapter boundary; their guards and shortcuts remain in NFC.
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
PageHost. Core adds executable characterization of the extracted helpers;
it does not relocate these product test suites.

## Verification

Run from the solution root after a locked restore:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Core tests cover history and PageHost mechanisms. NFC proves product and pixel parity against the frozen parent in its
adoption.

## NFC ownership and adoption

NFC retains `ShellPage`, page identity and factories, firmware guards, Home back
no-op policy, the same-page command shortcut, confirmation and exit state,
source-clear policy, wording, commands, breadcrumbs, startup wrappers, and the
MessageCenter facade. `MainWindowViewModel.Context.cs` retains product page
activation and successful `Navigation.UpdateState()` refresh;
`MainWindowViewModel.Construction.cs` retains product composition.

`MainWindow.axaml` retains the visual hosts, page templates, layout, and resource
placement. `MainWindow.axaml.cs` retains page registration and admission policy.
`DeferredShellState.cs` retains settings and editor factories and cached product
state. `MainWindow.StartupWarmup.cs` retains the warmed-page list, run-idle and
generation checks, progress, scheduling, and startup trace wrappers.
`ShellPreloadSession.cs` retains preload admission, retries, cancellation, and
worker budgets. NFC also retains App, DesktopApplication, and MainWindow startup
wrappers, report composition and persistence adapters, firmware, wording, schemas,
trust, and release authority, including its MessageCenter facade and template.

The residual workspace ownership inventory is a separate adoption gate. Any
broader workspace move requires an explicit scope and source inventory;
history/PageHost evidence covers only this interface. Existing Panels primitives
require separate adoption scope and NFC pixel evidence before replacing NFC
workspace markup.

NFC adopts this module in its own pull request. It downloads verified, versioned
`Nvt.Core` and `Nvt.Core.Avalonia` nupkg files at build time through
`core-packages.json`, with exact `[x]` versions, locked restore, and source mapping
restricted to the download folder. The manifest records each package's Release
tag and SHA-256; the Release's `SOURCE.md` identifies its Core source. Core and
NFC retain independent versioned releases. NFC owns its shared package references,
pins, source mapping, and lock files and adds no ProjectReference to a Core checkout.

NFC retargets the two helper callers and deletes only their local method bodies,
generic history storage, and completion rollback body after package provenance
and complete executable values, identities, and event traces match the frozen
parent. It retains the product adapters and proves zero changed decoded UI pixels
in the same recorded OS, resolved fonts, DPI, theme, renderer, viewport, motion,
input, time, and IDs, recording comparison-artifact SHA-256 hashes. The eight
legacy font values remain unchanged; new Core font roles are outside this adoption.
