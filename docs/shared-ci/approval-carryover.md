# 規則草案：改動不大時，已有的核准保留

## 出處

owner 原話（2026-10-03 06:4x）：

> 例如 NFU 提出改動非重要的文字說明 accept 保持有效看起來不錯 但如果這種修改每次要透過人工平展效率低

owner 補充（2026-10-03 07:2x，在 commander 畫面，由 commander 轉述）：

> 應該說他之前有題更改不大 approve 會保留?

所以這條規則的來源是 owner 記得的一個概念：**改動不大時，已有的核准保留**。範圍不限於文字說明，是哪個 session 提的也不確定。**「改動不大」的定義由 owner 決定**；下面的草案只是 NVT CORE 提出的起點。

## 兩種核准

PR 上有兩種「核准」，可以分開決定要不要保留：

| | 是什麼 | 現在什麼時候失效 |
|---|---|---|
| review record 的 accept | 獨立審查 session 貼的紀錄（`Review record: <SHA> accept`、`verdict: accept`） | 檢查器要求紀錄指向目前 head，所以任何新 push 都失效 |
| owner 的 GitHub Approve | owner 在 GitHub 按的 Approve，高風險 PR 才需要 | ruleset 的 dismiss stale：PR 的 diff 改變就失效 |

草案建議**只保留 accept**，不改 ruleset。理由見「為什麼不保留 owner 的 Approve」。

## 「改動不大」可以怎麼定義

| 定義 | 好處 | 風險 |
|---|---|---|
| 甲、只看路徑：差異只落在白名單裡的文件 | 規則簡單，好判定 | 一份文件裡也能藏進實質變更 |
| 乙、路徑加內容（草案採用）：白名單內的 `.md`／`.txt`，有行數上限，不得動 code fence、網址、HTML、不可見字元 | 擋掉已知的藏法 | 合法的大段文件修改還是要重新審查 |
| 丙、只看大小：任何路徑，差異不超過 N 行 | 也涵蓋小的程式修正 | 一行程式就能改變韌體 bytes 或核准規則；NFC 的 R3 與 NFU 的 owner-gated 路徑不能用這個定義 |

## 現況：任何新 head 都要重新審查

| | NFC | NFU | NFH |
|---|---|---|---|
| review record 綁定 | `commit_id` 與區塊裡的 `head` 都要等於目前 head（`authority_check.py`） | 第一行的 SHA 要等於目前 head（`approval_check.py` 的 `record_result`） | 還沒有檢查器 |
| owner 的 Approve | ruleset dismiss stale：diff 改變就失效 | 同左；檢查器另外要求 `commit_id` 等於目前 head | 未確認 |
| 文件條文 | ADR 0080：任何新 SHA 都要新的 review record，tree 相同也一樣 | `CONTRIBUTING.md`：After any later push, review again and post a new record | `CONTRIBUTING.md`：合併前核對目前 head |

GitHub 的 dismiss stale 只在「PR 的 diff 改變」或「merge base 帶進新變更」時才撤銷 Approve，不是每個 push 都撤。

## 規則草案（定義乙）

### 名詞

- **A**：最新那份 accept 紀錄所指的 head（已審過的 head）。
- **H**：PR 目前的 head。
- **淨變更**：PR 相對於目前 base 改了哪些檔案、每個檔案改成什麼內容。

### 判定步驟

1. 找出最新的 review record（找法和現在一樣，格式錯、reject、dismissed 都照舊擋下）。它是 accept、指向 A，且 A ≠ H，才進入沿用判定。
2. 用 GitHub compare API 取兩份淨變更：`compare/<目前 base>...A` 與 `compare/<目前 base>...H`。逐檔比對狀態、blob SHA、舊檔名，三者都相同的檔案視為沒變。其餘檔案組成**差異集 D**。
   - 這個比法同時涵蓋「在 A 後面追加 commit」和「rebase 或併入新的 base」兩種情況。
   - base 也改過、PR 也改過的檔案會出現在 D 裡，由下一步判定；判定不過就重新審查（保守）。
   - compare 結果被截斷（超過 300 個檔案）或 A 已經取不到時，一律不沿用。
3. D 裡的**每一個**檔案都符合下列條件，accept 才沿用到 H：
   1. 路徑符合該 repo 設定的沿用白名單，而且不符合任何高風險樣式（NFU 的 owner-gated、NFC 的 R1 以上、NFH 的高風險範圍）。白名單與高風險樣式都讀 **base 那一份** policy，PR 不能自己放寬。
   2. 狀態是修改或新增；刪除、改名、複製不沿用。
   3. 檔案模式不變，而且不是 symlink、submodule 或可執行檔。
   4. 副檔名是 `.md` 或 `.txt`。
   5. 新增或刪除的行都不在 code fence 裡，也不新增或修改網址、HTML 標籤或 HTML 註解，不含不可見的格式字元（Unicode `Cf` 類，例如零寬字元、雙向控制字元）。
   6. D 的總量不超過上限：預設 5 個檔案、40 行（新增加刪除）。
