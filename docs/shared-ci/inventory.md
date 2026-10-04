# 三個 repo 的 CI/CD 與治理盤點

盤點時間：2026-10-03 07:00 前後（台北）。只讀 Git ref 與 GitHub 公開 API，沒有改任何 repo 或設定。

2026-10-04 起 NFH 的主開發移到 public 的 `Dennis40816/nvt-freeform-helper`，原本 private 的 `FreeformHelper` 之後封存。本文 NFH 的部分是轉移前的盤點。

## 讀取範圍

| 簡稱 | Repo | GitHub 可見性 | 讀取的 ref | 備註 |
|---|---|---|---|---|
| NFC | `Dennis40816/nvt_fw_combiner` | public | `origin/1.2.x` `8479dee8`；與 `origin/main` `0cb57b90` 比對 | 兩者在治理檔案上只差 5 個 CLI 檔加入 R3 路徑 |
| NFH | `Dennis40816/FreeformHelper` | private（未登入時 API 回 404） | `origin/1.3.x` `99ad6c5e` | 治理文件分支 `s15005b-governance-docs` 已在盤點期間合併進 1.3.x |
| NFU | `Dennis40816/nvt-event-buffer-replay` | public | `feature/0.1.2/setup-batch` `b40be44`（本機批次分支，尚未合併）；ruleset 讀 GitHub 現況 | 核准檢查以這個分支為準，它包含 0.1.2 之後的強化 |

- GitHub 帳號 `Dennis40816` 是個人帳號（API `type: User`），不是組織。
- NFC、NFU 的 ruleset 是用未登入的 GitHub API 讀到的實際設定。bypass 名單要有管理權限才看得到，標「未確認」。NFH 是 private，ruleset 讀不到。
- NFC、NFH 的細節由 codex 以唯讀沙盒讀 Git ref 整理，NFU 由 NVT CORE 直接讀。NFH 是 private repo，本文只寫到摘要層級（有什麼、缺什麼），不列私有資料與產品規則的細節。沒有執行任何建置、測試或 workflow，所以文中不能說任何檢查「目前是綠燈」。

分類標記：

- **相同**：三邊做法一致，可以直接當共用規則。
- **分歧**：名稱或目的相同，內容已經不同。
- **特有**：只有某個 repo 有。
- **缺**：該 repo 還沒有，但文件說要做。

## 1. 分支、合併與 GitHub 身分

| 項目 | NFC | NFH | NFU | 分類 |
|---|---|---|---|---|
| `main` 的角色 | 只放已發佈版本 | 同左 | 同左 | 相同 |
| 版本 trunk 命名 | `X.Y.x`（`1.2.x`） | `X.Y.x`（`1.3.x`） | 三段數字 `X.Y.Z`（`0.1.2`、`0.2.0`） | 分歧 |
| release branch | `X.Y.Z` 從 trunk 凍結，合併進 `main` 後刪除 | 尚未落地（`S15.005e`） | 沒有，trunk 本身就是三段數字 | 特有（NFC） |
| 工作分支 | `feature/<version>/<topic>` | 同左 | 同左 | 相同 |
| 合併方式 | 只允許 merge commit（ruleset） | 文件規定 `gh pr merge --merge --match-head-commit` | 同 NFH，ruleset 也只允許 merge | 相同（NFH 的 GitHub 設定未確認） |
| agent 寫 GitHub 的身分 | GitHub App `nfc-agent-dennis40816[bot]`（id 334370883） | 文件說沿用既有 App | 同一個 App | 相同 |
| App 不得有 `workflows` 權限 | `.github/AGENTS.md`：workflow 變更要人工審查 | 未寫明 | `CONTRIBUTING.md` 明文：App 不得有 `workflows` 與 commit status 寫入權限 | 分歧（實際 App 權限未確認） |

## 2. Workflows

