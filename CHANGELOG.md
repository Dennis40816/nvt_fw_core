# Changelog

This file lists the changes in each Core release. The GitHub Release of each `core-v*` tag carries the same notes. Core follows SemVer; 1.x makes no breaking changes.

Each `core-v*` release ships `Nvt.Core` and `Nvt.Core.Avalonia` with the same version. `Nvt.Core.Fonts` starts at `0.1.0` with independent `core-fonts-v*` tags.

## Unreleased

### Added

- Launcher entry and first-install use cases (#243), moved from NFC. `ManagedLauncherEntryCoordinator` and `ManagedFirstInstallationExperience` live in `Nvt.Core.Launcher.Setup`, with their plan, result, progress and port types. The host supplies the adapters: candidate source, payload source, root probe and root materializer. This adds 34 public types and 254 entries to `src/Nvt.Core/PublicAPI.Unshipped.txt`. No existing API changes.

### Changed

- Licensing: the repository root `LICENSE` now states the copyright only and grants no license. 36 files (`src/Nvt.Core.Avalonia/Theme`, `src/Nvt.Core.Avalonia/Icons` and `src/Nvt.Core.Fonts/FontRoles.axaml`) carry an `SPDX-License-Identifier: MIT` header and a folder `LICENSE`. The other files stay under the grant in Part 1 of the new `LICENSE-COMPONENTS.txt`, which the packages carry as `LICENSE`. `THIRD-PARTY-NOTICES.md` is new. No C# API, resource URI or namespace changes.
- Licensing: 34 more files carry an `SPDX-License-Identifier: MIT` header in place of "All rights reserved". They are `AtomicOutput` and its tests, six `Nvt.Core.TestSupport` helpers and their tests (not `ChildProcessFixture`), the six external-process contract types and the six background-job and progress types, with their tests. Each of the eight folders has a `LICENSE` file for the scope. No C# API, namespace or behavior changes.
- LogConsole look (#251): the header is one `NfcControlHeight` row (32 DIP, was 40 DIP) with no vertical padding, and the toolbar's zoom group (zoom out, zoom value, zoom in) has no border. The toolbar stays 40 DIP. The owner asked for both on 2026-10-18. No C# API, resource key or token changes.
- `Nvt.Core.Fonts` 0.1.2 carries the new component `LICENSE` (Part 1 grant, MIT text and third-party notices) in its package. The font files, the font license texts and the C# API are the same as in 0.1.1. The release tag will be `core-fonts-v0.1.2`.

## 0.9.4 - 2026-10-14

Tag `core-v0.9.4` on commit `f6f44e02a8ff79417709a3fd8c23aa22f594f2cf` (#237).

A style addition for the NFU Settings tab list, and for any host that needs a selected control without a rounded border.
It changes no C# API and no existing role, so `PublicAPI.Shipped.txt` of every published project keeps all of its 0.9.3 entries.

### Added

- `actionGhost.flat`: a borderless (`BorderThickness` 0), rectangular `actionGhost` for selected controls that must not show a rounded border, such as a Settings tab list.
  `Button.actionGhost.flat.active` and `ToggleButton.actionGhost.flat:checked` show the accent surface and accent text with a transparent border. Hover uses the neutral selection surface and press uses the neutral pressed surface. The keyboard focus ring stays and follows the square corners.
  The bordered `ToggleButton.actionGhost:checked` pill is unchanged. This is a style addition only. No public C# API changes.

## 0.9.3 - 2026-10-14

Tag `core-v0.9.3` on commit `869644adb183df93ddbc46bff2538316e5e74404` (#232).

The Console toolbar rework that the owner chose in #226 (direction C), for the NFH adoption.
It breaks one breakpoint default and three resource names, listed below. `PublicAPI.Shipped.txt` of every published project keeps all of its 0.9.2 entries.

### Changed

- Console toolbar rework (owner direction C of #226). The toolbar has four regions: level filter, search, actions and zoom. The level filter shows one text label such as `All levels` or `Debug and above`.
  Hovering the label for 300 ms opens the six level toggles to the right on the same row. A click pins the row open. Escape, a second click, a press outside or focus leaving closes a pinned row. A hover-opened row also closes 500 ms after the pointer leaves the toolbar. Touch never opens it by hover.
  One visibility rule decides every part: it shows when the width tier allows it and the host did not hide it. The toolbar uses three width tiers: below 700 DIP (compact), 700 to 1179 DIP and from 1180 DIP (wide).
  While the row is open below 1180 DIP, the search field and the action buttons hide. A hint shows the count of hidden Error and Fatal entries, only when such entries exist.
  The search field keeps the Only matches button inside it. On is a funnel, off is a funnel with a slash, and neither draws a filled disc. Its tooltip reads `Only matches: on` or `Only matches: off`.
  A disabled console action no longer draws the shared disabled fill and ring.
- Console header and toolbar rows are 40 DIP high. The header has the title, a `Display` menu (time format and dedupe) and a `More` menu (copy selected, copy visible, save log, include time and level, clear).
  The Time and Export buttons of the header are gone.
- The level toggles use the standard focus ring.

### Added

- `ConsoleToolbar.List` and `ConsoleToolbar.CopyVisibleCommand` (public API, with their styled properties). The zoom out, zoom value, zoom in and jump to latest buttons are disabled until `List` is set. `NvtIcons.CopyAll` is a new icon (72 icons in total).
- Resource keys: `Nvt.Console.WideBreakpoint`, `Nvt.Console.ZoomValueWidth`, `Nvt.Console.Levels.All`, `Levels.AndAbove`, `Levels.FatalOnly`, `Levels.Some`, `Levels.None`, `Levels.Name`, `Levels.NameHidden`, `Levels.ErrorsHidden`,
  `Levels.Group`, `Zoom.Value`, `Zoom.Name`, `Zoom.Out`, `Zoom.In`, `Zoom.Reset`, `Jump`, `OnlyMatches.On` and `OnlyMatches.Off`.

### Breaking changes

- `Nvt.Console.NarrowBreakpoint` changes from 960 to 700. A host that overrides it keeps its own value.
- The resource keys `Nvt.Console.Export`, `Nvt.Console.Export.Name` and `Nvt.Console.Time.Name` are removed. The header no longer has a Time button or an Export button.

## 0.9.2 - 2026-10-13

Tag `core-v0.9.2` on commit `12bb7f6375e154f530caa4bdaf816707ea9839d3` (#227).

A release after the feature freeze of 0.9.0, at the request of the owner. It adds font zoom to the Console list for the NFH adoption.
No breaking change since 0.9.1. `PublicAPI.Shipped.txt` of every published project keeps all of its 0.9.1 entries.

### Added

- Console font zoom. The Console list zooms the rows from 80 to 300 percent in steps of 10 percent. Ctrl+Plus, Ctrl+Minus and Ctrl+0 change the zoom while the list has focus. Ctrl with the mouse wheel over the list zooms too.
  The zoom scales the row height, the column widths and the row font sizes. A paused list keeps its first visible row. Public API (10 entries in `Nvt.Core.Avalonia`, nine names because `Zoom` has a getter and a setter):
  `ConsoleListView.Zoom`, `ZoomProperty`, `ZoomChanged`, `ZoomIn()`, `ZoomOut()`, `ResetZoom()`, `MinimumZoom`, `MaximumZoom` and `ZoomStep`.
  The list does not store the value. A host saves it in the `ZoomChanged` handler.

## 0.9.1 - 2026-10-13

Tag `core-v0.9.1` on commit `26f4230a683ef7d300afc085502ca2121369d06f` (#222).

A release after the feature freeze of 0.9.0. It adds the interaction, accessibility and lifecycle parts of the Console that NFH needs for its K2e adoption.
No breaking change since 0.9.0. `PublicAPI.Shipped.txt` of every published project keeps all of its 0.9.0 entries.

### Added

- Console row interaction (K2c, #188). The Console list opens links through `IConsoleLinkOpener`, has a row menu, selection and copy, and takes the keyboard.
  An application supplies the link opener and the clipboard adapter. Public API (13 entries in `Nvt.Core.Avalonia`): `IConsoleLinkOpener` (`GetUnavailableReason`, `Open`),
  `IConsoleClipboard` (`SetText`), `ConsoleController.LinkOpener`, `ConsoleController.Clipboard` (init properties), `ConsoleController.CopySelectionCommand`,
  `ConsoleListView.Controller` and `ConsoleListView.ControllerProperty`.
- Console accessibility (K2d, #205). The list exposes a List peer with the realized rows, and each row a ListItem peer with selection and expand patterns.
  New resource keys: `Nvt.Console.List.Name`, `Nvt.Console.List.Row.NameFormat`, `Nvt.Console.List.Row.RepeatFormat`,
  `Nvt.Console.List.Row.LinkOneFormat` and `Nvt.Console.List.Row.LinksFormat`. It adds no public API.
- Console lifecycle and documents (K2e, #221). A hosted `ConsoleController` stops observing the log store when the last Console control detaches. It observes again from the latest snapshot when a control attaches. A page switch leaves no store subscription and no queued refresh. A controller that never had a hosted control observes until `Dispose()`, as before. The Console documents in English and Traditional Chinese list a hosting checklist and the Avalonia public API, and a guard test keeps that table in step with the public API. It adds no public API.

### Fixed

- Console scrolling repeats less work. The projection order is cached instead of copied on every paused scroll, row configuration and height geometry are reused,
  and scroll, theme change and small scrolls have measure-count bounds (#188).
- Console: following the end during a measure keeps the end, deferred delivery clears its pending state when it throws, and Resume clears ignored Pause requests (#188).
- Console: a recycled row restores its focus fallback, and the focus ring of the first and last row stays inside the viewport (#205).
- Tests: the Console scroll gate counts work (row visits, host measure passes per step and row presenter measures of one full traversal) instead of timing it against a calibration unit, so the load of a solution-level parallel run does not change it (#206, #220). The 10,000 and 20,000 row tests drop from about 4.5 minutes each to under one minute. The scale gate counts one traversal per size. The gate no longer reads a clock, so a cost per row that grows without changing a count is not caught.

## 0.9.0 - 2026-10-11

Tag `core-v0.9.0` on commit `fb18767fab03b0eaef2547dc62fdaaa3f96fef91` (#191).

Feature freeze. After this release only fixes follow until 1.0.0. The breaking changes below are all breaking changes since 0.5.0.

### Breaking changes

- ReportList: pager buttons now use the shared `actionNeutral` role.
  Status captions apply `Nvt.Font.Caption.Family`, `Nvt.Font.Caption.Size`, and `Nvt.Font.Caption.Weight` directly.
  They keep the muted text color `NfcTextMutedBrush`, so keep `Theme/ThemeTokens.axaml` loaded.
  Load `Theme/ButtonStyles.axaml` and `Nvt.Core.Fonts/FontRoles.axaml` before using the pager templates.
  Keep `Theme/ThemeTokens.axaml` loaded for the button resources.
  Remove local `semanticAction`, `secondary`, and `captionText` styles that existed only for the pager.
- `Nvt.Core` targets `net10.0` only. It no longer ships a `net8.0` assembly. NFC, NFH and NFU already target `net10.0`. The SDK pin in `global.json` moves to `10.0.303`. No source change is needed in a `net10.0` consumer.
- Launcher: `InstalledApplicationCoordinator`, `InstalledApplicationPresentation`, `InstalledApplicationInfo`, `IInstalledApplicationPresentation` and `ManagedPackageVerifier` are internal. Use `IManagedVersionRepository.VerifyPackageAsync` instead of `ManagedPackageVerifier`. Use `FileSystemManagedVersionRepository.ReadInstalledApplicationAsync` instead of `InstalledApplicationCoordinator`. The caller now supplies the exact installed admission and display values, and resolves the active version itself (`IVersionManagerStateReader.LoadAsync`). No Core consumer of these types is known. NFC references no Core type, and its own copies of the verifier and activation policy are not Core consumers.
- `RegularFileGuard.ReadUnixIdentity` is internal. Use the public `RequirePath` and `RequireOpenHandle` guards.
- `BoundedReadResult.Sha256` changes to `byte[]?`. Its positional constructor hash parameter and `Deconstruct` hash output become nullable. Guard uninitialized hashes. Successful reads retain complete hashes.
- `UndoService.TryPop` changes to `[NotNullWhen(true)] out UndoAction? action`. Use the success branch. Empty stacks still return false and null.
- `LocalJsonDocument.Options` is removed. Call `LocalJsonDocument.CreateOptions()` for an independent mutable copy. The codec uses private frozen defaults.
- `ProgressUpdate.StepText` changes to `string?`. Its positional constructor text parameter and `Deconstruct` text output become nullable. Guard absent text or apply the tool's existing presentation fallback.
- `MemoizedIndexedReadOnlyList<T>.MaterializedCount` is internal. Count factory calls in external allocation tests.
- `UiThread.IsUiThreadThatRunsALoop` is removed. Use `TryGetRunningDispatcher`, `IsCurrent`, or a local test predicate.
- `NvtCoreFonts.CjkFallback` returns a fresh `FontFallback` per access. Retrieve one instance per builder or owned font options.
- `ProcessInheritedHandle.EnvironmentVariable` changes to `string?`. Guard uninitialized bindings. Both `ProcessLaunchGate.StartContained` overloads reject defaults before callbacks or native work.
- `UpdateCatalogPackagePath.Value` changes to `string?`. Its positional constructor parameter and `Deconstruct` output become nullable. Guard raw paths. `UpdateCatalogVersionSnapshot.Create` still rejects default paths and preserves validated identities.

**RuntimeQuery cancellation.** RuntimeQuery carries cancellation through one execution handler.
Shared protocol defaults are read-only before first use.
Confirmation delivery, startup eligibility, command order, response defaults, and wire bytes remain unchanged.

The table lists changed members and new migration factories.
`Response` below means `Task<RuntimeQueryResponseEnvelope>`.
`Args` means `IReadOnlyDictionary<string, string>?`.

| Public member | Before | After |
| --- | --- | --- |
| `RuntimeQueryCommand` constructor | `Handler: Func<Args, Response>` | `Handler: Func<RuntimeQueryInvocation, Args, CancellationToken, Response>` |
| `RuntimeQueryCommand.Handler`, including get/init accessors | `Func<Args, Response>` | `Func<RuntimeQueryInvocation, Args, CancellationToken, Response>` |
| `RuntimeQueryCommand.Deconstruct` | Third output: `Func<Args, Response>` | Third output: `Func<RuntimeQueryInvocation, Args, CancellationToken, Response>` |
| `RuntimeQueryCommand.InvocationHandler`, including get/init accessors | Optional invocation-aware delegate replacing `Handler` | Removed. Supply `Handler` directly. |
| `RuntimeQueryCommand.FromArgs` | Absent | Factory accepting `Func<Args, CancellationToken, Response>` and existing startup metadata |
| `RuntimeQueryCommandRouter(handlers)` | Dictionary values: `Func<Args, Response>` | Dictionary values: `Func<Args, CancellationToken, Response>` |
| `RuntimeQueryCommandRouter.ExecuteAsync` | `(request, expectedVersion)` | `(request, expectedVersion, cancellationToken = default)` |
| `RuntimeQueryCommandRouter.RouteAsync` | `(commandText, args)` | `(commandText, args, cancellationToken = default)` |
| `RuntimeQueryCommandRouter.ExecuteStartupPhaseAsync` | `(calls, phase)` | `(calls, phase, cancellationToken = default)` |
| `RuntimeQueryProtocol.CompactJsonOptions` | Mutable before first serialization | Always read-only. Exact transport defaults remain unchanged. |
| `RuntimeQueryProtocol.PrettyJsonOptions` | Mutable before first serialization | Always read-only. Exact pretty-output defaults remain unchanged. |
| `RuntimeQueryProtocol.CreateCompactJsonOptions` | Absent | Independent mutable copy of compact defaults |
| `RuntimeQueryProtocol.CreatePrettyJsonOptions` | Absent | Independent mutable copy of pretty defaults |
| `RuntimeQueryUiThread.Wrap` | Dispatches already cancelled requests | Checks cancellation before dispatch and before queued work begins |
| `RuntimeQueryGenericCommandOptions.CaptureScreenshot` | Receives `CancellationToken.None` | Receives the invocation token. The delegate signature stays unchanged. |
| `RuntimeQueryGenericCommands.Create` | Handlers omit cancellation | Handlers check cancellation before effects and forward it to screenshot capture |
| `RuntimeQueryIpcServer.DisposeAsync` | Bounded shutdown can detach its handler wait | Bounded shutdown signals cancellation. Started handlers retain their cooperative execution and cleanup. A diagnostic event can arrive after `DisposeAsync` returns, and `Stopped` is not reported while a handler that ignores its token is still running. |

Tool migration steps:

- Update every RuntimeQuery consumer: the `DesktopRuntimeQuery` factories and the router wrappers, with the new signatures.
- Supply one invocation-aware handler, or use `RuntimeQueryCommand.FromArgs` for commands that ignore invocation timing.
- Forward the token that your entry point receives to `Router.ExecuteAsync(request, version, token)`. It comes from the IPC server handler, or from your own entry point when you have no server. Passing a startup token to `ExecuteStartupPhaseAsync` is optional. A cancel during startup throws `OperationCanceledException` and the results of commands that already ran are lost. A tool that wants the old result when the window closes during startup passes no startup token.
- Update `AppearanceLaunchCommands` and handler tests while preserving command results, confirmation delivery, and transport bytes.
- Customize JSON through the copy factories. Keep the shared options for transport and default output.
- The other two inspected consumers need no source migration.
- The integrator pins the accepted release, updates package hashes, regenerates locks, and restores in locked mode.

See the [RuntimeQuery migration examples](docs/core/modules/RuntimeQuery.md#cancellation-and-migration)
and the [Traditional Chinese examples](docs/core/modules/RuntimeQuery.zh-TW.md#取消與遷移).

Tool migration: NFH must update `CadLoadOverlayFrameYieldPolicyTests` before upgrading its Core package.
Replace the removed conjunction helper with a local predicate or real dispatcher evidence.
Rebuild its existing `UiThread` consumers.
NFC has no inspected direct calls to these changed members.
Future adoption must guard defaults and replace `Options` reads with `CreateOptions()`.
NFU has no inspected source migration.
Rebuild its CSV, AtomicOutput, and SourceFileNavigation consumers against the accepted package.
Integrators must pin the accepted release, update package-download hashes, regenerate locks, and restore in locked mode.

Inputs removes the inert large-step API from `NumberScrubber` before the public contract freeze.

| Member | Before | After |
| --- | --- | --- |
| `NumberScrubber.LargeChange` | Read/write `decimal`, default `10m`, with no input behavior. | Removed. |
| `NumberScrubber.LargeChangeProperty` | Public static readonly `StyledProperty<decimal>` identifier. | Removed. |
| `NumberScrubber.get_LargeChange()` | Public `decimal` getter. | Removed. |
| `NumberScrubber.set_LargeChange(decimal)` | Public `void` setter. | Removed. |

Remove `LargeChange` attributes, bindings, assignments, and reads.
Remove styled identifier and accessor references. Keep `SmallChange` for text snapping, wheel steps, and scrub steps.
NFC, NFH, and NFU have no inspected dependency on Core `NumberScrubber`.
NFC keeps its local large-step behavior in `HexEditorPanel` on future adoption.
Range rejection is withdrawn because binding coherence was not proved. The existing range contract remains unchanged.
Both Inputs documents describe valid bound-update order and the retained reversed-range hazard.

### Added

- Public API review: `PublicAPI.Shipped.txt` of `Nvt.Core` (1,616 entries), `Nvt.Core.Avalonia` (477), `Nvt.Core.Fonts` (3) and `Nvt.Core.TestSupport` (50) lists every public member that this release commits to. The analyzer gate (RS0016 and RS0017 as errors) is on for these four projects, so a change to a listed member needs a `*REMOVED*` line and a CHANGELOG entry. See the [API inventory](docs/core/api-inventory.md).
- LogConsole: `Nvt.Core.LogConsole` adds a bounded log store and an immutable projection with level and source filters, literal search, grouping of duplicates and time modes. `Nvt.Core.Avalonia.LogConsole` adds the controller, header, toolbar, empty state and a recycling list view that keeps the reading position. Apps own the store, the source registry and the file export ([#140](https://github.com/Dennis40816/nvt_fw_core/pull/140), [#169](https://github.com/Dennis40816/nvt_fw_core/pull/169), [#170](https://github.com/Dennis40816/nvt_fw_core/pull/170)).
- Added the public API inventory for `Nvt.Core.Avalonia` (477 entries), `Nvt.Core.Fonts` (3) and `Nvt.Core.TestSupport` (50), and switched the analyzer gate on for the four published projects. A new public member now needs an entry in `PublicAPI.Unshipped.txt`, or the build fails with RS0016.
- Added the public API inventory for the Launcher coordination, transport and repository namespaces (`PublicAPI.Unshipped.txt`, 394 entries). Every public `Nvt.Core` API is now listed. Enforcement follows in the last inventory step.
- Added the public API inventory for the Launcher contracts, persistence and activation namespaces (`PublicAPI.Unshipped.txt`, 477 entries). Enforcement follows in the last inventory step.
- `Nvt.Core.TestSupport` gains `TestFiles`: `WriteAllBytes` and `ReadLinesAsync` for plain fixture files, through streams, because the `File.ReadAll*` and `File.WriteAll*` shortcuts are banned.
- `Nvt.Core.TestSupport` gains `TaskBlock`: the one way to block on a task in a test hook that cannot await. It rethrows the original fault and does not use the caller's synchronization context.
- `Nvt.Core.TestSupport` gains `RelativePerf`: performance thresholds that follow the machine (a calibration unit, a scale ratio, an allocation count) with a warm-up and a median of at least seven samples. Testing rule 3 and convention T1 now ask for such a test on hot paths and forbid absolute wall-clock limits.
- Add Nvt.Core.TestSupport, a test-only net10.0 package sharing the Core version, with deterministic manual time, bounded temporary workspace cleanup, `SignalWait` (a signal with a watchdog that only prevents a hang), and `ChildProcessFixture` (a child process with an output limit, a watchdog and process-tree cleanup). Core Processes and Launcher Transport tests now consume the shared helpers. Deliberate behavior differences from the old Processes helpers are listed in the project README. It is not packed or published by the `core-v*` release; publishing is a follow-up.
- Threading: `UiEventRunner` observes UI event operation failures on the calling context, handles operation-token cancellation, and contains primary and fallback reporter failures. NFC can compile the canonical source as internal with `NVT_CORE_SOURCE_CONSUMPTION` and verify its LF-byte SHA-256 using `tools/source-consumption/manifest.json`.
- `Nvt.Core.Fonts` package 0.1.0: independent version and `core-fonts-v*` releases with font roles, Chinese fallback, Material Symbols, and font licenses.
- Added the public API inventory for the non-Launcher `Nvt.Core` namespaces (`PublicAPI.Unshipped.txt`, 745 entries) and the consumer table in `docs/core/api-inventory.md`. The analyzer gate follows in a later change.
- `Nvt.Core.Fonts` 0.1.1 adds the Inter font license (SIL Open Font License 1.1, version 3.19 text) as `licenses/Inter/LICENSE`. Package 0.1.0 did not carry it. The release tag is `core-fonts-v0.1.1`.

### Fixed

- Tests: the test probe's 30-second waiting modes now end when the test creates a release file, and the managed-start cleanup test runs on a manual clock. `ManagedStartDeadline` takes an internal `TimeProvider`. The scanner oracle and corpus tests run their independent cases in parallel. Production behavior and the public API do not change. Summed test time drops by about 5 minutes, and Issue #175 no longer depends on a real-time bound.
- MessageCenter: `ActivityItems`, `HasActivityItems` and `HasNoActivityItems` share one lazy provider capture and row projection per revision. Filter, debug disclosure, activity/diagnostic signals and language changes invalidate the cache before changed notifications, keeping rows and presence flags coherent while preserving notification order and observer isolation.

## 0.5.0 - 2026-10-08

No breaking changes since 0.4.0.

### New modules and features

- Theme: add FluentPill toggle roles, danger states, and shared runtime Pill/Square shapes with token-based colors and corners (#120).
- Theme: add `toggleSoft` for tonal filters with shared color tokens, runtime shapes, and keyboard focus (#124).

### Internal

- Launcher: add the four frozen handoff tests that the READY transport was missing (#121).
- Inputs and Progress: hold each drag in one ScrubSession and derive cancellation requests from job status (#122).

## 0.4.0 - 2026-10-08

Tag `core-v0.4.0` on commit `7bd42ce33eea26f3bd2eeacce958aa1cf701ccae` (#118). No breaking changes since 0.3.0.

### New modules and features

- Icons: `NvtIcons` names 68 Material Symbols glyphs, with one `Nvt.Icon.<Name>` resource each and one shared icon style (#111).
- RuntimeQuery: generic exit gains `DecideExitRequest` and `RuntimeQueryExitRequest`. `ReceivesConfirmation` lets handlers receive `--confirm` without changing legacy behavior (#115).
- RuntimeQuery: `BeforeFirstFrameAndRuntime` runs one command before the first layout and at runtime. `InvocationHandler` tells the handler which timing called it (#117).

### Tools

- `tools/repo-checks`: C# health enforcement checks fresh build/SARIF and format fingerprints, policy drift and evaluated project coverage. Core enrollment records existing debt; new occurrences fail and fixes lower the ledger.

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