4. 沿用成立時，檢查結果寫明「accept 沿用自 A」，並列出 D 的檔案與行數，讓 owner 和合併的 agent 看得到。
5. 一律拿 H 和**最後一份真正審過的 A** 比，不會一份沿用接一份沿用地累積。

### 各 repo 的白名單（預設值，要各 repo 確認）

| Repo | 可以沿用的路徑 | 必須排除 |
|---|---|---|
| NFC | policy 裡 floor 為 R0 的路徑（現在只有 `docs/handoff/**/*.md`） | 其他全部；R1 的 `docs/handoff/1.1.12.md` 被測試讀取，不能沿用 |
| NFU | `README.md`、`docs/**/*.md` | `docs/adr/**`、`docs/product-spec.md`、`docs/release.md`、`docs/nvt-fw-util-claude-handoff.md`（agent 必讀）、`TODO*.md`（工作清單）。注意：NFU 的 `AGENTS.md` 規定 `docs/` 下其他文件也是產品契約，白名單可能要再縮小 |
| NFH | 交接與審查紀錄類的 Markdown（確切路徑由 NFH 確認） | 治理文件、roadmap 與 golden 規則、效能基準、工作清單 |

## 風險與對策

| 風險 | 例子 | 對策 |
|---|---|---|
| 把實質變更藏在文字裡 | 改 README 裡的安裝指令或下載網址 | 條件 3.5：不得動 code fence 與網址 |
| 對 agent 的隱藏指示 | 在 Markdown 加 HTML 註解，畫面上看不到，agent 讀得到 | 條件 3.5：不得動 HTML 標籤或註解，不得有不可見字元 |
| 改到規則或契約 | 改 ADR、產品規格、治理文件、agent 必讀文件 | 白名單排除；高風險樣式優先於白名單 |
| 文件其實是程式的輸入 | NFC 有測試讀取 `docs/handoff/1.1.12.md` | 白名單只收不被程式讀取的文件；NFC 直接沿用 R0 的定義 |
| PR 自己放寬白名單 | PR 同時改 policy 與文件 | 讀 base 的 policy；改 policy 本身屬於高風險路徑 |
| 大改寫 | 整份文件重寫 | 5 個檔案、40 行的上限 |
| 和新 base 的語意衝突 | rebase 後別的 PR 改了同一份文件 | 該檔案會進入 D，不在白名單就重新審查；CI 照常在 H 上重跑 |
| 檢查器本身被改 | PR 改 `approval_check.py` | 已是高風險路徑；NFU 從 base 執行檢查器；共用後由共用 repo 的固定版本執行 |

## 為什麼不保留 owner 的 Approve

要讓 owner 的 Approve 在文字改動後也保留，必須在 ruleset 關掉 `dismiss_stale_reviews_on_push`，改由檢查器自己判斷。代價：

- GitHub 原生的保護會對**所有**變更失效，不只文字。之後全靠檢查器把關。
- 檢查 workflow 的定義是從 PR 的 merge ref 讀的（NFU 的 `CONTRIBUTING.md` 已寫明這個限制）。現在還有 GitHub 原生的 dismiss stale 當第二道防線，關掉後就只剩一道。
- owner-gated 的 PR 在文字改動後重新 Approve，owner 只需在 GitHub 按一次；省下的主要是審查 session 的時間，而那部分沿用 accept 就省到了。

建議第一階段只沿用 accept，不改 ruleset。

## 套到各 repo 要改的地方

| Repo | 要改的檔案 | 備註 |
|---|---|---|
| NFU | `scripts/approval_check.py`（`record_result` 加沿用判定，需要讀 compare API）、`tests/scripts/test_approval_check.py` 與 fixtures、`.github/approval-policy.json`（新增 `review_carryover` 設定）、`CONTRIBUTING.md`「Change sequence」第 5 步與「Review record」、`.github/pull_request_template.md` | 全部屬於 owner-gated。ruleset 不用改 |
| NFC | `scripts/authority_check.py`（review record 的 head 比對）、`tests/scripts/test_authority_check.py`、`docs/governance/authority-policy.schema.json`（若新增設定）、`docs/adr/0080-governance-reset.md`（要修訂「tree 相同也要新紀錄」那條）、`AGENTS.md` 的 Risk-adaptive gates、`.github/pull_request_template.md` | 全部是 R3 `governance-owner`。另外要確認 `scripts/release_promotion_policy.py` 收集發佈證據時，是否也要求 exact-head 的 review record；如果是，沿用的紀錄會讓發佈資格判定失敗 |
| NFH | 還沒有檢查器。`S15.005c` 若採用共用的檢查器，就直接內建這條規則；只需在 `CONTRIBUTING.md` 合併邊界一節寫明 | 見 [proposal.md](proposal.md) 的導入順序 |

如果三個 repo 各自實作，上表就是三份程式、三份測試、三份文件，正是 owner 說的「人工平展效率低」。共用之後，判定邏輯寫在共用的檢查器裡一次，各 repo 只改 policy 裡的白名單與文件。

## 要 owner 決定的事

見 [proposal.md](proposal.md) 的「要 owner 決定的事」第 1、2 題。
