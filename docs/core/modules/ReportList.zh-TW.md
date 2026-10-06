[English](ReportList.md) | [中文](ReportList.zh-TW.md)

# ReportList

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportIndexedReadOnlyLists.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ResettableObservableCollection.cs`

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

模組位於 `src/Nvt.Core/ReportList/`。
命名空間為 `Nvt.Core.ReportList`。
目標框架為 .NET 8。
模組僅依賴 BCL。
模組統一擁有下列四個泛型集合型別。

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

套件還原完成後，於儲存庫根目錄執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

驗證使用以 Core `b98099a43553f4d04f084a3a33bc027bd9a8c95c` 為基礎的本地工作目錄。
建置通過。
建置回報 0 個警告。
建置回報 0 個錯誤。
完整方案測試中的 84 個 ReportList 案例全部通過。
Core 測試專案通過 377 個測試。
Avalonia 測試專案通過 260 個測試。
沒有失敗的測試。
沒有略過的測試。

## 已知差異

命名空間改為 `Nvt.Core.ReportList`。
四個型別改為 public。
其建構函式與必要成員改為 public。
實作拆成四個來源檔案。
加入著作權標頭與 XML API 文件。
判斷式、常值、訊息與方法主體保留凍結來源的行為。
`ObjectReadOnlyList<T>` 保留給後續 Avalonia 模型擷取。
此模組沒有新增 UI。

## NFC 擁有範圍與採用

NFC 保留報表列模型與視窗導覽政策。
NFC 保留記憶體涵蓋投影與互動狀態建構。
共用的替換集合供 `MergeCoverageSegments` 使用。
共用的替換集合也供 `ReplaceCoverageSegments` 使用。
共用的替換集合也供 `CtrlRamOverview` 使用。
MessageCenter 的被動活動投影不屬於此模組。
後續使用者必須共用同一個替換集合實作。
NFC 採用時必須鎖定已審查的確切 Core revision 或套件版本。
四個泛型集合的使用者全部改用該版本後，才刪除 NFC 的本地副本。
保留的 object 檢視須等候其獨立擷取完成採用。
NFC 既有功能與發布測試須對照凍結基準執行。
比對內容須包含完整值、列身分、具現化數量與通知。
NFC UI 採用仍須保持解碼後像素零差異。
僅通過 Core 測試無法證明 UI 零差異。
