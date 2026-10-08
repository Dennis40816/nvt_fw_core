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

Applies to all C# code in NVT repositories. New code follows these rules from the first PR. Existing code is migrated in separate PRs, not inside unrelated changes.

Reviews check every pull request against the 11 rules below; see [Review and approval](../agents/review.md).

1. **Store each fact once.** If a value can be computed from other state, use a get-only property or a pure function. Raise `NotifyPropertyChangedFor` on the sources. Do not store `HasX` or `XCount` next to `X`.
2. **Group fields that are set and cleared together.** Put them in one `record` or one child ViewModel. Replace the whole object in one step.
3. **Use one enum for one mode.** If two or more `bool` fields describe the same lifecycle or mode, replace them with an `enum` or a closed `record` hierarchy. Each method checks that the current phase allows its operation.
4. **Show temporary state with a nullable object.** `null` means "not active". Examples: an open dialog, a drag in progress, a confirmation prompt. Do not use one `bool` plus several loose fields.
5. **Make suppress flags nestable.** Implement them as a counter or a `using` scope, inside `try`/`finally`. A flag must not stay set across an `await`. If it must, model it as an explicit mode.
6. **Give each cache one invalidation point.** Use a revision number, for example. Do not clear the same cache in several files.
7. **Give each piece of state one holder.** A View reads the ViewModel and keeps no copy. Bind a setting to one place. Do not push a value into a control and read it back.
8. **Check stale async results in one way.** Use one shared generation helper. Do not add a separate `long` counter for each operation.
9. **Route every user action through a `Command`.** Then shortcuts and menus share the same running-state and `CanExecute` checks.
10. **Extract a type when a partial file needs its own state.** A partial file with its own state fields means a type is missing.
11. **State who protects each mutable field.** Name the `lock`, or write "UI thread only".

### When to group state into one type

Ask the questions below for each field, from top to bottom. Stop at the first "yes".

1. **Can it be computed?** Delete the field and use a computed property. Do not combine it.
2. **Are the fields mutually exclusive?** If several `bool` fields are never true together, use one `enum`.
3. **Do the fields exist together and disappear together?** Examples: a dialog request, a drag, a confirmation prompt. Use a nullable `record`. `null` means "none".
4. **Are the fields always changed together, or copied together from one source?** Use a `record`. Change it with `with` or replace it whole.
5. **Does the user edit the fields one by one, but they belong to one panel?** Use a child ViewModel, not a `record`.
6. **None of the above.** Keep separate fields.

**Signals that mean you must combine** (any one is enough):

- **Changed together.** Every write to A also writes B. Search all assignments to check.
- **Valid together.** A state with A set and B unset is not legal.
- **Passed together.** A and B are passed as arguments to two or more methods, or copied together from one projection.
- **Invariant.** Examples: `start <= end`, or `count` equals `list.Count`. The second example belongs to question 1: compute it.
- **Common prefix.** Three or more fields share a prefix, such as `ExportPath`, `ExportFormat` and `ExportOverwrite`. The prefix is the name of the missing type.
- **String keys tell values apart.** Example: `"top-left"`. Make a `record` and create one instance per key.

**Do not combine when:**

- The fields change independently and the UI binds them one by one. If you want a group, use a child ViewModel.
- The fields share a topic but have different lifecycles or holders.
- The code is a hot path that changes every frame, such as canvas drawing. Measure the allocation cost first. If needed, use a mutable `struct`.

**Choose the form:**

| Situation | Form |
|---|---|
| Snapshot, computed result, loaded data | `sealed record`, replaced whole |
| Temporary state that is present or absent | nullable `record` (`null` means none) |
| Mutually exclusive modes | `enum`, or a closed `record` hierarchy |
| A group of UI fields that the user edits one by one | child ViewModel (`ObservableObject`) |

**Review thresholds:**

- A class with more than 30 state members needs a PR note that explains why it is not split.
- If two fields match a combine signal, the reviewer decides. If three or more fields match, combine them.

### Examples in Core


- `NumberScrubber` keeps three drag fields. They exist only during a drag, so they become one nullable `ScrubSession` (step 3). Its `_isEditing` repeats the focus state, so it is computed (step 1). See #86.
- `BackgroundJobService.CancelRequested` repeats `Status == Cancelling`. It becomes a get-only property (step 1). See #86.
- `WindowsStableRelativeWriteTree` tracks one lifecycle with six fields. They become one phase value (step 2). See #85.

## Where records live

Keep bugs, review records, images, decisions and handoff notes in their one home. See [Where project records live](repo-content-homes.md).

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
