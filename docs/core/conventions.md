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

Core styles use only the `Nfc*` keys in `src/Nvt.Core.Avalonia/Theme/`. Pick each key by the role of its use, not by the source tool's token name.

- Use Core's existing state pairs for hover and pressed. For example, `Button.secondary` uses `NfcAccentSurfaceBrush` for hover and `NfcSecondaryActionPressedBrush` for pressed.
- Several source tokens may map to one Core key. One source token may map to different keys in different modules.
- Tools adopt Core's colors. Do not override `Nfc*` keys with a tool's own values at application scope.
- Each module document lists its source-to-Core mapping and every literal under "Known differences".

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
