[English](post-adoption-tuning.md) | [中文](post-adoption-tuning.zh-TW.md)

# 導入後微調

編輯 token，統一微調控制項。不要將幾何值寫入樣式檔案。

| 控制項或角色 | Token 檔案 |
| --- | --- |
| CheckBox 與 RadioButton | `ChoiceTokens.axaml` |
| ListBoxItem 與 ComboBoxItem | `ListTokens.axaml` |
| MenuItem、ContextMenu 與選單分隔線 | `ListTokens.axaml` |
| Expander、ProgressBar、Separator 與 GridSplitter | `DividerTokens.axaml` |
| ToggleButton 角色與 ToggleSwitch | `ToggleTokens.axaml` |
| TextBox、NumericUpDown、關閉狀態的 ComboBox、TabControl 與 TabItem | `FormTokens.axaml` 與 `TabTokens.axaml` |
| 共用選取顏色，以及外側或內縮焦點邊距 | `ControlTokens.axaml` |
| 靜止填色模式 | `ControlTokens.axaml`（預設）、`RestFillSoft.axaml` 與 `RestFillNone.axaml` |
| 隨形狀改變的圓角 | `ShapePill.axaml` 與 `ShapeSquare.axaml` |
| 共用列高、字型與焦點框粗細 | `ThemeTokens.axaml` |

這些檔案位於 `src/Nvt.Core.Avalonia/Theme`。
[Theme 模組](modules/Theme.zh-TW.md) 列出兩種形狀的預設值。

新 token 使用 `Nvt.<ControlOrFamily>.<Role>` 命名。
使用角色名稱，例如 `Nvt.CheckBox.CheckWidth`，不要使用 `Size12` 這類數字名稱。
內距、邊距與框線使用 `Thickness`。單一尺寸使用 `x:Double`。
隨形狀改變的值必須放入兩個形狀字典。
不受形狀影響的值維持相同預設值。
沿用屬性原有的資源參照種類。這些控制項使用 `DynamicResource` 參照可微調的幾何值。
結構性的零值、網格位置、單行限制與堆疊順序保留在樣式中。

變更 `ThemeTokens.axaml` 中的 `Nvt.Focus.RingThickness`，即可用單一值調整所有鍵盤焦點框。
變更 `Nvt.Controls.FocusRingMargin`，可調整 Choice 控制項、Expander、GridSplitter 與非開關 ToggleButton 角色的外側焦點框。
此值不會改變 Button 角色的焦點框。
開關焦點框的幾何由 `Nvt.Toggle.SwitchFocusWidth` 與 `Nvt.Toggle.SwitchFocusHeight` 決定。
變更 `Nvt.Controls.InsetFocusRingMargin`，可調整清單與選單項目的內縮焦點框。

下列相互關聯的值須保持一致：

- `Nvt.Shape.FocusCornerRadius`：Square 為 10 DIP = 控制項圓角 6 DIP + 外側邊距 4 DIP。
- `Nvt.List.FocusCornerRadius`：Square 為 4 DIP = 控制項圓角 6 DIP - 內縮邊距 2 DIP。
- `Nvt.Expander.ContainerPadding`：6 DIP，與分隔線的 6 DIP 間距對齊。
- `Nvt.Menu.PopupOffset`：-16 DIP，與值為 16 DIP 的 `Nvt.Menu.PopupShadowMargin` 對齊。

在應用程式資源根節點覆寫共用 key，可微調所有已附加的實例。
變更家族 token，可縮小調整範圍。動態資源更新時不需替換模板。

變更開關幾何時，維持軌道、旋鈕、移動距離與焦點框尺寸的一致性。
`Nvt.Toggle.SwitchKnobTravel` 同時設定旋鈕畫布寬度與勾選時的位置。
原生 `ToggleSwitch` 會保留旋鈕原位置，直到勾選狀態改變，才使用新的移動距離。
`Nvt.Menu.PopupOffset` 在兩個軸向補償 popup 陰影邊距。
變更 popup 內距或陰影邊距時，檢查子選單位移。

變更顏色後，檢查[英文](modules/Theme.md)與[繁體中文](modules/Theme.zh-TW.md)的對比表。
執行完整 Avalonia 測試專案。狀態測試涵蓋兩種主題與兩種形狀。
使用詳細測試輸出檢視實測對比值。
依據測量結果更新兩份表格。
啟用文字的對比至少為 4.5:1。停用文字、作用中指示器與鍵盤焦點框的對比至少為 3:1。

```text
dotnet build Nvt.Core.sln -warnaserror
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --logger "console;verbosity=detailed"
```

輸出圖表前，將各 renderer 環境變數設為輸出目錄。

| Renderer 類別 | 重新設計方法 | 環境變數 | 圖表 |
| --- | --- | --- | --- |
| `ChoiceStylesRenderer` | `RenderRedesignChoices` | `NVT_CHOICE_IMAGES_DIR` | CheckBox、RadioButton |
| `ListMenuStylesRenderer` | `RenderRedesignListsAndMenus` | `NVT_LIST_IMAGES_DIR` | 清單、下拉項目列、選單、快顯選單 |
| `DividerStylesRenderer` | `RenderRedesignDividers` | `NVT_DIVIDER_IMAGES_DIR` | Expander、進度列、分割線、分隔線 |
| `ToggleStylesRenderer` | `RenderRedesignToggles` | `NVT_TOGGLE_IMAGES_DIR` | 開關與 Toggle 角色 |
| `FormStylesRenderer` | `RenderForms` | `NVT_FORMS_IMAGES_DIR` | TextBox、NumericUpDown、ComboBox 與 Fluent 比較圖 |
| `TabStylesRenderer` | `RenderTabs` | `NVT_FORMS_IMAGES_DIR` | TabItem 與 Fluent 比較圖 |
| `TextStylesRenderer` | `RenderTextStyles` | `NVT_FORMS_IMAGES_DIR` | Light 與 Dark 的文字角色 |

```text
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --filter "FullyQualifiedName~RenderRedesign|FullyQualifiedName~FormStylesRenderer|FullyQualifiedName~TabStylesRenderer|FullyQualifiedName~TextStylesRenderer"
```

Renderer 以無頭模式輸出 65 張圖表，縮放比例為 100%。
檔名使用 `<control>-<pill|square>-<light|dark>.png` 與 `separator.png`。
未設定這些變數時，renderer 測試會檢查版面，但不寫入影像。
只調整幾何時，將解碼後的像素與先前圖表比較。每個像素都必須相同。
