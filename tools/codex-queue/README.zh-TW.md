# codex-queue

[English](README.md) | 繁體中文

Windows Git Bash 派工佇列工具，複製自已部署版本。需要 Python 3.10+、Git for Windows（含 `cygpath`）、PowerShell 7；正式工作另需 Codex CLI。brief 或呼叫者未指定時，沿用 Codex 模型與 reasoning effort 預設值。

執行期佇列放在 checkout 外。每個 queue 有 `queue.env`、選用的 `build-notes.md`、`accept-templates.txt`、`risk-floor.txt`，以及 `proposed/`、`ready/`、`running/`、`done/`、`failed/`、`work/`。`queue.env` 是受信任 Bash 程式碼，使用前須審核；`stop` 檔停止領取新工作。同層 queues 共用 claim 鎖及磁碟預留。

在 repository 根目錄，以 Git Bash 執行：

```bash
bash tools/codex-queue/qworker.sh worker '<QUEUE>'
bash tools/codex-queue/qrefill.sh '<QUEUE>'
bash tools/codex-queue/qfocus.sh '<QUEUE>'
```

執行前手動替換所有尖括號佔位符。`queue.env` 使用 Git Bash 路徑；建議 queue 參數使用絕對路徑。名稱／相對 queue 參數沿用原行為，以工具目錄的同層資料夾解析。refill／focus 會 fetch 並 detach 專用 `TRUNK_WT`；Codex 對它唯讀。兩者分別讀 `refill-prompt.md`／`refill-focus-prompt.md`，拆分輸出為 proposed briefs，審核後才移到 ready。

## 參數

| 介面 | 參數 |
| --- | --- |
| `qworker.sh` | 必填位置參數：worker 名稱、queue 資料夾。 |
| `qrefill.sh`、`qfocus.sh` | 必填 queue 資料夾；選用環境變數 `REF` 覆寫 refill checkout 的 `BASE`。 |
| `restart-pool.sh` | 必填 queue 資料夾。保留原一次性操作：設 stop、等待四筆 stopped 日誌（最多四十次、每次 60 秒）、啟動 `w5`–`w10`、將 `work/requeue/*.md` 移入 ready；使用前審核此操作。 |
| `run-codex-ws.ps1` | 必填 `-PromptFile`、`-Out`、`-Log`、`-Dir`；選填 `-Model`、`-Effort`（空值用 Codex 預設）、`-Sandbox`（預設 `read-only`）、`-AddDirs`（分號分隔資料夾，預設空）。另支援 PowerShell 通用參數。 |
| `tests/test-p0.py` | helper 路徑、一次性 fixture 根目錄；由 `test-p0.sh` 提供。已移除產品盤點模式。 |
| `tests/test-wrapper.ps1` | `-Wrapper` 路徑、一次性 `-Fixture` 根目錄；由 test-p0.sh 提供。 |

`queue.env` 設定：

| 設定 | 必填／預設／用途 |
| --- | --- |
| `REPO` | worker 必填；repository 路徑。 |
| `WT` | worker 必填；已存在的 task worktrees 父目錄及磁碟檢查位置。 |
| `BASE` | worker／refill／focus 必填；Git base ref。 |
| `TRUNK_WT` | refill／focus 必填；專用 worktree 路徑。 |
| `QUOTA_CHECK` | worker 必填，除非 `ALLOW_CREDITS=1`；受信任指令，輸出單行 JSON，含 `status`（`OK`／`EXHAUSTED`）、數字 `used_percent` 與 `sample_age_min`。沒有本機 fallback。 |
| `ALLOW_CREDITS` | 預設 `0`；`1` 明確跳過 quota gate。 |
| `SETUP` | 選填受信任主機 Bash 指令；預設空。 |
| `MIN_FREE_GB` | 預設 `215`；可用磁碟 GiB 下限。 |
| `TASK_RESERVE_GB` | 預設 `20`；每個 running、cleanup-pending、新任務的正整數 GiB 估量。 |
| `ACCEPT_POLICY` | 預設 `warn`；`enforce` 要求精確受信任 Accept 模板。兩模式均保留證據、Scope／risk 檢查。 |
| `EXTRA_ADD_DIRS` | 舊設定；worker 忽略共用資料夾，只授予本任務暫存資料夾。 |