| 項目 | NFC | NFH | NFU | 分類 |
|---|---|---|---|---|
| 主 CI 檔 | `ci.yml`：`policy / polytail`、`python-worker / verify`、`python / repository policy (<shard>)`、`dotnet / build`、`dotnet / test (<shard>)`、彙總 `dotnet / build-test`，全部 `windows-latest` | `ci.yml`：`policy / structure`、`dotnet / build`、四個 `dotnet / test (<shard>)`、彙總 `dotnet / build-test` | `ci.yml`：單一 job `build-and-test`（restore、build、test、800 行檢查與其自測、效能 smoke） | 分歧（NFC、NFH 都有 `dotnet / build-test` 彙總 job，NFU 沒有） |
| CI 的 push 分支 | `[main, 1.2.x]`，版本寫死 | `[main, '[0-9]+.[0-9]+.x']` | `[main, '[0-9]+.[0-9]+.[0-9]+']` | 分歧 |
| 核准檢查 workflow | `authority.yml` → check run `governance / authority` | 沒有（`S15.005c`） | `approval.yml` → commit status `governance/approval-rule`；`approval-self-test.yml` 跑檢查器單元測試 | 分歧，NFH 缺 |
| 發佈 | `release.yml`：`ci` 在 `main` 完成後觸發；candidate、eligibility、promote（`release` environment）、published-smoke，另有 v0.9.16 parity 三個 job | 沒有（`S15.005e`） | `release.yml`：手動觸發，`PREPARE_ONLY`／`PUBLISH`；candidate、promote（`release` environment）、published-smoke | 分歧（同一套模式，NFU 是精簡版），NFH 缺 |
| 預演／預覽包 | `release-rehearsal.yml` | 沒有 | `preview-package.yml`（保留 3 天） | 分歧 |
| 第三方 action 釘版 | 全部完整 SHA | 全部完整 SHA，`verify.ps1 -StructureOnly` 會檢查 | 全部完整 SHA，沒有自動檢查 | 相同（自動檢查分歧） |
| 釘的版本 | `checkout@9c091bb…`（v7.0.0）、`upload-artifact@043fb46…`（v7.0.1）、`setup-python@ece7cb0…`（v6.3.0） | 前兩個相同 | 三個都相同，另有 `setup-dotnet@a98b568…`（v6.0.0） | 相同 |
| 預設權限 | `contents: read`，只有需要的 job 升權 | 同左 | 同左 | 相同 |
| .NET SDK 安裝 | `scripts/install-dotnet.ps1`，installer 釘 commit；包在 local composite action `setup-toolchain` | `scripts/ci/install-dotnet.ps1`，installer 釘 commit `cbd31355` | `actions/setup-dotnet` 讀 `global.json` | 分歧 |
| Dependabot | NuGet、pip、GitHub Actions，每週 | 沒有 | 沒有 | 特有（NFC） |
| 私有測試資料 | 私有 Golden runner（細節未讀） | 有私有測試資料，CI 用專用金鑰取得（private repo，細節不列） | 私有資料放在被 ignore 的 `captures/`、`golden/` | 特有（各自） |

## 3. 核准檢查

