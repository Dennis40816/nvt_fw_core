# Core conventions

Use these conventions for Core extraction and tool adoption. The tool names are:

- NFC: NVT FW Combiner.
- NFH: Freeform Helper.
- NFU: NVT FW UTIL.

## Layout and branches

Start from the current `main`. Use a `feature/core/<module>` branch for each module.

- Put source files in `src/<Library>/<Module>/`.
- Use the namespace `<Library>.<Module>`.
- Put tests in `tests/<Library>.Tests/<Module>/`.

For example, `src/Nvt.Core.Avalonia/Theme/` uses `Nvt.Core.Avalonia.Theme`. Its tests belong in `tests/Nvt.Core.Avalonia.Tests/Theme/`.

Change only your module's directories and documents. Do not edit the root `README.md` or `README.zh-TW.md`. The integrator adds each module to their module list.

## Shared files

Only the integrator, NVT CORE, changes these shared files:

- `Directory.Packages.props`
- `Directory.Build.props`
- `global.json`
- `Nvt.Core.sln`
- NuGet package lock files
- Root configuration
- CI workflows

If you need a shared change, stop and report it to NVT CORE. Name the module, affected files, required change, and reason.
The integrator applies the change on the integration branch.

## Pull requests and review

Each module has one extraction pull request in Core and one adoption pull request in its source tool.
Port the source's existing tests and add characterization tests for representative inputs.

An independent codex performs the first review of each pull request. The review checks:

- Zero-difference evidence.
- Minimal API scope.
- Product logic boundaries.
- Public repository hygiene.
- License headers.
- Tests.

Claude reviews the findings and high-risk Launcher, process, and IO changes. NVT CORE sets the merge order.
Report each opened pull request to the coordinator with its full URL. The owner approves pull requests before merge.

## Zero-difference evidence

Freeze the parent baseline before extraction. Record its repository, ref, full commit SHA, and extracted file paths.
Put this record in the extraction commit message and both module documents:

- `docs/core/modules/<Module>.md`
- `docs/core/modules/<Module>.zh-TW.md`

The adoption pull request includes comparison evidence against that frozen baseline. Compare UI snapshots with the same OS, fonts, DPI, and theme.
Never update a baseline to make a check pass.

Owner decision of 2026-10-06: 「NFC 要求保持一致，其他沒有要求」 ("NFC must stay identical. The others have no such requirement.")

This decision adjusts point 3 of the five import acceptance criteria:

- NFC UI snapshots must show zero difference.
- NFH and NFU UI snapshots may differ without owner approval. Attach before/after images to the pull request as a record.
- Non-UI output, including files and data, must show zero difference in every tool.

The theme look PR is the one exception for NFC UI snapshots. The owner approves its before-and-after images (owner decision 2026-10-07).

## Package versions and lock files

Owner decision of 2026-10-06: 「repo 共同鎖定」 ("Repositories lock dependencies together.")

Every repository enables NuGet package lock files. CI always restores in locked mode:

```text
dotnet restore --locked-mode
```

Align shared package versions with the set pinned in Core's `Directory.Packages.props`. NVT CORE maintains that list.
Request version and lock file changes through the integrator.

## Fonts

Owner decision of 2026-10-06: 「我覺得可以討論出一個 font set 就只能用裡面的，font set 定位要清晰，例如 title 固定用哪種」 ("I think we can agree on one font set and use only its fonts. Define clear roles, such as a fixed title font.")

Use only Inter, Cascadia Mono, Noto Sans TC, and Material Symbols Outlined. Noto Sans TC provides the Traditional Chinese fallback.

| Role | Font | Size |
|---|---|---|
| Caption | Inter | 11 |
| Body | Inter | 13 |
| Heading | Inter | 16 |
| Title | Inter | 24 |
| Mono and numbers | Cascadia Mono | 13 |
| Icon | Material Symbols Outlined | 16 |

Strong roles change only the weight. Embed all fonts at fixed versions. Never package Windows fonts.

The [Fonts module](modules/Fonts.md) has the full role table with weights and resource keys. Numbers uses Regular (400), because Cascadia Mono has no official static Medium (500) file (owner, 2026-10-06).

NFC adopts the font set in two steps:

1. Extract with zero difference first.
2. Apply the new role table in a separate pull request. Attach before/after images and obtain the owner's approval.

## Theme keys

Core styles use the `Nfc*`, `Nvt.Focus.*` and `Nvt.Button.*` keys in `src/Nvt.Core.Avalonia/Theme/`. Pick each key by the role of its use, not by the source tool's token name.

- Use Core's existing state pairs for hover and pressed. For example, `Button.actionNeutral` uses `NfcSelectionSurfaceBrush` for hover and `NfcSecondaryActionPressedBrush` for pressed.
- Several source tokens may map to one Core key. One source token may map to different keys in different modules.
- Tools adopt the shared palette in the two steps below. An accent is one color expressed as seven `NfcAccent*` keys; a tool sets all seven.
- Each module document lists its source-to-Core mapping and every literal under "Known differences".

The owner chose this two-step adoption on 2026-10-07. It replaces the 2026-10-06 plan of zero difference first and colors later, because overrides cannot restore a tool's old look.

