[English](MessageCenter.md)

# MessageCenter

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MessageCenterViewModel.cs`：活動篩選、列的嚴重性旗標與無障礙文字、被動計數、初始模態狀態、匯出情境判斷，以及 `Open`、`Close`、`SelectSystemInformation` 的世代遞增與狀態提交順序。重新整理、匯出、記錄、報告組合與產品格式化留在 NFC。
- `src/NvtFwCombiner.Application/Diagnostics/SystemInformationModels.cs`：重要性與嚴重性列舉，以及不可變 `SystemActivityEntry` 的序號、揭露層級與嚴重性中繼資料。分類、代碼、草稿、診斷、快照與套件資料留在 NFC。
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.MessageCenter.Startup.cs`：`ActivityHistoryUsesTwoDisclosureLevels` 的揭露與主程式文字重新投影斷言。
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellScreenInventoryTests.cs`：`SystemActivityContentFitsAndFilters` 的列選擇與順序，以及 `SystemActivitySelectionsPreserveNavigationAndHistory` 的被動歷程保留。版面、繪製、導覽與焦點斷言留在 NFC。
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.MessageCenter.cs`：`DiagnosticsPickerRejectsClosedAndReopenedContext`、`DiagnosticsExportCompletionRejectsReopenedContext` 的工作階段情境斷言，以及 `MessageCenterKeepsSystemLifecycleSeparateFromRunReports` 的頁面選擇。選擇器、匯出器、診斷與報告斷言留在 NFC。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.MessageCenter` 在 `Nvt.Core` 提供被動顯示契約與模態工作階段，目標為 `net8.0`，只依賴 BCL。主程式提供已准入的活動項目、顯示字串、計數與回呼。

## 公開 API

| 型別 | 契約 |
| --- | --- |
| `MessageActivityImportance` | `Important`、`Debug`。 |
| `MessageActivitySeverity` | `Information`、`Success`、`Warning`、`Error`。 |
| `MessageActivityFilter` | `Important`、`Warnings`、`Errors`。 |
| `MessageCenterActivityItem` | 非 sealed 的列 record：`Time`、`Title`、`Detail`、`Category`、`Status`、`Severity`；四個嚴重性旗標與 `AccessibleText`。 |
| `MessageCenterActivity` | Sealed 中繼資料 record：`long Sequence`、重要性、嚴重性與 `Func<MessageCenterActivityItem> ProjectItem`。 |
| `IMessageCenterProvider` | 被動 `ActiveDiagnosticCount`、未篩選的 `ActivityCount`，以及 `IReadOnlyList<MessageCenterActivity> CaptureActivity()`。 |
| `MessageCenterActivityFilter` | 靜態 `Apply(IEnumerable<MessageCenterActivity> activities, MessageActivityFilter filter, bool includeDebug)`，傳回已具體化的列。 |
| `MessageCenterSession` | 唯讀 `IsOpen`、`IsActivitySelected`、`ExportContextGeneration`；`Open(Action? beforeOpen = null)`、`Close(Action? beforeClose = null)`、`SelectActivity(bool selected, Action? beforeSelect = null)` 與 `IsExportContextCurrent(long generation)`。 |

實作位於 [MessageCenterActivity.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterActivity.cs)、[IMessageCenterProvider.cs](../../../src/Nvt.Core/MessageCenter/IMessageCenterProvider.cs)、[MessageCenterActivityFilter.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterActivityFilter.cs) 與 [MessageCenterSession.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterSession.cs)。

## 保留的顯示行為

各嚴重性旗標直接比較列的嚴重性與對應列舉值。完整無障礙字串精確如下：

```csharp
$"{Time}. {Title}. {Category}. {Detail}. {Status}."
```

空欄位保留標點；Core 不正規化或在地化字串。列保持非 sealed，供 NFC 的具型別 XAML 別名使用；該別名不新增顯示行為。

提供者契約要求以不可變主程式項目建立有序、已准入的中繼資料快照。擷取與兩項計數都不需要投影顯示列。各委派可在執行時使用主程式目前的顯示文字，讓擷取項目不變而重新投影。中繼資料與投影列的嚴重性必須一致；這是主程式的不變條件，不新增 Core 驗證例外。

`Apply` 保留以下順序：

1. `includeDebug` 為 true 或重要性為 `Important` 時揭露項目。
2. 套用嚴重性篩選。`Important` 包含所有已揭露的嚴重性；`Warnings` 僅匹配 `Warning`；`Errors` 僅匹配 `Error`。
3. 依序號遞減排序保留的中繼資料；相同序號保留輸入順序。
4. 依該順序對每個保留列呼叫 `ProjectItem` 恰好一次，並具體化結果。

隱藏列不會投影。排序階段會在投影前完成來源列舉。列舉與投影錯誤原樣傳遞。未定義篩選值只有在已揭露項目實際接受判斷時，才拋出原本不帶參數的 `ArgumentOutOfRangeException`；空來源或完全未揭露的來源不會判斷該值。Null 來源保留 LINQ 的 `ArgumentNullException`，參數名稱為 `source`。未定義的重要性與嚴重性值仍套用相同判斷式，不新增驗證。

具體化結果不受來源清單後續變更影響。Core 不跨呼叫快取投影。此被動投影與 ReportList 的具體化、Reset 發布及分頁分開。

## 保留的工作階段行為

新工作階段為關閉狀態、活動頁已選取、世代為零。

| 操作 | 順序與提交狀態 |
| --- | --- |
| `Open` | 一律以 checked 遞增世代、執行 `beforeOpen`，然後提交 `IsOpen = true`；重複開啟亦同。 |
| `Close` | 一律以 checked 遞增世代、執行 `beforeClose`，然後提交 `IsOpen = false`；重複關閉亦同。 |
| `SelectActivity` | 選取目前頁面時直接返回。否則以 checked 遞增世代、執行 `beforeSelect`，然後提交選取狀態。可見性不變。 |
| `IsExportContextCurrent` | 依序要求世代相同、模態已開啟、活動頁已選取。 |

回呼可觀察已遞增的世代與原本已提交的可見性和選取狀態。選取操作透過提交前掛鉤保留 Toolkit 的 property-changing 時機。回呼拋出的例外會傳遞，該操作的狀態不提交，但世代遞增保留。開啟與關閉保留頁面選取。

固定機制上限為 `long.MaxValue`：再次遞增會在任何回呼或狀態提交前拋出 `OverflowException`，世代維持不變。在此上限選取目前頁面仍無動作。溢位測試使用 internal 建構式；沒有公開世代 setter。工作階段同步執行回呼，不新增 dispatcher 或同步機制。

## 所有權與使用端契約

Core 只擁有列契約、被動篩選與投影順序，以及模態世代機制。NFC 保留訊息來源、歷程、診斷轉換註冊、路徑 token 驗證、用語、在地化與時間格式、報告歷程、重新整理政策、儲存選擇器、匯出位元組／schema／路徑及組合範本。NFC 凍結的 **128 筆活動上限** 留在 NFC。本切片沒有 Core 歷程、路徑或大小上限，也沒有正值上限參數。

NFC 透過 `vendor/nuget/` 的已驗證版本套件、精確 `[x]` 套件版本、locked restore 與限定至該資料夾的來源對應使用 Core。`SOURCE.md` 綁定已審查的 Core 原始碼與套件 SHA-256 雜湊。使用端採用屬於獨立變更：改接已擷取的篩選、列與工作階段實作，再於相同記錄環境下通過完整值／事件軌跡一致性與解碼 UI 像素完全相同的證據後，刪除本地可執行副本。保留狹窄具型別別名與所有產品所有者。Core 測試本身不能證明 NFC 視覺或產品一致性。

## 來源至 Core 測試對照

測試使用 xunit.v3 與記憶體內合成資料，位於 [DisplayContractTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/DisplayContractTests.cs) 與 [SessionTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/SessionTests.cs)。

| 凍結來源證據 | Core 測試與保留斷言 |
| --- | --- |
| `ActivityHistoryUsesTwoDisclosureLevels` | `ActivityHistoryUsesTwoDisclosureLevelsAndReprojectsHostText`：預設隱藏 debug、保留 important success、展開後顯示 debug，並以第二組明顯不同的文字重新投影所有欄位。`BadgeAndSummaryCountsAndCaptureNeedNoRowProjection` 保留被動計數。 |
| `SystemActivityContentFitsAndFilters` | `InventoryFiltersPreserveRowsAndHostHistory`：相同警告／錯誤選擇、debug／important 遞減順序、警告／錯誤列旗標、切換頁面後主程式項目不變，以及原本 110 字元的合成詳細文字範例。`FiltersProjectOnlyRetainedRowsInExactOrder` 涵蓋六種篩選／揭露組合與回呼軌跡。 |
| `SystemActivitySelectionsPreserveNavigationAndHistory` | 空篩選、揭露、擷取後變更來源及頁面選取測試保留被動部分。產品導覽、IC 選取、報告歷程與繪製仍為 NFC 斷言。 |
| `DiagnosticsPickerRejectsClosedAndReopenedContext` 與 `DiagnosticsExportCompletionRejectsReopenedContext` | `ClosedAndReopenedContextRejectsOldGeneration` 與 `DelayedObservationRejectsReopenedContext` 保留過期情境拒絕。延遲測試使用確定性閘門與來源十秒等待門檻。實際選擇器接受與 I/O 完成仍為 NFC 斷言。 |
| `MessageCenterKeepsSystemLifecycleSeparateFromRunReports` | `OpenAndClosePreserveTheSelectedPane`、`PaneChangesPreservePropertyChangingTiming` 與目前情境測試涵蓋模態／頁面行為。診斷轉換、Build 阻擋與報告生命週期仍為 NFC 斷言。 |

新增特性測試涵蓋完整無障礙字串與空欄位、無新增行為的綁定別名、打亂序號與 signed `long` 邊界的穩定同序號排序、隱藏列零投影、投影／列舉錯誤順序、未定義列舉值與延後檢查無效篩選、null 輸入、重複可見性操作、同頁面無動作、提交前狀態觀察及回呼失敗。世代邊界測試涵蓋 `long.MaxValue - 1`、`long.MaxValue`、嘗試遞增超過上限、在上限回呼失敗，以及兩個頁面在上限選取目前頁面時無動作。本模組沒有原生 process、handle 或 Job 行為。

## 驗證命令

主程式在 locked 相依套件準備完成後驗證已準備的原始碼，使用預設 `bin` 與 `obj` 輸出：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```
