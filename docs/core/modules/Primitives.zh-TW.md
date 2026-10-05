[English](Primitives.md) | [中文](Primitives.zh-TW.md)

# Primitives

## 用途與公開 API

這三個小型控制項位於 `src/Nvt.Core.Avalonia/Primitives/`，命名空間為 `Nvt.Core.Avalonia.Primitives`。本模組只包含排列、區段框架與資訊標籤。

| 控制項 | 公開屬性與預設值 | 行為 |
|---|---|---|
| `BalancedWrapPanel`（sealed `Panel`） | `ItemSpacing = 0`、`LineSpacing = 0`、`BalanceLastRow = true` | 以無限空間量測每個子項目，依可見子項目的期望尺寸換行；若最後一列只有一個子項目，而且容得下前一列的最後一個子項目，便將該子項目移至最後一列的最前方。只處理最後兩列。 |
| `SectionFrame`（`ContentControl`） | `Title = string.Empty`；繼承的 `Content` | 標題使用可選取文字，兩側有分隔線，下方為水平延展的內容呈現器。標題與內容持續繫結至各自屬性。 |
| `InfoLabel`（sealed `UserControl`） | `Text = string.Empty`、`Tip = string.Empty`、`TipTargetClass = null` | 使用可選取且自動換行的文字，工具提示設於標籤本身。設定目標類別後，也會將工具提示套用至具有該類別的最近視覺祖先。 |

每個屬性都有對應的 styled property 欄位。`SectionFrame` 保留來源的繼承內容行為。`InfoLabel` 在附加至視覺樹及 `Tip` 變更時套用非空白提示。方法會略過空字串或純空白提示，但標籤本身的工具提示會和來源一樣，透過 XAML 繫結跟隨 `Tip`。`TipTargetClass` 為 null 或空字串時，只套用至標籤。搜尋從視覺父項目開始，跳過不符合的祖先，並在第一個符合類別的控制項停止。單獨變更 `TipTargetClass` 不會套用提示；移除控制項或變更目標，也不會清除先前祖先已套用的提示。

