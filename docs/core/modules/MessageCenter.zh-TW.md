[English](MessageCenter.md)

# MessageCenter

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MessageCenterViewModel.cs`：活動篩選、列的嚴重性旗標與無障礙文字、被動計數、初始模態狀態、匯出情境判斷、`Open`、`Close`、`SelectSystemInformation` 的世代遞增與狀態提交順序，以及 `ExportAsync` 的世代檢查與例外／回呼範圍。另擷取展示命令接線、揭露與相依通知、主程式供應的標籤、語言重設、進度／狀態 setter 與互動順序。內層重新整理政策、套件資料擷取、語意活動記錄、報告組合與產品格式化留在 NFC。
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/PresentationObserver.cs`：私有的已提交活動／診斷通知隔離與原始追蹤訊息。產品重新整理隔離位置留在主程式委派。
- `src/NvtFwCombiner.Presentation.Avalonia/Views/MessageCenterModal.axaml.cs`：`ExportWithPickerAsync` 的接受、取消與失敗判斷。實際儲存選擇器、本機路徑轉換與其失敗訊息、預設檔名、版面、焦點與報告組合留在 NFC。
- `src/NvtFwCombiner.Application/Diagnostics/SystemInformationModels.cs`：重要性與嚴重性列舉，以及不可變 `SystemActivityEntry` 的序號、揭露層級與嚴重性中繼資料。分類、代碼、草稿、診斷、快照與套件資料留在 NFC。
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.MessageCenter.Startup.cs`：`ActivityHistoryUsesTwoDisclosureLevels` 的揭露與主程式文字重新投影斷言。
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellScreenInventoryTests.cs`：`SystemActivityContentFitsAndFilters` 的列選擇與順序，以及 `SystemActivitySelectionsPreserveNavigationAndHistory` 的被動歷程保留。版面、繪製、導覽與焦點斷言留在 NFC。
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.MessageCenter.cs`：`DiagnosticsPickerRejectsClosedAndReopenedContext`、`DiagnosticsPickerFailureIsVisibleAndRetryable`、`DiagnosticsExportCompletionRejectsReopenedContext` 的工作階段與合成工作流程斷言，以及 `MessageCenterKeepsSystemLifecycleSeparateFromRunReports` 的頁面選擇。實際選擇器、套件資料、診斷與報告斷言留在 NFC。
- `tests/NvtFwCombiner.UiSmoke.Tests/DiagnosticsExportFailureGuidanceTests.cs`：通用失敗／成功回呼觀察。通用狀態、語言／開啟重設及繼承屬性反射 setter 斷言擷取至展示測試。在地化指引、活動中繼資料、版面與像素斷言留在 NFC。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.MessageCenter` 在 `Nvt.Core` 提供被動顯示契約、模態工作階段與匯出工作流程，目標為 `net8.0`，只依賴 BCL。主程式提供已准入的活動項目、顯示字串、計數、檢視身分、匯出 I/O 與狀態回呼。

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
| `MessageCenterExportWorkflow` | 建構式 `(MessageCenterSession session, Func<string, CancellationToken, Task> export, Action succeeded, Action failed)`；`Task ExportAsync(string destinationPath, CancellationToken cancellationToken)`；`Task ExportWithPickerAsync(Func<Task<string?>> pickPathAsync, Func<bool> isViewContextCurrent)`。 |
| `MessageCenterRefreshCoordinator` | 建構式 `(Func<bool, CancellationToken, Task> refresh)`；`Task RefreshAsync(bool reloadSources, CancellationToken cancellationToken)`；`Task RefreshAfterCurrentAsync(bool reloadSources, CancellationToken cancellationToken)`。 |

實作位於 [MessageCenterActivity.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterActivity.cs)、[IMessageCenterProvider.cs](../../../src/Nvt.Core/MessageCenter/IMessageCenterProvider.cs)、[MessageCenterActivityFilter.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterActivityFilter.cs)、[MessageCenterSession.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterSession.cs)、[MessageCenterExportWorkflow.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterExportWorkflow.cs) 與 [MessageCenterRefreshCoordinator.cs](../../../src/Nvt.Core/MessageCenter/MessageCenterRefreshCoordinator.cs)。

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

## 保留的匯出工作流程行為

匯出委派是唯一的 I/O 操作。直接匯出在呼叫委派前擷取 `ExportContextGeneration`，並原樣傳入目的地與取消 token；不驗證或正規化路徑，也不檢查可見性或頁面。委派完成後，世代相同才執行成功回呼。只有 `IOException`、`UnauthorizedAccessException`、`ArgumentException`（含衍生例外）進入預期失敗的 catch；世代相同才執行失敗回呼。世代不符會抑制任一狀態發布，但不會中斷寫入。

