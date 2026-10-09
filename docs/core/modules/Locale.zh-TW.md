[English](Locale.md) | [中文](Locale.zh-TW.md)

# Locale：共用文字

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Core.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Localized.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Settings.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Report.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Navigation.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportWindowedListViewModel.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportPagedListViewModel.cs`

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

## API

[`CommonTextResources`](../../../src/Nvt.Core/Locale/CommonTextResources.cs) 位於 `Nvt.Core.Locale`。
目標框架為 `net10.0`。
此模組只依賴 BCL。
`CommonLanguage` 包含 `English` 與 `TraditionalChinese`。
`For(language)` 為每種語言回傳一個延遲建立的快取物件。
`Language` 記錄所選語言。
資源物件提供 23 個唯讀字串屬性。
資源物件提供三個整數格式方法。
此型別沒有公開建構子。
不支援的語言值會拋出 `ArgumentOutOfRangeException`。
例外保留參數名稱 `language` 與傳入的列舉值。

## 來源對應

未列出的共用標籤沿用來源名稱。
相同的 Cancel 文字共用 `CancelLabel`。
相同的 Preferences 文字共用 `PreferencesLabel`。

| 凍結來源成員 | Core 成員 |
| --- | --- |
| `ShellLanguage.ChineseTraditional` | `CommonLanguage.TraditionalChinese` |
| `BackTooltip` | `BackLabel` |
| `OutputDeliveryCancelLabel`, `FirmwareNumberMismatchCancelLabel` | `CancelLabel` |
| `ExitConfirmLabel` | `ExitLabel` |
| `LeaveEditorConfirmLabel`, `OutputDeliveryConfirmLabel` | `ContinueLabel` |
| `NavigationClearCancelLabel` | `StayOnPageLabel` |
| `SettingsPreferencesTitle`, `LocalStatePreferencesLabel` | `PreferencesLabel` |
| `SystemThemeChoiceLabel`, `LightThemeChoiceLabel`, `DarkThemeChoiceLabel` | `SystemThemeLabel`, `LightThemeLabel`, `DarkThemeLabel` |
| `EnglishLanguageChoiceLabel`, `ChineseTraditionalLanguageChoiceLabel` | `EnglishLanguageLabel`, `TraditionalChineseLanguageLabel` |
| Windowed `PageStatus` 字串插值 | `FormatWindowStatus(first, last, total)` |
| Paged `PageStatus` 字串插值 | `FormatPagedStatus(visible, total)` |
| Paged `LoadMoreLabel` 字串插值 | `FormatLoadMore(next, remaining)` |

## 行為與使用方式

兩種語言保留凍結來源的文字與標點。
三個格式方法在每次呼叫時使用目前文化設定。
方法直接插入傳入的整數。
方法不驗證或限制數值。
方法不加入數字分組格式。
方法不設定分頁上限。
共用文字的使用端以注入方式取得所選資源物件。
呼叫端保留分頁狀態與參數驗證。
Windowed 呼叫端在 `VisibleCount == 0` 時選用 `NoItemsLabel`。
其他情況先以 checked 運算取得首尾位置，再格式化文字。
Paged 呼叫端一律格式化已顯示數量與總數。
在 `!(RemainingCount > 0)` 時選用 `AllItemsLoadedLabel`。
其他情況將 `Math.Min(pageSize, RemainingCount)` 傳入 `next`。
這些判斷保留在使用端。
直接傳入零值不會自動選用空清單或全部載入標籤。

NFC 保留偏好設定解析與文化設定選擇。
NFC 保留產品用語與診斷格式方法。
Message Center 診斷與 launcher 訊息保留在原產品。
後續清單使用端注入 `ReportListLabels`。
後續 Message Center 使用端注入 `IMessageCenterText`。
這兩個型別不屬於本模組 API。
共用文字 adapter 的呼叫端全部改用此資源且採用證據通過後，才可刪除 adapter。

## 驗證與已知差異

Locale 測試以 ordinal 及 UTF-8 位元組比對兩種語言的全部 23 個標籤。
測試移植凍結來源的快取物件識別與雙語導覽標籤斷言。
測試涵蓋空清單與單筆文字。
測試涵蓋 63、64、65、130、10,000 與 10,001 筆數。
測試涵蓋無效列舉值與整數極值。
測試驗證五種文化設定與合成負號。
測試驗證完整公開成員清單與唯讀屬性。
本次不預期改變文字或格式方法行為。
來源成員改名列於上方表格。

主機既有套件還原完成後執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

NFC 採用是另一個任務。
Core 測試不能證明 NFC 採用後的像素完全相同。
