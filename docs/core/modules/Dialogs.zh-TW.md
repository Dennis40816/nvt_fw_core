[English](Dialogs.md) | [中文](Dialogs.zh-TW.md)

# Dialogs

## 用途與公開 API

`src/Nvt.Core.Avalonia/Dialogs/` 提供兩個小型視窗，命名空間為 `Nvt.Core.Avalonia.Dialogs`：

- `ConfirmDialog()` 與 `ConfirmDialog(string title, string message, string confirmText, string cancelText, bool emphasizeCancel = false, string? confirmTip = null, string? cancelTip = null)`。確認按鈕以 `true` 完成 `ShowDialog<bool>(owner)`；取消按鈕以 `false` 完成。強調取消時加入 `danger` 並顯示關閉圖示。任一提示為 null 時，對應按鈕沒有工具提示。
- `WarningDialog()` 與 `WarningDialog(string title, string message)`。按鈕文字為 `OK`；點擊後呼叫 `Close()` 並完成 `ShowDialog(owner)`。

兩個視窗均保留可選取的標題與訊息、訊息自動換行、固定 360 × 170 尺寸、`CanResize = false` 及 `WindowStartupLocation = CenterOwner`。無參數建構函式載入相同版面，內容為空。如同 NFH，傳入的標題是可選取的內容標題，不會設定 `Window.Title`。

每個視窗在自身樣式中載入 `DialogsStyles.axaml`。應用程式將 `avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml` 載入資源，並提供原有的視窗／按鈕控制項佈景及基本按鈕／danger 樣式。Core 現有的 `Theme/ButtonStyles.axaml` 可提供按鈕樣式。未新增應用程式佈景、對話框服務、選項記錄或其他設定。

## 來源

凍結的母專案基準：儲存庫 `Dennis40816/nvt-freeform-helper`、ref `1.3.x`、完整 commit `e01e07a361b8dc264a06b3741f40274feeeace2d`、Avalonia 11.3.12 與 xUnit 2。抽取路徑：

- `src/FreeformHelper.UI/Views/ConfirmDialog.axaml`
- `src/FreeformHelper.UI/Views/ConfirmDialog.axaml.cs`
- `src/FreeformHelper.UI/Views/WarningDialog.axaml`
- `src/FreeformHelper.UI/Views/WarningDialog.axaml.cs`
- `src/FreeformHelper.UI/Styles/Controls.Overlay.axaml` — 僅五個 `confirmDialog*` 規則，不含工具提示樣式。
- `src/FreeformHelper.UI/Styles/Tokens.axaml` — 僅這些視窗與規則參照的值，包含 `DialogWidth`、`DialogHeight` 與 `ConfirmDialog*`。

既有證據來自 `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.CommandsAndUndo.InputAndExport.cs` 的 `ExportDxfLayerImageCommand_WhenDxfMissing_ShowsWarningDialog`。此測試擷取警告請求，而非建立對話框。Core 僅移植其中的警告標題與訊息斷言，並使用 `src/FreeformHelper.UI/ViewModels/FreeformHelperViewModel.DxfEditing.Export.cs` 中凍結的呼叫端文字；命令、狀態及 ViewModel 行為留在 NFH。提供的來源覆蓋範圍沒有直接對話框測試。

僅為了解情境而檢視的呼叫端：`src/FreeformHelper.UI/Views/FreeformHelperView.Pickers.cs`、`LeftDxfPanel.axaml.cs`、`NotchExportSelectionWindow.axaml.cs`、`SettingsWindow.axaml.cs` 及 `src/FreeformHelper.UI/MainWindow.axaml.cs`。最後一個是「Unsaved project」提示。它是唯一設定 `emphasizeCancel: true`、也是唯一 await `ShowDialog<bool?>` 的呼叫端。不按按鈕直接關閉視窗會回傳 null，讓應用程式保持開啟。取消回傳 false，代表不存檔就離開。

## 資源對應

以下列出所有抽取的 NFH 資源與現有 Core 資源之對應。全部 Core 參照使用 `DynamicResource`；`ThemeTokens.axaml` 未變更。

| NFH token | Core token | 用途 |
| --- | --- | --- |
| `BrushBgSurface` | `NfcSurfaceBrush` | 視窗背景 |
| `BrushTextPrimary` | `NfcTextStrongBrush` | 可選取標題 |
| `BrushTextSubtle` | `NfcTextBrush` | 可選取訊息 |
| `BrushWhite` | 按鈕的實際前景色 | 取消向量圖示描邊 |
| `BrushWarning` | `NfcWarningAccentStrongBrush` | 警告向量圖示描邊 |
| `Space8` | `NfcSpace8` | 操作列／標題列間距 |
| `ConfirmDialogDangerContentSpacing` | `NfcSpace8` | 取消圖示與文字間距 |

字型形式的 `IconGlyphs.Close` 改為透過 `DynamicResource` 使用 `NfcCloseIconGeometry`。

## 驗證