worker 將 `QUEUE_BUILD_NOTES` 設為 `<QUEUE>/build-notes.md`；直接呼叫 `q.py prompt` 可將此環境變數設為選用的 notes 檔。wrapper 保留原無 bytecode／pytest cache、遙測及語言環境設定，並非呼叫者設定項。

最小本機 `queue.env`（替換佔位符，填妥後勿提交）：

```bash
REPO='<REPO>'
WT='<WT>'
BASE=origin/main
TRUNK_WT='<TRUNK_WT>'
QUOTA_CHECK="pwsh -NoProfile -File '<QUOTA_SCRIPT>'"
```

`example-queue/` 只含四個通用起始檔。請複製到 checkout 外的執行期 queue，再替換所有佔位符。
使用前審核該 queue 的 quota 指令、Accept 模板與 risk 規則。註解模板不增加允許指令；僅註解的 risk 檔採共通保護，不保留缺檔時的全部舊版保護。
產品計畫、repository 名稱、工作項目與私有相依資料，均放在各自的執行期 queue。
`--compat` 與 `--compat-all` 已移除，因為這兩個盤點模式指定特定產品 queue。

## Python helper 與 brief

`q.py` 的全部位置參數如下；Git 檢查須在目標 repository 內執行：

```text
get <brief> <Key>
accept <brief>
check <brief>
run-accept <brief> <log-prefix> [base-sha]
run-prebuild <brief> <log-prefix>
quota                              stdin 單行 JSON；耗盡時 exit 3
check-diff <brief> <base-sha>
unique <queue> <name> [source]      呼叫者持有 claim 鎖
chain-base <brief> <queue> <base>
required-free <queue> <minimum> <estimate>
prompt <brief> <worktree> <branch> <base>
split <refill-out.md> <proposed-dir>
```

brief 欄位仍為 `Title`、`Risk`、`Base`、`Wait`、`Prebuild`、`Model`、`Effort`、`Why`、`Goal`、`Scope`、`Accept`、`Not in scope`。Risk 限 R0–R2，R3 不執行。Scope 每行一個字面 repository 相對路徑。Accept 每行以 `$ ` 起頭，使用原預設或 queue 模板。模型／effort 覆寫須有任務個別原因；省略就用 Codex 預設。parser、quota、鏈、存證、清理、risk 規則的原契約見 `q.py` 開頭與 `CHANGES.md`。

固定 `Prebuild` 展開採匿名專案名稱：

- `Desktop` 建置 `src/Project.Desktop/Project.Desktop.csproj`。
- 其他名稱建置 `tests/Project.<Name>.Tests/Project.<Name>.Tests.csproj`。
- 目標 repository 若使用其他名稱，將 `Prebuild` 留空，改在 `Accept` 提供已審核建置指令。

## 離線測試

在 repository 根目錄，以 Git Bash 執行：

```bash
bash tools/codex-queue/tests/test-gate.sh
bash tools/codex-queue/tests/test-p0.sh
```

test-p0.sh 共用 gate fixture helpers，執行自己的整合案例、Python 單元測試及 PowerShell wrapper 測試。Codex、quota、SDK、fetch 均用替身；Git worktrees、commit、存證與清理使用一次性合成 fixture。不碰 live queue 或執行 Codex。真實 quota／Codex 工作與 pool restart 由操作人員提供自己的執行期設定。工具目錄不含來源備份與執行狀態。

## Source snapshot（來源快照）

來源：the deployed queue tools。快照時間：`2026-10-05T18:24:14+00:00`（UTC）。

來源不是 Git repository，因此沒有來源 ref 或 commit SHA；以下逐檔 SHA-256 為本次基準。
路徑均相對於來源工具資料夾。狀態表示 repository 副本與來源 bytes 是否完全一致。

