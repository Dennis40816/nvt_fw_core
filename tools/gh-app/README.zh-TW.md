# 共用 GitHub App 寫入模組

這個 PowerShell 模組透過儲存庫的 GitHub App 執行寫入。既有腳本保持原狀。
執行環境需要 PowerShell 7.4 以上、Git、GitHub CLI，以及 owner 安裝的 token helper。
離線測試需要 Windows 與 Pester 3.4.0。

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
本機 base tree 必須等於遠端 parent tree。所有 SHA 必須是 40 個小寫十六進位字元。
更新 ref 前，產生的 tree 必須等於本機 HEAD tree。既有 ref 只允許非強制前進。
只有 HTTP 404 允許建立缺少的 ref。`-ExtraParent` 加入第二個 parent。
已開啟 PR 需要 `-AllowOpenPr`。任何 owner review 都會禁止 push，即使有這個選項。

```powershell
Push-GhAppBranch -Worktree . -LocalBase '<LOCAL_BASE_SHA>' `
    -RemoteParent '<REMOTE_PARENT_SHA>' -Branch 'feature/example' `
    -MessageFile .\message.txt -ExtraParent '<SECOND_PARENT_SHA>'
```

`New-GhAppPullRequest` 從檔案載入描述並建立 PR，然後讀回作者。
作者不等於 `botLogin` 時，它會傳回含完整 PR URL 的錯誤。PR 保持開啟。
`-Base` 預設為 `main`。`-Draft` 建立草稿。Head 與 base 都必須是設定儲存庫中的分支。

```powershell
New-GhAppPullRequest -Head 'feature/example' -Title 'Add shared behavior' `
    -BodyFile .\pr-body.txt -Base main -Draft
```

`Set-GhAppPullRequestBody` 使用檔案內容取代 PR 描述。

```powershell
Set-GhAppPullRequestBody -Number 7 -BodyFile .\pr-body.txt
```

`Add-GhAppComment` 使用檔案內容新增 PR 或 issue 留言。

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

`Merge-GhAppApprovedPullRequest` 依序處理 PR。已關閉或合併的 PR 會略過。
遇到第一個不安全操作或失敗請求時，整批處理停止。
owner 最新一筆會改變狀態的 review 必須對目前 head 給予 `APPROVED`。之後的 COMMENTED review 不會取消核准。
模組依提交時間排序 review，同時間以 review ID 排序。Owner 的 pending review 會禁止合併。
Owner 後續留言、要求修改或撤銷 review 都會取代先前批准。
落後分支透過 App 更新，並核對 `expected_head_sha`。更新後會再次檢查批准是否保留。
批准未保留、需要人工解衝突，或三次更新後仍落後，都會停止。
模組等待必要 checks 通過及 merge state 為 clean，並在合併前再核對 head 和批准。
失敗或取消的 checks 會停止。預設逾時為 1,200 秒，輪詢間隔為 15 秒。
可使用 `-TimeoutSeconds` 和 `-PollSeconds` 修改這些限制。
合併確認後，只有 ref 未改變且 `main` 已包含該 head 時才刪除分支。
Base、已改變、不存在或未被 `main` 包含的分支會保留。
`-LogPath` 的目錄必須已存在。紀錄會附加 UTC 時間、head SHA、merge SHA、更新及分支處理結果。

```powershell
Merge-GhAppApprovedPullRequest -Numbers 7,8 -LogPath .\merge.log
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

## Token 規則

- 模組只讀取設定與呼叫端指定的內容檔案，不讀取 private key 或 DPAPI 內容。
- 只有 owner 安裝的 helper 取得 token。每次寫入取得的新 token 只留在記憶體。
- 只有一個 `gh` 子程序透過 `GH_TOKEN` 取得 token。父程序環境不變。
- 請求內容透過 stdin 傳遞，不建立暫存 payload 檔案。
- 意外回顯的 token 會替換為 `[redacted]`。捕捉的錯誤內容不會傳給呼叫端或紀錄。
- 程序或 API 錯誤只包含 `HTTP <status> <API path>`。無法取得 status 時使用 `unknown`。
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
- 合併時，owner 最新一筆會改變狀態的 review 必須是對目前 head 的 APPROVED。之後的 COMMENTED review 不會取消核准，和 GitHub 的規則相同。
- Core 的合併腳本只要 head 上有任何一筆核准就合併。模組在核准之後出現 CHANGES_REQUESTED 或 DISMISSED 時會停下。
- 審查紀錄一律是 COMMENT review,並鎖定預期的 head。

導入的 repository 退役舊腳本之前，要用同一組合成 PR 比較新舊兩邊的 API 呼叫、request body、log 與停止訊息。

## 後續導入

這次新增模組不移除任何既有腳本。儲存庫可在後續變更中導入。

1. Owner 安裝 helper 並建立外部設定。
2. 儲存庫入口腳本載入模組與設定。
3. 使用對應函式替換 push、PR、留言、review record 和已批准的 merge。
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
Runner 在完成後移除暫存目錄。

- 結束碼 `0`：已執行的測試全部通過。
- 結束碼 `1`：測試或測試區塊失敗。
- 結束碼 `2`：無法執行有效的支援測試套件。

API 行為參考 [GitHub PR API](https://docs.github.com/en/rest/pulls/pulls)。必要 checks 使用 [GitHub CLI checks](https://cli.github.com/manual/gh_pr_checks)。