成功回呼仍位於上述 catch 範圍內：其預期例外可觸發失敗回呼，並再次檢查世代。失敗回呼在 catch 本體內執行，其例外直接傳遞，不會再次捕捉。未預期的匯出／成功回呼例外與取消都會傳遞，即使世代已過期亦同。相同世代的並行匯出不互相取代；各自可依完成順序發布狀態。

選擇器保留以下順序：

1. 先驗證 `pickPathAsync`，再驗證必要的身分委派 `isViewContextCurrent`。工作階段關閉時不呼叫任一委派而直接返回。工作階段已開啟但選取其他頁面時，仍啟動選擇器。
2. 在呼叫並等待選擇器前擷取世代。
3. 遇到 `OperationCanceledException` 時靜默返回。其他選擇器例外先檢查檢視身分，再檢查 `IsExportContextCurrent(capturedGeneration)`，只有兩者都通過才發布失敗。
4. 取得結果時，依序要求路徑非 null 且非空白、檢視身分相同、相同世代的活動頁已開啟。接受的路徑原樣交給直接匯出，token 為 `CancellationToken.None`。

主程式的身分委派比較目前檢視情境與呼叫工作流程前擷取的情境，保留 DataContext 替換拒絕。身分檢查與失敗回呼的例外位於選擇器 try 本體之外，會直接傳遞。Null、空白與取消的選擇結果不執行狀態回呼，保留主程式原有狀態。工作流程為新建構式與必要身分輸入新增 null 檢查；選擇器檢查保留原本的 `pickPathAsync` 參數名稱。

關閉／重開、重複開啟與實際頁面變更會使擷取世代失效；同頁面選取不會。主程式語言變更與重新整理不遞增世代。選擇器接受後，寫入完成只檢查世代，因此單獨替換檢視不會抑制狀態發布。關閉工作階段不會取消或刪除已執行中的寫入。Await 延續保留呼叫端情境；主程式在原有序列化情境中執行工作階段操作與回呼。Core 不新增 dispatcher、鎖定、關閉逾時、dispose API 或取消政策。

## 保留的重新整理協調行為

`MessageCenterRefreshCoordinator` 來自 NFC 的 `MessageCenterViewModel`。主機提供重新整理委派，委派收到核准的強度（`reloadSources`）與擁有者的 token。

- 沒有未完成的重新整理時，`RefreshAsync` 以呼叫者的 token 啟動委派，並記為進行中的重新整理。
- 相容的請求會加入未完成的進行中工作：觀察可加入任何工作，重新載入只加入進行中的重新載入。加入者以自己的 token 等待，取消時不會取消擁有者。
- 進行中工作是觀察時，重新載入請求會先等它結束，忽略該工作無關的失敗或取消，再啟動一次完整的重新載入。相容的重新載入請求可以加入這次新的嘗試。
- `RefreshAfterCurrentAsync` 一定先等目前未完成的工作，再照一般規則核准。只有目前的工作不算滿足這個請求。等待中呼叫者取消時，取消會往外傳。
- 完成時只清除協調器自己記錄的工作。已完成的工作不會被加入。
- 核准不是執行緒安全的。主機要序列化呼叫與後續動作，NFC 在 UI 執行緒上這樣做。Core 不加 dispatcher、鎖或取消政策。發布、就緒與最新發布政策留在主機。
- `Lifecycle.CoalescedRefresh` 負責排程回呼，不能取代這個非同步合併器。

## 所有權與使用端契約

Core 擁有列契約、被動篩選與投影順序、模態世代機制、選擇器／匯出狀態發布工作流程，以及下述通用 Avalonia 展示。NFC 保留訊息來源、歷程、診斷轉換註冊、路徑 token 驗證、用語、在地化與時間格式、報告歷程、重新整理政策、儲存選擇器、匯出位元組／schema／路徑及組合範本。NFC 凍結的 **128 筆活動上限** 留在 NFC。顯示與匯出契約沒有 Core 歷程、路徑或大小上限，也沒有正值上限參數；工作階段的固定世代上限仍為 `long.MaxValue`。

