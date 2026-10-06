[English](Inputs.md) | [中文](Inputs.zh-TW.md)

# Inputs

`NumberScrubber` 是十進位數值文字輸入控制項，具有無圖示的拖曳邊框，並可透過 Alt 加滑鼠滾輪調整。程式碼位於 `src/Nvt.Core.Avalonia/Inputs/`，命名空間為 `Nvt.Core.Avalonia.Inputs`。支援上下限、步長吸附、唯讀輸入及混合值。拖曳區域沒有字形或圖示相依性。

## 公開 API 與使用方式

原有十一個樣式屬性的名稱、型別及預設值皆保留。`Value` 保留 `BindingMode.TwoWay`；其他屬性保留原有的預設繫結中繼資料。

| 屬性 | 型別 | 預設值 | 行為 |
| --- | --- | --- | --- |
| `Value` | `decimal` | `0m` | 目前數值；外部賦值會限制範圍，但不吸附步長。 |
| `Minimum` | `decimal` | `decimal.MinValue` | 包含端點的下限。 |
| `Maximum` | `decimal` | `decimal.MaxValue` | 包含端點的上限。 |
| `SmallChange` | `decimal` | `1m` | 文字吸附與滾輪／拖曳步長；非正值停用吸附及步進。 |
| `LargeChange` | `decimal` | `10m` | 保留的屬性；來源沒有大步長行為。 |
| `FormatString` | `string` | `"0.###"` | 使用不變文化的顯示格式；空字串或純空白使用不變文化的預設格式。 |
| `RequireAltForWheel` | `bool` | `true` | 滾輪調整是否需要 Alt。 |
| `SnapToStep` | `bool` | `true` | 將文字數值取整到 `SmallChange` 的倍數，滾輪／拖曳步數取整為整數，中點皆向遠離零的方向取整。 |
| `ScrubPixelsPerStep` | `double` | `6.0` | 每個拖曳步長所需的垂直像素，有效最小值為 1。 |
| `IsReadOnly` | `bool` | `false` | 文字框設為唯讀、停用拖曳區域命中測試，並拒絕滾輪及開始新拖曳。 |
| `IsMixed` | `bool` | `false` | 顯示 `*`；取得焦點時清空文字，不改變數值或此旗標。 |
| `ScrubHint` | `string?` | `null` | 新增屬性：拖曳區域工具提示；null 或空字串代表沒有提示，純空白則保留。 |

主程式載入原有的 Avalonia 文字框佈景主題、既有 `Theme/ThemeTokens.axaml` 資源字典及 `Inputs/InputsStyles.axaml` 樣式，使用 `avares://Nvt.Core.Avalonia/` 資源 URI。樣式只保留此控制項及其 `numberScrubber`、`numberScrubberInput`、`numberScrubberSpin`、`focused` 類別的原有基本規則。NFH 的 `panelForm`、`panelFormField`、`settingsPage` 版面情境規則保留在 NFH。不需要新增佈景主題鍵值。

解析使用 `decimal.TryParse`、`NumberStyles.Float` 及 `CultureInfo.InvariantCulture`。接受前後空白、正負號及指數；拒絕千分位分隔符號及文化特定的小數逗號。空白、無效及超出 decimal 範圍的文字不改變 `Value`。編輯期間，每次可解析的文字變更都立即正規化至步長並限制範圍，同時保留輸入文字。Enter 提交並更新文字；Escape 從目前數值更新文字，保留所有即時數值更新。兩者都不結束編輯。失去焦點時提交、結束編輯並更新文字。`IsMixed` 不會自動清除。

滾輪使用實際垂直差值，可要求 Alt；垂直差值為零時忽略。符合條件的非零滾輪事件會標記為已處理，即使取整後差值為零或 `SmallChange` 非正。左鍵按下開始拖曳、擷取指標並讓文字框取得焦點。向上移動會從拖曳起始數值增加；水平移動無影響。放開或失去指標擷取會結束拖曳；文字框仍有焦點時保留 focused 類別。所有步進都不使用 `LargeChange`。

## 凍結來源

父儲存庫：`Dennis40816/nvt-freeform-helper`；參照：`1.3.x`；完整提交：`e01e07a361b8dc264a06b3741f40274feeeace2d`。來源使用 Avalonia 11.3.12 與 xUnit 2；Core 使用 Avalonia 12.1.1 與 xUnit v3。所有來源讀取都取自此提交。

擷取檔案：

- `src/FreeformHelper.UI/Controls/NumberScrubber.axaml`
- `src/FreeformHelper.UI/Controls/NumberScrubber.axaml.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.Properties.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.Input.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.Scrub.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.ValueFormatting.cs`
- `src/FreeformHelper.UI/Styles/Controls.Form.axaml` — 僅 NumberScrubber 及其組成部分的基本規則。其 `panelForm`、`panelFormField` 情境規則保留在 NFH。
- `src/FreeformHelper.UI/Styles/Controls.Settings.axaml` — 不擷取。其 `settingsPage` 情境規則保留在 NFH。

輔助證據：`src/FreeformHelper.UI/Styles/Tokens.axaml` 提供凍結的字面 token 值；`tests/FreeformHelper.Tests/UI/Smoke/HeadlessUiSmokeTests.cs` 提供既有工具提示涵蓋。已搜尋完整凍結測試樹中的 `NumberScrubber`，唯一引用位於 `RightWorkflowStep3View_TargetCapTooltipUsesCurrentNotchAndEmsCaps`。此測試檢查整個控制項的產品工具提示，並非拖曳區域提示。Core 使用合成文字移植通用的文字／型別／開啟斷言，另行測試 `ScrubHint`。

