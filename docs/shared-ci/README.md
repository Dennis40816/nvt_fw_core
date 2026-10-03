# 共用的 CI/CD 與治理框架

NVT Core 的第一步（owner 2026-10-03）。目標：一條治理或 CI 規則只改一次，三個 repo 一起生效，不再人工逐一修改。

- [`inventory.md`](inventory.md)：三個 repo 目前的 workflow、核准規則、ruleset、治理文件與驗證腳本的盤點，每項標明三邊相同、已分歧或某個 repo 特有
- [`approval-carryover.md`](approval-carryover.md)：第一條要共用的規則草案「改動不大時，已有的核准保留」，含「改動不大」的幾種定義、判定方式、風險與套到各 repo 要改的地方
- [`proposal.md`](proposal.md)：共用方式的方案比較與建議、導入順序，以及要 owner 決定的事

由 session「NVT CORE」撰寫（2026-10-03）。這一步只做盤點與方案，沒有改任何 repo 的檔案或設定。
