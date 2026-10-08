# nvt-sched

[English](README.md) | 繁體中文

本機 Windows 工作排程器工具，只執行白名單內的影子 tick。需要標準
`Program Files\PowerShell\7` 位置的 PowerShell 7 和 `ScheduledTasks` 模組。
由 owner 在一般、未提權的 PowerShell 視窗操作，不存密碼、不繞過 GPO／執行政策。

## 路徑參數

| 介面 | 用途 |
| --- | --- |
| `-CommanderDir`／`COMMANDER_DIR` | 已審核且包含 `tick-shadow.ps1` 的本機資料夾。命令列優先於環境變數，沒有本機預設值。 |
| runner `-TaskPath` | 可讀名稱使用 `\NVT\`；舊名稱使用 `\`。產生的 action 指定正確位置。 |
| `allowlist.psd1` | 保留固定腳本檔名、工作說明與成功碼。CLI 在 commander 資料夾下解析腳本位置。 |

安裝將 `-CommanderDir` 寫入 runner 參數；後續指令使用相同資料夾。
根目錄的舊排程保留來源的動作，沒有 `-CommanderDir`。它的 runner 讀 `COMMANDER_DIR`，沒設定時以代碼 2 結束。
host 執行檔與本機紀錄資料夾由 Windows 系統資料夾推導。

## 無視窗排程動作

排程使用 `conhost.exe --headless` 啟動 PowerShell，不開終端機視窗。
即使 PowerShell 指定 `-WindowStyle Hidden`，Windows Terminal 仍可能開啟視窗。
headless console 避免這個視窗，因此 action 不再使用 `-WindowStyle`。

`--headless` 是未公開文件的 conhost 選項，需要 Windows 10 版本 1809 或更新版本。
Microsoft 的 [pseudoconsole API 文件](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole) 列出相同的底層版本需求。
舊系統可能無法啟動工作，或會開啟 console 視窗。
安裝前在「設定 > 系統 > 關於」檢查版本與組建；Windows 10 1809 的組建為 17763。
owner 授權執行後，用 `list` 檢查 `Result` 與 `ResultSource`，並確認沒有出現 console 視窗。

nvt-sched 讀取 headless `\NVT\commander-tick` action 的 runner 狀態檔。
conhost 丟棄子程序退出碼，排程器的 `LastTaskResult` 通常只會是 0。
[上游回報](https://github.com/microsoft/terminal/issues/17178) 說明了這個行為。

- 從 `[Environment]::SystemDirectory` 取得 `conhost.exe`，並用 `Assert-NvtPath` 檢查。
- 從 `Program Files\PowerShell\7` 取得 `pwsh.exe`，不查 PATH，也不接受呼叫者指定執行檔。
- 檢查固定 runner，工作目錄仍只接受白名單 tick 所在的資料夾。

action 引數格式如下：

```text
--headless "<pwsh path>" -NoLogo -NoProfile -NonInteractive -File "<runner>" -Id commander-tick -TaskPath \NVT\ -CommanderDir "<commander folder>"
```

不使用密碼、encoded command 或 execution policy switch。
`list`／`status` 將舊的 `pwsh.exe -WindowStyle Hidden` action 標為 `DefinitionOutdated`。
`run`／`remove` 以代碼 4 拒絕這個過期定義。
`install`／`add` 只在其餘欄位完全符合要求的白名單定義時重新註冊。
更新須取得 runner 鎖、確認程序已退出，且排程狀態為 Ready。
重新匯出並確認 headless 定義後，才清理根目錄舊排程。
其他定義改動與已停用工作仍拒絕處理。

## owner 重新註冊的一行指令

先將 `COMMANDER_DIR` 設為已審核的 commander 資料夾，再於 repository 根目錄執行：

```powershell
pwsh -NoProfile -File tools/nvt-sched/nvt-sched.ps1 install -Id commander-tick -EveryMinutes 20
```

如果有舊排程，成功後會看到：

```text
\NVT\commander-tick verified; legacy removed; history retained.
```

沒有舊排程時，訊息會確認新排程設定相同並保留歷史。工作排程器會看到資料夾
`\NVT\`、名稱 `commander-tick`、作者 `commander`，以及中英文說明：用途、週期、
執行腳本與維護者。說明模板放在 `allowlist.psd1` 的必要欄位 `Description`；少了它，
排程器畫面無法說明用途、週期及維護者。週期會填入實際分鐘數。
Principal 仍是目前使用者 SID，採 `InteractiveToken`／`LeastPrivilege`。
`install` 與相容的 `add` 做同一件事，只更新上述完全相符的過期 action。
已存在但有其他設定改動或停用的排程，不覆寫、不啟用。

先註冊新工作，再重新匯出核對 action、SID、設定、作者與說明；成功後才核對、
停用並移除根目錄舊工作 `\NVT-S-<SID>-commander-tick`。新註冊／驗證失敗會報錯，
舊工作保持原狀；驗證失敗時新工作可能還在，請先檢查再重試。清理舊工作仍取得
同一把 runner 鎖、核對 PID 與啟動時間。程序尚在或證據不明時，舊工作留在停用狀態，
工具回非零；等完成、看 `list`，再執行同一行安裝指令。
新舊工作共用原有狀態與鎖，不能同時執行 tick；撞鎖的嘗試可能留下失敗紀錄。
runner 會區分新舊來源路徑，因此新註冊失敗時，舊工作仍能執行。

## 預覽、查詢、執行與移除

下列指令在 repository 根目錄執行：

```powershell
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 install -Id commander-tick -EveryMinutes 20 -DryRun
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 list
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 status -Id commander-tick
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 list -Json
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 run -Id commander-tick
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 remove -Id commander-tick
```

`list`／`status` 是同一個唯讀查詢：顯示 `\NVT\` 工作及根目錄本 SID 的舊名稱。
舊名稱標 `Legacy: True`、`Unmanaged`，一般 run／remove 不操作它們。
原有 `Installed`、`DefinitionMismatch`、`NotInstalled`、`Unmanaged` 意義不變；
新增的 `DefinitionOutdated` 表示舊 action 尚未更新。
定義不符仍可查完成紀錄，但不能執行。`-Json` 固定輸出陣列；新增 `TaskPath` 是為了
區分資料夾，`Legacy` 是為了指出遷移殘留。最近成功、退出碼、排程結果、下次時間及
最近五筆歷史照舊。證據讀不到即顯示未知，不修復檔案。查詢失敗回非零，不落錯誤紀錄，
不輸出半份 JSON 或底層錯誤全文。`run` 只送請求，完成結果要看 `list`。

`Result` 顯示有效結果，`ResultSource` 說明證據來源：

- `RunnerState`：狀態已完成、`ExitCode` 為整數，且 `UpdatedAtUtc` 足以對應排程器的 `LastRunTime`，使用該退出碼。
- `Scheduler`：執行中代碼 267009、從沒跑過代碼 267011、根目錄舊工作及未使用 conhost headless 的 action，沿用 `LastTaskResult`。
- `Unknown`：headless 工作的狀態遺失、無法讀取、尚未完成、時間無效，或比最新執行時間舊且超過容許誤差。

比較時將排程器的本機時間轉成 UTC，容許狀態時間比 `LastRunTime` 早最多五秒。
這五秒涵蓋時間精度與小幅時鐘差異；更早即顯示 `Unknown`，即使舊狀態記錄成功也一樣。
`LastTaskResult`、`ExitCode` 及原有完成欄位保持不變，維持相容性。
`status`、JSON 與文字輸出都包含相同的有效結果欄位。

`remove` 只處理 `\NVT\`，先停用、確認程序退出後才註銷，不強殺、不清紀錄。
PID 重用須核對啟動時間。`EveryMinutes`（1–44640，預設 20）、`DataDir`、`DryRun`
只適用 install／add；資料目錄只接受白名單 tick 所在位置。`Json` 適用 list／status。
拒絕未知指令、參數與 ID。DryRun 顯示路徑、名稱及 XML，不查排程器、不落檔。

## 每週排程清單

也可手動唯讀稽核：

```powershell
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 audit
```

只讀取排程器資料與 headless runner 狀態，排除 `\Microsoft\` 及其子資料夾；`\MicrosoftVendor\` 等廠商
資料夾仍列入。輸出 `%LOCALAPPDATA%\NVT\sched\audit\audit-<yyyyMMdd>.md` 與
`latest.json`，畫面只回報清單路徑。繁中手機版先一行結論，再分「需要注意」「我們的」
「廠商的」；每個工作有路徑、名稱、狀態、上次時間、有效結果、結果來源、下次時間、作者與說明。

標記失敗（runner 結果非零、Unknown，或排程器結果不等於 0／267009／267011）、從沒跑過、已停用、沒有作者，以及我們的
（`\NVT\`，含子資料夾）。首次建立基準；之後用完整路徑加名稱比較新增與刪除。
「建議可停用」只列就緒、從沒跑過且沒有作者的廠商工作，附理由並提醒先確認用途，
不列我們的與舊名稱，也絕不自動停用。依指定分類，tick 的結果 10 仍標非零失敗，
清單會補充說明：這個 tick 的 10 代表有新變化，runner 視為成功。

既有 tick 每個 ISO 週在週一 08:30 本機時間之後第一次執行時產生清單，睡眠／登出可在
該週稍後補做，沒有新增排程。預設每 20 分鐘，所以可能約 08:40 才做，並在
`snapshot-changes.log` 加上 `weekly task audit ready: <路徑>`，回傳 10。
同週手動稽核可直接沿用；通知成功寫入 log 後才確認該週已通知，log 失敗時重試通知，
不重做清單。舊快照無法讀取時報錯，不偷偷重設比較基準。同日手動重跑會取代當日 Markdown。

## 保留的紀錄與必要欄位

`%LOCALAPPDATA%\NVT\sched` 不搬動；原狀態、錯誤、runner 鎖與歷史檔保留。
歷史最多 50 筆，查詢顯示最近五筆，不存 tick 原始輸出或例外全文。tick 的 0／10 都成功。
新 `latest.json` 只存 `At`（判定 ISO 週）及 `Tasks`（指定中繼資料，供下次比較），也用
此檔的獨占 handle 防止稽核重疊，不另加鎖檔。日期 Markdown 供 owner 閱讀。
每個工作新增 `Result` 與 `ResultSource`，並保留原本的 `LastTaskResult`。
`snapshot.json` 只新增 `weekly_task_audit_week`，確認週通知已寫 log；少了它，清單產生
後當機可能丟通知，或每輪重複通知。沒有其他新持久欄位／稽核檔案。

仍拒絕 traversal、UNC／裝置路徑、ADS、任一父目錄的 junction／symlink。
XML 補已知排程器省略預設值、正規化 SID；action、權限及執行設定改動仍拒絕。
原去識別 XML fixture 繼續驗證匯出省略形狀。排程採無限週期加本人登入、
StartWhenAvailable、IgnoreNew、不喚醒電腦、允許電池執行、五分鐘上限。
睡眠／關機／登出會延遲，不會喚醒 Claude session。GPO、新資料夾權限、重啟／睡醒／登入
與逾時行為留給 owner 本機驗收。

工具錯碼沿用：0 成功、2 參數／白名單、3 不安全路徑、4 定義不符、5 未知／停用、
6 程序存活、7 鎖不可得、8 執行／移除時不存在、9 本機 API／檔案失敗。
稽核錯誤不寫 job 錯誤紀錄。寫 JSON 時當機仍可能留下未知證據，須檢查。

## 離線驗證與回退

```powershell
pwsh -NoProfile -File .\tools\nvt-sched\test-nvt-sched.ps1
pwsh -NoProfile -File .\tools\nvt-sched\test-nvt-sched.ps1 -ShowExamples
```

全部排程 API 用 mock，程序與輸入／輸出隔離；不查詢、註冊或修改真的排程，不碰網路。
本次交付沒有正式註冊，也正式安裝與通知接續仍須 owner 本機驗收。

owner 安裝前若需要回退，先在工作排程器匯出舊工作的 XML。
若舊工作清理失敗而仍在，owner 可在工作排程器停用新工作，再檢查並啟用舊工作。
若舊工作已刪，用目前工具 remove 新工作，再匯入備份舊 XML，或還原舊版工具後用原 add
指令重建根目錄工作。腳本位置和 SID 須一致；本機紀錄不清除。恢復前確認只有一個工作啟用。

匯入的 suite 以合成 tick 呼叫端驗證子程序中的 audit 與通知。
部署中的外部 tick 不在匯入範圍，其與 scheduler 的整合仍須主機驗證。
suite 保留搬移、工作 metadata、每週 audit、報告重用與通知重試測試。

## Source snapshot（來源快照）

來源：the commander's scheduler tools。快照時間：`2026-10-05T18:24:14+00:00`（UTC）。

來源不是 Git repository，因此沒有來源 ref 或 commit SHA；以下逐檔 SHA-256 為本次基準。
路徑均相對於來源工具資料夾。狀態表示 repository 副本與來源 bytes 是否完全一致。

| 匯入檔案 | 來源 SHA-256 | Repository 副本 |
| --- | --- | --- |
| `allowlist.psd1` | `b16ad24e85480061cb519e981de4f51a7ceabfe603208efdcafefe32d725f082` | Changed（已修改） |
| `nvt-sched-runner.ps1` | `2611e1f754e1dc86ca5a9bb29dd64de77c4f66ce482b79f0d504bd655e1a27da` | Changed（已修改） |
| `nvt-sched.ps1` | `04ae2328ab928dba32f3b7b49b9e3ab41ce3ca71ea07b8a07b6b765c50e6484e` | Changed（已修改） |
| `README.md` | `f24729513ea87e7dd5b0473048310f544c1e230d3b72ac716b6581bf0e1ed89d` | Changed（已修改） |
| `README.zh-TW.md` | `250c07c425e8b87652ebaafe310bebb32d1b8c20cd2f7923f4c902d32880e305` | Changed（已修改） |
| `registered-task.fixture.xml` | `8f2a57eb7021745c9d45c772157c682c3cdad49fcd69d8bd7337626888732921` | Changed（已修改） |
| `test-nvt-sched.ps1` | `efec2708c404e4aac60b851995184dcad25c68762e27b95762bcdd26376f0d18` | Changed（已修改） |

匯入腳本均補 owner 要求的版權聲明；修改文字使用無 BOM 的 UTF-8 及 LF 換行。

- `allowlist.psd1`: 新增版權標頭；保留固定 tick 檔名，由既有參數解析資料夾。
- `nvt-sched-runner.ps1`: 新增版權標頭；由參數或環境取得必填 CommanderDir，再傳給 CLI。
- `nvt-sched.ps1`: 新增版權標頭；保留 CommanderDir 參數化、資料夾必填檢查與 Windows 引數跳脫，新增 headless action 與過期定義的精確搬移。
- `README.md`: 改用 repository 相對指令與路徑參數；移除本機部署細節，新增來源 hash 及外部 tick 驗證限制。
- `README.zh-TW.md`: 改用 repository 相對指令與路徑參數；移除本機部署細節，新增來源 hash 及外部 tick 驗證限制。
- `registered-task.fixture.xml`: 改用 headless action，帳號、console、host、runner 與資料夾均用佔位符，保留合成 SID 與匯出省略形狀。
- `test-nvt-sched.ps1`: 新增版權標頭；保留參數測試，從 fixture 推導路徑負例；使用帳號佔位符與合成外部 tick 呼叫端。

零差異驗證先比對來源 hash，再逐項審核上述轉換，最後從 repository 根目錄執行全部匯入 suite。
queue fixture 驗證 parser、gate、證據與清理；scheduler mock 驗證工作定義、搬移、歷史與 audit。
主機比對使用已審核路徑參數；私有 queue 狀態、外部 tick 證據及正式驗收留在 repository 外。
