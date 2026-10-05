[English](IO.md) | [中文](IO.zh-TW.md)

# IO：AtomicOutput

[`AtomicOutput.WriteAsync`](../../../src/Nvt.Core/IO/AtomicOutput.cs) 是 NFU 用於一般串流輸出的輔助方法，抽取至 `Nvt.Core.IO`，目標為 `net8.0`，只依賴 BCL。它在目的地目錄寫入暫存檔、完成 flush，再將暫存檔移至目的地並覆寫原檔。**不得用它取代 NFC 強化過的韌體輸出寫入器。** 工具採用屬於另一個任務。

凍結的來源基準：NFU（`nvt-event-buffer-replay`）、`origin/0.2.0`、commit `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b`。抽取來源：

- `src/Nvt.Replay.Core/AtomicOutput.cs`
- `tests/Nvt.Replay.Tests/ReplaySidecarTests.cs`：`Interrupted_atomic_write_preserves_prior_output_and_removes_temporary_file`

除了 namespace，執行程式碼維持不變；Core 加入版權標頭與 API 文件。既有直接測試移植至 [`AtomicOutputTests`](../../../tests/Nvt.Core.Tests/IO/AtomicOutputTests.cs)，並以合成資料加入二進位與 UTF-8 位元組完全一致、替換時機、取消、寫入／flush／發布失敗、清理、路徑正規化與參數驗證等行為特徵測試。Replay schema、報表寫入器及其整合測試仍留在 NFU。

保留目前行為：即使 token 已取消，delegate 仍會先被呼叫，之後輔助方法才檢查取消；成功時會覆寫目的地。失敗前建立的父目錄會保留。清理會嘗試刪除暫存檔，刪除失敗的例外可能蓋過原始例外。本方法沒有額外的崩潰復原或韌體輸出保證。

既有套件還原完成後，Core 驗證指令：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
```

NFU 後續改用 Core 時，須在切換前後先以 `--no-restore` 建置，停用 telemetry，並執行以下測試：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-restore
dotnet test tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-build --filter "FullyQualifiedName~ReplaySidecarTests|FullyQualifiedName~CaptureAnalysisTests|FullyQualifiedName~ReadableCommunicationLogWriterTests|FullyQualifiedName~ReplayExportTests"
```

使用同一組凍結的合成文件、報表與繪圖輸入，包含固定的序列化中繼資料。保留基準輸出，改用 Core 後在相同目的地重新產生，比較最終檔案集合及每個 JSON／CSV／JSONL／PNG 的位元組或 SHA-256。保留 NFU 的確定性 heatmap golden hash、取消斷言、原有輸出保護與暫存檔清理檢查；不得更新基準來接受差異。NFU 採用與封裝驗收仍待後續工作。
