# 共用核准檢查器 v0

此 composite action 對 pull request 執行 NFU 已合併版的核准判定，並把結果寫成 head commit 的 status。檢查器會確認 base 與 head 未移動、head 已含最新 base、review record 有效；依 base branch 與變更路徑判定是否還需要 owner 核准。檢查結果也會附加到 job summary。

## Inputs

| Input | 預設 | 說明 |
| --- | --- | --- |
| `policy-path` | `.github/approval-policy.json` | 相對於 base branch checkout 的 policy JSON 路徑。 |
| `status-context` | `governance/approval-rule` | 寫入 head commit 的 status context。 |
| `token` | `${{ github.token }}` | 讀取 PR 與寫入 commit status 的 token。 |

呼叫端 workflow 應使用 NFU 原有的 `pull_request` 事件（`opened`、`synchronize`、`reopened`、`ready_for_review`、`edited`、`converted_to_draft`）及 `pull_request_review` 事件（`submitted`、`edited`、`dismissed`）。權限須包含 `contents: read`、`pull-requests: read`、`statuses: write`。完整範例見 [NFU caller](../../examples/nfu-approval.yml)。

## 信任模型

檢查器來自呼叫端以完整 SHA 釘住的共用 action；policy 來自 PR 的 base branch。GitHub 從 PR 的 merge ref 讀取 workflow 定義，因此可修改 workflow 的人可能改變呼叫方式；agent 的 GitHub App 不得有 `workflows` 權限。呼叫端須釘住共用 repo 受保護 tag 所指的 SHA。

Ruleset 應 require commit status `governance/approval-rule`，來源選 **GitHub Actions**；不要 require 同名 check run。此 action 每次會先送 pending，最後送 success 或 failure commit status。

與 NFU 原版相比，`approval_check.py` 唯一差異是新增 `--policy <path>`。未指定時仍讀 `ROOT / ".github/approval-policy.json"`；其他判定邏輯、輸出訊息及 User-Agent 不變。