## 樣式 token 對應

以下列出所有從 NFH token 對應到既有 Core token 的項目。所有 Core 資源參照都使用 `DynamicResource`；`ThemeTokens.axaml` 未變更。

| NFH token | Core token |
| --- | --- |
| `BrushBgSurface` | `NfcSurfaceBrush` |
| `BrushBorderMuted` | `NfcBorderMutedBrush` |
| `BrushBgInteractivePressed` | `NfcSelectionSurfaceBrush` |
| `BrushAccent` | `NfcAccentBrush` |
| `BrushBorderStrong` | `NfcBorderBrush` |

## 驗證與 NFH 採用

`tests/Nvt.Core.Avalonia.Tests/Inputs/` 的無視窗特徵測試重用既有 `ThemeTestApplication`，鎖定 `en-US`、`de-DE`、`zh-TW` 下的字面數值與顯示文字、正負號、指數、空白、拒絕的分隔符號、無效／超界輸入、中點吸附、有限上下限與 decimal 兩端點。亦涵蓋即時數值變更序列、Enter／Escape 的處理狀態、實際失焦、混合值焦點、滾輪修飾鍵／小數差值、拖曳像素步數、實際指標擷取／失去擷取、唯讀行為、獨立工具提示，以及編譯後的明暗基本樣式。

設定 `AVALONIA_TELEMETRY_OPTOUT=1` 後執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Inputs"
```

採用時，NFH 設定 `ScrubHint="Drag or Alt + mouse wheel to adjust"`，並採用 Core 的色彩。NFH 只保留自己的 `panelForm`、`panelFormField`、`settingsPage` 情境樣式，並附上前後對照圖片。執行 NFH 的 `HeadlessUiSmokeTests` 測試組，特別是 `RightWorkflowStep3View_TargetCapTooltipUsesCurrentNotchAndEmsCaps`、`SharedTooltipStyle_CanOpen_Headless`、`SharedTooltipStyle_StringTooltipUsesTooltipForegroundTextBlock_Headless` 及兩個 `FreeformHelperView` 版面測試。對凍結 NFH 控制項及採用後的 Core 控制項重現 Core 特徵測試案例。

每次編輯及事件派送後，比較字面 `Value`、顯示文字與有序 `ValueProperty` 變更。包含步長 `0.5` 下的 `1.24`、`1.25`、`-1.25`、上下限 `-1.3..1.3`、格式 `0.###` 下的 `12.3456`、步長 `1`、`0.1`、`10` 下的 decimal 兩端點、無效分隔符號、混合值焦點、空輸入、Enter、Escape 及失焦。比較滾輪差值 `0`、`0.49`、`0.5`、`-0.5`、`1`、`-1`，含有／沒有 Alt 及兩種 Alt 政策；比較每步六像素下的 `±3`、`±6`、`±12` 像素拖曳、失去擷取、放開、唯讀及擷取期間變更唯讀。比較已處理旗標、焦點／類別、擷取目標、唯讀／命中測試狀態及保持不變的混合值旗標，包含保留的溢位例外。

數值及事件行為必須維持相同。NFH UI 快照可能因佈景主題／字型對應及選用提示而改變。採用 PR 附上相同作業系統、字型、DPI、佈景主題及控制項狀態的前後圖片；圖片補充行為證據。本次擷取不包含 NFH 採用。

## 已知差異

- 命名空間、版權標頭及公開 XML 文件屬於 Core；保留 partial 檔案切分與數值運算。
- Avalonia 12 以 `FocusChangedEventArgs` 取代 `GotFocusEventArgs`，只調整焦點處理函式的參數型別。繫結模式參照匯入 `Avalonia.Data` 命名空間，避免與 `Nvt.Core.Avalonia` 衝突。
- 硬編碼的產品工具提示改為唯一選用屬性 `ScrubHint`。省略來源 XAML 未使用的命名空間，不匯入 NFH 圖示資源。
- 透過上表以 Core 既有色彩取代 NFH token，因此視覺快照可能不同。以下型別值沒有合適的既有 Core token，保留字面值，不新增鍵值：

| NFH token | 保留字面值 |
| --- | --- |
| `FormControlHeight` | `30` |
| `BorderThin` | `1` |
| `RadiusMd` | `10` |
| `Inset6_2` | `6,2` |
| `BorderRightThin` | `1,0,0,0` |
| `SpinButtonWidth` | `6` |
| `RadiusRightMd` | `0,10,10,0` |

保留而超出任務摘要的來源細節：正規化及滾輪／拖曳 decimal 運算可能在限制範圍前拋出 `OverflowException`，不新增飽和運算。已擷取拖曳期間變更唯讀不會取消拖曳，且程式變更已取得焦點的唯讀輸入文字仍會更新數值。仍在編輯時格式化可排入另一次即時解析：停用吸附且格式為 `0.###` 時，對 `12.3456` 按 Enter 或 Escape，佇列文字事件處理完後數值／文字為 `12.346`；失焦在這些事件前結束編輯，保留數值 `12.3456`，文字為 `12.346`。不新增上下限與步長選項驗證。

## 保留於 NFH 的內容

產品文字、產品專用工具提示、`panelForm`、`panelFormField` 與 `settingsPage` 情境樣式、UI 快照基準、其他全部控制項及圖示、設定與 view-model 繫結皆保留於 NFH。本模組不新增設定抽象、圖示系統、其他可設定文字或採用變更。沒有未解決的 API 問題；NFH 採用證據仍需由其自己的 PR 提供。
