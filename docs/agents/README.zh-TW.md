# Agent 協作流程

owner 核准決策與 pull request。commander 協調 project session。project session 透過 Codex worker 完成範圍明確的任務。

每個 pull request 都先由獨立的 Codex 初審，再由 Claude 複核，最後交由 owner 核准。

這些文件說明 Core 與採用專案的通用協作流程。本機設定與即時工作紀錄留在此公開儲存庫之外。

[English](README.md)

## 依目前步驟閱讀文件

| 使用時機 | 文件 |
|---|---|
| 分配角色、判斷功能歸屬，或請 owner 決策 | [協作流程](workflow.md) |
| 保存進度、交接工作，或在 context compact 後接續 | [交接與狀態](handoff.md) |
| 準備任務 brief、派工，或檢查 worker 結果 | [派工](dispatch.md) |
| 審查 pull request、請求核准，或準備合併 | [審查與核准](review.md) |
| 安裝前評估新 skill 或 plugin | [Skill 評估](skills.md) |

## 套用儲存庫規則

請搭配 [CONTRIBUTING.md](../../CONTRIBUTING.md) 閱讀。採用專案在同一個採用 pull request 中複製或連結規則。

儲存庫文字使用英文。英文與繁體中文 README 一起更新。其他文件僅在 owner 要求時新增翻譯。

公開內容只保留通用角色與去識別化證據。公開檔案、歷史與產物不得包含下列資料：

- 本機絕對路徑、使用者名稱、機器名稱與帳號名稱。
- 應用程式識別碼、秘密與憑證。
- 私有儲存庫名稱、私有資料與私人專案細節。
- 個人用量限制、即時佇列狀態與本機執行設定。
- 可辨識實際部署的 session 名稱與識別碼。

公開紀錄需要標示參與者時，使用角色名稱。
