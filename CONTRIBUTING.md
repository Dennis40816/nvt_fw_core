# Contributing

This repository is the shared baseline for the NVT firmware tools (NFC, NFH, NFU). When a tool repository adopts a rule from here, it copies or links the text in the same pull request.

## Code and test conventions

New C# code and tests follow [conventions.md](docs/core/conventions.md) and [testing.md](docs/core/testing.md). Existing code is the baseline. It can only go down.

## Language

Owner decision of 2026-10-05: 「後續文件，除了 README 預設要有中文版本，其他都預設用英文，除非我指定新增中文版本」 ("From now on, documents default to English. The exception is README, which also has a Chinese version by default. Other documents get a Chinese version only when I ask for one.")

- Write repository text in English by default. This covers pull request titles and descriptions, review comments, commit messages, documentation, handoff notes, ADRs and TODO lists.
- README has an English and a Chinese version. Here they are `README.md` (English) and `README.zh-TW.md` (Chinese). Change both in the same pull request.
- Add a Chinese version of any other document only when the owner asks for one.
- Existing Chinese documents are translated to English in batches, using idle codex capacity. This is low-priority work that never blocks development. Once a document is translated, only the English version is maintained. Translation pull requests follow the repository's normal risk rules.
- Owner quotes keep the original Chinese text. An English translation may follow in parentheses.

## Approval after merging main

Owner decision of 2026-10-05 10:3x (Taipei): 「可以，但手動解衝突要重批」 ("Yes, but manually resolved conflicts require approval again.")

After approval, a new commit that only merges `main` cleanly may be merged once required checks pass, without a new approval. If anyone resolves a conflict by hand, the owner must approve again.

## One status table

Owner decision of 2026-10-05 12:0x (Taipei): 「可以先用各自的，但路徑未來要統一」 ("Each project can use its own for now, but the paths must be unified later.")

Each project keeps one status table; other documents link to it. Here it is the [Current status table](docs/shared-ci/README.md#current-status-2026-10-05). A common path and format across projects is to be planned.

## Deleting remote branches

List the branches and obtain the owner's approval before deleting them, except for categories already authorized by the owner. On 2026-10-05 01:2x (Taipei), the owner chose 「已合併進 main 的就刪」 ("Delete those already merged into main"), authorizing deletion of remote branches whose content is already in `main`.

## Core or project feature

Owner decision of 2026-10-05 21:1x (Taipei), relayed by commander: 「我相信完成後，開發功能前都必須先問是屬於 Core 的功能還是專案專屬功能」 ("I believe that once this is complete, before developing any feature we must first ask whether it belongs in Core or is project-specific.")

Before building a new feature, the session proposes a classification (Core or project-specific) with its reason and sends it to commander as an owner question. Commander asks the owner in batches; the owner decides. Features that are clearly firmware product logic are project-specific without asking; just record them.

## When a module counts as imported into Core

Owner decision of 2026-10-05 21:1x (Taipei), relayed by commander: all five criteria are required:

1. The code is in `nvt_fw_core` through a pull request the owner approved.
2. It builds and its tests pass in `nvt_fw_core`, verified locally first.
3. At least one tool uses the Core version and deletes its own copy, and the tool's existing tests pass. NFC UI snapshots must show zero difference. NFH and NFU UI snapshots may differ without owner approval, but the pull request attaches before/after images as a record. Non-UI output (files and data) must still show zero difference in every tool. Owner decision of 2026-10-06 01:1x (Taipei), relayed by commander: 「NFC 要求保持一致，其他沒有要求」 ("NFC must remain identical; there are no other requirements.")
4. Each tool still builds and releases on its own; Core ships with the tool (owner decision of 2026-10-03 08:4x).
5. English and Chinese README documentation is available, and `docs/components.md` records the source, version and adopting tools.

## NuGet package locks

Owner decision of 2026-10-06 01:1x (Taipei), relayed by commander: 「repo 共同鎖定」 ("Lock packages across repositories.") Every repository enables NuGet package lock files, and CI always restores with `dotnet restore --locked-mode`. Shared package versions align with the set pinned in Core's `Directory.Packages.props`, maintained by NVT CORE.

## Font set

Owner decision of 2026-10-06 00:5x (Taipei), relayed by commander, after: 「我覺得可以討論出一個 font set 就只能用裡面的，font set 定位要清晰，例如 title 固定用哪種」 ("I think we can agree on a font set and use only its fonts; the roles must be clear, for example which font titles always use.")

Only Inter, Cascadia Mono, Noto Sans TC (Traditional Chinese fallback), and Material Symbols Outlined are allowed. Sizes: caption 11, body 13, heading 16, title 24; mono and numbers 13; icon 16. Strong roles change only the weight. All fonts are embedded at fixed versions; Windows fonts are never packaged. NFC adopts in two steps: a zero-difference extraction first, then the new role table in a separate PR with before/after images that the owner approves.
