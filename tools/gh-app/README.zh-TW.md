# 共用 GitHub App 寫入模組

這個 PowerShell 模組透過儲存庫的 GitHub App 執行寫入。既有腳本保持原狀。
執行環境需要 PowerShell 7.4 以上、GitHub CLI，以及 owner 安裝的 token helper。合併證明需要 Git 2.38 以上。
離線測試需要 Windows 與 Pester 3.4.0。

[templates/review-template.md](templates/review-template.md) 是獨立、唯讀的審查提示，使用佔位值 `{WORKTREE}`、`{BASE}`、`{HEAD}`、`{REPORT}`、`{TESTLOG}`。

## 設定與載入

Owner 在所有儲存庫之外建立 `~/.nvt/gh-app/<owner>-<repo>.json`。
設定只記錄識別資料與路徑。以下全部都是佔位值。

```json
{
  "owner": "<OWNER>",
  "repo": "<REPO>",
  "clientId": "<CLIENT_ID>",
  "installationId": "<INSTALLATION_ID>",
  "tokenHelperPath": "<ABSOLUTE_HELPER_PATH>",
  "dpapiPath": "<ABSOLUTE_DPAPI_PATH>",
  "botLogin": "<APP_SLUG>[bot]"
}
```

路徑必須是絕對路徑。Installation ID 必須是正整數，或其十進位字串。
Helper 必須是已安裝的 `.ps1`。缺少欄位會中止載入。
載入失敗會清除目前設定。傳回的設定物件無法修改模組目前使用的設定。

### 新儲存庫不需要手建設定檔

Owner 可以在所有儲存庫之外建立一個 owner 預設檔：`~/.nvt/gh-app/<owner>.owner-default.json`。
欄位與儲存庫設定檔相同，但沒有 `repo`。
`Import-GhAppConfig` 先讀 `<owner>-<repo>.json`。
只有該檔不存在時，才讀 owner 預設檔，並代入要求的儲存庫名稱。
有自己設定檔的儲存庫不會用到預設檔。
兩個檔都不存在時，錯誤訊息會列出兩個預期路徑。

Owner 預設檔的檢查與儲存庫設定檔完全相同。
它的 `owner` 必須等於要求的 owner，且不得含 `repo` 欄位。
檔案、helper 與 DPAPI 檔都必須在所有儲存庫之外。
Token helper 仍把每個 token 限縮在呼叫中指定的單一儲存庫。
Review ledger 與 issues 寫入記錄不能指向 owner 預設檔。

每個函式都接受選用的 `-Repository OWNER/REPO`，別名是 `-Repo`。
不同的儲存庫參數或 `GH_REPO` 會在執行 `gh` 前中止。

```powershell
Import-Module .\tools\gh-app\NvtGhApp.psd1
Import-GhAppConfig -Owner OWNER -Repo REPO
```

Helper 透過獨立的 `pwsh -NoProfile -NonInteractive -File` 程序執行。
模組傳入 `-Mode token`、`-Owner`、`-Repo`、`-ClientId`、`-InstallationId` 和 `-DpapiPath`。
Helper 只能在 stdout 傳回一個不含空白的 ASCII token，並以零結束。
Owner 負責審查 helper、安裝於儲存庫之外，並限制 token 只能操作指定儲存庫。

## 函式與範例

`Push-GhAppBranch` 把本機 `LocalBase..HEAD` 的樹差異重建為一個遠端 commit。
它處理二進位檔案、Unicode 檔名、檔案模式、symlink、gitlink 和刪除。
未提交的工作目錄變更不會進入 commit。
HEAD 必須包含 `LocalBase`。否則從較舊 base 切出的分支，會把 base 之後新增的檔案全部刪掉。函式在任何 `gh` 呼叫前檢查這點，所以請先 rebase 到遠端 base。
本機 base tree 必須等於遠端 parent tree。所有 SHA 必須是 40 個小寫十六進位字元。
更新 ref 前，產生的 tree 必須等於本機 HEAD tree。既有 ref 只允許非強制前進。
只有 HTTP 404 允許建立缺少的 ref。`-ExtraParent` 加入第二個 parent。

推送 workflow 檔案：GitHub 要求 `workflows` 權限，才能建立改動 `.github/workflows/` 底下檔案的 tree。
模組預設不要求這個權限，其他推送完全不變。
加上 `-IncludeWorkflowsWrite` 與 `-WorkflowsLogPath <檔案>`，才為單次推送要求它。

- 開關預設關閉，每次推送各自決定。
- 推送的 tree 沒有改動 `.github/workflows/` 底下的檔案時，函式在任何 `gh` 呼叫前拒絕。
- 每次使用在紀錄檔追加一行：UTC 時間、分支、檔案數、workflow 檔案數。這行不含 token。
- 只有該次推送的 token 請求，會多傳 `-IncludeWorkflowsWrite` 給 helper。儲存的設定不變，也不做快取。
- GitHub 沒有把 `workflows` 授予安裝時，helper 會回報，函式只顯示這一句固定訊息：`Installation token lacks the requested workflows permission.` 這時由 owner 到安裝頁接受新權限。

```powershell
Push-GhAppBranch -Worktree . -LocalBase '<LOCAL_BASE_SHA>' `
    -RemoteParent '<REMOTE_PARENT_SHA>' -Branch 'build/example' `
    -MessageFile .\message.txt -IncludeWorkflowsWrite -WorkflowsLogPath .\workflows-write.log
```
已開啟 PR 需要 `-AllowOpenPr`。任何 owner review 都會禁止 push，即使有這個選項，只有兩個例外。
兩個例外都保留 owner 看過的內容，也都仍需要 `-AllowOpenPr`：

