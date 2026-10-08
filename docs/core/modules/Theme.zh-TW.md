[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme（`Nvt.Core.Avalonia.Theme`）

Theme 為 NFC、NFH、NFU 定義同一套中性色、語意色、圓角、尺寸、狀態與焦點框。各工具只保留主色。將 token 合併至應用程式資源，並在 Fluent 之後載入三個樣式檔。只有 `ButtonStyles.axaml` 是 Core 按鈕樣式檔。八個舊有字型值與資源解析器保持不變。

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml" />
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
| Single-line field guidance / 單行欄位原則 | 32, padding / 內距 10,0; radius / 圓角 6 |
| Spacing / 間距 | `NfcSpace2/4/8/12/16/24`; `NfcFieldSpacing` = 4 |

以上皆為邏輯像素，不再額外乘 DPI。按鈕內容置中、單行、字元省略並裁切；圖示角色繼承所組合色彩角色。焦點需容器預留 4 px；相鄰控制項建議間隔 8 px。接合邊為零圓角；多行編輯器與資料視覺不套用 32 px。字型與字型 fallback 不變。本次沒有增加一般輸入框樣式。

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

基準版本沒有 `toggleSoft` 資源，因此清單別名直接對應 Core 色票。
同時覆寫 `Nvt.List.Selected*` 資源，即可在執行時替換選取色票。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.List.SelectedBrush` | `#F7F9FE`，`NfcAccentSurfaceSubtleBrush` | `#162034`，`NfcAccentSurfaceSubtleBrush` |
| `Nvt.List.SelectedPointerOverBrush` | `#EFF3FD`，`NfcAccentSurfaceBrush` | `#1A2940`，`NfcAccentSurfaceBrush` |
| `Nvt.List.SelectedPressedBrush` | `#EFF3FD`，`NfcAccentSurfaceBrush` | `#1A2940`，`NfcAccentSurfaceBrush` |
| `Nvt.List.SelectedLabelBrush` | `#1148BE`，`NfcAccentStrongBrush` | `#8FBFFB`，`NfcAccentStrongBrush` |
| `Nvt.List.TransparentBrush` | `#00FFFFFF`，`Nvt.Toggle.TransparentBrush` | 相同 |
| `Nvt.List.CompactHeight` | 24 | 24 |

項目使用 `Nvt.Shape.ControlCornerRadius`：Pill 為 999，Square 為 6。
標記使用 `Nvt.Shape.RoundCornerRadius`。
焦點沿用 `Nvt.Focus.RingBrush` 與 `Nvt.Focus.RingThickness`。
顏色轉場持續 150 ms，項目或上層的 `reducedMotion` 類別可停用轉場。

## Menu、MenuItem、ContextMenu 與選單分隔線

`MenuStyles.axaml` 提供選單列、彈出命令、右鍵選單與選單分隔線。
選單項目點擊範圍高 32 DIP，padding 為 10,0。
項目表面四周預留 4 DIP，容納外側焦點框。
如此可在捲動範圍內保留一個 2 DIP 框與 2 DIP 間隔。

選單表面使用 `NfcSurfaceBrush`、1 DIP 的 `NfcBorderBrush` 框線與 padding 4。
表面圓角跟隨共用形狀，並以現有表面圓角 token 為上限。
Pill 表面圓角為 8 DIP，Square 為 6 DIP。
表面四周預留透明陰影空間，彈出位置的 offset 會補償這段空間。

### 選單狀態與 token

滑鼠移入、原生選單選取與鍵盤焦點使用 `NfcSelectionSurfaceBrush`。
按下使用 `NfcSecondaryActionPressedBrush`。
停用內容使用 `NfcTextDisabledBrush`，opacity 維持 1。
勾選項目顯示勾號。有圖示時使用 20 DIP 圖示欄。
快捷鍵文字使用 `NfcTextMutedBrush`，子選單保留箭頭。
選單列採用相同項目狀態與高度。

焦點框跟隨 Avalonia 的 `:focus-visible` 狀態。
原生方向鍵導覽保留 Avalonia 的選單選取行為。
樣式保留方向鍵、Enter、Escape、存取鍵、命令與自動化功能。
選單分隔線高 1 DIP，使用 `NfcBorderBrush`。
舊式 `MenuItem Header="-"` 分隔線也採用相同外觀。
顏色轉場持續 150 ms，`reducedMotion` 也會停用選單轉場。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Menu.PopupShadow` | `0 4 12 0 #26000000` | `0 4 12 0 #66000000` |
| `Nvt.Menu.PopupShadowMargin` | 16 | 16 |
| `Nvt.Menu.PopupMaximumCornerRadius` | 8，`NfcSurfaceCornerRadius` | 8，`NfcSurfaceCornerRadius` |
| `Nvt.Menu.ChevronGeometry` | `M1 1 L5 5 L1 9` | 相同 |

陰影透明度色碼是唯一新增的顏色常值，定義於 `ListTokens.axaml`。
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
| 選取清單文字與標記 | 7.018:1 | 7.674:1 |
| 停用文字 | 3.903:1 | 4.275:1 |
| 焦點框對項目填色 | 4.006:1 | 4.930:1 |

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
| `Nvt.Toggle.SelectedPressedBrush` | `#1148BE` (`NfcAccentStrongBrush`) | `#0E3C9E` |
| `Nvt.Toggle.SelectedLabelBrush` | `#FFFFFF` | `#FFFFFF` |
| `Nvt.Toggle.DangerFillBrush` | `#A82035` (`NfcDangerTextBrush`) | `#A82035` |
| `Nvt.Toggle.DangerFillPointerOverBrush` | `#861B2C` (`NfcDangerTextStrongBrush`) | `#861B2C` |
| `Nvt.Toggle.DangerFillPressedBrush` | `#861B2C` (`NfcDangerTextStrongBrush`) | `#861B2C` |
| `Nvt.Toggle.SwitchOnBrush` | `#2563EB` | `#2563EB` |
| `Nvt.Toggle.SwitchPointerOverBrush` | `#1D4ED8` | `#1D4ED8` |
| `Nvt.Toggle.SwitchPressedBrush` | `#1D4ED8` | `#1D4ED8` |
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

兩種形狀下，開關軌道與開關焦點框皆使用 `Nvt.Shape.RoundCornerRadius`。
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
| On pointer over 或 pressed | `Nvt.Toggle.SoftPointerOverBrush` | `Nvt.Toggle.SoftCheckedForegroundBrush` |
| Disabled，off | `Nvt.Toggle.TransparentBrush` | `Nvt.Toggle.SoftDisabledForegroundBrush` |
| Disabled，on | `Nvt.Toggle.SoftDisabledCheckedBrush` | `Nvt.Toggle.SoftDisabledForegroundBrush` |
| Keyboard focus | 保留目前填色。 | 保留目前前景色。 |

以下別名在兩種主題皆重用既有 Core 資源，不新增寫死的顏色。
替換包含這些 `Nvt.Toggle.Soft*` 鍵的單一字典，即可一起更新已掛載的控制項。

| Token | Core 資源 | Light | Dark |
| --- | --- | --- | --- |
| `Nvt.Toggle.SoftCheckedBrush` | `NfcAccentSurfaceBrush` | `#EFF3FD` | `#1A2940` |
| `Nvt.Toggle.SoftCheckedForegroundBrush` | `NfcAccentStrongBrush` | `#1148BE` | `#8FBFFB` |
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

鍵盤焦點使用 `focus-visible`、`Nvt.Focus.RingBrush` 與 `Nvt.Focus.RingThickness`，外側間隙為 2 px。
指標焦點不顯示焦點框。
背景與前景轉場皆為 150 ms。
按下時使用 0.98 繪製縮放，配置尺寸與點擊範圍不變。
Space 切換數值，Tab 聚焦控制項，停用控制項忽略輸入。

文字與圖示繼承相同前景色，在兩種主題皆達到 4.5:1 對比。
On 的對比在 Light 為 7.018:1，Dark 為 7.674:1。
On pointer over 與 pressed 分別為 6.673:1 與 7.672:1。
焦點環對頁面底與勾選淡色底的對比，亮色為 4.446:1 至 4.938:1，暗色為 5.573:1 至 7.131:1。測試要求至少 3:1。
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

`ChoiceStyles.axaml` 為原生核取方塊提供共用 Core 外觀，不需指定外觀 class。
將 `ThemeTokens.axaml` 合併至應用程式資源，並在 Fluent 後載入 `ChoiceStyles.axaml`。

指示框尺寸為 20 × 20 DIP。標籤使用 `NfcTextBrush`、`NfcUiFontFamily` 與 13 DIP 的 `NfcFontSize13` 內文字級。
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
鍵盤焦點在整列外側顯示一個 2 DIP 的 `Nvt.Focus.RingBrush` 焦點框，間隔為 2 DIP。
預設焦點裝飾器已關閉。
筆刷轉場為 150 ms。控制項或祖先加上 `reducedMotion` 即可關閉轉場。

| 狀態 | 指示框底色 | 指示框外框 | 標籤 |
| --- | --- | --- | --- |
| 未勾選靜止 | `NfcSurfaceBrush` | `NfcBorderBrush` | `NfcTextBrush` |
| 未勾選指標移入 | `NfcSurfaceSubtleBrush` | `NfcTextSecondaryBrush` | `NfcTextBrush` |
| 未勾選按下 | `NfcSelectionSurfaceBrush` | `NfcTextStrongBrush` | `NfcTextBrush` |
| 勾選或不確定 | `Nvt.Toggle.SelectedBrush` | `NfcAccentBorderBrush` | `NfcTextBrush` |
| 已選取指標移入 | `Nvt.Toggle.SelectedPointerOverBrush` | `NfcAccentBorderStrongBrush` | `NfcTextBrush` |
| 已選取按下 | `Nvt.Toggle.SelectedPressedBrush` | `NfcAccentBorderStrongBrush` | `NfcTextBrush` |
| 停用未勾選 | `NfcSurfaceSubtleBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| 停用已選取 | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| 鍵盤焦點 | 保留目前底色 | 保留目前外框 | 保留目前標籤 |

停用控制項忽略指標、按下與鍵盤焦點樣式。
兩種主題的白色選取內容均使用 `Nvt.Toggle.SelectedLabelBrush`。
深色主題的選取外框使用較亮的 Core 強調色，確保指示框與鄰近底色可辨識。

### 選項 token

`ThemeTokens.axaml` 包含 `ChoiceTokens.axaml`。其中六個幾何 token 在 Light 與 Dark 使用相同值。
所有顏色與單選圓角均使用既有 Core token。唯一新增的值是核取方塊圓角，定義為 `Nvt.Choice.CheckBoxCornerRadius`。此控制項家族不新增顏色常數。

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Choice.IndicatorSize` | 20 | 20 |
| `Nvt.Choice.DotSize` | 10 | 10 |
| `Nvt.Choice.CompactHeight` | 24 | 24 |
| `Nvt.Choice.CheckBoxCornerRadius` | 6 | 6 |
| `Nvt.Choice.RowPadding` | `0,6` | `0,6` |
| `Nvt.Choice.CompactPadding` | `0,2` | `0,2` |
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
| `Nvt.Toggle.SelectedPressedBrush` | `#1148BE` | `#0E3C9E` |
| `Nvt.Toggle.SelectedLabelBrush` | `#FFFFFF` | `#FFFFFF` |
| `Nvt.Toggle.TransparentBrush` | `#00FFFFFF` | `#00FFFFFF` |
| `Nvt.Focus.RingBrush` | `#1F6FD1` | `#4DA3FF` |
| `Nvt.Focus.RingThickness` | 2 | 2 |

勾號沿用 `NfcDoneIconGeometry`。
核取方塊圓角使用 `Nvt.Choice.CheckBoxCornerRadius`（6），Pill 與 Square 相同，避免看起來像單選圓環。單選指示點維持圓形。
整列焦點框使用 `Nvt.Shape.FocusCornerRadius`：Pill 999、Square 10。
在資源根節點呼叫 `ThemeShapes.SetShape`，即可更新已掛載選項而不替換模板。
在同一色票字典覆寫既有筆刷資源，可於執行期更新顏色。

以下最小對比涵蓋兩種形狀與五種鄰近底色，包含按下時的底色。

| 對比 | Light | Dark |
| --- | --- | --- |
| 啟用標籤 | 11.866:1 | 10.501:1 |
| 停用標籤 | 3.698:1 | 3.783:1 |
| 所有狀態的指示框外框 | 3.257:1 | 3.256:1 |
| 所有選取狀態的白色圖示，含停用 | 4.559:1 | 3.422:1 |
| 啟用勾選底色上的白色圖示 | 5.879:1 | 7.794:1 |
| 鍵盤焦點框 | 4.006:1 | 4.930:1 |

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
