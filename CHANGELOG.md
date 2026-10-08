# Changelog

This file lists the changes in each Core release. The GitHub Release of each `core-v*` tag carries the same notes. Core follows SemVer; 1.x makes no breaking changes.

Each release ships `Nvt.Core` and `Nvt.Core.Avalonia` with the same version. `Nvt.Core.Fonts` gets its own version and tag later.

## Unreleased

### New modules and features

- Theme: add `toggleSoft` for tonal filters with shared color tokens, runtime shapes, and keyboard focus.
- Theme: add FluentPill toggle roles, danger states, and shared runtime Pill/Square shapes with token-based colors and corners.

### Internal

- Inputs and Progress: hold each drag in one ScrubSession and derive cancellation requests from job status (#86).

## 0.4.0 - 2026-10-08

Tag `core-v0.4.0` on commit `7bd42ce33eea26f3bd2eeacce958aa1cf701ccae` (#118). No breaking changes since 0.3.0.

### New modules and features

- Icons: `NvtIcons` names 68 Material Symbols glyphs, with one `Nvt.Icon.<Name>` resource each and one shared icon style (#111).
- RuntimeQuery: generic exit gains `DecideExitRequest` and `RuntimeQueryExitRequest`. `ReceivesConfirmation` lets handlers receive `--confirm` without changing legacy behavior (#115).
- RuntimeQuery: `BeforeFirstFrameAndRuntime` runs one command before the first layout and at runtime. `InvocationHandler` tells the handler which timing called it (#117).

### Tools

- `tools/gh-app`: the review ledger records the head sent to the owner. Merges check that the owner's approval came after it, and that any later changes come only from clean merges of the base branch (#108).
- `tools/gh-app`: reads retry up to three attempts on unknown, 429 and 5xx failures. Writes never retry, except for the App token request, which runs before gh starts. `Push-GhAppBranch` refuses a `LocalBase` that HEAD does not contain (#116).
- `tools/nvt-sched`: tasks run under `conhost.exe --headless`, so no terminal window opens. `list`, `status` and the audit read run results from the runner state, because the headless console hides exit codes (#114).

### Docs

- `ROADMAP.md` records 0.3.0, the version rules and the NFC 1.0.0 condition (#110).

## 0.3.0 - 2026-10-07

Tag `core-v0.3.0` on commit `dfdf61c461a3132c120d99f3bd41b5abc45d45bf` (#109). No breaking changes since 0.2.0.

### Dependencies

- `Nvt.Core.Avalonia` depends on `CommunityToolkit.Mvvm` 8.4.2, the version NFC uses (#91).

### New modules and features

- Shell: page host helpers (#77).
- Files and Launcher: stable Windows read and launch custody (#78).
- Message Center: refresh coordinator (#81), export workflow (#83).
- Launcher: launch and change coordination (#84), installation and inventory (#89).
- Processes: external process runner with bounded cleanup (#88).
- ReportList: windowed and load-more paging models with host-supplied labels (#96), and the two pager templates (#103).
- Message Center: Avalonia presentation view model (#100).
- RuntimeQuery: one pipe per window and `--pid` (#99), and generic commands for Avalonia tools: help, ping, focus, page, screenshot and exit (#102).

### Fixes

- Files: the stable write tree uses one phase value. Wrong-order calls fail with `InvalidOperationException` instead of an unrelated exception (#94, issue #85).

### Tests

- RuntimeQuery: the disconnected-peer client test reads one byte before it disconnects, so it no longer fails under load (#80).
- Test probe: `orphan-chain-exit` mode (#95).
- Processes: 25 of NFC's 26 runner lifetime tests (#97).
- Core-linked test child for Launcher process tests, with a self-check mode (#105).

### Tools

- `tools/gh-app`: one PowerShell module for every GitHub write as the repository's GitHub App (#104).

### Docs

- Theme adoption uses two steps: a package PR with no visual change, then one look PR approved by the owner.
- Conventions gain "State management".
- State management gains "When to group state into one type".
- This changelog.
- Message Center documents the refresh coordinator (#90). Shell records its boundary and the seams NFC keeps (#92).
- `ROADMAP.md` holds the fix lane, product features and tool adoption.

## 0.2.0 - 2026-10-07

Tag `core-v0.2.0` on commit `15562adf08fbda5fedea93220720cc8885b053f4` (#79).

### Breaking changes

- `PackageVerificationLimits` gains a seventh required constructor parameter, `MaximumInstalledDirectories` (#67).
- Button roles: the global `Button` rule and the 0.1.0 role classes are removed. Roles use NFH's names, such as `actionPrimary`. "Breaking changes since core-v0.1.0" in `docs/core/modules/Theme.md` lists every removed class and its replacement (#74).

### New modules and features

- RuntimeQuery: pipe limited to the same user (#58), command router and argument helpers (#59), risk levels and the `--confirm` guard (#64), UI-thread step and server host (#65), query command line (#69), startup entry (#70).
- Launcher: activation state contracts (#60), bounded package verification (#67), durable state files and the exact writer lease (#75).
- Processes: contained process launch gate (#76).
- Fonts project with font roles and the Chinese fallback (#47), test probe for process lifetime tests (#48), rooted path and regular-file guards (#49), process contracts and bounded output reader (#50), common text (#51), report lists (#52), UI resource resolver (#53), navigation history (#54), Message Center display contract (#57), progress UI and loading surface (#46).
- Theme: shared scroll styles (#68), one shared palette and one button style file (#74).
- Tools: build-time Core package download (#61), doc-sync and handoff checks (#72).

### Docs

- #43, #55, #56, #66, #73.

## 0.1.0 - 2026-10-06

Tag `core-v0.1.0` on commit `c9774d4c16e33463db980208f707dc285cb27cc9`. First release.

- Library skeleton and packaging on tags (#12, #35). Avalonia 12.1.1 (#40).
- Theme, Focus and IO (#13), NFC's eight legacy font resources (#17).
- Lifecycle: CoalescedRefresh and UndoService (#14).
- Persistence (#15), startup trace (#16), Time (#34).
- RuntimeQuery named-pipe JSON transport (#18).
- Testing: headless Avalonia test host (#19).
- Files: bounded stream read (#20). IO: `AtomicOutput.WriteBytesAsync` (#45).
- Threading: UiThread dispatcher registry (#21).
- Primitives (#22), NumberScrubber input (#28), collapsible panel and workspace shell (#30), confirm and warning dialogs (#31).
- Csv quoting (#36), source file navigation (#37).
- Progress data, background job service and progress throttle (#39), loading scope coordinator (#44).
- Launcher contracts (#41).
- Shared CI: approval check (#1, #23, #25, #38), path guard (#24), repository checkers (#26), codex dispatch queue and scheduler (#33).
