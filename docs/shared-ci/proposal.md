# 方案：改一次，三邊生效

目標（owner 2026-10-03）：一條治理或 CI 規則只改一次，NFC、NFH、NFU 一起生效，不再人工逐一修改。現況見 [inventory.md](inventory.md)；作為例子的規則見 [approval-carryover.md](approval-carryover.md)。

## 先講結論

1. 共用的 CI 與治理放在 **`nvt_fw_core`，並改成 public**（owner 2026-10-03 08:2x 的決定，下文稱「共用 repo」）。理由：NFC、NFU 是 public，GitHub 不允許 public repo 引用 private repo 的 action 或 reusable workflow。公開前已移除本機路徑與 NFH 的內部細節。
2. **核准檢查器**做成共用 repo 裡的 composite action。各 repo 用完整 SHA 引用，升版由 Dependabot 自動在三個 repo 開 PR。
3. **ruleset** 寫成 JSON 範本，各 repo 只提供參數（分支樣式、required check 名稱）。套用由一個管理用 GitHub App 執行，私鑰只存在共用 repo 的受保護 environment，每次套用都要 owner 在 GitHub 核准；共用 repo 每週產出唯讀的漂移報告。
4. **各 repo 的差異留在各 repo 的 policy 檔**：哪些路徑高風險、R3 角色、沿用白名單。共用的是判定邏輯，不是路徑清單。
5. **試點選 NFU**：先把它的檢查器原樣搬進共用 repo、行為不變，再上第一條新規則「改動不大時，已有的核准保留」。NFH 的 `S15.005c` 直接採用共用檢查器，不要做第三份實作（owner 已讓 NFH 暫停這兩項等方案）。NFC 最後遷移，選在發佈之間的空檔。

## 範圍精簡（owner 2026-10-03 10:4x）

owner 要求避免過度設計，只保留做到「改一次、三邊生效」所需的最小部分。「改一次」靠共用 action，「三邊生效」靠 Dependabot 的升版 PR；以下項目延後，本文其他段落提到它們時，以這一節為準：

| 項目 | 處理 |
|---|---|
| 漂移報告（每週比對 ruleset、引用的 SHA、共用文字段落） | 延後，三個 repo 都採用共用檢查器後再看要不要 |
| 三個 repo 的契約測試 | 不另建；哪個 repo 採用，就在那個 PR 把它的 policy 與一兩個 fixture 加進共用測試 |
| 從 policy 產生 CODEOWNERS 的腳本 | 不做；CODEOWNERS 照現在手動維護 |
| 共用規則文字的同步 bot | 先不做；規則文字只寫在共用 action 的 README，各 repo 採用時在同一個 PR 手動改一次 `CONTRIBUTING.md` 並連到 README |
| ruleset 範本與管理用 App 的套用 workflow | 延到階段 3，只做 NFH 需要的那一份；管理用 App 到時再建。NFC、NFU 的 ruleset 現在不動 |

## 會影響設計的 GitHub 限制