| 項目 | NFC | NFH | NFU | 分類 |
|---|---|---|---|---|
| 風險分級 | R0–R3 加角色（`firmware-owner`、`release-owner`、`governance-owner`，目前都是 Dennis40816） | 文件只有兩層：高風險要 owner 在 GitHub 核准，其餘獨立審查 accept；R0–R3 列在 `S15.005c` | 兩層：owner-gated（整個 PR 要 owner 核准）與 review-gated | 分歧 |
| 分級的設定檔 | `docs/governance/authority-policy.json` 與 schema，18 個 entry，未分類路徑預設 R3 | 沒有 | `.github/approval-policy.json`：路徑樣式清單、`tests/**` 只有新增檔不算 owner-gated、合併進 `main` 一律 owner-gated | 分歧，NFH 缺 |
| 高風險的例子 | 韌體 bytes、Golden、write range、profiles、CRC worker、release、簽章、核准機制本身（R3） | `src/**`、`.github/**`、決定 gate 的 `scripts/**`、build 設定、發佈、agent 權限 | `src/**`、建置設定、修改既有測試、`.github/**`、`scripts/**`、`eng/**`、ADR、產品規格、agent 設定、`CONTRIBUTING.md` | 分歧 |
| CODEOWNERS | 只投影 R3 樣式，全給 `@Dennis40816`；有測試保證投影一致 | 沒有 | 投影所有 owner-gated 樣式，全給 `@Dennis40816`；檔頭說明哪些規則 CODEOWNERS 表達不了、由腳本負責 | 分歧，NFH 缺 |
| 檢查器 | `scripts/authority_check.py`：從 **PR head** 執行；base 與 head 兩份 policy 都套，取較嚴的；也看 submodule | 沒有 | `scripts/approval_check.py`：從 **base branch** 執行，PR 改不到檢查器與 policy；評估期間 PR 有變動就失敗 | 分歧 |
| 檢查結果的形式 | check run（ruleset 要求 `governance / authority`） | — | commit status（ruleset 要求 `governance/approval-rule`）。理由寫在 `CONTRIBUTING.md`：同名 check run 會累積，舊的紅燈不會消失 | 分歧 |
| review record 格式 | `COMMENTED` review 裡的 `nfc-review-record` JSON 區塊：`head`、`state: complete`、`openP0P1: 0`、`verdict: accept` 或 `accept-with-changes`、runtime 關係 | 文件只要求「獨立審查 accept、無 P0／P1」，沒有格式 | review 第一行 `Review record: <完整 head SHA> <accept\|reject>`；任何像「review record」的字樣都當作紀錄，格式不對就擋（防止用 HTML、不可見字元藏紀錄） | 分歧 |
| 誰能發 review record | `nfc-agent-dennis40816[bot]`、`Dennis40816` | — | 同 NFC | 相同 |
| 嚴重度 | P0／P1 不得有 | P0／P1 不得有 | P0–P3 定義，accept 要 0 個 P0、P1 | 相同 |
| 新 commit 之後 | 任何新 SHA 都要新的 review record，tree 相同也一樣；GitHub 端 dismiss stale | 文件要求合併前核對 head；沒有自動檢查 | 任何新 push 都要重新審查並重貼紀錄；owner 的 approve 也要對到目前 head | 相同（都綁 exact head；NFH 尚未自動化） |
| 「部分變更不讓核准失效」的例外 | 沒有。R0 是「普通的非規範文字」（`docs/handoff/**/*.md`），只是本地驗證可以走短路徑，審查仍要重做 | 沒有 | 沒有 | 相同（都沒有） |
| 合併前由合併的 agent 再確認 | 比對 approval snapshot、live record、base 的 authority 與 check run | 核對 head 與核准 | 從 base checkout 在本機再跑一次檢查器，要 exit 0 | 分歧 |

## 4. Ruleset（GitHub 現況）

NFC、NFU 都用 GitHub Actions（`integration_id` 15368）當 required check 的來源。兩邊的 ruleset 骨架相同，細節已經分歧：

| 規則 | NFC `main` | NFC trunk `*.*.x` | NFC release `*.*.*`（排除 `*.*.x`） | NFU `main` | NFU trunk `*.*.*` |
|---|---|---|---|---|---|
| 必要核准數 | 1 | 0 | 1 | 1 | 0 |
| dismiss stale reviews | 是 | 是 | 是 | 是 | 是 |
| code owner review | 是 | 是 | 是 | 是 | 是 |
| last push approval | 是 | 否 | 是 | 是 | 否 |
| 討論串須解決 | 是 | 是 | 是 | 是 | 是 |
| `require_extra_approval_for_unattributed_changes` | 是 | 是 | 是 | 是 | 是 |
| 合併方式 | merge | merge | merge | merge | merge |
| 禁止 force push | 是 | 是 | 是 | 是 | 是 |
| 禁止刪除 | 是 | 是 | 否（tag 後可刪） | 是 | 是 |
| required checks | `policy / polytail`、`python-worker / verify`、`dotnet / build-test` | 同左加 `governance / authority` | 同 `main` | `build-and-test`、`governance/approval-rule` | 同 `main` |
| strict（要與 base 同步） | 是 | 否 | 否 | 是 | 是 |

