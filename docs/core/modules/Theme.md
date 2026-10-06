[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme (`Nvt.Core.Avalonia.Theme`)

Theme defines one neutral and semantic palette, radii, sizes, states and focus ring for NFC, NFH and NFU. Each tool keeps its accent. Merge the tokens into application resources and load the two style includes after Fluent. `ButtonStyles.axaml` is the only Core button style file; do not load draft PR #71's `ActionRoleStyles.axaml`. The eight legacy font values and resource resolver remain unchanged.

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
```

## Shared palette

Every existing key and type is retained. `Nfc*` names provide compatibility and do not imply an NFC-only look. Data colors stay in tools; Core adds none.

### Neutral colors

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

`NfcBorderBrush` is the identifiable input boundary; soft, muted and divider brushes are decorative. Disabled controls and scroll thumbs share the disabled color; hovered thumbs use muted text.

### Semantic colors

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

No informative text sits directly on the scrim; dialogs use opaque surfaces. Compatible success-emphasis, warning-muted, caution and critical names remain.

### Tool accents

Core defaults to NFC. In application-scope Light/Dark resources, a tool overrides only the seven `NfcAccent*` keys below. Tools override no other Core key and do not override basic control styles locally. Core adds a role when a tool needs a variant. Focus colors are independent of accents.

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

Filled buttons use `NfcSurfaceBrush` labels in Light and `NfcAppBackgroundBrush` in Dark. The style-local `PrimaryLabelBrush` is a theme alias to those existing tokens, not a tool override token.

## Sizes and radii

| Item | Token / value |
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

All values are logical pixels; do not multiply by DPI again. Button content is centered, single-line, trimmed by character ellipsis and clipped. Icons inherit their composed color role. Containers reserve 4 px for focus; 8 px between adjacent controls is recommended. Joined edges have zero radius; multiline editors and data visuals are not constrained to 32 px. Fonts and fallbacks are unchanged. This change adds no general input-control style.

## Button roles and states

Public classes are `actionPrimary`, `actionNeutral`, `actionDanger`, `actionGhost`, `actionIconButton`, `chipAction` and `Border.chipStatus`. Interactive roles support both `Button` and `ToggleButton`; a role alone gives the complete look. Former layout classes `actionTextButton`, `actionChip` and `actionButton` are not public roles. No global Button/ToggleButton rule exists; controls without a role receive no setters from this file.

| Role | State | Background | Border | Foreground |
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

`actionIconButton` defaults to `actionNeutral` and composes with any of the four color roles, inheriting every state. `chipAction` follows neutral colors; `chipAction.active` and a ToggleButton's checked state follow the same selected rules. Selected hover uses the subtle accent surface; selected press uses the neutral pressed surface, retaining strong accent text and border. Ordinary Button has no checked state. Status warning/danger/success modifiers use the semantic surfaces, text and borders above. Statuses have no interactive, selected or focus state and retain informative colors and opacity 1 when disabled.

Disabled has highest priority: every interactive role uses `NfcSurfaceSubtleBrush`, `NfcBorderMutedBrush`, `NfcTextDisabledBrush` and opacity 1 over hover, press, checked and active. The task brief gives disabled ghost buttons the subtle surface, superseding the proposal's transparent surface. No double dimming remains.

Background, border and foreground transitions last 120 ms. Pressing disables transitions; the primary pressed inner 2 px edge is an overlay that does not move content. Focus appears immediately. `.reducedMotion` on the control or any ancestor disables transitions.

### Focus ring

| Token | Light | Dark |
|---|---|---|
| `Nvt.Focus.RingBrush` | `#1F6FD1` | `#4DA3FF` |
| `Nvt.Focus.DangerRingBrush` | `#C62828` | `#FF6B6B` |

`Nvt.Focus.RingThickness` is Thickness 2; all three focus tokens live in `ThemeTokens.axaml`. Every interactive role defaults to `FocusAdorner=null`, disabling the default rectangle. Only `:focus-visible` shows the Border adorner: real Tab traversal shows it, pointer focus does not. `Margin=-4` gives a 2 px ring and 2 px exterior gap; `IsHitTestVisible=False` and `AdornerLayer.IsClipEnabled=False`. Corners follow live control radius changes: pills retain 999; each rectangular corner adds 4. `actionDanger`, including icon-danger combinations, uses the red ring; all other roles use blue. Focus changes no background, border or foreground at rest, hover, checked or active.

Owner question: keep the keyboard-only `:focus-visible` ring? This implements approved choice B.

## Role migration

The three approved `role_mapping` sections supply every NFC (59), NFH (105) and NFU (22) source class/combination below. Product-role behavior and layout stay in the tool; shared basic appearance comes from Core role composition.

| Tool | Current class | Shared role or destination |
|---|---|---|
| NFC | `action` | `actionPrimary` |
| NFC | `activityFilter` | product role, kept in the tool |
| NFC | `bankScope` | product role, kept in the tool |
| NFC | `bankViewSwitch` | product role, kept in the tool |
| NFC | `breadcrumb` | `actionGhost` |
| NFC | `browseAction` | `actionNeutral` |
| NFC | `closeButton` | product role, kept in the tool |
| NFC | `command` | `actionGhost` |
| NFC | `configSelected` | product role, kept in the tool |
| NFC | `danger` | `actionDanger` |
| NFC | `fileRevealAction` | product role, kept in the tool |
| NFC | `hexApplyChange` | `actionNeutral` |
| NFC | `hexAsciiSearch` | `actionIconButton` + `actionGhost` |
| NFC | `hexChangedBlockNavigator` | product role, kept in the tool |
| NFC | `hexChangedBlockRow` | product role, kept in the tool |
| NFC | `hexGoToAddress` | `actionIconButton` + `actionGhost` |
| NFC | `hexInspectorAction` | `actionIconButton` |
| NFC | `iconButton` | `actionIconButton` |
| NFC | `iconButton.codeBlockCopy` | `actionIconButton` |
| NFC | `inlineDisclosure` | product role, kept in the tool |
| NFC | `inlineEdit` | `actionIconButton` + `actionGhost` |
| NFC | `launcher-action` | `actionNeutral` |
| NFC | `launcher-close` | product role, kept in the tool |
| NFC | `launcher-icon` | `actionIconButton` + `actionGhost` |
| NFC | `launcher-primary` | `actionPrimary` |
| NFC | `messageCenterNavigationItem` | product role, kept in the tool |
| NFC | `nav` | product role, kept in the tool |
| NFC | `outputNameEdit` | product role, kept in the tool |
| NFC | `outputRailAction` | product role, kept in the tool |
| NFC | `primary` | `actionPrimary` |
| NFC | `primaryRailAction` | product role, kept in the tool |
| NFC | `quietDisclosure` | product role, kept in the tool |
| NFC | `railAction` | product role, kept in the tool |
| NFC | `railAction.reducedMotion` | product role, kept in the tool |
| NFC | `referenceChoice` | product role, kept in the tool |
| NFC | `reportListRow` | product role, kept in the tool |
| NFC | `reportLoad` | `actionNeutral` |
| NFC | `secondary` | `actionNeutral` |
| NFC | `segment` | product role, kept in the tool |
| NFC | `semanticAction` | `actionNeutral` |
| NFC | `semanticAction.command` | `actionGhost` |
| NFC | `semanticAction.command.configSelected` | product role, kept in the tool |
| NFC | `settingsNavItem` | product role, kept in the tool |
| NFC | `settingsNavItem.selected` | product role, kept in the tool |
| NFC | `slotClearAction` | product role, kept in the tool |
| NFC | `slotStateAction` | `chipAction` |
| NFC | `slotStateAction.checking` | `chipAction` |
| NFC | `slotStateAction.error` | `chipAction` |
| NFC | `slotStateAction.inspected` | `chipAction` |
| NFC | `slotStateAction.notApplicable` | `chipAction` |
| NFC | `slotStateAction.pendingInput` | `chipAction` |
| NFC | `slotStateAction.verified` | `chipAction` |
| NFC | `slotStateAction.warning` | `chipAction` |
| NFC | `sourceEditButton` | product role, kept in the tool |
| NFC | `summaryChip` | `chipAction` |
| NFC | `versionChoice` | product role, kept in the tool |
| NFC | `versionInstallAction` | product role, kept in the tool |
| NFC | `versionReleaseNotesAction` | product role, kept in the tool |
| NFC | `versionTableAction` | product role, kept in the tool |
| NFH | `actionButton` | `actionNeutral` |
| NFH | `actionChip` | `chipAction` |
| NFH | `actionDanger` | `actionDanger` |
| NFH | `actionGhost` | `actionGhost` |
| NFH | `actionIconButton` | `actionIconButton` |
| NFH | `actionIconButton.actionDanger` | `actionIconButton` + `actionDanger` |
| NFH | `actionIconButton.actionGhost` | `actionIconButton` + `actionGhost` |
| NFH | `actionIconButton.actionNeutral` | `actionIconButton` + `actionNeutral` |
| NFH | `actionIconButton.actionPrimary` | `actionIconButton` + `actionPrimary` |
| NFH | `actionIconButton.consoleHeaderAction` | product role, kept in the tool |
| NFH | `actionIconButton.panelChromeToggle` | product role, kept in the tool |
| NFH | `actionIconButton.viewportOverlayAction` | product role, kept in the tool |
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
| NFH | `chipAction.combined` | product role, kept in the tool |
| NFH | `chipAction.direct` | product role, kept in the tool |
| NFH | `chipAction.duplicate` | product role, kept in the tool |
| NFH | `chipAction.geometry` | product role, kept in the tool |
| NFH | `chipAction.hidden` | product role, kept in the tool |
| NFH | `chipAction.incoming` | product role, kept in the tool |
| NFH | `chipAction.layer` | product role, kept in the tool |
| NFH | `chipAction.legacy` | product role, kept in the tool |
| NFH | `chipAction.linked` | product role, kept in the tool |
| NFH | `chipAction.nocad` | product role, kept in the tool |
| NFH | `chipAction.outgoing` | product role, kept in the tool |
| NFH | `chipAction.transfer` | product role, kept in the tool |
| NFH | `chipStatus` | `chipStatus` |
| NFH | `chipStatus.danger` | `chipStatus.danger` |
| NFH | `chipStatus.success` | `chipStatus.success` |
| NFH | `chipStatus.warning` | `chipStatus.warning` |
| NFH | `confirmDialogActionButton` | `actionNeutral` |
| NFH | `confirmDialogDangerButton` | `actionDanger` |
| NFH | `consoleHeaderAction` | product role, kept in the tool |
| NFH | `danger` | `actionDanger` |
| NFH | `dangerTextButton` | `actionDanger` |
| NFH | `dangerTextButton.dxfEditMiniAction` | `actionIconButton` + `actionDanger` |
| NFH | `dxfEditCompactIconAction` | `actionIconButton` |
| NFH | `dxfEditMiniAction` | `actionIconButton` |
| NFH | `dxfEditSummaryChip` | product role, kept in the tool |
| NFH | `dxfLayerBulkToggleButton` | product role, kept in the tool |
| NFH | `icon` | product role, kept in the tool |
| NFH | `icon.ghost` | product role, kept in the tool |
| NFH | `icon.ghost.workflowOverviewAction` | product role, kept in the tool |
| NFH | `icon.ghost.workflowStepAction` | product role, kept in the tool |
| NFH | `iconTextButton` | `actionNeutral` |
| NFH | `notchExportRestoreHintAction` | product role, kept in the tool |
| NFH | `padInfoActionButton` | `actionNeutral` |
| NFH | `padInfoActionButton.compact` | `actionNeutral` |
| NFH | `padInfoAllocationButton` | product role, kept in the tool |
| NFH | `padInfoHeaderActionButton` | `actionGhost` |
| NFH | `padInfoInfoButton` | `actionIconButton` + `actionGhost` |
| NFH | `padInfoOverrideIconButton` | `actionIconButton` |
| NFH | `padInfoSectionToggle` | product role, kept in the tool |
| NFH | `padInfoSectionToggle.debugCard` | product role, kept in the tool |
| NFH | `panelBlockHeader` | product role, kept in the tool |
| NFH | `panelChromeToggle` | product role, kept in the tool |
| NFH | `rightPanelBrowserTab` | product role, kept in the tool |
| NFH | `rightPanelTab` | product role, kept in the tool |
| NFH | `settingsNavItem` | product role, kept in the tool |
| NFH | `shellTab` | product role, kept in the tool |
| NFH | `shellTab.settingsWindowNavTab` | product role, kept in the tool |
| NFH | `simulationActionButton` | `actionPrimary` |
| NFH | `validationCountPill` | product role, kept in the tool |
| NFH | `validationRowKindTag` | product role, kept in the tool |
| NFH | `validationRowKindTag.direct` | product role, kept in the tool |
| NFH | `validationRowKindTag.incoming` | product role, kept in the tool |
| NFH | `validationRowKindTag.legacy` | product role, kept in the tool |
| NFH | `validationRowKindTag.nocad` | product role, kept in the tool |
| NFH | `validationRowKindTag.outgoing` | product role, kept in the tool |
| NFH | `validationRowKindTag.workspaceContextBadge` | product role, kept in the tool |
| NFH | `validationTraceRow` | product role, kept in the tool |
| NFH | `verificationSummaryChip` | product role, kept in the tool |
| NFH | `verificationSummaryChip.active` | product role, kept in the tool |
| NFH | `verificationSummaryChip.direct` | product role, kept in the tool |
| NFH | `verificationSummaryChip.incoming` | product role, kept in the tool |
| NFH | `verificationSummaryChip.legacy` | product role, kept in the tool |
| NFH | `verificationSummaryChip.nocad` | product role, kept in the tool |
| NFH | `verificationSummaryChip.outgoing` | product role, kept in the tool |
| NFH | `verificationSummaryChip.transfer` | product role, kept in the tool |
| NFH | `verificationTableRow` | product role, kept in the tool |
| NFH | `verificationTableRow.verificationTableRowActive` | product role, kept in the tool |
| NFH | `viewMenuButton` | `actionNeutral` |
| NFH | `viewportOverlayAction` | product role, kept in the tool |
| NFH | `workspaceActionButton` | `actionNeutral` |
| NFH | `workspaceDataRow` | product role, kept in the tool |
| NFH | `workspaceDataRow.workspaceDataRowActive` | product role, kept in the tool |
| NFH | `workspaceFoldHeader` | product role, kept in the tool |
| NFH | `workspaceGroupActionButton` | `actionNeutral` |
| NFH | `workspaceHeaderAction` | `actionGhost` |
| NFH | `workspaceHeaderInspectorCard` | product role, kept in the tool |
| NFH | `workspaceInfoButton` | `actionIconButton` + `actionGhost` |
| NFH | `workspaceSearchClearButton` | `actionIconButton` + `actionGhost` |
| NFH | `workspaceSubtleAction` | `actionGhost` |
| NFH | `workspaceTableHeaderButton` | product role, kept in the tool |
| NFH | `workspaceTableHeaderButton.active` | product role, kept in the tool |
| NFH | `workspaceTextAction` | `actionGhost` |
| NFH | `workspaceToggleChip` | `chipAction` |
| NFU | `axisToggle` | product role, kept in the tool |
| NFU | `canvasIcon` | product role, kept in the tool |
| NFU | `canvasQuick` | product role, kept in the tool |
| NFU | `canvasQuick.active` | product role, kept in the tool |
| NFU | `ghost` | `actionGhost` |
| NFU | `headerAction` | `actionGhost` |
| NFU | `headerAction.headerPrimary` | `actionPrimary` |
| NFU | `headerIcon` | `actionIconButton` + `actionGhost` |
| NFU | `inspectorIcon` | `actionIconButton` + `actionGhost` |
| NFU | `outputPreviewPlay` | product role, kept in the tool |
| NFU | `outputPreviewPlay.playing` | product role, kept in the tool |
| NFU | `paintMarkerRemove` | `actionIconButton` + `actionDanger` |
| NFU | `primary` | `actionPrimary` |
| NFU | `rawSourceOpen` | `actionGhost` |
| NFU | `settingsChoice` | product role, kept in the tool |
| NFU | `settingsNav` | product role, kept in the tool |
| NFU | `settingsNav.active` | product role, kept in the tool |
| NFU | `shortcutRow` | product role, kept in the tool |
| NFU | `sourceLineLink` | `actionGhost` |
| NFU | `toolbarAction` | `actionNeutral` |
| NFU | `transportPlay` | product role, kept in the tool |
| NFU | `transportPlay.playing` | product role, kept in the tool |

Former Core `railActionIcon`, `railActionLabel`, `railActionIconSlot`, `NfcSemanticButtonTheme` and theme resources used only by old roles also leave Core: product role, kept in the tool. `codeBlockCopy` maps to `actionIconButton`; `primaryRailAction` and `fileRevealAction` are product roles. Tools remove the old file inclusion and layout classes; no compatibility aliases are supplied.

Core consumers: ConfirmDialog uses primary for confirmation, neutral for cancel and danger for emphasized cancel; WarningDialog uses primary for OK. CollapsiblePanel uses ghost for its header, retaining expansion binding, stretched content and chevron direction. The panel root does not clip the exterior focus ring.

## NFC legacy font extraction

`Theme/NfcLegacyFontTokens.axaml` preserves the eight font resources from NFC's `ThemeTokens.axaml`, lines 218–225, at the frozen commit below.
The dictionary contains exactly two family lists and six sizes in Avalonia device-independent pixels.
`Theme/ThemeTokens.axaml` merges this dictionary and exposes all eight original keys through `StaticResource` aliases.

| Core key | NFC compatibility key | Frozen value |
|---|---|---|
| `Nvt.Font.NfcLegacy.Ui.Family` | `NfcUiFontFamily` | `fonts:Inter#Inter, Microsoft JhengHei UI, Noto Sans CJK TC, Noto Sans TC, Segoe UI` |
| `Nvt.Font.NfcLegacy.Technical.Family` | `NfcTechnicalFontFamily` | `Cascadia Mono, Consolas` |
| `Nvt.Font.NfcLegacy.Size10` | `NfcFontSize10` | `10` |
| `Nvt.Font.NfcLegacy.Size11` | `NfcFontSize11` | `11` |
| `Nvt.Font.NfcLegacy.Size12` | `NfcFontSize12` | `12` |
| `Nvt.Font.NfcLegacy.Size13` | `NfcFontSize13` | `13` |
| `Nvt.Font.NfcLegacy.Size14` | `NfcFontSize14` | `14` |
| `Nvt.Font.NfcLegacy.Size16` | `NfcFontSize16` | `16` |

This extraction adds no fonts, role table, or fallback changes.
The family order and existing Inter registration remain unchanged.
The legacy resources serve existing NFC consumers.

For font-only adoption, NFC merges the Core font dictionary before defining its eight compatibility keys in its own resource dictionary.
Use the mapping above for every alias, as this example shows:

```xml
<ResourceDictionary.MergedDictionaries>
  <ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/NfcLegacyFontTokens.axaml" />
</ResourceDictionary.MergedDictionaries>
<StaticResource x:Key="NfcFontSize14" ResourceKey="Nvt.Font.NfcLegacy.Size14" />
```

NFC must retain `StaticResource NfcFontSize14` in these existing source files:

- `src/NvtFwCombiner.Presentation.Avalonia/Resources/MainWindowSharedTemplates.axaml`
- `src/NvtFwCombiner.Presentation.Avalonia/Resources/SettingsEventBufferFormatPageTemplate.axaml`

NFC must also preserve:

- Existing styles and inline values.
- Font sizes, weights, and inheritance rules.
- Static and dynamic resource lookup modes.
- Font assets, Inter package version, and fallback order.


## Verification and provenance

This replaces the NFC-derived palette and button roles, reusing PR #71 templates and focus approach in the single `ButtonStyles.axaml` while fixing disabled and active priority. `ThemeTokens.xml` and `ButtonStyles.xml` were deliberately regenerated from the newly approved files; they no longer claim to preserve the old NFC appearance. `ExtractedXamlMatchesFrozenBaseline` still freezes the complete XML, expanding the eight existing font aliases for token comparison. Scroll geometry and its baseline are not changed by this palette update.

The earlier NFC extraction and unchanged fonts came from `origin/1.2.x`, commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. PR #71 role work is pinned at `0f29143712cc18127df8e00636b1bfb3552d3aef`; its baselines are superseded. The shared proposal and NFH role naming approved on 2026-10-06 specify this change.

- `ButtonThemeTests`: ThemeTokens/ButtonStyles only; both themes and control types, every role/state, geometry, single-line/ellipsis/clipping, unclassed controls, real Tab/pointer focus, ring brushes/geometry/hit testing, no color change on focus in four states, disabled priority and reduced motion.
- `PaletteContrastTests`: compiled tokens, all three accents in one data-driven test; body, placeholder, semantic and accent text and filled labels at 4.5:1; disabled text, input borders, rings and scroll thumbs at 3:1. All opaque neutral/semantic/accent-tint surfaces are checked, with no documented exceptions. Decorative boundaries and translucent scrims do not carry text.
- `ThemeContractTests`: every Theme static/dynamic resource resolves in both variants; button selectors require roles; all three XML baseline comparisons remain.
- `NfcLegacyFontTests` and resource resolver tests are unchanged; Dialogs/Panels tests now use shared roles and preserve the other behavior contracts.

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

After tests pass, a temporary headless helper loads Fluent, ThemeTokens, ButtonStyles and ScrollStyles and writes `artifacts/palette-gallery/roles-dark.png`, `roles-light.png`, `controls-dark.png` and `controls-light.png`. Outputs are git-ignored; the helper is then deleted. The gallery compares fixed states side by side and does not replace real input tests.

### Adoption

Each tool adopts in its own PR, applying its listed accent and removing local basic-control overrides. Every adoption PR attaches before and after images for owner approval, covering Light/Dark, English/Traditional Chinese, states, DPI and long labels. Product behavior and data colors stay in tools. This task adopts no tool and makes no commit.

## Scroll styles

`Theme/ScrollStyles.axaml` makes NFC's scroll bar look the shared Core look. It adds NFH's two opt-in classes that keep content inside the viewport width.
The styles change parts of the Fluent `ScrollBar` template, so a tool that includes them must use the Fluent theme.
NFC loads it in `src/NvtFwCombiner.Presentation.Avalonia/App.axaml` at `085f71c`. NFH loads it in `src/FreeformHelper.UI/App.axaml` at `847cc45`.
Include the file at the position where the tool's own scroll styles were, so that the style order stays the same:

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
```

What the styles do:

- Every `ScrollViewer`, and every templated control through the attached `ScrollViewer.AllowAutoHide`, keeps its scroll bars visible.
- A vertical bar is 14 px wide, and a horizontal bar is 14 px high. The track is transparent.
- The thumb is 6 px thick, centred in the bar, pill-shaped, and uses `NfcTextDisabledBrush`. It changes to `NfcTextMutedBrush` while the pointer is over the bar or the thumb.
- The track rectangle and the line buttons are fully transparent. The line buttons still take space.
- `ScrollViewer.viewportBoundScroll` stretches its content and removes its padding. Inside it, `Border.viewportBoundContent` takes the viewport width, so the vertical bar never covers or clips the content. Use both with `HorizontalScrollBarVisibility="Disabled"`.

Which rules need the Fluent template:

- The thumb, track rectangle, line button and thumb-border rules select Fluent template parts (`/template/ Thumb`, `/template/ Rectangle#TrackRect`, `/template/ RepeatButton` and the thumb's `Border`). Under another theme they match nothing.
- The bar size, bar background and auto-hide rules set properties on the `ScrollBar` and `ScrollViewer` themselves. They apply under any theme.
- The viewport-bound classes bind to `ScrollViewer.Viewport`. The 14 px lane they leave for the vertical bar comes from the Fluent `ScrollViewer` template.

### Scroll frozen sources and checks

The file is built from two frozen sources. The scroll bar look comes from NFC. Only the two viewport-bound classes come from NFH.
In the lines taken from each source, every selector, setter, value and the order are unchanged:

- NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `085f71cfaf9d1f592759d1c58b5bdc4f7b572902`, `src/NvtFwCombiner.Presentation.Avalonia/Styles/MainWindowControlStyles.axaml`, lines 117-185. NFC's `ScrollViewer#SupportMatrixScroll` rule at line 186 is product-specific and stays in NFC.
- NFH (`nvt-freeform-helper`), ref `origin/1.3.x`, full commit `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`, `src/FreeformHelper.UI/Styles/Controls.Scroll.axaml`, lines 12-19 (the two viewport-bound classes only).

NFH's own scroll bar rules (lines 8-94 of the same NFH file) are not ported. Core uses NFC's look, and tools adopt Core's colors, as [conventions](../conventions.md) requires.

Checks:

- `ExtractedXamlMatchesFrozenBaseline` compares the file with `Baseline/ScrollStyles.xml`, which was built from the same source lines.
- `ScrollStylesTests` checks that every resource key exists in both themes, that the viewport-bound classes come last, the bar size and auto-hide setters in both orientations, and auto-hide on scroll viewers and templated controls.
- Under the Fluent template, `ScrollStylesTests` also checks both orientations in Light and Dark: the 14 px lane, the centred 6 px thumb, the hidden track and line buttons, the hover brush, dragging the thumb, and paging with a click on the hidden track. A `ListBox`'s inner scroll viewer also keeps its bars visible. It checks that viewport-bound content takes the viewport width. It also checks that the width settles in the same layout pass each time the vertical bar appears or disappears, and that it does not oscillate.
- The test project references `Avalonia.Themes.Fluent` 12.1.1 for these checks. Scroll tests and the temporary palette gallery load `FluentTheme` in their own windows. The packages gain no dependency.
- The same seven runtime cases for NFC's rules passed against a temporary copy of the full frozen NFC file. The copy left out three styles that select an NFC view type, because Core cannot compile them. The copy is not retained.

For adoption with the shared palette:

- NFC replaces lines 117-185 with the include at the same position. Geometry stays the same; the shared palette changes the thumb colors. Run its UI smoke tests and attach before and after captures with the same OS, fonts, DPI and theme for owner review.
- NFH will look and behave differently. The owner must approve the change in NFH's adoption PR, which attaches before and after screenshots. Its non-UI tests must keep the same list and outcomes.
- NFH keeps `scrollV2`, `scrollDevCandidate`, `workspaceDataList`, `workspaceGroupStripScroll` and `DevScrollPreviewHeight`.

What changes in NFH on adoption:

| Item | NFH now (`847cc45`) | Core |
|---|---|---|
| Bar thickness | 2.5 px (`ScrollBarThickness`) | 14 px |
| Bar background | `BrushScrollTrack` | Transparent |
| Thumb | Fills the bar, `BrushScrollThumb` at rest, hover and pressed, `RadiusPill` | 6 px, centred, `NfcTextDisabledBrush`, `NfcTextMutedBrush` on hover, `NfcPillCornerRadius` |
| Thumb minimum length | 36 (`ScrollBarThumbMinLength`) | Not set |
| Line buttons | `IsVisible=False`, take no space | `Opacity=0`, still take space |
| Track rectangle | Not changed | Hidden |
| Pressed rules | Present | None |

Adopters: none yet.

## Resource resolver

`UiResourceResolver` reads one theme resource for a control that draws in code. Each method takes the owner control, the key and a fallback value.

| Method | Accepted resource types |
|---|---|
| `GetBrush` | any `IBrush`, or a `Color` passed to the caller's brush factory |
| `GetColor`, `TryGetColor` | `Color`, or the color of a `SolidColorBrush` |
| `GetDouble` | `double`, `float` or `int` |
| `GetCornerRadius` | `CornerRadius` |
| `GetThickness` | `Thickness` |

A lookup has three steps:

1. It searches the owner and its styling parents with the owner's actual theme variant. A control outside any tree has a null variant, so the lookup uses `ThemeVariant.Default`.
2. If that fails and `UiThread.IsCurrent` succeeds, it searches the current application with the same variant.
3. Otherwise it returns the fallback.

A wrong resource type also returns the fallback. With the Default variant, keys that exist only in Light and Dark dictionaries are not found.
Call the resolver on the UI thread. It does not cache values or watch theme changes. A control that caches resolved values must refresh them when its theme variant changes.
Register the UI dispatcher with `UiThread.RegisterRunningDispatcher` at startup. Without the registration, step 2 is skipped.

### Resolver frozen source and checks

Frozen parent: NFH (`Dennis40816/nvt-freeform-helper`), ref `origin/1.3.x`, full commit `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`.
Extracted source path: `src/FreeformHelper.UI/Services/UiResourceResolver.cs`.
The Core version keeps the NFH signatures and behavior. It changes only the namespace, the `UiThread` import, the visibility (public) and the documentation.

`tests/Nvt.Core.Avalonia.Tests/Theme/FrozenNfhUiResourceResolver.cs` keeps a frozen copy of the NFH file. Only its namespace, class name and `UiThread` import differ. Core's `UiThread` is the unchanged port of NFH's.
`UiResourceResolverTests` runs each of its 7 cases against both versions, and both must give the same results:

- Light and Dark for a control in a window, for a window key and an application key.
- Brush resources, brushes built from colors, and the factory call count.
- Colors from `Color` and `SolidColorBrush` only. An `ImmutableSolidColorBrush` returns the fallback.
- Number conversion and fallback for other types.
- Corner radius and thickness types.
- A control outside any tree: a null variant, application top-level keys found, Light and Dark keys not found.
- No registered dispatcher: the application step is skipped.

The test class runs in a collection without parallel tests, because one case clears the shared dispatcher registration.

For zero-difference adoption in NFH:

- Before NFH deletes its copy, the Core tests pass against both versions.
- The full NFH test list and outcomes match the frozen parent.
- NFH's `ui-visual-minimal-baseline.json` hashes and notch golden outputs stay unchanged.
- All NFH callers share one dispatcher registration. Either NFH's `UiThread` calls move to Core's `UiThread`, or NFH registers the dispatcher with both while both exist. About 20 NFH call sites read NFH's own `UiThread`, so registering only with Core's breaks them. Registering only with NFH's skips step 2 without an error.

Adopters: none. NFH adoption is pending.
