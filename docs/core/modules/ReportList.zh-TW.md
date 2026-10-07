[English](ReportList.md) | [中文](ReportList.zh-TW.md)

# ReportList

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportIndexedReadOnlyLists.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ResettableObservableCollection.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportWindowedListViewModel.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportPagedListViewModel.cs`

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

模組位於 `src/Nvt.Core/ReportList/`。
命名空間為 `Nvt.Core.ReportList`。
目標框架為 .NET 8。
模組僅依賴 BCL。
模組統一擁有下列四個泛型集合型別。
分頁模型位於 `src/Nvt.Core.Avalonia/ReportList/`，命名空間為 `Nvt.Core.Avalonia.ReportList`，目標框架為 .NET 10。
模型使用 CommunityToolkit.Mvvm 8.4.2 與共用集合，不依賴 Locale。
internal 的非複製 `ObjectReadOnlyList<T>` 轉接器來自 `ReportIndexedReadOnlyLists.cs:105-126`。
兩個模型的演算法完整擷取；NFC 保留語言對應並提供標籤。

## 公開 API

`MemoizedIndexedReadOnlyList<T>(int count, Func<int, T> factory)` 實作 `IReadOnlyList<T>`。
`T` 必須是參考型別。
`Count` 傳回宣告的筆數。
`MaterializedCount` 計算成功建立的列數。
每個索引首次存取時才配置 lazy 容器。
並行讀取者共用每個索引發布的同一列。
工廠例外依索引快取。
工廠傳回 null 時會擲出 `InvalidOperationException`。
例外訊息為 `A report row factory returned null.`。
這個例外也會快取。
建構函式先拒絕負數筆數，再檢查工廠。
無效列索引會擲出參數名稱為 `index` 的 `ArgumentOutOfRangeException`。
列舉到某列時才建立該列。
`HasMaterializedReference` 保持 internal。

`FactoryReadOnlyList<T>(int count, Func<int, T> factory)` 實作 `IReadOnlyList<T>`。
它接受參考型別與值型別。
`Count` 傳回宣告的筆數。
每次存取有效索引都會呼叫工廠。
工廠結果不會保留。
工廠可以傳回 null。
工廠失敗後，下次存取會重新呼叫。
建構函式先檢查工廠，再拒絕負數筆數。
無效列索引會擲出參數名稱為 `index` 的 `ArgumentOutOfRangeException`。
列舉到某列時才呼叫工廠。
此集合沒有固定的每頁筆數上限。

`IndexedReadOnlyList<T>(IReadOnlyList<T> source, IReadOnlyList<int> indices)` 實作 `IReadOnlyList<T>` 與 `IList`。
`T` 必須是參考型別。
建構函式先檢查 `source`，再檢查 `indices`。
它會複製選取的索引。
它會保留來源清單。
索引順序與重複項目會保留。
無效來源索引會擲出參數名稱為 `indices` 的 `ArgumentOutOfRangeException`。
`Count` 傳回選取的索引數。
索引子傳回原始來源列。
無效檢視位置會擲出 `IndexOutOfRangeException`。
列舉遵循選取的順序。
`IList.IndexOf` 與 `IList.Contains` 不會建立 memoized 來源列。
Memoized 查詢採用參考身分比對。
其他來源採用 `Equals`。
`IList.IsFixedSize` 與 `IList.IsReadOnly` 為 true。
`ICollection.IsSynchronized` 為 false。
`ICollection.SyncRoot` 是檢視本身。
所有 `IList` 修改操作都會擲出 `NotSupportedException`。
例外訊息為 `The indexed report projection is read-only.`。
`ICollection.CopyTo` 拒絕 null 陣列。
它依選取順序使用 `Array.SetValue` 複製。
目的索引加法使用 checked 算術。
陣列操作失敗可能留下已複製的前綴。
空檢視不會驗證目的索引或陣列維度。

`ResettableObservableCollection<T>` 繼承 `ObservableCollection<T>`。
`ReplaceAll(IEnumerable<T> items)` 保留集合身分。
它先拒絕 null 輸入，再檢查重入。
它先清空集合，再列舉輸入。
新增輸入項目時不會發送逐項通知。
通知順序是 `Count`、`Item[]`，最後一次集合 `Reset`。
空集合或相同內容的替換也會發送這些通知。
以集合本身作為輸入會產生空集合。
列舉失敗會留下已加入的前綴。
列舉失敗時不會發送替換通知。
觀察者例外會從發生例外的通知傳出。
繼承的重入規則保持不變。

### 分頁模型與標籤

`ReportListLabels` 是 sealed record，包含不可變的 `NoItems`、`PreviousPage`、`NextPage` 與 `AllItemsLoaded` 字串。
其 `WindowStatus(first, last, total)`、`PagedStatus(visible, total)` 與 `LoadMore(next, remaining)` 委派提供格式化。
主應用程式提供穩定的格式化函式。兩個模型建構時遇到 null 標籤或格式化函式，會擲出 `ArgumentNullException`。
模型沒有可變的重新本地化 API。

兩個 sealed 檢視模型皆繼承 Toolkit `ObservableObject`，並提供：

```csharp
Create<T>(IReadOnlyList<T> items, int pageSize, ReportListLabels labels,
    bool loadInitialPage = true)
