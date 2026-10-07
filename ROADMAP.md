# Roadmap

This file is the one place for Core's planned work. Status as of 2026-10-07.

The owner asked on 2026-10-07 for one roadmap per repository, with Core adoption tracked as progress: 「我認為需要重新整理各專案 Todo 規劃，也要把整合 core 納入進度，要不然很難做 hotfix」 ("I think we need to reorganize each project's to-do plan and include Core integration in the progress; otherwise hotfixes are hard to do.")

The roadmap has three lanes:

1. [Fix lane](#1-fix-lane): hotfixes on the latest release.
2. [Product features](#2-product-features): the 1.0.0 scope and the work after 1.0.0.
3. [Tool adoption](#3-tool-adoption): how far NFH, NFC and NFU use Core.

Every item names a target version, a status and a link. [components.md](docs/components.md) records where each module lives and where it came from. [CHANGELOG.md](CHANGELOG.md) records what each release shipped.

## Releases

Core follows the version rules that the owner set for every project on 2026-10-07: 「後續所有版號規則按目前所定」 ("all later version numbers follow the rules set now").

- The three tools are Core's users. Every `core-v*` release is a customer release, with a tag and a GitHub Release.
- Core follows SemVer. 1.x makes no breaking changes, so every public API is reviewed before 0.9.0.
- The owner's approval of a version pull request also approves its tag. The tag points to the commit that merges that pull request.
- A fix to a released version starts from its tag. See the [fix lane](#1-fix-lane).
- Internal group tags are optional for Core. They never get a Release. Their format follows the shared rule once the owner decides it.

| Version | Date | Content | Status |
| --- | --- | --- | --- |
| 0.1.0 | 2026-10-06 | First modules | [Released](https://github.com/Dennis40816/nvt_fw_core/releases/tag/core-v0.1.0) |
| 0.2.0 | 2026-10-07 | Everything merged after 0.1.0 | [Released](https://github.com/Dennis40816/nvt_fw_core/releases/tag/core-v0.2.0) |
| 0.3.0 | 2026-10-07 | Everything merged after 0.2.0, released early for NFC | [Released](https://github.com/Dennis40816/nvt_fw_core/releases/tag/core-v0.3.0) |
| 0.9.0 | 2026-10-13 | Feature freeze. After it, only fixes. | Planned |
| 1.0.0 | 2026-10-15 | All three tools adopted and their screens confirmed by the owner. For NFC, the 1.3.x trunk counts. | Planned |

NFC meets the 1.0.0 condition when its 1.3.x trunk uses Core and the owner confirms its screens in a development build (owner decision 2026-10-07). Core 1.0.0 does not wait for an NFC customer release.

A tool does not need every version. It adopts with zero difference on 0.2.0, takes its new look on 0.9.0, and moves to 1.0.0 last.

## 1. Fix lane

A fix to a released version uses its own branch:

1. Create `hotfix/core-<version>` from the latest `core-v*` tag. For example, `hotfix/core-0.2.1` starts from `core-v0.2.0`.
2. Fix, review and release from that branch.
3. Merge the release back into `main`.

Feature and adoption work on `main` never blocks the fix lane.

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| No open fix on a released version | — | — | — |
| Stable write tree uses one phase value; wrong-order calls fail with `InvalidOperationException` | 0.3.0 | Merged into `main` | [#94](https://github.com/Dennis40816/nvt_fw_core/pull/94), [#85](https://github.com/Dennis40816/nvt_fw_core/issues/85) |

## 2. Product features

The groups follow the owner's eight Core groups of 2026-10-05, as in [components.md](docs/components.md#components).

### 1.0.0 scope

The whole list is in 1.0.0. Only "basic controls, set 3" is best effort.

**1. Agent foundation documents**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| Agent workflow, dispatch, handoff and review | 1.0.0 | Merged; refine | [docs/agents](docs/agents/README.md) |

**2. Repository foundation documents**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| README, contribution rules, license | 1.0.0 | Merged | [README](README.md), [CONTRIBUTING](CONTRIBUTING.md) |
| Pull request templates | 1.0.0 | Planned | — |

**3. CI/CD workflows and tests**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| Core build, test and release workflow | 1.0.0 | Merged | [core.yml](.github/workflows/core.yml) |
| Approval checker and path guard actions | 1.0.0 | Merged; NFH pilot in progress | [actions](actions/) |
| Headless and screenshot test support | 1.0.0 | Merged | [Testing](src/Nvt.Core.Avalonia/Testing/) |
| Shared release stages for tools | After 1.0.0 | Planned | [shared CI](docs/shared-ci/README.md) |

**4. Automation scripts**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| Core package download at build time | 1.0.0 | Merged | [#61](https://github.com/Dennis40816/nvt_fw_core/pull/61) |
| Doc-sync and handoff checks | 1.0.0 | Merged; warning mode | [#72](https://github.com/Dennis40816/nvt_fw_core/pull/72) |
| Codex dispatch queue and Windows scheduler | 1.0.0 | Merged | [tools](tools/) |

**5. Shared UI classes**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| Theme: one palette and one button style file | 1.0.0 | Merged | [#74](https://github.com/Dennis40816/nvt_fw_core/pull/74) |
| Fonts with the Chinese fallback | 1.0.0 | Merged | [#47](https://github.com/Dennis40816/nvt_fw_core/pull/47) |
| Shell page host | 1.0.0 | Merged | [#77](https://github.com/Dennis40816/nvt_fw_core/pull/77) |
| Report list paging models | 1.0.0 | Merged | [#96](https://github.com/Dennis40816/nvt_fw_core/pull/96) |
| Report list paging templates | 1.0.0 | Merged | [#103](https://github.com/Dennis40816/nvt_fw_core/pull/103) |
| Message Center presentation view model | 1.0.0 | Merged | [#100](https://github.com/Dennis40816/nvt_fw_core/pull/100) |
| RuntimeQuery generic commands: help, ping, focus, page, screenshot, exit | 1.0.0 | Merged | [#102](https://github.com/Dennis40816/nvt_fw_core/pull/102) |
| Basic controls, set 1: tooltip wrapping, text and number input, combo box, toggle switch, tab control | 1.0.0 | Planned (NFH ports) | — |
| Basic controls, set 2: toggle button | 1.0.0 | In progress; the owner picks from comparison images | — |
| Basic controls, set 3: text styles, check box, radio button, list box, expander, grid splitter, progress bar, menus | 1.0.0, best effort | Planned | — |
| Icons from the Material Symbols font | 1.0.0 | In progress | — |
| Console: redesigned shared control, adopted by NFH | 1.0.0 | Design proposal open | [#82](https://github.com/Dennis40816/nvt_fw_core/pull/82) |
| Number scrubber holds one drag session | 1.0.0 | Open | [#86](https://github.com/Dennis40816/nvt_fw_core/issues/86) |

**6. Launcher**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| State contracts, package verification, state files, coordination, custody, installation | 1.0.0 | Merged | [#60](https://github.com/Dennis40816/nvt_fw_core/pull/60), [#67](https://github.com/Dennis40816/nvt_fw_core/pull/67), [#75](https://github.com/Dennis40816/nvt_fw_core/pull/75), [#78](https://github.com/Dennis40816/nvt_fw_core/pull/78), [#84](https://github.com/Dennis40816/nvt_fw_core/pull/84), [#89](https://github.com/Dennis40816/nvt_fw_core/pull/89) |
| Remaining Launcher steps, including a second-app test for WorkStation | 1.0.0 | In progress (NFC) | [Launcher](docs/core/modules/Launcher.md) |

**7. Utility functions**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| Files, Processes, Locale, Persistence, IO, Csv, source-file navigation, Startup, Time, Lifecycle | 1.0.0 | Merged | [components.md](docs/components.md#7-utility-functions) |
| Process runner and its lifetime tests | 1.0.0 | Merged | [#88](https://github.com/Dennis40816/nvt_fw_core/pull/88), [#97](https://github.com/Dennis40816/nvt_fw_core/pull/97) |
| RuntimeQuery: one pipe per window and `--pid` | 1.0.0 | Merged | [#99](https://github.com/Dennis40816/nvt_fw_core/pull/99) |

**8. Other shared items**

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| Background job lifecycle and progress | 1.0.0 | Merged | [Progress](docs/core/modules/Progress.md) |
| Test probe for process tests | 1.0.0 | Merged | [Test probe](tests/Nvt.Core.TestProbe/README.md) |
| Core-linked test child for Launcher process tests | 1.0.0 | Merged | [#105](https://github.com/Dennis40816/nvt_fw_core/pull/105) |
| GitHub App module with the review ledger | 1.0.0 | Merged | [#104](https://github.com/Dennis40816/nvt_fw_core/pull/104), [#108](https://github.com/Dennis40816/nvt_fw_core/pull/108) |
| Public API review before 0.9.0 | 0.9.0 | In progress | — |

### After 1.0.0

| Item | Target | Status | Link |
| --- | --- | --- | --- |
| `Nvt.Core.Fonts` with its own version and tag | 1.x | Planned | [CHANGELOG](CHANGELOG.md) |
| NFH UI candidates, extracted when a second tool needs one | 1.x | Recorded | [components.md](docs/components.md#nfh-candidates-extract-when-a-second-tool-needs-them) |
| Log entry and formatter | 1.x | Planned with the console | [components.md](docs/components.md#7-utility-functions) |
| Shared release stages for tools | 1.x | Planned | [shared CI](docs/shared-ci/README.md) |
| Breaking shell integration | 2.0 | Deferred by the owner | [components.md](docs/components.md) |

## 3. Tool adoption

Each tool adopts Core in its own pull requests. A theme change uses two steps: a package pull request with no visual change, then one look pull request with Light and Dark images that the owner approves.

Each tool's own roadmap is the source for its progress. This table summarizes it.

| Tool | Release line | Core version | Modules in use | Theme step 1 | Theme step 2 | Next |
| --- | --- | --- | --- | --- | --- | --- |
| NFH (Freeform Helper) | `1.3.x` | 0.2.0 ([NFH #50](https://github.com/Dennis40816/nvt-freeform-helper/pull/50)) | UI-thread helper ([NFH #44](https://github.com/Dennis40816/nvt-freeform-helper/pull/44)) | Done ([NFH #50](https://github.com/Dennis40816/nvt-freeform-helper/pull/50)) | Not started; after NFC and NFU | RuntimeQuery switch with zero difference, then the Console |
| NFC (NVT FW Combiner) | `1.3.x` trunk. The `1.3.0` release branch ships without Core. After 1.3.0, Core adoption and features mix on the trunk and go into the next customer release in finishing order. The first customer release with Core also changes the license. `1.2.x` takes Core-free hotfixes only. | 0.2.0 on the trunk ([NFC #580](https://github.com/Dennis40816/nvt_fw_combiner/pull/580)), used only to download and verify the packages at build time; 0.3.0 on a RuntimeQuery branch, not merged | None yet | Done ([NFC #580](https://github.com/Dennis40816/nvt_fw_combiner/pull/580)) | Not started | RuntimeQuery read-only commands in review ([NFC #581](https://github.com/Dennis40816/nvt_fw_combiner/pull/581)); the 16 startup options as Core commands and the generic commands with one pipe per window, both in progress; then Launcher steps L09 to L12 |
| NFU (NVT FW UTIL) | Released `v0.1.1`; development line `0.2.0` ([roadmap](https://github.com/Dennis40816/nvt-event-buffer-replay/pull/41)) | 0.2.0 ([NFU #40](https://github.com/Dennis40816/nvt-event-buffer-replay/pull/40)) | Atomic output ([NFU #34](https://github.com/Dennis40816/nvt-event-buffer-replay/pull/34)), CSV quoting ([NFU #35](https://github.com/Dennis40816/nvt-event-buffer-replay/pull/35)), source-file navigation ([NFU #36](https://github.com/Dennis40816/nvt-event-buffer-replay/pull/36)) | Done ([NFU #40](https://github.com/Dennis40816/nvt-event-buffer-replay/pull/40)) | In progress | Theme step 2, then the move to Core 1.0.0 |

Each tool keeps its own fix lane. Core adoption on a tool's development line never blocks that tool's hotfix. For example, NFC 1.2.2 starts from `v1.2.1` without Core and ships to customers as usual. NT51925 support goes into both 1.2.2 and 1.3.0. Core adoption starts after 1.3.0 on the 1.3.x trunk.

Each tool session updates its own row in the same pull request that changes its adoption.
