# nvt_fw_core
[English](README.md) | 中文

NVT 韌體工具族的共用核心。**.NET 專案骨架已建立，共用執行階段模組尚未抽取。** 這個 repo 也放規劃文件，以及第一步「共用的 CI/CD 與治理框架」的程式碼：共用核准檢查 [`actions/approval-check`](actions/approval-check/)。

## 這個 repo 是什麼

NVT 有三個各自獨立的韌體工具：

| 簡稱 | 工具 | Repo | 目前版本線 |
|---|---|---|---|
| NFC | NVT FW Combiner | `Dennis40816/nvt_fw_combiner` | 1.2.x |
| NFH | Freeform Helper | `Dennis40816/nvt-freeform-helper`（2026-10-04 起的主開發 repo） | 1.3.x |
| NFU | NVT FW UTIL（原 Event Buffer Replay） | `Dennis40816/nvt-event-buffer-replay` | 0.x |

三個工具都有啟動、外殼、設定、診斷這類彼此相似的部分。這個 repo 用來規劃它們的共用核心，最終目標是整合成一個 NVT FW Workstation。

owner（Dennis）的暫定目標，原話：

> 三個版本的 2.0.0 時都開始共用核心架構並在下一個重大版本更新推出整合型的 work station

## 現況（2026-10-05）

- 共用核心：`Nvt.Core` 與 `Nvt.Core.Avalonia` 骨架已建立，模組抽取與工具採用尚待後續工作。
- 共用的 CI/CD 與治理：共用核准檢查 `v0.1.0` 已發佈，三個工具 repo 都還沒採用。試點是 NFH，等 NFH 的 CI 修正與 1.3.2 完成後開始。進度見 [docs/shared-ci/README.md](docs/shared-ci/README.md)。

## 原則

1. **逐元件、誰成熟誰納入共用。** launcher 由 NFC 開發完成後直接套用到其他工具；其他元件之後逐一看成熟度，哪個專案做得最好就把它納入共用。
2. **1.x 照常投入。** 各工具在 1.x（NFU 是 0.x、1.0）期間不必為了共用而停下來。
3. **先有雛型再探討。** 在各專案各自有雛型之前，不寫共用程式碼，也不為別的專案預先設計。
4. **開發優先順序：** NFC ≥ NFH > NVT Core > NFU。
5. **領域中立。** 共用核心不放韌體、Event Buffer、Freeform 的業務邏輯；NFC 裡決定輸出 bytes 的程式（R3）不進共用核心。這一條是規劃時的假設，尚未由 owner 確認。

## 文件

- [src/](src/)：Nvt.Core 程式庫（`Nvt.Core` 與 `Nvt.Core.Avalonia`）
- [docs/vision.md](docs/vision.md)：目標、版本節奏、階段
- [docs/components.md](docs/components.md)：元件對照表（三個專案各自的實作與成熟度）
- [docs/decisions.md](docs/decisions.md)：owner 的決定紀錄
- [docs/open-questions.md](docs/open-questions.md)：還沒決定的事
- [docs/shared-ci/README.md](docs/shared-ci/README.md)：共用的 CI/CD 與治理框架（盤點、方案、進度）

## Nvt.Core 程式庫

[Nvt.Core.sln](Nvt.Core.sln) 包含不依賴 UI 的 `Nvt.Core`（`net8.0`）、`Nvt.Core.Avalonia`（`net10.0`、Avalonia 12.0.5），以及各自的空白 xUnit 測試專案。Avalonia 測試專案參照 `Avalonia.Headless.XUnit`；Task 0 不抽取應用程式主機或執行階段程式碼。

凍結的設定基準：NFC（`nvt_fw_combiner`）、`origin/1.2.x`、commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。慣例取自：

- `global.json`、`Directory.Build.props`、`Directory.Packages.props`、`.gitignore`
- `src/NvtFwCombiner.Presentation.Avalonia/NvtFwCombiner.Presentation.Avalonia.csproj`
- `tests/NvtFwCombiner.Domain.Tests/NvtFwCombiner.Domain.Tests.csproj`
- `tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj`

僅保留共用編譯／建置設定及本骨架使用的套件。SDK 為 `10.0.301`，套件版本集中鎖定為 NFC 基準的版本。外部腳本負責還原套件並提交產生的 `packages.lock.json`。還原後以以下指令驗證：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Task 0 沒有行為測試或工具採用，因此尚不適用執行階段零差異驗證。後續每個抽取任務都必須移植來源模組既有測試，並加入行為特徵測試。NFC 改用 Core 時，須在切換前後執行原模組測試，UI 變更包含 `NvtFwCombiner.UiSmoke.Tests`，並在相同 OS、字型、DPI 與主題下，比較輸出位元組或 UI 快照與凍結基準是否一致；不得更新基準來接受差異。

## 誰維護

2026-10-03 起由專門的 session「NVT CORE」依 owner 的指示維護。規劃內容的變更要有 owner 的決定為依據，並記進 [docs/decisions.md](docs/decisions.md)。每個 PR 都要 owner 核准才能合併。