```

模型保留來源清單，不複製也不列舉。
主應用程式必須保持來源筆數穩定，並在 UI 執行緒存取模型、執行命令及發送通知。
首次載入僅以索引讀取；延後建構時不讀取任何列。
`Items` 是身分固定的 `ReadOnlyObservableCollection<object>`。
參考型別列保留來源身分；值型別列會裝箱，null 元素原樣傳遞。
`TotalCount` 讀取來源筆數；`VisibleCount` 讀取可見集合筆數。
`pageSize` 必須為正數；兩個凍結模型皆沒有固定上限。
NFC 的累積報表批次使用 8、24 與 40，變更區塊視窗使用 64；這些政策仍由主應用程式透過參數決定。
建立時依序檢查 `items`、`pageSize` 與注入的 `labels` 引數。
無效大小的 `ArgumentOutOfRangeException` 保留 `pageSize` 參數名稱與實際值。

`ReportWindowedListViewModel` 使用 `ResettableObservableCollection<object>.ReplaceAll` 保留單一固定視窗。
它提供 `PageIndex`、`PageCount`、`HasPreviousPage`、`HasNextPage`、`HasMultiplePages`、`PageStatus`、`PreviousPageLabel` 與 `NextPageLabel`。
`PreviousPageCommand` 與 `NextPageCommand` 是身分固定的 `IRelayCommand`。
下一頁命令可以載入延後的第一個視窗。
空來源的頁數為零並使用 `NoItems`；延後載入的非空視窗也會使用 `NoItems`，直到載入為止。
`ShowItemAt(index)` 使用凍結來源的 unsigned 比較，拒絕 `[0, TotalCount)` 以外的索引。
例外的參數名稱為 `index`，不儲存實際值。
此方法僅選取包含該列的頁面；目前頁面已可見時不進行任何工作。
所有列建立完成後，才變更頁面與替換視窗。
新 `PageIndex` 在集合通知前指派。
通知順序是集合 `Count`、`Item[]`、一次 `Reset`；模型 `VisibleCount`、`PageIndex`、`HasPreviousPage`、`HasNextPage`、`HasMultiplePages`、`PageStatus`；上一頁命令可用性，最後下一頁命令可用性。
`PageIndex` 不使用產生的 observable setter，因為那會改變此順序。

`ReportPagedListViewModel` 累積保留所有已載入列，並提供 `RemainingCount`、`HasMoreItems`、`PageStatus`、`LoadMoreLabel` 與身分固定的 `IRelayCommand`：`LoadMoreCommand`。
`EnsureInitialPage()` 僅在 `VisibleCount == 0 && TotalCount > 0` 時載入。
任何列可見後，重複呼叫皆具冪等性。
狀態一律使用 `PagedStatus`，包括空來源。
下一批標籤使用每頁大小與剩餘筆數的較小值；結束標籤為 `AllItemsLoaded`。
每個新增列依序發送集合 `Count`、`Item[]` 與 `Add`，然後模型發送 `VisibleCount`、`RemainingCount`、`HasMoreItems`、`PageStatus`、`LoadMoreLabel`，最後命令可用性。
消費者在各次 Add 時可觀察逐步增長的前綴。
來源或觀察者失敗會保留已加入的前綴，並在後續通知前傳出例外。
Toolkit `RelayCommand.Execute` 不強制檢查 `CanExecute`：在結尾直接執行時，仍進行凍結的 checked 加法，成功後仍發送模型通知。
視窗命令保留自己的邊界判斷，在兩端不進行任何工作。

所有凍結的 checked 算術與運算式順序保持不變。
視窗結尾加法與累積批次加法會先做 checked 檢查，再限制至總筆數。
視窗狀態保留 `first + VisibleCount - 1` 的中間值溢位，包括 `int.MaxValue` 來源最後一列的單列視窗。
模型不儲存衍生筆數或可用性旗標；視窗頁碼是唯一獨立的可變位置。
集合內容與頁碼由單一模型擁有，僅於 UI 執行緒存取。

## 測試與來源

測試位於 `tests/Nvt.Core.Tests/ReportList/`。
所有測試資料皆為合成資料。
`tests/NvtFwCombiner.UiSmoke.Tests/ReportIndexedReadOnlyListsTests.cs` 的三個 fact 均已移植。
配置量上限保持不變。
確定性的並行讀取同步保持不變。
單次 Reset 斷言來自 `tests/NvtFwCombiner.UiSmoke.Tests/ReportWindowedListViewModelTests.cs`。
合成視窗保留每頁 64 筆的邊界。
最後一頁保留兩筆資料。
Reset 次數上限來自 `tests/NvtFwCombiner.UiSmoke.Tests/MemoryCoveragePublicationTests.cs`。
觀察狀態身分斷言來自 `tests/NvtFwCombiner.UiSmoke.Tests/MemoryCoverageStatePublicationTests.cs`。
僅改寫其中的集合斷言。
產品模型與 golden fixture 不會匯入。
新增測試涵蓋空清單與單筆資料。
新增測試涵蓋索引邊界與無效引數。
新增測試涵蓋 64 筆與 65 筆。
工廠測試涵蓋 `int.MaxValue`，且不配置各列。
測試涵蓋 null 失敗快取。
測試涵蓋不建立列的查詢。
測試涵蓋所有 `IList` 修改拒絕行為。
測試涵蓋 `CopyTo` 失敗行為。
測試涵蓋替換通知順序與重入。
測試涵蓋列舉失敗與觀察者失敗。

分頁測試位於 `tests/Nvt.Core.Avalonia.Tests/ReportList/`，使用合成列與字面注入標籤。

| 凍結來源斷言 | Core 對應 |
| --- | --- |
| `ReportWindowedListViewModelTests.NavigationReplacesTheCurrentFixedSizeWindow` | 同名類別與方法；保留 64 列視窗、共 130 列、最後兩列視窗、標籤、命令與單次 Reset。 |
| `ReportWindowedListViewModelTests.DirectItemNavigationShowsOnlyTheContainingWindow` | 同名類別與方法；保留 10,000 列、直接索引 9,999、最後 16 列視窗、80 次工廠呼叫及重選時不進行工作。 |
| `ReportWindowedListViewModelTests.NavigationLabelsFollowTheSelectedShellLanguage` | `ReportWindowedListViewModelTests.NavigationLabelsFollowTheInjectedLabels`；精確的雙語斷言改用注入值。 |
| `ReportProjectionConcurrencyTests.ReportPerformance.cs:105-172` | `ReportListMechanismTests.LargeIndexedProjectionUsesBoundedDeferredAndMemoizedPages`；1,000 列、40 群組、8 列摘要、延後載入 25 列中的 24 列明細、快取身分與累積載入 16 群組。報表 JSON 與產品判定斷言保留在 NFC。 |
| 共用記憶體 Reset 發布與觀察狀態身分 | 既有 `Nvt.Core.Tests.ReportList.ResettableObservableCollectionTests` 保留集合斷言與通知次數界線。 |

直接的 `ReportPagedListViewModelTests` 涵蓋空來源、延後載入、每頁一列、完整與部分頁面、多次載入、結尾命令、冪等性、來源身分及失敗。
兩個模型測試類別皆涵蓋零與負每頁大小、正數下界、`int.MaxValue` 與相鄰值，以及引數驗證順序。
視窗測試涵蓋無效索引、兩個有效索引邊界、頁面邊界相鄰值與空清單索引。
筆數案例涵蓋主應用程式大小 8、24、40 與 64 的上下邊界，包括第二頁邊界。
溢位測試涵蓋中間加法低於、等於與高於 `int.MaxValue`，且不配置龐大清單。
測試斷言完整通知軌跡、每次 Add 的狀態、每次替換單次 Reset、重選不進行工作、命令與集合身分，以及來源零列舉。
`ReportListMechanismTests` 另涵蓋 null 元素、memoized 視窗重訪、不可變標籤選擇及 null 標籤成員。
自訂標籤與兩種凍結語言皆不同。

套件還原完成後，於儲存庫根目錄執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

## 已知差異

命名空間改為 `Nvt.Core.ReportList`。
四個型別改為 public。
其建構函式與必要成員改為 public。
實作拆成四個來源檔案。
加入著作權標頭與 XML API 文件。
判斷式、常值、訊息與方法主體保留凍結來源的行為。
`ObjectReadOnlyList<T>` 是 Avalonia 分頁模型的 internal 型別，不新增公開集合擁有者。
兩個模型及其建立／導覽成員在 `Nvt.Core.Avalonia.ReportList` 改為 public。
語言分支改為不可變注入標籤與格式化函式；null 標籤與標籤內的 null 成員，都在凍結的來源與每頁大小驗證後拒絕。
凍結模型的通知順序與 Toolkit 命令行為保持不變。
分頁範本與這些模型分開。

## NFC 擁有範圍與採用

NFC 保留報表列模型與視窗導覽政策。
NFC 保留記憶體涵蓋投影與互動狀態建構。
共用的替換集合供 `MergeCoverageSegments` 使用。
共用的替換集合也供 `ReplaceCoverageSegments` 使用。
共用的替換集合也供 `CtrlRamOverview` 使用。
MessageCenter 的被動活動投影不屬於此模組。
後續使用者必須共用同一個替換集合實作。
NFC 採用時必須鎖定已審查的確切 Core revision 或套件版本。
NFC 於建置時透過 `core-packages.json` 下載經驗證的版本化套件（Core #61）。
使用精確 `[x]` 版本、locked restore，以及限制至套件下載資料夾的來源對應。
Manifest 記錄各套件的 Release tag 與 SHA-256；Core 與 NFC 保持各自版本化發布。
所有呼叫端使用已審查的套件，且等效可執行檢查保留凍結的值、身分、具現化與通知後，才刪除 NFC 的本地泛型集合、分頁模型與 object 轉接器。
所有報表與共用記憶體 Reset 消費者皆須改用同一個集合擁有者。
NFC 保留獨立標籤工廠、ShellLanguage 對應、列工廠、報表 DTO、schema、匯出、非同步提供者、報表歷史與產品導覽政策。
MessageCenter 保留獨立報表歷史表格。
NFC 既有功能與發布測試須對照凍結基準執行。
比對內容須包含完整值、列身分、具現化數量與通知。
NFC UI 採用仍須保持解碼後像素零差異。
僅通過 Core 測試無法證明 UI 零差異。
比對時固定 OS、實際字型、DPI、佈景、renderer、viewport、motion、input、time 與 IDs，並記錄各產物的 SHA-256。
適用時比較完整值、事件軌跡與輸出 bytes，並保留八個既有字型值。
