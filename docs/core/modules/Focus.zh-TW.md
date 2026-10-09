[English](Focus.md) | [中文](Focus.zh-TW.md)

# Focus

`src/Nvt.Core.Avalonia/Focus/` 的公開附加行為使用命名空間 `Nvt.Core.Avalonia.Focus`：

- `FocusOnRevealBehavior`：控制項附加到視覺樹或自身 `IsVisible` 變為 true 時，排程以 Tab 導覽方式移轉焦點；執行時再次確認行為仍啟用、有效可見／啟用且可取得焦點。
- `FocusToolTipBehavior`：Tab／方向鍵焦點開啟既有 tooltip，不移轉焦點。Escape、ComboBox 選取變更及下拉關閉會關閉並抑制 tooltip；失去焦點或指標離開恢復 tooltip 服務，指標進入時則僅在控制項未取得焦點時恢復服務。

凍結的父版本基準：NFC（`nvt_fw_combiner`）、ref `origin/1.2.x`、完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`、Avalonia 12.0.5。抽取的來源路徑：

- `src/NvtFwCombiner.Presentation.Avalonia/Behaviors/FocusOnRevealBehavior.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/Behaviors/FocusToolTipBehavior.cs`

兩個行為檔僅變更命名空間並加入要求的版權標頭。以下凍結測試路徑的通用斷言已移植至 `tests/Nvt.Core.Avalonia.Tests/Focus/`：

- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Startup.cs` — `CatalogWarmupUsesAccessibleRetryableForegroundLoadingSurface` 的附加／排程焦點合約，改以執行階段驗證。
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.cs` — `IcDetailTooltipUsesOneNonInteractiveFocusAwareCard` 的 ComboBox tooltip 生命週期斷言，使用合成選項。
- `tests/NvtFwCombiner.UiSmoke.Tests/OutputDeliverySourceTooltipTests.cs` — `KeyboardFocusOpensDetailsWithoutDuplicateOrModalDismissal` 的 Tab／Shift+Tab、單一 tooltip、Escape 攔截、焦點保留及恢復斷言，使用合成列。

新增行為特徵測試涵蓋 null／附加屬性預設值、延後可用性檢查、停用與重新啟用、僅祖先揭露、重新附加、導覽方式、指標恢復及下拉關閉。產品內容、樣式與對話框／view model 留在 NFC。

還原套件後的 Core 驗證：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore -p:UseSharedCompilation=false -m:1 -nr:false
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

NFC 採用時，先以上述父版本 commit 凍結 UI 證據。以 Core 取代重複行為前後，皆以 `--no-restore` 建置並執行 `dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build`，包含 `XamlControlStyleContractTests`（上述合約及 tooltip 使用端）、`OutputDeliverySourceTooltipTests`、`SupportMatrixInteractionTests` 與 `StartupFocusTests`。僅調整採用所需的命名空間／來源位置參照，保留行為斷言與預期證據。比較焦點控制項與走訪順序、tooltip 開啟／服務狀態、Escape 處理；在相同 OS、字型、DPI 與主題下，將既有截圖逐像素比對，不得更新基準來接受差異。工具採用及這些產品層級比較仍待後續工作。

## Tooltip 文字換行與樣式

`ToolTipTextWrapping.Register()` 將後續設定的非空白字串提示轉為會換行的 `TextBlock`。
Null、空字串、純空白及非字串提示保持原狀。
處理器透過 `SetCurrentValue` 保留字串繫結。
請在建立使用 tooltip 的控制項之前呼叫。重複呼叫只會安裝一個處理器。
`ToolTipTextWrapping.Unregister()` 用於測試時移除處理器，既有文字控制項仍會保留。
註冊鎖保護訂閱的生命週期。請在 UI 執行緒設定提示。

產生的文字以動態資源繫結前景色與最大寬度。
Avalonia 透過私有 `SetResourceReference` 輔助方法，以 `Bind` 搭配 `DynamicResourceExtension` 完成繫結。
既有提示會隨主題及資源替換更新，不需要重建文字。
`FocusToolTipBehavior` 繼續負責鍵盤開啟、Escape 與焦點恢復。
自訂控制項提示保留自己的內容及區域屬性值。

### Tooltip tokens

`Focus/ToolTipStyles.axaml` 擁有以下 tokens 與三個 tooltip 樣式規則。
筆刷別名分別在 Light 與 Dark 字典中使用既有 Core 色盤。
樣式沒有新增色彩常值，並保留 Fluent 的 tooltip 範本。
它設定 tooltip、子文字及範本內容呈現器的前景色。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.ToolTip.BackgroundBrush` | `NfcSurfaceBrush`（`#FFFFFF`） | `NfcSurfaceBrush`（`#111827`） |
| `Nvt.ToolTip.BorderBrush` | `NfcBorderSoftBrush`（`#94A3B8`） | `NfcBorderSoftBrush`（`#475569`） |
| `Nvt.ToolTip.ForegroundBrush` | `NfcTextBrush`（`#1E293B`） | `NfcTextBrush`（`#E2E8F0`） |
| `Nvt.ToolTip.CornerRadius` | 上限 8 DIP：Pill 8、Square 6 | 上限 8 DIP：Pill 8、Square 6 |
| `Nvt.ToolTip.Padding` | 8,4 DIP | 8,4 DIP |
| `Nvt.ToolTip.BorderThickness` | 1 DIP | 1 DIP |
| `Nvt.ToolTip.MaxWidth` | 320 DIP | 320 DIP |