| 匯入檔案 | 來源 SHA-256 | Repository 副本 |
| --- | --- | --- |
| `CHANGES.md` | `6323b355d8f536f4804f1d16637101e69e7ede4dc631da83250e39c198a17757` | Changed（已修改） |
| `q.py` | `23c4f52386f0b14a13c290b0b4b4c5ed061922c0ab78755e3be4ca8f3a6413d9` | Changed（已修改） |
| `qfocus.sh` | `f91508c5f9c8e05360699756475c60991dafa42663ae0b7cf4c99db85eb31645` | Changed（已修改） |
| `qrefill.sh` | `6df8a5db311df5eb48d81d5f7fc46d0fe012176a4e91dfe6ed360f1dc9b30f14` | Changed（已修改） |
| `qworker.sh` | `d139eb0b4edf121447e33c2e8ca9c30393389031f15fbf32d1820cb7de1a2048` | Changed（已修改） |
| `restart-pool.sh` | `d920d5de66f828c96170bcc1493a2c03bf0adb6639bfeab731522e0eccb3a7ea` | Changed（已修改） |
| `run-codex-ws.ps1` | `98f6abfb89925aad799684072289358a13bf365db81fc3fe56eea2b88fd7819e` | Changed（已修改） |
| `tests/test-gate.sh` | `e79f381c8d79237a8e8d14d4bc7e2041f1776b5998af7b7ee64ea4ff5bf0e281` | Changed（已修改） |
| `tests/test-p0.py` | `aab7cb0ad4933e77a175cef13850d58c8379df9866c9f3375375738f6cce177d` | Changed（已修改） |
| `tests/test-p0.sh` | `ed6ffa395f74d16a8ce632421dc2f246da7fd25a9e8edcb59a8a4d0b835b117c` | Changed（已修改） |
| `tests/test-wrapper.ps1` | `4edc03d2110f1796476a6621d32bd86ddd87fd3bbb13f825c08f0b2d727803c3` | Changed（已修改） |

匯入腳本均補 owner 要求的版權聲明；修改文字使用無 BOM 的 UTF-8 及 LF 換行。

- `CHANGES.md`: 以通用行為說明取代內部部署紀錄及產品工作項目。
- `q.py`: 新增版權標頭；Prebuild 的私有專案前綴改為匿名 Project。
- `qfocus.sh`: 新增版權標頭及 TRUNK_WT／BASE 必填檢查；未更新部署邏輯。
- `qrefill.sh`: 新增版權標頭及 TRUNK_WT／BASE 必填檢查，移除產品 queue 範例；未更新部署邏輯。
- `qworker.sh`: 新增版權標頭；REPO、WT、BASE 及 quota 設定改為必填，移除本機 quota 預設與路徑註解。
- `restart-pool.sh`: 新增版權標頭；queue 改由參數指定，以通用 pool 操作說明取代部署歷史。
- `run-codex-ws.ps1`: 僅新增版權標頭。
- `tests/test-gate.sh`: 新增版權標頭；使用合成 Git 身分與明確 mock quota 指令，驗證缺少 quota 設定。
- `tests/test-p0.py`: 新增版權標頭；移除兩種產品盤點模式，改用合成 queue 規則、通用範例、匿名專案及動態絕對路徑負例。
- `tests/test-p0.sh`: 新增版權標頭；改用合成 queue 規則與 fixture 推導的共用路徑。
- `tests/test-wrapper.ps1`: 僅新增版權標頭。

兩份 README 及四個通用範例檔由 repository 撰寫；不匯入各工具設定、相容性報告、備份與執行期狀態。

零差異驗證先比對來源 hash，再逐項審核上述轉換，最後從 repository 根目錄執行全部匯入 suite。
queue fixture 驗證 parser、gate、證據與清理；scheduler mock 驗證工作定義、搬移、歷史與 audit。
主機比對使用已審核路徑參數；私有 queue 狀態、外部 tick 證據及正式驗收留在 repository 外。
