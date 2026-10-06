[繁體中文](Shell.zh-TW.md)

# Shell

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`: history initialization at line 15, `CanGoBack` at line 47, preceding target at line 115, and completion/rollback at lines 171–203. The command paths at lines 74–85 and 107–119 establish the adapter boundary; their guards and shortcuts remain in NFC.

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Shell` contains a synchronous, BCL-only navigation history helper for
host-defined page identities. It has no product, Toolkit, or Avalonia dependency
and exposes no mutable history collection. This extraction covers history and
activation rollback. Page hosting, workspace composition, and preload scheduling
are separate mechanisms.

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

## Verification

Run from the solution root after a locked restore:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Core tests cover the history mechanism. NFC proves product and pixel parity against the frozen parent in its
adoption.

## NFC ownership and adoption

NFC retains `ShellPage`, page identity and factories, firmware guards, Home back
no-op policy, the same-page command shortcut, confirmation and exit state,
source-clear policy, wording, commands, breadcrumbs, startup wrappers, and the
MessageCenter facade. `MainWindowViewModel.Context.cs` retains product page
activation and successful `Navigation.UpdateState()` refresh;
`MainWindowViewModel.Construction.cs` retains product composition.

NFC adopts this module in its own pull request. That pull request consumes a verified, versioned `Nvt.Core`
nupkg from `vendor/nuget/` with an exact `[x]` version, locked restore, package source mapping, and the source
and package SHA-256 in `SOURCE.md`. NFC owns its package references and lock files. NFC deletes only its local
generic history storage and the completion rollback body, after the package and the executable behavior match
the frozen parent. NFC keeps its product adapter and proves identical UI snapshots in the same recorded
environment.