- 兩邊都另有 tag ruleset「Protect stable v* tags」：禁止更新與刪除 `refs/tags/v*`。
- ruleset 名稱：NFC `main` 與 tag 的名稱和 NFU 完全相同，trunk 的名稱不同（NFC `trunk`、`release branches`；NFU `Protect version trunks`）。
- NFC 的 `main` 還沒要求 `governance / authority`；ADR 0080 說要在 G2 改 check 名稱時一起加。
- **NFH：未確認。** `S15.005d` 把 ruleset 列為待辦；文件指定的 required checks 是 `policy / structure` 與 `dotnet / build-test`。NFH 是 private repo，個人帳號在 private repo 用 ruleset 需要 GitHub Pro，要 owner 確認方案。
- **bypass 名單：未確認。** NFC 的 ADR 0080 寫的是只有 owner 的 Repository admin 可以 bypass，App 不行。
- **ruleset 當成程式碼：只有 NFC 有。** `docs/handoff/1.1.13/g0-scripts/rulesets/RS-*.json` 加上 owner 檢查清單（備份、套用、讀回、復原）。這些 JSON 與現況不完全一致（例如 trunk 的 last push 已由 owner 改成 false），不能當作現況。

## 5. 治理文件

| 文件 | NFC | NFH | NFU | 分類 |
|---|---|---|---|---|
| `AGENTS.md` | 根目錄加 10 份以上子目錄 AGENTS；`validate_repository.py` 限制根目錄檔案不超過 16 KiB | 根目錄一份 | 根目錄一份 | 分歧 |
| `CONTRIBUTING.md` | 有 | 有（1.3.x 剛加入） | 有，核准規則寫得最完整 | 分歧 |
| 分支與版本治理 | `docs/governance/branch-version-and-release-governance.md` | 同名檔案，內容不同 | 寫在 `CONTRIBUTING.md`、`docs/release.md` | 分歧（NFC、NFH 同名） |
| PR 範本 | 必填 `nfc-authority` JSON 區塊、Golden 影響、驗證 | 沒有 | 勾選 owner-gated／review-gated、驗證、review record 格式提示 | 分歧，NFH 缺 |
| Issue 範本 | engineering-change、firmware-change | 沒有（用 GitHub Issues 加 5 個 lifecycle label） | 沒有 | 特有（NFC） |
| ADR | `docs/adr/` 82 份；`0000-template.md`；`README.md` 定義狀態 | 沒有 ADR 目錄 | 4 份短 ADR，沒有範本 | 分歧 |
| 與治理或 CI 有關的 ADR | 0021（code size，已被 0080 取代）、0033（受保護的 CI 發佈）、0060（發佈包大小上限）、0068（衍生檔同步）、0079（測試架構）、0080（現行 R0–R3 治理） | — | 0004（MVP 與單檔 800 行） | 特有 |
| `SECURITY.md` | 有（標題仍是 Draft） | 沒有 | 沒有 | 特有（NFC） |
| 交接文件 | `docs/handoff/` | `docs/handoff/`（版本協調板、工作流交接、bug ledger） | `docs/nvt-fw-util-claude-handoff.md` 單檔 | 分歧 |

## 6. 驗證腳本

