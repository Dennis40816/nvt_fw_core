# Core module plan

Complete the shared module imports and refinement by 2026-10-18. From 10-19, tools develop shared features in Core.

NFC means NVT FW Combiner. NFH means Freeform Helper. NFU means NVT FW UTIL. Commander coordinates the sessions and records owner decisions.

This plan combines the module inventory with the accelerated assignments. NVT CORE integrates the work and maintains shared configuration.

## Roadmap

Dates, versions and the status of planned work are in [ROADMAP.md](../ROADMAP.md). This file records where each module lives and where it came from.

## Components

The owner defined the eight groups at 2026-10-05 21:2x:

「我認為 NVT Core 應該包含的項目至少有 1. Agent 相關的基礎文件 (涵蓋 agent workflow, e.g., commander... ) 2. repo 基礎文件 3. CI/CD workflow & test 4. 自動化 scripts 5. 共用 UI class」 ("I think NVT Core should contain at least: 1. Agent foundation documents, including agent workflows such as commander; 2. Repository foundation documents; 3. CI/CD workflows and tests; 4. Automation scripts; 5. Shared UI classes.")

「6. launcher 7. util function 8. 其他的你補充歸納」 ("6. Launcher; 7. Utility functions; 8. Other items for you to classify.")

Folder targets are planned destinations. Statuses record inventory and dispatch evidence, not accepted imports.
Stage `1 → 2` means import in stage 1 and refine in stage 2.
After acceptance, record the source ref and commit, Core version, and adopting tool versions here.

### 1. Agent foundation documents

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Agent workflow: roles, questions, dispatch, handoff, and review | Commander; NVT Core | NVT CORE | Documents | `docs/agents/` | 1 → 2 | Pull request open. |

### 2. Repository foundation documents

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| README, contribution rules, and pull request templates | NFC; NVT Core | NVT CORE | Documents | Repository root; `.github/` | 1 → 2 | Contribution rules exist; canonical links and templates planned. |
| License and security baseline | NFC; NFH | NVT CORE | Documents | Repository root | 1 → 2 | License decision recorded; baseline work planned. |

### 3. CI/CD workflows and tests