每個角取 `Nvt.Shape.ControlCornerRadius` 與 `Nvt.ToolTip.CornerRadius` 的較小值。
兩個輸入都使用動態資源，因此執行時切換形狀會更新既有提示。
`MaxWidth` 同時限制 tooltip 與產生的文字。內距與邊框會減少可用文字寬度。
應用程式可在資源根覆寫這些 tokens。
調整色盤時，請維持最低對比要求。

| 提示文字與背景的對比 | Light | Dark |
| --- | --- | --- |
| `Nvt.ToolTip.ForegroundBrush` 對 `Nvt.ToolTip.BackgroundBrush` | 14.629:1 | 14.390:1 |

Headless 測試計算 sRGB 相對亮度，並斷言兩種主題及形狀皆至少達到 4.5:1。
測試也涵蓋開啟、長文字、繫結更新、註冊清理，以及主題、形狀與 tokens 的即時變更。
`ToolTipStylesRenderer` 僅在設定 `NVT_TOOLTIP_IMAGES_DIR` 時輸出預覽。
它輸出寬度 1200 像素的 `tooltip-light.png`、`tooltip-dark.png`、`tooltip-square-light.png` 與 `tooltip-before-light.png`。

### 載入與註冊

將既有主題 tokens 合併至應用程式資源，再於 Fluent 之後引入 tooltip 樣式。
Tooltip 樣式需獨立引入。`ThemeTokens.axaml` 不會載入它。

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://Nvt.Core.Avalonia/Focus/ToolTipStyles.axaml" />
</Application.Styles>
```

載入應用程式 XAML 後，在建立檢視之前註冊：

```csharp
using Nvt.Core.Avalonia.Focus;

ToolTipTextWrapping.Register();
```

註冊前已設定的字串提示會保持原狀，直到再次設定。
需要鍵盤存取提示的控制項，請保留 `FocusToolTipBehavior.IsEnabled`。

### 來源基準與採用

來源基準：NFH、ref `origin/main`、commit `d6ceb2afb591162cbafc1e99eaf7891e6c4855e1`。
抽取的來源與測試構想來自以下檔案：

- `src/FreeformHelper.UI/Services/SharedToolTipStyleService.cs`。
- `src/FreeformHelper.UI/Styles/Controls.Overlay.axaml` 最後三個 tooltip 規則。
- `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs` 的 tooltip 樣式檢查。
- `tests/FreeformHelper.Tests/UI/Smoke/HeadlessUiSmokeTests.cs` 的 tooltip 開啟測試。

NFH 採用時需要以下變更：

1. 載入 Core 主題 tokens，並於 Fluent 之後引入 `Focus/ToolTipStyles.axaml`。
2. 刪除 `SharedToolTipStyleService`，將其註冊改為 `ToolTipTextWrapping.Register()`。
3. 刪除 `Controls.Overlay.axaml` 的三個 tooltip 規則，保留對話框按鈕規則。
4. 依下表替換三個筆刷鍵的參考，並移除已無用途的區域定義。
5. 擷取 Light 與 Dark 的採用前後圖片，並執行既有 tooltip smoke tests。

| 來源筆刷鍵 | Core token |
| --- | --- |
| `BrushTooltipBackground` | `Nvt.ToolTip.BackgroundBrush` |
| `BrushTooltipBorder` | `Nvt.ToolTip.BorderBrush` |
| `BrushTooltipForeground` | `Nvt.ToolTip.ForegroundBrush` |

已知差異：來源在兩種主題都使用白底黑字、固定 6 DIP 圓角與前景色查詢。
Core 使用主題色盤、上限 8 DIP 的即時形狀圓角、動態前景繫結及 320 DIP 最大寬度。
產品採用及其前後對照證據仍待完成。