- `base-merge`：推出的 commit 是舊 head 加一個乾淨的 base merge。舊 head 是審核請求的 head，或從它出發的乾淨鏈。第二個 parent（`-ExtraParent`）已包含在 base 分支裡。推出的樹與重算的 merge 樹相同。這與合併證明的規則相同，不需要 patch-id。
- `rebase`：推出的 commit 的 patch-id 等於審核請求記下的 patch-id，而且與請求 head 各自合併進目前 base 後的 tree 相同。沒有 patch-id 的舊登記無法證明，所以會被拒絕。

推出的 commit 還不存在，所以函式在本機建立一個樹與 parent 相同的探測 commit。探測 commit 不改任何 ref。
`rebase` 除了 patch-id 之外，還要求整合後的 tree 相同：請求的 head 與探測 commit 各自合併進目前 base，必須得到同一棵 tree。只比 patch-id 不夠，因為它忽略空白與行號。
函式只讀一次 `HEAD`。檢查、diff 與發布的 commit 都用這一個 commit，即使 `HEAD` 在中途移動也一樣。
函式從 ledger（`-LedgerPath`，預設值與其他函式相同）讀審核請求。
符合例外時，函式在推送前往 push log 附加一行：`<時間> push after owner review kind=base-merge|rebase pr=#N branch=B parents=...`。檔案是 `-PushLogPath`，預設為 ledger 旁的 `push-after-review.log`。回傳物件的 `OwnerReviewPush` 帶有種類。
其他在 owner review 之後的推送一律以 `#N has an owner review; push refused. Open a follow-up PR instead.` 拒絕，包括內容有改、ledger 沒有該 PR 的登記，以及任何無法證明內容的情況。

```powershell
Push-GhAppBranch -Worktree . -LocalBase '<LOCAL_BASE_SHA>' `
    -RemoteParent '<REMOTE_PARENT_SHA>' -Branch 'feature/example' `
    -MessageFile .\message.txt -ExtraParent '<SECOND_PARENT_SHA>'
```

`New-GhAppPullRequest` 從檔案載入描述並建立 PR，然後讀回作者。
作者不等於 `botLogin` 時，它會傳回含完整 PR URL 的錯誤。PR 預設保持開啟。
加上 `-CloseOnWrongAuthor` 時，作者不符會先透過 App 關閉 PR，再拋出錯誤。
`-Base` 預設為 `main`。`-Draft` 建立草稿。Head 與 base 都必須是設定儲存庫中的分支。
沒有加 `-Draft` 的 PR 一開出來就是 ready，函式會立刻登記 owner 審查請求，作法和 `Request-GhAppOwnerReview` 相同。
傳回物件的 `ReviewRequest` 屬性是記錄的 entry；草稿是 `$null`。草稿一律不登記。
`-LedgerPath` 指定 ledger；`-Worktree` 會加上 `Request-GhAppOwnerReview` 一節說明的 patch-id。

```powershell
New-GhAppPullRequest -Head 'feature/example' -Title 'Add shared behavior' `
    -BodyFile .\pr-body.txt -Base main -Draft
```

`Set-GhAppPullRequestBody` 使用檔案內容取代 PR 描述。
加上 `-ExpectedHeadSha` 時，函式先讀取 PR，head 不同就在寫入前停止。
這只是寫入前的檢查，不是鎖。PATCH 帶不了預期的 head，所以檢查與 PATCH 之間 head 若改變，新內容仍會寫進去。
用它來擋下為舊 head 寫的描述，例如引用 head、CI run 或 review 的描述。它擋不住同一時刻的 push。

```powershell
Set-GhAppPullRequestBody -Number 7 -BodyFile .\pr-body.txt -ExpectedHeadSha '<EXPECTED_HEAD_SHA>'
```

`Update-GhAppPullRequestBranch` 透過 `PUT pulls/{n}/update-branch` 把 base 分支合併進 PR 分支。
它把 `-ExpectedHeadSha` 當作 `expected_head_sha` 送出，所以 GitHub 也會拒絕已移動的 head。
新 head 會讓送審 request 與 review record 失效，因此遇到以下情形，函式在任何寫入前停止：

- PR 不是開啟狀態，或作者不是 `botLogin`;
- head 不是 `-ExpectedHeadSha`;
- Review ledger 有這個 PR 的 entry，也就是它已送 owner;
- Owner 已經審查過這個 PR。

草稿 PR 可以更新。呼叫後函式每隔 `-PollSeconds`（預設 5）輪詢，直到 head 改變，最久 `-TimeoutSeconds`（預設 120），並傳回 `Number`、`OldHead`、`NewHead`。
逾時表示 GitHub 已接受更新但 head 還沒變，再次呼叫前先檢查 PR。
`-LedgerPath` 必須與 `Request-GhAppOwnerReview` 寫入的是同一個 ledger。
`Merge-GhAppApprovedPullRequest` 會在核准後自己更新落後的分支並證明結果，owner 已看過的 PR 不要呼叫這個函式。

```powershell
Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha '<EXPECTED_HEAD_SHA>'
```

`Add-GhAppComment` 使用檔案內容新增 PR 或 issue 留言。

`-IncludeIssuesWrite` 為單次 issue 留言要求 `issues: write`，預設關閉，每次呼叫各自選用。每次使用前都必須取得 owner 同意，並以 `-IssuesLogPath` 指定儲存庫外的紀錄檔；每次使用會在發送前追加一行 UTC 紀錄。目標是 PR 時會拒絕，因為 PR 留言不需要此權限。

```powershell
Add-GhAppComment -Number 7 -BodyFile .\comment.txt
```

`Add-GhAppReviewRecord` 先核對 PR head，再把呼叫端提供的 review record 發送至 review API。
`commit_id` 必須等於核對的 head。Event 永遠是 `COMMENT`。
`APPROVE`、`REQUEST_CHANGES` 與其他 event 都會在任何網路呼叫前被拒絕。只有 owner 可以批准。

```powershell
Add-GhAppReviewRecord -Number 7 -ExpectedHeadSha '<EXPECTED_HEAD_SHA>' `
    -BodyFile .\review-record.txt
```