1. Package PR: pin the Core packages to the new version. The screens do not change.
2. Look PR, approved by the owner: adopt the Core theme, button and scroll styles in one change, and keep only the seven `NfcAccent*` keys. Heights, corner radii and focus rings change in the same PR. Attach before-and-after images in Light and Dark. Do not add Core variants to restore an old look.

## State management

Owner rule of 2026-10-07: 「請評估這些內部狀態是否必要以及是否可以用更結構化的方式去組織，而不是無腦堆疊」 ("Check whether each internal state is needed and whether it can be organized in a more structured way, instead of piling it up.")

These rules apply to C# code in Core, NFC, NFH and NFU. New code follows them now. Reviews check them; see [Review and approval](../agents/review.md).

1. Store one fact once. Compute a value that other state determines with a get-only property or a pure function, and raise its change notification with the source. Do not store `HasX` or `XCount` next to `X`.
2. Fields that are always set and cleared together belong in one record or child view model. Replace the whole value at once.
3. Two or more flags that describe one lifecycle or mode become one enum or a closed record hierarchy. Each method checks that the current phase allows the operation.
4. Represent temporary state with a nullable object; null means none. Examples are an open dialog, a drag in progress and a confirmation prompt. Do not use a flag plus loose fields.
5. Event-suppression flags must nest. Use a counter or a `using` scope inside `try`/`finally`. A flag must not span an `await`; use an explicit mode state instead.
6. Each cache has one invalidation point, such as a revision number. Do not clear it from several files.
7. Each piece of state has one owner. A view reads its view model and keeps no copy. Bind a setting in one place; do not push it into a control and read it back.
8. Check stale asynchronous results with one shared generation helper. Do not add a separate counter for each operation.
9. Route every user action through a command, so shortcuts and menus share the same running and `CanExecute` checks.
10. A partial file that needs its own state fields shows a missing type. Extract that state into its own class.
11. Document what protects each mutable field: a named lock, or UI-thread-only access.

Bugs found against these rules are tracked as GitHub issues. Core fixes its own before 1.0.0.

### When to group state into one type

Owner request of 2026-10-07: 「另外提出狀態變數管理方法論 什麼時候該整合成一個 data class」 ("Also propose a method for managing state variables: when to combine them into one data class.")

Ask these questions for each field, from top to bottom. Stop at the first "yes".

1. **Can it be computed?** Delete the field and compute the value with a get-only property. Do not group it.
2. **Are the flags mutually exclusive?** Replace them with one enum.
3. **Do the fields appear and disappear together?** Examples are a dialog request, a drag in progress and a confirmation prompt. Use one nullable record; null means none.
4. **Do the fields always change together, or come from one source together?** Use one record. Replace it whole or with `with`.
5. **Does the user edit them one by one, but they belong to one panel?** Use a child view model, not a record.
6. Otherwise, keep a separate field.

Group fields when any of these signals holds:

- **Written together.** Every place that writes A also writes B. Search all assignments to check.
- **Valid together.** A without B is an invalid state.
- **Passed together.** A and B travel as arguments to two or more methods, or are copied from one projection together.
- **Bound by an invariant.** For example, start ≤ end. When the invariant is "count equals list.Count", the count is computed instead (step 1).
- **Shared prefix.** Three or more fields share a prefix. The prefix is the missing type name.
- **Keyed by strings.** One set of values is told apart by string keys, such as `"top-left"`. Make a record and create one instance per key.

Do not group in these cases:

- The fields change independently and the UI binds each one. Group them in a child view model if they need grouping.
- The fields share a topic but have different lifetimes or owners.
- The fields change on every frame in a hot path, such as canvas drawing. Measure the allocation cost first; use a mutable struct if needed.

Pick the form by the case:

| Case | Form |
|---|---|
| Snapshot, computed result or loaded data | `sealed record`, replaced whole |
| Temporary state that exists or not | Nullable `record`; `null` means none |
| Mutually exclusive modes | `enum`, or a closed record hierarchy |
| A set of UI fields edited one by one | Child view model (`ObservableObject`) |

Review thresholds:

- A class with more than 30 state members needs a reason in the pull request for not splitting it.
- When two fields match a grouping signal, the reviewer decides. When three or more match, group them.

Examples in Core:

- `NumberScrubber` keeps three drag fields. They exist only during a drag, so they become one nullable `ScrubSession` (step 3). Its `_isEditing` repeats the focus state, so it is computed (step 1). See #86.
- `BackgroundJobService.CancelRequested` repeats `Status == Cancelling`. It becomes a get-only property (step 1). See #86.
- `WindowsStableRelativeWriteTree` tracks one lifecycle with six fields. They become one phase value (step 2). See #85.

## Public repository hygiene and license

Keep local paths, user names, machine names, secrets, and private repository details out of public files.
Keep product logic in its source tool. Use synthetic test data.

Every new source file starts with this header:

```csharp
// Copyright (c) 2026 Dennis Liu. All rights reserved.
```

Use the equivalent comment syntax in Python, shell, and PowerShell files.

## Host commits

Codex prepares and verifies files in the sandbox. The host makes codex commits because the sandbox cannot write a worktree's Git metadata.
