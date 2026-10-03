# 還沒決定的事

這些都要 owner 決定。owner 說要等各專案各自有雛型後再一起探討，所以現在只列問題與選項，不下結論。

## 1. 共用核心要不要拆成多個 lib

owner 2026-10-02 說這點他還沒想清楚。

- 單一核心套件：版本管理簡單，但三個工具要一起升級。
- 依功能拆成數個 lib（launcher、shell、設定、診斷各一個）：各工具可以只採用需要的部分，符合「逐元件、誰成熟誰納入」；代價是版本相依要管。
- 先單一、之後再拆。

## 2. 共用的程式碼放在哪裡

- 這個 repo（`nvt_fw_core`）。
- NFC 的 2.0 主幹。
- 各元件留在原本的 repo，以套件或 submodule 形式共用。

先建了這個 repo 放規劃，實際上已經傾向第一個選項，但還沒有正式決定。

## 3. 工具怎麼掛進共用外殼

- 程序內外掛：整合緊密，但一個工具出錯會影響整個外殼。
- 獨立程序加契約（manifest、啟動協定）：隔離好，NFC 裡決定輸出 bytes 的程式可以維持獨立；代價是整合的體驗要另外設計。

## 4. 共用核心的範圍

暫定假設見 [vision.md](vision.md)：只含外殼與基礎設施，不含各工具的業務邏輯。尚未由 owner 確認。

## 5. Avalonia 主版本對齊

NFC 12.0.5、NFU 12.1.1、NFH 11.3.12。NFH 何時升到 12，以及 AvaloniaEdit 有沒有對應版本，還沒有確定的時程。

## 6. 命名

2.0.0 允許破壞性重整。目前命名空間是 `NvtFwCombiner.*`、`FreeformHelper.*`、`Nvt.Replay.*`；共用核心用什麼命名空間、套件怎麼命名，還沒決定。owner 已決定 NFU 的命名空間「這輪不改」，那只管 1.x。

## 7. 成熟度的評估標準

[components.md](components.md) 列了五項提案，還沒有 owner 的決定。

## 8. 治理

三個工具的 repo 已經用同一套「公版」規則（ADR、核准檢查、ruleset）。這個 repo 目前只有文件，還沒套用；開始放程式碼之前要決定怎麼套。

2026-10-03 盤點後發現「公版」只剩骨架相同，核准檢查已分成 NFC、NFU 兩套實作。共用方式的方案與要 owner 決定的 10 題見 [shared-ci/proposal.md](shared-ci/proposal.md)。

## 9. 專門的 NVT Core session 何時開

owner 已決定由專門的 session 開發，但要等雛型。交接檔在 commander 的本機資料夾（`nvt-core-adr-brief.md`）。

## 10. Workstation 的組成與版本

owner 2026-10-03 已決定各工具永遠可以獨立使用與發佈，Workstation 是額外的整合發佈（見 [decisions.md](decisions.md)）。還沒決定、等雛型階段再問：

- Workstation 能不能只挑其中幾個工具組裝。
- Workstation 的版本與各工具版本怎麼對應。