| 項目 | NFC | NFH | NFU | 分類 |
|---|---|---|---|---|
| 驗證入口 | `scripts/verify.py`（`verify.ps1`、`verify.sh` 為包裝），多種 lane 與 CI shard | `scripts/verify.ps1`：`-StructureOnly`、`-CiLane build/test`、`-All` | `scripts/verify.ps1`：發佈身分、restore、build、test、行數檢查、效能 smoke | 分歧（名稱相同） |
| 單檔大小 | 非空白行 2,000 行以上要登記、不得超過登記值，降到 1,500 以下移除；另有測試擋 2,500 行以上 | 只量測（`measure-code-size.ps1`），不擋 | 手寫 `.cs`／`.axaml` 每檔 800 行，6 個舊檔用 baseline 上限，縮小時要調低 | 分歧 |
| 覆蓋率 | `coverage_policy.py`：整體不得低於 baseline，Domain／Application 變更模組 line 85%、branch 80% | 沒有 | 沒有 | 特有（NFC） |
| 效能門檻 | 只量測啟動時間，沒有門檻 | 有啟動與回歸效能預算，不在 CI | `performance-gate.ps1 -Mode Smoke` 在 CI | 分歧 |
| Golden | R3；發佈前跑 `--release-golden` | 有 golden 與輸出逐 byte 比對（private repo，細節不列） | 私有 golden 不進 repo | 特有（各自） |
| 發佈包檢查 | closed allowlist、SHA256SUMS、SBOM；ZIP 上限 128 MiB | 沒有 | closed allowlist、SHA256、`smoke-release.ps1` | 分歧 |
| 衍生檔同步 | `sync_derived.py`：預設只檢查漂移，`--write` 要指定 provider；CI 範本是逐 byte 鏡像 | 沒有 | 沒有 | 特有（NFC） |
| 格式與換行 | 未讀 | CRLF 正規化、`dotnet format --verify-no-changes`、XAML action role 檢查 | 未讀 | 特有（NFH） |

## 7. 結論：「公版」現在的真實狀態

1. **只有骨架是共用的。** 三邊共用的是概念：`main`、trunk、feature 分支模型；只用 merge commit；action 釘完整 SHA；預設唯讀權限；同一個 GitHub App；review record 綁 exact head；P0／P1 為零才 accept；ruleset 的 dismiss stale 與 code owner review。
2. **核准檢查已經分成兩套實作。** NFC（R0–R3、check run、從 PR head 執行、JSON 紀錄）和 NFU（兩層、commit status、從 base 執行、單行紀錄）的格式、執行來源和結果形式都不同。同一條規則要改，兩邊要各寫一次程式、各補測試、各改文件。
3. **NFH 正要做第三份。** `S15.005c`（R0–R3 policy、檢查腳本、CODEOWNERS、PR 範本、review record）和 `S15.005d`（ruleset）都還沒做。這是導入共用框架成本最低的時間點。
4. **ruleset 已經開始漂移。** trunk 命名、strict、required check 名稱、`governance` 檢查要不要套在 `main`，NFC 和 NFU 都不同。目前只有 NFC 有 ruleset JSON 與套用程序，而且 JSON 已經落後現況。
5. **各自特有、不該共用的部分。** NFC 的韌體 R3 路徑、parity 與 release 證據；NFH 的 golden 與輸出比對、私有測試資料；NFU 的 800 行規則與效能 smoke。這些留在各 repo，由共用框架讀各自的設定。

## 要 owner 在 GitHub 上確認的事實

這些盤點讀不到，請 owner 確認或讀回：

1. NFH 是否已有 ruleset 或 branch protection；`Dennis40816` 帳號的方案能否在 private repo 使用 ruleset（需要 GitHub Pro）。設定頁：<https://github.com/Dennis40816/FreeformHelper/settings/rules>
2. 三個 repo ruleset 的 bypass 名單。NFC：<https://github.com/Dennis40816/nvt_fw_combiner/settings/rules>；NFU：<https://github.com/Dennis40816/nvt-event-buffer-replay/settings/rules>
3. GitHub App `nfc-agent-dennis40816` 實際有沒有 `workflows` 權限。App 設定頁：<https://github.com/settings/apps>
4. 三個 repo 的「Actions permissions」允許哪些 actions 與 reusable workflows（關係到能不能引用共用 repo）。NFC：<https://github.com/Dennis40816/nvt_fw_combiner/settings/actions>；NFH：<https://github.com/Dennis40816/FreeformHelper/settings/actions>；NFU：<https://github.com/Dennis40816/nvt-event-buffer-replay/settings/actions>