將 `Theme/ThemeTokens.axaml` 載入應用程式資源，並加入預設樣式：

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Primitives/PrimitivesStyles.axaml"/>
```

`BalancedWrapPanel` 不需要預設樣式。文字、內容與產品專用類別由使用端提供。

## 來源

凍結的父專案基準：儲存庫 `Dennis40816/nvt-freeform-helper`、ref `1.3.x`、完整 commit `e01e07a361b8dc264a06b3741f40274feeeace2d`。來源使用 Avalonia 11.3.12 與 xUnit 2；Core 使用 Avalonia 12.0.5 與 xUnit v3。

從該 commit 擷取的檔案路徑：

- `src/FreeformHelper.UI/Controls/BalancedWrapPanel.cs`
- `src/FreeformHelper.UI/Controls/PadInfoSectionFrame.cs`
- `src/FreeformHelper.UI/Controls/SettingsInfoLabel.axaml`
- `src/FreeformHelper.UI/Controls/SettingsInfoLabel.axaml.cs`
- `src/FreeformHelper.UI/Styles/Controls.PadInfo.axaml` — 僅擷取 `PadInfoSectionFrame` 範本及其分隔線、標題規則。
- `src/FreeformHelper.UI/Styles/Controls.Settings.axaml` — 僅擷取標籤對齊及 `settingsInfoLabelText` 規則。

讀取 `src/FreeformHelper.UI/Styles/Tokens.axaml` 是為了確認凍結的 token 值，並未匯入其資源。`BalancedWrapPanel` 的量測、排列、列建構與平衡演算法完全不變。另兩個控制項改用通用名稱、命名空間與內部樣式類別。固定祖先類別改為本任務要求的 `TipTargetClass` 屬性。

## Token 對應

擷取的範本與標籤規則使用的所有來源 token 均列於下表。Core 的資源參照全部使用 `DynamicResource`，且僅參照 `Theme/ThemeTokens.axaml` 的既有 key；沒有新增 key。

| NFH token | Core 資源或字面值 | 用途 |
|---|---|---|
| `Space8` | `NfcSpace8` | 區段 StackPanel 間距及標題 Grid 欄間距，預設皆為 8。 |
| `Space1` | 字面值 `1` | 分隔線高度；既有 Nfc key 沒有值為 1 的對應項目。 |
| `BrushWhite` | `NfcSurfaceBrush` | 分隔線背景；最接近的既有亮色表面筆刷。 |
| `BrushTextPrimary` | `NfcTextStrongBrush` | 區段標題與標籤前景。 |
| `BrushPadInfoPanelBackground` | `NfcSurfaceBrush` | 區段標題背景；最接近的既有表面語意。 |
| `Inset8_4` | 字面值 `8,4` | 標題內距；Nfc 間距的 double 資源沒有相符的 `Thickness` token。 |

## 驗證

`tests/Nvt.Core.Avalonia.Tests/Primitives/` 的測試重用既有 `ThemeTestApplication`，涵蓋屬性預設值、兩種佈景的編譯樣式、動態資源、標題／內容／文字繫結、可選取且換行的文字，以及工具提示套用時機、最近祖先搜尋、缺少或停用目標、空白提示與重新附加。

在凍結 commit 以 `git grep` 搜尋三個控制項名稱，只找到 `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs`：`CriticalScrollContainers_UseViewportBoundedWidth` 檢查產品徽章區的面板使用、`BalanceLastRow` 樣式 setter，以及產品工具列沒有使用此換行面板。可移植的樣式屬性契約由 `BalanceLastRowCanBeSetFromStyles` 執行驗證；產品視圖與類別斷言留在 NFH。基準沒有這三個控制項的既有直接行為測試。

矩形預期值是在執行 Core 測試前，依凍結演算法推導得出。下表矩形格式為 `(x, y, width, height)`，順序與原始子項目相同。除另有註明外，項目間距為 5、列間距為 7，且啟用平衡。期望尺寸是面板量測所得的尺寸；子項目尺寸皆為合成資料。

| 情境 | 可用寬度；子項目尺寸 | 固定預期矩形；期望尺寸 |
|---|---|---|
| 單列 | 100；`20x10, 30x20, 10x15` | `(0,0,20,10), (25,0,30,20), (60,0,10,15)`；`70x20` |
| 多列，寬度恰好符合 | 65；`30x10, 30x20, 30x15, 30x25, 30x12, 30x18` | `(0,0,30,10), (35,0,30,20), (0,27,30,15), (35,27,30,25), (0,59,30,12), (35,59,30,18)`；`65x77` |
| 最後一列只有一項，啟用平衡 | 70；`20x10, 20x12, 20x30, 20x15` | `(0,0,20,10), (25,0,20,12), (0,19,20,30), (25,19,20,15)`；`45x49` |
| 相同輸入，停用平衡 | 70；相同子項目 | `(0,0,20,10), (25,0,20,12), (50,0,20,30), (0,37,20,15)`；`70x52` |
| 只平衡最後兩列 | 70；七個 `20x10` 子項目 | `(0,0,20,10), (25,0,20,10), (50,0,20,10), (0,17,20,10), (25,17,20,10), (0,34,20,10), (25,34,20,10)`；`70x44` |
| 欲移動的子項目放不下 | 60；`20x10, 30x20, 60x15` | `(0,0,20,10), (25,0,30,20), (0,27,60,15)`；`60x42` |
| 前一列只有一項 | 50；`40x10, 40x20`；列間距 3 | `(0,0,40,10), (0,13,40,20)`；`40x33` |
| 零尺寸與收合子項目 | 100；`20x10, 0x0, collapsed 100x100, 30x15` | `(0,0,20,10), (25,0,0,0), (0,0,0,0), (30,0,30,15)`；`60x15` |
| 可用寬度無限 | Infinity；`20x10, 30x20, 40x5, 10x15`；排列寬度 200 | `(0,0,20,10), (25,0,30,20), (60,0,40,5), (105,0,10,15)`；`115x20` |
| 排列與量測寬度不同 | 量測 100、排列 65；三個 `30x10` 子項目 | `(0,0,30,10), (0,17,30,10), (35,17,30,10)`；量測尺寸 `100x10` |

例如四個子項目的平衡情境，先形成 `[1,2,3]` 與 `[4]` 兩列。移動子項目 3 後，列高度變成 12 與 30，因此第二列從 `12 + 7 = 19` 開始，期望高度為 `12 + 7 + 30 = 49`。停用平衡時，列高度保持 30 與 15，因此第二列從 37 開始，期望高度為 52。可見的零尺寸子項目仍計入間距；收合子項目則排除。空面板與只有收合子項目的面板量測為 `0x0`。測試也固定驗證任一排列屬性變更時會使量測失效。

還原套件後執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Primitives"
```

