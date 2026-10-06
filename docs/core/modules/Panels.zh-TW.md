[English](Panels.md) | [中文](Panels.zh-TW.md)
# Panels

## 用途

`Nvt.Core.Avalonia.Panels` 包含從 NFH（FreeformHelper）抽出的兩個版面控制項：平面式可收合面板，以及具有具名內容區域的工作區。文字與內容由使用端提供，產品流程留在 NFH。

將 `avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml` 合併至使用端資源，並在樣式中加入：

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Panels/PanelsStyles.axaml" />
```

`ButtonStyles.axaml` 必須在 `PanelsStyles.axaml` 之前載入，讓面板標頭保留 stretch 內容對齊。

## 公開 API

`CollapsiblePanel` 繼承 `ContentControl`；繼承的 `Content` 是內文區域。

| 屬性 | 型別 | 預設值 |
| --- | --- | --- |
| `Title` | `string` | 空字串 |
| `IsExpanded` | `bool` | `true` |
| `DefaultExpanded` | `bool` | `true` |
| `IsCollapsible` | `bool` | `true` |
| `HeaderRight` | `object?` | `null` |

初始化時，未設定的 `IsExpanded` 會採用 `DefaultExpanded`。明確設定的值優先，包括明確設定為 `true`。初始化後變更 `DefaultExpanded` 不會改變展開狀態。關閉 `IsCollapsible` 會強制展開，並拒絕之後的收合嘗試。再次啟用時，面板維持展開，直到明確設定收合或操作標頭。停用的標頭與隱藏的箭頭沿用 NFH 不可收合時的行為。範本依序包含切換標頭與內文；標頭放置標題、右側內容及箭頭，不使用 `Expander` 或巢狀內文卡片。

`WorkspaceShell` 繼承 `TemplatedControl`。

| 屬性 | 型別 | 預設值／區域 |
| --- | --- | --- |
| `Title` | `string` | 空字串；主要標題 |
| `Subtitle` | `string` | 空字串；標題下方可換行文字 |
| `HeaderRight` | `object?` | `null`；標頭右上方 |
| `SummaryContent` | `object?` | `null`；標頭下方 |
| `ToolbarContent` | `object?` | `null`；摘要下方 |
| `LeftContent` | `object?` | `null`；主要區域左欄 |
| `RightContent` | `object?` | `null`；主要區域右欄 |
| `FooterContent` | `object?` | `null`；主要欄位下方 |
| `LeftColumnWidth` | `GridLength` | `2.2*` |
| `RightColumnWidth` | `GridLength` | `*` |

上述每個屬性都有名為 `<Property>Property` 的公開靜態 `StyledProperty` 欄位。兩個欄寬屬性繫結至主要格線的欄位定義，套用範本後仍會更新。預設值保留 NFH 原有比例，移除產品 token 名稱。

## 來源

凍結的上游儲存庫：`Dennis40816/nvt-freeform-helper`；ref：`1.3.x`；完整 commit：`e01e07a361b8dc264a06b3741f40274feeeace2d`。來源皆從該 commit 讀取，未從工作樹讀取。

| 抽出的來源路徑 | 抽出部分 |
| --- | --- |
| `src/FreeformHelper.UI/Controls/HidePanelBlock.axaml.cs` | 屬性預設值及初始化／變更行為，改名為 `CollapsiblePanel` |
| `src/FreeformHelper.UI/Controls/ReviewWorkspaceShell.cs` | 工作區屬性，改名為 `WorkspaceShell` |
| `src/FreeformHelper.UI/Styles/Controls.Panel.axaml` | 兩個範本及相關面板根部、標頭、箭頭與內文規則 |
| `src/FreeformHelper.UI/Styles/Controls.Core.axaml` | 僅 `workspaceTopLayerBox`、`workspaceMainTitle`，以及限定於面板標頭的切換按鈕 `SelectableTextBlock` 互動規則 |

`src/FreeformHelper.UI/Styles/Tokens.axaml` 提供替代值與欄寬預設值所依據的凍結數值。未抽出其他控制項或產品檢視。

## Token 對應

`PanelsStyles.axaml` 的每個資源參照都透過 `DynamicResource` 使用現有 `Nfc*` 鍵。`ThemeTokens.axaml` 提供共用色票。

| NFH token | Core token |
| --- | --- |
| `BrushBgSurfaceInset` | `NfcSurfaceSubtleBrush` |
| `BrushBgPanel` | `NfcSurfaceBrush` |
| `BrushBorder` | `NfcBorderBrush` |
| `BrushTextPrimary` | `NfcTextStrongBrush` |
| `BrushTextSubtle` | `NfcTextSecondaryBrush` |
| `BrushTextMuted` | `NfcTextMutedBrush` |
| `RadiusSm` | `NfcCompactCornerRadius` |
| `Space2` | `NfcSpace2` |
| `Space12` | `NfcSpace12` |
| `Space16` | `NfcSpace16` |
| `FontSizeSectionTitle` | `NfcFontSize16` |

## 驗證

沿用現有 `ThemeTestApplication` 執行無視窗測試，不修改測試應用程式。所有內容均為合成資料。`CollapsiblePanelTests` 固定所有預設值、未設定與明確設定的初始化、初始化前清除明確值、透過標頭鍵盤操作雙向切換、不可收合初始化的兩種設定順序、可收合與不可收合之間的轉換、內文可見性、標頭右側可見性，以及平面版面；也檢查向量箭頭方向。`WorkspaceShellTests` 檢查每個插槽的視覺區域、列順序、預設比例，以及自訂與即時更新欄寬。`PanelsStylesTests` 檢查兩種主題中的資源解析、即時主題／使用端覆寫，以及適用的來源版面限制。來源限制測試讀取 checkout 中的樣式檔；執行階段測試載入已編譯的樣式 include。

已搜尋凍結測試來源中的 `HidePanelBlock`、`ReviewWorkspaceShell`、`panelBlock*`、`workspaceTopLayerBox` 與 `workspaceMainTitle`，未找到專用測試或直接參照。從 `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs` 移植適用的限制：token 之外不使用行內十六進位色碼、同一樣式不重複設定同一屬性、不將捲動寬度繫結至 `Bounds.Width`，以及不使用預設 `Expander`。`UI/Smoke/WorkspaceViewsSmokeTests.cs` 與 `UI/Smoke/NotchExportSelectionWindowSmokeTests.cs` 的無視窗版面檢查改以合成插槽涵蓋，產品設定留在 NFH。產品來源雜湊與渲染快照基準不複製至 Core。

設定 `AVALONIA_TELEMETRY_OPTOUT=1` 後執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Panels"
```

