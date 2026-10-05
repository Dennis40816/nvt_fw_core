# Component comparison table

Purpose: During the later inventory, use this table to decide whose implementation of each component to bring into shared use. The principle is what the owner said: 「誰最好最納入共用」 ("Bring whichever is best into shared use.").

**This table is currently mostly empty.** The filled-in content comes from reading the project files (`.csproj`, `Directory.Packages.props`) of the three repositories on 2026-10-02; the UI code was not read, so only 「有哪些專案」 ("which projects exist") can be confirmed, and quality cannot be judged. 「未查」 ("Not inspected") means it has not been examined yet, not that it does not exist.

## Current platform status

| | NFC | NFH | NFU |
|---|---|---|---|
| .NET SDK | 10.0.301 | 10.0.301 | 10.0.303 |
| Avalonia | 12.0.5 | **11.3.12** (also uses AvaloniaEdit 11.4.1) | 12.1.1 |
| Project structure | Domain／Application／Infrastructure／Contracts／Platform／Profiles／Presentation.Avalonia／Desktop／Cli／Bootstrap, plus Launcher, LauncherBootstrap, DistributionLauncher, VersionManagement.Application／Infrastructure | FreeformHelper.Domain／Application／Infrastructure／UI | Nvt.Replay.Core／Sources／Formats／Analysis／Rendering／Avalonia／Cli |
| UI tests | Avalonia.Headless.XUnit | Not inspected | Avalonia.Headless.XUnit |

The Avalonia major versions differ: before shared UI components go live, NFH needs to upgrade from 11 to 12. NFH has already included this in its plan; the upgrade is not happening now.

## Components

| Component | NFC | NFH | NFU | Maturity assessment | Decision |
|---|---|---|---|---|---|
| launcher (startup, version management, updates) | Present: Launcher, LauncherBootstrap, DistributionLauncher, VersionManagement.* | Not inspected | Not inspected | Not assessed | **Apply directly after NFC develops it** (owner 2026-10-03) |
| Main UI framework (shell, navigation, tool registration) | Presentation.Avalonia, Desktop | FreeformHelper.UI | Nvt.Replay.Avalonia; a 「工具首頁」 ("tool home page") shell is being developed. MainWindow currently has 19 files and about 8,400 lines | Not assessed | Undecided |
| Themes and fonts | Avalonia.Themes.Fluent, Fonts.Inter | Not inspected | Avalonia.Themes.Fluent, Fonts.Inter | Not assessed | Undecided |
| Settings storage | Not inspected | Not inspected | Not inspected | Not assessed | Undecided |
| Diagnostics and logging | Not inspected | Has AppLogStore (learned from the PR description; code not read) | Not inspected | Not assessed | Undecided |
| About and license pages | Not inspected | Not inspected | Not inspected | Not assessed | Undecided |
| CLI conventions | NvtFwCombiner.Cli | Not inspected | Nvt.Replay.Cli | Not assessed | Undecided |
| Release and packaging | Not inspected | Not inspected | Has package and release scripts (learned from the session report) | Not assessed | Undecided |
| console system | Not inspected | **Try out the new architecture here first** (owner 2026-10-04) | Not inspected | Not assessed | **After NFH's trial implementation matures, push it to nvt_fw_core as the shared baseline**; other modules will later follow the same pattern, abstracted into shared structures one by one and then integrated (owner 2026-10-04). Timing: 「之後」 ("later") |
| codex task dispatch tools (queuing, dispatch, review, wrap-up) | Present: `lane.sh`, `dispatch.ps1`, `creview.sh`, `vq.sh` | Present (queuing and wrap-up scripts) | Present (not inspected) | Not assessed | **Bring into nvt_fw_core later**; do not hardcode the model or effort, use the codex defaults (`~/.codex/config.toml`, adjusted periodically), with the dispatcher specifying them for individual tasks (owner 2026-10-03). Timing: 「之後」 ("later"), not scheduled in stages 0–2 of shared CI |

## How to assess maturity

The following is a proposal; the owner has not yet made a decision:

1. Already used in released versions.
2. Has automated tests.
3. Decoupled from the tool's business logic, so moving it out does not require bringing along a chain of dependencies.
4. Works on Avalonia 12.
5. Has a healthy file structure (for example, no single huge class).

## Duplication list

During development, the three sessions keep notes whenever 「自己又做了一份通用的東西」 ("we have built another copy of something general-purpose"). These are currently kept in commander's local folder (`ledger\`), one file per session; they will be moved into this repository during the inventory. As of 2026-10-03, it is still empty.