NFC 在匯出委派執行時擷取目前診斷套件資料，並呼叫既有 `ISystemDiagnosticsExporter`。匯出器、JSON schema 與序列化 context、隱私允許清單、檔名、目錄規則、原子寫入與清理行為、實際 `StorageProvider` 及選擇器用語皆留在 NFC。Core 不寫入檔案或建立目錄；此擷取不將診斷匯出改接 `AtomicOutput`，也不建立報告歷程。

NFC 在建置時透過 `core-packages.json` 下載已驗證的版本套件，並以精確 `[x]` 套件版本、locked restore 與限定至下載資料夾的來源對應使用 Core。清單記錄每個套件的 Release 標籤與 SHA-256。使用端採用屬於獨立變更：改接已擷取的篩選、列、工作階段與匯出工作流程實作，且只有在相同記錄環境下通過完整值／事件軌跡、匯出位元組一致性、既有 JSON 隱私斷言及解碼 UI 像素完全相同的證據後，才刪除本地可執行副本。委派選擇器前先擷取檢視身分，並保留狹窄具型別別名與所有產品所有者。Core 測試本身不能證明 NFC 視覺或產品一致性。

## 來源至 Core 測試對照

測試使用 xunit.v3 與記憶體內合成資料，位於 [DisplayContractTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/DisplayContractTests.cs)、[SessionTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/SessionTests.cs)、[ExportWorkflowTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/ExportWorkflowTests.cs) 與 [RefreshCoordinatorTests.cs](../../../tests/Nvt.Core.Tests/MessageCenter/RefreshCoordinatorTests.cs)。工作流程測試不使用檔案系統或實際選擇器，並斷言完整目的地／token 記錄與選擇器、身分檢查、匯出器及狀態回呼的有序軌跡。

| 凍結來源證據 | Core 測試與保留斷言 |
| --- | --- |
| `ActivityHistoryUsesTwoDisclosureLevels` | `ActivityHistoryUsesTwoDisclosureLevelsAndReprojectsHostText`：預設隱藏 debug、保留 important success、展開後顯示 debug，並以第二組明顯不同的文字重新投影所有欄位。`BadgeAndSummaryCountsAndCaptureNeedNoRowProjection` 保留被動計數。 |
| `SystemActivityContentFitsAndFilters` | `InventoryFiltersPreserveRowsAndHostHistory`：相同警告／錯誤選擇、debug／important 遞減順序、警告／錯誤列旗標、切換頁面後主程式項目不變，以及原本 110 字元的合成詳細文字範例。`FiltersProjectOnlyRetainedRowsInExactOrder` 涵蓋六種篩選／揭露組合與回呼軌跡。 |
| `SystemActivitySelectionsPreserveNavigationAndHistory` | 空篩選、揭露、擷取後變更來源及頁面選取測試保留被動部分。產品導覽、IC 選取、報告歷程與繪製仍為 NFC 斷言。 |
| `DiagnosticsPickerRejectsClosedAndReopenedContext` | `ExportWorkflowTests.DiagnosticsPickerRejectsClosedAndReopenedContext`：舊選擇器不產生匯出或狀態，下一次目前選擇器以 `CancellationToken.None` 匯出並發布成功。`SessionTests.ClosedAndReopenedContextRejectsOldGeneration` 保留底層判斷。 |
| `DiagnosticsPickerFailureIsVisibleAndRetryable` | 同名工作流程測試保留失敗發布及 null 重試後的既有失敗，並新增成功重試。`CanceledPickerPreservesOldFailure` 涵蓋同步取消與已取消 Task。 |
| `DiagnosticsExportCompletionRejectsReopenedContext` | 同名工作流程測試保留重開後合成寫入確實完成、狀態仍為空的斷言，使用確定性閘門與來源十秒等待門檻。`SessionTests.DelayedObservationRejectsReopenedContext` 保留延遲判斷觀察。 |
| `DiagnosticsExportFailureGuidanceTests` 回呼觀察 | `ExpectedDirectFaultPublishesFailure`、`DirectCompletionRequiresOnlyUnchangedGeneration` 與重試回歸涵蓋通用失敗／成功發布。精確在地化訊息、活動中繼資料與版面留在 NFC 採用測試；通用重設斷言對照於下方展示測試。 |
| `MessageCenterKeepsSystemLifecycleSeparateFromRunReports` | `OpenAndClosePreserveTheSelectedPane`、`PaneChangesPreservePropertyChangingTiming` 與目前情境測試涵蓋模態／頁面行為。診斷轉換、Build 阻擋與報告生命週期仍為 NFC 斷言。 |