### NFH 採用證據

採用屬於另一個 NFH 變更。將 `InfoLabel.TipTargetClass` 設為 `settingsFieldTile`，並在使用端資源整合中，依上表將 NFH token 對應至 Nfc key。保留原始產品類別與使用端。採用前後都執行 `UiLayoutGuardTests`，特別是 `CriticalScrollContainers_UseViewportBoundedWidth`、`CriticalDynamicTextBindings_UseWrapOrTrimmingContract`、`HoverAffordances_KeepContrastTooltipsAndInputBorderScope` 與兩個 `PadInfoPopover` guard：

```powershell
dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.UiLayoutGuardTests"
```

每次測試前先建置 NFH 測試專案。只更新採用所需的來源／型別參照，保留產品斷言。對凍結的 NFH 與 Core 使用相同合成子項目輸入，逐項比較上表的矩形與期望尺寸。也在相同視窗檢視區寬度下比較實際徽章子項目矩形，並在相同作業系統、字型、DPI 與佈景下比較區段標題／分隔線／內容邊界，以及標籤文字邊界。直接比較屬性值、文字／內容繫結更新，以及標籤／祖先工具提示值與生命週期。非 UI 行為維持相同。

NFH UI 快照可能因文件記錄的資源對應與 Avalonia 版本而改變。採用 PR 須附上兩種佈景的前後圖片並指出這些差異；Core 測試本身不能證明 NFH 視覺採用已通過。

## 已知差異

- `PadInfoSectionFrame` 改名為 `SectionFrame`；`SettingsInfoLabel` 改名為 `InfoLabel`。內部 settings／pad-info 樣式類別改為通用名稱，選擇器範圍限於這些控制項。
- 祖先工具提示傳遞改為透過 `TipTargetClass` 明確啟用，預設 null。NFH 設定 `settingsFieldTile` 即可恢復原始類別搜尋。
- NFH 色彩對應至 Core 的既有色盤。`BrushWhite` 與半透明 `BrushPadInfoPanelBackground` 都對應至 `NfcSurfaceBrush`，因此深色分隔線色彩與標題背景透明度會改變。單一共用資源 key 無法同時保留來源的兩個不同筆刷。主要文字色彩也不同。
- 分隔線高度 `1` 與標題內距 `8,4` 因沒有相符的 Nfc key，保留凍結值作為字面值。來源的靜態間距查找改為動態查找 `NfcSpace8`。
- 程式針對 Avalonia 12.0.5 編譯，而非 11.3.12；不需要其他控制項 API 調整。Headless 測試使用 xUnit v3，而非 xUnit 2。產品層級的呈現相容性仍須 NFH 採用證據。

## 留在 NFH 的內容與待確認事項

產品名稱、settings 類別、pad-info 用語、徽章／工具列樣式、視圖、ViewModel 與 UI 快照留在 NFH。其他所有控制項，包括 `FontIcon`、`LoadingSpinner`、`HidePanelBlock`、`ReviewWorkspaceShell`、`NumberScrubber`、`WorkspaceHeader` 與對話框，都不屬於本模組。此任務沒有在 NFH 執行採用。

沒有尚待確認的實作問題。NFH 的 Avalonia 升級相容性，以及採用前後圖片，仍屬產品層級驗證工作。