## Sync-GhAppLabels

這個函式讓定義檔裡的標籤在一個 repo 中存在。
`tools/gh-app/labels.json` 是共用標籤名稱、顏色與說明的單一來源。要新增標籤，先改這個檔，再到每個 repo 各跑一次同步。

每個標籤只有 `name`、`color`、`description` 三個欄位。名稱只能有英數字、`.`、`_`、`-`，不能有空白。顏色是六位十六進位數字，不加 `#`。說明最多 100 個字元。

沒有 `-Apply` 時只讀取，回傳每個標籤的計畫：`create`、`update` 或 `unchanged`。它不會刪除標籤，也不理會定義檔沒有列出的標籤。

`-Apply` 建立或更新有差異的標籤。這些呼叫會要求 `issues: write`，所以每次使用前都要 owner 同意。它需要放在 repo 外的 `-IssuesLogPath`，並在每次變更前寫一行 UTC 紀錄。

```powershell
Sync-GhAppLabels -DefinitionPath .\tools\gh-app\labels.json
Sync-GhAppLabels -DefinitionPath .\tools\gh-app\labels.json -Apply -IssuesLogPath ..\issues-write.log
```

`tools/gh-app/sync-labels.ps1 -Owner OWNER -Repo REPO [-Apply -IssuesLogPath PATH]` 會載入 repo 設定檔並執行這個函式。

## Get-GhAppPullRequestReadiness

這個函式檢查 PR 能不能送 owner。它只讀取，不寫入 GitHub。
它傳回 `Number`、`Head`、`Ready`、`Checks` 與 `Files`。
沒有任何檢查失敗時 `Ready` 為 true。`Checks` 的每一列有 `Name`、`Result`（`PASS` 或 `FAIL`）與說明原因的 `Detail`。
一項失敗不會讓其他檢查停止，所以一次呼叫就列出所有問題。
`Files` 列出變更的檔案及新增、刪除的行數。

| 檢查 | 通過條件 |
| --- | --- |
| `State` | PR 是開啟狀態 |
| `Draft` | PR 不是草稿 |
| `Author` | 作者是 `botLogin` |
| `Head` | head 等於 `-ExpectedHeadSha`;沒有這個參數時，這一列只顯示 head |
| `MergeState` | merge state 是 `CLEAN`、`BLOCKED`、`UNSTABLE` 或 `HAS_HOOKS`。Owner 核准前 `BLOCKED` 是正常的。`BEHIND` 不通過，因為合併時 head 會改變。`DIRTY` 與 `UNKNOWN` 不通過。 |
| `ReviewRecord` | head 這個 commit 上最新的結論是接受：狀態為 `COMMENTED` 或 `APPROVED` 的 review，第一個非空行符合 pattern |
| `Run: <workflow>` | 該 workflow 在這個 head 上最新的一次 run 已完成，結論是 `success` |

各 repository 的 review record 格式不同，所以模組不寫死格式。
`-ReviewRecordPattern` 是正規表示式，只對 review 內容的第一個非空行區分大小寫比對。前面的空白行會略過，第一個非空行是拒絕、後面才引用接受的 review 不會通過。
預設格式也讓模組知道拒絕長什麼樣子（`Review record: <head> <其他結論>`）。自訂 pattern 只描述接受，所以用自訂 pattern 時，該 head 上較晚的、有內文的 review，或被 dismissed、或是 `CHANGES_REQUESTED` 的 review，都算一次結論，會取消較早的接受。沒有內文的 review（例如 inline comment 的容器）不算。
狀態為 `PENDING` 或 `DISMISSED` 的 review 永遠不算接受。同一個 head 上較晚的結論會取代較早的：`Review record: <head> <其他結論>`、`CHANGES_REQUESTED` review 或被撤銷的紀錄，都會取消它之前的接受；之後再出現新的接受就重新算。
Pattern 裡的 `{HEAD}` 會換成 head SHA。
預設是 `^Review record: {HEAD} accept\b`。
Record 寫成 `Verdict: accept` 的 repository，傳 `-ReviewRecordPattern '^Verdict: accept$'`。

