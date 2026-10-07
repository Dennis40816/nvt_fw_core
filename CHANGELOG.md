# Changelog

This file lists the changes in each Core release. The GitHub Release of each `core-v*` tag carries the same notes. Core follows SemVer; 1.x makes no breaking changes.

Each release ships `Nvt.Core` and `Nvt.Core.Avalonia` with the same version. `Nvt.Core.Fonts` gets its own version and tag later.

## Unreleased

### New modules and features

- Shell: page host helpers (#77).
- Files and Launcher: stable Windows read and launch custody (#78).
- Message Center: refresh coordinator (#81).

### Tests

- RuntimeQuery: the disconnected-peer client test reads one byte before it disconnects, so it no longer fails under load (#80).

### Docs

- Theme adoption uses two steps: a package PR with no visual change, then one look PR approved by the owner.
- Conventions gain "State management".
- This changelog.

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