For shared-CI progress, use the [single status table](shared-ci/README.md#current-status-2026-10-05).

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Approval checker and NFH pilot | NVT Core; NFU | NVT CORE | Shared action | `actions/approval-check/` | 1 → 2 | Checker `v0.1.0` released; NFH pilot in progress. |
| CI path guard | NVT Core | NVT CORE | Shared action | `actions/path-guard/` | 1 → 2 | Merged into `main`; NFH calibration pending. |
| Approval carryover | NVT Core | NVT CORE | Shared action | `actions/approval-check/` | 1 → 2 | Merged into `main`; disabled by default. |
| Headless and screenshot test support | NFC | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Testing/` | 1 → 2 | Pull request open; tool baselines stay in each tool. |
| Candidate, package, and smoke release stages | NFC; NFU | NVT CORE | Shared CI | `.github/workflows/` | 1 → 2 | Planned; tool manifests and release gates stay local to each repository. |

### 4. Automation scripts

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Codex dispatch queue | Commander; NVT Core | NVT CORE | Tool scripts | `tools/codex-queue/` | 1 → 2 | Merged into `main`; uses Codex defaults for model and effort. |
| Windows scheduler | Commander; NVT Core | NVT CORE | Tool scripts | `tools/nvt-sched/` | 1 → 2 | Merged into `main`; the owner registered the task on a real machine. |
| Repository checker engines | NFC; NFH; NFU | NVT CORE | Checker scripts | `tools/repo-checks/` | 1 → 2 | Merged into `main` with NFC's engines; tools supply their own policy values. |
| GitHub App writes | NVT Core; NFC | NVT CORE | PowerShell module | `tools/gh-app/` | 1 → 2 | Module added with offline tests; repository adoption follows separately. |

### 5. Shared UI classes

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Theme tokens and button states | NFC | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Theme/` | 1 → 2 | Merged into `main`; NFC adopts first. |
| Font set | NFC baseline; Core role table | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Theme/` | 1 → 2 | Role table decided; NFC's legacy font resources in an open pull request. |
| Shared icon names and style | Core | Core | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Icons/` | 1 → 2 | Shared [icon names and style](core/modules/Icons.md) added. Applications adopt separately. |
| Reveal focus and tooltips | NFC | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Focus/` | 1 → 2 | Merged into `main`; NFC adopts first. |
| Loading, progress, and cancellation surface | NFC; NFH; NFU | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Progress/` | 1 → 2 | Part of the Progress module; UI controls follow the data and job tasks. NFC adopts first. |
| Cards, dialogs, and input controls | NFH | NFH | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Primitives/`; `src/Nvt.Core.Avalonia/Inputs/`; `src/Nvt.Core.Avalonia/Panels/`; `src/Nvt.Core.Avalonia/Dialogs/` | 1 → 2 | Pull requests open; NFH adoption follows its Avalonia 12 upgrade. |
| Message Center and diagnostic presentation | NFC | NFC | `Nvt.Core`; `Nvt.Core.Avalonia` | `src/Nvt.Core/MessageCenter/`; `src/Nvt.Core.Avalonia/MessageCenter/` | 2 | Display contract and session, refresh coordinator, and export workflow (M01–M03) merged into `main`; the [presentation view model](core/modules/MessageCenter.md) (M04) is in `Nvt.Core.Avalonia`; product providers, reports, and export destinations stay in NFC. |
| Report lists and history presentation | NFC | NFC | `Nvt.Core`; `Nvt.Core.Avalonia` | `src/Nvt.Core/ReportList/`; `src/Nvt.Core.Avalonia/ReportList/` | 1 → 2 | Indexed read-only lists merged into `main`; the windowed and load-more [paging models](core/modules/ReportList.md) with `ReportListLabels` in an open pull request; product schemas and export policy stay in NFC. |
| Console | NFH | NFH | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Console/` | 2 | NFH trial first; full shared import planned by 10-18. |
| Shell, navigation, and workspace | NFC | NFC | `Nvt.Core`; `Nvt.Core.Avalonia` | `src/Nvt.Core/Shell/`; `src/Nvt.Core.Avalonia/Shell/` | 2 | Navigation history (#54) and the page host helpers (#77) merged into `main`; guards and shortcuts stay in NFC. |
| Localization and accessibility resources | NFC | NFC | `Nvt.Core` | `src/Nvt.Core/Locale/` | 1 → 2 | Common text merged into `main`; only common strings are shared. |

### 6. Launcher

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Startup, update, package verification, activation, recovery, and rollback | NFC | NFC | `Nvt.Core` (contracts); proposed `Nvt.Core.Launcher` (full import) | `src/Nvt.Core/Launcher/`; proposed `src/Nvt.Core.Launcher/` | 1 boundary; 2 full import | Contracts merged into `main`. The full import is planned. Its acceptance requires verification, recovery, and rollback evidence. |

### 7. Utility functions

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| JSON/settings codec and latest-save coordinator | NFC | NVT CORE | `Nvt.Core` | `src/Nvt.Core/Persistence/` | 1 → 2 | Merged into `main`; NFC adopts first. |
| Atomic stream output | NFU | NVT CORE | `Nvt.Core` | `src/Nvt.Core/IO/` | 1 → 2 | Merged into `main`; NFU adopts in its next 0.2.x patch. |
| File path guard, bounded reads, and hashing | NFC | NFC | `Nvt.Core` | `src/Nvt.Core/Files/` | 1 → 2 | Bounded read in an open pull request; path guard and hashing planned. |
| Process execution and containment | NFC | NFC | `Nvt.Core` | `src/Nvt.Core/Processes/` | 1 → 2 | Bounded output reader merged into `main`; execution, containment, and executable trust policy stay in each tool. |
| UTC clock and startup tracing | NFC | NFC | `Nvt.Core` | `src/Nvt.Core/Startup/`; `src/Nvt.Core/Time/` | 1 → 2 | Startup trace merged into `main`; clock in an open pull request. |
| Log entry and formatter | NFH | NFH | `Nvt.Core` | `src/Nvt.Core/Diagnostics/` | 2 | Planned with the console trial. |
| Coalesced refresh, undo, and UI dispatch | NFH | NFH | `Nvt.Core`; `Nvt.Core.Avalonia` | `src/Nvt.Core/Lifecycle/`; `src/Nvt.Core.Avalonia/Threading/` | 1 → 2 | Refresh and undo merged into `main`; UI dispatch in an open pull request. |
| CSV quoting | NFU | NFU | `Nvt.Core` | `src/Nvt.Core/Csv/` | 1 → 2 | Pull request open; replay columns stay in NFU. |
| Source-file navigation | NFU | NFU | `Nvt.Core` | `src/Nvt.Core/SourceFileNavigation/` | 1 → 2 | Pull request open; the tool supplies editor and operating-system policy. |
| Universal result/validation framework | No shared source | NVT CORE | None | None | 0; excluded | No common implementation; retain product result types and built-in validation. |

### 8. Other shared candidates

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Background job lifecycle | NFU | NVT CORE | `Nvt.Core` | `src/Nvt.Core/Progress/` | 1 → 2 | Part of the Progress module (owner, 2026-10-06); extraction in progress. |
| Runtime Query automation transport and envelope | NFH | NFH | `Nvt.Core`; `Nvt.Core.Avalonia` | `src/Nvt.Core/RuntimeQuery/`; `src/Nvt.Core.Avalonia/RuntimeQuery/` | 1 → 2 | Transport, pipe security, command router, `--confirm` guard, command line and startup entry merged into `main`; the UI-thread step and server host are in `Nvt.Core.Avalonia`. Tool commands stay in each tool. |

## NFH candidates (extract when a second tool needs them)

The owner decided on 2026-10-06 to record these NFH UI parts and to extract one only when a second tool needs it.
Source: `Dennis40816/nvt-freeform-helper` at `4df72911867ad047b3217195d12223038a5781b7`. Paths are relative to that repository.

| # | Component | NFH source (lines) | Dependencies | Before extraction |
|---|---|---|---|---|
| 1 | Tooltip text wrapping: string tooltips become a wrapping, themed TextBlock | `src/FreeformHelper.UI/Services/SharedToolTipStyleService.cs` (48); ToolTip rules in `src/FreeformHelper.UI/Styles/Controls.Overlay.axaml` (55) | `UiResourceResolver`; hard-coded keys `BrushTooltipBackground`, `BrushTooltipBorder`, `BrushTooltipForeground` | Extract `UiResourceResolver` first, after the NFH Avalonia 12 upgrade. Map the three keys to Core tokens. Core Focus already has `FocusToolTipBehavior` for opening, so add wrapping next to it. Port the NFH static style check in `UiLayoutGuardTests` and the headless tooltip-open smoke test. |
| 2 | Out-of-process loading spinner: a second process animates a topmost window while the UI thread is blocked | `src/FreeformHelper.UI/Services/CadLoadSpinnerProcessHost.cs` (632), `CadLoadSpinnerDebugState.cs` (174), `CadLoadSpinnerStartupContext.cs` (88), `CadLoadSpinnerHostService.cs` (44), `CadLoadSpinnerIpc.cs` (28); `src/FreeformHelper.UI/Views/CadLoadSpinnerWindow.axaml` (37) and `.axaml.cs` (307); `tests/FreeformHelper.Tests/UI/Services/CadLoadSpinnerProcessHostTests.cs` (137) | Hooks in `Program.cs` and `App.axaml.cs`; the `--cad-load-spinner` argument; the pipe name `freeformhelper.cadloadspinner.{pid}`; three `user32` imports (Windows only); `LoadingSpinner` | First decide whether the second tool should stop blocking its UI thread instead. If not, make the argument and pipe names parameters, drop the CAD wording and keep it Windows-only. |
| 3 | Hover-open, click-to-pin menu: closes on an outside click or window deactivation, and stays inside the window | `src/FreeformHelper.UI/Controls/WorkspaceHeader.axaml.cs` (about 180 of 303 lines); `src/FreeformHelper.UI/ViewModels/WorkspaceHeaderItem.cs` (96) | `WorkspaceHeader.axaml` binds NFH values (`CadSelectionText`, `PadInspector*`); CommunityToolkit.Mvvm in the item view model | Move the popup logic out of `WorkspaceHeader` into a behavior. The item view model already has no NFH types. Extract from the Avalonia 12 version, which replaces the ToggleButton `Checked` and `Unchecked` events with `IsCheckedChanged`. |
| 4 | Text-entry focus guard: global shortcuts are skipped while a text-entry control has focus | `src/FreeformHelper.UI/Views/FreeformHelperView.InputAndShortcuts.cs` lines 192-239 of 355 (`ShouldClearFocus`, `IsTextEntryControlSource`) | Hard-coded types: `TextBox`, AvaloniaEdit `TextEditor`, `ComboBox`, `NumberScrubber`, `ConsolePanel`, `PadCanvas` | Take the extra control types as a parameter. Compare with NFC's view-model flag `IsTextEntryFocused` and pick one approach. |
| 5 | Top-right toast: `WindowNotificationManager`, at most 3 items, 2.5 s | `src/FreeformHelper.UI/MainWindow.axaml.cs` (`ShowTopToast`, about 15 of 111 lines) | The title "Freeform Helper" is hard-coded; `UiLayoutGuardTests` checks the method signature as text | Make the title a parameter. Wait for a second consumer, because NFC does not use `WindowNotificationManager`. |
| 6 | Style gallery page and UI rule documents | `src/FreeformHelper.UI/Views/DevView.axaml` (868), `DevView.axaml.cs` (93); `src/FreeformHelper.UI/ViewModels/DevViewModel.cs` (77); `docs/guides/ui-action-role-system.md` (284), `ui-density-token-rules.md` (76), `ui-action-role-visual-qa.md` (45) | NFH class names, NFH sample data, NFH-internal codes in the documents | Rebuild the gallery from Core styles instead of moving the NFH page. Rewrite the documents in tool-neutral terms. This depends on the action-role button styles, which follow the NFH Avalonia 12 upgrade. |
| 7 | Form and tab styles: TextBox, ComboBox, CheckBox, ToggleSwitch, NumericUpDown, TabItem and toggle tabs | `src/FreeformHelper.UI/Styles/Controls.Form.axaml` (213), `Controls.Tab.axaml` (205) | NFH-specific classes mixed in (`boundLayerSelector`, `regularLayerSelector`, `rightPanelTab`, `numberScrubber`, `settingsPage`); NFH tokens; Fluent template parts | Split the NFH-specific classes out first. Compare with NFC's `MainWindowControlStyles.axaml`, which overlaps. |
| 8 | Rendered snapshot comparison: render to BGRA, 16x16 average hash, Hamming distance; modes Check, DryRun and Apply | `tests/FreeformHelper.Tests/UI/Snapshots/UiRenderedVisualSnapshotTests.cs` (271, about 110 generic), `UiBaselineUpdateMode.cs` (29) | NFH baseline JSON, NFH views, the `FH_UI_BASELINE_MODE` variable, `TestPaths.RepoRoot` | Apply mode refreshes baselines, which Core adoption evidence forbids, so remove or lock it. Compare with NFC's exact pixel capture (`DesktopScreenshotCapture`). |
| 9 | XAML hard-coded color check: a tree scan for inline hex colors outside the token file, and for `Width` bound to `Bounds.Width` | `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs` (1,245 lines; about 90 lines in the generic tests `ViewsAndControls_DoNotUseInlineHexColors`, `StylesOutsideTokens_DoNotUseInlineHexColors`, `Xaml_DoesNotBindScrollContentToBoundsWidth`) | Hard-coded paths `src/FreeformHelper.UI` and `Tokens.axaml`; `TestPaths` | Make the scanned folders and the token file parameters. Compare with NFC's `XamlControlStyleContractTests`, which checks hex colors per file. |

## Import acceptance criteria

All five points are required:

1. The module enters `nvt_fw_core` through an owner-approved pull request.
2. Core builds and tests pass, verified locally first.
3. At least one tool adopts Core, deletes its own copy, and passes its existing tests. NFC UI snapshots must show zero difference. NFH and NFU UI snapshots may differ without owner approval. The pull request attaches before/after images as a record. Non-UI output, including files and data, must show zero difference in every tool.
4. Each tool builds and releases independently, with Core bundled.
5. English and Chinese README documentation is available. This document records the source, version, and adopting tools.

Freeze the parent baseline before extraction. Compare UI snapshots under the same operating system, fonts, DPI, and theme.

## Owner decisions

### 2026-10-05 22:1x

The owner adopted the recommended answers, relayed by commander:

- NFC adopts compatible modules in its next 1.2.x patch. NFU adopts them in its next 0.2.x patch.
- NFH adopts Core in 1.3.3, after 1.3.2. It first uses `Nvt.Core` targeting .NET 8, without shared UI.
- Core's license reserves all rights to the owner. NFH code imported into Core follows the same license.
- Compatible modules can be adopted now, replacing the earlier 2.0-only gate. Breaking shell integration remains a 2.0 change.

The 22:4x accelerated plan replaces the earlier limited weekly scope. NFH evaluates Avalonia 12 now and upgrades after 1.3.2 releases.
Its assessment covers the editor, fonts, runtime, and headless tests before shared UI adoption.

### 2026-10-06: snapshots and package locks

Owner decision at 01:1x, relayed by commander: 「NFC 要求保持一致，其他沒有要求」 ("NFC must remain consistent; the others have no such requirement.")

Import acceptance point 3 applies this decision to UI snapshots. Non-UI output still requires zero difference in every tool.
The NFC font-role change below requires separate owner approval.

Owner decision at 01:1x, relayed by commander: 「repo 共同鎖定」 ("Lock dependencies across the repositories.")

Every repository enables NuGet package lock files. CI always restores with `dotnet restore --locked-mode`.
Shared package versions align with Core's `Directory.Packages.props`. NVT CORE maintains that version list.

### 2026-10-06 00:5x: font set

The owner decided the policy after saying:

「我覺得可以討論出一個 font set 就只能用裡面的，font set 定位要清晰，例如 title 固定用哪種」 ("I think we can agree on a font set and use only its fonts. Define each role clearly, such as a fixed font for titles.")

The shared UI row tracks the module draft. The rules below record the approved font policy.
Only these four fonts are allowed:

| Role | Font | Size |
|---|---|---|
| Caption | Inter | 11 |
| Body | Inter | 13 |
| Heading | Inter | 16 |
| Title | Inter | 24 |
| Mono and numbers | Cascadia Mono | 13 |
| Traditional Chinese fallback | Noto Sans TC | Same as the role |
| Icon | Material Symbols Outlined | 16 |

Strong roles change only the weight. All fonts are embedded at fixed versions. Windows fonts are never packaged.
NFC first extracts its existing font resources with zero difference.
It adopts the new role table in a separate pull request with before/after images and explicit owner approval.

### 2026-10-06 00:5x: Core distribution

Core ships versioned `.nupkg` files committed to each tool repository as a local feed.
The same files go to a Core GitHub Release with SHA-256 hashes.
A version number is never rebuilt with different content. Core uses its own SemVer.
`Nvt.Core` and `Nvt.Core.Avalonia` use the same version.

## Scope boundaries

Product behavior and formats stay in their tools:

- NFC retains firmware byte planning, CRC, profiles, and output policy. Its hardened firmware writer remains separate from the atomic stream helper.
- NFH retains geometry and project formats.
- NFU retains replay decoding, review semantics, data formats, and export policy.

Public Core contains generic documents, sanitized scripts, and synthetic tests.
Exclude local paths, accounts, private repository details, secrets, private datasets, and runtime queue state.
