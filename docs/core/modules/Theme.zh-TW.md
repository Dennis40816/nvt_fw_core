[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme（`Nvt.Core.Avalonia.Theme`）

Theme 為 NFC、NFH、NFU 定義同一套中性色、語意色、圓角、尺寸、狀態與焦點框。各工具只保留主色。將 token 合併至應用程式資源，並在 Fluent 之後載入樣式檔。只有 `ButtonStyles.axaml` 是 Core 按鈕樣式檔。八個舊有字型值與資源解析器保持不變。

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<ResourceInclude Source="avares://Nvt.Core.Fonts/FontRoles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/FormStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ListStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/TabStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/TextStyles.axaml" />
```

## 共用色票

既有鍵名與型別全部保留。`Nfc*` 是相容命名，不代表 NFC 獨有外觀。資料色留在工具，不加入 Core。

### 中性色

| Token | Light | Dark |
|---|---|---|
| `NfcAppBackgroundBrush` | `#F1F5F9` | `#0B1220` |
| `NfcSurfaceBrush` | `#FFFFFF` | `#111827` |
| `NfcSurfaceSubtleBrush` | `#F8FAFC` | `#182337` |
| `NfcSelectionSurfaceBrush` | `#E8EEF5` | `#1E293B` |
| `NfcSecondaryActionPressedBrush` | `#E2E8F0` | `#243247` |
| `NfcBorderBrush` | `#718096` | `#708198` |
| `NfcBorderSoftBrush` | `#94A3B8` | `#475569` |
| `NfcBorderMutedBrush` | `#CBD5E1` | `#334155` |
| `NfcDividerBrush` | `#E2E8F0` | `#273449` |
| `NfcTextStrongBrush` | `#0F172A` | `#F8FAFC` |
| `NfcTextBrush` | `#1E293B` | `#E2E8F0` |
| `NfcTextSecondaryBrush` | `#475569` | `#CBD5E1` |
| `NfcTextMutedBrush` | `#526176` | `#A1AEC2` |
| `NfcTextDisabledBrush` | `#68778C` | `#7B8CA5` |

`NfcBorderBrush` 是可辨識的輸入邊界；soft、muted 與 divider 只作裝飾。停用控制項與捲軸拇指共用 disabled 色，拇指滑入採 muted 色。

### 語意色

| Token | Light | Dark |
|---|---|---|
| `NfcSuccessSurfaceBrush` | `#ECFDF5` | `#123024` |
| `NfcSuccessBorderBrush` | `#4D8E68` | `#4D9D75` |
| `NfcSuccessAccentBrush` | `#168043` | `#69CF99` |
| `NfcSuccessTextStrongBrush` | `#14532D` | `#BBF7D0` |
| `NfcSuccessTextBrush` | `#166534` | `#89DFB2` |
| `NfcSuccessEmphasisBrush` | `#166534` | `#89DFB2` |
| `NfcInfoSurfaceStrongBrush` | `#ECF3FC` | `#173248` |
| `NfcInfoTextBrush` | `#245B91` | `#8AC5F2` |
| `NfcWarningSurfaceBrush` | `#FFFBEB` | `#2D2313` |
| `NfcWarningBorderBrush` | `#A97925` | `#BB8A3D` |
| `NfcWarningAccentBrush` | `#946000` | `#E5B35D` |
| `NfcWarningAccentStrongBrush` | `#875400` | `#F5CE8A` |
| `NfcWarningTextSubtleBrush` | `#734A11` | `#F5CE8A` |
| `NfcWarningTextMutedBrush` | `#875400` | `#E5B35D` |
| `NfcWarningTextBrush` | `#875400` | `#F5CE8A` |
| `NfcWarningBorderStrongBrush` | `#946000` | `#E5B35D` |
| `NfcWarningTextStrongBrush` | `#70440B` | `#FFE9C4` |
| `NfcCautionSurfaceBrush` | `#FFFBEB` | `#2D2313` |
| `NfcCautionBorderBrush` | `#A97925` | `#BB8A3D` |
| `NfcCautionBorderSoftBrush` | `#BE913D` | `#9D773B` |
| `NfcCautionTextBrush` | `#875400` | `#F5CE8A` |
| `NfcDangerSurfaceBrush` | `#FFF1F3` | `#2B171E` |
| `NfcDangerBorderBrush` | `#C04A5C` | `#C96579` |
| `NfcDangerTextBrush` | `#A82035` | `#FFADB7` |
| `NfcDangerSurfaceMutedBrush` | `#FFE8ED` | `#321B24` |
| `NfcDangerBorderStrongBrush` | `#A82035` | `#FF8295` |
| `NfcDangerTextStrongBrush` | `#861B2C` | `#FFD0D7` |
| `NfcCriticalSurfaceBrush` | `#FFE4E6` | `#391D27` |
| `NfcCriticalBorderBrush` | `#A82035` | `#FF8295` |
| `NfcModalScrimBrush` | `#660F172A` | `#B30B1220` |

遮罩上不直接放資訊文字；對話框使用不透明表面。相容的 success emphasis、warning muted、caution 與 critical 名稱都保留。

### 工具主色

Core 預設採 NFC 值。一個主色以七個 `NfcAccent*` 鍵表達；工具在應用程式資源範圍的 Light／Dark 字典設定下表全部七個鍵。採用分為兩步（擁有者 2026-10-07 決定）：

1. 套件 PR：工具把 Core 套件釘到新版本，畫面不變。
2. 外觀 PR，經擁有者核准：工具載入 `ThemeTokens`、`ButtonStyles` 與 `ScrollStyles`，只設定主色的七個 `NfcAccent*` 鍵，把按鈕改用 Core 角色，並刪除功能相同的本地樣式。高度、圓角與焦點框也在同一個 PR 改變。PR 附 Light 與 Dark 的前後對照影像。

覆寫無法還原工具的舊外觀。部分 Core 值是固定的，例如 14 px 捲軸寬度與 14,0 按鈕內距。也有多個狀態共用一個鍵，例如主色按鈕的底色與邊框。

不得局部覆寫基本控制項樣式。需要變體時由 Core 增加角色。焦點色不跟隨主色。

| Tool / 工具 | Token | Light | Dark |
|---|---|---|---|
| NFC | `NfcAccentBrush` | `#1557E9` | `#5FA5FA` |
| NFC | `NfcAccentStrongBrush` | `#1148BE` | `#8FBFFB` |
| NFC | `NfcAccentSurfaceBrush` | `#EFF3FD` | `#1A2940` |
| NFC | `NfcAccentSurfaceSubtleBrush` | `#F7F9FE` | `#162034` |
| NFC | `NfcAccentBorderBrush` | `#1557E9` | `#5FA5FA` |
| NFC | `NfcAccentBorderStrongBrush` | `#1148BE` | `#8FBFFB` |
| NFC | `NfcAccentBorderLightBrush` | `#1557E9` | `#5FA5FA` |
| NFH | `NfcAccentBrush` | `#2967A9` | `#53A6FF` |
| NFH | `NfcAccentStrongBrush` | `#225489` | `#86C0FF` |
| NFH | `NfcAccentSurfaceBrush` | `#F0F4F9` | `#192941` |
| NFH | `NfcAccentSurfaceSubtleBrush` | `#F8FAFC` | `#152134` |
| NFH | `NfcAccentBorderBrush` | `#2967A9` | `#53A6FF` |
| NFH | `NfcAccentBorderStrongBrush` | `#225489` | `#86C0FF` |
| NFH | `NfcAccentBorderLightBrush` | `#2967A9` | `#53A6FF` |
| NFU | `NfcAccentBrush` | `#4A6F00` | `#82B11A` |
| NFU | `NfcAccentStrongBrush` | `#3D5B00` | `#97CD1E` |
| NFU | `NfcAccentSurfaceBrush` | `#F2F5ED` | `#1F2A25` |
| NFU | `NfcAccentSurfaceSubtleBrush` | `#F9FAF6` | `#182126` |
| NFU | `NfcAccentBorderBrush` | `#4A6F00` | `#82B11A` |
| NFU | `NfcAccentBorderStrongBrush` | `#3D5B00` | `#97CD1E` |
| NFU | `NfcAccentBorderLightBrush` | `#4A6F00` | `#82B11A` |

填色按鈕透過 `DynamicResource` 使用 `ThemeTokens.axaml` 的 `Nvt.Button.PrimaryLabelBrush`：Light 為 `#FFFFFF`，Dark 為 `#0B1220`。工具可在採用步驟於應用程式範圍覆寫此鍵。

## 尺寸與圓角

| 項目 | Token／值 |
|---|---|
| Control height / 控制項高度 | `NfcControlHeight` = 32 |
| Compact corners / 緊湊圓角 | `NfcCompactCornerRadius` = 6 |
| Surface corners / 表面圓角 | `NfcSurfaceCornerRadius` = 8 |
| Button and chip corners / 按鈕與膠囊圓角 | `NfcPillCornerRadius` = 999 |
| Text padding / 文字內距 | 14 horizontal / 水平, 0 vertical / 垂直 |
| Icon button / 圖示按鈕 | 32 × 32, padding / 內距 0 |
| Button and chip border / 按鈕與膠囊邊界 | 1 |
| Single-line field guidance / 單行欄位原則 | 32, padding / 內距 `12,0`, Pill / Square radius / 圓角 `999` / `6` |
| Spacing / 間距 | `NfcSpace2/4/8/12/16/24`; `NfcFieldSpacing` = 4 |

以上皆為邏輯像素，不再額外乘 DPI。按鈕內容置中、單行、字元省略並裁切；圖示角色繼承所組合色彩角色。焦點需容器預留 4 px；相鄰控制項建議間隔 8 px。接合邊為零圓角；多行編輯器與資料視覺不套用 32 px。字型與字型 fallback 不變。`FormStyles.axaml` 提供共用輸入控制項樣式。

## 按鈕角色與狀態

公開 class 為 `actionPrimary`、`actionNeutral`、`actionDanger`、`actionGhost`、`actionIconButton`、`chipAction` 與 `Border.chipStatus`。互動角色同時支援 `Button`／`ToggleButton`，單一角色即完整外觀。舊版面 class `actionTextButton`、`actionChip`、`actionButton` 不是公開角色。沒有全域 Button／ToggleButton 規則；未指定角色的控制項不受此檔影響。

| 角色 | 狀態 | 底色 | 邊界 | 文字 |
|---|---|---|---|---|
| `actionPrimary` | rest | `NfcAccentBrush` | `NfcAccentBrush` | `Nvt.Button.PrimaryLabelBrush` |
| `actionPrimary` | hover | `NfcAccentStrongBrush` | `NfcAccentStrongBrush` | `Nvt.Button.PrimaryLabelBrush` |
| `actionPrimary` | pressed | `NfcAccentStrongBrush` | `NfcAccentStrongBrush` | `Nvt.Button.PrimaryLabelBrush` |
| `actionPrimary` | disabled | `NfcSurfaceSubtleBrush` | `NfcBorderMutedBrush` | `NfcTextDisabledBrush` |
| `actionPrimary` | checked | `NfcAccentSurfaceBrush` | `NfcAccentBorderBrush` | `NfcAccentStrongBrush` |
| `actionPrimary` | checked_hover | `NfcAccentSurfaceSubtleBrush` | `NfcAccentBorderStrongBrush` | `NfcAccentStrongBrush` |
| `actionPrimary` | checked_pressed | `NfcSecondaryActionPressedBrush` | `NfcAccentBorderStrongBrush` | `NfcAccentStrongBrush` |
| `actionNeutral` | rest | `NfcSurfaceBrush` | `NfcBorderBrush` | `NfcTextBrush` |
| `actionNeutral` | hover | `NfcSelectionSurfaceBrush` | `NfcBorderBrush` | `NfcTextBrush` |
| `actionNeutral` | pressed | `NfcSecondaryActionPressedBrush` | `NfcBorderBrush` | `NfcTextBrush` |
| `actionNeutral` | disabled | `NfcSurfaceSubtleBrush` | `NfcBorderMutedBrush` | `NfcTextDisabledBrush` |
| `actionNeutral` | checked | `NfcAccentSurfaceBrush` | `NfcAccentBorderBrush` | `NfcAccentStrongBrush` |
| `actionNeutral` | checked_hover | `NfcAccentSurfaceSubtleBrush` | `NfcAccentBorderStrongBrush` | `NfcAccentStrongBrush` |
| `actionNeutral` | checked_pressed | `NfcSecondaryActionPressedBrush` | `NfcAccentBorderStrongBrush` | `NfcAccentStrongBrush` |
| `actionDanger` | rest | `NfcDangerSurfaceBrush` | `NfcDangerBorderBrush` | `NfcDangerTextBrush` |
| `actionDanger` | hover | `NfcDangerSurfaceMutedBrush` | `NfcDangerBorderStrongBrush` | `NfcDangerTextBrush` |
| `actionDanger` | pressed | `NfcCriticalSurfaceBrush` | `NfcCriticalBorderBrush` | `NfcDangerTextStrongBrush` |
| `actionDanger` | disabled | `NfcSurfaceSubtleBrush` | `NfcBorderMutedBrush` | `NfcTextDisabledBrush` |
| `actionDanger` | checked | `NfcDangerSurfaceMutedBrush` | `NfcDangerBorderStrongBrush` | `NfcDangerTextStrongBrush` |
| `actionDanger` | checked_hover | `NfcDangerSurfaceMutedBrush` | `NfcDangerBorderStrongBrush` | `NfcDangerTextStrongBrush` |
| `actionDanger` | checked_pressed | `NfcCriticalSurfaceBrush` | `NfcCriticalBorderBrush` | `NfcDangerTextStrongBrush` |
| `actionGhost` | rest | `Transparent` | `Transparent` | `NfcTextSecondaryBrush` |
| `actionGhost` | hover | `NfcSelectionSurfaceBrush` | `Transparent` | `NfcTextBrush` |
| `actionGhost` | pressed | `NfcSecondaryActionPressedBrush` | `Transparent` | `NfcTextBrush` |
| `actionGhost` | disabled | `NfcSurfaceSubtleBrush` | `NfcBorderMutedBrush` | `NfcTextDisabledBrush` |
| `actionGhost` | checked | `NfcAccentSurfaceBrush` | `NfcAccentBorderBrush` | `NfcAccentStrongBrush` |
| `actionGhost` | checked_hover | `NfcAccentSurfaceSubtleBrush` | `NfcAccentBorderStrongBrush` | `NfcAccentStrongBrush` |
| `actionGhost` | checked_pressed | `NfcSecondaryActionPressedBrush` | `NfcAccentBorderStrongBrush` | `NfcAccentStrongBrush` |
| `chipStatus` | rest / disabled | `NfcSurfaceSubtleBrush` | `NfcBorderMutedBrush` | `NfcTextSecondaryBrush` |
| `chipStatus.warning` | rest / disabled | `NfcWarningSurfaceBrush` | `NfcWarningBorderBrush` | `NfcWarningTextBrush` |
| `chipStatus.danger` | rest / disabled | `NfcDangerSurfaceBrush` | `NfcDangerBorderBrush` | `NfcDangerTextBrush` |
| `chipStatus.success` | rest / disabled | `NfcSuccessSurfaceBrush` | `NfcSuccessBorderBrush` | `NfcSuccessTextBrush` |

`actionIconButton` 預設採 `actionNeutral`；可與四種色彩角色組合，採其所有狀態。`chipAction` 採中性色角色；`chipAction.active` 與 ToggleButton 的 checked 使用相同選取規則。active／checked 滑入採淡主色底色，按下採中性按下底色，保留強主色文字與強邊界。一般 Button 沒有 checked。`chipStatus` 的 warning／danger／success 使用上表語意底色、文字與邊界；無互動、選取或焦點狀態，停用仍保留資訊顏色與 opacity 1。

停用最優先：所有互動角色採 `NfcSurfaceSubtleBrush`、`NfcBorderMutedBrush`、`NfcTextDisabledBrush`、opacity 1，覆蓋滑入、按下、checked 與 active。ghost 停用也採相同次表面。沒有雙重淡化。

背景、邊界、文字轉場為 120 ms。按下時關閉轉場；primary 按下的內側 2 px 邊線由不占版面的覆蓋層繪製。焦點立即顯示。控制項或任何祖先加 `.reducedMotion` 會關閉轉場。

### 焦點框

| Token | Light | Dark |
|---|---|---|
| `Nvt.Focus.RingBrush` | `#1F6FD1` | `#4DA3FF` |
| `Nvt.Focus.DangerRingBrush` | `#C62828` | `#FF6B6B` |

`Nvt.Focus.RingThickness` 為 Thickness 2，三個焦點 token 全在 `ThemeTokens.axaml`。每個互動角色預設 `FocusAdorner=null`，取消預設矩形。只有 `:focus-visible` 顯示 Border adorner；真實 Tab 顯示，指標焦點不顯示。`Margin=-4` 形成 2 px 線與 2 px 外側間隙；`IsHitTestVisible=False`、`AdornerLayer.IsClipEnabled=False`。圓角隨控制項動態變化：膠囊保留 999，矩形每個角加 4。`actionDanger` 文字按鈕及 `actionIconButton.actionDanger` 採紅框；chip（含 `chipAction.actionDanger`）與其餘角色採藍框。樣式資源 `Nvt.Focus.RingRadiusConverter` 計算焦點框圓角。焦點在靜止、滑入、checked、active 均不改底色、邊界或文字。

## 相較 core-v0.1.0 的重大變更

Core 0.2.0 引入共用色票與按鈕角色；本節為其版本發布說明。下列 class、selector 與資源曾隨 `core-v0.1.0` 發布，現在移除或取代，不提供相容別名。

| 0.1.0 移除或取代的項目 | 0.2.0 替代方式 |
|---|---|
| `Button.semanticAction` | `actionNeutral` |
| `Button.semanticAction.command` | `actionGhost` |
| `Button.secondary` | `actionNeutral` |
| `Button.primary` | `actionPrimary` |
| `Button.action` | `actionPrimary` |
| `Button.danger` | `actionDanger` |
| `Button.command` | `actionGhost` |
| `Button.breadcrumb` | `actionGhost` |
| `Button.browseAction` | `actionNeutral` |
| `Button.iconButton` | `actionIconButton` |
| `Button.iconButton.codeBlockCopy` | `actionIconButton` |
| `Button.inlineEdit` | `actionIconButton` 加 `actionGhost` |
| `Button.summaryChip` | `chipAction` |
| `Button.closeButton` | Core 無替代項；須由工具定義 |
| `Button.fileRevealAction` | Core 無替代項；須由工具定義 |
| `Button.railAction`（含 `.reducedMotion`） | Core 無 rail 角色；須由工具定義。`.reducedMotion` 仍適用於所有共用角色 |
| `Button.primaryRailAction` | Core 無替代項；須由工具定義 |
| `railActionIcon`, `railActionLabel`, `railActionIconSlot` | Core 無替代項；須由工具定義 |
| 資源 `NfcSemanticButtonTheme` (ControlTheme) | 無替代資源；改用共用角色 class |
| 全域 `Button` rule (theme and null `FocusAdorner`) | 已移除；加上角色 class。未指定角色的按鈕回到 Fluent，包含 Fluent 焦點視覺 |
| 對話框 class `confirmDialogActionButton` (`DialogsStyles.axaml`) | `actionNeutral` 或 `actionPrimary` |
| `Button.danger Path.confirmDialogDangerIcon` selector | `Button.actionDanger Path.confirmDialogDangerIcon` |
| 面板標頭專用範本、`PART_ContentPresenter`、12,10 內距、10,10,0,0 圓角及面板根部裁切 | `actionGhost` 角色；先載入 `ButtonStyles.axaml`，再載入 `PanelsStyles.axaml`。共用範本採 14,0 內距與膠囊圓角；面板根部不再裁切外側焦點框 |

`railAction`、`primaryRailAction`、`closeButton`、`fileRevealAction` 等產品角色及 rail 圖示／標籤／插槽版面必須由各工具自行定義。全域 `Button` 規則已移除：未指定角色 class 的按鈕回到 Fluent，包含 Fluent 自己的焦點視覺。

既有 `ThemeTokens.axaml` 鍵及型別均未移除或改名。下表摘要 token 與行為變更；目前數值詳見前面的色票、主色與尺寸表。

| 區域 | 相較 0.1.0 的變更 |
|---|---|
| 色票 | Light／Dark 的中性表面、狀態色、文字、邊界、語意色與 NFC 七個主色值改採共用色票。保留既有名稱，包括 caution、critical 與 success-emphasis 別名 |
| 尺寸與間距 | 角色按鈕高 32 px，內距 14,0；圖示按鈕 32 × 32，內距為零。對話框動作原為 36/40 px。`NfcFieldSpacing` 由 3 改為 4；`NfcSpace2/4/8/12/16/24` 數值保留 |
| 圓角 | Token 數值維持 `NfcCompactCornerRadius` = 6、`NfcSurfaceCornerRadius` = 8、`NfcPillCornerRadius` = 999。共用按鈕及面板標頭採膠囊圓角；矩形焦點框各角為控制項圓角加 4 |
| 焦點 | 共用角色以僅鍵盤顯示的 2 px 線與 2 px 間隙取代停用的預設 adorner。Light／Dark 藍色為 `#1F6FD1` / `#4DA3FF`，危險紅色為 `#C62828` / `#FF6B6B`；危險 chip 保留藍框。未指定角色的按鈕保留 Fluent 焦點視覺 |
| 新 token 鍵 | `NfcControlHeight` = 32；`Nvt.Focus.RingBrush`、`Nvt.Focus.DangerRingBrush`、`Nvt.Focus.RingThickness` = Thickness 2；Light／Dark 的 `Nvt.Button.PrimaryLabelBrush` = `#FFFFFF` / `#0B1220` |
| 新樣式資源 | `Nvt.Focus.RingRadiusConverter` 跟隨控制項各角；色彩、尺寸與圓角 token 均透過 `DynamicResource` 參照 |

## 角色遷移

逐項依三個核准 `role_mapping` 區段列出 NFC 59、NFH 105、NFU 22 個來源 class／組合。product role 的行為與版面留在工具；共同基本外觀由 Core 角色組合提供。

| 工具 | 目前 class | 共用角色或保留位置 |
|---|---|---|
| NFC | `action` | `actionPrimary` |
| NFC | `activityFilter` | 產品角色，留在工具內 |
| NFC | `bankScope` | 產品角色，留在工具內 |
| NFC | `bankViewSwitch` | 產品角色，留在工具內 |
| NFC | `breadcrumb` | `actionGhost` |
| NFC | `browseAction` | `actionNeutral` |
| NFC | `closeButton` | 產品角色，留在工具內 |
| NFC | `command` | `actionGhost` |
| NFC | `configSelected` | 產品角色，留在工具內 |
| NFC | `danger` | `actionDanger` |
| NFC | `fileRevealAction` | 產品角色，留在工具內 |
| NFC | `hexApplyChange` | `actionNeutral` |
| NFC | `hexAsciiSearch` | `actionIconButton` + `actionGhost` |
| NFC | `hexChangedBlockNavigator` | 產品角色，留在工具內 |
| NFC | `hexChangedBlockRow` | 產品角色，留在工具內 |
| NFC | `hexGoToAddress` | `actionIconButton` + `actionGhost` |
| NFC | `hexInspectorAction` | `actionIconButton` |
| NFC | `iconButton` | `actionIconButton` |
| NFC | `iconButton.codeBlockCopy` | `actionIconButton` |
| NFC | `inlineDisclosure` | 產品角色，留在工具內 |
| NFC | `inlineEdit` | `actionIconButton` + `actionGhost` |
| NFC | `launcher-action` | `actionNeutral` |
| NFC | `launcher-close` | 產品角色，留在工具內 |
| NFC | `launcher-icon` | `actionIconButton` + `actionGhost` |
| NFC | `launcher-primary` | `actionPrimary` |
| NFC | `messageCenterNavigationItem` | 產品角色，留在工具內 |
| NFC | `nav` | 產品角色，留在工具內 |
| NFC | `outputNameEdit` | 產品角色，留在工具內 |
| NFC | `outputRailAction` | 產品角色，留在工具內 |
| NFC | `primary` | `actionPrimary` |
| NFC | `primaryRailAction` | 產品角色，留在工具內 |
| NFC | `quietDisclosure` | 產品角色，留在工具內 |
| NFC | `railAction` | 產品角色，留在工具內 |
| NFC | `railAction.reducedMotion` | 產品角色，留在工具內 |
| NFC | `referenceChoice` | 產品角色，留在工具內 |
| NFC | `reportListRow` | 產品角色，留在工具內 |
| NFC | `reportLoad` | `actionNeutral` |
| NFC | `secondary` | `actionNeutral` |
| NFC | `segment` | 產品角色，留在工具內 |
| NFC | `semanticAction` | `actionNeutral` |
| NFC | `semanticAction.command` | `actionGhost` |
| NFC | `semanticAction.command.configSelected` | 產品角色，留在工具內 |
| NFC | `settingsNavItem` | 產品角色，留在工具內 |
| NFC | `settingsNavItem.selected` | 產品角色，留在工具內 |
| NFC | `slotClearAction` | 產品角色，留在工具內 |
| NFC | `slotStateAction` | `chipAction` |
| NFC | `slotStateAction.checking` | `chipAction` |
| NFC | `slotStateAction.error` | `chipAction` |
| NFC | `slotStateAction.inspected` | `chipAction` |
| NFC | `slotStateAction.notApplicable` | `chipAction` |
| NFC | `slotStateAction.pendingInput` | `chipAction` |
| NFC | `slotStateAction.verified` | `chipAction` |
| NFC | `slotStateAction.warning` | `chipAction` |
| NFC | `sourceEditButton` | 產品角色，留在工具內 |
| NFC | `summaryChip` | `chipAction` |
| NFC | `versionChoice` | 產品角色，留在工具內 |
| NFC | `versionInstallAction` | 產品角色，留在工具內 |
| NFC | `versionReleaseNotesAction` | 產品角色，留在工具內 |
| NFC | `versionTableAction` | 產品角色，留在工具內 |
| NFH | `actionButton` | `actionNeutral` |
| NFH | `actionChip` | `chipAction` |
| NFH | `actionDanger` | `actionDanger` |
| NFH | `actionGhost` | `actionGhost` |
| NFH | `actionIconButton` | `actionIconButton` |
| NFH | `actionIconButton.actionDanger` | `actionIconButton` + `actionDanger` |
| NFH | `actionIconButton.actionGhost` | `actionIconButton` + `actionGhost` |
| NFH | `actionIconButton.actionNeutral` | `actionIconButton` + `actionNeutral` |
| NFH | `actionIconButton.actionPrimary` | `actionIconButton` + `actionPrimary` |
| NFH | `actionIconButton.consoleHeaderAction` | 產品角色，留在工具內 |
| NFH | `actionIconButton.panelChromeToggle` | 產品角色，留在工具內 |
| NFH | `actionIconButton.viewportOverlayAction` | 產品角色，留在工具內 |
| NFH | `actionNeutral` | `actionNeutral` |
| NFH | `actionPrimary` | `actionPrimary` |
| NFH | `actionTextButton` | `actionNeutral` |
| NFH | `actionTextButton.actionDanger` | `actionDanger` |
| NFH | `actionTextButton.actionGhost` | `actionGhost` |
| NFH | `actionTextButton.actionNeutral` | `actionNeutral` |
| NFH | `actionTextButton.actionPrimary` | `actionPrimary` |
| NFH | `actionTextButton.workspaceActionButton` | `actionNeutral` |
| NFH | `chipAction` | `chipAction` |
| NFH | `chipAction.active` | `chipAction.active` |
| NFH | `chipAction.combined` | 產品角色，留在工具內 |
| NFH | `chipAction.direct` | 產品角色，留在工具內 |
| NFH | `chipAction.duplicate` | 產品角色，留在工具內 |
| NFH | `chipAction.geometry` | 產品角色，留在工具內 |
| NFH | `chipAction.hidden` | 產品角色，留在工具內 |
| NFH | `chipAction.incoming` | 產品角色，留在工具內 |
| NFH | `chipAction.layer` | 產品角色，留在工具內 |
| NFH | `chipAction.legacy` | 產品角色，留在工具內 |
| NFH | `chipAction.linked` | 產品角色，留在工具內 |
| NFH | `chipAction.nocad` | 產品角色，留在工具內 |
| NFH | `chipAction.outgoing` | 產品角色，留在工具內 |
| NFH | `chipAction.transfer` | 產品角色，留在工具內 |
| NFH | `chipStatus` | `chipStatus` |
| NFH | `chipStatus.danger` | `chipStatus.danger` |
| NFH | `chipStatus.success` | `chipStatus.success` |
| NFH | `chipStatus.warning` | `chipStatus.warning` |
| NFH | `confirmDialogActionButton` | `actionNeutral` |
| NFH | `confirmDialogDangerButton` | `actionDanger` |
| NFH | `consoleHeaderAction` | 產品角色，留在工具內 |
| NFH | `danger` | `actionDanger` |
| NFH | `dangerTextButton` | `actionDanger` |
| NFH | `dangerTextButton.dxfEditMiniAction` | `actionIconButton` + `actionDanger` |
| NFH | `dxfEditCompactIconAction` | `actionIconButton` |
| NFH | `dxfEditMiniAction` | `actionIconButton` |
| NFH | `dxfEditSummaryChip` | 產品角色，留在工具內 |
| NFH | `dxfLayerBulkToggleButton` | 產品角色，留在工具內 |
| NFH | `icon` | 產品角色，留在工具內 |
| NFH | `icon.ghost` | 產品角色，留在工具內 |
| NFH | `icon.ghost.workflowOverviewAction` | 產品角色，留在工具內 |
| NFH | `icon.ghost.workflowStepAction` | 產品角色，留在工具內 |
| NFH | `iconTextButton` | `actionNeutral` |
| NFH | `notchExportRestoreHintAction` | 產品角色，留在工具內 |
| NFH | `padInfoActionButton` | `actionNeutral` |
| NFH | `padInfoActionButton.compact` | `actionNeutral` |
| NFH | `padInfoAllocationButton` | 產品角色，留在工具內 |
| NFH | `padInfoHeaderActionButton` | `actionGhost` |
| NFH | `padInfoInfoButton` | `actionIconButton` + `actionGhost` |
| NFH | `padInfoOverrideIconButton` | `actionIconButton` |
| NFH | `padInfoSectionToggle` | 產品角色，留在工具內 |
| NFH | `padInfoSectionToggle.debugCard` | 產品角色，留在工具內 |
| NFH | `panelBlockHeader` | 產品角色，留在工具內 |
| NFH | `panelChromeToggle` | 產品角色，留在工具內 |
| NFH | `rightPanelBrowserTab` | 產品角色，留在工具內 |
| NFH | `rightPanelTab` | 產品角色，留在工具內 |
| NFH | `settingsNavItem` | 產品角色，留在工具內 |
| NFH | `shellTab` | 產品角色，留在工具內 |
| NFH | `shellTab.settingsWindowNavTab` | 產品角色，留在工具內 |
| NFH | `simulationActionButton` | `actionPrimary` |
| NFH | `validationCountPill` | 產品角色，留在工具內 |
| NFH | `validationRowKindTag` | 產品角色，留在工具內 |
| NFH | `validationRowKindTag.direct` | 產品角色，留在工具內 |
| NFH | `validationRowKindTag.incoming` | 產品角色，留在工具內 |
| NFH | `validationRowKindTag.legacy` | 產品角色，留在工具內 |
| NFH | `validationRowKindTag.nocad` | 產品角色，留在工具內 |
| NFH | `validationRowKindTag.outgoing` | 產品角色，留在工具內 |
| NFH | `validationRowKindTag.workspaceContextBadge` | 產品角色，留在工具內 |
| NFH | `validationTraceRow` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip.active` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip.direct` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip.incoming` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip.legacy` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip.nocad` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip.outgoing` | 產品角色，留在工具內 |
| NFH | `verificationSummaryChip.transfer` | 產品角色，留在工具內 |
| NFH | `verificationTableRow` | 產品角色，留在工具內 |
| NFH | `verificationTableRow.verificationTableRowActive` | 產品角色，留在工具內 |
| NFH | `viewMenuButton` | `actionNeutral` |
| NFH | `viewportOverlayAction` | 產品角色，留在工具內 |
| NFH | `workspaceActionButton` | `actionNeutral` |
| NFH | `workspaceDataRow` | 產品角色，留在工具內 |
| NFH | `workspaceDataRow.workspaceDataRowActive` | 產品角色，留在工具內 |
| NFH | `workspaceFoldHeader` | 產品角色，留在工具內 |
| NFH | `workspaceGroupActionButton` | `actionNeutral` |
| NFH | `workspaceHeaderAction` | `actionGhost` |
| NFH | `workspaceHeaderInspectorCard` | 產品角色，留在工具內 |
| NFH | `workspaceInfoButton` | `actionIconButton` + `actionGhost` |
| NFH | `workspaceSearchClearButton` | `actionIconButton` + `actionGhost` |
| NFH | `workspaceSubtleAction` | `actionGhost` |
| NFH | `workspaceTableHeaderButton` | 產品角色，留在工具內 |
| NFH | `workspaceTableHeaderButton.active` | 產品角色，留在工具內 |
| NFH | `workspaceTextAction` | `actionGhost` |
| NFH | `workspaceToggleChip` | `chipAction` |
| NFU | `axisToggle` | 產品角色，留在工具內 |
| NFU | `canvasIcon` | 產品角色，留在工具內 |
| NFU | `canvasQuick` | 產品角色，留在工具內 |
| NFU | `canvasQuick.active` | 產品角色，留在工具內 |
| NFU | `ghost` | `actionGhost` |
| NFU | `headerAction` | `actionGhost` |
| NFU | `headerAction.headerPrimary` | `actionPrimary` |
| NFU | `headerIcon` | `actionIconButton` + `actionGhost` |
| NFU | `inspectorIcon` | `actionIconButton` + `actionGhost` |
| NFU | `outputPreviewPlay` | 產品角色，留在工具內 |
| NFU | `outputPreviewPlay.playing` | 產品角色，留在工具內 |
| NFU | `paintMarkerRemove` | `actionIconButton` + `actionDanger` |
| NFU | `primary` | `actionPrimary` |
| NFU | `rawSourceOpen` | `actionGhost` |
| NFU | `settingsChoice` | 產品角色，留在工具內 |
| NFU | `settingsNav` | 產品角色，留在工具內 |
| NFU | `settingsNav.active` | 產品角色，留在工具內 |
| NFU | `shortcutRow` | 產品角色，留在工具內 |
| NFU | `sourceLineLink` | `actionGhost` |
| NFU | `toolbarAction` | `actionNeutral` |
| NFU | `transportPlay` | 產品角色，留在工具內 |
| NFU | `transportPlay.playing` | 產品角色，留在工具內 |

舊 Core 的 `railActionIcon`、`railActionLabel`、`railActionIconSlot` 與 `NfcSemanticButtonTheme` 及僅供舊角色的 theme 資源也離開 Core，屬於產品角色，留在工具內。`codeBlockCopy` 對應 `actionIconButton`；`primaryRailAction` 與 `fileRevealAction` 是產品角色。工具須移除舊檔載入與舊版面 class；本次不提供相容別名。

Core 使用處：ConfirmDialog 的確認採 primary、取消採 neutral、強調取消改為 danger；WarningDialog 的 OK 採 primary；CollapsiblePanel 的 header 採 ghost，保留展開繫結、stretch 內容與 chevron 方向。panel root 不裁切外側焦點框。

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

## 驗證與來源

本次取代 NFC 衍生色票與舊按鈕角色，合併 PR #71 的範本與焦點方案到唯一的 `ButtonStyles.axaml`，修正停用與 active 疊加。`ThemeTokens.xml`、`ButtonStyles.xml` 刻意依新的核准檔案重新產生，不再宣稱保留舊 NFC 外觀。`ExtractedXamlMatchesFrozenBaseline` 仍鎖定完整 XML；token 比對先展開八個既有字型別名。捲軸幾何與 baseline 不因本次色票調整而變動。

舊 NFC 抽取與字型的凍結來源為 `origin/1.2.x`、commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。PR #71 的角色來源固定於 `0f29143712cc18127df8e00636b1bfb3552d3aef`；其基準不沿用。2026-10-06 核准的共用提案與 NFH 命名決策是本次規格。

- `ButtonThemeTests`：僅載入 ThemeTokens／ButtonStyles；Light／Dark、兩種控制項、每個角色與狀態、幾何、單行／省略／裁切、未指定角色、真實 Tab 與指標焦點、焦點 brush／幾何／命中測試、四種狀態焦點不改色、停用優先、減少動態、應用程式範圍採用覆寫，以及全部七鍵的主色覆寫。
- `PaletteContrastTests`：編譯後 token，三工具主色同一資料驅動測試；內文、提示、語意文字、主色文字與填色標籤 4.5:1；停用文字、輸入邊界、焦點、捲軸拇指 3:1。檢查全部不透明中性／語意／主色底色，無例外測試。裝飾邊界與半透明 scrim 不承載文字。
- `ThemeContractTests`：所有 Theme 檔的 StaticResource／DynamicResource 在兩個主題均解析；只允許有角色的按鈕 selector；保留三份 XML baseline 比對。
- `NfcLegacyFontTests` 與資源解析器測試不變；Dialogs／Panels 的測試改為共用角色，其他行為合約不變。

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

圖庫使用 Fluent、ThemeTokens、ButtonStyles、ScrollStyles，於 `artifacts/palette-gallery/roles-dark.png`、`roles-light.png`、`controls-dark.png`、`controls-light.png` 並排比較固定狀態。這些圖片補充真實輸入測試。

### 採用

每套工具分成兩個獨立 PR（擁有者 2026-10-07 決定，取代 2026-10-06「先零差異再換色」）：

1. 套件 PR：把 Core 套件釘到新版本，畫面不變。
2. 外觀 PR，經擁有者核准：一次導入 `ThemeTokens`、`ButtonStyles` 與 `ScrollStyles`。只保留工具主色的七個 `NfcAccent*` 鍵，必須設定全部七個。把按鈕改用 Core 角色，並刪除功能相同的本地樣式。不在 Core 加相容變體來還原舊外觀。前後對照影像涵蓋每個主要畫面的 Light 與 Dark。

影像涵蓋 Light／Dark、英文／繁中、各互動狀態、DPI 與長文字。產品行為與資料色留在工具。

## Expander

內容分隔線與標頭相隔 6 DIP，在焦點環向外延伸的 4 DIP 之外保留 2 DIP；向上展開時採鏡像間距。


1 DIP 淡邊框與表面底色將標頭和內容組成同一區塊。容器圓角為 Pill 8、Square 6 DIP。
容器 padding 6 保護外側焦點框。標頭 padding 為 12,0；內容為 24,12,12,12。
1 DIP 分隔線跟隨標頭與內容交界，向上展開時換邊。巢狀兄弟項目建議間距 8 DIP。
Pill 標頭在展開後仍為膠囊。section 不再用穿過焦點框的頂線。

在 Fluent 後載入 `Theme/ExpanderStyles.axaml`，並將 `ThemeTokens.axaml` 合併至應用程式資源。
一般 `Expander` 標頭高 32 DIP。加上 `section` 後，標頭高 44 DIP，使用 SemiBold 字重。
標頭預設無邊框，圓角跟隨共用 Pill 或 Square 形狀。
12 × 6 箭頭、20 DIP 箭頭容器與 10 DIP 間距均與 `CollapsiblePanel` 一致。
樣式支援 `ExpandDirection` 的 Down 與 Up，不處理 Left 與 Right。
`CollapsiblePanel` 保留原有模板與行為。

靜止與展開標頭使用 `NfcSurfaceSubtleBrush`（Soft 靜止填色模式；見[靜止填色設定](#靜止填色設定)）。滑過使用 `NfcSelectionSurfaceBrush`；按下使用 `Nvt.Controls.ExpanderPressedBrush`。
停用標頭使用 `Nvt.Controls.ExpanderDisabledForegroundBrush`，並忽略滑過、按下與焦點視覺。
鍵盤焦點顯示一個兩 DIP 焦點框，與控制項外緣相隔兩 DIP。滑鼠焦點不顯示焦點框。
向下或向上展開時，內容分別位於標頭下方或上方。箭頭隨方向及展開狀態旋轉。
顏色與箭頭轉場持續 150 毫秒。在控制項或祖先加上 `reducedMotion` 可停用轉場。
Avalonia 保留 Space 切換、存取鍵、展開事件與無障礙名稱。

| Token | Light | Dark |
| --- | --- | --- |
| `NfcControlHeight` | 32 | 32 |
| `Nvt.Expander.SectionHeaderHeight` | 44 | 44 |
| `NfcSelectionSurfaceBrush` | `#E8EEF5` | `#1E293B` |
| `Nvt.Controls.ExpanderPressedBrush` | `#CBD5E1` | `#29384D` |
| `NfcTextBrush` | `#1E293B` | `#E2E8F0` |
| `Nvt.Controls.ExpanderPressedForegroundBrush` | `#0B1220` | `#FFFFFF` |
| `Nvt.Controls.ExpanderDisabledForegroundBrush` | `#637085` | `#91A1B9` |
| `Nvt.Divider.TransparentBrush` | `#00FFFFFF` | 相同 |

標頭使用 `Nvt.Shape.ControlCornerRadius`：兩種主題下，Pill 為 999、Square 為 6。
焦點框使用 `Nvt.Shape.FocusCornerRadius`：兩種主題下，Pill 為 999、Square 為 10。
分隔線與焦點框顏色使用下方記載的共用 token。
採用時移除本機標頭模板、高度、滑鼠移入邊框、圓角規則與焦點裝飾。
標頭內容、命令、存取鍵與繫結仍由宿主管理。

```xml
<Expander Header="Details" />
<Expander Classes="section" Header="Advanced options" ExpandDirection="Up" />
```

## ProgressBar

在 Fluent 後載入 `Theme/ProgressStyles.axaml`。樣式保留 Fluent 的範圍投影、百分比文字與不確定進度動畫。
預設粗細為六 DIP。加上 `thin` 為三 DIP，`thick` 為十 DIP。
確定與不確定進度軌道維持相同粗細。垂直進度列將粗細套用至寬度。
`ShowProgressText` 預設為 false。呼叫端仍管理範圍、數值、文字設定、可見性與不確定模式。
停用進度列保留軌道，指示器改用停用 token。
筆刷轉場持續 150 毫秒。

`ProgressIndicator` 保留公開 API 與 `ProgressBar` 樣式身分。
既有 `Progress` 屬性仍將已知比例投影至 `Value`；缺少比例時保留最後數值。
控制項透過既有樣式鍵取得這些 token，不需要另一套元件模板或 selector。
使用 `LoadingSurface` 時仍保留 `Progress/ProgressStyles.axaml`。該元件樣式檔服務不同用途。

在進度列或祖先加上 `reducedMotion`，可將不確定動畫替換為位於軌道中間三分之一的靜止指示器。
離開不確定模式後，恢復原生確定進度模板。減少動作也會停用筆刷轉場。
靜止模板保留原生必要指示器部件與進度無障礙 peer。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Progress.Height` | 6 | 6 |
| `Nvt.Progress.ThinHeight` | 3 | 3 |
| `Nvt.Progress.ThickHeight` | 10 | 10 |
| `Nvt.Progress.TrackBrush` | `#CBD5E1`, `NfcBorderMutedBrush` | `#334155`, `NfcBorderMutedBrush` |
| `Nvt.Progress.IndicatorBrush` | `#1557E9`, `NfcAccentBrush` | `#5FA5FA`, `NfcAccentBrush` |
| `Nvt.Progress.DisabledIndicatorBrush` | `#68778C`，`NfcTextDisabledBrush` | `#7B8CA5`，相同別名 |

較淡軌道使用 `NfcBorderMutedBrush`，指示器使用 `NfcAccentBrush`。
內部轉換器將 `Nvt.Progress.CornerRadius` 限制為短邊的一半。
Pill 端點為 1.5、3、5 DIP；Square 在各粗細皆為 1 DIP。
軌道、確定進度、兩個動畫指示器與減少動作指示器均使用相同規則。
替換 `Nvt.Progress.*` 色盤字典，可同步更新已附加的進度列與指示器。
採用時移除本機軌道色、指示器色、粗細、圓角與衝突模板。

```xml
<ProgressBar Value="42" />
<ProgressBar Classes="thin" IsIndeterminate="True" />
<ProgressBar Classes="thick reducedMotion" IsIndeterminate="True" />
```

## Separator

分隔線屬於裝飾，不受形狀影響。預設改用較淡的 `NfcDividerBrush`；strong 維持原值。

在 Fluent 後載入 `Theme/DividerStyles.axaml`。
`Separator` 與 `Border.divider` 顯示相同的一 DIP 線，預設不留 margin。
預設為水平線。加上 `vertical` 改為垂直線；加上 `strong` 改用較強邊框色。
分隔線維持非互動控制項，並保留原生無障礙行為。
選單內的分隔線由清單與選單樣式負責，使用該樣式自己的 margin 與顏色。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Divider.LineThickness` | 1 | 1 |
| `NfcDividerBrush` | `#E2E8F0` | `#273449` |
| `NfcBorderBrush` | `#718096` | `#708198` |

移除本機分隔線背景、粗細與預設 margin 規則。呼叫端配置間距保留在控制項外部。
原生分隔線與一般 divider 邊框使用相同類別。

```xml
<Separator />
<Separator Classes="vertical strong" />
<Border Classes="divider" />
<Border Classes="divider vertical strong" />
```

## GridSplitter

垂直 splitter 中央新增 4 × 24 DIP 握柄，水平版為 24 × 4 DIP。
`Nvt.GridSplitter.GripCornerRadius` 為 Pill 2、Square 1。靜止握柄使用 `NfcBorderBrush`。
滑過使用 `NfcAccentBrush`，按下使用 `NfcAccentStrongBrush`。點擊區域維持 6 DIP。

在 Fluent 後載入 `Theme/DividerStyles.axaml`。
預設垂直分隔線提供六 DIP 點擊區域，中央顯示一 DIP 線。
加上 `vertical` 調整欄寬，或 `horizontal` 調整列高。明確指定 `ResizeDirection="Rows"` 也會選用水平外觀。
滑過顯示兩 DIP 主色線；拖曳改用三 DIP 強主色線，指標離開目標後仍保留。
停用時線條與握柄均改用較淡邊框色，並抑制互動視覺。
鍵盤焦點顯示一個兩 DIP 焦點框，與控制項外緣相隔兩 DIP。滑鼠焦點不顯示焦點框。
游標跟隨調整方向。Avalonia 保留拖曳、方向鍵調整與尺寸限制。
設定 `ShowsPreview="True"` 時，拖曳會顯示 50% 透明的主色預覽條（`Nvt.GridSplitter.PreviewBrush`、`Nvt.GridSplitter.PreviewOpacity`），放開指標後才調整相鄰區域。
顏色轉場持續 150 毫秒。在 splitter 或祖先加上 `reducedMotion` 可停用轉場。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.GridSplitter.HitSize` | 6 | 6 |
| `Nvt.Divider.LineThickness` | 1 | 1 |
| `Nvt.GridSplitter.ActiveLineThickness` | 2 | 2 |
| `Nvt.GridSplitter.PreviewBrush` | `NfcAccentBrush` | `NfcAccentBrush` |
| `Nvt.GridSplitter.PreviewOpacity` | 0.5 | 0.5 |
| `NfcBorderSoftBrush` | `#94A3B8` | `#475569` |
| `NfcAccentBrush` | `#1557E9` | `#5FA5FA` |
| `Nvt.Focus.RingBrush` | `#1F6FD1` | `#4DA3FF` |
| `Nvt.Focus.RingThickness` | 2 | 2 |

兩種方向使用 `Nvt.GridSplitter.FocusCornerRadius`：Pill 999、Square 2。
採用時移除本機 splitter 模板、寬度、滑鼠移入填色、游標與焦點裝飾。
Grid 位置、調整行為、拖曳增量、鍵盤增量與預覽設定仍由宿主管理。

```xml
<GridSplitter Classes="vertical" Grid.Column="1" ResizeBehavior="PreviousAndNext" />
<GridSplitter Classes="horizontal" Grid.Row="1" ResizeBehavior="PreviousAndNext" />
```

`DividerStylesRenderer` 以 headless 方式檢查配置，僅在設定 `NVT_DIVIDER_IMAGES_DIR` 時寫入圖片。
輸出 `divider-light.png`、`divider-dark.png`、`divider-square-light.png` 與左右並排的 `divider-before-light.png`。
每張圖片寬 1200 像素，縮放為一，檔案小於一 MB。
Divider 測試固定幾何、所有標頭狀態、對比、執行期形狀、token 替換、動作政策、無障礙名稱與原生鍵盤行為。
實際指標拖曳驗證兩側相鄰格均依拖曳距離調整。

既定樣式在兩種形狀下均達到以下最低對比。
指示器檢查涵蓋啟用進度列。焦點框檢查涵蓋相鄰表面、應用程式背景與選取表面。

| Contrast | Light | Dark |
| --- | --- | --- |
| 啟用標頭各狀態的文字 | 12.525:1 | 11.866:1 |
| 停用標頭文字 | 4.794:1 | 5.997:1 |
| 進度指示器對軌道 | 3.960:1 | 4.067:1 |
| 焦點框對相鄰表面 | 4.228:1 | 5.572:1 |

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
- 測試專案為這些檢查參考 `Avalonia.Themes.Fluent` 12.1.1。捲軸測試與暫存色票圖庫在各自的視窗載入 `FluentTheme`。套件不增加任何相依。
- NFC 規則的同樣 7 個執行期案例，對完整 NFC 凍結檔的暫存副本也都通過。副本拿掉了 3 個選取 NFC view 型別的樣式，因為 Core 無法編譯它們。副本未保留。

採用共用色票：

- NFC 把第 117-185 行換成上面的 include，位置不變。幾何維持相同，共用色票改變 thumb 顏色。執行 UI smoke 測試，以相同作業系統、字型、DPI 與主題附上採用前後影像供 owner 審查。
- NFH 的外觀與行為會改變。NFH 的外觀 PR 須附前後截圖，由 owner 核准外觀改變。非 UI 測試的清單與結果須相同。
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

## ListBox 與下拉選單項目

`ListStyles.axaml` 讓 `ListBoxItem` 與 `ComboBoxItem` 共用外觀。
ListBox 容器沒有背景、框線與 padding，由應用程式提供周圍表面。
項目最小高度為 32 DIP，padding 為 10,5。
在 ListBox 或個別項目加上 `compact`，即可使用高度 24 DIP、padding 10,0 的密集項目。
ComboBox 本體保留原有主題與範本。

### 清單狀態與 token

靜止時透明，滑鼠移入使用 `NfcSelectionSurfaceBrush`，按下使用 `NfcSecondaryActionPressedBrush`。
選取項目使用柔和主色表面、主色文字與 2 × 12 DIP 主色標記。標記距左緣 4 DIP，與文字之間留 4 DIP。
多重選取使用相同外觀。
停用項目維持 opacity 1，並使用 `NfcTextDisabledBrush`。
停用且選取時使用 `NfcSelectionSurfaceBrush`。
鍵盤焦點顯示一個 2 DIP 框，向內縮 2 DIP，避免捲動時裁切。
滑鼠焦點不顯示框，焦點也不改變清單項目顏色。

List 別名與 Choice 列、勾選 MenuItem、`toggleSoft` 共用 `Nvt.Controls.Selected*` 資源。
同時覆寫 `Nvt.List.Selected*` 資源，即可在執行時替換選取色票。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.List.SelectedBrush` | `#EFF3FD`, `Nvt.Controls.SelectedBrush` | `#1A2940`, `Nvt.Controls.SelectedBrush` |
| `Nvt.List.SelectedPointerOverBrush` | `#E3ECFC`, `Nvt.Controls.SelectedPointerOverBrush` | `#20334F`, `Nvt.Controls.SelectedPointerOverBrush` |
| `Nvt.List.SelectedPressedBrush` | `#DCE7FA`, `Nvt.Controls.SelectedPressedBrush` | `#243C5B`, `Nvt.Controls.SelectedPressedBrush` |
| `Nvt.List.SelectedLabelBrush` | `#0E3C9E`, `Nvt.Controls.SelectedForegroundBrush` | `#BEDAFF`, `Nvt.Controls.SelectedForegroundBrush` |
| `Nvt.List.TransparentBrush` | `#00FFFFFF`，`Nvt.Toggle.TransparentBrush` | 相同 |
| `Nvt.List.CompactHeight` | 24 | 24 |

項目使用 `Nvt.Shape.ControlCornerRadius`：Pill 為 999，Square 為 6。
標記使用 `Nvt.Shape.RoundCornerRadius`。
焦點使用 `Nvt.Controls.FocusBrush` 與 `Nvt.Focus.RingThickness`。
`Nvt.List.FocusCornerRadius` 為 Pill 999、Square 4。
顏色轉場持續 150 ms，項目或上層的 `reducedMotion` 類別可停用轉場。

## Menu、MenuItem、ContextMenu 與選單分隔線

`MenuStyles.axaml` 提供選單列、彈出命令、右鍵選單與選單分隔線。
選單項目點擊範圍高 32 DIP，padding 為 10,0。
有色表面完整高 32 DIP。焦點框寬 2 DIP、內縮 2 DIP，與 List 相同。
內縮避免捲動裁切；Square 焦點圓角為 4 DIP。

選單表面使用 `NfcSurfaceBrush`、1 DIP 的 `NfcBorderBrush` 框線與 padding 4。
表面圓角跟隨共用形狀，並以現有表面圓角 token 為上限。
Pill 表面圓角為 8 DIP，Square 為 6 DIP。
表面四周預留透明陰影空間，彈出位置的 offset 會補償這段空間。

### 選單狀態與 token

滑鼠移入、原生選單選取與鍵盤焦點使用 `NfcSelectionSurfaceBrush`。
按下使用 `NfcSecondaryActionPressedBrush`。
停用內容使用 `NfcTextDisabledBrush`，opacity 維持 1。
勾選項目使用共用選取底色、前景、滑過與按下 token。
12 DIP 勾號置於固定 20 DIP 欄位，與標籤留 8 DIP 間距；圖示保留自己的 20 DIP 欄。
普通 menu-bar 命令省略勾號欄。原生導覽選取與勾選狀態維持獨立。
快捷鍵文字使用 `NfcTextMutedBrush`；勾選項目改用選取前景。子選單保留箭頭。
選單列採用相同項目狀態與高度。

焦點框跟隨 Avalonia 的 `:focus-visible` 狀態。
原生方向鍵導覽保留 Avalonia 的選單選取行為。
樣式保留方向鍵、Enter、Escape、存取鍵、命令與自動化功能。
選單分隔線高 1 DIP，使用 `NfcDividerBrush`，保留 margin 10,4。
舊式 `MenuItem Header="-"` 分隔線也採用相同外觀。
顏色轉場持續 150 ms，`reducedMotion` 也會停用選單轉場。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Menu.PopupShadow` | `0 4 12 0 #26000000` | `0 4 12 0 #66000000` |
| `Nvt.Menu.CheckSize` | 12 | 12 |
| `Nvt.Menu.IconSlotSize` | 20 | 20 |
| `Nvt.Menu.LabelGap` | `0,0,8,0` | `0,0,8,0` |
| `Nvt.Menu.PopupShadowMargin` | 16 | 16 |
| `Nvt.Menu.PopupMaximumCornerRadius` | 8，`NfcSurfaceCornerRadius` | 8，`NfcSurfaceCornerRadius` |
| `Nvt.Menu.ChevronGeometry` | `M1 1 L5 5 L1 9` | 相同 |

陰影透明度色碼定義於 `ListTokens.axaml`；共用淡色定義於 `ControlTokens.axaml`。
樣式中的所有顏色與圓角均來自 token 或所屬控制項。
彈出圓角轉換器為 internal，此控制項家族沒有新增 public C# API。

### 清單與選單採用步驟

1. 將 `ThemeTokens.axaml` 合併到應用程式資源。
2. 在 Fluent 之後、建立控制項之前載入 `ListStyles.axaml` 與 `MenuStyles.axaml`。
3. 移除衝突的本地項目主題、顏色、padding、圓角與焦點裝飾。
4. 保留容器表面、項目內容範本、選取繫結、命令、圖示與無障礙名稱。
5. 檢查項目內的次要文字。項目樣式會把所有子孫 `TextBlock` 的前景綁到項目前景，所以用樣式類別設定的淡色會失效。請改用區域 `Foreground` 值設定淡色文字。
6. 在資源根節點設定共用形狀，並驗證兩種主題。
7. 在真實桌面視窗開啟一次右鍵選單與兩層子選單。彈出陰影需要視窗支援逐像素透明，無頭測試看不出來。

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ListStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/MenuStyles.axaml" />
```

| 採用工具 | 差異與應移除規則 |
| --- | --- |
| NFC | 替換 Fluent 清單選取與主色文字下拉項目覆寫。移除本地 ListBox 與 ComboBoxItem 外觀規則。 |
| NFH | 在原先隱藏選取的工作區項目恢復可見選取。移除這些項目規則、下拉項目覆寫與選單字型覆寫。 |
| NFU | 將主色填滿選取與 34 DIP 下拉項目改為柔和的 32 DIP 項目。移除共用、inspector 與下拉項目外觀規則。 |
| 三個工具 | 替換 Fluent 彈出表面與選單狀態。保留項目產生、導覽、命令與容器表面。 |

### 清單與選單驗證

Headless 測試涵蓋兩種主題與形狀、狀態優先順序、精確尺寸、資源替換、對比與原生鍵盤輸入。
也會驗證捲動、視窗大小與 render scale 改變後，焦點框仍位於項目內。
以下最低對比在兩種形狀下相同。

| 對比 | Light | Dark |
| --- | --- | --- |
| 選取清單文字與標記 | 7.807:1 | 7.830:1 |
| 停用文字 | 3.903:1 | 4.275:1 |
| 焦點框對項目填色 | 5.340:1 | 6.194:1 |

`ListMenuStylesRenderer` 僅在設定 `NVT_LIST_IMAGES_DIR` 時匯出。
輸出 `list-light.png`、`list-dark.png`、`list-square-light.png` 與 `list-before-light.png`。
所有圖片寬 1200 像素、scale 為 1，每張均小於 1 MB。
比較圖使用真正的 Fluent 範本，並排顯示 Core 範本。

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

## ToggleButton

`ToggleStyles.axaml` 提供共用的 FluentPill 切換控制項外觀。
`ThemeTokens.axaml` 已包含其色票與預設 Pill 形狀。
請在 Fluent 之後，與其他 Core 主題樣式一起載入。

| 角色 | 控制項與用途 | 幾何 |
| --- | --- | --- |
| `toggleSegment` | 位於 `Border.toggleSegmentGroup > StackPanel` 內的 `ToggleButton`，用於群組選項。 | 高度 40，水平內距 20。 |
| `toggleSegmentGroup` | 包住分段選項的中性底槽。 | 內距 4、間距 2、高度 48。 |
| `toggleTab` | 導覽用 `ToggleButton`，由宿主提供導覽基線。 | 高度 40，水平內距 20。 |
| `toggleIcon` | 由宿主提供圖示與無障礙名稱的 `ToggleButton`。 | 40 × 40。 |
| `toggleSwitch` | 套用開關模板的 `ToggleButton`。 | 點擊區 58 × 40。 |
| `ToggleSwitch` | 原生控制項不需 class，即使用相同開關外觀。 | 軌道 52 × 28、旋鈕 22 × 22、位移 24。 |

所有尺寸皆為裝置獨立像素。
兩種主題與形狀下，開關旋鈕皆維持圓形與白色。
開關模板只顯示軌道。可見標籤請放在旁邊，並提供無障礙名稱。
宿主負責選取規則、命令與內容。
每個控制項只使用一個切換角色。

### 切換狀態與 danger

以下九種狀態適用實心填色的切換角色。
滑鼠取得焦點時不顯示焦點框。Tab 焦點顯示單一 2 px 焦點框，外側間隙為 2 px。
Space 切換勾選狀態。
筆刷與旋鈕轉場皆為 150 ms。
按下實心填色的 ToggleButton 角色時，繪製縮放為 0.98；原生 ToggleSwitch 維持原尺寸。
配置尺寸與點擊範圍不變。

| 狀態 | Segment、tab 與 icon 填色 | 內容 | 開關軌道 |
| --- | --- | --- | --- |
| Rest | Segment 與 tab 透明；icon 使用 `NfcSurfaceSubtleBrush`。 | `NfcTextSecondaryBrush` | `NfcBorderBrush` |
| Pointer over | Segment 使用 `NfcSurfaceSubtleBrush`；tab 與 icon 使用 `NfcSelectionSurfaceBrush`。 | `NfcTextBrush` | `NfcTextDisabledBrush` |
| Pressed | `NfcSecondaryActionPressedBrush` | `NfcTextStrongBrush` | `NfcBorderBrush` |
| Checked | `Nvt.Toggle.SelectedBrush` | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SwitchOnBrush` |
| Checked pointer over | `Nvt.Toggle.SelectedPointerOverBrush` | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SwitchPointerOverBrush` |
| Checked pressed | `Nvt.Toggle.SelectedPressedBrush` | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SwitchPressedBrush` |
| Disabled | `NfcSurfaceSubtleBrush` | `NfcTextDisabledBrush` | `NfcBorderBrush` |
| Disabled checked | `NfcSelectionSurfaceBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| Keyboard focus | 保留目前填色。 | 保留目前內容顏色。 | 保留目前軌道顏色。 |

對實心填色的切換角色或原生 `ToggleSwitch` 加上 `danger`，啟用且勾選時即顯示紅底白色內容。
勾選後的 pointer over 與 pressed 使用同一個較深紅色。
Danger 控制項使用 `Nvt.Focus.RingBrush`，勾選後的鍵盤焦點也相同。
停用且勾選的控制項維持核准圖片的中性外觀，danger 亦同。
停用控制項忽略 hover、pressed 與 focus 覆寫，透明度維持 1。

### 切換 token

樣式中的每個顏色與圓角都透過資源取得。
以下色票適用兩種形狀。
Light 別名重用既有 Core 筆刷。Dark 填色使用較深顏色，以維持白色內容對比。
請在同一個資源字典一起覆寫 `Nvt.Toggle.*`，即可替換切換控制項色票。
替換應用程式或視窗中的該字典，即會更新已掛載的控制項。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Toggle.SelectedBrush` | `#1557E9` (`NfcAccentBrush`) | `#1148BE` |
| `Nvt.Toggle.SelectedPointerOverBrush` | `#1148BE` (`NfcAccentStrongBrush`) | `#0E3C9E` |
| `Nvt.Toggle.SelectedPressedBrush` | `#0E3C9E` | `#0B307E` |
| `Nvt.Toggle.SelectedLabelBrush` | `#FFFFFF` | `#FFFFFF` |
| `Nvt.Toggle.DangerFillBrush` | `#A82035` (`NfcDangerTextBrush`) | `#A82035` |
| `Nvt.Toggle.DangerFillPointerOverBrush` | `#861B2C` (`NfcDangerTextStrongBrush`) | `#861B2C` |
| `Nvt.Toggle.DangerFillPressedBrush` | `#861B2C` (`NfcDangerTextStrongBrush`) | `#861B2C` |
| `Nvt.Toggle.SwitchOnBrush` | `#2563EB` | `#2563EB` |
| `Nvt.Toggle.SwitchPointerOverBrush` | `#1D4ED8` | `#1D4ED8` |
| `Nvt.Toggle.SwitchPressedBrush` | `#1E40AF` | `#1E40AF` |
| `Nvt.Toggle.KnobBrush` | `#FFFFFF` | `#FFFFFF` |
| `Nvt.Toggle.TransparentBrush` | `#00FFFFFF` | `#00FFFFFF` |

中性狀態重用上方列出的共用色票。
焦點重用 `Nvt.Focus.RingBrush`：Light 為 `#1F6FD1`，Dark 為 `#4DA3FF`。
`Nvt.Focus.RingThickness` 在兩種主題皆為 2。

測試在兩種形狀下量得以下最低對比。
勾選填色檢查涵蓋啟用狀態。停用且勾選時使用上表的中性色。

| 對比 | Light | Dark |
| --- | --- | --- |
| 白色對勾選的 segment、tab 與 icon | 5.879:1 | 7.794:1 |
| 白色對勾選的開關 | 5.169:1 | 5.169:1 |
| 白色對勾選的 danger | 7.184:1 | 7.184:1 |
| 標準焦點框對相鄰表面 | 4.228:1 | 5.572:1 |
| 停用文字 | 3.903:1 | 4.275:1 |
| 所有狀態的旋鈕對軌道 | 4.015:1 | 3.422:1 |

### 共用形狀設定

`ThemeShape` 定義 `Pill` 與 `Square`。
請在 UI 執行緒呼叫 `ThemeShapes.SetShape(IResourceDictionary resources, ThemeShape shape)`。
傳入應用程式、視窗或子樹資源，作用範圍與主題變體的根節點方式一致。
此方法只替換一個形狀字典，保留其他資源。
透過 `RequestedThemeVariant` 切換 Light 或 Dark，不會改變形狀。
控制項保留原有模板，且不會收到個別的本機圓角設定。

```csharp
using Avalonia;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Theme;

ThemeShapes.SetShape(Application.Current!.Resources, ThemeShape.Square);
Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
ThemeShapes.SetShape(Application.Current.Resources, ThemeShape.Pill);
```

`ShapePill.axaml` 與 `ShapeSquare.axaml` 提供可重用的資源，供後續工具列按鈕與篩選膠囊採用。
Light 與 Dark 使用相同數值。

| Token | Pill | Square |
| --- | --- | --- |
| `Nvt.Shape.ControlCornerRadius` | 999 | 6 |
| `Nvt.Shape.GroupCornerRadius` | 999 | 6 |
| `Nvt.Shape.FocusCornerRadius` | 999 | 10 |
| `Nvt.Shape.RoundCornerRadius` | 999 | 999 |

開關軌道使用 `Nvt.Shape.ControlCornerRadius`，焦點框使用 `Nvt.Shape.FocusCornerRadius`。
旋鈕保持圓形；軌道 52 × 28、旋鈕 22 × 22、移動 24 DIP 均不變。
既有 Button 角色維持目前的圓角資源。

### 切換控制項採用步驟

1. 將 `ThemeTokens.axaml` 合併至應用程式資源，並在 Fluent 後載入 `ToggleStyles.axaml`。
2. 指定切換角色並繫結 `IsChecked`。分段選項請放入下方群組結構。
3. 由宿主提供圖示內容、無障礙名稱與選取規則。
4. 在需要的地方加上 `danger`，並於資源根節點選擇共用形狀。
5. 移除衝突的本機顏色與圓角。驗證兩種主題、兩種形狀、鍵盤操作與停用狀態。

```xml
<Border Classes="toggleSegmentGroup">
  <StackPanel>
    <ToggleButton Classes="toggleSegment" Content="Overview" IsChecked="True" />
    <ToggleButton Classes="toggleSegment" Content="Details" />
  </StackPanel>
</Border>
<ToggleButton Classes="toggleTab" Content="Overview" />
<ToggleButton Classes="toggleSwitch danger" AutomationProperties.Name="Enable option" />
<ToggleSwitch AutomationProperties.Name="Enable option" />
```

`ToggleStylesRenderer` 以 headless 方式檢查配置，僅在設定 `NVT_TOGGLE_IMAGES_DIR` 時寫入圖片。
輸出 `toggle-src-light.png` 與 `toggle-src-dark.png`，尺寸為 1320 × 2920，包含兩種形狀與所有狀態。
測試涵蓋對比、執行期資源替換、精確開關幾何、鍵盤操作，以及禁止樣式內寫死顏色或圓角。

### toggleSoft

`toggleSoft` 適合精簡篩選器與工具列的選用項目，勾選時顯示淡主色底。
支援純文字、圖示加文字，以及不需群組邊框的並列控制項。
高度使用 `NfcControlHeight`，預設為 32 DIP；水平內距為 12 DIP。

| 狀態 | 填色 | 文字與圖示 |
| --- | --- | --- |
| Off | `Nvt.Toggle.TransparentBrush` | `Nvt.Toggle.SoftForegroundBrush` |
| Off pointer over | `Nvt.Toggle.SoftPointerOverBrush` | `Nvt.Toggle.SoftPointerOverForegroundBrush` |
| Off pressed | `Nvt.Toggle.SoftPressedBrush` | `Nvt.Toggle.SoftPressedForegroundBrush` |
| On | `Nvt.Toggle.SoftCheckedBrush` | `Nvt.Toggle.SoftCheckedForegroundBrush` |
| On pointer over | `Nvt.Toggle.SoftCheckedPointerOverBrush` | `Nvt.Toggle.SoftCheckedForegroundBrush` |
| On pressed | `Nvt.Toggle.SoftCheckedPressedBrush` | `Nvt.Toggle.SoftCheckedForegroundBrush` |
| Disabled，off | `Nvt.Toggle.TransparentBrush` | `Nvt.Toggle.SoftDisabledForegroundBrush` |
| Disabled，on | `Nvt.Toggle.SoftDisabledCheckedBrush` | `Nvt.Toggle.SoftDisabledForegroundBrush` |
| Keyboard focus | 保留目前填色。 | 保留目前前景色。 |

以下別名在兩種主題共用 Core 資源。選取狀態使用改版驗證章節記載的淡色色盤。
替換包含這些 `Nvt.Toggle.Soft*` 鍵的單一字典，即可一起更新已掛載的控制項。

| Token | Core 資源 | Light | Dark |
| --- | --- | --- | --- |
| `Nvt.Toggle.SoftCheckedBrush` | `Nvt.Controls.SelectedBrush` | `#EFF3FD` | `#1A2940` |
| `Nvt.Toggle.SoftCheckedForegroundBrush` | `Nvt.Controls.SelectedForegroundBrush` | `#0E3C9E` | `#BEDAFF` |
| `Nvt.Toggle.SoftPointerOverBrush` | `NfcSelectionSurfaceBrush` | `#E8EEF5` | `#1E293B` |
| `Nvt.Toggle.SoftPressedBrush` | `NfcSecondaryActionPressedBrush` | `#E2E8F0` | `#243247` |
| `Nvt.Toggle.SoftForegroundBrush` | `NfcTextSecondaryBrush` | `#475569` | `#CBD5E1` |
| `Nvt.Toggle.SoftPointerOverForegroundBrush` | `NfcTextBrush` | `#1E293B` | `#E2E8F0` |
| `Nvt.Toggle.SoftPressedForegroundBrush` | `NfcTextStrongBrush` | `#0F172A` | `#F8FAFC` |
| `Nvt.Toggle.SoftDisabledForegroundBrush` | `NfcTextDisabledBrush` | `#68778C` | `#7B8CA5` |
| `Nvt.Toggle.SoftDisabledCheckedBrush` | `NfcSelectionSurfaceBrush` | `#E8EEF5` | `#1E293B` |

本體圓角使用 `Nvt.Shape.ControlCornerRadius`：Pill 為 999，Square 為 6。
焦點圓角使用 `Nvt.Shape.FocusCornerRadius`：Pill 為 999，Square 為 10。
既有 `ThemeShapes.SetShape` 方法可於執行期一起切換。

鍵盤焦點使用 `focus-visible`、`Nvt.Controls.FocusBrush` 與 `Nvt.Focus.RingThickness`，外側間隙為 2 px。
指標焦點不顯示焦點框。
背景與前景轉場皆為 150 ms。
按下時使用 0.98 繪製縮放，配置尺寸與點擊範圍不變。
Space 切換數值，Tab 聚焦控制項，停用控制項忽略輸入。

文字與圖示繼承相同前景色，在兩種主題皆達到 4.5:1 對比。
Light 選取靜止、滑過、按下文字對比為 8.759、8.185、7.807:1。
Dark 分別為 10.216、8.898、7.830:1。
此角色使用 `Nvt.Controls.FocusBrush`。對三種選取底色的最低值，Light 為 5.340:1、Dark 為 6.194:1。
Disabled on 的文字對其選取底色，亮色為 3.903:1，暗色為 4.275:1。

採用分成三步：

1. 合併 `ThemeTokens.axaml`，並在 Fluent 後載入 `ToggleStyles.axaml`。
2. 指定 `Classes="toggleSoft"`、繫結 `IsChecked`，並提供繼承前景色的內容。
3. 移除衝突的本機顏色與圓角，再驗證兩種主題、兩種形狀與鍵盤操作。

```xml
<StackPanel Orientation="Horizontal" Spacing="8">
  <ToggleButton Classes="toggleSoft" Content="Matches only" IsChecked="{Binding MatchesOnly}" />
  <ToggleButton Classes="toggleSoft" Content="Dedupe" IsChecked="{Binding Dedupe}" />
</StackPanel>
```

提供圖示內容時，請載入既有字型與圖示資源。
向量圖示的填色或筆畫須繫結至切換控制項的前景色。
`toggleSoft` 使用淡色調色票，沒有 `danger` 變體。

`ToggleSoftRenderer` 以 headless 方式檢查兩種形狀、純文字、圖示與所有狀態。
只有設定 `NVT_TOGGLE_IMAGES_DIR` 時，才寫入 `toggle-soft-light.png` 與 `toggle-soft-dark.png`。
測試與既有角色一樣，在本機停用轉場以取得穩定快照。

## CheckBox

可見列使用 `Nvt.Shape.ControlCornerRadius`，核取指示框保留圓角 6 的方形。
靜止使用 subtle 表面（Soft 靜止填色模式；見[靜止填色設定](#靜止填色設定)）；滑過與按下改變整列底色。勾選與不確定列共用三段淡色選取狀態。
列 padding 為 10,6；compact 為 10,2。啟用標籤使用 `Nvt.Controls.ChoiceForegroundBrush`。

`ChoiceStyles.axaml` 為原生核取方塊提供共用 Core 外觀，不需指定外觀 class。
將 `ThemeTokens.axaml` 合併至應用程式資源，並在 Fluent 後載入 `ChoiceStyles.axaml`。

指示框尺寸為 20 × 20 DIP。標籤使用 `Nvt.Controls.ChoiceForegroundBrush`、`NfcUiFontFamily` 與 13 DIP 的 `NfcFontSize13` 內文字級。
指示框與標籤間距為 8 DIP。整列可點擊，最小高度為 32 DIP。
長字串標籤會換行並增加列高，指示框維持與第一行對齊。
自訂內容保留原有內容模板，並自行控制文字換行。

密集篩選清單可加上 `compact`，固定列高為 24 DIP，指示框仍為 20 DIP。
`compact` 只用於單行標籤。列高固定，換行的標籤會被裁掉。
目前使用清單需要此變體來呈現篩選核取方塊。

Space 保留 Avalonia 的二態循環。`IsThreeState="True"` 依序循環未勾選、勾選、不確定，再回到未勾選。
不確定狀態顯示白色短橫線，勾選狀態顯示白色勾號。
停用選項保留選取圖示與 opacity 1。

### 選項狀態

下表適用於核取方塊的未勾選、勾選、不確定，以及單選按鈕的未勾選、勾選。
焦點僅改變外側焦點框。指標焦點不顯示焦點框。
鍵盤焦點在整列外側顯示一個 2 DIP 的 `Nvt.Controls.FocusBrush` 焦點框，間隔為 2 DIP。
預設焦點裝飾器已關閉。
筆刷轉場為 150 ms。控制項或祖先加上 `reducedMotion` 即可關閉轉場。

| 狀態 | 指示框底色 | 指示框外框 | 標籤 |
| --- | --- | --- | --- |
| 未勾選靜止 | `NfcSurfaceBrush` | `NfcBorderBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| 未勾選指標移入 | `NfcSurfaceSubtleBrush` | `NfcTextSecondaryBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| 未勾選按下 | `NfcSelectionSurfaceBrush` | `NfcTextStrongBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| 勾選或不確定 | `Nvt.Toggle.SelectedBrush` | `NfcAccentBorderBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| 已選取指標移入 | `Nvt.Toggle.SelectedPointerOverBrush` | `NfcAccentBorderStrongBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| 已選取按下 | `Nvt.Toggle.SelectedPressedBrush` | `NfcAccentBorderStrongBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| 停用未勾選 | `NfcSurfaceSubtleBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| 停用已選取 | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| 鍵盤焦點 | 保留目前底色 | 保留目前外框 | 保留目前標籤 |

停用控制項忽略指標、按下與鍵盤焦點樣式。
兩種主題的白色選取內容均使用 `Nvt.Toggle.SelectedLabelBrush`。
深色主題的選取外框使用較亮的 Core 強調色，確保指示框與鄰近底色可辨識。

### 選項 token

`ThemeTokens.axaml` 包含 `ChoiceTokens.axaml`。其中 14 個幾何 token 在 Light 與 Dark 使用相同值。
幾何使用 Choice 與 Shape token；列顏色使用共用色盤與 `ControlTokens.axaml`。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Choice.IndicatorSize` | 20 | 20 |
| `Nvt.Choice.DotSize` | 10 | 10 |
| `Nvt.Choice.CompactHeight` | 24 | 24 |
| `Nvt.Choice.CheckBoxCornerRadius` | 6 | 6 |
| `Nvt.Choice.RowPadding` | `10,6` | `10,6` |
| `Nvt.Choice.CompactPadding` | `10,2` | `10,2` |
| `NfcControlHeight` | 32 | 32 |
| `NfcFontSize13` | 13 | 13 |
| `NfcSurfaceBrush` | `#FFFFFF` | `#111827` |
| `NfcSurfaceSubtleBrush` | `#F8FAFC` | `#182337` |
| `NfcSelectionSurfaceBrush` | `#E8EEF5` | `#1E293B` |
| `NfcBorderBrush` | `#718096` | `#708198` |
| `NfcTextBrush` | `#1E293B` | `#E2E8F0` |
| `NfcTextSecondaryBrush` | `#475569` | `#CBD5E1` |
| `NfcTextStrongBrush` | `#0F172A` | `#F8FAFC` |
| `NfcTextDisabledBrush` | `#68778C` | `#7B8CA5` |
| `NfcAccentBorderBrush` | `#1557E9` | `#5FA5FA` |
| `NfcAccentBorderStrongBrush` | `#1148BE` | `#8FBFFB` |
| `Nvt.Toggle.SelectedBrush` | `#1557E9` | `#1148BE` |
| `Nvt.Toggle.SelectedPointerOverBrush` | `#1148BE` | `#0E3C9E` |
| `Nvt.Toggle.SelectedPressedBrush` | `#0E3C9E` | `#0B307E` |
| `Nvt.Toggle.SelectedLabelBrush` | `#FFFFFF` | `#FFFFFF` |
| `Nvt.Toggle.TransparentBrush` | `#00FFFFFF` | `#00FFFFFF` |
| `Nvt.Controls.FocusBrush` | `#1557C0` | `#8FC5FF` |
| `Nvt.Focus.RingThickness` | 2 | 2 |

勾號沿用 `NfcDoneIconGeometry`。
核取方塊圓角使用 `Nvt.Choice.CheckBoxCornerRadius`（6），Pill 與 Square 相同，避免看起來像單選圓環。單選指示點維持圓形。
整列焦點框使用 `Nvt.Shape.FocusCornerRadius`：Pill 999、Square 10。
在資源根節點呼叫 `ThemeShapes.SetShape`，即可更新已掛載選項而不替換模板。
在同一色票字典覆寫既有筆刷資源，可於執行期更新顏色。

以下最小對比涵蓋兩種形狀與五種鄰近底色，包含按下時的底色。

| 對比 | Light | Dark |
| --- | --- | --- |
| 啟用標籤 | 14.328:1 | 11.215:1 |
| 停用標籤 | 3.698:1 | 3.783:1 |
| 所有狀態的指示框外框 | 3.257:1 | 3.256:1 |
| 所有選取狀態的白色圖示，含停用 | 4.559:1 | 3.422:1 |
| 啟用勾選底色上的白色圖示 | 5.879:1 | 7.794:1 |
| 鍵盤焦點框 | 5.340:1 | 6.194:1 |

### CheckBox 採用步驟

1. 合併主題 token，並在 Fluent 後載入選項樣式。
2. 繫結 `IsChecked`；需要不確定值時設定 `IsThreeState`。
3. 密集篩選列加上 `compact`，並提供有意義的標籤或無障礙名稱。
4. 移除本機核取方塊模板、文字色、固定高度、內距覆寫與透明度變更。
5. 驗證兩種主題、兩種形狀、指標操作、鍵盤操作與停用選取狀態。

以 `compact` 取代既有篩選列的 24 DIP 高度。
以共用內文取代本機 14 DIP 中等字重的文字。

```xml
<CheckBox Content="Include archived items" IsChecked="True" />
<CheckBox Content="Include annotations" IsThreeState="True" IsChecked="{x:Null}" />
<CheckBox Classes="compact" Content="Include archive" />
```

## RadioButton

整列與 CheckBox 共用幾何、顏色、對比與執行期形狀切換。20 DIP 外圈與 10 DIP 指示點維持圓形。

`ChoiceStyles.axaml` 同時為原生單選按鈕提供外觀，不需指定外觀 class。
載入 CheckBox 章節所述的相同主題 token 與樣式。
單選外框尺寸為 20 × 20 DIP，勾選時顯示 10 DIP 白色圓點。
`Nvt.Shape.RoundCornerRadius` 讓外框在 Pill 與 Square 均保持圓形。
標籤間距、列高、換行、焦點框、轉場、停用選取與 `compact` class 均遵循核取方塊規則。
單選按鈕使用選項狀態表中的勾選與未勾選狀態。

選取行為由 Avalonia 管理，不新增自訂控制項、行為或鍵盤處理器。
未命名群組以父容器為範圍；相同 `GroupName` 可跨同一根節點內的不同面板分組。
Avalonia 12.1.1 需由宿主設定 `XYFocus.NavigationModes="Keyboard"`，方向鍵才會在一般 `StackPanel` 中移動焦點。
方向鍵僅移動焦點，不改變選取。Space 選取目前焦點的單選按鈕，不會取消選取。
此行為遵循 [Avalonia 方向焦點導覽](https://docs.avaloniaui.net/docs/input-interaction/focus)，並由選項測試記錄原生結果。
無障礙名稱與原生自動化控制項類型維持有效。

### RadioButton 採用步驟

1. 在 Fluent 後載入 `ChoiceStyles.axaml`，並合併主題 token。
2. 相關選項放入 `StackPanel`，或保留既有命名群組的 `GroupName`。
3. 繫結 `IsChecked`，並保留標籤、命令與無障礙名稱。
4. 移除本機選項模板、外框尺寸、圓點尺寸、顏色、圓角與固定卡片高度。
5. 採用後驗證群組範圍與方向鍵導覽。

採用此家族時，以共用選項列取代按鈕式單選導覽、篩選模板與高卡片。
共用 20/10 DIP 指示尺寸取代本機 18/8 DIP 外框與圓點。

```xml
<StackPanel>
  <RadioButton Content="Standard review" GroupName="Review" IsChecked="True" />
  <RadioButton Content="Extended review" GroupName="Review" />
</StackPanel>
```

`ChoiceStylesRenderer` 以 headless 方式檢查配置，僅在設定 `NVT_CHOICE_IMAGES_DIR` 時寫入圖片。
輸出 `choice-light.png`、`choice-dark.png`、`choice-square-light.png` 與 `choice-before-light.png`，每張寬 1200 像素、比例為 100%。
比較圖將 Fluent 與 Core 控制項並排呈現。
測試涵蓋所有狀態、對比、列幾何、換行、原生操作、執行期字典與形狀、無障礙及減少動態效果。

## 控制項改版驗證

本次只修改樣式與資源，沒有公開 C# API 變更。
在 Fluent 後載入既有家族樣式；`ThemeTokens.axaml` 自動引入 `ControlTokens.axaml`。
移除衝突的本機幾何、色盤、列填色、握柄與焦點樣式，保留應用邏輯與原生鍵盤行為。

ListBoxItem、ComboBoxItem、勾選 MenuItem、Choice 列與 `toggleSoft` 共用選取底色。
List 與 Toggle 別名保留供字典替換；同一家族色盤應一起替換。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Controls.SelectedBrush` | `#EFF3FD` | `#1A2940` |
| `Nvt.Controls.SelectedPointerOverBrush` | `#E3ECFC` | `#20334F` |
| `Nvt.Controls.SelectedPressedBrush` | `#DCE7FA` | `#243C5B` |
| `Nvt.Controls.SelectedForegroundBrush` | `#0E3C9E` | `#BEDAFF` |
| `Nvt.Controls.FocusBrush` | `#1557C0` | `#8FC5FF` |
| `Nvt.Controls.ChoiceForegroundBrush` | `#0F172A` | `#FFFFFF` |
| `Nvt.Controls.ExpanderPressedBrush` | `#CBD5E1` | `#29384D` |
| `Nvt.Controls.ExpanderPressedForegroundBrush` | `#0B1220` | `#FFFFFF` |
| `Nvt.Controls.ExpanderDisabledForegroundBrush` | `#637085` | `#91A1B9` |
| `Nvt.Toggle.SoftCheckedPointerOverBrush` | `#E3ECFC` | `#20334F` |
| `Nvt.Toggle.SoftCheckedPressedBrush` | `#DCE7FA` | `#243C5B` |

| Token | Pill | Square |
| --- | --- | --- |
| `Nvt.Progress.CornerRadius` | 999 | 1 |
| `Nvt.Expander.ContainerCornerRadius` | 8 | 6 |
| `Nvt.List.FocusCornerRadius` | 999 | 4 |
| `Nvt.GridSplitter.GripCornerRadius` | 2 | 1 |
| `Nvt.GridSplitter.FocusCornerRadius` | 999 | 2 |

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Expander.ContainerPadding` | `6` | `6` |
| `Nvt.Expander.HeaderPadding` | `12,0` | `12,0` |
| `Nvt.Expander.ContentPadding` | `24,12,12,12` | `24,12,12,12` |
| `Nvt.Expander.ContentMargin` | `0,6,0,0` | `0,6,0,0` |
| `Nvt.Expander.ContentUpMargin` | `0,0,0,6` | `0,0,0,6` |
| `Nvt.Expander.ContentLeftMargin` | `0,0,6,0` | `0,0,6,0` |
| `Nvt.Expander.ContentRightMargin` | `6,0,0,0` | `6,0,0,0` |
| `Nvt.Expander.ContainerBorderThickness` | `1` | `1` |
| `Nvt.Expander.ContentDividerThickness` | `0,1,0,0` | `0,1,0,0` |
| `Nvt.Expander.ContentDividerUpThickness` | `0,0,0,1` | `0,0,0,1` |
| `Nvt.Expander.ContentDividerLeftThickness` | `0,0,1,0` | `0,0,1,0` |
| `Nvt.Expander.ContentDividerRightThickness` | `1,0,0,0` | `1,0,0,0` |
| `Nvt.GridSplitter.GripWidth` | 4 | 4 |
| `Nvt.GridSplitter.GripLength` | 24 | 24 |
| `Nvt.GridSplitter.PressedLineThickness` | 3 | 3 |

下列新配色使用 WCAG 相對亮度、不透明 sRGB 色彩，適用兩種形狀；數值取三位小數。
文字、圖示、啟用外框與焦點均保留原文件最低對比。
裝飾分隔線與停用 splitter 提示依設計刻意變淡，不承載文字或啟用控制項辨識。

| Contrast | Light | Dark |
| --- | --- | --- |
| 選取：文字、圖示與標記 | 8.759:1 | 10.216:1 |
| 選取：Choice 標籤 | 16.075:1 | 14.633:1 |
| 選取：Choice 外框 | 5.294:1 | 5.748:1 |
| 選取：淡色列焦點 | 5.991:1 | 8.082:1 |
| 選取滑過：文字、圖示與標記 | 8.185:1 | 8.898:1 |
| 選取滑過：Choice 標籤 | 15.022:1 | 12.744:1 |
| 選取滑過：Choice 外框 | 6.558:1 | 6.683:1 |
| 選取滑過：淡色列焦點 | 5.598:1 | 7.039:1 |
| 選取按下：文字、圖示與標記 | 7.807:1 | 7.830:1 |
| 選取按下：Choice 標籤 | 14.328:1 | 11.215:1 |
| 選取按下：Choice 外框 | 6.255:1 | 5.882:1 |
| 選取按下：淡色列焦點 | 5.340:1 | 6.194:1 |
| Choice 標籤／靜止 | 17.063:1 | 15.737:1 |
| Choice 外框／靜止 | 3.838:1 | 3.959:1 |
| Choice 焦點／靜止 | 6.359:1 | 8.692:1 |
| Choice 標籤／滑過 | 15.285:1 | 14.629:1 |
| Choice 外框／滑過 | 6.488:1 | 9.853:1 |
| Choice 焦點／滑過 | 5.696:1 | 8.080:1 |
| Choice 標籤／按下 | 14.482:1 | 12.945:1 |
| Choice 外框／按下 | 14.482:1 | 12.373:1 |
| Choice 焦點／按下 | 5.397:1 | 7.150:1 |
| Choice 停用文字與外框／列 | 4.358:1 | 4.599:1 |
| Expander 靜止文字與箭頭 | 13.982:1 | 12.766:1 |
| Expander 滑過文字與箭頭 | 12.525:1 | 11.866:1 |
| Expander 按下文字與箭頭 | 12.611:1 | 11.884:1 |
| Expander 停用文字與箭頭 | 4.794:1 | 5.997:1 |
| Expander 容器／表面（裝飾） | 1.485:1 | 1.713:1 |
| 分隔線與內容交界／表面（裝飾） | 1.233:1 | 1.414:1 |
| 進度指示器／軌道 | 3.960:1 | 4.067:1 |
| 進度指示器／表面 | 5.879:1 | 6.968:1 |
| 停用進度指示器／軌道 | 3.071:1 | 3.026:1 |
| Splitter 靜止握柄／表面 | 4.015:1 | 4.462:1 |
| Splitter 滑過握柄／表面 | 5.879:1 | 6.968:1 |
| Splitter 按下線條與握柄／表面 | 7.794:1 | 9.303:1 |
| Splitter 停用線條與握柄／表面（非互動） | 1.485:1 | 1.713:1 |
| 實心選取按下白色符號 | 9.728:1 | 12.081:1 |
| Switch 按下白色旋鈕 | 8.722:1 | 8.722:1 |

審查提議深色 Expander 按下底色 `#334155`，即使白字也只有 10.355:1，低於原本 11.866:1。
改用 `#29384D` 配白字後為 11.884:1。亮色按下文字改為 `#0B1220`，保留 12.525:1 下限。
選取文字改為 `#0E3C9E`／`#BEDAFF`；淡色列焦點改為 `#1557C0`／`#8FC5FF`。
這些配色補償較強的選取底色，不修改全域 `Nfc*` 或 `Nvt.Focus.*` 色彩。
本次要求 section 保留 44 DIP，因此未採用審查的 40 DIP 建議。
本次不新增平面圖示角色；既有 Toggle 角色保留幾何，並增加明確的選取按下回饋。

新增的 `RenderRedesign*` 測試沿用家族環境變數，輸出 `<control>-<pill|square>-<light|dark>.png`。
`separator.png` 同時展示兩個主題，標示 "Not affected by shape"。
每張圖寬 1200 像素、英文標籤、縮放一倍，檔案小於一 MB。
進度圖展示靜止的不確定進度占位狀態。ProgressBar 與 Separator 不適用互動狀態。
每個指定控制項、所有 Toggle 角色與 ComboBoxItem 均有獨立執行期測試，且不替換模板。
仍需業主檢視真實 popup 定位、150 毫秒動作、繁中長標籤與應用組合畫面。

### 導入後幾何 token

以下預設值在兩種形狀與主題中相同。形狀相關圓角仍列於上方形狀 token 表。

凍結的父版本為儲存庫 `Dennis40816/nvt_fw_core`，commit ref 為 `c6c50c1c26b428f1c81d2397515feab3ff0e28fa`（完整 SHA）。
被取代的固定值來自 `src/Nvt.Core.Avalonia/Theme` 下的檔案：

- `ChoiceStyles.axaml` 與 `DividerStyles.axaml`。
- `ExpanderStyles.axaml` 與 `ListStyles.axaml`。
- `MenuStyles.axaml` 與 `ToggleStyles.axaml`。

凍結的圖表集包含 45 張重新設計圖表：

- `checkbox`、`radiobutton`、`list` 與 `combobox`。
- `menu`、`contextmenu`、`expander` 與 `progressbar`。
- `gridsplitter`、`switch` 與 `toggle`。

每個名稱有四張圖：`<control>-pill-light.png`、`<control>-pill-dark.png`、`<control>-square-light.png` 與 `<control>-square-dark.png`。
另一張為 `separator.png`，同時展示兩種主題，且不受形狀影響。

原生 `ToggleSwitch` 只有在勾選狀態改變後，才會將旋鈕移至新的 `Nvt.Toggle.SwitchKnobTravel` 位置。
變更此 token 會立即更新旋鈕畫布寬度，但旋鈕會保留原位置，直到下一次切換。

| Token | Pill 預設值 | Square 預設值 |
| --- | --- | --- |
| `Nvt.Choice.IndicatorBorderThickness` | `2` | `2` |
| `Nvt.CheckBox.CheckWidth` | `12` | `12` |
| `Nvt.CheckBox.CheckHeight` | `10` | `10` |
| `Nvt.CheckBox.CheckStrokeThickness` | `2` | `2` |
| `Nvt.CheckBox.DashWidth` | `10` | `10` |
| `Nvt.CheckBox.DashHeight` | `2` | `2` |
| `Nvt.Choice.LabelMargin` | `8,0,0,0` | `8,0,0,0` |
| `Nvt.Choice.LabelMinHeight` | `20` | `20` |
| `Nvt.Controls.FocusRingMargin` | `-4` | `-4` |
| `Nvt.Expander.HeaderSpacing` | `10` | `10` |
| `Nvt.Expander.ChevronSlotSize` | `20` | `20` |
| `Nvt.Expander.ChevronGeometry` | `M0 0 L6 6 L12 0` | `M0 0 L6 6 L12 0` |
| `Nvt.Expander.ChevronWidth` | `12` | `12` |
| `Nvt.Expander.ChevronHeight` | `6` | `6` |
| `Nvt.Expander.ChevronStrokeThickness` | `1.5` | `1.5` |
| `Nvt.List.RowPadding` | `10,5` | `10,5` |
| `Nvt.List.SelectionIndicatorWidth` | `2` | `2` |
| `Nvt.List.SelectionIndicatorHeight` | `12` | `12` |
| `Nvt.List.SelectionIndicatorMargin` | `4,0,0,0` | `4,0,0,0` |
| `Nvt.Controls.InsetFocusRingMargin` | `2` | `2` |
| `Nvt.List.CompactPadding` | `10,0` | `10,0` |
| `Nvt.Menu.PopupOffset` | `-16` | `-16` |
| `Nvt.Menu.PopupBorderThickness` | `1` | `1` |
| `Nvt.Menu.PopupPadding` | `4` | `4` |
| `Nvt.Menu.ItemPadding` | `10,0` | `10,0` |
| `Nvt.Menu.CheckStrokeThickness` | `2` | `2` |
| `Nvt.Menu.GestureMargin` | `24,0,0,0` | `24,0,0,0` |
| `Nvt.Menu.ChevronWidth` | `6` | `6` |
| `Nvt.Menu.ChevronHeight` | `10` | `10` |
| `Nvt.Menu.ChevronMargin` | `16,0,0,0` | `16,0,0,0` |
| `Nvt.Menu.ChevronStrokeThickness` | `1.5` | `1.5` |
| `Nvt.Menu.SubMenuHorizontalOffset` | `-20` | `-20` |
| `Nvt.Menu.SeparatorThickness` | `1` | `1` |
| `Nvt.Menu.SeparatorMargin` | `10,4` | `10,4` |
| `Nvt.Toggle.SegmentGroupPadding` | `4` | `4` |
| `Nvt.Toggle.SegmentSpacing` | `2` | `2` |
| `Nvt.Toggle.Height` | `40` | `40` |
| `Nvt.Toggle.Padding` | `20,0` | `20,0` |
| `Nvt.Toggle.IconSize` | `40` | `40` |
| `Nvt.Toggle.SwitchWidth` | `58` | `58` |
| `Nvt.Toggle.SwitchTrackWidth` | `52` | `52` |
| `Nvt.Toggle.SwitchTrackHeight` | `28` | `28` |
| `Nvt.Toggle.SwitchTrackBorderThickness` | `2` | `2` |
| `Nvt.Toggle.SwitchKnobTravel` | `24` | `24` |
| `Nvt.Toggle.SwitchKnobMargin` | `6,0,0,0` | `6,0,0,0` |
| `Nvt.Toggle.SwitchKnobSize` | `22` | `22` |
| `Nvt.Toggle.SwitchKnobTopOffset` | `3` | `3` |
| `Nvt.Toggle.SwitchFocusWidth` | `60` | `60` |
| `Nvt.Toggle.SwitchFocusHeight` | `36` | `36` |
| `Nvt.Toggle.PressedTransform` | `scale(0.98)` | `scale(0.98)` |
| `Nvt.Toggle.SoftPadding` | `12,0` | `12,0` |

請參閱 [導入後微調指南](../post-adoption-tuning.zh-TW.md)，了解 token 歸屬、共用調整、對比檢查與控制項圖表輸出。

## Astra 表單與頁籤

表單與頁籤現在共用柔和的 Astra 控制項外觀。本次新增樣式與 token，不變更公開 C# API。

在 Fluent 之後載入 `FormStyles.axaml` 與 `TabStyles.axaml`。ComboBox 彈出項目仍載入 `ListStyles.axaml`。
`ThemeTokens.axaml` 自動合併 `FormTokens.axaml` 與 `TabTokens.axaml`。
載入這些樣式之前，必須先將 `ThemeTokens` 合併至應用程式層級資源。
`Nvt.Form.StateTransitionDuration` 與 `Nvt.Tab.StateTransitionDuration` 使用載入時的 `StaticResource` 值。
執行期覆寫 token 不會改變既有轉場時間。重新載入樣式才能套用新的時間。

`FormStyles` 全域套用至 TextBox、NumericUpDown 與 ComboBox，也涵蓋其他控制項範本內的 TextBox。
內嵌 TextBox 可加上 `formEmbedded`，保留原生範本並將最小高度設為零。
其他表單 setter 仍會套用。可編輯 ComboBox 使用此類別；NumericUpDown 則刻意保留 Core 輸入範本。
Tab 導覽直接到達可編輯 ComboBox 的輸入框，保留原生焦點轉交會遺失的鍵盤焦點可見狀態。
TextBox 繼承 `FontFamily`，因此等寬編輯器可在祖先設定字型。
`AcceptsReturn=True` 使用頂端對齊，兩種形狀皆採 6 DIP 圓角與 10 DIP 焦點圓角。
32 DIP 最小高度允許編輯器增高。
TextBox、NumericUpDown、ComboBox 與 TabItem 使用 `UseLayoutRounding=False`，保留精確的焦點外距與微調幾何。
非整數 DPI 下，邊框與文字可能較柔和，因為邊界不會捨入至實體像素。

- TextBox 使用柔和填色、32 DIP 最小列高與原生文字編輯呈現器。
- NumericUpDown 共用外框，內含無邊框輸入框與兩個緊湊的微調按鈕。
- ComboBox 的關閉欄位採用相同外觀，展開時使用按下填色。彈出項目保留 List 外觀。
- TabControl 新增柔和頁籤列。TabItem 採用柔和選取填色，並在內容方向顯示選取指示線。
- 控制項支援一般、滑入、按下或展開、鍵盤焦點、停用，以及執行期 Pill／Square 切換。

TextBox 與 NumericUpDown 保留原生 `IsReadOnly` 行為。
唯讀會抑制滑入填色與邊框。錯誤狀態保留唯讀填色，並使用錯誤邊框。
TextBox 沒有原生 `:pressed` 狀態。狀態圖表強制設定此偽類別，以預覽保留的樣式規則。
ComboBox 沒有原生唯讀屬性。`readOnly` 類別只提供唯讀視覺狀態。
採用端負責限制選取。樣式保留原生鍵盤行為。

在任一表單控制項加上 `error`，即可使用 `Nvt.Form.ErrorBorderBrush`。
原生 `:error` 驗證狀態也使用此 token。錯誤邊框優先於滑入、焦點、唯讀與停用邊框。
焦點只顯示一道 2 DIP 外環，與控制項相隔 2 DIP。滑鼠焦點與停用控制項不顯示外環。
數值輸入的焦點環包住整個微調控制項。驗證呈現器仍保留，且不裁切外環。
控制項本身或祖先的 `reducedMotion` 類別會停用色彩轉場。

表單是 Core 原創設計，從 NFH 外觀取用緊湊欄位列、無邊框數值輸入與小型微調目標的設計意圖。
頁籤是 Core 原創設計，從 NFH 外觀取用中性頁籤列與明確選取狀態的設計意圖。
Core 使用自己的名稱與色盤。
採用端移除重複的欄位尺寸、關閉選單、微調按鈕、頁籤列、指示線與焦點樣式。
保留資料繫結、驗證、命令、無障礙名稱與既有彈出項目邏輯。

### 表單與頁籤色盤 token

Pill 與 Square 使用相同色盤預設值。別名重用既有筆刷，沒有新增色彩常值。
調整主題時，請一起替換整組控制項家族色盤。

| Token | Light | Dark | 別名來源 |
| --- | --- | --- | --- |
| `Nvt.Form.TransparentBrush` | `#00FFFFFF` | `#00FFFFFF` | `Nvt.Toggle.TransparentBrush` |
| `Nvt.Form.FillBrush` | `#F8FAFC` | `#182337` | `NfcSurfaceSubtleBrush` |
| `Nvt.Form.HoverFillBrush` | `#E8EEF5` | `#1E293B` | `NfcSelectionSurfaceBrush` |
| `Nvt.Form.PressedFillBrush` | `#E2E8F0` | `#243247` | `NfcSecondaryActionPressedBrush` |
| `Nvt.Form.ReadOnlyFillBrush` | `#F8FAFC` | `#182337` | `NfcSurfaceSubtleBrush` |
| `Nvt.Form.DisabledFillBrush` | `#F8FAFC` | `#182337` | `NfcSurfaceSubtleBrush` |
| `Nvt.Form.TextBrush` | `#1E293B` | `#E2E8F0` | `NfcTextBrush` |
| `Nvt.Form.PlaceholderBrush` | `#526176` | `#A1AEC2` | `NfcTextMutedBrush` |
| `Nvt.Form.DisabledTextBrush` | `#68778C` | `#7B8CA5` | `NfcTextDisabledBrush` |
| `Nvt.Form.BorderBrush` | `#718096` | `#708198` | `NfcBorderBrush` |
| `Nvt.Form.HoverBorderBrush` | `#475569` | `#CBD5E1` | `NfcTextSecondaryBrush` |
| `Nvt.Form.PressedBorderBrush` | `#0F172A` | `#F8FAFC` | `NfcTextStrongBrush` |
| `Nvt.Form.ErrorBorderBrush` | `#C04A5C` | `#C96579` | `NfcDangerBorderBrush` |
| `Nvt.Form.FocusBrush` | `#1557C0` | `#8FC5FF` | `Nvt.Controls.FocusBrush` |
| `Nvt.Form.SelectionBrush` | `#EFF3FD` | `#1A2940` | `Nvt.Controls.SelectedBrush` |
| `Nvt.Form.SelectionTextBrush` | `#0E3C9E` | `#BEDAFF` | `Nvt.Controls.SelectedForegroundBrush` |
| `Nvt.Tab.TransparentBrush` | `#00FFFFFF` | `#00FFFFFF` | `Nvt.Toggle.TransparentBrush` |
| `Nvt.Tab.StripBrush` | `#F8FAFC` | `#182337` | `NfcSurfaceSubtleBrush` |
| `Nvt.Tab.FillBrush` | `#00FFFFFF` | `#00FFFFFF` | `Nvt.Toggle.TransparentBrush` |
| `Nvt.Tab.HoverFillBrush` | `#E8EEF5` | `#1E293B` | `NfcSelectionSurfaceBrush` |
| `Nvt.Tab.PressedFillBrush` | `#E2E8F0` | `#243247` | `NfcSecondaryActionPressedBrush` |
| `Nvt.Tab.SelectedFillBrush` | `#EFF3FD` | `#1A2940` | `Nvt.Controls.SelectedBrush` |
| `Nvt.Tab.SelectedHoverFillBrush` | `#E3ECFC` | `#20334F` | `Nvt.Controls.SelectedPointerOverBrush` |
| `Nvt.Tab.SelectedPressedFillBrush` | `#DCE7FA` | `#243C5B` | `Nvt.Controls.SelectedPressedBrush` |
| `Nvt.Tab.TextBrush` | `#475569` | `#CBD5E1` | `NfcTextSecondaryBrush` |
| `Nvt.Tab.HoverTextBrush` | `#1E293B` | `#E2E8F0` | `NfcTextBrush` |
| `Nvt.Tab.PressedTextBrush` | `#0F172A` | `#F8FAFC` | `NfcTextStrongBrush` |
| `Nvt.Tab.SelectedTextBrush` | `#0E3C9E` | `#BEDAFF` | `Nvt.Controls.SelectedForegroundBrush` |
| `Nvt.Tab.DisabledTextBrush` | `#68778C` | `#7B8CA5` | `NfcTextDisabledBrush` |
| `Nvt.Tab.DisabledFillBrush` | `#F8FAFC` | `#182337` | `NfcSurfaceSubtleBrush` |
| `Nvt.Tab.DisabledSelectedFillBrush` | `#E8EEF5` | `#1E293B` | `NfcSelectionSurfaceBrush` |
| `Nvt.Tab.IndicatorBrush` | `#0E3C9E` | `#BEDAFF` | `Nvt.Controls.SelectedForegroundBrush` |
| `Nvt.Tab.FocusBrush` | `#1557C0` | `#8FC5FF` | `Nvt.Controls.FocusBrush` |

### 表單與頁籤幾何 token

以下新預設值在 Light 與 Dark 相同。內層輸入框保持零圓角，形狀由外框統一負責。
控制項與頁籤列重用 `Nvt.Shape.ControlCornerRadius` 和 `Nvt.Shape.GroupCornerRadius`：Pill 999，Square 6。
焦點重用 `Nvt.Shape.FocusCornerRadius`：Pill 999，Square 10。
外環厚度與外側邊距重用 `Nvt.Focus.RingThickness` 和 `Nvt.Controls.FocusRingMargin`。

調整數值控制項時，請同步調整 `SpinnerWidth`、`BorderThickness`、`InnerFocusOffset` 和 `LeftSpinnerFocusOffset`。
左側位移為微調寬度加邊框內縮後的負值。兩個 15 DIP 按鈕可放入 32 DIP 欄位。

| Token | Pill | Square |
| --- | --- | --- |
| `Nvt.Form.Padding` | `12,0` | `12,0` |
| `Nvt.Form.BorderThickness` | `1` | `1` |
| `Nvt.Form.EmptyThickness` | `0` | `0` |
| `Nvt.Form.InputCornerRadius` | `0` | `0` |
| `Nvt.Form.MultilineCornerRadius` | `6` | `6` |
| `Nvt.Form.MultilineFocusCornerRadius` | `10` | `10` |
| `Nvt.Form.SpinnerWidth` | `28` | `28` |
| `Nvt.Form.SpinnerButtonHeight` | `15` | `15` |
| `Nvt.Form.LeftSpinnerFocusOffset` | `-29` | `-29` |
| `Nvt.Form.SpinnerButtonMinHeight` | `0` | `0` |
| `Nvt.Form.GlyphWidth` | `10` | `10` |
| `Nvt.Form.GlyphHeight` | `5` | `5` |
| `Nvt.Form.GlyphStrokeThickness` | `1.5` | `1.5` |
| `Nvt.Form.InnerFocusOffset` | `-1` | `-1` |
| `Nvt.Form.StateTransitionDuration` | `0:0:0.15` | `0:0:0.15` |
| `Nvt.Form.ChevronGeometry` | `M0 0 L5 5 L10 0` | `M0 0 L5 5 L10 0` |
| `Nvt.Form.IncreaseGeometry` | `M0 5 L5 0 L10 5` | `M0 5 L5 0 L10 5` |
| `Nvt.Form.Height` | `32` | `32` |
| `Nvt.Tab.Padding` | `14,0` | `14,0` |
| `Nvt.Tab.StripPadding` | `4` | `4` |
| `Nvt.Tab.ItemMargin` | `4` | `4` |
| `Nvt.Tab.ContentPadding` | `0,12,0,0` | `0,12,0,0` |
| `Nvt.Tab.EmptyThickness` | `0` | `0` |
| `Nvt.Tab.IndicatorMargin` | `14,0,14,3` | `14,0,14,3` |
| `Nvt.Tab.IndicatorThickness` | `2` | `2` |
| `Nvt.Tab.IndicatorLength` | `16` | `16` |
| `Nvt.Tab.StateTransitionDuration` | `0:0:0.15` | `0:0:0.15` |
| `Nvt.Tab.Height` | `32` | `32` |
| `Nvt.Tab.BottomIndicatorMargin` | `14,3,14,0` | `14,3,14,0` |
| `Nvt.Tab.LeftIndicatorMargin` | `0,0,3,0` | `0,0,3,0` |
| `Nvt.Tab.RightIndicatorMargin` | `3,0,0,0` | `3,0,0,0` |

### 表單與頁籤對比

這些配對沿用既有 37 組表格的 WCAG 相對亮度方法，使用不透明 sRGB 色彩。
比值適用兩種形狀，四捨五入至小數點後三位。
啟用文字維持 4.5:1。停用文字、邊框、有效指示線與焦點環維持 3:1。
共用選取文字與焦點配對保留既有實測比值。
`FormsContrastTests` 會在兩種形狀與主題下，以編譯後資源量測所有配對。

| 對比配對 | Light | Dark |
| --- | --- | --- |
| 表單文字、插入點與微調圖示／一般／唯讀 | 13.982:1 | 12.766:1 |
| 表單提示文字／一般／唯讀 | 6.027:1 | 7.006:1 |
| 表單邊框／一般／唯讀 | 3.838:1 | 3.959:1 |
| 表單錯誤邊框／一般／唯讀 | 4.587:1 | 4.212:1 |
| 表單焦點環／一般／唯讀 | 6.359:1 | 8.692:1 |
| 表單文字、插入點與微調圖示／滑入 | 12.525:1 | 11.866:1 |
| 表單提示文字／滑入 | 5.399:1 | 6.512:1 |
| 表單邊框／滑入 | 6.488:1 | 9.853:1 |
| 表單錯誤邊框／滑入 | 4.109:1 | 3.916:1 |
| 表單焦點環／滑入 | 5.696:1 | 8.080:1 |
| 表單文字、插入點與微調圖示／按下／展開 | 11.866:1 | 10.501:1 |
| 表單提示文字／按下／展開 | 5.115:1 | 5.763:1 |
| 表單邊框／按下／展開 | 14.482:1 | 12.373:1 |
| 表單錯誤邊框／按下／展開 | 3.893:1 | 3.465:1 |
| 表單焦點環／按下／展開 | 5.397:1 | 7.150:1 |
| 表單停用文字、提示、邊框與微調圖示 | 4.358:1 | 4.599:1 |
| 表單錯誤邊框／停用填色 | 4.587:1 | 4.212:1 |
| 表單反白文字 | 8.759:1 | 10.216:1 |
| 表單與頁籤焦點環／應用程式 | 6.073:1 | 10.341:1 |
| 表單一般邊框／應用程式 | 3.665:1 | 4.710:1 |
| 表單滑入邊框／應用程式 | 6.917:1 | 12.611:1 |
| 表單按下邊框／應用程式 | 16.296:1 | 17.895:1 |
| 表單錯誤邊框／應用程式 | 4.381:1 | 5.012:1 |
| 表單停用邊框／應用程式 | 4.162:1 | 5.471:1 |
| 表單與頁籤焦點環／表面 | 6.653:1 | 9.798:1 |
| 表單一般邊框／表面 | 4.015:1 | 4.462:1 |
| 表單滑入邊框／表面 | 7.578:1 | 11.948:1 |
| 表單按下邊框／表面 | 17.853:1 | 16.955:1 |
| 表單錯誤邊框／表面 | 4.800:1 | 4.748:1 |
| 表單停用邊框／表面 | 4.559:1 | 5.184:1 |
| 頁籤文字／一般 | 7.243:1 | 10.600:1 |
| 頁籤焦點環／一般 | 6.359:1 | 8.692:1 |
| 頁籤文字／滑入 | 12.525:1 | 11.866:1 |
| 頁籤焦點環／滑入 | 5.696:1 | 8.080:1 |
| 頁籤文字／按下 | 14.482:1 | 12.373:1 |
| 頁籤焦點環／按下 | 5.397:1 | 7.150:1 |
| 頁籤文字與指示線／選取 | 8.759:1 | 10.216:1 |
| 頁籤焦點環／選取 | 5.991:1 | 8.082:1 |
| 頁籤文字與指示線／選取滑入 | 8.185:1 | 8.898:1 |
| 頁籤焦點環／選取滑入 | 5.598:1 | 7.039:1 |
| 頁籤文字與指示線／選取按下 | 7.807:1 | 7.830:1 |
| 頁籤焦點環／選取按下 | 5.340:1 | 6.194:1 |
| 頁籤文字／停用 | 4.358:1 | 4.599:1 |
| 頁籤文字與指示線／選取停用 | 3.903:1 | 4.275:1 |

## 文字樣式類別

`TextStyles.axaml` 提供可選用的 TextBlock 文字角色，也適用其 SelectableTextBlock 子類別。
每個類別都從既有 `Nvt.Font.<Role>.*` token 設定字型家族、大小與字重。
所有角色都將前景設為 `NfcTextBrush`，覆寫繼承的前景，包括選取列與實心按鈕的前景。
另外加上 `muted`，只將前景改為 `NfcTextMutedBrush`。
這些類別套用至任何符合的 TextBlock，也包含其他控制項範本內的文字。
既有 `title`、`body`、`caption` 或 `muted` 等同名類別可能因此與這些樣式衝突。

這些角色需要 `Nvt.Core.Fonts` 套件及其資源。
參考 `Nvt.Core.Fonts`，在載入文字樣式之前合併 `FontRoles.axaml`，並在 Fluent 之後載入 `TextStyles.axaml`。
依 [Fonts 模組](Fonts.zh-TW.md) 使用 `WithNvtCoreFonts()` 設定既有中文後備字型。
樣式不需要新的字型註冊或公開 C# API。
採用後移除重複的本機文字角色樣式。

類別不設定行高，也不改動未加類別的 TextBlock、SelectableTextBlock 與 Label 預設值。
每個文字控制項使用一個角色類別，需要時再搭配 `muted`。
H1 至 H3 階層規則不在本次變更範圍。

| 類別 | 角色 token 前綴 | 字型家族 | 大小 | 字重 |
| --- | --- | --- | ---: | ---: |
| `title` | `Nvt.Font.Title.*` | Inter | 24 | 600 |
| `heading` | `Nvt.Font.Heading.*` | Inter | 16 | 600 |
| `body` | `Nvt.Font.Body.*` | Inter | 13 | 400 |
| `caption` | `Nvt.Font.Caption.*` | Inter | 11 | 400 |
| `mono` | `Nvt.Font.Mono.*` | Cascadia Mono | 13 | 400 |
| `numbers` | `Nvt.Font.Numbers.*` | Cascadia Mono | 13 | 400 |
| `bodyStrong` | `Nvt.Font.BodyStrong.*` | Inter | 13 | 600 |
| `captionStrong` | `Nvt.Font.CaptionStrong.*` | Inter | 11 | 600 |
| `monoStrong` | `Nvt.Font.MonoStrong.*` | Cascadia Mono | 13 | 600 |
| `monoCaption` | `Nvt.Font.MonoCaption.*` | Cascadia Mono | 11 | 400 |

角色後綴為 `Family`（FontFamily）、`Size`（double）與 `Weight`（FontWeight）。

| 前景 token | Light | Dark |
| --- | --- | --- |
| `NfcTextBrush` | `#1E293B` | `#E2E8F0` |
| `NfcTextMutedBrush` | `#526176` | `#A1AEC2` |

這些文字配對沿用相同 sRGB 方法，兩種形狀皆維持 4.5:1 文字門檻。

| 對比配對 | Light | Dark |
| --- | --- | --- |
| 所有文字類別／應用程式 | 13.353:1 | 15.188:1 |
| muted 類別／應用程式 | 5.756:1 | 8.335:1 |
| 所有文字類別／表面 | 14.629:1 | 14.390:1 |
| muted 類別／表面 | 6.306:1 | 7.897:1 |
| 所有文字類別／柔和表面 | 13.982:1 | 12.766:1 |
| muted 類別／柔和表面 | 6.027:1 | 7.006:1 |
| 所有文字類別／滑入填色 | 12.525:1 | 11.866:1 |
| muted 類別／滑入填色 | 5.399:1 | 6.512:1 |
| 所有文字類別／按下填色 | 11.866:1 | 10.501:1 |
| muted 類別／按下填色 | 5.115:1 | 5.763:1 |
| 所有文字類別／選取填色 | 13.172:1 | 11.870:1 |
| muted 類別／選取填色 | 5.678:1 | 6.514:1 |
| 所有文字類別／選取滑入填色 | 12.309:1 | 10.338:1 |
| muted 類別／選取滑入填色 | 5.306:1 | 5.673:1 |
| 所有文字類別／選取按下填色 | 11.741:1 | 9.098:1 |
| muted 類別／選取按下填色 | 5.061:1 | 4.993:1 |

### 渲染與檢閱

執行新增的 renderer 測試前，先設定 `NVT_FORMS_IMAGES_DIR`。
`FormStylesRenderer`、`TabStylesRenderer` 和 `TextStylesRenderer` 以合成資料輸出英文圖表。
控制項圖表使用 `<control>-<pill|square>-<light|dark>.png`。
文字圖表使用 `textstyles-light.png` 與 `textstyles-dark.png`。
Fluent 比較圖使用 `forms-before-light.png` 與 `tabs-before-light.png`。
每張圖寬 1200 像素、比例為一，且小於一 MB。

測試涵蓋執行期圓角、token 幾何、錯誤優先順序、僅鍵盤顯示且不裁切的焦點環、原生編輯、微調、頁籤導覽與字型角色三項設定。
擁有者仍需檢閱實際應用程式的彈出位置、150 ms 動態轉場、長繁體中文內容與頁面組合。
ComboBox 測試涵蓋關閉欄位、可編輯輸入框與原生驗證幾何。唯讀視覺類別不會限制選取。
token 歸屬與驗證流程請參閱 [導入後微調指南](../post-adoption-tuning.zh-TW.md)。

## 靜止填色設定

`ThemeRestFill.Soft` 為預設值，保留目前外觀。
`ThemeRestFill.None` 隱藏 CheckBox 與 RadioButton 的列填色，包含已勾選、不確定與停用狀態。
它也隱藏 Expander 標題的靜止與停用填色。
滑入與按下填色、鍵盤焦點環、指示器顏色及整列點擊區域皆保持不變。
選取的 List 列、已勾選的 Menu 項目、輸入欄位、切換按鈕、開關、進度軌道及 Expander 容器表面皆保持不變。

新增的 API 位於 `Nvt.Core.Avalonia.Theme`：

```csharp
public enum ThemeRestFill { Soft, None }
public static class ThemeRestFills
public static void SetRestFill(IResourceDictionary resources, ThemeRestFill fill)
```

在 UI 執行緒呼叫 `ThemeRestFills.SetRestFill`，傳入應用程式、視窗或子樹的資源。
若 `ThemeTokens.axaml` 合併在同一資源根節點，請先合併它，再呼叫此方法。
方法替換一個靜止填色 `ResourceInclude`，保留其他字典及形狀設定。
只辨識絕對 `avares://` 靜止填色來源。加入 include 前，請先解析相對路徑。Avalonia 不公開 include 的基底 URI。
null 資源、未定義列舉值及背景執行緒呼叫都會在修改資源前擲回例外。
已附加控制項透過動態資源更新，不替換範本。
較近資源根節點的 None 設定只隱藏該子樹的填色。切換主題或形狀仍保留所選填色模式。
Soft 移除該根節點的覆寫並使用繼承資源；祖先的 None 設定仍會繼承。

```csharp
using Nvt.Core.Avalonia.Theme;
ThemeRestFills.SetRestFill(Avalonia.Application.Current!.Resources, ThemeRestFill.None);
ThemeRestFills.SetRestFill(Avalonia.Application.Current!.Resources, ThemeRestFill.Soft);
```

`RestFillSoft.axaml` 不含任何 key，因此套用預設模式。
`RestFillNone.axaml` 把一個一般字串 token `Nvt.Controls.RestFillMode` 設為 `None`；`ControlTokens.axaml` 預設為 `Soft`。
`Nvt.Controls.RestFillMode` 為內部 token。只有完全相同的字串 `None` 會生效；其他任何值皆代表 Soft。
請只透過 `SetRestFill` 變更它。
後合併的字典在兩種主題下都會覆寫預設值。
Choice 列與 Expander 標題把該模式放在 `Tag`。針對 `Tag=None` 的 selector 讓靜止與停用填色變透明，且排在滑入與按下樣式之前，所以那兩個狀態仍然優先。
Soft 路徑就是基準路徑：列與標題仍動態讀取 `NfcSurfaceSubtleBrush` 與 `Nvt.Controls.SelectedBrush`。應用程式或視窗層級的色票覆寫，包含首次使用後才設定的，仍會生效。

| Token | Soft | None |
| --- | --- | --- |
| `Nvt.Controls.RestFillMode` | `Soft` | `None` |

Soft 特徵測試與 65 張像素比對所用的凍結基準：儲存庫 `Dennis40816/nvt_fw_core`，分支 `feature/core/controls-forms-tabs`，完整 commit `98f4e3a53c6d0075da693438bca6deb20f54af33`。受影響檔案為 `src/Nvt.Core.Avalonia/Theme/ChoiceStyles.axaml`、`src/Nvt.Core.Avalonia/Theme/ControlTokens.axaml` 與 `src/Nvt.Core.Avalonia/Theme/ExpanderStyles.axaml`。
`SoftFillsFollowPaletteOverridesAtTheWindow` 釘住動態色票查找。65 張圖以明確 Soft 模式重繪，並與該基準逐像素比對：差異像素為 0。

### 靜止填色對比

`PaintedSurfaceContrastPreservesDocumentedLevels` 在兩種形狀、主題及模式下，從已附加控制項測量 8,232 組配對。
透明筆刷先顯露實際下方表面，再計算相對亮度。
Pill 與 Square 分別測量且結果相同；下表每一列均適用於兩種形狀。
下表使用 Choice 下方及 Expander 容器內的 `NfcSurfaceBrush`。
測試另驗證 Choice 下方的應用程式、柔和、選取及按下表面。
既有文件記載的數值下限皆維持不變，並容許小數點後三位的捨入誤差。
啟用文字及符號維持 4.5:1；停用文字及符號、指示器輪廓與焦點環維持 3:1。
None 模式不需修正任何對比 token。

靜止數值也適用於鍵盤焦點狀態，焦點不改變填色。
已勾選及不確定 CheckBox 與已勾選 RadioButton 的顏色配對相同。
停用列不分選取狀態皆共用填色，且不顯示焦點環。
選取指示器的邊界可與內部填色相同；輪廓對比以列為背景，符號則以指示器填色為背景。
容器邊界與分隔線保留既有裝飾性對比，不承載文字或作用中控制項識別。

| 配對（Pill 與 Square） | Soft Light | Soft Dark | None Light | None Dark |
| --- | ---: | ---: | ---: | ---: |
| 未勾選 靜止 文字／列 | 17.063:1 | 15.737:1 | 17.853:1 | 17.740:1 |
| 未勾選 靜止 指示器邊界／列 | 3.838:1 | 3.959:1 | 4.015:1 | 4.462:1 |
| 未勾選 靜止 焦點／列 | 6.359:1 | 8.692:1 | 6.653:1 | 9.798:1 |
| 未勾選 滑入 文字／列 | 15.285:1 | 14.629:1 | 15.285:1 | 14.629:1 |
| 未勾選 滑入 指示器邊界／列 | 6.488:1 | 9.853:1 | 6.488:1 | 9.853:1 |
| 未勾選 滑入 焦點／列 | 5.696:1 | 8.080:1 | 5.696:1 | 8.080:1 |
| 未勾選 按下 文字／列 | 14.482:1 | 12.945:1 | 14.482:1 | 12.945:1 |
| 未勾選 按下 指示器邊界／列 | 14.482:1 | 12.373:1 | 14.482:1 | 12.373:1 |
| 未勾選 按下 焦點／列 | 5.397:1 | 7.150:1 | 5.397:1 | 7.150:1 |
| 已勾選／不確定 靜止 文字／列 | 16.075:1 | 14.633:1 | 17.853:1 | 17.740:1 |
| 已勾選／不確定 靜止 指示器邊界／列 | 5.294:1 | 5.748:1 | 5.879:1 | 6.968:1 |
| 已勾選／不確定 靜止 焦點／列 | 5.991:1 | 8.082:1 | 6.653:1 | 9.798:1 |
| 已勾選／不確定 滑入 文字／列 | 15.022:1 | 12.744:1 | 15.022:1 | 12.744:1 |
| 已勾選／不確定 滑入 指示器邊界／列 | 6.558:1 | 6.683:1 | 6.558:1 | 6.683:1 |
| 已勾選／不確定 滑入 焦點／列 | 5.598:1 | 7.039:1 | 5.598:1 | 7.039:1 |
| 已勾選／不確定 按下 文字／列 | 14.328:1 | 11.215:1 | 14.328:1 | 11.215:1 |
| 已勾選／不確定 按下 指示器邊界／列 | 6.255:1 | 5.882:1 | 6.255:1 | 5.882:1 |
| 已勾選／不確定 按下 焦點／列 | 5.340:1 | 6.194:1 | 5.340:1 | 6.194:1 |
| 停用 Choice 文字／列 | 4.358:1 | 4.599:1 | 4.559:1 | 5.184:1 |
| 停用 Choice 指示器邊界／列 | 4.358:1 | 4.599:1 | 4.559:1 | 5.184:1 |
| 未勾選 靜止 邊界／指示器填色 | 4.015:1 | 4.462:1 | 4.015:1 | 4.462:1 |
| 未勾選 滑入 邊界／指示器填色 | 7.243:1 | 10.600:1 | 7.243:1 | 10.600:1 |
| 未勾選 按下 邊界／指示器填色 | 15.285:1 | 13.982:1 | 15.285:1 | 13.982:1 |
| 未勾選 停用 邊界／指示器填色 | 4.358:1 | 4.599:1 | 4.358:1 | 4.599:1 |
| 已勾選／不確定 靜止 邊界／指示器填色 | 1.000:1 | 3.062:1 | 1.000:1 | 3.062:1 |
| 已勾選／不確定 滑入 邊界／指示器填色 | 1.000:1 | 5.102:1 | 1.000:1 | 5.102:1 |
| 已勾選／不確定 按下 邊界／指示器填色 | 1.248:1 | 6.336:1 | 1.248:1 | 6.336:1 |
| 已勾選／不確定 停用 邊界／指示器填色 | 1.000:1 | 1.000:1 | 1.000:1 | 1.000:1 |
| 已選取 靜止 符號／指示器填色 | 5.879:1 | 7.794:1 | 5.879:1 | 7.794:1 |
| 已選取 滑入 符號／指示器填色 | 7.794:1 | 9.728:1 | 7.794:1 | 9.728:1 |
| 已選取 按下 符號／指示器填色 | 9.728:1 | 12.081:1 | 9.728:1 | 12.081:1 |
| 已選取 停用 符號／指示器填色 | 4.559:1 | 3.422:1 | 4.559:1 | 3.422:1 |
| Choice 焦點／五種周圍表面（最低值） | 5.397:1 | 7.150:1 | 5.397:1 | 7.150:1 |
| Expander 靜止 文字與箭頭／標題 | 13.982:1 | 12.766:1 | 14.629:1 | 14.390:1 |
| Expander 滑入 文字與箭頭／標題 | 12.525:1 | 11.866:1 | 12.525:1 | 11.866:1 |
| Expander 按下 文字與箭頭／標題 | 12.611:1 | 11.884:1 | 12.611:1 | 11.884:1 |
| Expander 停用 文字與箭頭／標題 | 4.794:1 | 5.997:1 | 5.016:1 | 6.760:1 |
| Expander 靜止 焦點／標題 | 4.720:1 | 5.994:1 | 4.938:1 | 6.757:1 |
| Expander 滑入 焦點／標題 | 4.228:1 | 5.572:1 | 4.228:1 | 5.572:1 |
| Expander 按下 焦點／標題 | 3.326:1 | 4.526:1 | 3.326:1 | 4.526:1 |
| Expander 焦點／容器 | 4.938:1 | 6.757:1 | 4.938:1 | 6.757:1 |
| Expander 分隔線／容器（裝飾） | 1.233:1 | 1.414:1 | 1.233:1 | 1.414:1 |
| Expander 容器邊界／容器（裝飾） | 1.485:1 | 1.713:1 | 1.485:1 | 1.713:1 |
| Expander 容器邊界／頁面（裝飾） | 1.355:1 | 1.808:1 | 1.355:1 | 1.808:1 |

`RenderRestFillSoftReferences` 沿用既有 renderer，輸出全部 45 張重新設計圖及 20 張表單、頁籤與文字圖。
由於四個 ComboBox 檔名重複，兩組參考圖分別放在不同目錄。
`RenderRestFillNoneChoices` 與 `RenderRestFillNoneExpanders` 沿用既有狀態圖，並輸出到 `NVT_RESTFILL_NONE_IMAGES_DIR`，因此不會覆蓋 Soft 圖。
它們輸出 12 張英文圖，比例為 100%、寬度為 1200 像素，每張均小於一 MB。
