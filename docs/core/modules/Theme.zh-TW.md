[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme（`Nvt.Core.Avalonia.Theme`）

Theme 保留 NVT FW Combiner（NFC）通用主題及八個舊有字型值。模組提供 `Theme/ThemeTokens.axaml`、`Theme/ButtonStyles.axaml` 與 `Theme/ActionRoleStyles.axaml`。另提供 `UiResourceResolver`，供在程式碼中讀取主題資源的控制項使用，見[資源解析](#資源解析)。將資源字典合併至應用程式資源，並在主機原本的按鈕樣式作用範圍載入樣式：

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

## 動作角色樣式

`Theme/ActionRoleStyles.axaml` 定義 NFC、NFH、NFU 共用的按鈕角色。外觀與行為由 Core 定義一份，每個工具都使用它。工具不在本地覆寫這些樣式。工具需要變化時，由 Core 新增角色。
角色來自 NFH 的通用角色類別、範本、狀態及配置。色彩對應至 Core 既有的 Light／Dark 色盤，因此這是 token 對應移植，預期會有可見差異。Core 共用色盤另案提案。owner 核准後，由 Core 更換對應的鍵，各工具不必改動。
採用工具須將 `ThemeTokens.axaml` 合併至資源，並在樣式中載入 `ActionRoleStyles.axaml`：

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ActionRoleStyles.axaml" />
```

使用這兩個檔案，不載入 `ButtonStyles.axaml`，因為後者設定全域 `Button` theme。每個 `Button` 或 `ToggleButton` 組合一個配置 primitive（`actionTextButton`、`actionIconButton`、`actionChip`）及一個色彩角色（`actionNeutral`、`actionPrimary`、`actionDanger`、`actionGhost`），或使用 `actionChip chipAction`。`chipAction.active` 表示選取。被動徽章使用 `Border.chipStatus`，可加上 `.warning`、`.danger` 或 `.success`。

保留來源順序。按鈕與切換按鈕涵蓋一般、`:pointerover`、`:pressed`、`:disabled`；切換按鈕另涵蓋 `:checked`、`:checked:pointerover`、`:checked:pressed`。配置 primitive 保留 Border 加上未命名 ContentPresenter 的範本、內容置中及裁切。文字／chip 內容保留不換行、單行及字元省略。Status chip 為被動控制項：修飾類別只改邊框／文字色，沒有滑過、按下或勾選規則。

### 動作角色凍結來源

來源 repository：`Dennis40816/nvt-freeform-helper`，ref `origin/1.3.x`，完整 commit `6fe3c269ac86d7153f57466b56f959f36e86b9c4`。即使 ref 日後前進，來源仍固定在此完整 SHA。讀取的來源路徑：

- `src/FreeformHelper.UI/Styles/Controls.Action.axaml`
- `src/FreeformHelper.UI/Styles/Controls.Tab.axaml`（無保留角色類別的額外規則）
- `src/FreeformHelper.UI/Styles/Tokens.axaml`
- `docs/guides/ui-action-role-system.md`
- `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs`（通用範本／配置／狀態與 4.5:1 對比檢查）

| 角色／區塊 | `6fe3c269` 的 `Controls.Action.axaml` 來源行範圍 |
|---|---|
| `actionTextButton` 配置 | 5–13（移除 `actionButton` 分支） |
| `actionIconButton` 配置與範本 | 15–65（只保留四個色彩角色的分支） |
| `actionChip` 配置 | 67–77 |
| 文字／chip 範本與文字規則 | 79–125（移除 `actionButton` 分支） |
| `actionNeutral`，含文字覆寫 | 144–210 |
| `actionPrimary`，含文字覆寫 | 212–260 |
| `actionDanger`，含文字覆寫 | 262–309 |
| `actionGhost`，含文字覆寫 | 312–359 |
| 圖示色彩角色覆寫 | 394–459 |
| `chipAction` 與 `.active` | 615–650 |
| `ToggleButton.chipAction` 勾選狀態 | 697–701 |
| `Border.chipStatus` 與修飾類別／文字規則 | 704–744 |

上述範圍內的來源焦點配色 setter 已移除，改成下述鍵盤焦點外圈。產品分支及 `controls|FontIcon` 規則不移植。

### 動作角色色彩對應

此表列出保留規則參考的每個色彩 token、兩個移除的焦點專用 token，以及來源通用停用對比檢查的 token。多個來源 token 可以共用一個 Core 鍵；同一來源 token 可依 Core 既有狀態配對，在一般、滑過、按下、停用時使用不同 Core 鍵。

| NFH 筆刷／色彩 token | Core 鍵或常值 |
|---|---|
| `BrushActionChipBackground` / `ColorActionChipBackground` | `NfcSurfaceSubtleBrush` |
| `BrushActionChipBorder` / `ColorActionChipBorder` | `NfcBorderBrush` |
| `BrushActionChipForeground` / `ColorActionChipForeground` | `NfcTextBrush`（一般）；`NfcAccentStrongBrush`（滑過／按下）；`NfcTextDisabledBrush`（停用） |
| `BrushActionChipHover` / `ColorActionChipHover` | `NfcAccentSurfaceBrush` |
| `BrushActionChipPressed` / `ColorActionChipPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushActionConsoleBackground` / `ColorActionConsoleBackground` | `Transparent` literal |
| `BrushActionConsoleCheckedBackground` / `ColorActionConsoleCheckedBackground` | `NfcSelectionSurfaceBrush` |
| `BrushActionConsoleForeground` / `ColorActionConsoleForeground` | `NfcTextBrush` |
| `BrushActionDangerBackground` / `ColorActionDangerBackground` | `NfcDangerSurfaceBrush` |
| `BrushActionDangerBorder` / `ColorActionDangerBorder` | `NfcDangerBorderBrush` |
| `BrushActionDangerForeground` / `ColorActionDangerForeground` | `NfcDangerTextBrush` |
| `BrushActionDangerHover` / `ColorActionDangerHover` | `NfcDangerSurfaceMutedBrush` |
| `BrushActionDangerPressed` / `ColorActionDangerPressed` | `NfcCriticalSurfaceBrush` |
| `BrushActionFocusBackground` / `ColorActionFocusBackground` | 移除焦點配色 setter，無保留的參考 |
| `BrushActionFocusBorder` / `ColorActionFocusBorder` | 移除焦點配色 setter，無保留的參考 |
| `BrushActionFocusForeground` / `ColorActionFocusForeground` | `NfcAccentStrongBrush` |
| `BrushActionGhostBackground` / `ColorActionGhostBackground` | `Transparent` literal |
| `BrushActionGhostBorder` / `ColorActionGhostBorder` | `Transparent` literal |
| `BrushActionGhostForeground` / `ColorActionGhostForeground` | `NfcTextSecondaryBrush` |
| `BrushActionGhostHover` / `ColorActionGhostHover` | `NfcAccentSurfaceBrush` |
| `BrushActionGhostPressed` / `ColorActionGhostPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushActionInverseBackground` / `ColorActionInverseBackground` | `NfcAccentSurfaceBrush` |
| `BrushActionInverseForeground` / `ColorActionInverseForeground` | `NfcAccentStrongBrush` |
| `BrushActionInversePressedBackground` / `ColorActionInversePressedBackground` | `NfcSecondaryActionPressedBrush` |
| `BrushActionNeutralBackground` / `ColorActionNeutralBackground` | `NfcSurfaceBrush` |
| `BrushActionNeutralBorder` / `ColorActionNeutralBorder` | `NfcBorderBrush` |
| `BrushActionNeutralForeground` / `ColorActionNeutralForeground` | `NfcTextBrush`（一般）；`NfcAccentStrongBrush`（滑過／按下） |
| `BrushActionNeutralHover` / `ColorActionNeutralHover` | `NfcAccentSurfaceBrush` |
| `BrushActionNeutralPressed` / `ColorActionNeutralPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushActionPrimaryBackground` / `ColorActionPrimaryBackground` | `NfcAccentSurfaceBrush` |
| `BrushActionPrimaryBorder` / `ColorActionPrimaryBorder` | `NfcAccentBorderLightBrush` |
| `BrushActionPrimaryForeground` / `ColorActionPrimaryForeground` | `NfcAccentStrongBrush` |
| `BrushActionPrimaryHover` / `ColorActionPrimaryHover` | `NfcAccentSurfaceSubtleBrush` |
| `BrushActionPrimaryPressed` / `ColorActionPrimaryPressed` | `NfcAccentSurfaceBrush` |
| `BrushActionSelectedBackground` / `ColorActionSelectedBackground` | `NfcSelectionSurfaceBrush` |
| `BrushActionSelectedBorder` / `ColorActionSelectedBorder` | `NfcAccentBorderBrush` |
| `BrushActionSelectedForeground` / `ColorActionSelectedForeground` | `NfcTextStrongBrush` |
| `BrushActionTextHoverBackground` / `ColorActionTextHoverBackground` | `NfcAccentSurfaceBrush` |
| `BrushActionTextHoverBorder` / `ColorActionTextHoverBorder` | `NfcAccentBorderBrush`（滑過）；`NfcAccentBorderStrongBrush`（按下） |
| `BrushActionTextHoverForeground` / `ColorActionTextHoverForeground` | `NfcAccentStrongBrush` |
| `BrushActionTextPressedBackground` / `ColorActionTextPressedBackground` | `NfcSecondaryActionPressedBrush` |
| `BrushBorderStrong` / `ColorBorderStrong` | `NfcAccentBorderBrush`（滑過）；`NfcAccentBorderStrongBrush`（按下） |
| `BrushButtonDisabledForeground` / `ColorButtonDisabledForeground` | `NfcTextDisabledBrush`（只用於 NFH 通用對比檢查） |
| `BrushButtonNeutralBackground` / `ColorButtonNeutralBackground` | `NfcSurfaceBrush` |
| `BrushButtonNeutralBorder` / `ColorButtonNeutralBorder` | `NfcBorderBrush`（一般）；`NfcAccentBorderBrush`（滑過）；`NfcAccentBorderStrongBrush`（按下） |
| `BrushButtonNeutralForeground` / `ColorButtonNeutralForeground` | `NfcTextBrush`（一般）；`NfcAccentStrongBrush`（滑過／按下）；`NfcTextDisabledBrush`（danger 停用 setter） |
| `BrushButtonNeutralHover` / `ColorButtonNeutralHover` | `NfcAccentSurfaceBrush` |
| `BrushButtonNeutralPressed` / `ColorButtonNeutralPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushDanger` / `ColorDanger` | `NfcDangerTextBrush` |
| `BrushStatusChipBackground` / `ColorStatusChipBackground` | `NfcSurfaceSubtleBrush` |
| `BrushStatusChipBorder` / `ColorStatusChipBorder` | `NfcBorderBrush` |
| `BrushStatusChipForeground` / `ColorStatusChipForeground` | `NfcTextBrush` |
| `BrushSuccess` / `ColorSuccess` | `NfcSuccessTextBrush` |
| `BrushTextMuted` / `ColorTextMuted` | `NfcTextDisabledBrush` |
| `BrushTransparent` | `Transparent` literal |
| `BrushWarning` / `ColorWarning` | `NfcWarningTextBrush` |

保留的 NFH 色彩鍵皆有對應。`BrushActionFocusBackground` 與 `BrushActionFocusBorder` 隨焦點配色 setter 一併移除。`BrushActionFocusForeground` 仍用於 ghost 滑過／按下規則。四個透明來源鍵使用 `Transparent` 常值；除了指定的焦點資源，不新增色彩或尺寸 token。

### 動作角色焦點外圈

每個配置 primitive 及色彩角色都將 `FocusAdorner` 設為 null，關閉預設焦點矩形。只有 `:focus-visible` 透過 `FocusAdorner` 提供外圈；滑鼠焦點沒有外圈。鍵盤焦點保留背景、邊框及前景色，包含勾選或 active 配色。

`ActionRoleStyles.axaml` 的 `Styles.Resources` 定義以下 token：

| Token | Light | Dark |
|---|---|---|
| `Nvt.Focus.RingBrush` | `#4DA3FF` | `#4DA3FF` |
| `Nvt.Focus.DangerRingBrush` | `#FF6B6B` | `#FF6B6B` |
| `Nvt.Focus.RingThickness` | `2`（共用 Thickness） | `2`（共用 Thickness） |

兩種控制項中，只有 `actionTextButton.actionDanger` 與 `actionIconButton.actionDanger` 使用紅圈。其他組合皆使用藍圈，包含 `actionChip actionDanger`。Border 設定 `IsHitTestVisible="False"`、`AdornerLayer.IsClipEnabled="False"` 及常值 `Margin="-4"`：外圈線寬 2 px，距按鈕外緣 2 px。文字按鈕使用 Core compact 半徑 6、外圈半徑 8；圖示按鈕與 chip 透過 `NfcPillCornerRadius` 保留膠囊圓角。

以 sRGB 相對亮度公式，計算對 Core 視窗背景（`NfcAppBackgroundBrush`）的對比：

| 主題／背景 | 藍圈對比 | 紅圈對比 |
|---|---:|---:|
| Light／`#F4F6FA` | 2.4266:1 | 2.5649:1 |
| Dark／`#0B1220` | 7.1312:1 | 6.7469:1 |

Light 對比仍待 owner 決定，已納入共用色盤提案。在此之前保留指定顏色。

### 動作角色已知差異與驗證

保留來源尺寸常值：`FormControlHeight` 與 `IconButtonSize` = 30；`Inset10_6` = `10,6`；`Inset8_4` = `8,4`；`InsetNone` 與 `BorderNone` = 0；`BorderControl` = 1.5；`OpacitySubtle` = 0.56；`WorkspaceSummaryChipMaxWidth` = 240。`RadiusPill` 對應 `NfcPillCornerRadius`（999）。文字 primitive 使用 `NfcCompactCornerRadius`（6），不依賴主機半徑；外圈加上 2 px 間隔。外圈半徑 8 與 margin -4 為焦點幾何常值。保留的規則沒有設定字族或字級，因此不新增 Fonts project reference；來源字重維持原樣。

保留來源樣式優先順序，即使與一般角色指南不同：

- 較後的 text-neutral 一般規則覆蓋較前的停用前景色 setter，因此 `actionTextButton actionNeutral` 停用時仍使用 `NfcTextBrush`。
- `.active` chip 配色覆蓋較前的滑過／按下／停用配色；停用透明度仍為 0.56。
- 已勾選的文字 primary／danger／ghost 按鈕，互動時使用較後的文字滑過／按下規則。文字 neutral 已勾選按鈕保留選取配色。已勾選圖示按鈕的滑過與按下都使用對應後的 inverse 滑過配色。

`Baseline/ActionRoleStyles.xml` 凍結對應後的來源子集與外圈。`ExtractedXamlMatchesFrozenBaseline` 比對完整 XML 樹；既有 ThemeTokens 與 ButtonStyles 基準不變。`ThemeContractTests` 在 Light、Dark 下驗證資源參考，包含樣式內的焦點字典。

`ActionRoleStylesTests` 只載入 ThemeTokens 與 ActionRoleStyles。Headless 檢查涵蓋兩種色盤及控制項、控制項／範本／文字的狀態配色、active 與勾選優先順序、尺寸／圓角／裁切、被動 status 修飾類別、實際 Tab 巡覽與滑鼠焦點、實際外圈筆刷／幾何／命中測試、預設焦點矩形關閉，以及焦點不改配色。每個開啟的視窗都在 `finally` 關閉；沒有 sleep 或網路呼叫。

NFH 通用互動文字配對檢查在 Light、Dark 都符合 4.5:1。另行移植的來源停用 neutral token 配對檢查，`NfcSurfaceBrush`／`NfcTextDisabledBrush` 不符合：Light **2.5640:1**、Dark **3.7277:1**。`DisabledNeutralTextContrastIsDocumentedException` 把這兩個數值記錄為已知例外；顏色及互動文字的 4.5:1 門檻皆不改動。

使用已還原的套件執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

建置：零警告、零錯誤。Avalonia 測試：350 通過、0 失敗、0 略過，共 350。

### NFC、NFH、NFU 採用動作角色

採用不在本次移植範圍。每個工具在各自的 PR 採用角色：載入上述兩個檔案，並移除本地的按鈕角色樣式。工具不得覆寫 Core 鍵以恢復舊色盤。每個採用 PR 須附上工具主要畫面的前後圖片，其中一張顯示 Tab 焦點，供 owner 核准外觀變更。

NFH：Dark neutral 文字按鈕由 NFH 的亮色表面（`#F4F7FC`）變成 Core 深色表面（`#111827`），並出現鍵盤焦點外圈。其他角色色彩依上表對應，包含圖示 inverse 滑過對應至 Core secondary 狀態配對。

以下屬於產品專用，暫時留在 NFH：`consoleHeaderAction`、`viewportOverlayAction`、`panelChromeToggle`、`dxfEditMiniAction`、`dangerTextButton`、CAD 專用 `chipAction` 修飾類別（`direct`、`transfer`、`linked`、`hidden`、`geometry`、`combined`、`duplicate`、`layer`、`incoming`、`outgoing`、`nocad`、`legacy`）、tab 樣式、未使用的 `actionButton`，以及來源第 127–141 行的 `controls|FontIcon` 規則。Core 不新增 FontIcon 控制項。

NFH 的圖片涵蓋三個渲染表面及 Dev 頁面角色矩陣。NFH 非 UI 測試清單及結果維持相同。採用、桌面圖片與 owner 核准仍待後續進行。
