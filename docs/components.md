# 元件對照表

用途：之後盤點時，用這張表決定每個元件由誰的實作納入共用。原則是 owner 說的「誰最好最納入共用」。

**這張表目前大多是空的。** 已填的內容來自 2026-10-02 讀三個 repo 的專案檔（`.csproj`、`Directory.Packages.props`），沒有讀 UI 程式碼，所以只能確定「有哪些專案」，不能判斷品質。標「未查」的是還沒看過，不代表沒有。

## 平台現況

| | NFC | NFH | NFU |
|---|---|---|---|
| .NET SDK | 10.0.301 | 10.0.301 | 10.0.303 |
| Avalonia | 12.0.5 | **11.3.12**（另用 AvaloniaEdit 11.4.1） | 12.1.1 |
| 專案結構 | Domain／Application／Infrastructure／Contracts／Platform／Profiles／Presentation.Avalonia／Desktop／Cli／Bootstrap，另有 Launcher、LauncherBootstrap、DistributionLauncher、VersionManagement.Application／Infrastructure | FreeformHelper.Domain／Application／Infrastructure／UI | Nvt.Replay.Core／Sources／Formats／Analysis／Rendering／Avalonia／Cli |
| UI 測試 | Avalonia.Headless.XUnit | 未查 | Avalonia.Headless.XUnit |

Avalonia 主版本不一致：共用 UI 元件上線前，NFH 要從 11 升到 12。NFH 已把這件事排進計畫，不是現在升。

## 元件

| 元件 | NFC | NFH | NFU | 成熟度評估 | 決定 |
|---|---|---|---|---|---|
| launcher（啟動、版本管理、更新） | 有：Launcher、LauncherBootstrap、DistributionLauncher、VersionManagement.* | 未查 | 未查 | 未評估 | **NFC 開發後直接套用**（owner 2026-10-03） |
| UI 主框架（外殼、導覽、工具註冊） | Presentation.Avalonia、Desktop | FreeformHelper.UI | Nvt.Replay.Avalonia；正在做「工具首頁」shell。MainWindow 目前 19 個檔約 8,400 行 | 未評估 | 未定 |
| 主題與字型 | Avalonia.Themes.Fluent、Fonts.Inter | 未查 | Avalonia.Themes.Fluent、Fonts.Inter | 未評估 | 未定 |
| 設定儲存 | 未查 | 未查 | 未查 | 未評估 | 未定 |
| 診斷與日誌 | 未查 | 有 AppLogStore（從 PR 說明得知，未讀程式碼） | 未查 | 未評估 | 未定 |
| 關於與授權頁 | 未查 | 未查 | 未查 | 未評估 | 未定 |
| CLI 慣例 | NvtFwCombiner.Cli | 未查 | Nvt.Replay.Cli | 未評估 | 未定 |
| 發佈與打包 | 未查 | 未查 | 有 package 與 release 腳本（從 session 回報得知） | 未評估 | 未定 |
| console 系統 | 未查 | **先在這裡試做新架構**（owner 2026-10-04） | 未查 | 未評估 | **NFH 試做成熟後推到 nvt_fw_core 當公版**；其他模組之後照同樣模式，逐一抽象成共用結構再整合進來（owner 2026-10-04）。時程「之後」 |
| codex 派工工具（排隊、派工、審查、收尾） | 有：`lane.sh`、`dispatch.ps1`、`creview.sh`、`vq.sh` | 有（排隊與收尾腳本） | 有（未查） | 未評估 | **之後收進 nvt_fw_core**；不寫死模型與 effort，用 codex 預設（`~/.codex/config.toml`，定期調整），個別任務由派工者指定（owner 2026-10-03）。時程「之後」，不排進共用 CI 的階段 0～2 |

## 成熟度怎麼評

以下是提案，還沒有 owner 的決定：

1. 已經在發佈的版本裡使用。
2. 有自動化測試。
3. 與該工具的業務邏輯解耦，搬出去不用帶一串相依。
4. 在 Avalonia 12 上可用。
5. 檔案結構健康（例如沒有單一巨大類別）。

## 重複清單

三個 session 在開發時順手記下「自己又做了一份通用的東西」。目前放在 commander 的本機資料夾（`ledger\`），每個 session 一個檔；盤點時搬進這個 repo。截至 2026-10-03 還是空的。
