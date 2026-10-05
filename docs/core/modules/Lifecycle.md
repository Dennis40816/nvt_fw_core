[English](Lifecycle.md) | [中文](Lifecycle.zh-TW.md)

# Lifecycle

UI-independent refresh coalescing and an undo stack in `src/Nvt.Core/Lifecycle/`, namespace `Nvt.Core.Lifecycle`.
Both helpers use caller-supplied delegates and depend only on .NET. They contain
no Avalonia, dispatcher, or NFH types.

## Public API and behavior

- `CoalescedRefresh(Action<Action> schedule, Action refresh)` rejects null
  delegates. `Request()` is safe to call from any thread and coalesces requests
  until the queued callback starts. The callback clears the scheduled flag before
  calling `refresh`, so a reentrant request schedules another callback.
- `CoalescedRefresh.Reset()` clears the flag so the next request can schedule.
  It does **not** cancel an already queued callback; that callback still refreshes.
  The refresh action must tolerate running after its owner has let go.
- A scheduling exception propagates from `Request()` without clearing the flag.
  Later requests remain coalesced until `Reset()` or an already queued callback
  clears it. There is no automatic retry.
- `UndoService.CanUndo` reports whether the stack contains entries.
  `Push(Action undo, string description)` stores an entry without executing it.
  `TryPop(out UndoAction action)` removes the newest entry in LIFO order without
  executing it. An empty stack returns `false` and a null `action`, preserving the
  source signature. The stack is not synchronized.
- `UndoAction(string Description, Action Undo)` is a sealed record. The stack
  preserves the description and delegate as supplied; the caller invokes `Undo`.

## Frozen provenance

- Repository: `Dennis40816/nvt-freeform-helper`
- Ref: `1.3.x`
- Full commit SHA: `e01e07a361b8dc264a06b3741f40274feeeace2d`
- Extracted implementation files:
  - `src/FreeformHelper.UI/Services/CoalescedRefresh.cs`
  - `src/FreeformHelper.UI/Services/UndoService.cs`
- Ported refresh tests:
  - `tests/FreeformHelper.Tests/UI/Services/CoalescedRefreshTests.cs`
- Undo behavior reference:
  - `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.CommandsAndUndo.CadEditing.cs`
    (`Undo_RevertsDisplayToggleChange`, `DeleteSelectedCadPadsCommand_CanUndoToRestoreHiddenPads`,
    `ClearCombinedCadPadsCommand_CanUndoBackToCombinedState`)
  - `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.CommandsAndUndo.GeometryTransforms.cs`
    (`RotateSelectedCadPadsCommand_RotatesSelectedPadAndCanUndo`,
    `OffsetSelectedCadOutputFwDiffIndicesCommand_ShiftsSelectedVisibleCadDiffsAndCanUndo`)

Implementation changes are limited to the namespace, making `CoalescedRefresh`
public, copyright headers, and API documentation. Method bodies and the undo
record declaration retain the frozen implementation. The four refresh tests are
ported; the reset test is renamed to describe scheduling rather than suggest
cancellation. Undo tests adapt the state-restoration and `CanUndo` assertions to
synthetic values with explicit caller execution of the popped delegate.

## Verification

Run from the Core repository root after packages have been restored:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Lifecycle"
```

The tests cover burst coalescing, scheduling after completion, reentrancy, reset
allowing another schedule, queued callbacks surviving reset, and a scheduler
exception leaving the flag set until reset. Undo tests cover caller-driven state
restoration, LIFO order, remaining entries and `CanUndo`, no execution on push or
pop, an empty pop returning null, and preservation of descriptions and delegates.

### NFH zero-difference check on adoption

Adopting Core in NFH is a separate change. Before and after replacing NFH's helpers,
run its existing tests from the NFH repository root with the same environment:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build FreeformHelper.sln --no-restore
dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.CoalescedRefreshTests|FullyQualifiedName~FreeformHelper.Tests.FreeformHelperViewModelTests"
```

The view-model filter includes all `CommandsAndUndo*.cs` tests and its existing
settings-load tests. Compare the same test names, counts, and assertion results
against the frozen baseline: callback queue and refresh counts; original state
restoration; `CanUndo` and undo command availability; unchanged status strings;
and existing suppression and settings-load behavior. Also run the Core Lifecycle
characterization suite above. For representative identical request/reset/pop
sequences, compare scheduled callbacks, refresh counts, popped action order,
descriptions, `CanUndo`, and caller-executed effects. Preserve the frozen expected
values instead of updating them to accept differences.

## What stays in NFH

Undo suppression, settings-load policy, domain-specific undo payloads, status
strings, command notifications, and dispatcher selection remain in NFH's view
model and UI services. These helpers do not manage those policies. This extraction
does not modify NFH or adopt Core there.