Core 使用 Avalonia 12.1.1、xUnit v3 及未變更的 `ThemeTestApplication` 無介面測試宿主。`tests/Nvt.Core.Avalonia.Tests/Dialogs/` 覆蓋可選取的傳入文字（一般、空字串、Unicode、多行）、兩種實際 `ShowDialog<bool>` 結果、擁有者、強調與一般取消狀態、無參數載入、獨立的選用／null／空字串提示、凍結的視窗／操作列版面、畫刷隨佈景切換、警告內容，以及實際模態工作的 OK 關閉行為。警告內容案例是限縮範圍的既有 NFH 證據移植；其他案例刻畫凍結對話框的行為。

使用既有套件還原結果執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Dialogs"
```

NFH 採用時：

1. 在 `FreeformHelperView.Pickers.cs` 的 Embed DXF 呼叫點傳入 `confirmTip: "Embed DXF into the project file."` 與 `cancelTip: "Cancel and keep external DXF reference."`。其他呼叫端選擇自己的提示或保留 null。凍結對話框將這兩個產品提示套用至每個實例；Core 明確改由呼叫端提供。
2. 採用 Core 的色彩。不在應用程式範圍以 NFH 的值覆寫 `Nfc*` 鍵。取消圖示採用按鈕的實際前景色，因此在任何使用端與按鈕狀態下都與取消文字同色。導入圖片呈現強調取消按鈕的一般與滑鼠移上狀態。
3. 執行上述 Core Dialogs 測試群組，以及 NFH 完整既有測試專案：`dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build`。尤其保留 `FreeformHelper.Tests.FreeformHelperViewModelTests.ExportDxfLayerImageCommand_WhenDxfMissing_ShowsWarningDialog`，包含其命令／狀態斷言。診斷採用問題時，可加上 `--filter "FullyQualifiedName~ExportDxfLayerImageCommand_WhenDxfMissing_ShowsWarningDialog"` 單獨執行。
4. 比較傳入的標題／訊息／按鈕文字、兩個 DXF 提示、`danger` 與圖示可見性、360 × 170 尺寸、縮放／啟動位置設定、確認 = true、取消 = false 及 OK 關閉。檢查 Embed DXF、重設 DXF 編輯、重設設定、未存檔專案與警告呼叫端。未存檔專案提示另須確認：不按按鈕直接關閉對話框仍回傳 null，應用程式保持開啟。這些對話框結果維持相同。
5. 使用凍結基準，在相同作業系統、字型、DPI 及佈景下擷取前後圖片。NFH UI 快照可能改變，包含圖示與對應色彩。採用 PR 附上兩組圖片並說明這些已知視覺差異；保留行為預期，不以更新預期來接受結果變更。

NFH 採用與產品圖片比對不在本次抽取範圍內。

## 已知差異

- 命名空間、版權標頭及 XML API 文件遵循 Core 慣例。Avalonia 11.3.12 → 12.1.1 無須調整對話框執行期 API；XAML 載入、`ShowDialog` 及 `Close` 維持原有行為。測試使用 xUnit v3 與既有 Core 無介面應用程式。
- 兩個寫死的 DXF 提示改為選用建構函式參數，預設 null。原本四個字串的建構函式呼叫及選用的強調參數仍保持原始碼相容。
- 取消字型圖示改為描邊 `Path`，使用 `NfcCloseIconGeometry`、24 × 24 方框與 2 單位圓頭描邊，取代粗體字型圖示。警告字型圖示改為外框 `Path`，使用 literal geometry `M10 1L19 18H1Z M10 6V11 M10 14V15`、20 × 20 方框、2 單位圓頭描邊及 `NfcWarningAccentStrongBrush`。未抽取圖示字型、`FontIcon` 或 `IconGlyphs`。
- Core 預設色盤與 NFH 不同。尤其 NFH 以固定的 `BrushWhite` 繪製取消圖示。Core 將描邊繫結到圖示繼承的 `TextElement.Foreground`，也就是按鈕的實際前景色，因此圖示在任何 danger 底色與按鈕狀態下都跟隨文字顏色。NFH 採用 Core 的色彩並檢視圖片。純量間距改用動態資源，取代 NFH 的靜態資源。
- 下列 NFH 值沒有相符型別／語意的 Core 鍵，保留完全相同的 literal。Core 有純量間距鍵，但沒有對應 inset 的 `Thickness` 資源。不新增佈景鍵。

| NFH token | 保留的 literal |
| --- | --- |
| `DialogWidth` | `360` |
| `DialogHeight` | `170` |
| `Inset16` | `16`（四邊 padding） |
| `InsetTop8` | `0,8,0,0` |
| `InsetTop14` | `0,14,0,0` |
| `Inset14_8` | `14,8` |
| `FontSizeLg` | `15` |
| `IconSizeLg` | `20`（警告向量方框） |
| `ConfirmDialogDangerIconSize` | `24`（取消向量方框） |
| `ConfirmDialogActionMinHeight` | `36` |
| `ConfirmDialogActionHeight` | `40` |

## 留在 NFH 的部分

DXF 用語、命令決策、設定／重設／匯出行為、ViewModel、應用程式佈景／基本控制項樣式、工具提示樣式、產品快照及採用工作留在 NFH。`CadPadDialog`、`RegularPadDialog`、所有其他檢視／控制項、圖示字型、`FontIcon` 及 `IconGlyphs` 均不屬於此模組。未變更來源儲存庫中的任何檔案。
