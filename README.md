# nvt_fw_core

NVT 韌體工具族的共用核心。**目前是規劃階段：這個 repo 只有規劃文件，沒有程式碼。**

## 這個 repo 是什麼

NVT 有三個各自獨立的韌體工具：

| 簡稱 | 工具 | Repo | 目前版本線 |
|---|---|---|---|
| NFC | NVT FW Combiner | `Dennis40816/nvt_fw_combiner` | 1.2.x |
| NFH | Freeform Helper | `Dennis40816/FreeformHelper` | 1.3.x |
| NFU | NVT FW UTIL（原 Event Buffer Replay） | `Dennis40816/nvt-event-buffer-replay` | 0.x |

三個工具都有啟動、外殼、設定、診斷這類彼此相似的部分。這個 repo 用來規劃它們的共用核心，最終目標是整合成一個 NVT FW Workstation。

owner（Dennis）的暫定目標，原話：

> 三個版本的 2.0.0 時都開始共用核心架構並在下一個重大版本更新推出整合型的 work station

## 現況（2026-10-03）

- 只有規劃，還沒開始開發。
- 共用核心的程式碼最後放在哪裡（這個 repo、NFC 的 2.0 主幹、或共用套件）還沒決定，見 [docs/open-questions.md](docs/open-questions.md)。
- 細節要等各專案各自有雛型後再一起探討。

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

## 誰維護

目前由 commander session 依 owner 的指示維護。專門的「NVT Core」session 開起來之後由它接手。規劃內容的變更要有 owner 的決定為依據，並記進 [docs/decisions.md](docs/decisions.md)。
