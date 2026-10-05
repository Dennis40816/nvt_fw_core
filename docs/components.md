# Core module plan

Complete the shared module imports and refinement by 2026-10-18. From 10-19, tools develop shared features in Core.

NFC means NVT FW Combiner. NFH means Freeform Helper. NFU means NVT FW UTIL. Commander coordinates the sessions and records owner decisions.

This plan combines the module inventory with the accelerated assignments. NVT CORE integrates the work and maintains shared configuration.

## Roadmap

These are the commander roadmap stages. They differ from the shared-CI stages 0 to 5 in the [shared-CI proposal](shared-ci/proposal.md#adoption-order).

| Stage | Dates | Goal |
|---|---|---|
| 0 Inventory | Completed 2026-10-05 | Identify shared modules and sources. |
| 1 Import | 2026-10-06 to 10-11 | Import the first modules and have tools adopt them. |
| 2 Refine | 2026-10-12 to 10-18 | Finish remaining imports, refine modules, and complete acceptance. |
| 3 Develop on Core | From 2026-10-19 | Develop shared features in Core and product features in each tool. |

The accelerated scope includes every shareable inventory item, including the full Launcher, shell, console, and Message Center.
NVT CORE, NFC, NFH, and NFU work together. Codex performs an independent first review; Claude reviews that result and high-risk work.
The owner approves pull requests in batches.

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
| CI path guard | NVT Core | NVT CORE | Shared action | `actions/path-guard/` | 1 → 2 | Pull request open; NFH calibration pending. |
| Approval carryover | NVT Core | NVT CORE | Shared action | `actions/approval-check/` | 1 → 2 | Pull request open; disabled by default. |
| Headless and screenshot test support | NFC | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Testing/` | 1 → 2 | Pull request open; tool baselines stay in each tool. |
| Candidate, package, and smoke release stages | NFC; NFU | NVT CORE | Shared CI | `.github/workflows/` | 1 → 2 | Planned; tool manifests and release gates stay local to each repository. |

### 4. Automation scripts

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Codex dispatch queue | Commander; NVT Core | NVT CORE | Tool scripts | `tools/codex-queue/` | 1 → 2 | Import in progress; use Codex defaults for model and effort. |
| Windows scheduler | Commander; NVT Core | NVT CORE | Tool scripts | `tools/nvt-sched/` | 1 → 2 | Import in progress; real-machine acceptance pending. |
| Repository checker engines | NFC; NFH; NFU | NVT CORE | Checker scripts | `tools/repo-checks/` | 1 → 2 | Pull request open with NFC's engines; tools supply their own policy values. |

### 5. Shared UI classes

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Theme tokens and button states | NFC | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Theme/` | 1 → 2 | Merged into `main`; NFC adopts first. |
| Font set | NFC baseline; Core role table | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Theme/` | 1 → 2 | Role table decided; NFC's legacy font resources in an open pull request. |
| Reveal focus and tooltips | NFC | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Focus/` | 1 → 2 | Merged into `main`; NFC adopts first. |
| Loading, progress, and cancellation surface | NFC | NVT CORE | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Controls/` | 1 → 2 | Planned; NFC adopts first. |
| Cards, dialogs, and input controls | NFH | NFH | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Primitives/`; `Inputs/`; `Panels/`; `Dialogs/` | 1 → 2 | Pull requests open; NFH adoption follows its Avalonia 12 upgrade. |
| Message Center and diagnostic presentation | NFC | NFC | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/MessageCenter/` | 2 | Planned; separate presentation from product providers and reports. |
| Report lists and history presentation | NFC | NFC | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Reports/` | 1 → 2 | Planned; product schemas and export policy stay in NFC. |
| Console | NFH | NFH | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Console/` | 2 | NFH trial first; full shared import planned by 10-18. |
| Shell, navigation, and workspace | NFC | NFC | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Shell/` | 2 | Planned; retain the existing page-host boundary. |
| Localization and accessibility resources | NFC | NFC | `Nvt.Core.Avalonia` | `src/Nvt.Core.Avalonia/Resources/` | 1 → 2 | Planned; share common strings only. |

### 6. Launcher

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Startup, update, package verification, activation, recovery, and rollback | NFC | NFC | Proposed `Nvt.Core.Launcher` | `src/Nvt.Core.Launcher/` | 1 boundary; 2 full import | Planned; acceptance requires verification, recovery, and rollback evidence. |

### 7. Utility functions

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| JSON/settings codec and latest-save coordinator | NFC | NVT CORE | `Nvt.Core` | `src/Nvt.Core/Persistence/` | 1 → 2 | Pull request open; NFC adopts first. |
| Atomic stream output | NFU | NVT CORE | `Nvt.Core` | `src/Nvt.Core/IO/` | 1 → 2 | Merged into `main`; NFU adopts in its next 0.2.x patch. |
| File path guard, bounded reads, and hashing | NFC | NFC | `Nvt.Core` | `src/Nvt.Core/Files/` | 1 → 2 | Bounded read in an open pull request; path guard and hashing planned. |
| Process execution and containment | NFC | NFC | `Nvt.Core` | `src/Nvt.Core/Processes/` | 1 → 2 | Planned; executable trust policy stays in each tool. |
| UTC clock and startup tracing | NFC | NFC | `Nvt.Core` | `src/Nvt.Core/Startup/` | 1 → 2 | Startup trace in an open pull request; clock planned. |
| Log entry and formatter | NFH | NFH | `Nvt.Core` | `src/Nvt.Core/Diagnostics/` | 2 | Planned with the console trial. |
| Coalesced refresh, undo, and UI dispatch | NFH | NFH | `Nvt.Core`; `Nvt.Core.Avalonia` | `src/Nvt.Core/Lifecycle/`; `src/Nvt.Core.Avalonia/Threading/` | 1 → 2 | Refresh and undo merged into `main`; UI dispatch in an open pull request. |
| CSV quoting | NFU | NFU | `Nvt.Core` | `src/Nvt.Core/Reports/` | 1 → 2 | Planned; replay columns stay in NFU. |
| Source-file navigation | NFU | NFU | `Nvt.Core` | `src/Nvt.Core/IO/` | 1 → 2 | Planned; the tool supplies editor and operating-system policy. |
| Universal result/validation framework | No shared source | NVT CORE | None | None | 0; excluded | No common implementation; retain product result types and built-in validation. |

### 8. Other shared candidates

| Module | Source tool | Owning session | Target library | Target folder | Stage | Status |
|---|---|---|---|---|---|---|
| Background job lifecycle | NFU | NFU | `Nvt.Core` | `src/Nvt.Core/Jobs/` | 1 → 2 | Planned; share cancellation and stale-result handling without replay payloads. |
| Runtime Query automation transport and envelope | NFH | NFH | `Nvt.Core` | `src/Nvt.Core/RuntimeQuery/` | 1 → 2 | Pull request open; tool commands stay in each tool. |

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