判斷依據是每個 workflow 最新的一次 run，不是 PR 的狀態 rollup。
Review record 貼在 CI 之後時，PR 的 rollup 可能永遠留著一個較舊的失敗 run，而 App 不能重跑它（見[App 沒有的權限](#app-沒有的權限)）。
函式讀取這個 head 的每一頁 run，最多 1,000 次。它依 workflow 分組，取每組最新的一次，並說明略過了幾次較舊的 run。
分組的鍵是 workflow ID，不是名稱，因為兩個 workflow 可以同名。每一列顯示名稱，所以兩列可能看起來一樣。
讀到的 run 比 GitHub 回報的少時，`Runs` 這一列判 FAIL：沒讀到最新 run 的 workflow 可能藏著失敗。

```powershell
$readiness = Get-GhAppPullRequestReadiness -Number 7 -ExpectedHeadSha '<EXPECTED_HEAD_SHA>'
$readiness.Checks | Format-Table Name, Result, Detail -AutoSize
```

## Request-GhAppOwnerReview

Session 把 PR 送 owner 審查時，呼叫此函式記錄當下的 head。
PR 必須在設定的 repository 內、保持開啟、不是草稿，且作者等於 `botLogin`。
函式只讀取 GitHub，不執行 GitHub 寫入，並傳回已記錄的 entry。
送審的新 head 需要重新呼叫。

寫入 ledger 之前，有兩個選用的檢查:

- `-ExpectedHeadSha` 在 head 不同時停止呼叫。
- `-CheckReadiness` 對 ledger 即將記錄的 head 執行 `Get-GhAppPullRequestReadiness` 的檢查，有一項失敗就停止，訊息列出每一項失敗的檢查。`-ReviewRecordPattern` 會傳給檢查；沒有 `-CheckReadiness` 時使用它會被拒絕。

預設 ledger 是設定檔旁的 `~/.nvt/gh-app/review-ledger.jsonl`。
預設位置與選用的 `-LedgerPath` 都必須在所有 repository 之外。
寫入只附加，不覆寫；每行一個 JSON object，採 UTF-8 且沒有 BOM。
欄位包含 `owner`、`repo`、`number`、`head`（40 字元 SHA）、`requestedAt`（UTC ISO 8601）與 `base`（分支名稱）。
Entry 還可以有 `patchId`；`Set-GhAppPullRequestBase` 寫入的 entry 另有 `registeredAt`、`reason` 與 `fromBase`。
Ledger 保持私有並保留舊紀錄。送審與合併必須使用相同的 `-LedgerPath`。

`-Worktree` 指定具完整歷史的本機 clone。加上它，entry 會多一個 `patchId`。
Patch-id 是「head 與 base 的 merge base 到 head 的 diff」的 `git patch-id --stable`。
它代表 owner 看到的內容。Rebase 或改 base 只要內容不變，值就不變。
沒有 `-Worktree` 時 entry 沒有 `patchId`，之後改 base 就無法證明無害。

```powershell
Request-GhAppOwnerReview -Number 7 -Worktree .
```

## Set-GhAppPullRequestReady

此函式一次完成「草稿轉 ready」與「登記 owner 審查請求」。
請用它取代手動轉 ready，這樣不會留下沒有紀錄的 ready PR。
PR 必須開啟、作者是 `botLogin`、仍是草稿，且 head 等於 `-ExpectedHeadSha`。
`-Worktree` 必填，所以 entry 一定有 `patchId`。
App 透過 GraphQL 轉 ready，函式再記錄 entry。
已經是 ready 的 PR 會被拒絕，因為再登記一次會讓 owner 已給的核准失效。這種 PR 請用 `Request-GhAppOwnerReview`。轉 ready 失敗時，不記錄任何東西。

```powershell
Set-GhAppPullRequestReady -Number 7 -ExpectedHeadSha '<HEAD_SHA>' -Worktree .
```

## Set-GhAppPullRequestBase

此函式改 PR 的 base，並保留審查請求。Session 改 base 一律用它。
用其他方式改 base，ledger 內的請求仍寫舊 base。
PR 必須開啟、作者是 `botLogin`、head 等於 `-ExpectedHeadSha`，而且還不是 `-NewBase`。
`-Worktree` 與 `-LogPath` 必填。

沒有審查請求的 PR（草稿）只改 base 並寫一行日誌，不寫 entry。
有審查請求的 PR，函式先證明內容不變：

1. 最新的請求必須是目前的 head，而且有 `patchId`。
2. 函式讀 `-NewBase` 的最新位置，算出 head 相對它的 patch-id。
3. Patch-id 不同時，函式在改任何東西**之前**就停止。它寫下拒絕的日誌並說明哪兩個值不同。Owner 必須重新核准，session 登記新的請求。
4. Patch-id 相同時，App 改 base，函式追加一筆含新 base 的 entry。

新 entry 沿用原本的 `requestedAt`，所以 owner 在原請求之後給的核准仍然有效。內容沒有變，owner 看到的是同一份 diff。
Entry 另記 `registeredAt`（實際時間）、`reason`（`base-change`）與 `fromBase`。
日誌那一行寫出新舊 base、patch-id、沿用的時間與新的登記時間。
若 base 在檢查與修改之間又動了，且 patch-id 因此不同，函式會在修改之後拋出錯誤，並且不保留請求。

GitHub 可能在改 base 時撤銷核准。模組不關這個設定。
這時 owner 再核准一次；新核准晚於原請求，合併會接受它。

```powershell
Set-GhAppPullRequestBase -Number 7 -NewBase main -ExpectedHeadSha '<HEAD_SHA>' -Worktree . -LogPath .\base.log
```

## Merge-GhAppApprovedPullRequest

`Merge-GhAppApprovedPullRequest` 依序處理 PR。已關閉或合併的 PR 會略過。
遇到第一個不安全操作或失敗請求時，整批處理停止。
10-05 規則允許在核准後乾淨合併 base，必要 checks 通過後保留原核准。
人工解衝突必須重新取得 owner 核准並記錄新的送審 request。
GitHub 可能把 review 的 `commit_id` 移到新 head，因此不能用該欄位證明 owner 審查的版本。
模組信任 session 送審時記錄的 head。

以下三個條件必須同時成立：

1. Ledger 內此 owner、repository 與 PR 最新附加的 entry 提供送審 head S 與時間 T。
2. Owner 最新會改變狀態的 review 是 `APPROVED`，而且提交時間嚴格晚於 T。依提交時間排序，同時間以 review ID 排序。`COMMENTED` 不影響核准；`CHANGES_REQUESTED` 與 `PENDING` 禁止合併。`DISMISSED` 也禁止。核准的 `commit_id` 也必須是 S,或 S 之後經證明的 merge。T 取自本機時鐘，本機時鐘比 GitHub 慢時，這個綁定可以防止舊 head 的核准被算成 S 的核准。
3. 從目前 head H 沿 first parent 回走，20 個 commit 內到達 S。S 之後每個 commit 都恰有兩個 parent，second parent 必須包含在目前 PR base 中。Base 分支名稱必須和送審紀錄相同。例外：送審紀錄有 `patchId`、H 就是 S，而且 H 相對目前 base 的 patch-id 相同時，接受改過的 base 名稱。

送審 request 之後的每個 merge 都會使用 `git merge-tree --write-tree P1 P2` 重算。
重算必須沒有衝突，tree 必須恰好等於 merge commit 的 tree。
合併證明需要 Git 2.38 以上；舊版會清楚中止。
只比較 blob 不足以證明乾淨合併：衝突若完全採用 base 的檔案，blob 與模式仍可相同；若完全採用 first parent，diff 甚至會是空的。
這兩種人工解衝突仍須 owner 核准。

### 核准只沿用一次

歷史變了、內容沒變時，owner 的核准可以再算一次。情況是 **rebase**：H 不是 S，也不是從 S 出發的乾淨 merge 鏈。核准所在的 commit 稱為 A，就是 owner 核准的 `commit_id`。
下面三點都要成立：

1. 送審紀錄有 `patchId`，而且 H 相對目前 base 的 patch-id 相同。這只是初篩：`git patch-id` 忽略空白與行號。
2. A 就是 S，或 A 通過從 S 出發的乾淨 merge 證明（也就是 owner 看過的歷史）。
3. A 合併進目前 base 的 tree，與 H 合併進目前 base 的 tree 完全相同（`git merge-tree --write-tree`）。Tree id 涵蓋每個位元組、路徑與模式。任一邊有衝突就停止合併。

送審紀錄沒有 `patchId` 時維持舊規則：rebase 需要新的核准。
被撤銷（`DISMISSED`）的核准一律不算，不論撤銷訊息寫什麼，都禁止合併。

合併會用掉這份核准。模組在合併確認之後才寫紀錄，所以停止的合併不會用掉核准。
紀錄寫在它所屬的請求下方：
`{"kind":"reuse","owner":...,"repo":...,"number":...,"mode":"tree-equal","requestedAt":T,"oldHead":S,"newHead":H,"approvalId":...,"approvalCommit":A,"patchId":...,"reusedAt":...}`。

- 紀錄只用來限制，不會讓任何 head 通過。每個 head 都從送審紀錄、核准與內容重新證明。
- 紀錄用 `oldHead` 與 `requestedAt` 綁定到請求。與最新請求不符的紀錄會被忽略。
- 同一份核准已被紀錄用在別的 head，又要 rebase 到另一個 head 時停止：`owner approval <id> was already reused for <head>; a new owner approval is required`。
- Patch-id 不同時停止，訊息要求新的送審紀錄。合併後的 tree 與核准 commit 不同（例如只改空白）也一樣停止。
- 同一個 PR 同一時間只能有一個合併呼叫。ledger 旁的鎖檔（`<ledger>.<owner>_<repo>_<number>.lock`）讓第二個呼叫以 `another merge of this pull request is running` 停止。

合併日誌多一行 `#N owner approval <id> reused from <S> (tree-equal); merged head <H>`。

證明只對一個 base 成立，而 GitHub 的 merge API 只綁定 head。所以 reuse 合併是縮小風險視窗，不是關閉它：

- 送出合併前一刻，函式再讀一次 PR。base 與證明時不同就以 `the base branch moved after the reuse proof` 停止。不送合併、不用掉核准。請重新證明或要求新的核准。
- 合併後，比較 merge commit 的 tree 與「證明時的 base + 合併的 head」的 tree。不同時寫入日誌 `TREE MISMATCH after reuse`、發出警告，回傳值的 `TreeMismatch` 為 `$true`。合併無法撤回、核准已用掉，請通知 PC-Commander。
- 函式對每個已合併的 PR 回傳一個物件：`Number`、`Head`、`MergeCommit`、`Reused`、`TreeMismatch`。
- 已知限制：最後一次讀取到合併之間，base 仍可能移動。GitHub 沒有以 base 為條件的伺服器端機制。
PR 的 ledger 出現未知 `kind` 時停止合併，因為舊版讀取器無法理解它。

必要參數 `-Worktree` 指定具完整歷史的本機 clone。
模組使用 owner 的正常 Git 憑證，從設定的 repository fetch 所需 commit objects。
本機 refs、`FETCH_HEAD`、index 與工作檔案保持原狀。重算會在 object database 寫入 tree objects。
S 之後出現非 merge commit、second parent 不在 base、merge tree 被修改，或超過 20 個 commit，都會停止。

`BEHIND` 時，App 呼叫 `PUT pulls/{n}/update-branch`，`expected_head_sha` 等於 H。
每個 PR 最多更新三次。更新失敗或 `DIRTY` 會以「manual resolution requires owner approval」中止。
更新後先證明新 head，再等待該 head 的必要 checks。
模組等待必要 checks 通過及 merge state 為 clean，並在合併前重新核對 ledger、核准與歷史，再以 `sha` 等於 H 合併。
repository 可能沒有設定必要 checks。這時 `gh pr checks --required` 沒有輸出、訊息為 `no checks reported on the '<branch>' branch`（gh 2.92.0；舊寫法是 `no required checks reported on the '<branch>' branch`）、結束碼為 1。模組只把這個明確的回答當成一種狀態，不當成失敗，改讀該 head 的全部 checks。
比對的是整句訊息，區分大小寫，且結束碼要是 1。前後多出文字、有第二行、結束碼不同或大小寫不同，都算失敗。
其他沒有清單的失敗（登入失敗、斷線、不認得的訊息）在兩次呼叫中都以 `HTTP unknown` 停止合併，絕不當成「沒有 checks」。
其中有失敗或取消就停止（`checks failed`），有進行中就等待。
該 head 完全沒有 check（第二次呼叫也是同一句訊息）時，由現行 ruleset 判斷，merge state 為 `CLEAN` 代表條件已滿足。
失敗或取消的 checks 會停止。預設逾時為 1,200 秒，輪詢間隔為 15 秒。
可使用 `-TimeoutSeconds` 和 `-PollSeconds` 修改這些限制。
合併後是否刪除分支，要用 `-DeleteBranchPrefix` 明確指定。沒有指定時，所有分支都保留，包括 release 分支，函式會對每個保留的分支印出警告。
之後要刪除這類分支，用 `Remove-GhAppMergedBranch`。
只能傳 owner 已授權可刪除的分支類別。刪除其他分支前，要先把分支名稱和 head 送 owner 確認。
有指定時，以下條件全部成立才刪除:

- 分支名稱以該 prefix 開頭;
- 分支不是 `main`,也不是 PR 的 base;
- ref 仍指向合併的 head;
- PR 的 base 已包含該 head。

`-LogPath` 的目錄必須已存在。紀錄會附加 UTC 時間、head SHA、merge SHA 及分支處理結果。
核准紀錄格式是 `#N review requested at <S>; approved <time>; merged head <H>; differences only from <base>`。
另外記錄 GraphQL timeline 中核准前最後一個 `PullRequestCommit`，只供觀察。
Commit 時間可能早於 push，因此 timeline 永遠不能授權合併；無法讀取時記錄 unavailable。

```powershell
Merge-GhAppApprovedPullRequest -Numbers 7,8 -Worktree . -LogPath .\merge.log -DeleteBranchPrefix 'feature/'
```

`Remove-GhAppMergedBranch` 刪除已合併 PR 的 head 分支。合併時沒有帶 `-DeleteBranchPrefix`，或分支要等 owner 同意才刪時使用。
刪除條件與合併後的刪除相同，另外多兩項檢查:

- PR 已合併;
- PR 的 head 等於 `-ExpectedHeadSha`，所以刪除前要先記下分支的 head。

`-BranchPrefix` 必填且不可為空。分支名稱必須以它開頭，只想刪一個分支時就傳完整分支名稱。
分支不能是 `main` 或 PR 的 base，ref 必須仍指向合併的 head，PR 的 base 必須已包含該 head。
函式和合併一樣把結果寫進 `-LogPath`，並傳回 `Number`、`Branch`、`Deleted`、`Reason`。

GitHub 沒有指定預期 SHA 的刪除 ref。函式先取得 App token，因為 token helper 可能花數秒並重試。接著再讀一次 ref，分支已經移動就保留（`Reason` 是 `ref changed before delete`）。
token 準備好之後，刪除前只剩這次最後的讀取與比對。刪除不重試。最後一次讀取與刪除之間的 push 仍會隨分支一起消失，程式無法封住這段空隙。合併後的刪除也一樣。

```powershell
Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha '<MERGED_HEAD_SHA>' -LogPath .\merge.log -BranchPrefix 'docs/release/record-0.9.5'
```

`Close-GhAppPullRequest` 先留言，再關閉 PR。留言失敗時不會關閉。
刪除分支需要 `-DeleteBranch` 與非空的 `-BranchPrefix`。
分支必須符合 prefix、保持原 head，且不能是 base 或 `main`。
Prefix 不符合時，在留言或關閉前就停止。這個選項供模組自己的測試分支使用。

```powershell
Close-GhAppPullRequest -Number 7 -CommentFile .\close-comment.txt `
    -DeleteBranch -BranchPrefix 'test/gh-app/'
```

`Invoke-GhAppRead` 使用 owner 已儲存的正常 GitHub CLI 登入，不呼叫 App helper。
Owner 必須先完成正常登入。子程序不會繼承 token 覆寫。
允許 PR view、list、checks，issue view、list，repo view，以及指定儲存庫的 REST GET。
拒絕寫入、GraphQL、extension、alias、瀏覽器啟動、其他 host 和其他儲存庫。
API input 與 field 選項可能選擇 POST，因此也會拒絕。PR 與 issue 識別值必須是數字。
模組不接受進階搜尋選項。

```powershell
Invoke-GhAppRead -Arguments @('pr', 'view', '7', '--json', 'state,headRefOid')
```

## Merge-GhAppTierBPullRequest

這個函式合併一張 B 級 PR。B 級 PR 不經 owner 核准，所以工具自己證明每個條件，任一條件不成立就拒絕。
它不需要送審登記，也不需要 owner 核准。它讀 PR、review、workflow run、檔案與 review 討論串。

下列條件全部成立才合併：

1. repo 在模組內的 B 級清單裡，且 base 分支符合清單項目。清單包含 freeform helper、replay 工具、combiner 的開發分支 `X.Y.x`，以及 Core 的 `main`。清單外的 repo 在發出任何呼叫之前就被拒。Core 哪些變更算 B 由機械判級（commander 工具的 `pr-tier.ps1`）決定。依 owner 2026-11-06 08:24 的決定（「不涉及 UI 或新功能就不需要我核准」），B 是 `src` 的 bug 修正與重構、tests、文件、其他 tools，以及只降不增的債務檔。A 是 UI、新功能、核准機制本身、放寬 guard／ratchet／baseline，以及版本與 release 檔。因為判級工具在這個 repository 之外，Core 在模組裡另有一道路徑底線：PR 只要改到或改名離開下列群組的路徑，不論判級結果，一律以 `Paths: <檔案> is a protected path of <repo>; only the owner approves a change there` 拒絕（`$script:CoreProtectedPathGroups` 命名了這些群組）：
   - UI 檔案：`.axaml`、`.xaml`、`.axaml.cs`；`src` 與 `examples` 底下任何 `Views`、`Theme`、`Icons` 資料夾、圖片檔（`.png`、`.jpg`、`.gif`、`.svg`、`.ico`、`.icns`、`.webp`、`.bmp`）與字型檔（`.ttf`、`.otf`、`.woff`）；以及 `src/Nvt.Core.Fonts/`；
   - 公開 API 清單 `PublicAPI*.txt`（新功能會往裡面加）；
   - 核准機制與 guard 程式：`tools/gh-app/`、`tools/repo-checks/`、`actions/`（共用 CI action）、`.github`、`.agents`、`.claude`、`.codex`、`scripts/`（打包腳本）；
   - `eng`，但兩個明確列名的債務帳本 `eng/code-health/baseline.json` 與 `eng/code-health/test-debt.json` 除外。`seam-owners.json`、policy 檔、schema 與其他檔案都是 A；
   - 專案與建置檔：`.sln`、`.slnx`、`.csproj`、`.props`、`.targets`、`.ruleset`、`.globalconfig`、`.rsp`、`.slnf`；
   - repository 檔案：`Directory.*`、`global.json`、`nuget.config`、`packages.lock.json`、`.editorconfig`、`.gitattributes`、`.gitignore`、`.gitmodules`、`CODEOWNERS`、`VERSION`、`AGENTS.md`、`CLAUDE.md`、`CONTRIBUTING.md`、`SECURITY.md`、`LICENSE*`；
   - 測試基礎設施：`tests/approval-check/`（必要的自我測試）與 `tests/Nvt.Core.TestSupport/`（會被打包的函式庫）；
   - `tests/` 底下路徑含 `guard`、`baseline`、`ratchet`、`contract`、`publicapi` 或 `publicsurface` 的所有檔案（包含 Theme 契約測試與它的 baseline XML，以及釘住公開 API 的測試）；
   - `docs/` 以外的任何 `Architecture/` 資料夾；
   - `docs/governance/`，以及任何深度的 `adr` 資料夾。
   兩個債務帳本只能減少行數：帳本增加一行，或被新增、刪除、改名，以 `is a debt ledger and gains lines or is added, removed or renamed` 拒絕。
   模組也證明檔案清單是完整的。它讀 PR 的檔案數，數量超過 3,000 或與列出的檔案數不同就拒絕（`Files: ...`），因為 GitHub 最多只列出 3,000 個檔案。
   一般 `src` 程式、其他 tools、tests、文件與 `CHANGELOG.md` 不在底線內。判級工具靠標題（`release:` 或 `type(release):`）判 release。其他 repository 沒有這道底線。
2. PR 是開啟的、已 Ready（不是 draft），而且作者是 App。
3. head 等於 `-ExpectedHeadSha`。
4. 該 head 上最新的結論是一則第一個非空行為 `Review record: <head> accept`、狀態為 `COMMENTED` 或 `APPROVED` 的 review（預設格式，可用 `-ReviewRecordPattern` 取代）。較晚的拒絕或撤銷會取消它。
5. 該 head 上每個 workflow 的最新一次 run 都成功完成。較舊的失敗 run 不算。
6. 沒有未解決的 review 討論串。讀不到討論串數量時也拒絕。
7. 合併狀態是 `CLEAN`。這比 `Get-GhAppPullRequestReadiness` 嚴，後者也接受 `BLOCKED`。

拒絕時，訊息一次列出所有不成立的條件。合併前工具會再讀一次 PR，head、base 或狀態有變就拒絕。
合併把 head 當作 `sha` 邊界，head 變了 GitHub 會拒絕合併。除非 `-DeleteBranchPrefix` 符合，工具不刪分支。

每次合併在 `tier-b-merges.jsonl` 加一行。這個檔和送審 ledger 在同一個資料夾，記錄 repo、PR 編號、head、base、merge commit、UTC 時間、App 帳號、操作者的帳號名稱、依據，以及通過的 workflow 名稱。
合併後若寫不進這一行，錯誤訊息會寫出 merge commit。`-LogPath` 收到的 log 行與 `Merge-GhAppApprovedPullRequest` 相同。

```powershell
Merge-GhAppTierBPullRequest -Number 7 -ExpectedHeadSha $head -LogPath .\merge.log
```

## App 沒有的權限

安裝沒有 `actions: write` 權限。
要求重跑 workflow 或 job 會得到 `HTTP 403`。不要嘗試。
某個 head 上失敗的 run 無法從這裡修復。由 owner 重跑，或讓 PR 有新的 head。
`Get-GhAppPullRequestReadiness` 以每個 workflow 最新的一次 run 為準，所以已被較新 run 取代的舊失敗不會擋住 PR。

## Token 規則

- 模組讀取設定、review ledger 與呼叫端指定的內容檔案，不讀取 private key 或 DPAPI 內容。
- 只有 owner 安裝的 helper 取得 token。每次寫入取得的新 token 只留在記憶體。
- 只有一個 `gh` 子程序透過 `GH_TOKEN` 取得 token。父程序環境不變。
- 設定檔、review ledger、helper、DPAPI 檔和 `gh` 都必須在所有 repository 之外。helper 失敗時，錯誤訊息只多加 helper 的結束碼。
- 請求內容透過 stdin 傳遞，不建立暫存 payload 檔案。
- 意外回顯的 token 會替換為 `[redacted]`。捕捉的錯誤內容不會傳給呼叫端或紀錄。
- 程序或 API 錯誤只包含 `HTTP <status> <API path>`。無法取得 status 時使用 `unknown`。
- 讀取在 status 為 unknown、429 或 5xx 時重試，最多 3 次，間隔 2 秒與 4 秒。第三次失敗後，訊息結尾加上 `(after 3 attempts)`。
- 寫入一律不重試，因為 GitHub 可能已經套用。例如留言、更新 ref、更新分支與合併。
- 寫入仍會重試 App token 的申請，最多 3 次。這一步在 gh 啟動前失敗，不會有任何請求送到 GitHub。
- 所有寫入都只針對 GitHub.com 上的設定儲存庫。

## 來源與刻意的差異

模組取代各 repository 各自保存的同類腳本:

| 來源 | 取代者 |
| --- | --- |
| Core 維護者的 push 分支、開 PR、合併已核准 PR 腳本(不在本 repository) | `Push-GhAppBranch`、`New-GhAppPullRequest`、`Merge-GhAppApprovedPullRequest` |
| NFC `nvt_fw_combiner` 的 `36754c99fc81949f09015df8bf9ce0bd622d6af0`,`docs/handoff/1.1.13/g0-scripts/Invoke-NfcGh.ps1` | 內部的 `gh` 執行函式與 `Invoke-GhAppRead` |
| NFC `tools/post_review.py` | `Add-GhAppReviewRecord` |

以下差異是刻意的:

- `Invoke-GhAppRead` 回傳 UTF-8 文字，並去掉結尾換行。NFC 的版本回傳原始位元組。支援的讀取指令只輸出 JSON 或文字。
- 合併依據最新的外部送審 request，以及嚴格晚於該 request 的 owner 核准。Review `commit_id` 與 GraphQL timeline 都不能授權合併。之後的 COMMENTED 不會取消核准。
- Core 的合併腳本只要 head 上有任何一筆核准就合併。模組在最新狀態是 CHANGES_REQUESTED、DISMISSED 或 PENDING 時停止。
- 10-05 規則只允許送審 head 之後合併經證明乾淨的 base。模組證明 first-parent 歷史，並使用 Git 2.38 以上的 `merge-tree` 重算每個 merge。
- 模組最多更新落後分支三次，重新核對 checks 與核准，只有指定 prefix 時才刪除合併的分支。Core 舊腳本一律刪除合併後分支。
- Session 用 `Request-GhAppOwnerReview` 記錄送審 head，再依賴 owner 核准。
- 審查紀錄一律是 COMMENT review,並鎖定預期的 head。

導入的 repository 退役舊腳本之前，要用同一組合成 PR 比較新舊兩邊的 API 呼叫、request body、log 與停止訊息。

## 後續導入

這次新增模組不移除任何既有腳本。儲存庫可在後續變更中導入。

1. Owner 安裝 helper 並建立外部設定。
2. 儲存庫入口腳本載入模組與設定。
3. 使用對應函式替換 push、PR、留言與 review record。用 `Request-GhAppOwnerReview` 記錄送 owner 的 head，合併改用 `Merge-GhAppApprovedPullRequest` 並傳入 `-Worktree`。
4. 使用 `Invoke-GhAppRead` 執行支援的 owner 讀取。
5. 執行離線測試並完成導入審查，再停用舊副本。

儲存庫保留自己的政策、內容產生流程及執行檔案位置。公開原始碼不得包含 helper 路徑、App 識別值或憑證。

## 離線測試

從儲存庫根目錄執行以下命令：

```powershell
pwsh -NoProfile -NonInteractive -File .\tools\gh-app\tests\Invoke-GhAppTests.ps1
```

Runner 清除 GitHub、Git 憑證與 vault 環境覆寫，然後啟動獨立程序。
測試需要已安裝的 Pester 3.4.0 和 Windows .NET Framework C# compiler。
測試編譯 fake `gh.exe`，並放在 PATH 最前面。Fake 只記錄參數、內容及已遮蔽的環境值。
比對旗標可證明子程序收到 fake token，但不會把 token 寫入檔案。
Fake helper 不讀取憑證，也不呼叫 GitHub。本機 Git objects 提供二進位、Unicode、模式、刪除和 merge parent 測試。
Review fixtures 涵蓋外部 ledger、核准時間、多次 branch update、乾淨合併重算、修改 merge tree、完全採用任一 parent 的解衝突與 20 個 commit 上限。
Runner 在完成後移除暫存目錄。

- 結束碼 `0`：已執行的測試全部通過。
- 結束碼 `1`：測試或測試區塊失敗。
- 結束碼 `2`：無法執行有效的支援測試套件。

API 行為參考 [GitHub PR API](https://docs.github.com/en/rest/pulls/pulls)。必要 checks 使用 [GitHub CLI checks](https://cli.github.com/manual/gh_pr_checks)。
乾淨合併重算依據 [Git merge-tree](https://git-scm.com/docs/git-merge-tree)。Timeline 觀察使用 [GitHub GraphQL PR schema](https://docs.github.com/en/graphql/reference/pulls)。
