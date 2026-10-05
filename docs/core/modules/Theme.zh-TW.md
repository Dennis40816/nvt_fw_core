[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme（`Nvt.Core.Avalonia.Theme`）

NFC 通用主題已抽取至 `Theme/ThemeTokens.axaml` 與 `Theme/ButtonStyles.axaml`。將資源字典合併至應用程式資源，並在主機原本的按鈕樣式作用範圍載入樣式：

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
```

為維持相容，資源鍵保留 `Nfc` 名稱。模組保留 Light、Dark 各 51 個筆刷鍵、22 個共用圓角／圖示／間距／字型 token、語意按鈕範本，以及 78 個樣式區塊。通用角色包含 semantic、primary、secondary、danger、command、icon/copy、close、inline edit、breadcrumb、action/browse、file reveal、summary chip，以及支援減少動態效果的展開式 rail。值、範本繫結、選擇器分支、轉場與樣式順序皆維持原樣。

排除的 token 家族：`NfcKept`、`NfcReferenceInput`、`NfcControllerInput`、`NfcHex`、`NfcRequired`、`NfcMemory`、`NfcWorkflow`、`NfcWorkspace`、`NfcNav`、`NfcReport`。排除的選擇器分支：`settingsNavItem`、`messageCenterNavigationItem`、`activityFilter`、`sourceEditButton`、`version*`、`slotClearAction`、`outputRailAction`、`outputNameEdit`。混合選擇器只保留通用分支。這些產品資源與分支由 NFC 保留；整合時須維持原有樣式優先順序。

凍結來源：NFC（`nvt_fw_combiner`），ref `origin/1.2.x`，完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。抽取的來源路徑：

- `src/NvtFwCombiner.Presentation.Avalonia/Styles/ThemeTokens.axaml`
- `src/NvtFwCombiner.Presentation.Avalonia/Styles/MainWindowButtonStyles.axaml`

通用測試斷言移植自同一 commit 的：

- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.ThemeTokens.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Buttons.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Build.cs`

`tests/Nvt.Core.Avalonia.Tests/Theme/Baseline/*.xml` 凍結來源資源的通用子集。`ExtractedXamlMatchesFrozenBaseline` 比對完整 XML 樹，逐一鎖定資源鍵／值、選擇器、setter、範本繫結、轉場及順序。行為特徵測試也檢查所有編譯後 token 值、兩種主題、一般按鈕幾何、primary／secondary／danger／action 狀態、滑鼠與鍵盤焦點差異、rail 展開及減少動態效果。測試保留 NFC 既有的 primary presenter 行為：較後的基本 setter 覆蓋按下／停用時的 presenter 配色，但內部文字仍隨狀態變化。同樣的 22 個執行階段特徵案例已對完整凍結 NFC XAML 的暫存副本通過驗證；副本未保留。

使用已還原的套件驗證 Core：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore --disable-build-servers
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

採用時的零差異驗證：先在凍結來源執行 NFC 的 `NvtFwCombiner.UiSmoke.Tests`，將通用資源／樣式載入改為 Core 後，再執行同一套測試。包含 `XamlControlStyleContractTests`（主題鍵、按鈕狀態、邊框與減少動態效果）、`NavigationFocusIndicatorTests`、`NavigationCheckedStateTests`，以及既有 modal 鍵盤巡覽測試。比對解析後筆刷值、按鈕／presenter 邊界與幾何、滑過／按下／停用／焦點狀態、Tab 順序及 Light／Dark 桌面截圖。OS、字型資產（含原有 Inter 設定）、DPI、視窗大小、主題與減少動態效果設定必須相同；與原始截圖逐像素比對，不得更新基準來接受差異。先依工具既有流程還原／建置，再執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build
```

目前尚無採用工具。NFC 整合、完整產品測試及桌面截圖比對仍屬後續工作；本次未更動套件或共用設定。
