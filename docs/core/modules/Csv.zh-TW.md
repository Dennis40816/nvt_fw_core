[English](Csv.md) | [中文](Csv.zh-TW.md)

# Csv：CsvQuoting

[`CsvQuoting.Quote`](../../../src/Nvt.Core/Csv/CsvQuoting.cs) 處理單一 CSV 欄位的引號。它位於 `Nvt.Core.Csv`，目標為 `net10.0`，只依賴 BCL。工具採用屬於另一個任務。

凍結的來源基準：NFU（`Dennis40816/nvt-event-buffer-replay`）、`origin/0.2.0`、commit `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b`。抽取自兩份完全相同的私有 helper：

- `src/Nvt.Replay.Rendering/AnalysisOutputWriter.cs:164-165`
- `src/Nvt.Replay.Rendering/ReadableCommunicationLogWriter.cs:123-124`

## 行為

- 欄位含有逗號、雙引號、CR 或 LF 時，才加上引號。
- 加引號時，外層包雙引號，內部每個雙引號重複一次。
- 其他內容一律不變，包括空字串、Unicode、定位字元、分號及前後空白。
- 欄位內的換行字元完全保留。
- 傳入 null 時拋出 `ArgumentNullException`，參數名稱為 `value`。原本的私有 helper 會拋出 `NullReferenceException`。

NFU 的呼叫端不會傳入 null。分析欄位不可為 null，或以空字串替代。通訊資料列在處理引號前，先把可為 null 的欄位換成空字串。欄位宣告位於 `src/Nvt.Replay.Analysis/CaptureAnalysis.cs` 與 `src/Nvt.Replay.Core/CaptureModels.cs`。

## 驗證與採用

[`CsvQuotingTests`](../../../tests/Nvt.Core.Tests/Csv/CsvQuotingTests.cs) 以固定語料比對凍結規則的明列預期字串，並驗證 null 的例外。

既有套件還原完成後執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
```

NFU 採用時，以這個方法取代兩份私有 helper。這是另一個任務，改用有版本號的 Core 套件。採用的證據是：分析 CSV 與通訊記錄 CSV 在改動前後的位元組完全相同。