新增特性測試涵蓋完整無障礙字串與空欄位、無新增行為的綁定別名、打亂序號與 signed `long` 邊界的穩定同序號排序、隱藏列零投影、投影／列舉錯誤順序、未定義列舉值與延後檢查無效篩選、null 輸入、重複可見性操作、同頁面無動作、提交前狀態觀察及回呼失敗。世代邊界測試涵蓋 `long.MaxValue - 1`、`long.MaxValue`、嘗試遞增超過上限、在上限回呼失敗，以及兩個頁面在上限選取目前頁面時無動作。本模組沒有原生 process、handle 或 Job 行為。

匯出特性測試另涵蓋全部預期例外型別與衍生例外、同步與非同步錯誤、取消傳遞、關閉時啟動、空值／Unicode 空白及特殊字元目的地、其他頁面啟動、頁面來回切換、檢視身分替換、過期選擇器／寫入失敗、委派呼叫前擷取世代、身分先於工作階段的檢查順序、回呼例外範圍、並行完成順序，以及接受後的寫入在關閉後繼續執行且 `None` token 未取消。直接匯出使用零、負數與 signed 世代極值而不遞增；測試涵蓋 `long.MaxValue - 1`、`long.MaxValue` 及上限世代的選擇器接受。既有工作階段測試涵蓋嘗試超過固定上限的遞增。不新增工作流程上限。

## Avalonia 展示 API

`Nvt.Core.Avalonia.MessageCenter` 目標為 `net10.0`，使用共用的 CommunityToolkit.Mvvm 8.4.2 與 Avalonia 12.1.1 參考。Toolkit 不進入只依賴 BCL 的 `Nvt.Core` 組件。實作位於 [MessageCenterViewModel.cs](../../../src/Nvt.Core.Avalonia/MessageCenter/MessageCenterViewModel.cs)、[IMessageCenterText.cs](../../../src/Nvt.Core.Avalonia/MessageCenter/IMessageCenterText.cs) 與 [MessageCenterInteraction.cs](../../../src/Nvt.Core.Avalonia/MessageCenter/MessageCenterInteraction.cs)。

| API | 契約 |
| --- | --- |
| `MessageCenterInteraction` | `Opened`、`RefreshRequested`、`ExportSucceeded`、`ExportFailed`；由主程式記錄語意活動。 |
| `IMessageCenterText` | 六個標籤：`ShowDebugActivityLabel`、`HideDebugActivityLabel`、`RefreshDiagnosticsLabel`、`RefreshingDiagnosticsLabel`、`DiagnosticsExportedLabel`、`DiagnosticsExportFailedLabel`；三個接受 `int count` 的格式函式：`FormatSessionActivitySummary`、`FormatMessageCenterAccessibleName`、`FormatSystemDiagnosticAnnouncement`。 |
| `MessageCenterViewModel` | 非 sealed 的 partial `ObservableObject`；建構式 `(IMessageCenterProvider provider, Func<IMessageCenterText> textProvider, Func<CancellationToken, Task> refresh, Func<string, CancellationToken, Task> export, Action closeReport, Action<MessageCenterInteraction> interaction)`。保留凍結的文字優先檢查順序：文字提供者、被動提供者、重新整理、匯出、關閉報告、互動。 |
| 工作階段屬性 | 唯讀 `IsOpen`、`IsSystemInformationSelected`、`IsRunReportsSelected`、`ExportContextGeneration`；`bool IsExportContextCurrent(long generation)`。工作階段是唯一狀態所有者。 |
| 揭露屬性 | 唯讀 `SelectedActivityFilter`、`IsDebugActivityExpanded`、`IsImportantActivitySelected`、`IsWarningActivitySelected`、`IsErrorActivitySelected`。 |
| 被動投影 | `Text`、`ActivityItems`、`HasActivityItems`、`HasNoActivityItems`、`ActiveBadgeCount`、`HasActiveDiagnostics`、`HasNoActiveDiagnostics`、`SessionActivitySummary`、`DebugActivityActionLabel`、`MessageCenterAccessibleName`、`SystemStatusAnnouncement`、`RefreshActionLabel`。 |
| 主程式 facade 狀態 | `IsRefreshInProgress`、`ExportStatus`、`HasExportFailure` 保留 protected setter 與 Toolkit 通知。 |
| 命令 | `IRelayCommand`：`OpenCommand`、`CloseCommand`、`OpenRunReportsCommand`、`ShowRunReportsCommand`、`ShowSystemInformationCommand`、`ShowImportantActivityCommand`、`ShowWarningActivityCommand`、`ShowErrorActivityCommand`、`ToggleDebugActivityCommand`；`IAsyncRelayCommand RefreshCommand`。 |
| 操作 | `Task ExportAsync(string destinationPath, CancellationToken cancellationToken)`、`Task ExportWithPickerAsync(Func<Task<string?>> pickPathAsync, Func<bool> isViewContextCurrent)`、`void ReportExportFailure()`、`void ApplyLanguageChanged()`、`void NotifyActivityChanged()`；protected `void NotifyDiagnosticsChanged()`。三個 public 方法在 NFC 都有 view model 以外的呼叫者：modal 畫面的 code-behind 在選擇器失敗時呼叫 `ReportExportFailure`，shell view model 呼叫 `ApplyLanguageChanged` 和 `NotifyActivityChanged`。衍生的主程式 view model 在診斷重新整理完成後呼叫 `NotifyDiagnosticsChanged`。 |

