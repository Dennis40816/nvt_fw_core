[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme (`Nvt.Core.Avalonia.Theme`)

Theme defines one neutral and semantic palette, radii, sizes, states and focus ring for NFC, NFH and NFU. Each tool keeps its accent. Merge the tokens into application resources and load the three style includes after Fluent. `ButtonStyles.axaml` is the only Core button style file. The eight legacy font values and resource resolver remain unchanged.

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml" />
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

Core defaults to NFC. An accent is one color expressed as seven `NfcAccent*` keys; a tool sets all seven below in application-scope Light/Dark resources. Adoption follows two steps (owner decision 2026-10-07):

1. Package PR: the tool pins the Core packages to the new version and changes no screen.
2. Look PR, approved by the owner: the tool loads `ThemeTokens`, `ButtonStyles` and `ScrollStyles`, sets only the seven `NfcAccent*` keys for its accent, moves its buttons to the Core roles, and deletes its local styles with the same purpose. Heights, corner radii and focus rings change in this same PR. The PR attaches before-and-after images in Light and Dark.

Overrides cannot restore a tool's old look. Some Core values are fixed, such as the 14 px scroll lane and the 14,0 button padding. Several states also share one key, such as the primary button background and border.

Tools do not override basic control styles locally. Core adds a role when a tool needs a variant. Focus colors are independent of accents.

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

Filled buttons use `Nvt.Button.PrimaryLabelBrush` from `ThemeTokens.axaml`, referenced through `DynamicResource`: `#FFFFFF` in Light and `#0B1220` in Dark. A tool may override it at application scope during the adoption step.

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

`actionIconButton` defaults to `actionNeutral` and composes with any of the four color roles, inheriting every state. `chipAction` follows neutral colors; `chipAction.active` and a ToggleButton's checked state follow the same selected rules. Selected hover uses the subtle accent surface; selected press uses the neutral pressed surface, retaining strong accent text and border. Ordinary Button has no checked state. Status warning/danger/success modifiers use the semantic surfaces, text and borders above. Statuses have no interactive, selected or focus state and retain informative colors and opacity 1 when disabled.

Disabled has highest priority: every interactive role uses `NfcSurfaceSubtleBrush`, `NfcBorderMutedBrush`, `NfcTextDisabledBrush` and opacity 1 over hover, press, checked and active. Disabled ghost buttons use the same subtle surface. No double dimming remains.

Background, border and foreground transitions last 120 ms. Pressing disables transitions; the primary pressed inner 2 px edge is an overlay that does not move content. Focus appears immediately. `.reducedMotion` on the control or any ancestor disables transitions.

### Focus ring

| Token | Light | Dark |
|---|---|---|
| `Nvt.Focus.RingBrush` | `#1F6FD1` | `#4DA3FF` |
| `Nvt.Focus.DangerRingBrush` | `#C62828` | `#FF6B6B` |

`Nvt.Focus.RingThickness` is Thickness 2; all three focus tokens live in `ThemeTokens.axaml`. Every interactive role defaults to `FocusAdorner=null`, disabling the default rectangle. Only `:focus-visible` shows the Border adorner: real Tab traversal shows it, pointer focus does not. `Margin=-4` gives a 2 px ring and 2 px exterior gap; `IsHitTestVisible=False` and `AdornerLayer.IsClipEnabled=False`. Corners follow live control radius changes: pills retain 999; each rectangular corner adds 4. `actionDanger` text buttons and `actionIconButton.actionDanger` use the red ring; chips, including `chipAction.actionDanger`, and all other roles use blue. The style resource `Nvt.Focus.RingRadiusConverter` computes the ring corners. Focus changes no background, border or foreground at rest, hover, checked or active.

## Breaking changes since core-v0.1.0

Core 0.2.0 introduces the shared palette and button roles. This section is its release note. The following classes, selectors and resources shipped in `core-v0.1.0` are removed or replaced; no compatibility aliases are provided.

| Removed or replaced item in 0.1.0 | Replacement in 0.2.0 |
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
| `Button.inlineEdit` | `actionIconButton` plus `actionGhost` |
| `Button.summaryChip` | `chipAction` |
| `Button.closeButton` | No Core replacement; define in the tool |
| `Button.fileRevealAction` | No Core replacement; define in the tool |
| `Button.railAction` (and `.reducedMotion`) | No Core rail role; define in the tool. `.reducedMotion` remains for every shared role |
| `Button.primaryRailAction` | No Core replacement; define in the tool |
| `railActionIcon`, `railActionLabel`, `railActionIconSlot` | No Core replacement; define in the tool |
| Resource `NfcSemanticButtonTheme` (ControlTheme) | No replacement resource; use a shared role class |
| Global `Button` rule (theme and null `FocusAdorner`) | Removed; add a role class. Without a role, a button falls back to Fluent, including its focus visual |
| Dialog class `confirmDialogActionButton` (`DialogsStyles.axaml`) | `actionNeutral` or `actionPrimary` |
| `Button.danger Path.confirmDialogDangerIcon` selector | `Button.actionDanger Path.confirmDialogDangerIcon` |
| Panel header's own template, `PART_ContentPresenter`, 12,10 padding, 10,10,0,0 radius and panel root clip | `actionGhost` role; load `ButtonStyles.axaml` before `PanelsStyles.axaml`. The shared template uses 14,0 padding and pill corners; the panel root no longer clips the exterior ring |

Product roles such as `railAction`, `primaryRailAction`, `closeButton` and `fileRevealAction`, including rail icon/label/slot layout, must be defined by each tool. The global `Button` rule is gone: a button without a role class falls back to Fluent, including Fluent's focus visual.

No existing `ThemeTokens.axaml` key or type is removed or renamed. Token and behavior changes are summarized below; the palette, accent and size tables above give the current values.

| Area | Change from 0.1.0 |
|---|---|
| Palette | Light/Dark neutral surfaces, state colors, text, borders, semantic colors and NFC's seven accent values now use the shared palette. Existing names are retained, including caution, critical and success-emphasis aliases |
| Sizes and spacing | Role buttons are 32 px high with 14,0 padding; icon buttons are 32 × 32 with zero padding. Dialog actions previously used 36/40 px. `NfcFieldSpacing` changes from 3 to 4; `NfcSpace2/4/8/12/16/24` values are retained |
| Radii | Token values remain `NfcCompactCornerRadius` = 6, `NfcSurfaceCornerRadius` = 8 and `NfcPillCornerRadius` = 999. Shared buttons and panel headers use pill corners; rectangular ring corners follow the control radius plus 4 |
| Focus | Shared roles replace the suppressed default adorner with a keyboard-only 2 px ring and 2 px gap. Blue is `#1F6FD1` / `#4DA3FF` and danger red is `#C62828` / `#FF6B6B` in Light/Dark; danger chips keep blue. Unclassified buttons keep Fluent's focus visual |
| New token keys | `NfcControlHeight` = 32; `Nvt.Focus.RingBrush`, `Nvt.Focus.DangerRingBrush`, `Nvt.Focus.RingThickness` = Thickness 2; `Nvt.Button.PrimaryLabelBrush` = `#FFFFFF` / `#0B1220` in Light/Dark |
| New style resource | `Nvt.Focus.RingRadiusConverter` follows each control corner; color, size and radius tokens are referenced through `DynamicResource` |

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

- `ButtonThemeTests`: ThemeTokens/ButtonStyles only; both themes and control types, every role/state, geometry, single-line/ellipsis/clipping, unclassed controls, real Tab/pointer focus, ring brushes/geometry/hit testing, no color change on focus in four states, disabled priority, reduced motion, application-scope adoption overrides and all-seven-key accent overrides.
- `PaletteContrastTests`: compiled tokens, all three accents in one data-driven test; body, placeholder, semantic and accent text and filled labels at 4.5:1; disabled text, input borders, rings and scroll thumbs at 3:1. All opaque neutral/semantic/accent-tint surfaces are checked, with no documented exceptions. Decorative boundaries and translucent scrims do not carry text.
- `ThemeContractTests`: every Theme static/dynamic resource resolves in both variants; button selectors require roles; all three XML baseline comparisons remain.
- `NfcLegacyFontTests` and resource resolver tests are unchanged; Dialogs/Panels tests now use shared roles and preserve the other behavior contracts.

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

The gallery uses Fluent, ThemeTokens, ButtonStyles and ScrollStyles to compare fixed states side by side in `artifacts/palette-gallery/roles-dark.png`, `roles-light.png`, `controls-dark.png` and `controls-light.png`. These images complement the real input tests.

### Adoption

Each tool follows two separate PRs (owner decision 2026-10-07, which replaces the 2026-10-06 zero-difference-then-color plan):

1. Package PR: pin the Core packages to the new version. The screens do not change.
2. Look PR, approved by the owner: adopt `ThemeTokens`, `ButtonStyles` and `ScrollStyles` in one change. Keep only the seven `NfcAccent*` keys for the tool's accent; set all seven. Move buttons to the Core roles and delete local styles with the same purpose. Do not add compatibility variants to Core to restore an old look. Before-and-after images cover every main screen in Light and Dark.

Images cover Light/Dark, English/Traditional Chinese, states, DPI and long labels. Product behavior and data colors stay in tools.

## Expander

The content divider sits 6 DIP from the header, leaving 2 DIP beyond the focus ring's 4 DIP outer extent; upward expansion mirrors this gap.


A 1 DIP muted border and a surface fill group the header and content. The container radius is 8 DIP for Pill and 6 DIP for Square.
Container padding 6 protects the exterior focus ring. Header padding is 12,0; content padding is 24,12,12,12.
The 1 DIP divider follows the header/content boundary when expanding up. Nested siblings should have an 8 DIP gap.
The header stays a capsule in Pill, including when expanded. No section top line crosses the focus ring.

Load `Theme/ExpanderStyles.axaml` after Fluent and merge `ThemeTokens.axaml` into application resources.
Plain `Expander` headers measure 32 DIP. Add `section` for a 44 DIP semibold header.
Headers have no border and use the shared Pill or Square corners.
The 12 × 6 chevron, 20-DIP chevron host, and 10-DIP spacing match `CollapsiblePanel`.
The styles support `ExpandDirection` Down and Up. Left and Right are not styled.
`CollapsiblePanel` retains its existing template and behavior.

Rest and expanded headers use `NfcSurfaceSubtleBrush`. Hover uses `NfcSelectionSurfaceBrush`; pressed uses `Nvt.Controls.ExpanderPressedBrush`.
Disabled headers use `Nvt.Controls.ExpanderDisabledForegroundBrush` and ignore pointer, pressed, and focus visuals.
Keyboard focus shows one two-DIP ring with a two-DIP outside gap. Pointer focus shows no ring.
Down and up expansion position the content below or above the header. The chevron rotates with the direction and expansion state.
Color and chevron transitions last 150 ms. Add `reducedMotion` to the control or an ancestor to disable these transitions.
Avalonia retains Space toggling, access keys, expansion events, and automation names.

| Token | Light | Dark |
| --- | --- | --- |
| `NfcControlHeight` | 32 | 32 |
| `Nvt.Expander.SectionHeaderHeight` | 44 | 44 |
| `NfcSelectionSurfaceBrush` | `#E8EEF5` | `#1E293B` |
| `Nvt.Controls.ExpanderPressedBrush` | `#CBD5E1` | `#29384D` |
| `NfcTextBrush` | `#1E293B` | `#E2E8F0` |
| `Nvt.Controls.ExpanderPressedForegroundBrush` | `#0B1220` | `#FFFFFF` |
| `Nvt.Controls.ExpanderDisabledForegroundBrush` | `#637085` | `#91A1B9` |
| `Nvt.Divider.TransparentBrush` | `#00FFFFFF` | Same |

Header corners use `Nvt.Shape.ControlCornerRadius`: Pill 999, Square 6 in both themes.
Focus corners use `Nvt.Shape.FocusCornerRadius`: Pill 999, Square 10 in both themes.
The divider and focus colors use the shared tokens documented below.
Remove local header templates, heights, hover borders, corner rules, and focus adorners when adopting this style.
Keep header content, commands, access keys, and bindings in the host.

```xml
<Expander Header="Details" />
<Expander Classes="section" Header="Advanced options" ExpandDirection="Up" />
```

## ProgressBar

Load `Theme/ProgressStyles.axaml` after Fluent. The style retains Fluent's range projection, percentage text, and indeterminate animations.
The default thickness is six DIP. Add `thin` for three DIP or `thick` for ten DIP.
Determinate and indeterminate tracks retain the same thickness. Vertical bars apply the thickness to their width.
`ShowProgressText` defaults to false. Callers still own ranges, values, text settings, visibility, and indeterminate policy.
Disabled bars retain their track and use the disabled indicator token.
Brush transitions last 150 ms.

`ProgressIndicator` keeps its public API and `ProgressBar` style identity.
Its existing `Progress` property still projects known fractions into `Value` and retains the last value for missing fractions.
It receives these tokens through its existing style key. No separate component template or selector is required.
Keep `Progress/ProgressStyles.axaml` when using `LoadingSurface`. That component stylesheet serves a separate purpose.

Add `reducedMotion` to a bar or an ancestor to replace indeterminate animation with a stationary middle third of the track.
Removing indeterminate mode restores the native determinate template. Reduced motion also disables brush transitions.
The static template preserves the native required indicator part and the progress automation peer.

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Progress.Height` | 6 | 6 |
| `Nvt.Progress.ThinHeight` | 3 | 3 |
| `Nvt.Progress.ThickHeight` | 10 | 10 |
| `Nvt.Progress.TrackBrush` | `#CBD5E1`, `NfcBorderMutedBrush` | `#334155`, `NfcBorderMutedBrush` |
| `Nvt.Progress.IndicatorBrush` | `#1557E9`, `NfcAccentBrush` | `#5FA5FA`, `NfcAccentBrush` |
| `Nvt.Progress.DisabledIndicatorBrush` | `#68778C`, `NfcTextDisabledBrush` | `#7B8CA5`, same alias |

The lighter track uses `NfcBorderMutedBrush`, and the indicator uses `NfcAccentBrush`.
The internal converter clamps `Nvt.Progress.CornerRadius` to half the shorter dimension.
Pill ends measure 1.5, 3, and 5 DIP. Square ends measure 1 DIP at every thickness.
Track, determinate, both animated indicators, and the reduced-motion indicator share this rule.
Replace the `Nvt.Progress.*` palette dictionary to update attached bars and indicators together.
Remove local track colors, indicator colors, thicknesses, corners, and competing templates when adopting this style.

```xml
<ProgressBar Value="42" />
<ProgressBar Classes="thin" IsIndeterminate="True" />
<ProgressBar Classes="thick reducedMotion" IsIndeterminate="True" />
```

## Separator

Separators are decorative and are not affected by shape. The default now uses the lighter `NfcDividerBrush`; strong remains unchanged.

Load `Theme/DividerStyles.axaml` after Fluent.
`Separator` and `Border.divider` render the same one-DIP line without a default margin.
The default line is horizontal. Add `vertical` for a vertical line and `strong` for the stronger border color.
Separators remain noninteractive and retain their native accessibility behavior.
Separators inside menus belong to the list and menu styles, which set their own margin and color.

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Divider.LineThickness` | 1 | 1 |
| `NfcDividerBrush` | `#E2E8F0` | `#273449` |
| `NfcBorderBrush` | `#718096` | `#708198` |

Remove local separator backgrounds, thicknesses, and default margin rules. Keep caller-owned layout spacing outside the control.
Use the same classes on native separators and plain divider borders.

```xml
<Separator />
<Separator Classes="vertical strong" />
<Border Classes="divider" />
<Border Classes="divider vertical strong" />
```

## GridSplitter

A centered 4 × 24 DIP grip identifies the vertical splitter; horizontal grips measure 24 × 4 DIP.
`Nvt.GridSplitter.GripCornerRadius` is 2 for Pill and 1 for Square. The resting grip uses `NfcBorderBrush`.
Hover uses `NfcAccentBrush`; pressed uses `NfcAccentStrongBrush`. The hit area remains 6 DIP.

Load `Theme/DividerStyles.axaml` after Fluent.
The default vertical divider has a six-DIP hit area and a centered one-DIP line.
Add `vertical` to resize columns or `horizontal` to resize rows. An explicit `ResizeDirection="Rows"` also selects the horizontal appearance.
Hover uses a two-DIP accent line. Dragging uses a three-DIP strong accent line, including outside the target.
Disabled splitters use the muted border color for both line and grip and suppress interaction visuals.
Keyboard focus shows one two-DIP ring with a two-DIP outside gap. Pointer focus shows no ring.
The cursor follows the resize direction. Avalonia retains drag handling, arrow resizing, and resize constraints.
With `ShowsPreview="True"`, a drag shows a 50% accent preview bar (`Nvt.GridSplitter.PreviewBrush`, `Nvt.GridSplitter.PreviewOpacity`). The neighbors resize when the pointer is released.
Color transitions last 150 ms. Add `reducedMotion` to the splitter or an ancestor to disable them.

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

Focus corners use `Nvt.GridSplitter.FocusCornerRadius`: Pill 999, Square 2, in both orientations.
Remove local splitter templates, widths, hover fills, cursors, and focus adorners when adopting this style.
Keep grid placement, resize behavior, drag increments, keyboard increments, and preview settings in the host.

```xml
<GridSplitter Classes="vertical" Grid.Column="1" ResizeBehavior="PreviousAndNext" />
<GridSplitter Classes="horizontal" Grid.Row="1" ResizeBehavior="PreviousAndNext" />
```

`DividerStylesRenderer` checks the layouts headlessly and writes images only when `NVT_DIVIDER_IMAGES_DIR` is set.
It exports `divider-light.png`, `divider-dark.png`, `divider-square-light.png`, and the side-by-side `divider-before-light.png`.
Every image is 1200 pixels wide at scale one and stays below one megabyte.
Divider tests pin geometry, all header states, contrast, runtime shapes, token replacement, motion policy, automation names, and native keyboard behavior.
Real pointer drags verify that both neighboring cells resize by the dragged distance.

The shipped styles meet these minimum contrast ratios in both shapes.
Indicator checks cover enabled progress bars. Focus checks cover the adjacent surface, application background, and selection surface.

| Contrast | Light | Dark |
| --- | --- | --- |
| Header text across enabled states | 12.525:1 | 11.866:1 |
| Disabled header text | 4.794:1 | 5.997:1 |
| Progress indicator against track | 3.960:1 | 4.067:1 |
| Focus ring against adjacent surfaces | 4.228:1 | 5.572:1 |

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

For adoption with the shared palette, each tool loads the scroll styles in its look PR:

- NFC replaces lines 117-185 with the include at the same position. Geometry stays the same; the shared palette changes the thumb colors. Run its UI smoke tests and attach before and after captures with the same OS, fonts, DPI and theme for owner review.
- NFH will look and behave differently. The owner approves the change in NFH's look PR, which attaches before and after screenshots. Its non-UI tests must keep the same list and outcomes.
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

## ListBox and dropdown items

`ListStyles.axaml` gives `ListBoxItem` and `ComboBoxItem` one shared look.
The ListBox host has no background, border, or padding. The app supplies its surrounding surface.
Rows have a 32 DIP minimum height and padding 10,5.
Add `compact` to a ListBox or individual item for a 24 DIP row with padding 10,0.
The ComboBox box keeps its existing theme and template.

### List states and tokens

Rest is transparent. Pointer over uses `NfcSelectionSurfaceBrush`, and pressed uses `NfcSecondaryActionPressedBrush`.
Selected rows use a soft accent surface, accent text, and a 2 × 12 DIP accent marker. The marker sits 4 DIP from the left edge and leaves a 4 DIP gap before the text.
Multi-selection uses the same appearance.
Disabled rows keep opacity 1 and use `NfcTextDisabledBrush`.
Disabled selected rows use `NfcSelectionSurfaceBrush`.
Keyboard focus shows one 2 DIP ring inset by 2 DIP, so scrolling does not clip it.
Pointer focus shows no ring. Focus does not recolor a list row.

List aliases share `Nvt.Controls.Selected*` resources with Choice rows, checked MenuItems, and `toggleSoft`.
Override the `Nvt.List.Selected*` resources together to replace the selected palette at runtime.

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.List.SelectedBrush` | `#EFF3FD`, `Nvt.Controls.SelectedBrush` | `#1A2940`, `Nvt.Controls.SelectedBrush` |
| `Nvt.List.SelectedPointerOverBrush` | `#E3ECFC`, `Nvt.Controls.SelectedPointerOverBrush` | `#20334F`, `Nvt.Controls.SelectedPointerOverBrush` |
| `Nvt.List.SelectedPressedBrush` | `#DCE7FA`, `Nvt.Controls.SelectedPressedBrush` | `#243C5B`, `Nvt.Controls.SelectedPressedBrush` |
| `Nvt.List.SelectedLabelBrush` | `#0E3C9E`, `Nvt.Controls.SelectedForegroundBrush` | `#BEDAFF`, `Nvt.Controls.SelectedForegroundBrush` |
| `Nvt.List.TransparentBrush` | `#00FFFFFF`, `Nvt.Toggle.TransparentBrush` | Same |
| `Nvt.List.CompactHeight` | 24 | 24 |

Items use `Nvt.Shape.ControlCornerRadius`: Pill 999 and Square 6.
The marker uses `Nvt.Shape.RoundCornerRadius`.
Focus uses `Nvt.Controls.FocusBrush` and `Nvt.Focus.RingThickness`.
`Nvt.List.FocusCornerRadius` is 999 for Pill and 4 for Square.
Color transitions last 150 ms. The existing `reducedMotion` class disables them on items or an ancestor.

## Menu, MenuItem, ContextMenu, and menu separators

`MenuStyles.axaml` supplies the menu bar, popup commands, context menus, and menu separators.
Menu items have a 32 DIP hit area and padding 10,0.
The colored body is the full 32 DIP high. Its 2 DIP focus ring is inset by 2 DIP, matching List rows.
The inset prevents scroll clipping. Square focus corners measure 4 DIP.

Menu surfaces use `NfcSurfaceBrush`, a 1 DIP `NfcBorderBrush` border, and padding 4.
Their corner follows the shared shape and caps at the existing surface corner token.
Pill surfaces use 8 DIP corners, and Square surfaces use 6 DIP corners.
Transparent shadow space surrounds the surface. Popup offsets compensate for that space.

### Menu states and tokens

Pointer over, native menu selection, and keyboard focus use `NfcSelectionSurfaceBrush`.
Pressed uses `NfcSecondaryActionPressedBrush`.
Disabled content uses `NfcTextDisabledBrush` with opacity 1.
Checked items use the shared selected fill, foreground, hover, and pressed tokens.
The 12 DIP check sits in a fixed 20 DIP slot with an 8 DIP label gap. Icons retain their own 20 DIP slot.
Ordinary menu-bar commands omit the check slot. Native navigation selection remains independent of checked state.
Input gestures use `NfcTextMutedBrush`; checked items inherit the selected foreground. Submenus retain their chevron.
Menu bars use the same item states and height.

Focus rings follow Avalonia's `:focus-visible` state.
Native arrow navigation retains Avalonia's menu selection behavior.
The styles preserve arrows, Enter, Escape, access keys, commands, and automation.
Menu separators are 1 DIP high, use `NfcDividerBrush`, and keep margin 10,4.
The legacy `MenuItem Header="-"` separator receives the same appearance.
Color transitions last 150 ms. `reducedMotion` also disables menu transitions.

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.Menu.PopupShadow` | `0 4 12 0 #26000000` | `0 4 12 0 #66000000` |
| `Nvt.Menu.CheckSize` | 12 | 12 |
| `Nvt.Menu.IconSlotSize` | 20 | 20 |
| `Nvt.Menu.LabelGap` | `0,0,8,0` | `0,0,8,0` |
| `Nvt.Menu.PopupShadowMargin` | 16 | 16 |
| `Nvt.Menu.PopupMaximumCornerRadius` | 8, `NfcSurfaceCornerRadius` | 8, `NfcSurfaceCornerRadius` |
| `Nvt.Menu.ChevronGeometry` | `M1 1 L5 5 L1 9` | Same |

Shadow alpha colors live in `ListTokens.axaml`. Shared tonal colors live in `ControlTokens.axaml`.
Every style color and corner resolves through tokens or the owning control.
The popup corner converter is internal. This family adds no public C# API.

### List and menu adoption

1. Merge `ThemeTokens.axaml` into application resources.
2. Load `ListStyles.axaml` and `MenuStyles.axaml` after Fluent and before creating controls.
3. Remove competing local item themes, colors, padding, corners, and focus adorners.
4. Keep host surfaces, item templates, selection bindings, commands, icons, and accessible names.
5. Check secondary text inside items. The item styles bind every descendant `TextBlock` foreground to the item foreground, so a style class that sets a muted color loses. Set muted text with a local `Foreground` value.
6. Choose the shared shape at the resource root and verify both themes.
7. Open a context menu and a two-level submenu on a real desktop window once. The popup shadow needs a window with per-pixel transparency, which headless tests cannot show.

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ListStyles.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/MenuStyles.axaml" />
```

| Adopter | Difference and required removal |
| --- | --- |
| NFC | Replaces Fluent list selection and accent-text dropdown overrides. Remove local ListBox and ComboBoxItem appearance rules. |
| NFH | Restores visible selection where workspace rows suppressed it. Remove those row rules, dropdown item overrides, and the menu font override. |
| NFU | Replaces accent-filled selection and 34 DIP dropdown rows with soft 32 DIP rows. Remove shared, inspector, and dropdown item appearance rules. |
| All three | Replaces Fluent popup surfaces and menu states. Keep item generation, navigation, commands, and host-owned surfaces. |

### List and menu verification

Headless tests cover both themes and shapes, state precedence, exact geometry, resource replacement, contrast, and native keyboard input.
They also check inset focus after scrolling, resizing, and changing render scale.
The minimum contrast values are identical across shapes.

| Contrast | Light | Dark |
| --- | --- | --- |
| Selected list text and marker | 7.807:1 | 7.830:1 |
| Disabled text | 3.903:1 | 4.275:1 |
| Focus ring against item fills | 5.340:1 | 6.194:1 |

`ListMenuStylesRenderer` exports only when `NVT_LIST_IMAGES_DIR` is set.
It writes `list-light.png`, `list-dark.png`, `list-square-light.png`, and `list-before-light.png`.
All images are 1200 pixels wide at scale 1. Each remains below 1 MB.
The comparison uses the real Fluent templates beside the Core templates.

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

## ToggleButton

FluentPill supplies the shared toggle look in `ToggleStyles.axaml`.
`ThemeTokens.axaml` includes its palette and the default Pill shape.
Load the style after Fluent alongside the other Core theme styles.

| Role | Control and use | Geometry |
| --- | --- | --- |
| `toggleSegment` | `ToggleButton` inside `Border.toggleSegmentGroup > StackPanel` for grouped choices. | Height 40, horizontal padding 20. |
| `toggleSegmentGroup` | Neutral track around segment choices. | Padding 4, spacing 2, height 48. |
| `toggleTab` | `ToggleButton` for navigation. The host supplies the navigation baseline. | Height 40, horizontal padding 20. |
| `toggleIcon` | `ToggleButton` with a host-supplied glyph and accessible name. | 40 × 40. |
| `toggleSwitch` | `ToggleButton` with a switch template. | 58 × 40 click area. |
| `ToggleSwitch` | The native control receives the same switch look without a class. | Track 52 × 28, knob 22 × 22, travel 24. |

All measurements use device-independent pixels.
Switch knobs stay round and white in both themes and shapes.
The switch template shows the track only. Place any visible label beside it and provide an accessible name.
The host owns selection rules, commands, and content.
Use one toggle role per control.

### Toggle states and danger

These nine states apply to the solid toggle roles.
Pointer focus shows no ring. Tab focus shows one 2 px ring with a 2 px exterior gap.
Space toggles the checked state.
Brush and knob transitions last 150 ms.
Pressed solid ToggleButton roles use a 0.98 render scale. The native ToggleSwitch retains its full scale.
Layout dimensions and hit targets stay unchanged.

| State | Segment, tab, and icon fill | Content | Switch track |
| --- | --- | --- | --- |
| Rest | Segment and tab transparent; icon `NfcSurfaceSubtleBrush`. | `NfcTextSecondaryBrush` | `NfcBorderBrush` |
| Pointer over | Segment `NfcSurfaceSubtleBrush`; tab and icon `NfcSelectionSurfaceBrush`. | `NfcTextBrush` | `NfcTextDisabledBrush` |
| Pressed | `NfcSecondaryActionPressedBrush` | `NfcTextStrongBrush` | `NfcBorderBrush` |
| Checked | `Nvt.Toggle.SelectedBrush` | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SwitchOnBrush` |
| Checked pointer over | `Nvt.Toggle.SelectedPointerOverBrush` | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SwitchPointerOverBrush` |
| Checked pressed | `Nvt.Toggle.SelectedPressedBrush` | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SwitchPressedBrush` |
| Disabled | `NfcSurfaceSubtleBrush` | `NfcTextDisabledBrush` | `NfcBorderBrush` |
| Disabled checked | `NfcSelectionSurfaceBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| Keyboard focus | Retains the current fill. | Retains the current content color. | Retains the current track color. |

Add `danger` to a solid toggle role or native `ToggleSwitch` for a red checked fill and white content.
Checked pointer over and pressed share the same darker red.
Danger toggles use `Nvt.Focus.RingBrush`, including checked keyboard focus.
Disabled checked controls retain the approved neutral appearance, including danger controls.
Disabled controls ignore hover, pressed, and focus overrides and keep opacity 1.

### Toggle tokens

Styles resolve every color and corner through resources.
The following palette applies to both shapes.
Light aliases reuse existing Core brushes. Dark fills use deeper colors to preserve white content contrast.
Override the `Nvt.Toggle.*` keys together in one resource dictionary to replace the toggle palette.
Replace that dictionary in the application or window resources to update attached controls.

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

Neutral states reuse the shared palette listed above.
Focus reuses `Nvt.Focus.RingBrush`: Light `#1F6FD1`, Dark `#4DA3FF`.
`Nvt.Focus.RingThickness` is 2 in both themes.

The tests measure these minimum contrast ratios in both shapes.
Checked fill checks cover enabled states. Disabled checked states use the neutral colors listed above.

| Contrast | Light | Dark |
| --- | --- | --- |
| White on checked segment, tab, and icon | 5.879:1 | 7.794:1 |
| White on checked switch | 5.169:1 | 5.169:1 |
| White on checked danger | 7.184:1 | 7.184:1 |
| Standard focus ring against adjacent surfaces | 4.228:1 | 5.572:1 |
| Disabled text | 3.903:1 | 4.275:1 |
| Knob against track across all states | 4.015:1 | 3.422:1 |

### Shared shape setting

`ThemeShape` defines `Pill` and `Square`.
Call `ThemeShapes.SetShape(IResourceDictionary resources, ThemeShape shape)` on the UI thread.
Pass application, window, or subtree resources, just as the requested theme variant uses a theme root.
The method replaces one shape dictionary and preserves unrelated resources.
Changing light or dark through `RequestedThemeVariant` leaves the shape unchanged.
Controls keep their templates and receive no local corner settings.

```csharp
using Avalonia;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Theme;

ThemeShapes.SetShape(Application.Current!.Resources, ThemeShape.Square);
Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
ThemeShapes.SetShape(Application.Current.Resources, ThemeShape.Pill);
```

`ShapePill.axaml` and `ShapeSquare.axaml` define reusable resources for future toolbar buttons and filter pills.
The values stay the same in Light and Dark.

| Token | Pill | Square |
| --- | --- | --- |
| `Nvt.Shape.ControlCornerRadius` | 999 | 6 |
| `Nvt.Shape.GroupCornerRadius` | 999 | 6 |
| `Nvt.Shape.FocusCornerRadius` | 999 | 10 |
| `Nvt.Shape.RoundCornerRadius` | 999 | 999 |

Switch tracks use `Nvt.Shape.ControlCornerRadius`; their focus rings use `Nvt.Shape.FocusCornerRadius`.
Knobs remain round. Track, knob, and travel dimensions remain 52 × 28, 22 × 22, and 24 DIP.
The existing Button roles continue using their current radius resources.

### Toggle adoption

1. Merge `ThemeTokens.axaml` into application resources and load `ToggleStyles.axaml` after Fluent.
2. Assign a toggle role and bind `IsChecked`. Put segments inside the group structure shown below.
3. Supply icon content, accessible names, and selection rules in the host.
4. Add `danger` where required and select the shared shape at the resource root.
5. Remove competing local colors and corners. Verify both themes, both shapes, keyboard input, and disabled states.

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

`ToggleStylesRenderer` checks the layout headlessly and writes images only when `NVT_TOGGLE_IMAGES_DIR` is set.
It exports `toggle-src-light.png` and `toggle-src-dark.png` at 1320 × 2920 with both shapes and all states.
Tests cover contrast, runtime resource replacement, exact switch geometry, keyboard input, and literal-free style rules.

### toggleSoft

Use `toggleSoft` for compact filters and optional toolbar choices with a light accent tint when checked.
It supports text, icons with text, and plain sibling rows without a group border.
The role uses `NfcControlHeight` at 32 DIP and horizontal padding of 12 DIP.

| State | Fill | Text and icon |
| --- | --- | --- |
| Off | `Nvt.Toggle.TransparentBrush` | `Nvt.Toggle.SoftForegroundBrush` |
| Off pointer over | `Nvt.Toggle.SoftPointerOverBrush` | `Nvt.Toggle.SoftPointerOverForegroundBrush` |
| Off pressed | `Nvt.Toggle.SoftPressedBrush` | `Nvt.Toggle.SoftPressedForegroundBrush` |
| On | `Nvt.Toggle.SoftCheckedBrush` | `Nvt.Toggle.SoftCheckedForegroundBrush` |
| On pointer over | `Nvt.Toggle.SoftCheckedPointerOverBrush` | `Nvt.Toggle.SoftCheckedForegroundBrush` |
| On pressed | `Nvt.Toggle.SoftCheckedPressedBrush` | `Nvt.Toggle.SoftCheckedForegroundBrush` |
| Disabled off | `Nvt.Toggle.TransparentBrush` | `Nvt.Toggle.SoftDisabledForegroundBrush` |
| Disabled on | `Nvt.Toggle.SoftDisabledCheckedBrush` | `Nvt.Toggle.SoftDisabledForegroundBrush` |
| Keyboard focus | Retains the current fill. | Retains the current foreground. |

The following aliases share Core resources in both themes.
Selected states use the tonal palette documented under Control redesign verification.
Replace one dictionary containing these `Nvt.Toggle.Soft*` keys to update attached controls together.

| Token | Core resource | Light | Dark |
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

`Nvt.Shape.ControlCornerRadius` supplies the body corners: Pill 999 and Square 6.
`Nvt.Shape.FocusCornerRadius` supplies the ring corners: Pill 999 and Square 10.
The existing `ThemeShapes.SetShape` method changes both at runtime.

Keyboard focus uses `focus-visible`, `Nvt.Controls.FocusBrush`, and `Nvt.Focus.RingThickness` with a 2 px exterior gap.
Pointer focus shows no ring.
Background and foreground transitions last 150 ms.
Pressed controls use a 0.98 render scale without changing layout or hit targets.
Space toggles the value, Tab focuses the control, and disabled controls ignore input.

Text and icons share the inherited foreground and meet 4.5:1 contrast in both themes.
Selected rest, hover, and pressed text measure 8.759, 8.185, and 7.807:1 in Light.
Dark values are 10.216, 8.898, and 7.830:1.
The role uses `Nvt.Controls.FocusBrush`. Against all three selected fills, its minimum is 5.340:1 in Light and 6.194:1 in Dark.
Disabled on text measures 3.903:1 in light and 4.275:1 in dark against its selection fill.

Adopt the role in three steps:

1. Merge `ThemeTokens.axaml` and load `ToggleStyles.axaml` after Fluent.
2. Add `Classes="toggleSoft"`, bind `IsChecked`, and provide content with an inherited foreground.
3. Remove competing local colors and corners, then verify both themes, both shapes, and keyboard input.

```xml
<StackPanel Orientation="Horizontal" Spacing="8">
  <ToggleButton Classes="toggleSoft" Content="Matches only" IsChecked="{Binding MatchesOnly}" />
  <ToggleButton Classes="toggleSoft" Content="Dedupe" IsChecked="{Binding Dedupe}" />
</StackPanel>
```

Load the existing font and icon resources when supplying icon content.
Bind a vector icon's fill or stroke to the toggle foreground.
`toggleSoft` uses its tonal palette and has no `danger` variant.

`ToggleSoftRenderer` checks both shapes, text, icons, and every state through headless rendering.
It writes `toggle-soft-light.png` and `toggle-soft-dark.png` only when `NVT_TOGGLE_IMAGES_DIR` is set.
Tests disable transitions locally for stable snapshots, as they do for the existing roles.

## CheckBox

The visible row follows `Nvt.Shape.ControlCornerRadius`, while the checkbox indicator remains square with radius 6.
Rest uses the subtle surface; hover and pressed tint the whole row. Checked and mixed rows share the selected tonal states.
Row padding is 10,6; compact padding is 10,2. Enabled labels use `Nvt.Controls.ChoiceForegroundBrush`.

`ChoiceStyles.axaml` gives native checkboxes a shared Core appearance without an appearance class.
Merge `ThemeTokens.axaml` into application resources and load `ChoiceStyles.axaml` after Fluent.

The indicator measures 20 × 20 DIP. The label uses `Nvt.Controls.ChoiceForegroundBrush`, `NfcUiFontFamily`, and the 13 DIP `NfcFontSize13` body size.
An 8 DIP gap separates the indicator and label. The whole row accepts input and measures at least 32 DIP high.
Long string labels wrap and grow the row. The indicator stays aligned with the first line.
Custom content retains its content template and controls its own text wrapping.

Add `compact` for dense filter lists. It fixes the row height at 24 DIP and retains the 20 DIP indicator.
Use `compact` only for single-line labels. The row height is fixed, so a wrapped label is clipped.
The current inventory requires this variant for filter checkboxes.

Space preserves Avalonia's normal two-state cycle. `IsThreeState="True"` cycles unchecked, checked, indeterminate, then unchecked.
An indeterminate value shows a white dash. A checked value shows a white check mark.
Disabled choices retain their selection glyph and opacity 1.

### Choice states

These states apply to unchecked, checked, and indeterminate checkboxes and to unchecked and checked radio buttons.
Focus changes only the exterior ring. Pointer focus shows no ring.
Keyboard focus shows one 2 DIP `Nvt.Controls.FocusBrush` ring with a 2 DIP exterior gap around the row.
The default focus adorner is disabled.
Brush transitions last 150 ms. Add `reducedMotion` to the control or an ancestor to disable them.

| State | Indicator fill | Indicator outline | Label |
| --- | --- | --- | --- |
| Unchecked rest | `NfcSurfaceBrush` | `NfcBorderBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| Unchecked pointer over | `NfcSurfaceSubtleBrush` | `NfcTextSecondaryBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| Unchecked pressed | `NfcSelectionSurfaceBrush` | `NfcTextStrongBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| Checked or indeterminate | `Nvt.Toggle.SelectedBrush` | `NfcAccentBorderBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| Selected pointer over | `Nvt.Toggle.SelectedPointerOverBrush` | `NfcAccentBorderStrongBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| Selected pressed | `Nvt.Toggle.SelectedPressedBrush` | `NfcAccentBorderStrongBrush` | `Nvt.Controls.ChoiceForegroundBrush` |
| Disabled unchecked | `NfcSurfaceSubtleBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| Disabled selected | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` | `NfcTextDisabledBrush` |
| Keyboard focus | Retains current fill | Retains current outline | Retains current label |

Disabled controls ignore pointer, pressed, and keyboard focus styles.
White selection content uses `Nvt.Toggle.SelectedLabelBrush` in both themes.
Dark selected outlines use the brighter Core accent to separate the indicator from adjacent surfaces.

### Choice tokens

`ThemeTokens.axaml` includes `ChoiceTokens.axaml`. Its six geometry tokens have identical Light and Dark values.
Geometry comes from Choice and Shape tokens. Row colors come from the shared palette and `ControlTokens.axaml`.

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

The check mark reuses `NfcDoneIconGeometry`.
Checkbox corners use `Nvt.Choice.CheckBoxCornerRadius` (6) in both shapes, so a checkbox never looks like a radio ring. Radio indicators stay circular.
The row focus ring uses `Nvt.Shape.FocusCornerRadius`: Pill 999, Square 10.
Call `ThemeShapes.SetShape` at the resource root to update attached choices without replacing their templates.
Override existing brush resources together in one palette dictionary to update colors at runtime.

The following minimum ratios cover both shapes and five adjacent surfaces, including the pressed surface.

| Contrast | Light | Dark |
| --- | --- | --- |
| Enabled label | 14.328:1 | 11.215:1 |
| Disabled label | 3.698:1 | 3.783:1 |
| Indicator outline across all states | 3.257:1 | 3.256:1 |
| White glyph across all selected states, including disabled | 4.559:1 | 3.422:1 |
| White glyph on enabled checked fill | 5.879:1 | 7.794:1 |
| Keyboard focus ring | 5.340:1 | 6.194:1 |

### CheckBox adoption

1. Merge the theme tokens and include the choice styles after Fluent.
2. Bind `IsChecked` and set `IsThreeState` where an indeterminate value is meaningful.
3. Apply `compact` to dense filter rows and supply meaningful labels or automation names.
4. Remove local checkbox templates, foreground overrides, fixed heights, padding overrides, and opacity changes.
5. Verify both themes, both shapes, pointer input, keyboard input, and disabled selections.

Use `compact` to replace existing 24 DIP filter heights.
Use the shared body text instead of local 14 DIP medium text.

```xml
<CheckBox Content="Include archived items" IsChecked="True" />
<CheckBox Content="Include annotations" IsThreeState="True" IsChecked="{x:Null}" />
<CheckBox Classes="compact" Content="Include archive" />
```

## RadioButton

The row shares CheckBox geometry, colors, contrast, and runtime shape changes. The 20 DIP ring and 10 DIP dot remain round.

`ChoiceStyles.axaml` also styles native radio buttons without an appearance class.
Load the same theme tokens and styles described under CheckBox.
The radio ring measures 20 × 20 DIP and contains a 10 DIP white dot when checked.
`Nvt.Shape.RoundCornerRadius` keeps the ring round in Pill and Square.
The label gap, row height, wrapping, focus ring, motion, disabled selection, and `compact` class follow the checkbox rules.
Radio buttons use the checked and unchecked states in the choice table.

Avalonia retains ownership of selection. No custom control, behavior, or keyboard handler is added.
Unnamed groups stay scoped to their parent. A shared `GroupName` groups choices across panels within the same root.
Avalonia 12.1.1 requires host `XYFocus.NavigationModes="Keyboard"` to move focus with arrow keys in a plain `StackPanel`.
Arrows move focus without changing selection. Space selects the focused radio without clearing it.
This follows [Avalonia directional focus](https://docs.avaloniaui.net/docs/input-interaction/focus) and the native behavior characterized by the choice tests.
Automation names and native automation control types remain intact.

### RadioButton adoption

1. Include `ChoiceStyles.axaml` after Fluent and merge the theme tokens.
2. Keep related radios in a `StackPanel`, or retain existing `GroupName` values for named groups.
3. Bind `IsChecked` and retain existing labels, commands, and automation names.
4. Remove local choice templates, ring sizes, dot sizes, colors, corners, and fixed card heights.
5. Verify selection boundaries and arrow navigation after adoption.

Adopting this family replaces button-like radio navigation, filter templates, and tall choice cards with the shared choice row.
The common 20/10 DIP indicators replace local 18/8 DIP ring and dot sizes.

```xml
<StackPanel>
  <RadioButton Content="Standard review" GroupName="Review" IsChecked="True" />
  <RadioButton Content="Extended review" GroupName="Review" />
</StackPanel>
```

`ChoiceStylesRenderer` checks layout headlessly and writes images only when `NVT_CHOICE_IMAGES_DIR` is set.
It exports `choice-light.png`, `choice-dark.png`, `choice-square-light.png`, and `choice-before-light.png`, each 1200 pixels wide at 100% scale.
The comparison shows Fluent and Core controls beside each other.
Tests cover every state, contrast, row geometry, wrapping, native input, runtime dictionaries, runtime shapes, accessibility, and reduced motion.

## Control redesign verification

The redesign changes styles and resources only. No public C# API changes.
Load the existing family styles after Fluent; `ThemeTokens.axaml` includes `ControlTokens.axaml` automatically.
Remove competing local geometry, palette, row-fill, grip, and focus styles. Keep application logic and native keyboard behavior.

Shared selected fills apply to ListBoxItem, ComboBoxItem, checked MenuItem, Choice rows, and `toggleSoft`.
List aliases and Toggle aliases remain available for dictionary replacement. Replace each family palette together.

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

All new pairs below use WCAG relative luminance, opaque sRGB colors, and both shapes. Ratios are rounded to three decimals.
Text, icons, active outlines, and focus preserve the previously documented minima.
Decorative separators and inactive splitter hints deliberately become lighter; they carry no text or active-control identity.

| Contrast | Light | Dark |
| --- | --- | --- |
| Tonal text, icons and markers / Selected | 8.759:1 | 10.216:1 |
| Choice label / Selected | 16.075:1 | 14.633:1 |
| Choice outline / Selected | 5.294:1 | 5.748:1 |
| Tonal focus / Selected | 5.991:1 | 8.082:1 |
| Tonal text, icons and markers / SelectedPointerOver | 8.185:1 | 8.898:1 |
| Choice label / SelectedPointerOver | 15.022:1 | 12.744:1 |
| Choice outline / SelectedPointerOver | 6.558:1 | 6.683:1 |
| Tonal focus / SelectedPointerOver | 5.598:1 | 7.039:1 |
| Tonal text, icons and markers / SelectedPressed | 7.807:1 | 7.830:1 |
| Choice label / SelectedPressed | 14.328:1 | 11.215:1 |
| Choice outline / SelectedPressed | 6.255:1 | 5.882:1 |
| Tonal focus / SelectedPressed | 5.340:1 | 6.194:1 |
| Choice label / rest | 17.063:1 | 15.737:1 |
| Choice outline / rest | 3.838:1 | 3.959:1 |
| Choice focus / rest | 6.359:1 | 8.692:1 |
| Choice label / hover | 15.285:1 | 14.629:1 |
| Choice outline / hover | 6.488:1 | 9.853:1 |
| Choice focus / hover | 5.696:1 | 8.080:1 |
| Choice label / pressed | 14.482:1 | 12.945:1 |
| Choice outline / pressed | 14.482:1 | 12.373:1 |
| Choice focus / pressed | 5.397:1 | 7.150:1 |
| Choice disabled label and outline / row | 4.358:1 | 4.599:1 |
| Expander rest text and chevron | 13.982:1 | 12.766:1 |
| Expander hover text and chevron | 12.525:1 | 11.866:1 |
| Expander pressed text and chevron | 12.611:1 | 11.884:1 |
| Expander disabled text and chevron | 4.794:1 | 5.997:1 |
| Expander container / surface (decorative) | 1.485:1 | 1.713:1 |
| Separator and content divider / surface (decorative) | 1.233:1 | 1.414:1 |
| Progress indicator / track | 3.960:1 | 4.067:1 |
| Progress indicator / surface | 5.879:1 | 6.968:1 |
| Progress disabled indicator / track | 3.071:1 | 3.026:1 |
| Splitter rest grip / surface | 4.015:1 | 4.462:1 |
| Splitter hover grip / surface | 5.879:1 | 6.968:1 |
| Splitter pressed line and grip / surface | 7.794:1 | 9.303:1 |
| Splitter disabled line and grip / surface (inactive) | 1.485:1 | 1.713:1 |
| Solid selected pressed white glyph | 9.728:1 | 12.081:1 |
| Switch pressed white knob | 8.722:1 | 8.722:1 |

The review proposed dark pressed Expander fill `#334155`. Even white text yields only 10.355:1, below the previous 11.866:1.
The replacement `#29384D` yields 11.884:1 with white. Light pressed text changes to `#0B1220` to retain 12.525:1.
Selected text changes to `#0E3C9E` / `#BEDAFF`, and tonal focus changes to `#1557C0` / `#8FC5FF`.
These compensate for the stronger selected fills without changing global `Nfc*` or `Nvt.Focus.*` colors.
Section headers remain 44 DIP, as required by this revision, rather than the review's proposed 40 DIP.
No flat icon role is introduced. Existing Toggle roles retain their geometry and receive distinct selected pressed feedback.

The additive `RenderRedesign*` tests export `<control>-<pill|square>-<light|dark>.png` using the existing family environment variables.
`separator.png` contains both themes and labels the control "Not affected by shape".
Each sheet is 1200 pixels wide, uses English labels and scale one, and stays below one megabyte.
Progress sheets show the static indeterminate placeholder. Interactive states do not apply to ProgressBar or Separator.
Dedicated runtime tests cover each requested control, all Toggle roles, and ComboBoxItem without replacing templates.
Owner review still needs real popup placement, 150 ms motion, long Traditional Chinese labels, and application compositions.