| 限制 | 出處 | 影響 |
|---|---|---|
| public repo 只能引用 **public** repo 的 reusable workflow；private repo 可以引用 private 或 public 的 | [Reusable workflows 參考](https://docs.github.com/en/actions/reference/workflows-and-actions/reusable-workflows) | NFC、NFU 是 public，共用 repo 必須是 public |
| private repo 的 action 與 reusable workflow 只能分享給**同一個使用者的其他 private repo**（Settings → Actions → General → Access） | [Repository 的 Actions 設定](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository) | `nvt_fw_core` 若維持 private，只有 NFH（private）能用；FreeformHelper 計畫推出 public 版，之後也不能用 |
| `Dennis40816` 是個人帳號，沒有組織 | GitHub API `users/Dennis40816` 的 `type: User` | 沒有組織層級的 ruleset 或「required workflows」，只能每個 repo 各自套 ruleset，所以需要範本加腳本 |
| 個人帳號的 private repo 要用 ruleset 需要 GitHub Pro | 未查到明確條文，**待 owner 確認方案** | NFH 的 ruleset（`S15.005d`）能不能設 |
| GitHub App 修改 `.github/workflows/*` 需要 `workflows` 權限；NFU 的規則明文禁止 App 有這個權限 | NFU `CONTRIBUTING.md`；NFU 的 `setup-batch` 分支因含 `ci.yml` 改由 owner SSH push（commander 紀錄） | 升版 PR 若要改 workflow 檔，不能由 agent 的 App 開。Dependabot 可以 |
| 呼叫 reusable workflow 時，caller workflow 的 `env` 不會傳過去；`GITHUB_TOKEN` 權限只能降不能升；最多 10 層、單檔最多 50 個 reusable workflow | 同第一列 | 共用 workflow 的輸入要用 `with:` 明確傳 |
| 用 SHA 引用時，fork 上的 commit 也能透過原 repo 用 SHA 取得 | 一般已知的 GitHub fork network 行為 | 只能釘共用 repo 受保護 tag 所指的 SHA；漂移報告要檢查這一點 |
| dismiss stale 只在 diff 改變或 merge base 帶進新變更時撤銷 Approve | [Rulesets 可用的規則](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets) | 見 approval-carryover.md |

## 四種做法的比較

| | A. reusable workflow／composite action | B. 共用腳本做成套件或 submodule | C. ruleset 當成程式碼 | D. bot 自動開 PR 同步 |
|---|---|---|---|---|
| 做什麼 | 各 repo 的 workflow 寫 `uses: <共用 repo>/...@<SHA>` | 各 repo 安裝或掛載共用腳本，版本寫在檔案裡 | JSON 範本加套用腳本 | 共用 repo 發版後，自動在各 repo 開 PR 複製檔案 |
| 可見性 | 共用 repo 必須 public | 套件或 submodule 要 public，否則要在 CI 放讀取權杖 | 不受影響 | 不受影響，共用 repo 可以 private |
| 版本怎麼釘 | 完整 SHA 加 `# vX.Y.Z` 註解 | 鎖定檔或 gitlink 的 SHA | 範本版本加各 repo 參數檔 | 複製進去的檔案就是那個版本 |
| 更新怎麼傳到三邊 | **Dependabot**（github-actions 生態系）自動開 PR，三邊各一個 | 要自己寫 bot 或人工 | owner 對三個 repo 各執行一次腳本（同一個指令） | 自己寫的 bot 開 PR |
| 需要的權限 | 改 workflow 檔要 `workflows` 權限，Dependabot 有，agent 的 App 沒有 | 鎖定檔不在 workflows 目錄時，App 就能開 PR | repo 管理權限，只有 owner | 複製到 workflows 目錄時也要 `workflows` 權限 |
| 退回 | 在該 repo 的升版 PR 頁按 Revert | 改回鎖定檔 | 用套用前的備份還原 | 再開一個反向 PR |
| 缺點 | caller workflow 本身還是各 repo 各一份（但很薄、很少變） | submodule 讓 agent 沙盒與本機開發變麻煩；自寫 bot 要維護 | 不涵蓋檔案內容 | 各 repo 仍是複本，可能被手改而漂移；要另外檢查漂移 |
| 適合 | 檢查器、CI 步驟 | 不建議作為主要方式 | ruleset | 文件裡共用的規則段落 |

### 建議：A＋C＋D 混合

| 共用的東西 | 做法 | 各 repo 保留的部分 |
|---|---|---|
| 核准檢查器（review record 判定、owner 核准判定、accept 沿用、防偽規則） | 共用 repo 的 composite action，程式隨 action 一起被釘版（執行時在 `github.action_path` 底下）。各 repo 的 `approval.yml` 只剩觸發條件、權限與一行 `uses:` | `approval-policy.json`（路徑、分級、角色、沿用白名單），從 **base branch** 讀 |
| CODEOWNERS | 共用腳本從 policy 產生，各 repo 的 CI 檢查是否一致（NFC 已有這種測試） | 產生出來的檔案 |
| ruleset | 共用 repo 的 `rulesets/` 放範本與各 repo 參數檔。套用 workflow 由 agent 以 `workflow_dispatch` 觸發：先產出差異，owner 在 GitHub 核准 environment 後才用管理用 App 套用；套用前備份、套用後讀回（沿用 NFC G0 的程序）。agent 拿不到 App 私鑰 | 參數檔：分支樣式、required checks、strict |
| 共用的規則文字（review record 格式、沿用規則、合併前確認步驟） | 共用 repo 的文件是正本；同步腳本用 App 在各 repo 開 PR，把它複製到 `CONTRIBUTING.md` 的標記段落；漂移報告會檢查（NFC `sync_derived.py` 的模式） | 標記段落以外的內容 |
| 第三方 action 的釘版 | 各 repo 加 `dependabot.yml`（現在只有 NFC 有） | — |

各 repo 不共用、繼續各自維護的：build、test、shard、golden、C export、單檔行數門檻、覆蓋率、效能門檻、發佈包內容、韌體 parity 與發佈證據。這些和產品綁在一起，等 owner 說的「各專案有雛型後」再看要不要抽。

## 版本、傳遞與退回

1. 共用 repo 在 `main` 上發 tag `vX.Y.Z`，套用和三個 repo 相同的 tag ruleset（禁止更新與刪除 `v*`）。
2. Dependabot 在每個 repo 開升版 PR，內容只有 `uses:` 的 SHA 與版本註解。這個 PR 屬於高風險路徑（`.github/**`），照各 repo 現行規則審查並由 owner 核准。
3. 升版順序固定：NFU → NFH → NFC。前一個 repo 合併後跑過至少一個真實 PR 沒問題，才合併下一個。
4. 退回：在出問題的 repo，到那個升版 PR 的頁面按 Revert，由 owner 合併，SHA 就回到上一版。這只影響該 repo。
5. 共用 repo 出錯時發修正版（`vX.Y.Z+1`），不移動既有 tag。
6. 漂移報告（共用 repo 的排程 workflow，每週一次，只讀）：比對三個 repo 的 ruleset 與範本、引用的 SHA 是不是受保護 tag、`CONTRIBUTING.md` 的共用段落是不是與正本一致。發現差異就在共用 repo 開 issue，不改任何 repo。NFH 是 private，讀它的 ruleset 需要唯讀權杖；在 FreeformHelper 公開前，可以先不納入。

## 治理變更由誰核准

共用規則一改就影響三個 repo，等同最高風險（NFC 的 R3 `governance-owner`）。

- 共用 repo：所有路徑的 CODEOWNERS 是 owner；ruleset 要求 PR、1 個核准、code owner review、dismiss stale、last push approval，以及兩組 required checks：
  - 檢查器的單元測試。
  - **三個 repo 的契約測試**：用三個 repo 各自的 policy 與真實 PR 的 fixture，確認新版本對三邊的判定和預期相同。「改一次」在發版前就對三邊驗證過。
- 各 repo：升版 PR 再由 owner 核准一次。兩道核准：共用 repo 核准規則本身，各 repo 核准採用時機。
- 改變判定結果的版本（主版號變更）要在 `nvt_fw_core` 的 [decisions.md](../decisions.md) 記錄 owner 的決定。
- agent 可以寫共用 repo 的 PR，但任何合併都要 owner 核准；共用 repo 不給 agent bypass。

## 導入順序

| 階段 | 內容 | 動到哪裡 | owner 要做的事 |
|---|---|---|---|
| 0 | `nvt_fw_core` 以 public 建到 GitHub。檢查器 v0 是 NFU `approval_check.py` 原樣搬過去，做成 composite action，帶 NFU 的測試與 fixture（PR #1） | 只有共用 repo | 核准 PR；共用 repo 的 `main` 與 `v*` tag ruleset 手動設定 |
| 1 | **試點 NFU，規則不變**：`approval.yml` 改成 `uses: <共用 repo>/approval-check@<SHA>`；commit status 名稱維持 `governance/approval-rule`，所以 ruleset 不用改；加 `dependabot.yml` | NFU 兩個檔案 | 核准；workflow 檔要由 owner push 或交給 Dependabot |
| 2 | **試點第一條新規則：accept 沿用**。在共用 repo 實作（預設關閉），NFU 的 policy 打開並設白名單。走一次完整流程：共用 repo 改一次 → Dependabot 開 PR → NFU 生效 | 共用 repo；NFU 的 policy 與 `CONTRIBUTING.md` | 核准兩個 PR |
| 3 | **NFH 採用**：`S15.005c` 用共用 action 加 NFH 自己的 policy（先用兩層，與現行文件一致），CODEOWNERS 手動維護；`S15.005d` 的 ruleset 用範本，由管理用 App 套用（範本與套用 workflow 在這一階段才做） | NFH | 確認 private repo 能用 ruleset；建管理用 App；在 GitHub 核准套用 |
| 4 | **NFC 遷移**：檢查器 v1 支援 R0–R3 與角色（以 NFC `authority_check.py` 為底，加上 NFU 的防偽規則與從 base 執行）。選在兩次發佈之間；同步修訂 ADR 0080，並確認 `release_promotion_policy.py` 對 review 證據的要求 | NFC（全部是 R3） | 核准；調整 trunk 與 `main` 的 required check |
| 5 | 之後再看：.NET SDK 安裝、action 釘版檢查、行數檢查等 CI 步驟做成共用 action；發佈流程做成 reusable workflow | 依情況 | 等各專案有雛型 |

為什麼試點選 NFU：檢查器最小（約 350 行、只用標準函式庫）、結果用 commit status（換實作不必改 ruleset 的 check 名稱）、repo 是 public、產品優先度最低，試點出問題不會拖到 NFC、NFH。owner 舉的例子也正好是 NFU。

為什麼 NFC 最後：`authority_check.py` 超過 1,100 行，涵蓋 R3 韌體角色與發佈證據；NFC 是優先度最高的產品，治理改動全部是 R3，不適合當試點。

## owner 的決定

2026-10-03 08:2x，owner 在 NVT CORE 畫面以問答回答（第 6 題是 07:2x 在 commander 畫面回答、由 commander 轉述）。原本的選項與比較保留在本文件的歷史版本。

| # | 題目 | owner 的選擇 |
|---|---|---|
| 1 | 改動不大時保留哪一種核准 | 只保留獨立審查的 accept；owner 的 GitHub Approve 照舊在 diff 改變後失效，ruleset 不改 |
| 2 | 「改動不大」的定義 | 路徑加內容：白名單內的 `.md`／`.txt`，最多 5 個檔案、40 行，不得動 code fence、網址、HTML、不可見字元 |
| 3 | 共用 CI 放在哪裡 | `nvt_fw_core` 改成 public，一起放 |
| 4 | 共用方式 | 混合：檢查器用 composite action 加 Dependabot，ruleset 用範本，共用的規則文字用同步 PR 加漂移檢查 |
| 5 | 試點 | NFU：先搬檢查器、行為不變，再上核准保留規則 |
| 6 | NFH 的 `S15.005c`／`S15.005d` | 先暫停，等方案；定案後照階段 3 直接採用 |
| 7 | 治理變更的核准 | 兩道：共用 repo 每個 PR 由 owner 核准，各 repo 的升版 PR 再由 owner 核准 |
| 8 | 誰套用 ruleset | 另建管理用 App；私鑰只放在共用 repo 的受保護 environment，每次套用由 owner 在 GitHub 核准 |
| 9 | review record 與檢查結果的格式 | NFC 遷移時統一成一種結構化紀錄，結果用 commit status |
| 10 | trunk 命名 | 現有分支維持，後續要統一（統一成哪種形式待定，NFC、NFH 用 `X.Y.x`） |

另外有 4 件要 owner 在 GitHub 上確認的事實（NFH 的 ruleset 與帳號方案、bypass 名單、agent App 的 `workflows` 權限、各 repo 允許的 actions），連結在 [inventory.md](inventory.md) 文末。

## 這一步沒有做的事

- 沒有建立共用 repo，沒有改三個 repo 的任何檔案、ruleset、workflow，沒有開 PR，沒有 push。
- Dependabot 是否會更新 composite action 與 reusable workflow 的 SHA、GitHub 個人帳號在 private repo 使用 ruleset 的方案條件，都還沒實測或查到明確條文，要在階段 0 驗證。