## 保留的展示行為

展示狀態、命令、工作階段操作與非同步延續都在主程式 UI 執行緒執行，Core 不新增派送。工作階段回呼保留提交前的 `PropertyChanging` 與提交後的 `PropertyChanged`。開啟先遞增世代、清除失敗再清除狀態、記錄 `Opened`、通知活動，最後提交可見性。關閉先遞增世代、關閉報告，最後提交可見性。OpenRunReports 先關閉報告、選取報告頁，再開啟。重複開啟仍重設／記錄／通知；重複關閉仍關閉報告。同頁面選取無動作。頁面變更在提交前依序通知 `IsSystemInformationSelected` 與 `IsRunReportsSelected` 的 changing，在提交後以相同順序通知 changed。

篩選命令依序通知篩選值、`ActivityItems`、`HasActivityItems`、`HasNoActivityItems`，以及 important／warning／error 旗標。揭露依序通知本身、上述三個活動屬性，最後 `DebugActivityActionLabel`。Toolkit 在提交前以相同順序發出主屬性及相依屬性的 changing，提交後發出 changed；同值 setter 無動作。計數原樣交給主程式格式函式，包含零與負數；目前診斷保留精確的 `count > 0` 判斷。Badge 與摘要讀取不擷取或投影活動。活動 getter 每次擷取新中繼資料，經共用篩選後才呼叫主程式投影，不新增列快取。

語言變更依序通知 `Text`、`MessageCenterAccessibleName`、`SystemStatusAnnouncement`、`RefreshActionLabel`、`ActivityItems`、`SessionActivitySummary` 與 `DebugActivityActionLabel`，再清除失敗及非空狀態，不變更世代。文字與投影回呼每次使用目前主程式文字。此操作保留原本不通知活動有無屬性的行為。

重新整理先記錄 `RefreshRequested`、通知活動，再呼叫供應的明確重新整理委派。主程式擁有進度切換、成功重設、診斷發布、就緒狀態及其重新整理位置的例外隔離。內層重新整理使用 `MessageCenterRefreshCoordinator`；VM 不新增整個命令的合併器。Toolkit 擁有命令執行、CanExecute 與取消。進度依序通知本身、`SystemStatusAnnouncement`、`RefreshActionLabel`，提交前發出 changing，提交後發出 changed。繼承的 protected virtual 通知接縫可覆寫，讓 facade 在通用名稱之間依凍結順序插入產品通知。

匯出透過同一工作階段的 `MessageCenterExportWorkflow`。成功先記錄 `ExportSucceeded`、清除失敗、設定供應的成功標籤，再通知活動。失敗先記錄 `ExportFailed`、設定失敗、設定供應的失敗標籤，再通知活動。Core 不建立歷程或診斷套件資料。活動通知各自隔離 `ActivityItems`、`HasActivityItems`、`HasNoActivityItems` 與 `SessionActivitySummary`。診斷通知各自隔離計數／旗標、無障礙名稱與公告，再通知活動。隔離追蹤保留 `Presentation observer failed: {0}`。語言、模態屬性通知、互動回呼、文字回呼與狀態 setter 保持未隔離；匯出回呼例外保留工作流程的 catch 範圍。

可見性、選取與世代只存於工作階段。推導旗標、計數與標籤沒有重複狀態。失敗樣式與狀態保留獨立可寫屬性，因為反射寫入與觀察者例外可能讓兩者分離；原子替換會改變凍結行為。重新整理進度只有一個主程式所有的旗標，VM 不新增執行階段或世代。不新增正值上限參數；既有 `long.MaxValue` 固定世代上限與主程式所有的 128 筆歷程上限不變。

