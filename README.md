# nvt_fw_core

NVT 韌體工具族的共用核心。**共用核心本身還在規劃階段，還沒有程式碼。** 目前這個 repo 放規劃文件，以及第一步「共用的 CI/CD 與治理框架」的程式碼：共用核准檢查 [`actions/approval-check`](actions/approval-check/)。

## 這個 repo 是什麼

NVT 有三個各自獨立的韌體工具：

| 簡稱 | 工具 | Repo | 目前版本線 |
|---|---|---|---|
| NFC | NVT FW Combiner | `Dennis40816/nvt_fw_combiner` | 1.2.x |
| NFH | Freeform Helper | `Dennis40816/nvt-freeform-helper`（2026-10-04 起的主開發 repo） | 1.3.x |
| NFU | NVT FW UTIL（原 Event Buffer Replay） | `Dennis40816/nvt-event-buffer-replay` | 0.x |

三個工具都有啟動、外殼、設定、診斷這類彼此相似的部分。這個 repo 用來規劃它們的共用核心，最終目標是整合成一個 NVT FW Workstation。

owner（Dennis）的暫定目標，原話：

> 三個版本的 2.0.0 時都開始共用核心架構並在下一個重大版本更新推出整合型的 work station

## 現況（2026-10-05）

- 共用核心：只有規劃，還沒開始開發。程式碼最後放在哪裡（這個 repo、NFC 的 2.0 主幹、或共用套件）還沒決定，見 [docs/open-questions.md](docs/open-questions.md)。細節要等各專案各自有雛型後再一起探討。
- 共用的 CI/CD 與治理：共用核准檢查 `v0.1.0` 已發佈，三個工具 repo 都還沒採用。試點是 NFH，等 NFH 的 CI 修正與 1.3.2 完成後開始。進度見 [docs/shared-ci/README.md](docs/shared-ci/README.md)。

## 原則

1. **逐元件、誰成熟誰納入共用。** launcher 由 NFC 開發完成後直接套用到其他工具；其他元件之後逐一看成熟度，哪個專案做得最好就把它納入共用。
2. **1.x 照常投入。** 各工具在 1.x（NFU 是 0.x、1.0）期間不必為了共用而停下來。
3. **先有雛型再探討。** 在各專案各自有雛型之前，不寫共用程式碼，也不為別的專案預先設計。
4. **開發優先順序：** NFC ≥ NFH > NVT Core > NFU。
5. **領域中立。** 共用核心不放韌體、Event Buffer、Freeform 的業務邏輯；NFC 裡決定輸出 bytes 的程式（R3）不進共用核心。這一條是規劃時的假設，尚未由 owner 確認。

## 文件

- [docs/vision.md](docs/vision.md)：目標、版本節奏、階段
- [docs/components.md](docs/components.md)：元件對照表（三個專案各自的實作與成熟度）
- [docs/decisions.md](docs/decisions.md)：owner 的決定紀錄
- [docs/open-questions.md](docs/open-questions.md)：還沒決定的事
- [docs/shared-ci/README.md](docs/shared-ci/README.md)：共用的 CI/CD 與治理框架（盤點、方案、進度）

## 誰維護

2026-10-03 起由專門的 session「NVT CORE」依 owner 的指示維護。規劃內容的變更要有 owner 的決定為依據，並記進 [docs/decisions.md](docs/decisions.md)。每個 PR 都要 owner 核准才能合併。