### NFH 導入證據

NFH 導入不在本次抽出範圍內。導入 PR 依下列方式驗證展開／收合行為與內容位置零差異：

1. 保留預設欄寬：`LeftColumnWidth=2.2*` 與 `RightColumnWidth=*` 已重現 `NotchExportMainLeftColumnWidth` 及 `NotchExportMainRightColumnWidth`。
2. 採用 Core 的色彩。僅在應用程式範圍覆寫核准的 `NfcAccent*` 鍵；其他 Core 鍵均不可覆寫。NFH 只保留自己的情境樣式。
3. 在 NFH 測試專案執行既有 `UiLayoutGuardTests`、`HeadlessUiSmokeTests`、`WorkspaceViewsSmokeTests`、`NotchExportSelectionWindowSmokeTests`、`UiVisualSnapshotTests` 與 `UiRenderedVisualSnapshotTests`。相關檔案位於 `tests/FreeformHelper.Tests/UI/Snapshots/` 與 `UI/Smoke/`。導入後也重新執行等效的合成 Panels 行為測試。這些既有廣泛測試不能取代控制項狀態比較。
4. 比較未設定／明確設定的初始展開狀態、展開／收合標頭、鍵盤與滑鼠切換、停用／重新啟用收合、不可收合時嘗試收合，以及有內容／空白的標頭右側。比較所有工作區插槽、副標題換行、面板標題截斷、標頭懸停／按下／停用狀態，以及雙欄比例。前後使用相同作業系統、字型、DPI、主題、視窗大小與合成內容。
5. NFH UI 快照可能改變，包括箭頭、Core 色彩替代，以及共用標頭角色。深色模式下，Core 的摘要與工具列區塊比主區域亮，NFH 的則較暗。圖片必須呈現這一點。導入 PR 附上前後對照圖片，逐項審查視覺變更，僅為核准的變更更新 NFH 快照基準。展開與收合行為保持相同。