## 展示測試對照與採用

[PresentationTests.cs](../../../tests/Nvt.Core.Avalonia.Tests/MessageCenter/PresentationTests.cs) 使用 [PresentationTestValues.cs](../../../tests/Nvt.Core.Avalonia.Tests/MessageCenter/PresentationTestValues.cs)、被動合成提供者、明顯不同的 A/B 文字、有序軌跡、確定性閘門及衍生 facade。UI 執行緒案例使用既有 linked-source [Testing 主機](Testing.zh-TW.md) 與唯一組件註冊，保留 Inter、Skia、`UseHeadlessDrawing=false`。

| 凍結來源斷言 | 展示測試 |
| --- | --- |
| `ActivityHistoryUsesTwoDisclosureLevels` | `ActivityDisclosureAndLanguageReprojectionPreserveResetOrder`、`FiltersProjectOnlyDisclosedMatchingMetadata`：預設揭露、展開、完整 A/B 重新投影及精確狀態重設。 |
| 模態命令與 `MessageCenterKeepsSystemLifecycleSeparateFromRunReports` | `ModalCommandsPreserveCompleteTransitionOrder`、提交前後觀察者與關閉報告錯誤測試：完整動作／屬性順序、重複命令及不變的報告所有權。 |
| `RefreshCommandPublishesVisibleProgressUntilReloadCompletes` | `RefreshCommandPublishesProgressUntilHostReloadCompletes`、`RefreshObserverIsolationPreservesSuccessFailureAndCancellation`、命令取消與內層協調器測試：五秒閘門、進度、重設、觀察者隔離及原樣傳遞操作錯誤。 |
| `ShellNavigationSystemTests.MessageCenter.cs` 的選擇器／匯出測試 | `DiagnosticsPickerRejectsStaleContext`、`DiagnosticsPickerFailureIsVisibleAndRetryable`、`DiagnosticsExportCompletionRejectsReopenedContext`：過期身分／世代拒絕、重試、十秒寫入閘門、合成寫入確實完成而沒有過期狀態。 |
| `DiagnosticsExportFailureGuidanceTests` | `ExportStatusPreservesInteractionSetterAndActivityOrder`、`InheritedPropertiesRetainReflectionSetters`、語言／開啟重設測試：通用狀態與綁定 facade 行為。 |
| 重新整理／匯出歷程分離 | `RefreshAndExportPreserveUnrelatedHistory`：無關的合成報告清單不變。 |
| `NotifySystemStateChanged` | `FacadeInsertsProductNotificationsInFrozenBatchOrder`、`HostDiagnosticObserverFaultsRetainCommittedStateAndCompleteTrace`、`BadgeObserverFaultKeepsHostCurrentAndLaterSites`：產品的 `Current` 在 `ActiveBadgeCount` 之前、位於獨立的位置，各診斷位置獨立隔離，已提交狀態不變，且錯誤只中止同一位置內其餘訂閱者。 |

另涵蓋 -1/0/1 與 signed 極值的被動計數、全部六種篩選／揭露組合與嚴重性旗標、文字優先的 null 檢查順序、null／空狀態重設、延後判斷未定義篩選、完整語言／診斷／進度 facade 通知插入、已提交與未隔離觀察者範圍、回呼錯誤，以及每種遞增模態機制的 `long.MaxValue - 1`、`long.MaxValue` 與超限嘗試。同頁面命令在上限仍無動作；開啟報告保留上限處的部分提交順序。不涉及原生 process／handle／Job 行為或新增截圖預期。

NFC 保留精確的 XAML 祖系與用語、報告歷程表格及內容、診斷匯出器、儲存選擇器、設定、目錄就緒、最新發布及重新整理政策，透過上述介面接入。採用使用狹窄的具型別綁定／政策 facade，以及共用工作階段、篩選、重新整理協調器與匯出工作流程。只有在相同記錄的 OS、字型、DPI、佈景、繪製器、viewport、動作、輸入、時間及 ID 下，完整值、事件軌跡、匯出位元組及解碼 UI 像素符合凍結來源，才刪除本地可執行副本。保留八個舊字型值。NFC 的 Inter／Skia 產品主機擁有像素、焦點證據與產物 SHA-256 記錄。NFC 在建置時透過 `core-packages.json`（Core #61）下載已驗證的版本套件，使用上述精確版本與 locked restore。Core 測試本身不能證明 NFC 產品或視覺一致性。
