[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme（`Nvt.Core.Avalonia.Theme`）

Theme 為 NFC、NFH、NFU 定義同一套中性色、語意色、圓角、尺寸、狀態與焦點框。各工具只保留主色。將 token 合併至應用程式資源，並在 Fluent 之後載入兩個樣式檔。只有 `ButtonStyles.axaml` 是 Core 按鈕樣式檔；不載入草稿 PR #71 的 `ActionRoleStyles.axaml`。八個舊有字型值與資源解析器保持不變。

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
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

Core 預設採 NFC 值。工具在應用程式資源範圍的 Light／Dark 字典只覆寫下表七個 `NfcAccent*` 鍵。不得覆寫任何其他 Core 鍵；不得局部覆寫基本控制項樣式。需要變體時由 Core 增加角色。焦點色不跟隨主色。

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

Light 填色按鈕的標籤採 `NfcSurfaceBrush`；Dark 採 `NfcAppBackgroundBrush`。樣式內的 `PrimaryLabelBrush` 只是這兩個既有 token 的主題別名，並非可供工具覆寫的 token。

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
| `actionPrimary` | rest | `NfcAccentBrush` | `NfcAccentBrush` | `NfcSurfaceBrush (Light) / NfcAppBackgroundBrush (Dark)` |
| `actionPrimary` | hover | `NfcAccentStrongBrush` | `NfcAccentStrongBrush` | `NfcSurfaceBrush (Light) / NfcAppBackgroundBrush (Dark)` |
| `actionPrimary` | pressed | `NfcAccentStrongBrush` | `NfcAccentStrongBrush` | `NfcSurfaceBrush (Light) / NfcAppBackgroundBrush (Dark)` |
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

停用最優先：所有互動角色採 `NfcSurfaceSubtleBrush`、`NfcBorderMutedBrush`、`NfcTextDisabledBrush`、opacity 1，覆蓋滑入、按下、checked 與 active。依工作簡述，ghost 停用也採次表面，取代提案的透明底色。沒有雙重淡化。

背景、邊界、文字轉場為 120 ms。按下時關閉轉場；primary 按下的內側 2 px 邊線由不占版面的覆蓋層繪製。焦點立即顯示。控制項或任何祖先加 `.reducedMotion` 會關閉轉場。

### 焦點框

| Token | Light | Dark |
|---|---|---|
| `Nvt.Focus.RingBrush` | `#1F6FD1` | `#4DA3FF` |
| `Nvt.Focus.DangerRingBrush` | `#C62828` | `#FF6B6B` |

`Nvt.Focus.RingThickness` 為 Thickness 2，三個焦點 token 全在 `ThemeTokens.axaml`。每個互動角色預設 `FocusAdorner=null`，取消預設矩形。只有 `:focus-visible` 顯示 Border adorner；真實 Tab 顯示，指標焦點不顯示。`Margin=-4` 形成 2 px 線與 2 px 外側間隙；`IsHitTestVisible=False`、`AdornerLayer.IsClipEnabled=False`。圓角隨控制項動態變化：膠囊保留 999，矩形每個角加 4。`actionDanger`（含圖示危險組合）採紅框，其餘採藍框。焦點在靜止、滑入、checked、active 均不改底色、邊界或文字。

擁有者待確認：是否維持僅鍵盤的 `:focus-visible` 焦點框？本次依核准選擇 B 實作。

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

- `ButtonThemeTests`：僅載入 ThemeTokens／ButtonStyles；Light／Dark、兩種控制項、每個角色與狀態、幾何、單行／省略／裁切、未指定角色、真實 Tab 與指標焦點、焦點 brush／幾何／命中測試、四種狀態焦點不改色、停用優先與減少動態。
- `PaletteContrastTests`：編譯後 token，三工具主色同一資料驅動測試；內文、提示、語意文字、主色文字與填色標籤 4.5:1；停用文字、輸入邊界、焦點、捲軸拇指 3:1。檢查全部不透明中性／語意／主色底色，無例外測試。裝飾邊界與半透明 scrim 不承載文字。
- `ThemeContractTests`：所有 Theme 檔的 StaticResource／DynamicResource 在兩個主題均解析；只允許有角色的按鈕 selector；保留三份 XML baseline 比對。
- `NfcLegacyFontTests` 與資源解析器測試不變；Dialogs／Panels 的測試改為共用角色，其他行為合約不變。

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

暫存 headless helper 在測試通過後載入 Fluent、ThemeTokens、ButtonStyles、ScrollStyles，產生 `artifacts/palette-gallery/roles-dark.png`、`roles-light.png`、`controls-dark.png`、`controls-light.png`。輸出受 git ignore；helper 隨後刪除。圖庫以固定狀態並排比較，不取代真實輸入測試。

### 採用

每套工具各自提出採用 PR，套用表中主色並移除局部基本控制項覆寫。每個 PR 附採用前後影像供 owner 核准，涵蓋 Light／Dark、英文／繁中、各互動狀態、DPI 與長文字；產品行為與資料色留在工具。本次不採用到任何工具、不提交變更。

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