## 已知差異

- 以通用名稱取代 `HidePanelBlock` 與 `ReviewWorkspaceShell`。工作區唯一新增的屬性是兩個 `GridLength` 欄寬。
- NFH 使用圖示字型的 `ExpandMore` 與 `ExpandLess` 字元。Core 在相同的 20×20 容器中以向量 `Path` 繪製，收合時朝下、展開時朝上。置中於容器的固定 12×6 版面與 1.5 線寬取代 `IconSizeSm=12` 字型圖示；現有 token 沒有適用的圖示尺寸／幾何資源。不新增圖示屬性或圖示字型相依項目。
- 面板標頭使用 Core 的 `actionGhost` 共用角色，採 32 px 高度、膠囊圓角與 14,0 內距。移除局部背景／邊界／狀態／範本覆寫，只保留 stretch 內容、選取文字命中規則與展開／chevron 行為。panel root 不裁切外側焦點框；焦點、停用與 checked 均依共用規則。
- NFH 以全域規則對切換按鈕內所有 `SelectableTextBlock` 設定 `IsHitTestVisible=False` 與 `Focusable=False`。Core 只在面板標頭內套用這兩個設定，因此可選取的標頭內容不會擋住切換。
- 工作區內容仍沿用使用端字型；共用按鈕角色為面板標頭提供既有 Core 字型別名。
- Core 對應的筆刷值可能與 NFH 色彩不同。對應依據是語意，不代表像素完全相等。NFH 採用 Core 的色彩並審查導入圖片。
- 現有資源沒有合適型別／數值時，以常值保留凍結幾何。Core 間距鍵是 `double`；直接作為 `Thickness` 屬性的 `DynamicResource` 會造成型別轉換錯誤，因此厚度數值維持常值，不新增轉換器或 token。

| 沒有對應 Core 資源的 NFH token | 保留的常值／替代方式 |
| --- | --- |
| `BrushTransparent` | `Transparent` |
| `InsetLeft10` | `10,0,0,0` |
| `Inset14` | `14` |
| `Space14` | `14` |
| `Inset10` | `10` |
| `Inset8` | `8` |
| `Inset10_8` | `10,8` |
| `BorderThin` | `1` |
| `BorderTopThin` | `0,1,0,0` |
| `PanelBlockChevronHostSize` | `20` |
| `NotchExportMainLeftColumnWidth` | `LeftColumnWidth`，預設 `2.2*` |
| `NotchExportMainRightColumnWidth` | `RightColumnWidth`，預設 `*` |

NFH 使用 Avalonia 11.3.12 與 xUnit 2；Core 使用 Avalonia 12.1.1 與 xUnit v3。控制項程式碼不需要 API 調整。Core 無視窗按鍵呼叫傳入 Avalonia 12 要求的實體按鍵與按鍵字串。欄位版面斷言比較 presenter 範圍，因為此版本的 `ColumnDefinition.ActualWidth` 包含間距。其餘展開邏輯未變更，也未發現與任務摘要不同的行為。

## 留在 NFH 的內容

所有其他 NFH 控制項、圖示字型、`FontIcon`、`IconGlyphs`、notch 匯出檢視、產品 view model／流程、token 所有權，以及產品快照基準皆留在 NFH。導入、套件／執行環境對齊，以及前後對照圖片核准仍是 NFH 工作。本次抽出沒有未解決的實作問題。
