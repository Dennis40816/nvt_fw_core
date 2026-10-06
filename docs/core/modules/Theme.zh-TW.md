[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme（`Nvt.Core.Avalonia.Theme`）

Theme 保留 NVT FW Combiner（NFC）通用主題及八個舊有字型值。模組提供 `Theme/ThemeTokens.axaml` 與 `Theme/ButtonStyles.axaml`，另有 `Theme/ScrollStyles.axaml`，見[捲軸樣式](#捲軸樣式)。另提供 `UiResourceResolver`，供在程式碼中讀取主題資源的控制項使用，見[資源解析](#資源解析)。將資源字典合併至應用程式資源，並在主機原本的按鈕樣式作用範圍載入樣式：

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
```

為維持相容，既有資源鍵保留 `Nfc` 名稱。八個字型值另有 Core 名稱。模組保留 Light、Dark 各 51 個筆刷鍵、22 個共用圓角／圖示／間距／字型相容 token、語意按鈕範本，以及 78 個樣式區塊。通用角色包含 semantic、primary、secondary、danger、command、icon/copy、close、inline edit、breadcrumb、action/browse、file reveal、summary chip，以及支援減少動態效果的展開式 rail。值、範本繫結、選擇器分支、轉場與樣式順序皆維持原樣。

排除的 token 家族：`NfcKept`、`NfcReferenceInput`、`NfcControllerInput`、`NfcHex`、`NfcRequired`、`NfcMemory`、`NfcWorkflow`、`NfcWorkspace`、`NfcNav`、`NfcReport`。排除的選擇器分支：`settingsNavItem`、`messageCenterNavigationItem`、`activityFilter`、`sourceEditButton`、`version*`、`slotClearAction`、`outputRailAction`、`outputNameEdit`。混合選擇器只保留通用分支。這些產品資源與分支由 NFC 保留；整合時須維持原有樣式優先順序。

## NFC 舊有字型資源抽取

`Theme/NfcLegacyFontTokens.axaml` 保留下列凍結 commit 中 NFC `ThemeTokens.axaml` 第 218–225 行的八個字型資源。
字典只包含兩個字族清單及六個字級。字級單位為 Avalonia 裝置獨立像素。
`Theme/ThemeTokens.axaml` 合併此字典，並透過 `StaticResource` 別名提供原有八個相容鍵。

| Core 鍵 | NFC 相容鍵 | 凍結值 |
|---|---|---|
| `Nvt.Font.NfcLegacy.Ui.Family` | `NfcUiFontFamily` | `fonts:Inter#Inter, Microsoft JhengHei UI, Noto Sans CJK TC, Noto Sans TC, Segoe UI` |
| `Nvt.Font.NfcLegacy.Technical.Family` | `NfcTechnicalFontFamily` | `Cascadia Mono, Consolas` |
| `Nvt.Font.NfcLegacy.Size10` | `NfcFontSize10` | `10` |
| `Nvt.Font.NfcLegacy.Size11` | `NfcFontSize11` | `11` |
| `Nvt.Font.NfcLegacy.Size12` | `NfcFontSize12` | `12` |
| `Nvt.Font.NfcLegacy.Size13` | `NfcFontSize13` | `13` |
| `Nvt.Font.NfcLegacy.Size14` | `NfcFontSize14` | `14` |
| `Nvt.Font.NfcLegacy.Size16` | `NfcFontSize16` | `16` |

本次不新增字型、不套用角色表，也不改 fallback。
字族順序與既有 Inter 註冊方式皆維持原樣。
舊有資源只供既有 NFC 使用處相容使用。

若只採用字型抽取，NFC 須先合併 Core 字型字典，再在自己的資源字典中定義八個相容鍵。
每個別名使用上表對應；以下示範 size-14 相容鍵：

```xml
<ResourceDictionary.MergedDictionaries>
  <ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/NfcLegacyFontTokens.axaml" />
</ResourceDictionary.MergedDictionaries>
<StaticResource x:Key="NfcFontSize14" ResourceKey="Nvt.Font.NfcLegacy.Size14" />
```

NFC 須保留以下既有來源檔案的 `StaticResource NfcFontSize14` 用法：

- `src/NvtFwCombiner.Presentation.Avalonia/Resources/MainWindowSharedTemplates.axaml`
- `src/NvtFwCombiner.Presentation.Avalonia/Resources/SettingsEventBufferFormatPageTemplate.axaml`

NFC 也須保留：

- 既有 style 與 inline 值。
- 字級、字重及繼承關係。
- Static 與 DynamicResource 查找方式。
- 字型資產、Inter 套件版本及 fallback 順序。

## 凍結來源與驗證

凍結來源：NFC（`nvt_fw_combiner`），ref `origin/1.2.x`，完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。
即使遠端 ref 前進，基準仍固定在此完整 commit。
抽取的來源路徑：

- `src/NvtFwCombiner.Presentation.Avalonia/Styles/ThemeTokens.axaml`
- `src/NvtFwCombiner.Presentation.Avalonia/Styles/MainWindowButtonStyles.axaml`

通用測試斷言移植自同一 commit 的：

- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.ThemeTokens.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Buttons.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Build.cs`
- `tests/NvtFwCombiner.Architecture.Tests/PresentationBoundaryTests.ShellSurface.cs`（只移植字型斷言）

`tests/Nvt.Core.Avalonia.Tests/Theme/Baseline/*.xml` 凍結來源資源的通用子集。`ExtractedXamlMatchesFrozenBaseline` 先展開八個字型相容別名，再比對完整凍結 XML 樹。測試逐一鎖定資源鍵／值、選擇器、setter、範本繫結、轉場及順序。基準檔案未更動。行為特徵測試也檢查所有編譯後 token 值、兩種主題、一般按鈕幾何、primary／secondary／danger／action 狀態、滑鼠與鍵盤焦點差異、rail 展開及減少動態效果。測試保留 NFC 既有的 primary presenter 行為：較後的基本 setter 覆蓋按下／停用時的 presenter 配色，但內部文字仍隨狀態變化。同樣的 22 個執行階段特徵案例已對完整凍結 NFC XAML 的暫存副本通過驗證；副本未保留。

`NfcLegacyFontTests` 將每個 Core 字型值固定在 NFC 凍結值，並在 Light、Dark 檢查所有相容鍵。
測試也獨立載入編譯後字型字典。
編譯後的 `StaticResource` 與 `DynamicResource` 使用處，對英文、繁中及技術文字保留相同字族、字級、字重、換行與邊界。
這些 headless 檢查不能證明實際桌面畫面相同。

使用已還原的套件驗證 Core：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore --disable-build-servers
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

採用時的零差異驗證：先在凍結來源執行 NFC 的 UI smoke 與架構測試，切換至 Core 後再執行。
調整來源文字檢查，使其追蹤資源載入與別名，但保留每個既有預期值。
須包含以下既有檢查：

- `XamlControlStyleContractTests`：主題鍵、字型值、按鈕狀態、邊框與減少動態效果。
- `PresentationBoundaryTests`：shell 字族與字級合約。
- `MemorySourcePresentationTests`：既有 static size-14 使用處、英文／繁中及展開互動。
- `ReportChangesLayoutTests` 與 `HomeWorkflowCardVerticalAlignmentDiagnosticTests`：技術與 UI 字族使用處。
- `NavigationFocusIndicatorTests`、`NavigationCheckedStateTests` 及既有 modal 鍵盤巡覽測試。

透過 Core 與 NFC 鍵比對解析後的字族清單及六個字級。
也須比對筆刷值、按鈕與 presenter 幾何、Tab 順序、rail 展開及減少動態效果行為。
比對採用前後的實際截圖、文字換行、文字邊界、控制項尺寸及互動。
檢查英文／繁中、Light／Dark，以及受影響的滑過、按下、停用與鍵盤焦點狀態。
每次比較須使用相同 OS、已安裝字型版本、字型資產、DPI、視窗大小、主題及減少動態效果設定。
桌面截圖須逐像素比對，並保留原始基準。
先依 NFC 既有流程還原與建置，再執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build
dotnet test tests/NvtFwCombiner.Architecture.Tests/NvtFwCombiner.Architecture.Tests.csproj --no-build
```

目前尚無採用工具。NFC 整合、完整產品測試及桌面截圖比對仍屬後續工作；本次未更動套件或共用設定。

## 捲軸樣式

`Theme/ScrollStyles.axaml` 把 NFC 的捲軸外觀定為 Core 共用外觀，另加入 NFH 的兩個選用 class，讓內容寬度不超過可視寬度。
這些樣式修改 Fluent `ScrollBar` 範本內的元件，所以載入它的工具必須使用 Fluent 主題。
NFC 在 `085f71c` 的 `src/NvtFwCombiner.Presentation.Avalonia/App.axaml` 載入 Fluent。NFH 在 `847cc45` 的 `src/FreeformHelper.UI/App.axaml` 載入 Fluent。
請在工具原本捲軸樣式所在的位置載入，維持樣式順序不變：

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
```

樣式內容：

- 所有 `ScrollViewer`，以及透過附加屬性 `ScrollViewer.AllowAutoHide` 的所有範本控制項，捲軸一律不自動隱藏。
- 垂直捲軸寬 14 px，水平捲軸高 14 px，軌道透明。
- thumb 粗 6 px、置中、膠囊形，使用 `NfcTextDisabledBrush`；指標停在捲軸或 thumb 上時改為 `NfcTextMutedBrush`。
- 軌道矩形與上下按鈕完全透明。上下按鈕仍佔空間。
- `ScrollViewer.viewportBoundScroll` 讓內容延展並移除內距。其中的 `Border.viewportBoundContent` 取可視寬度，垂直捲軸不會蓋住或截斷內容。兩者搭配 `HorizontalScrollBarVisibility="Disabled"` 使用。

哪些規則需要 Fluent 範本：

- thumb、軌道矩形、上下按鈕與 thumb 內 Border 的規則選取 Fluent 範本元件（`/template/ Thumb`、`/template/ Rectangle#TrackRect`、`/template/ RepeatButton` 與 thumb 的 `Border`）。換成其他主題時，這些規則不會套用到任何元件。
- 捲軸尺寸、捲軸背景與自動隱藏的規則直接設定 `ScrollBar` 與 `ScrollViewer` 本身的屬性，任何主題都會套用。
- viewport-bound class 綁定 `ScrollViewer.Viewport`。它替垂直捲軸留下的 14 px 空間來自 Fluent `ScrollViewer` 範本。

### 捲軸樣式的凍結來源與驗證

檔案由兩個凍結來源組成。捲軸外觀來自 NFC，只有兩個 viewport-bound class 來自 NFH。
從各來源取用的行，每個選擇器、setter、值與順序都不變：

- NFC（`nvt_fw_combiner`），ref `origin/1.2.x`，完整 commit `085f71cfaf9d1f592759d1c58b5bdc4f7b572902`，`src/NvtFwCombiner.Presentation.Avalonia/Styles/MainWindowControlStyles.axaml` 第 117-185 行。第 186 行的 `ScrollViewer#SupportMatrixScroll` 屬於 NFC 產品，留在 NFC。
- NFH（`nvt-freeform-helper`），ref `origin/1.3.x`，完整 commit `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`，`src/FreeformHelper.UI/Styles/Controls.Scroll.axaml` 第 12-19 行（只取兩個 viewport-bound class）。

NFH 自己的捲軸規則（同一檔第 8-94 行）沒有移植。Core 採用 NFC 的外觀，工具採用 Core 的顏色，依[慣例](../conventions.md)的規定。

驗證：

- `ExtractedXamlMatchesFrozenBaseline` 比對此檔與 `Baseline/ScrollStyles.xml`，baseline 由同樣的來源行產生。
- `ScrollStylesTests` 檢查：每個資源鍵在兩個主題都存在、viewport-bound class 排在最後、兩個方向的捲軸尺寸與自動隱藏 setter，以及 scroll viewer 與範本控制項的自動隱藏。
- `ScrollStylesTests` 也在 Fluent 範本下檢查兩個方向的淺色與深色：14 px 軌道、置中的 6 px thumb、隱藏的軌道與上下按鈕、指標停留時的筆刷、拖曳 thumb，以及點擊隱藏軌道翻頁。`ListBox` 內部的 scroll viewer 捲軸也不自動隱藏。它檢查 viewport-bound 內容取可視寬度，並檢查垂直捲軸每次出現或消失時，寬度在同一次排版內穩定，不會來回震盪。
- 測試專案為這些檢查參考 `Avalonia.Themes.Fluent` 12.1.1。只有捲軸測試在自己的視窗載入 `FluentTheme`。套件不增加任何相依。
- NFC 規則的同樣 7 個執行期案例，對完整 NFC 凍結檔的暫存副本也都通過。副本拿掉了 3 個選取 NFC view 型別的樣式，因為 Core 無法編譯它們。副本未保留。

零差異採用：

- NFC 把第 117-185 行換成上面的 include，位置不變。NFC 必須逐像素相同：執行 UI smoke 測試，並以相同的作業系統、字型、DPI 與主題比對採用前後的桌面截圖。
- NFH 的外觀與行為會改變。NFH 的採用 PR 須附前後截圖，由 owner 核准外觀改變。非 UI 測試的清單與結果須相同。
- NFH 保留 `scrollV2`、`scrollDevCandidate`、`workspaceDataList`、`workspaceGroupStripScroll` 與 `DevScrollPreviewHeight`。

NFH 採用時會改變的地方：

| 項目 | NFH 現況（`847cc45`） | Core |
|---|---|---|
| 捲軸粗細 | 2.5 px（`ScrollBarThickness`） | 14 px |
| 捲軸背景 | `BrushScrollTrack` | 透明 |
| thumb | 填滿捲軸，平時、停留、按下都用 `BrushScrollThumb`，`RadiusPill` | 6 px、置中，`NfcTextDisabledBrush`，停留時 `NfcTextMutedBrush`，`NfcPillCornerRadius` |
| thumb 最小長度 | 36（`ScrollBarThumbMinLength`） | 未設定 |
| 上下按鈕 | `IsVisible=False`，不佔空間 | `Opacity=0`，仍佔空間 |
| 軌道矩形 | 未更動 | 隱藏 |
| 按下（pressed）規則 | 有 | 無 |

目前尚無採用工具。

## 資源解析

`UiResourceResolver` 為在程式碼中繪製的控制項讀取一個主題資源。每個方法都接收擁有者控制項、資源鍵與後備值。

| 方法 | 接受的資源型別 |
|---|---|
| `GetBrush` | 任何 `IBrush`，或交給呼叫端筆刷工廠的 `Color` |
| `GetColor`、`TryGetColor` | `Color`，或 `SolidColorBrush` 的顏色 |
| `GetDouble` | `double`、`float` 或 `int` |
| `GetCornerRadius` | `CornerRadius` |
| `GetThickness` | `Thickness` |

查找分三步：

1. 以擁有者實際的主題變體，搜尋擁有者及其樣式父層。不在任何樹上的控制項變體為 null，查找改用 `ThemeVariant.Default`。
2. 若找不到且 `UiThread.IsCurrent` 成功，以同一變體搜尋目前的應用程式。
3. 否則傳回後備值。

資源型別不符時也傳回後備值。使用 Default 變體時，只存在於 Light 與 Dark 字典的鍵查不到。
請在 UI 執行緒呼叫。解析器不快取值，也不監看主題變更。快取解析結果的控制項，須在主題變體改變時自行更新。
啟動時以 `UiThread.RegisterRunningDispatcher` 註冊 UI dispatcher。未註冊時會略過第 2 步。

### 解析器的凍結來源與驗證

凍結來源：NFH（`Dennis40816/nvt-freeform-helper`），ref `origin/1.3.x`，完整 commit `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`。
抽取的來源路徑：`src/FreeformHelper.UI/Services/UiResourceResolver.cs`。
Core 版保留 NFH 的簽章與行為，只改命名空間、`UiThread` 的 using、可見度（public）與文件註解。

`tests/Nvt.Core.Avalonia.Tests/Theme/FrozenNfhUiResourceResolver.cs` 保留 NFH 檔案的凍結副本，只有命名空間、類別名稱與 `UiThread` 的 using 不同。Core 的 `UiThread` 是 NFH 版本未經修改的移植。
`UiResourceResolverTests` 的 7 個案例都對兩個版本各跑一次，結果必須相同：

- 視窗內控制項在 Light 與 Dark 下，視窗資源鍵與應用程式資源鍵的結果。
- 筆刷資源、由顏色建立的筆刷，以及工廠呼叫次數。
- 顏色只來自 `Color` 與 `SolidColorBrush`；`ImmutableSolidColorBrush` 傳回後備值。
- 數值轉換，以及其他型別傳回後備值。
- 圓角與邊距的型別。
- 不在樹上的控制項：變體為 null，找得到應用程式頂層鍵，找不到 Light 與 Dark 的鍵。
- 未註冊 dispatcher：略過應用程式那一步。

此測試類別在不平行執行的集合中執行，因為其中一個案例會清除共用的 dispatcher 註冊。

NFH 零差異採用：

- NFH 刪除自己的副本前，Core 測試對兩個版本都通過。
- NFH 完整測試清單與結果和凍結來源相同。
- NFH 的 `ui-visual-minimal-baseline.json` 雜湊與 notch golden 輸出不變。
- NFH 的所有呼叫端共用同一份 dispatcher 註冊：把 NFH 的 `UiThread` 呼叫改成 Core 的 `UiThread`，或在兩份並存期間兩邊都註冊。NFH 約有 20 處讀自己的 `UiThread`，只註冊 Core 的會讓它們失效；只註冊 NFH 的則會略過第 2 步且不報錯。

目前尚無採用工具，NFH 採用仍待進行。
