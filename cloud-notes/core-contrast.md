# Nvt.Core.Avalonia contrast audit (Light and Dark)

Analysis only. No source, token or test file was changed. Branch `cloud/core-contrast`, one file.

| | |
|---|---|
| Base | `origin/main` at `53954f8` (merge of PR #269, 2026-11-05 15:37 +0800) |
| Scope | `src/Nvt.Core.Avalonia` styles and tokens, Core default palette (the NFC accent) |
| Floors | text 4.5:1 (WCAG 1.4.3), non-text 3:1 (WCAG 1.4.11) |
| Formula | sRGB relative luminance, `(L1+0.05)/(L2+0.05)`, same as `PaletteContrastTests` |
| Translucent fills | composited over the real parent first, then measured (see "Method") |

## 摘要 (zh-TW)

- Core 自己的樣式裡,**沒有任何**未停用的文字配對低於 4.5:1(218 組配對,最低 4.588:Light 表單 placeholder 在 pressed 填色上)。NFH、NFU 強調色覆寫後也沒有新增失敗。
- 非文字配對有 **3 項建議必改**(都落在 WCAG 1.4.11 的元件邊界或狀態指示):`NumberScrubber` 邊框(Light 與 Dark)、Dark 的 switch 開啟 hover/pressed 軌道、Dark 的 danger switch 軌道。
- 另有 **9 項屬建議等級**(選取反白在 hover 欄位上幾乎不可見、Dark 的 checked segment/tab、Light 的 pressed 焦點環 2.82–2.94、裝飾性邊框等)。
- 停用狀態的文字(最低 3.57:1)與純裝飾分隔線依 WCAG 可豁免,見第 5 節。
- 本機沙箱的 `origin/main` 起初停在 #268(`0286a27`),重新 fetch 後才包含 #269。以下數字全部以 `53954f8` 為準。

## 1. Result

| Class | Count | Meaning |
|---|---|---|
| Text pairs below 4.5:1 in Core styles, enabled | **0** | min 4.588 (Section 4 lists everything near 5.0) |
| Text pairs below 4.5:1, disabled | 17 | exempt, all at or above 3.57:1 (Section 5) |
| Non-text, **Required** (R1 to R3) | **3** | component boundary or state indicator, no second cue |
| Non-text, **Advisory** (A1 to A9) | 9 | below 3:1, but a second cue exists, the element is decorative, or the pair is not a WCAG requirement |
| Latent text pairs | 9 tokens | muted, accent and semantic text that fall under 4.5:1 only when a consumer places them on a hover or pressed layer (Section 6) |

Coverage: 447 distinct pairs (218 text, 229 non-text), 900 measured ratios (Light and Dark). The full list is in Appendix A. A matrix of every text token against every surface and state fill (1,092 combinations) was run as a cross-check; its failures are the latent pairs in Section 6.

### How the pairs were chosen

- A pair is a foreground and the background it is actually painted on in a given state. The background is a layered composite where the style uses a translucent token.
- Non-text borders and fills are measured against the **page** (`NfcAppBackgroundBrush`, `NfcSurfaceBrush`, `NfcSurfaceSubtleBrush`), because that is what a user sees next to the component. Inner pairs (border against the control's own fill) are marked as such.
- "Required" means WCAG 1.4.11 applies and no second cue carries the same information. "Advisory" means it is below 3:1 but a label, outline, glyph or text carries the state, or the element is decoration. These are judgement calls; the numbers are in the tables so the call can be revisited.

## 2. Required (non-text below 3:1)

### R1. `NumberScrubber` border (Light and Dark)

- `src/Nvt.Core.Avalonia/Inputs/InputsStyles.axaml:10` sets `BorderBrush` to `NfcBorderMutedBrush`. The fill (`:9`) is `NfcSurfaceBrush`, the same colour as most hosts, so the 1 px border is the only boundary of the field.
- Ratio against the page: Light 1.36 / 1.48 / 1.42 (AppBackground / Surface / SurfaceSubtle); Dark 1.81 / 1.71 / 1.52.
- Reached in: rest (and every state other than `.focused`, which uses `NfcAccentBrush` and passes).
- Suggested value: use the token the other fields use, `NfcBorderBrush` (Light `#718096`, Dark `#708198`). Resulting ratio: Light 3.67 / 4.02 / 3.84; Dark 4.71 / 4.46 / 3.96. No new colour is needed. Form fields already use it (`Nvt.Form.BorderBrush`).

### R2. Dark switch, ON, hover and pressed track

- `Theme/ToggleStyles.axaml:162` (`:checked:pointerover`) and `:165` (`:checked:pressed`) use `Nvt.Toggle.SwitchPointerOverBrush` `#1D4ED8` and `Nvt.Toggle.SwitchPressedBrush` `#1E40AF`, defined once for both themes at `Theme/ToggleTokens.axaml:52-53`.
- Track against the page, Dark: hover 2.79 / 2.65 / 2.35, pressed 2.15 / 2.03 / 1.80. Light passes (the same colours are dark blues on white). The Dark rest value `#2563EB` (`:51`) passes with a thin margin: 3.62 / 3.43 / **3.04**.
- The knob (white) stays visible, but the track edge is the component boundary and it darkens toward the page while the pointer is on it.
- Suggested values: move the three switch brushes into the Light and Dark dictionaries, keep the Light values, and use in Dark:

| Token (Dark) | Value | vs AppBackground / Surface / SurfaceSubtle | white knob |
|---|---|---|---|
| `Nvt.Toggle.SwitchOnBrush` | `#2F6BEE` | 3.98 / 3.77 / 3.34 | 4.71 |
| `Nvt.Toggle.SwitchPointerOverBrush` | `#3B82F6` | 5.09 / 4.82 / 4.28 | 3.68 |
| `Nvt.Toggle.SwitchPressedBrush` | `#2C6AEB` | 3.89 / 3.69 / 3.27 | 4.81 |

  If the owner wants hover to keep darkening as in Light, the usable window in Dark is narrow: the track must stay at or above `#2A66E4` (3.09 on SurfaceSubtle) to keep 3:1.

### R3. Dark danger switch, ON track

- `Theme/ToggleStyles.axaml:180` and `:184` (`.danger:checked`, `:pointerover`, `:pressed`) use `Nvt.Toggle.DangerFillBrush` `#A82035`, `DangerFillPointerOverBrush` and `DangerFillPressedBrush` `#861B2C` (`Theme/ToggleTokens.axaml:44-46`, Dark).
- Track against the page, Dark: rest 2.61 / 2.47 / 2.19; hover and pressed 1.97 / 1.86 / 1.65. Light passes (`NfcDangerTextBrush`, `NfcDangerTextStrongBrush`).
- The same tokens colour `toggleSegment`, `toggleTab` and `toggleIcon` `.danger` with a white label that must keep 4.5:1, so the Dark value has a narrow window (relative luminance about 0.15 to 0.18 against SurfaceSubtle).
- Suggested values (Dark): rest `#CC3F54` (3.93 / 3.72 / 3.30 against the page, white 4.77); hover and pressed `#C8384D` (3.68 / 3.49 / 3.09, white 5.09).

## 3. Advisory (below the floor, second cue or decoration)

| ID | Pair | Light / Dark | Source | Why advisory | Suggestion |
|---|---|---|---|---|---|
| A1 | Text selection highlight against the field fill while hovered or pressed | hover 1.04 / 1.01, pressed 1.04 / 1.08, rest 1.26 / 1.28 | `Theme/FormStyles.axaml:78` via `Nvt.Form.SelectionBrush` = `Nvt.Controls.SelectedBrush` (`Theme/FormTokens.axaml:19,36`) | Selected text is still readable (the selected label measures 7.36 / 8.58) and its colour changes. The highlight itself nearly vanishes, and mouse selection happens with the pointer over the field, which is the hover state. | Give the form its own highlight. Light `#B3CBF3`: 1.57 / 1.29 / 1.20 against rest / hover / pressed fills, label `#0E3C9E` 5.91. Dark `#2B4A7A`: 1.77 / 1.38 / 1.28, label `#BEDAFF` 6.20. Changes only `Nvt.Form.SelectionBrush`. |
| A2 | Dark checked `toggleSegment`, `toggleTab`, `toggleIcon` fill | segment against its group surface 1.57; tab and icon against the page 2.40 / 2.28 / 2.02. Light passes | `Theme/ToggleStyles.axaml:98`; `Theme/ToggleTokens.axaml:41` (`#1148BE`) | Label colour and SemiBold weight are a second cue. White reaches only 4.08:1 on the lightest fill that is 3:1 against `#26364D`, so a white label cannot satisfy both floors. This is a design choice and not a token tweak. | Either add the 1 px accent outline that `toggleSoft` already draws, or use the Dark primary-button recipe: fill `#5FA5FA`, label `#0B1220` (fill against group surface 4.80, against pages 7.35 / 6.97 / 6.18, label 7.35). |
| A3 | Console selected row fill against the row | 1.27 against Surface, 1.16 against an error row (Light); 1.45 and 1.38 (Dark) | `LogConsole/ConsoleRowPresenter.cs:275` | A selected row is told apart by fill only. Light on an error row is under the repo's own 1.2 fill floor. | Add a non-colour cue (the 2 x 12 DIP accent marker that `ListBoxItem` uses), or accept. No token value reaches 3:1 without breaking text contrast on the row. |
| A4 | Light focus ring inside a pressed layer | 2.82 / 3.06 / 2.94 (AppBackground / Surface / SurfaceSubtle); Dark 3.00 or more | `Theme/ThemeTokens.axaml:73` (`Nvt.Focus.RingBrush` `#1F6FD1`) | Already named as a known limitation in PR #269 (it quotes 2.939 and 3.004). Only applies where a ring is drawn inside the control's own pressed fill. AppBackground at 2.82 was not in that note. | `#1A5FBD`: 3.51 / 3.66 / 3.74 on the pressed layer, 5.61 or more on every page surface. |
| A5 | Light and Dark neutral borders carrying no unique information | Expander container `NfcBorderMutedBrush` 1.36 to 1.81; `chipStatus` neutral border 1.36 to 1.81; ToolTip border `NfcBorderSoftBrush` 2.08 to 2.56 | `Theme/ExpanderStyles.axaml:13`; `Theme/ButtonStyles.axaml:345`; `Focus/ToolTipStyles.axaml:9` | Grouping edge, label or content carries the meaning. | No change recommended. If a stronger edge is wanted, `NfcBorderBrush` gives 3.67 or more. |
| A6 | `GridSplitter` 1 px line at rest | 2.34 / 2.56 / 2.45 (Light); 2.47 / 2.34 / 2.08 (Dark) | `Theme/DividerStyles.axaml:42` (`NfcBorderSoftBrush`) | The 4 x 24 DIP grip next to it (`NfcBorderBrush`, line `:59`) passes at 3.67 or more. | Optional: `NfcBorderBrush` for the line. |
| A7 | `ProgressBar` empty track against the page | 1.36 / 1.48 / 1.42 (Light); 1.81 / 1.71 / 1.52 (Dark) | `Theme/ProgressStyles.axaml:16` | The indicator passes against both the track and the page; the track only shows the total length. | No change: darkening the track would pull the indicator toward it. |
| A8 | Neutral button hover border against its own hover fill | 2.71 / 2.54 | `Theme/ButtonStyles.axaml:116` | New with the darker `HoverFillBrush` from PR #269. The border against the page is unchanged at 3.67 to 4.02 (Light) and 3.96 to 4.71 (Dark). | None needed. Listed because it is a pair the theme defines. |
| A9 | Dark `.danger` checked fill as a segment, tab or icon | 1.70 against the group surface, 2.19 to 2.61 against the page | `Theme/ToggleStyles.axaml:180` | Label (white on `#A82035`, 7.18) carries the text. The switch case is R3. | Covered by the R3 values. |

Also noted, not counted: `Nvt.Controls.SelectedPointerOverBrush` against the page is 1.08 / 1.19 / 1.14 (Light) because hover lightens a selected row. PR #268 lists this as a known limitation; the selection marker or label still shows the state.

## 4. Tight text margins (enabled, under 5.0:1)

None fail. These have the least headroom and move first if a token changes.

| Ratio (L / D) | Pair | Source |
|---|---|---|
| 4.588 / 5.056 | Form placeholder on the pressed form fill | `Theme/FormStyles.axaml:62`, `Theme/FormTokens.axaml:8,25` |
| 4.619 / 4.804 | Console link hover text `NfcAccentBrush` on a selected row | `LogConsole/ConsoleTextPresenter.cs:249` |
| 4.837 / 4.675 | `NfcAccentStrongBrush` on `PressedFillBrush` (checked + pressed) | `Theme/ButtonStyles.axaml:204,222,258,296` |
| 4.954 / 5.444 (Warn icon 5.002 in Light) | `NfcTextMutedBrush` and `NfcWarningTextBrush` on `NfcSelectionSurfaceBrush` (console time, `.muted`, placeholder on hover) | `LogConsole/ConsoleRowPresenter.cs:28,117`, `Theme/TextStyles.axaml:65` |

NFH and NFU accent overrides (values from `PaletteContrastTests`): the lowest accent-dependent text pair is 4.57 (NFH Light link hover) and 4.67 (NFH and NFU Dark checked + pressed). No new failure appears for either tool. Only the four accent keys and the three accent border keys were overridden; any other host override is not covered.

## 5. Exempt: disabled text and decoration

Disabled text is exempt under WCAG 1.4.3 ("inactive user interface component"). All of it is at or above 3.57:1, which is also the repo's own disabled floor of 3:1 (`PaletteContrastTests`).

| Ratio | Pair | Source |
|---|---|---|
| 4.36 / 4.16 Light | `NfcTextDisabledBrush` `#68778C` on SurfaceSubtle / AppBackground (buttons, choice, list, menu, tabs, toggles, form) | e.g. `Theme/ButtonStyles.axaml:266`, `Theme/ChoiceStyles.axaml:112`, `Theme/FormStyles.axaml:242`, `Theme/TabStyles.axaml:96` |
| 3.58 Light / 3.57 Dark | `NfcTextDisabledBrush` on `NfcSelectionSurfaceBrush` (selected + disabled list item, tab, toggle) | `Theme/ListStyles.axaml:82` (fill; label at `:78`), `Theme/TabStyles.axaml:100`, `Theme/ToggleStyles.axaml:118` |
| 4.36 Light | disabled spinner glyph, soft toggle, `actionGhost.flat` | `Theme/FormStyles.axaml:302`, `Theme/ToggleStyles.axaml:270`, `Theme/ButtonStyles.axaml:280` |

Disabled borders (`NfcBorderMutedBrush`, 1.36 to 1.81) and the disabled splitter are also exempt.
Decoration only: `Separator` and `Border.divider` (`NfcDividerBrush`, 1.13 to 1.49). They carry no information.

## 6. Latent text pairs: muted, accent and semantic text on a state layer

Since PR #269, hover and pressed on rows that rest with no fill are 20% and 24% tints (`Nvt.Interaction.HoverBrush`, `PressedBrush`) over whatever is behind. Core's own styles switch the label to `NfcTextBrush` or `NfcTextStrongBrush` in those states, so none of these is reached by Core. A host that puts a text token inside a `ListBoxItem`, `MenuItem`, ghost button, `toggleTab` or `toggleSoft` (for example a secondary line in a list item) reaches them. Consumer usage was not audited.

Lowest ratio of each token across the hover and pressed layers (both over AppBackground, Surface and SurfaceSubtle) and the two opaque fills `HoverFillBrush` and `PressedFillBrush`:

| Token | Light | Dark |
|---|---|---|
| `NfcTextBrush` | 8.35 | 6.40 |
| `NfcTextSecondaryBrush` | **4.32** | 5.31 |
| `NfcTextMutedBrush` | **3.60** | **3.51** |
| `NfcAccentBrush` | **3.35** | **3.10** |
| `NfcAccentStrongBrush` | **4.45** | **4.14** |
| `NfcDangerTextBrush` | **4.10** | **4.47** |
| `NfcWarningTextBrush` | **3.63** | 5.29 |
| `NfcSuccessTextBrush` | **4.07** | 4.98 |
| `NfcInfoTextBrush` | **4.02** | **4.26** |

Worst cases are the pressed layer, on AppBackground in Light and mostly on SurfaceSubtle in Dark.

Options, in the order of least change:

1. Follow the pattern #269 used for the menu gesture text: swap to `NfcTextBrush` while a state layer shows. No token changes.
2. Token-level: `NfcTextMutedBrush` would need Light about `#3F4E63` (4.83) and Dark about `#BCC7D8` (4.62) to hold 4.5:1 on every layer. That leaves Muted close to Secondary in Light (`#475569`), so option 1 is the better fit.
3. Lower the layer alpha. This trades against the 1.2 / 1.25 fill-separation floor pinned by `InteractionContrastTests`.

`LoadingSurface` (`Progress/ProgressStyles.axaml:11`): Core sets only the scrim (`NfcModalScrimBrush`, Light `#660F172A`, Dark `#B30B1220`) and no content colour. Over the Light scrim `NfcTextBrush` measures 5.31 to 5.73; `NfcTextMutedBrush` measures 2.29 to 2.47 (Dark 7.98 and above). Content placed directly on the scrim in Light should not use Muted.

## 7. State-fill separation against the page

Informational. WCAG does not require 3:1 for a hover or pressed tint on its own. The repo pins a practical floor of 1.2 (Light) and 1.25 (Dark) in `InteractionContrastTests` for the fills it lists. Values are against AppBackground / Surface / SurfaceSubtle after compositing.

| Fill | Light | Dark |
|---|---|---|
| `Nvt.Interaction.HoverBrush` (20%) | 1.47 / 1.48 / 1.48 | 1.71 / 1.76 / 1.76 |
| `Nvt.Interaction.PressedBrush` (24%) | 1.60 / 1.61 / 1.61 | 1.94 / 1.99 / 2.00 |
| `Nvt.Interaction.HoverFillBrush` | 1.35 / 1.48 / 1.42 | 1.85 / 1.76 / 1.56 |
| `Nvt.Interaction.PressedFillBrush` | 1.47 / 1.61 / 1.54 | 2.10 / 1.99 / 1.77 |
| `NfcSelectionSurfaceBrush` | 1.16 / 1.27 / 1.22 | 1.53 / 1.45 / 1.29 |
| `Nvt.Form.PressedFillBrush` | 1.25 / 1.37 / 1.31 | 1.65 / 1.56 / 1.39 |
| `Nvt.Controls.SelectedBrush` | 1.21 / 1.32 / 1.26 | 1.52 / 1.44 / 1.28 |
| `Nvt.Controls.SelectedPointerOverBrush` | **1.08 / 1.19 / 1.14** | 1.47 / 1.39 / 1.23 |
| `Nvt.Controls.SelectedPressedBrush` | **1.14 / 1.25 / 1.19** | 1.67 / 1.58 / 1.40 |

Below the repo floor: Light `NfcSelectionSurfaceBrush` on AppBackground (1.16), Light `SelectedPointerOverBrush` on all three surfaces, and Light `SelectedPressedBrush` on AppBackground and SurfaceSubtle (1.14, 1.19). The repo test lists only Surface and SurfaceSubtle and does not cover the two selected hover and pressed tokens.

## 8. Method and limits

- Palette values were parsed from the `*.axaml` token files at `53954f8`, aliases (`StaticResource`) resolved per theme variant. Spot checks against numbers quoted in PR #269 match: form placeholder pressed 4.588 / 5.056, focus ring on the pressed layer 2.939 / 3.004.
- Composite: `out = top * a + bottom * (1 - a)` per channel, with `a = alpha / 255`, on 8-bit channels. Layer order follows the paint order: translucent fill over its parent surface, then text. Rest fills (`Nvt.Interaction.RestBrush`, alpha 0) composite to the parent exactly.
- Parents used for transparent rest fills: AppBackground, Surface, SurfaceSubtle; popups use Surface; the segment group uses `NfcSelectionSurfaceBrush`; tabs use the strip (SurfaceSubtle); console rows use Surface, the selection fill, or the error surface.
- Choice rows and Expander headers were measured in both rest-fill modes (`Soft` default, and `None`).
- Not covered: high-contrast themes, Avalonia Fluent defaults for controls Core does not restyle, host overrides beyond the NFH and NFU accent keys, the fade between states (only end states), text over images, anti-aliasing and font weight, and the keyboard-focus ring against a neighbouring control. Focus rings drawn outside the control (`Margin -4`) were measured against the page; they are not adjacent to the control fill, so a ring-against-fill pair there is not a finding (for example `Nvt.Focus.RingBrush` on `NfcAccentBrush` is 1.19 in Light, but the ring sits 2 to 4 px outside the button).
- Classification into Required and Advisory is a judgement. Every number behind it is in Appendix A.

## Appendix A. Every measured pair

Source column is `file:line` under `src/Nvt.Core.Avalonia/` of the setter that applies the colour (or the token line). "over" means the translucent layer composited on that parent.

### A.1 Text pairs (floor 4.5:1)

Bold = below the floor. `ex` = disabled/decorative (exempt). `adv` = advisory class (see section 3).


#### Button

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| actionNeutral / chipAction / actionIconButton | rest | `NfcTextBrush` | `NfcSurfaceBrush` | 14.63 | 14.39 | `Theme/ButtonStyles.axaml:111` |  |
| actionNeutral | hover | `NfcTextBrush` | `Nvt.Interaction.HoverFillBrush` | 9.86 | 8.20 | `Theme/ButtonStyles.axaml:115` |  |
| actionNeutral | pressed | `NfcTextBrush` | `Nvt.Interaction.PressedFillBrush` | 9.08 | 7.23 | `Theme/ButtonStyles.axaml:121` |  |
| actionNeutral / chip / icon | flyout-open | `NfcTextBrush` | `Nvt.Interaction.HoverFillBrush` | 9.86 | 8.20 | `Theme/ButtonStyles.axaml:187` |  |
| actionGhost | flyout-open | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 9.06 | 8.86 | `Theme/ButtonStyles.axaml:182` |  |
| actionGhost | flyout-open | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/ButtonStyles.axaml:182` |  |
| actionGhost | flyout-open | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 9.44 | 7.24 | `Theme/ButtonStyles.axaml:182` |  |
| actionPrimary | rest | `Nvt.Button.PrimaryLabelBrush` | `NfcAccentBrush` | 5.88 | 7.35 | `Theme/ButtonStyles.axaml:129` |  |
| actionPrimary | hover / pressed | `Nvt.Button.PrimaryLabelBrush` | `NfcAccentStrongBrush` | 7.79 | 9.82 | `Theme/ButtonStyles.axaml:135` |  |
| actionDanger | rest | `NfcDangerTextBrush` | `NfcDangerSurfaceBrush` | 6.54 | 9.58 | `Theme/ButtonStyles.axaml:147` |  |
| actionDanger | hover | `NfcDangerTextBrush` | `NfcDangerSurfaceMutedBrush` | 6.16 | 9.04 | `Theme/ButtonStyles.axaml:153` |  |
| actionDanger | pressed | `NfcDangerTextStrongBrush` | `NfcCriticalSurfaceBrush` | 7.93 | 11.06 | `Theme/ButtonStyles.axaml:159` |  |
| actionGhost (rest = alpha-0 layer) | rest | `NfcTextSecondaryBrush` | `Nvt.Interaction.RestBrush over NfcAppBackgroundBrush` | 6.92 | 12.61 | `Theme/ButtonStyles.axaml:165` |  |
| actionGhost (rest = alpha-0 layer) | rest | `NfcTextSecondaryBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 7.58 | 11.95 | `Theme/ButtonStyles.axaml:165` |  |
| actionGhost (rest = alpha-0 layer) | rest | `NfcTextSecondaryBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `Theme/ButtonStyles.axaml:165` |  |
| actionGhost | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 9.06 | 8.86 | `Theme/ButtonStyles.axaml:171` |  |
| actionGhost | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/ButtonStyles.axaml:171` |  |
| actionGhost | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 9.44 | 7.24 | `Theme/ButtonStyles.axaml:171` |  |
| actionGhost | pressed (24% layer) | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | 8.35 | 7.82 | `Theme/ButtonStyles.axaml:177` |  |
| actionGhost | pressed (24% layer) | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 9.08 | 7.23 | `Theme/ButtonStyles.axaml:177` |  |
| actionGhost | pressed (24% layer) | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 8.71 | 6.40 | `Theme/ButtonStyles.axaml:177` |  |
| ToggleButton action* (neutral, chip, icon, primary, ghost) | checked | `NfcAccentStrongBrush` | `NfcAccentSurfaceBrush` | 7.02 | 7.67 | `Theme/ButtonStyles.axaml:194` |  |
| ToggleButton action* | checked + hover | `NfcAccentStrongBrush` | `NfcAccentSurfaceSubtleBrush` | 7.40 | 8.54 | `Theme/ButtonStyles.axaml:200` |  |
| ToggleButton action* | checked + pressed | `NfcAccentStrongBrush` | `Nvt.Interaction.PressedFillBrush` | 4.84 | 4.67 | `Theme/ButtonStyles.axaml:204` |  |
| actionGhost.flat | checked / active | `NfcAccentStrongBrush` | `NfcAccentSurfaceBrush` | 7.02 | 7.67 | `Theme/ButtonStyles.axaml:286` |  |
| actionGhost.flat | checked / active + hover | `NfcAccentStrongBrush` | `NfcSelectionSurfaceBrush` | 6.12 | 6.41 | `Theme/ButtonStyles.axaml:292` |  |
| actionGhost.flat | checked / active + pressed | `NfcAccentStrongBrush` | `Nvt.Interaction.PressedFillBrush` | 4.84 | 4.67 | `Theme/ButtonStyles.axaml:296` |  |
| ToggleButton.actionDanger | checked / + hover | `NfcDangerTextStrongBrush` | `NfcDangerSurfaceMutedBrush` | 8.16 | 11.58 | `Theme/ButtonStyles.axaml:230` |  |
| ToggleButton.actionDanger | checked + pressed | `NfcDangerTextStrongBrush` | `NfcCriticalSurfaceBrush` | 7.93 | 11.06 | `Theme/ButtonStyles.axaml:242` |  |
| all action* buttons | disabled | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/ButtonStyles.axaml:266` | ex |
| actionGhost.flat | disabled (no fill) | `NfcTextDisabledBrush` | `NfcAppBackgroundBrush` | **4.16** | 5.47 | `Theme/ButtonStyles.axaml:280` | ex |
| actionGhost.flat | disabled (no fill) | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ButtonStyles.axaml:280` | ex |
| actionGhost.flat | disabled (no fill) | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/ButtonStyles.axaml:280` | ex |
| chipStatus (neutral) | static | `NfcTextSecondaryBrush` | `NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `Theme/ButtonStyles.axaml:349` |  |
| chipStatus.warning | static | `NfcWarningTextBrush` | `NfcWarningSurfaceBrush` | 6.14 | 10.34 | `Theme/ButtonStyles.axaml:362` |  |
| chipStatus.danger | static | `NfcDangerTextBrush` | `NfcDangerSurfaceBrush` | 6.54 | 9.58 | `Theme/ButtonStyles.axaml:375` |  |
| chipStatus.success | static | `NfcSuccessTextBrush` | `NfcSuccessSurfaceBrush` | 6.77 | 9.00 | `Theme/ButtonStyles.axaml:388` |  |

#### Choice

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| CheckBox / RadioButton label | rest, Soft mode | `Nvt.Controls.ChoiceForegroundBrush` | `NfcSurfaceSubtleBrush` | 17.06 | 15.74 | `Theme/ChoiceStyles.axaml:34` |  |
| CheckBox / RadioButton label | rest, None mode (alpha-0 layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.RestBrush over NfcAppBackgroundBrush` | 16.30 | 18.72 | `Theme/ChoiceStyles.axaml:125` |  |
| CheckBox / RadioButton label | rest, None mode (alpha-0 layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 17.85 | 17.74 | `Theme/ChoiceStyles.axaml:125` |  |
| CheckBox / RadioButton label | rest, None mode (alpha-0 layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 17.06 | 15.74 | `Theme/ChoiceStyles.axaml:125` |  |
| CheckBox / RadioButton label | hover, Soft mode | `Nvt.Controls.ChoiceForegroundBrush` | `NfcSelectionSurfaceBrush` | 14.02 | 12.23 | `Theme/ChoiceStyles.axaml:128` |  |
| CheckBox / RadioButton label | hover, None mode (20% layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 11.06 | 10.92 | `Theme/ChoiceStyles.axaml:135` |  |
| CheckBox / RadioButton label | hover, None mode (20% layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 12.03 | 10.11 | `Theme/ChoiceStyles.axaml:135` |  |
| CheckBox / RadioButton label | hover, None mode (20% layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 11.52 | 8.93 | `Theme/ChoiceStyles.axaml:135` |  |
| CheckBox / RadioButton label | pressed, Soft mode | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.PressedFillBrush` | 11.08 | 8.91 | `Theme/ChoiceStyles.axaml:131` |  |
| CheckBox / RadioButton label | pressed, None mode (24% layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | 10.19 | 9.64 | `Theme/ChoiceStyles.axaml:138` |  |
| CheckBox / RadioButton label | pressed, None mode (24% layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 11.08 | 8.91 | `Theme/ChoiceStyles.axaml:138` |  |
| CheckBox / RadioButton label | pressed, None mode (24% layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 10.63 | 7.89 | `Theme/ChoiceStyles.axaml:138` |  |
| CheckBox / RadioButton label | checked, Soft mode | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Controls.SelectedBrush` | 13.51 | 12.29 | `Theme/ChoiceStyles.axaml:141` |  |
| CheckBox / RadioButton label | checked, None mode (alpha-0 layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Controls.SelectedRestBrush over NfcAppBackgroundBrush` | 16.30 | 18.72 | `Theme/ChoiceStyles.axaml:144` |  |
| CheckBox / RadioButton label | checked, None mode (alpha-0 layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Controls.SelectedRestBrush over NfcSurfaceBrush` | 17.85 | 17.74 | `Theme/ChoiceStyles.axaml:144` |  |
| CheckBox / RadioButton label | checked, None mode (alpha-0 layer) | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Controls.SelectedRestBrush over NfcSurfaceSubtleBrush` | 17.06 | 15.74 | `Theme/ChoiceStyles.axaml:144` |  |
| CheckBox / RadioButton label | checked + hover | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Controls.SelectedPointerOverBrush` | 15.02 | 12.74 | `Theme/ChoiceStyles.axaml:147` |  |
| CheckBox / RadioButton label | checked + pressed | `Nvt.Controls.ChoiceForegroundBrush` | `Nvt.Controls.SelectedPressedBrush` | 14.33 | 11.22 | `Theme/ChoiceStyles.axaml:150` |  |
| CheckBox / RadioButton label | disabled | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/ChoiceStyles.axaml:112` | ex |
| CheckBox / RadioButton label | disabled | `NfcTextDisabledBrush` | `NfcAppBackgroundBrush` | **4.16** | 5.47 | `Theme/ChoiceStyles.axaml:112` | ex |
| CheckBox / RadioButton label | disabled | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ChoiceStyles.axaml:112` | ex |

#### Toggle

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| toggleSegment | rest (alpha-0 over group surface) | `NfcTextSecondaryBrush` | `Nvt.Toggle.SegmentRestBrush over NfcSelectionSurfaceBrush` | 5.95 | 8.24 | `Theme/ToggleStyles.axaml:35` |  |
| toggleSegment | hover | `NfcTextBrush` | `NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `Theme/ToggleStyles.axaml:87` |  |
| toggleSegment / toggleIcon | pressed | `NfcTextStrongBrush` | `Nvt.Interaction.PressedFillBrush` | 11.08 | 8.52 | `Theme/ToggleStyles.axaml:94` |  |
| toggleTab | rest (alpha-0 layer) | `NfcTextSecondaryBrush` | `Nvt.Interaction.RestBrush over NfcAppBackgroundBrush` | 6.92 | 12.61 | `Theme/ToggleStyles.axaml:36` |  |
| toggleTab | rest (alpha-0 layer) | `NfcTextSecondaryBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 7.58 | 11.95 | `Theme/ToggleStyles.axaml:36` |  |
| toggleTab | rest (alpha-0 layer) | `NfcTextSecondaryBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `Theme/ToggleStyles.axaml:36` |  |
| toggleTab | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 9.06 | 8.86 | `Theme/ToggleStyles.axaml:78` |  |
| toggleTab | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/ToggleStyles.axaml:78` |  |
| toggleTab | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 9.44 | 7.24 | `Theme/ToggleStyles.axaml:78` |  |
| toggleTab | pressed (24% layer) | `NfcTextStrongBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | 10.19 | 9.22 | `Theme/ToggleStyles.axaml:90` |  |
| toggleTab | pressed (24% layer) | `NfcTextStrongBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 11.08 | 8.52 | `Theme/ToggleStyles.axaml:90` |  |
| toggleTab | pressed (24% layer) | `NfcTextStrongBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 10.63 | 7.54 | `Theme/ToggleStyles.axaml:90` |  |
| toggleIcon | rest (opaque) | `NfcTextSecondaryBrush` | `NfcSurfaceBrush` | 7.58 | 11.95 | `Theme/ToggleStyles.axaml:36` |  |
| toggleIcon | rest (opaque) | `NfcTextSecondaryBrush` | `NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `Theme/ToggleStyles.axaml:36` |  |
| toggleIcon | hover | `NfcTextBrush` | `Nvt.Interaction.HoverFillBrush` | 9.86 | 8.20 | `Theme/ToggleStyles.axaml:82` |  |
| toggleSegment / Tab / Icon | checked | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SelectedBrush` | 5.88 | 7.79 | `Theme/ToggleStyles.axaml:99` |  |
| toggleSegment / Tab / Icon | checked + hover | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SelectedPointerOverBrush` | 7.79 | 9.73 | `Theme/ToggleStyles.axaml:104` |  |
| toggleSegment / Tab / Icon | checked + pressed | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SelectedPressedBrush` | 9.73 | 12.08 | `Theme/ToggleStyles.axaml:109` |  |
| toggle*.danger | checked | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.DangerFillBrush` | 7.18 | 7.18 | `Theme/ToggleStyles.axaml:180` |  |
| toggle*.danger | checked + hover | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.DangerFillPointerOverBrush` | 9.52 | 9.52 | `Theme/ToggleStyles.axaml:184` |  |
| toggle*.danger | checked + pressed | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.DangerFillPressedBrush` | 9.52 | 9.52 | `Theme/ToggleStyles.axaml:187` |  |
| toggleSegment / Tab / Icon | disabled | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/ToggleStyles.axaml:114` | ex |
| toggleSegment / Tab / Icon | disabled + checked | `NfcTextDisabledBrush` | `NfcSelectionSurfaceBrush` | **3.58** | **3.57** | `Theme/ToggleStyles.axaml:118` | ex |
| toggleSoft | rest (alpha-0 layer) | `Nvt.Toggle.SoftForegroundBrush` | `Nvt.Interaction.RestBrush over NfcAppBackgroundBrush` | 6.92 | 12.61 | `Theme/ToggleStyles.axaml:204` |  |
| toggleSoft | rest (alpha-0 layer) | `Nvt.Toggle.SoftForegroundBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 7.58 | 11.95 | `Theme/ToggleStyles.axaml:204` |  |
| toggleSoft | rest (alpha-0 layer) | `Nvt.Toggle.SoftForegroundBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `Theme/ToggleStyles.axaml:204` |  |
| toggleSoft | hover (20% layer) | `Nvt.Toggle.SoftPointerOverForegroundBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 9.06 | 8.86 | `Theme/ToggleStyles.axaml:242` |  |
| toggleSoft | hover (20% layer) | `Nvt.Toggle.SoftPointerOverForegroundBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/ToggleStyles.axaml:242` |  |
| toggleSoft | hover (20% layer) | `Nvt.Toggle.SoftPointerOverForegroundBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 9.44 | 7.24 | `Theme/ToggleStyles.axaml:242` |  |
| toggleSoft | pressed (24% layer) | `Nvt.Toggle.SoftPressedForegroundBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | 10.19 | 9.22 | `Theme/ToggleStyles.axaml:246` |  |
| toggleSoft | pressed (24% layer) | `Nvt.Toggle.SoftPressedForegroundBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 11.08 | 8.52 | `Theme/ToggleStyles.axaml:246` |  |
| toggleSoft | pressed (24% layer) | `Nvt.Toggle.SoftPressedForegroundBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 10.63 | 7.54 | `Theme/ToggleStyles.axaml:246` |  |
| toggleSoft | checked | `Nvt.Toggle.SoftCheckedForegroundBrush` | `Nvt.Toggle.SoftCheckedBrush` | 7.36 | 8.58 | `Theme/ToggleStyles.axaml:251` |  |
| toggleSoft | checked + hover | `Nvt.Toggle.SoftCheckedForegroundBrush` | `Nvt.Toggle.SoftCheckedPointerOverBrush` | 8.19 | 8.90 | `Theme/ToggleStyles.axaml:255` |  |
| toggleSoft | checked + pressed | `Nvt.Toggle.SoftCheckedForegroundBrush` | `Nvt.Toggle.SoftCheckedPressedBrush` | 7.81 | 7.83 | `Theme/ToggleStyles.axaml:259` |  |
| toggleSoft | disabled | `Nvt.Toggle.SoftDisabledForegroundBrush` | `NfcAppBackgroundBrush` | **4.16** | 5.47 | `Theme/ToggleStyles.axaml:270` | ex |
| toggleSoft | disabled | `Nvt.Toggle.SoftDisabledForegroundBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ToggleStyles.axaml:270` | ex |
| toggleSoft | disabled | `Nvt.Toggle.SoftDisabledForegroundBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/ToggleStyles.axaml:270` | ex |
| toggleSoft | disabled + checked | `Nvt.Toggle.SoftDisabledForegroundBrush` | `Nvt.Toggle.SoftDisabledCheckedBrush` | **3.58** | **3.57** | `Theme/ToggleStyles.axaml:273` | ex |

#### Form

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| TextBox / NumericUpDown / ComboBox text | rest / read-only | `Nvt.Form.TextBrush` | `NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `Theme/FormStyles.axaml:38` |  |
| TextBox / NumericUpDown / ComboBox placeholder | rest / read-only | `Nvt.Form.PlaceholderBrush` | `NfcSurfaceSubtleBrush` | 6.03 | 7.01 | `Theme/FormStyles.axaml:62` |  |
| TextBox / NumericUpDown / ComboBox text | hover | `Nvt.Form.TextBrush` | `NfcSelectionSurfaceBrush` | 11.49 | 9.92 | `Theme/FormStyles.axaml:224` |  |
| TextBox / NumericUpDown / ComboBox placeholder | hover | `Nvt.Form.PlaceholderBrush` | `NfcSelectionSurfaceBrush` | 4.95 | 5.44 | `Theme/FormStyles.axaml:62` |  |
| TextBox / NumericUpDown / ComboBox text | pressed / ComboBox dropdown-open | `Nvt.Form.TextBrush` | `Nvt.Form.PressedFillBrush` | 10.64 | 9.21 | `Theme/FormStyles.axaml:228` |  |
| TextBox / NumericUpDown / ComboBox placeholder | pressed / ComboBox dropdown-open | `Nvt.Form.PlaceholderBrush` | `Nvt.Form.PressedFillBrush` | 4.59 | 5.06 | `Theme/FormStyles.axaml:62` |  |
| TextBox / NumericUpDown / ComboBox | disabled text + placeholder | `Nvt.Form.DisabledTextBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/FormStyles.axaml:242` | ex |
| TextBox selection | selected text | `Nvt.Form.SelectionTextBrush` | `Nvt.Form.SelectionBrush` | 7.36 | 8.58 | `Theme/FormStyles.axaml:79` |  |
| RepeatButton.formSpin glyph | rest | `Nvt.Form.TextBrush` | `NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `Theme/FormStyles.axaml:283` |  |
| RepeatButton.formSpin glyph | hover | `Nvt.Form.TextBrush` | `Nvt.Form.HoverFillBrush` | 11.49 | 9.92 | `Theme/FormStyles.axaml:296` |  |
| RepeatButton.formSpin glyph | pressed | `Nvt.Form.TextBrush` | `Nvt.Form.PressedFillBrush` | 10.64 | 9.21 | `Theme/FormStyles.axaml:299` |  |
| RepeatButton.formSpin glyph | disabled | `Nvt.Form.DisabledTextBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/FormStyles.axaml:302` | ex |

#### List / Menu

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| ListBoxItem / ComboBoxItem | rest (alpha-0 layer) | `NfcTextBrush` | `Nvt.Interaction.RestBrush over NfcAppBackgroundBrush` | 13.35 | 15.19 | `Theme/ListStyles.axaml:19` |  |
| ListBoxItem / ComboBoxItem | rest (alpha-0 layer) | `NfcTextBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 14.63 | 14.39 | `Theme/ListStyles.axaml:19` |  |
| ListBoxItem / ComboBoxItem | rest (alpha-0 layer) | `NfcTextBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `Theme/ListStyles.axaml:19` |  |
| ListBoxItem / ComboBoxItem | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 9.06 | 8.86 | `Theme/ListStyles.axaml:62` |  |
| ListBoxItem / ComboBoxItem | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/ListStyles.axaml:62` |  |
| ListBoxItem / ComboBoxItem | hover (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 9.44 | 7.24 | `Theme/ListStyles.axaml:62` |  |
| ListBoxItem / ComboBoxItem | pressed (24% layer) | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | 8.35 | 7.82 | `Theme/ListStyles.axaml:65` |  |
| ListBoxItem / ComboBoxItem | pressed (24% layer) | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 9.08 | 7.23 | `Theme/ListStyles.axaml:65` |  |
| ListBoxItem / ComboBoxItem | pressed (24% layer) | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 8.71 | 6.40 | `Theme/ListStyles.axaml:65` |  |
| ListBoxItem / ComboBoxItem | selected | `Nvt.List.SelectedLabelBrush` | `Nvt.List.SelectedBrush` | 7.36 | 8.58 | `Theme/ListStyles.axaml:68` |  |
| ListBoxItem / ComboBoxItem | selected + hover | `Nvt.List.SelectedLabelBrush` | `Nvt.List.SelectedPointerOverBrush` | 8.19 | 8.90 | `Theme/ListStyles.axaml:72` |  |
| ListBoxItem / ComboBoxItem | selected + pressed | `Nvt.List.SelectedLabelBrush` | `Nvt.List.SelectedPressedBrush` | 7.81 | 7.83 | `Theme/ListStyles.axaml:75` |  |
| ListBoxItem / ComboBoxItem | disabled | `NfcTextDisabledBrush` | `NfcAppBackgroundBrush` | **4.16** | 5.47 | `Theme/ListStyles.axaml:78` | ex |
| ListBoxItem / ComboBoxItem | disabled | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ListStyles.axaml:78` | ex |
| ListBoxItem / ComboBoxItem | disabled | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/ListStyles.axaml:78` | ex |
| ListBoxItem / ComboBoxItem | selected + disabled | `NfcTextDisabledBrush` | `NfcSelectionSurfaceBrush` | **3.58** | **3.57** | `Theme/ListStyles.axaml:82` | ex |
| MenuItem | rest (alpha-0 layer over popup) | `NfcTextBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 14.63 | 14.39 | `Theme/MenuStyles.axaml:66` |  |
| MenuItem | hover / open / focus-visible (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/MenuStyles.axaml:164` |  |
| MenuItem | pressed (24% layer) | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 9.08 | 7.23 | `Theme/MenuStyles.axaml:167` |  |
| MenuItem gesture text | rest | `NfcTextMutedBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 6.31 | 7.90 | `Theme/MenuStyles.axaml:105` |  |
| MenuItem gesture text | hover / open / focus-visible | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/MenuStyles.axaml:170` |  |
| MenuItem gesture text | pressed | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 9.08 | 7.23 | `Theme/MenuStyles.axaml:170` |  |
| MenuItem label + gesture | disabled | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/MenuStyles.axaml:174` | ex |

#### Tab

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| TabItem | rest (alpha-0 layer over strip) | `Nvt.Tab.TextBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `Theme/TabStyles.axaml:41` |  |
| TabItem | hover (20% layer) | `Nvt.Tab.HoverTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 9.44 | 7.24 | `Theme/TabStyles.axaml:76` |  |
| TabItem | pressed (24% layer) | `Nvt.Tab.PressedTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 10.63 | 7.54 | `Theme/TabStyles.axaml:80` |  |
| TabItem | selected | `Nvt.Tab.SelectedTextBrush` | `Nvt.Tab.SelectedFillBrush` | 7.36 | 8.58 | `Theme/TabStyles.axaml:84` |  |
| TabItem | selected + hover | `Nvt.Tab.SelectedTextBrush` | `Nvt.Tab.SelectedHoverFillBrush` | 8.19 | 8.90 | `Theme/TabStyles.axaml:88` |  |
| TabItem | selected + pressed | `Nvt.Tab.SelectedTextBrush` | `Nvt.Tab.SelectedPressedFillBrush` | 7.81 | 7.83 | `Theme/TabStyles.axaml:92` |  |
| TabItem | disabled | `Nvt.Tab.DisabledTextBrush` | `NfcSurfaceSubtleBrush` | **4.36** | 4.60 | `Theme/TabStyles.axaml:96` | ex |
| TabItem | selected + disabled | `Nvt.Tab.DisabledTextBrush` | `NfcSelectionSurfaceBrush` | **3.58** | **3.57** | `Theme/TabStyles.axaml:100` | ex |

#### Expander

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| Expander header | rest, Soft mode | `NfcTextBrush` | `NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `Theme/ExpanderStyles.axaml:55` |  |
| Expander header | rest, None mode (alpha-0 layer over container) | `NfcTextBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 14.63 | 14.39 | `Theme/ExpanderStyles.axaml:55` |  |
| Expander header | hover, Soft mode | `NfcTextBrush` | `NfcSelectionSurfaceBrush` | 11.49 | 9.92 | `Theme/ExpanderStyles.axaml:106` |  |
| Expander header | hover, None mode (20% layer) | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/ExpanderStyles.axaml:114` |  |
| Expander header | pressed, Soft mode | `Nvt.Controls.ExpanderPressedForegroundBrush` | `Nvt.Interaction.PressedFillBrush` | 11.62 | 8.91 | `Theme/ExpanderStyles.axaml:110` |  |
| Expander header | pressed, None mode (24% layer) | `Nvt.Controls.ExpanderPressedForegroundBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 11.62 | 8.91 | `Theme/ExpanderStyles.axaml:117` |  |
| Expander header | disabled | `Nvt.Controls.ExpanderDisabledForegroundBrush` | `NfcSurfaceSubtleBrush` | 4.79 | 6.00 | `Theme/ExpanderStyles.axaml:120` | ex |
| Expander header | disabled | `Nvt.Controls.ExpanderDisabledForegroundBrush` | `NfcSurfaceBrush` | 5.02 | 6.76 | `Theme/ExpanderStyles.axaml:120` | ex |

#### Text

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| TextBlock .title/.body/.caption/.mono… | static | `NfcTextBrush` | `NfcAppBackgroundBrush` | 13.35 | 15.19 | `Theme/TextStyles.axaml:8` |  |
| TextBlock .title/.body/.caption/.mono… | static | `NfcTextBrush` | `NfcSurfaceBrush` | 14.63 | 14.39 | `Theme/TextStyles.axaml:8` |  |
| TextBlock .title/.body/.caption/.mono… | static | `NfcTextBrush` | `NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `Theme/TextStyles.axaml:8` |  |
| TextBlock .title/.body/.caption/.mono… | static | `NfcTextBrush` | `NfcSelectionSurfaceBrush` | 11.49 | 9.92 | `Theme/TextStyles.axaml:8` |  |
| TextBlock .title/.body/.caption/.mono… | static | `NfcTextBrush` | `NfcAccentSurfaceBrush` | 13.17 | 11.87 | `Theme/TextStyles.axaml:8` |  |
| TextBlock .title/.body/.caption/.mono… | static | `NfcTextBrush` | `NfcAccentSurfaceSubtleBrush` | 13.89 | 13.20 | `Theme/TextStyles.axaml:8` |  |
| TextBlock.muted | static | `NfcTextMutedBrush` | `NfcAppBackgroundBrush` | 5.76 | 8.34 | `Theme/TextStyles.axaml:65` |  |
| TextBlock.muted | static | `NfcTextMutedBrush` | `NfcSurfaceBrush` | 6.31 | 7.90 | `Theme/TextStyles.axaml:65` |  |
| TextBlock.muted | static | `NfcTextMutedBrush` | `NfcSurfaceSubtleBrush` | 6.03 | 7.01 | `Theme/TextStyles.axaml:65` |  |
| TextBlock.muted | static | `NfcTextMutedBrush` | `NfcSelectionSurfaceBrush` | 4.95 | 5.44 | `Theme/TextStyles.axaml:65` |  |
| TextBlock.muted | static | `NfcTextMutedBrush` | `NfcAccentSurfaceBrush` | 5.68 | 6.51 | `Theme/TextStyles.axaml:65` |  |
| TextBlock.muted | static | `NfcTextMutedBrush` | `NfcAccentSurfaceSubtleBrush` | 5.99 | 7.25 | `Theme/TextStyles.axaml:65` |  |

#### Other

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| NumberScrubber input text | rest | `NfcTextBrush` | `NfcSurfaceBrush` | 14.63 | 14.39 | `Inputs/InputsStyles.axaml:9` |  |
| NumberScrubber input text | focused | `NfcTextBrush` | `NfcSelectionSurfaceBrush` | 11.49 | 9.92 | `Inputs/InputsStyles.axaml:16` |  |
| ToolTip | static | `Nvt.ToolTip.ForegroundBrush` | `Nvt.ToolTip.BackgroundBrush` | 14.63 | 14.39 | `Focus/ToolTipStyles.axaml:10` |  |
| ConfirmDialog / WarningDialog title | static | `NfcTextStrongBrush` | `NfcSurfaceBrush` | 17.85 | 16.96 | `Dialogs/ConfirmDialog.axaml:19` |  |
| ConfirmDialog / WarningDialog body | static | `NfcTextBrush` | `NfcSurfaceBrush` | 14.63 | 14.39 | `Dialogs/ConfirmDialog.axaml:24` |  |
| WorkspaceShell title | static | `NfcTextStrongBrush` | `NfcAppBackgroundBrush` | 16.30 | 17.89 | `Panels/PanelsStyles.axaml:134` |  |
| WorkspaceShell title | static | `NfcTextStrongBrush` | `NfcSurfaceBrush` | 17.85 | 16.96 | `Panels/PanelsStyles.axaml:134` |  |
| WorkspaceShell title | static | `NfcTextStrongBrush` | `NfcSurfaceSubtleBrush` | 17.06 | 15.04 | `Panels/PanelsStyles.axaml:134` |  |
| WorkspaceShell subtitle | static | `NfcTextMutedBrush` | `NfcAppBackgroundBrush` | 5.76 | 8.34 | `Panels/PanelsStyles.axaml:92` |  |
| WorkspaceShell subtitle | static | `NfcTextMutedBrush` | `NfcSurfaceBrush` | 6.31 | 7.90 | `Panels/PanelsStyles.axaml:92` |  |
| WorkspaceShell subtitle | static | `NfcTextMutedBrush` | `NfcSurfaceSubtleBrush` | 6.03 | 7.01 | `Panels/PanelsStyles.axaml:92` |  |
| SectionFrame / InfoLabel text | static | `NfcTextStrongBrush` | `NfcSurfaceBrush` | 17.85 | 16.96 | `Primitives/PrimitivesStyles.axaml:37` |  |
| Report pager labels | static | `NfcTextMutedBrush` | `NfcAppBackgroundBrush` | 5.76 | 8.34 | `ReportList/ReportPagerTemplates.axaml:14` |  |
| Report pager labels | static | `NfcTextMutedBrush` | `NfcSurfaceBrush` | 6.31 | 7.90 | `ReportList/ReportPagerTemplates.axaml:14` |  |
| Report pager labels | static | `NfcTextMutedBrush` | `NfcSurfaceSubtleBrush` | 6.03 | 7.01 | `ReportList/ReportPagerTemplates.axaml:14` |  |

#### Console

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| Row time + trace icon | static | `NfcTextMutedBrush` | `NfcSurfaceBrush` | 6.31 | 7.90 | `LogConsole/ConsoleRowPresenter.cs:28` |  |
| Row time + trace icon | static | `NfcTextMutedBrush` | `NfcSelectionSurfaceBrush` | 4.95 | 5.44 | `LogConsole/ConsoleRowPresenter.cs:28` |  |
| Row time + trace icon | static | `NfcTextMutedBrush` | `NfcDangerSurfaceBrush` | 5.74 | 7.52 | `LogConsole/ConsoleRowPresenter.cs:28` |  |
| Row source / count / arrow / debug icon | static | `NfcTextSecondaryBrush` | `NfcSurfaceBrush` | 7.58 | 11.95 | `LogConsole/ConsoleRowPresenter.cs:31` |  |
| Row source / count / arrow / debug icon | static | `NfcTextSecondaryBrush` | `NfcSelectionSurfaceBrush` | 5.95 | 8.24 | `LogConsole/ConsoleRowPresenter.cs:31` |  |
| Row source / count / arrow / debug icon | static | `NfcTextSecondaryBrush` | `NfcDangerSurfaceBrush` | 6.90 | 11.37 | `LogConsole/ConsoleRowPresenter.cs:31` |  |
| Row message + level text | static | `NfcTextBrush` | `NfcSurfaceBrush` | 14.63 | 14.39 | `LogConsole/ConsoleRowPresenter.cs:32` |  |
| Row message + level text | static | `NfcTextBrush` | `NfcSelectionSurfaceBrush` | 11.49 | 9.92 | `LogConsole/ConsoleRowPresenter.cs:32` |  |
| Row message + level text | static | `NfcTextBrush` | `NfcDangerSurfaceBrush` | 13.32 | 13.69 | `LogConsole/ConsoleRowPresenter.cs:32` |  |
| Info icon | static | `NfcInfoTextBrush` | `NfcSurfaceBrush` | 7.05 | 9.59 | `LogConsole/ConsoleRowPresenter.cs:116` |  |
| Info icon | static | `NfcInfoTextBrush` | `NfcSelectionSurfaceBrush` | 5.54 | 6.61 | `LogConsole/ConsoleRowPresenter.cs:116` |  |
| Info icon | static | `NfcInfoTextBrush` | `NfcDangerSurfaceBrush` | 6.42 | 9.12 | `LogConsole/ConsoleRowPresenter.cs:116` |  |
| Warn icon | static | `NfcWarningTextBrush` | `NfcSurfaceBrush` | 6.37 | 11.89 | `LogConsole/ConsoleRowPresenter.cs:117` |  |
| Warn icon | static | `NfcWarningTextBrush` | `NfcSelectionSurfaceBrush` | 5.00 | 8.20 | `LogConsole/ConsoleRowPresenter.cs:117` |  |
| Warn icon | static | `NfcWarningTextBrush` | `NfcDangerSurfaceBrush` | 5.80 | 11.32 | `LogConsole/ConsoleRowPresenter.cs:117` |  |
| Error icon | static | `NfcDangerTextBrush` | `NfcSurfaceBrush` | 7.18 | 10.06 | `LogConsole/ConsoleRowPresenter.cs:118` |  |
| Error icon | static | `NfcDangerTextBrush` | `NfcSelectionSurfaceBrush` | 5.64 | 6.94 | `LogConsole/ConsoleRowPresenter.cs:118` |  |
| Error icon | static | `NfcDangerTextBrush` | `NfcDangerSurfaceBrush` | 6.54 | 9.58 | `LogConsole/ConsoleRowPresenter.cs:118` |  |
| Fatal icon | static | `NfcDangerTextStrongBrush` | `NfcSurfaceBrush` | 9.52 | 12.89 | `LogConsole/ConsoleRowPresenter.cs:119` |  |
| Fatal icon | static | `NfcDangerTextStrongBrush` | `NfcSelectionSurfaceBrush` | 7.48 | 8.88 | `LogConsole/ConsoleRowPresenter.cs:119` |  |
| Fatal icon | static | `NfcDangerTextStrongBrush` | `NfcDangerSurfaceBrush` | 8.67 | 12.26 | `LogConsole/ConsoleRowPresenter.cs:119` |  |
| Search hit | static | `NfcWarningTextStrongBrush` | `NfcWarningSurfaceBrush` | 8.03 | 13.00 | `LogConsole/ConsoleTextPresenter.cs:226` |  |
| Link text hover | hover | `NfcAccentBrush` | `NfcSurfaceBrush` | 5.88 | 6.97 | `LogConsole/ConsoleTextPresenter.cs:249` |  |
| Link text hover | hover | `NfcAccentBrush` | `NfcSelectionSurfaceBrush` | 4.62 | 4.80 | `LogConsole/ConsoleTextPresenter.cs:249` |  |
| Link text hover | hover | `NfcAccentBrush` | `NfcDangerSurfaceBrush` | 5.36 | 6.63 | `LogConsole/ConsoleTextPresenter.cs:249` |  |
| Menu item label | rest | `NfcTextBrush` | `NfcSurfaceBrush` | 14.63 | 14.39 | `LogConsole/ConsoleControlStyles.axaml:23` |  |
| Menu item label | disabled | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `LogConsole/ConsoleControlStyles.axaml:26` | ex |
| consoleSearchFlag (unchecked) | rest | `NfcTextMutedBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 6.03 | 7.01 | `LogConsole/ConsoleControlStyles.axaml:55` |  |
| consoleSearchFlag | checked | `NfcTextBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `LogConsole/ConsoleControlStyles.axaml:60` |  |
| consoleSearchFlag | checked + hover | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 9.44 | 7.24 | `LogConsole/ConsoleControlStyles.axaml:65` |  |
| consoleSearchFlag | checked + pressed | `NfcTextBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 8.71 | 6.40 | `LogConsole/ConsoleControlStyles.axaml:70` |  |
| consoleSearch placeholder + toolbar search icon | static | `NfcTextMutedBrush` | `NfcSurfaceSubtleBrush` | 6.03 | 7.01 | `LogConsole/ConsoleControlStyles.axaml:116` |  |
| consoleSearch text / caret | static | `NfcTextBrush` | `NfcSurfaceSubtleBrush` | 13.98 | 12.77 | `LogConsole/ConsoleControlStyles.axaml:109` |  |
| consoleSearch selected text | selected | `NfcTextBrush` | `NfcSelectionSurfaceBrush` | 11.49 | 9.92 | `LogConsole/ConsoleControlStyles.axaml:108` |  |
| Empty state | static | `NfcTextMutedBrush` | `NfcSurfaceBrush` | 6.31 | 7.90 | `LogConsole/ConsoleEmptyState.axaml:12` |  |
| Retention banner | static | `NfcTextSecondaryBrush` | `NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `LogConsole/ConsoleListStyles.axaml:29` |  |
| Header icon | static | `NfcTextSecondaryBrush` | `NfcSurfaceBrush` | 7.58 | 11.95 | `LogConsole/ConsoleHeader.axaml:11` |  |

#### Other

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| LoadingSurface (content text over scrim; Core defines no content colour) | static | `NfcTextBrush` | `NfcModalScrimBrush over NfcAppBackgroundBrush` | 5.31 | 15.19 | `Progress/ProgressStyles.axaml:11` | adv |
| LoadingSurface (content text over scrim; Core defines no content colour) | static | `NfcTextBrush` | `NfcModalScrimBrush over NfcSurfaceBrush` | 5.73 | 14.94 | `Progress/ProgressStyles.axaml:11` | adv |
| LoadingSurface (content text over scrim; Core defines no content colour) | static | `NfcTextBrush` | `NfcModalScrimBrush over NfcSurfaceSubtleBrush` | 5.52 | 14.53 | `Progress/ProgressStyles.axaml:11` | adv |
| LoadingSurface (muted text over scrim) | static | `NfcTextMutedBrush` | `NfcModalScrimBrush over NfcAppBackgroundBrush` | **2.29** | 8.34 | `Progress/ProgressStyles.axaml:11` | adv |
| LoadingSurface (muted text over scrim) | static | `NfcTextMutedBrush` | `NfcModalScrimBrush over NfcSurfaceBrush` | **2.47** | 8.20 | `Progress/ProgressStyles.axaml:11` | adv |
| LoadingSurface (muted text over scrim) | static | `NfcTextMutedBrush` | `NfcModalScrimBrush over NfcSurfaceSubtleBrush` | **2.38** | 7.98 | `Progress/ProgressStyles.axaml:11` | adv |

### A.2 Non-text pairs (floor 3:1)

Bold = below the floor. `ex` = disabled/decorative (exempt). `adv` = advisory class (see section 3).


#### Button

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| actionNeutral | rest border | `NfcBorderBrush` | `NfcAppBackgroundBrush` | 3.67 | 4.71 | `Theme/ButtonStyles.axaml:110` |  |
| actionNeutral | rest border | `NfcBorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/ButtonStyles.axaml:110` |  |
| actionNeutral | rest border | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/ButtonStyles.axaml:110` |  |
| actionNeutral | hover border | `NfcBorderBrush` | `Nvt.Interaction.HoverFillBrush` | **2.71** | **2.54** | `Theme/ButtonStyles.axaml:116` |  |
| actionPrimary | rest fill vs page | `NfcAccentBrush` | `NfcAppBackgroundBrush` | 5.37 | 7.35 | `Theme/ButtonStyles.axaml:127` |  |
| actionPrimary | rest fill vs page | `NfcAccentBrush` | `NfcSurfaceBrush` | 5.88 | 6.97 | `Theme/ButtonStyles.axaml:127` |  |
| actionPrimary | rest fill vs page | `NfcAccentBrush` | `NfcSurfaceSubtleBrush` | 5.62 | 6.18 | `Theme/ButtonStyles.axaml:127` |  |
| actionPrimary | hover / pressed fill vs page | `NfcAccentStrongBrush` | `NfcAppBackgroundBrush` | 7.11 | 9.82 | `Theme/ButtonStyles.axaml:133` |  |
| actionPrimary | hover / pressed fill vs page | `NfcAccentStrongBrush` | `NfcSurfaceBrush` | 7.79 | 9.30 | `Theme/ButtonStyles.axaml:133` |  |
| actionPrimary | hover / pressed fill vs page | `NfcAccentStrongBrush` | `NfcSurfaceSubtleBrush` | 7.45 | 8.25 | `Theme/ButtonStyles.axaml:133` |  |
| actionDanger | rest border | `NfcDangerBorderBrush` | `NfcAppBackgroundBrush` | 4.38 | 5.01 | `Theme/ButtonStyles.axaml:146` |  |
| actionDanger | rest border | `NfcDangerBorderBrush` | `NfcSurfaceBrush` | 4.80 | 4.75 | `Theme/ButtonStyles.axaml:146` |  |
| actionDanger | rest border | `NfcDangerBorderBrush` | `NfcSurfaceSubtleBrush` | 4.59 | 4.21 | `Theme/ButtonStyles.axaml:146` |  |
| actionDanger | hover border | `NfcDangerBorderStrongBrush` | `NfcAppBackgroundBrush` | 6.56 | 7.92 | `Theme/ButtonStyles.axaml:152` |  |
| actionDanger | hover border | `NfcDangerBorderStrongBrush` | `NfcSurfaceBrush` | 7.18 | 7.50 | `Theme/ButtonStyles.axaml:152` |  |
| actionDanger | hover border | `NfcDangerBorderStrongBrush` | `NfcSurfaceSubtleBrush` | 6.87 | 6.65 | `Theme/ButtonStyles.axaml:152` |  |
| actionDanger | pressed border | `NfcCriticalBorderBrush` | `NfcAppBackgroundBrush` | 6.56 | 7.92 | `Theme/ButtonStyles.axaml:158` |  |
| actionDanger | pressed border | `NfcCriticalBorderBrush` | `NfcSurfaceBrush` | 7.18 | 7.50 | `Theme/ButtonStyles.axaml:158` |  |
| actionDanger | pressed border | `NfcCriticalBorderBrush` | `NfcSurfaceSubtleBrush` | 6.87 | 6.65 | `Theme/ButtonStyles.axaml:158` |  |
| ToggleButton action* | checked border | `NfcAccentBorderBrush` | `NfcAppBackgroundBrush` | 5.37 | 7.35 | `Theme/ButtonStyles.axaml:193` |  |
| ToggleButton action* | checked border | `NfcAccentBorderBrush` | `NfcSurfaceBrush` | 5.88 | 6.97 | `Theme/ButtonStyles.axaml:193` |  |
| ToggleButton action* | checked border | `NfcAccentBorderBrush` | `NfcSurfaceSubtleBrush` | 5.62 | 6.18 | `Theme/ButtonStyles.axaml:193` |  |
| ToggleButton action* | checked + hover / pressed border | `NfcAccentBorderStrongBrush` | `NfcAppBackgroundBrush` | 7.11 | 9.82 | `Theme/ButtonStyles.axaml:199` |  |
| ToggleButton action* | checked + hover / pressed border | `NfcAccentBorderStrongBrush` | `NfcSurfaceBrush` | 7.79 | 9.30 | `Theme/ButtonStyles.axaml:199` |  |
| ToggleButton action* | checked + hover / pressed border | `NfcAccentBorderStrongBrush` | `NfcSurfaceSubtleBrush` | 7.45 | 8.25 | `Theme/ButtonStyles.axaml:199` |  |
| ToggleButton.actionDanger | checked border | `NfcDangerBorderStrongBrush` | `NfcAppBackgroundBrush` | 6.56 | 7.92 | `Theme/ButtonStyles.axaml:229` |  |
| ToggleButton.actionDanger | checked border | `NfcDangerBorderStrongBrush` | `NfcSurfaceBrush` | 7.18 | 7.50 | `Theme/ButtonStyles.axaml:229` |  |
| ToggleButton.actionDanger | checked border | `NfcDangerBorderStrongBrush` | `NfcSurfaceSubtleBrush` | 6.87 | 6.65 | `Theme/ButtonStyles.axaml:229` |  |
| all action* buttons | disabled border | `NfcBorderMutedBrush` | `NfcAppBackgroundBrush` | **1.36** | **1.81** | `Theme/ButtonStyles.axaml:265` | ex |
| all action* buttons | disabled border | `NfcBorderMutedBrush` | `NfcSurfaceBrush` | **1.48** | **1.71** | `Theme/ButtonStyles.axaml:265` | ex |
| all action* buttons | disabled border | `NfcBorderMutedBrush` | `NfcSurfaceSubtleBrush` | **1.42** | **1.52** | `Theme/ButtonStyles.axaml:265` | ex |
| chipStatus (neutral) | static border | `NfcBorderMutedBrush` | `NfcAppBackgroundBrush` | **1.36** | **1.81** | `Theme/ButtonStyles.axaml:345` | adv |
| chipStatus (neutral) | static border | `NfcBorderMutedBrush` | `NfcSurfaceBrush` | **1.48** | **1.71** | `Theme/ButtonStyles.axaml:345` | adv |
| chipStatus (neutral) | static border | `NfcBorderMutedBrush` | `NfcSurfaceSubtleBrush` | **1.42** | **1.52** | `Theme/ButtonStyles.axaml:345` | adv |
| chipStatus.warning | static border | `NfcWarningBorderBrush` | `NfcAppBackgroundBrush` | 3.52 | 6.08 | `Theme/ButtonStyles.axaml:358` | adv |
| chipStatus.warning | static border | `NfcWarningBorderBrush` | `NfcSurfaceBrush` | 3.85 | 5.76 | `Theme/ButtonStyles.axaml:358` | adv |
| chipStatus.warning | static border | `NfcWarningBorderBrush` | `NfcSurfaceSubtleBrush` | 3.68 | 5.11 | `Theme/ButtonStyles.axaml:358` | adv |
| chipStatus.danger | static border | `NfcDangerBorderBrush` | `NfcAppBackgroundBrush` | 4.38 | 5.01 | `Theme/ButtonStyles.axaml:371` | adv |
| chipStatus.danger | static border | `NfcDangerBorderBrush` | `NfcSurfaceBrush` | 4.80 | 4.75 | `Theme/ButtonStyles.axaml:371` | adv |
| chipStatus.danger | static border | `NfcDangerBorderBrush` | `NfcSurfaceSubtleBrush` | 4.59 | 4.21 | `Theme/ButtonStyles.axaml:371` | adv |
| chipStatus.success | static border | `NfcSuccessBorderBrush` | `NfcAppBackgroundBrush` | 3.56 | 5.70 | `Theme/ButtonStyles.axaml:384` | adv |
| chipStatus.success | static border | `NfcSuccessBorderBrush` | `NfcSurfaceBrush` | 3.90 | 5.40 | `Theme/ButtonStyles.axaml:384` | adv |
| chipStatus.success | static border | `NfcSuccessBorderBrush` | `NfcSurfaceSubtleBrush` | 3.73 | 4.79 | `Theme/ButtonStyles.axaml:384` | adv |

#### Choice

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| indicator border | rest | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/ChoiceStyles.axaml:10` |  |
| indicator border | rest | `NfcBorderBrush` | `Nvt.Interaction.RestBrush over NfcAppBackgroundBrush` | 3.67 | 4.71 | `Theme/ChoiceStyles.axaml:10` |  |
| indicator border | rest | `NfcBorderBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/ChoiceStyles.axaml:10` |  |
| indicator border | rest | `NfcBorderBrush` | `Nvt.Interaction.RestBrush over NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/ChoiceStyles.axaml:10` |  |
| indicator border | hover, Soft | `NfcTextSecondaryBrush` | `NfcSelectionSurfaceBrush` | 5.95 | 8.24 | `Theme/ChoiceStyles.axaml:82` |  |
| indicator border | hover, None | `NfcTextSecondaryBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 4.69 | 7.35 | `Theme/ChoiceStyles.axaml:82` |  |
| indicator border | hover, None | `NfcTextSecondaryBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 5.11 | 6.81 | `Theme/ChoiceStyles.axaml:82` |  |
| indicator border | hover, None | `NfcTextSecondaryBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 4.89 | 6.01 | `Theme/ChoiceStyles.axaml:82` |  |
| indicator border | pressed, Soft | `NfcTextStrongBrush` | `Nvt.Interaction.PressedFillBrush` | 11.08 | 8.52 | `Theme/ChoiceStyles.axaml:86` |  |
| indicator border | pressed, None | `NfcTextStrongBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | 10.19 | 9.22 | `Theme/ChoiceStyles.axaml:86` |  |
| indicator border | pressed, None | `NfcTextStrongBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 11.08 | 8.52 | `Theme/ChoiceStyles.axaml:86` |  |
| indicator border | pressed, None | `NfcTextStrongBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 10.63 | 7.54 | `Theme/ChoiceStyles.axaml:86` |  |
| indicator border | checked | `NfcAccentBorderBrush` | `Nvt.Controls.SelectedBrush` | 4.45 | 4.83 | `Theme/ChoiceStyles.axaml:90` |  |
| indicator border | checked | `NfcAccentBorderBrush` | `Nvt.Controls.SelectedRestBrush over NfcAppBackgroundBrush` | 5.37 | 7.35 | `Theme/ChoiceStyles.axaml:90` |  |
| indicator border | checked | `NfcAccentBorderBrush` | `Nvt.Controls.SelectedRestBrush over NfcSurfaceBrush` | 5.88 | 6.97 | `Theme/ChoiceStyles.axaml:90` |  |
| indicator border | checked | `NfcAccentBorderBrush` | `Nvt.Controls.SelectedRestBrush over NfcSurfaceSubtleBrush` | 5.62 | 6.18 | `Theme/ChoiceStyles.axaml:90` |  |
| indicator border | checked + hover / pressed | `NfcAccentBorderStrongBrush` | `Nvt.Controls.SelectedPointerOverBrush` | 6.56 | 6.68 | `Theme/ChoiceStyles.axaml:94` |  |
| indicator border | checked + hover / pressed | `NfcAccentBorderStrongBrush` | `Nvt.Controls.SelectedPressedBrush` | 6.26 | 5.88 | `Theme/ChoiceStyles.axaml:94` |  |
| check / dash / dot glyph | checked | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SelectedBrush` | 5.88 | 7.79 | `Theme/ChoiceStyles.axaml:89` |  |
| check / dash / dot glyph | checked + hover | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SelectedPointerOverBrush` | 7.79 | 9.73 | `Theme/ChoiceStyles.axaml:93` |  |
| check / dash / dot glyph | checked + pressed | `Nvt.Toggle.SelectedLabelBrush` | `Nvt.Toggle.SelectedPressedBrush` | 9.73 | 12.08 | `Theme/ChoiceStyles.axaml:97` |  |
| indicator border | disabled | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | 4.36 | 4.60 | `Theme/ChoiceStyles.axaml:111` | ex |
| indicator border | disabled | `NfcTextDisabledBrush` | `NfcAppBackgroundBrush` | 4.16 | 5.47 | `Theme/ChoiceStyles.axaml:111` | ex |
| indicator border | disabled | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ChoiceStyles.axaml:111` | ex |
| check glyph | disabled + checked | `Nvt.Toggle.SelectedLabelBrush` | `NfcTextDisabledBrush` | 4.56 | 3.42 | `Theme/ChoiceStyles.axaml:115` | ex |

#### Toggle

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| toggleSegment checked fill vs group surface | checked | `Nvt.Toggle.SelectedBrush` | `NfcSelectionSurfaceBrush` | 4.62 | **1.57** | `Theme/ToggleStyles.axaml:98` |  |
| toggleTab / toggleIcon checked fill vs page | checked | `Nvt.Toggle.SelectedBrush` | `NfcAppBackgroundBrush` | 5.37 | **2.40** | `Theme/ToggleStyles.axaml:98` | adv |
| toggleTab / toggleIcon checked fill vs page | checked | `Nvt.Toggle.SelectedBrush` | `NfcSurfaceBrush` | 5.88 | **2.28** | `Theme/ToggleStyles.axaml:98` | adv |
| toggleTab / toggleIcon checked fill vs page | checked | `Nvt.Toggle.SelectedBrush` | `NfcSurfaceSubtleBrush` | 5.62 | **2.02** | `Theme/ToggleStyles.axaml:98` | adv |
| toggleSegment/Tab/Icon.danger checked fill | checked | `Nvt.Toggle.DangerFillBrush` | `NfcAppBackgroundBrush` | 6.56 | **2.61** | `Theme/ToggleStyles.axaml:180` | adv |
| toggleSegment/Tab/Icon.danger checked fill | checked | `Nvt.Toggle.DangerFillBrush` | `NfcSurfaceBrush` | 7.18 | **2.47** | `Theme/ToggleStyles.axaml:180` | adv |
| toggleSegment/Tab/Icon.danger checked fill | checked | `Nvt.Toggle.DangerFillBrush` | `NfcSurfaceSubtleBrush` | 6.87 | **2.19** | `Theme/ToggleStyles.axaml:180` | adv |
| toggleSegment/Tab/Icon.danger checked fill | checked | `Nvt.Toggle.DangerFillBrush` | `NfcSelectionSurfaceBrush` | 5.64 | **1.70** | `Theme/ToggleStyles.axaml:180` | adv |
| toggleSwitch track | rest / pressed / disabled-off | `NfcBorderBrush` | `NfcAppBackgroundBrush` | 3.67 | 4.71 | `Theme/ToggleStyles.axaml:36` |  |
| toggleSwitch track | rest / pressed / disabled-off | `NfcBorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/ToggleStyles.axaml:36` |  |
| toggleSwitch track | rest / pressed / disabled-off | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/ToggleStyles.axaml:36` |  |
| toggleSwitch track | hover | `NfcTextDisabledBrush` | `NfcAppBackgroundBrush` | 4.16 | 5.47 | `Theme/ToggleStyles.axaml:153` |  |
| toggleSwitch track | hover | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ToggleStyles.axaml:153` |  |
| toggleSwitch track | hover | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | 4.36 | 4.60 | `Theme/ToggleStyles.axaml:153` |  |
| toggleSwitch track | checked | `Nvt.Toggle.SwitchOnBrush` | `NfcAppBackgroundBrush` | 4.72 | 3.62 | `Theme/ToggleStyles.axaml:159` |  |
| toggleSwitch track | checked | `Nvt.Toggle.SwitchOnBrush` | `NfcSurfaceBrush` | 5.17 | 3.43 | `Theme/ToggleStyles.axaml:159` |  |
| toggleSwitch track | checked | `Nvt.Toggle.SwitchOnBrush` | `NfcSurfaceSubtleBrush` | 4.94 | 3.04 | `Theme/ToggleStyles.axaml:159` |  |
| toggleSwitch track | checked + hover | `Nvt.Toggle.SwitchPointerOverBrush` | `NfcAppBackgroundBrush` | 6.12 | **2.79** | `Theme/ToggleStyles.axaml:162` |  |
| toggleSwitch track | checked + hover | `Nvt.Toggle.SwitchPointerOverBrush` | `NfcSurfaceBrush` | 6.70 | **2.65** | `Theme/ToggleStyles.axaml:162` |  |
| toggleSwitch track | checked + hover | `Nvt.Toggle.SwitchPointerOverBrush` | `NfcSurfaceSubtleBrush` | 6.41 | **2.35** | `Theme/ToggleStyles.axaml:162` |  |
| toggleSwitch track | checked + pressed | `Nvt.Toggle.SwitchPressedBrush` | `NfcAppBackgroundBrush` | 7.96 | **2.15** | `Theme/ToggleStyles.axaml:165` |  |
| toggleSwitch track | checked + pressed | `Nvt.Toggle.SwitchPressedBrush` | `NfcSurfaceBrush` | 8.72 | **2.03** | `Theme/ToggleStyles.axaml:165` |  |
| toggleSwitch track | checked + pressed | `Nvt.Toggle.SwitchPressedBrush` | `NfcSurfaceSubtleBrush` | 8.34 | **1.80** | `Theme/ToggleStyles.axaml:165` |  |
| toggleSwitch.danger track | checked | `Nvt.Toggle.DangerFillBrush` | `NfcAppBackgroundBrush` | 6.56 | **2.61** | `Theme/ToggleStyles.axaml:180` |  |
| toggleSwitch.danger track | checked | `Nvt.Toggle.DangerFillBrush` | `NfcSurfaceBrush` | 7.18 | **2.47** | `Theme/ToggleStyles.axaml:180` |  |
| toggleSwitch.danger track | checked | `Nvt.Toggle.DangerFillBrush` | `NfcSurfaceSubtleBrush` | 6.87 | **2.19** | `Theme/ToggleStyles.axaml:180` |  |
| toggleSwitch.danger track | checked + hover / pressed | `Nvt.Toggle.DangerFillPointerOverBrush` | `NfcAppBackgroundBrush` | 8.69 | **1.97** | `Theme/ToggleStyles.axaml:184` |  |
| toggleSwitch.danger track | checked + hover / pressed | `Nvt.Toggle.DangerFillPointerOverBrush` | `NfcSurfaceBrush` | 9.52 | **1.86** | `Theme/ToggleStyles.axaml:184` |  |
| toggleSwitch.danger track | checked + hover / pressed | `Nvt.Toggle.DangerFillPointerOverBrush` | `NfcSurfaceSubtleBrush` | 9.10 | **1.65** | `Theme/ToggleStyles.axaml:184` |  |
| toggleSwitch knob | off rest / pressed | `Nvt.Toggle.KnobBrush` | `NfcBorderBrush` | 4.02 | 3.98 | `Theme/ToggleStyles.axaml:138` |  |
| toggleSwitch knob | off hover | `Nvt.Toggle.KnobBrush` | `NfcTextDisabledBrush` | 4.56 | 3.42 | `Theme/ToggleStyles.axaml:138` |  |
| toggleSwitch knob | on (rest / hover / pressed) | `Nvt.Toggle.KnobBrush` | `Nvt.Toggle.SwitchOnBrush` | 5.17 | 5.17 | `Theme/ToggleStyles.axaml:138` |  |
| toggleSwitch knob | on (rest / hover / pressed) | `Nvt.Toggle.KnobBrush` | `Nvt.Toggle.SwitchPointerOverBrush` | 6.70 | 6.70 | `Theme/ToggleStyles.axaml:138` |  |
| toggleSwitch knob | on (rest / hover / pressed) | `Nvt.Toggle.KnobBrush` | `Nvt.Toggle.SwitchPressedBrush` | 8.72 | 8.72 | `Theme/ToggleStyles.axaml:138` |  |
| toggleSwitch knob | on (rest / hover / pressed) | `Nvt.Toggle.KnobBrush` | `Nvt.Toggle.DangerFillBrush` | 7.18 | 7.18 | `Theme/ToggleStyles.axaml:138` |  |
| toggleSwitch knob | on (rest / hover / pressed) | `Nvt.Toggle.KnobBrush` | `Nvt.Toggle.DangerFillPointerOverBrush` | 9.52 | 9.52 | `Theme/ToggleStyles.axaml:138` |  |
| toggleSwitch track + knob | disabled + checked | `NfcTextDisabledBrush` | `NfcAppBackgroundBrush` | 4.16 | 5.47 | `Theme/ToggleStyles.axaml:171` | ex |
| toggleSwitch track + knob | disabled + checked | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ToggleStyles.axaml:171` | ex |
| toggleSwitch track + knob | disabled + checked | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | 4.36 | 4.60 | `Theme/ToggleStyles.axaml:171` | ex |
| toggleSoft checked outline | checked (1px) | `NfcAccentBorderBrush` | `NfcAppBackgroundBrush` | 5.37 | 7.35 | `Theme/ToggleStyles.axaml:226` |  |
| toggleSoft checked outline | checked (1px) | `NfcAccentBorderBrush` | `NfcSurfaceBrush` | 5.88 | 6.97 | `Theme/ToggleStyles.axaml:226` |  |
| toggleSoft checked outline | checked (1px) | `NfcAccentBorderBrush` | `NfcSurfaceSubtleBrush` | 5.62 | 6.18 | `Theme/ToggleStyles.axaml:226` |  |
| toggleSoft checked outline | checked + hover (2px) | `NfcAccentStrongBrush` | `NfcAppBackgroundBrush` | 7.11 | 9.82 | `Theme/ToggleStyles.axaml:265` |  |
| toggleSoft checked outline | checked + hover (2px) | `NfcAccentStrongBrush` | `NfcSurfaceBrush` | 7.79 | 9.30 | `Theme/ToggleStyles.axaml:265` |  |
| toggleSoft checked outline | checked + hover (2px) | `NfcAccentStrongBrush` | `NfcSurfaceSubtleBrush` | 7.45 | 8.25 | `Theme/ToggleStyles.axaml:265` |  |

#### Form

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| TextBox selection highlight vs field fill | rest | `Nvt.Form.SelectionBrush` | `NfcSurfaceSubtleBrush` | **1.26** | **1.28** | `Theme/FormStyles.axaml:78` | adv |
| TextBox selection highlight vs field fill | hover (mouse drag-select happens here) | `Nvt.Form.SelectionBrush` | `NfcSelectionSurfaceBrush` | **1.04** | **1.01** | `Theme/FormStyles.axaml:78` | adv |
| TextBox selection highlight vs field fill | pressed | `Nvt.Form.SelectionBrush` | `Nvt.Form.PressedFillBrush` | **1.04** | **1.08** | `Theme/FormStyles.axaml:78` | adv |
| Form border | rest | `Nvt.Form.BorderBrush` | `NfcAppBackgroundBrush` | 3.67 | 4.71 | `Theme/FormStyles.axaml:40` |  |
| Form border | rest | `Nvt.Form.BorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/FormStyles.axaml:40` |  |
| Form border | rest | `Nvt.Form.BorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/FormStyles.axaml:40` |  |
| Form border | hover | `Nvt.Form.HoverBorderBrush` | `NfcAppBackgroundBrush` | 6.92 | 12.61 | `Theme/FormStyles.axaml:225` |  |
| Form border | hover | `Nvt.Form.HoverBorderBrush` | `NfcSurfaceBrush` | 7.58 | 11.95 | `Theme/FormStyles.axaml:225` |  |
| Form border | hover | `Nvt.Form.HoverBorderBrush` | `NfcSurfaceSubtleBrush` | 7.24 | 10.60 | `Theme/FormStyles.axaml:225` |  |
| Form border | pressed | `Nvt.Form.PressedBorderBrush` | `NfcAppBackgroundBrush` | 16.30 | 17.89 | `Theme/FormStyles.axaml:229` |  |
| Form border | pressed | `Nvt.Form.PressedBorderBrush` | `NfcSurfaceBrush` | 17.85 | 16.96 | `Theme/FormStyles.axaml:229` |  |
| Form border | pressed | `Nvt.Form.PressedBorderBrush` | `NfcSurfaceSubtleBrush` | 17.06 | 15.04 | `Theme/FormStyles.axaml:229` |  |
| Form border | error | `Nvt.Form.ErrorBorderBrush` | `NfcAppBackgroundBrush` | 4.38 | 5.01 | `Theme/FormStyles.axaml:245` |  |
| Form border | error | `Nvt.Form.ErrorBorderBrush` | `NfcSurfaceBrush` | 4.80 | 4.75 | `Theme/FormStyles.axaml:245` |  |
| Form border | error | `Nvt.Form.ErrorBorderBrush` | `NfcSurfaceSubtleBrush` | 4.59 | 4.21 | `Theme/FormStyles.axaml:245` |  |
| Form border | error + pressed | `Nvt.Form.ErrorBorderBrush` | `Nvt.Form.PressedFillBrush` | 3.49 | 3.04 | `Theme/FormStyles.axaml:245` |  |
| Form border | disabled | `Nvt.Form.DisabledTextBrush` | `NfcAppBackgroundBrush` | 4.16 | 5.47 | `Theme/FormStyles.axaml:241` | ex |
| Form border | disabled | `Nvt.Form.DisabledTextBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/FormStyles.axaml:241` | ex |
| Form border | disabled | `Nvt.Form.DisabledTextBrush` | `NfcSurfaceSubtleBrush` | 4.36 | 4.60 | `Theme/FormStyles.axaml:241` | ex |

#### List / Menu

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| List selection indicator bar | selected | `Nvt.List.SelectedLabelBrush` | `Nvt.List.SelectedBrush` | 7.36 | 8.58 | `Theme/ListStyles.axaml:43` |  |
| List selection indicator bar | selected | `Nvt.List.SelectedLabelBrush` | `Nvt.List.SelectedPointerOverBrush` | 8.19 | 8.90 | `Theme/ListStyles.axaml:43` |  |
| List selection indicator bar | selected | `Nvt.List.SelectedLabelBrush` | `Nvt.List.SelectedPressedBrush` | 7.81 | 7.83 | `Theme/ListStyles.axaml:43` |  |
| MenuItem check mark / chevron | rest | `NfcTextBrush` | `NfcSurfaceBrush` | 14.63 | 14.39 | `Theme/MenuStyles.axaml:97` |  |
| MenuItem check mark / chevron | hover | `NfcTextBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 9.86 | 8.20 | `Theme/MenuStyles.axaml:97` |  |
| Menu popup border | static | `NfcBorderBrush` | `NfcAppBackgroundBrush` | 3.67 | 4.71 | `Theme/MenuStyles.axaml:39` | adv |
| Menu popup border | static | `NfcBorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/MenuStyles.axaml:39` | adv |
| Menu popup border | static | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/MenuStyles.axaml:39` | adv |

#### Tab

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| Tab indicator bar | selected (+ hover / pressed) | `Nvt.Tab.IndicatorBrush` | `Nvt.Tab.SelectedFillBrush` | 7.36 | 8.58 | `Theme/TabStyles.axaml:64` |  |
| Tab indicator bar | selected (+ hover / pressed) | `Nvt.Tab.IndicatorBrush` | `Nvt.Tab.SelectedHoverFillBrush` | 8.19 | 8.90 | `Theme/TabStyles.axaml:64` |  |
| Tab indicator bar | selected (+ hover / pressed) | `Nvt.Tab.IndicatorBrush` | `Nvt.Tab.SelectedPressedFillBrush` | 7.81 | 7.83 | `Theme/TabStyles.axaml:64` |  |

#### Expander

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| Expander container border | static | `NfcBorderMutedBrush` | `NfcAppBackgroundBrush` | **1.36** | **1.81** | `Theme/ExpanderStyles.axaml:13` | adv |
| Expander container border | static | `NfcBorderMutedBrush` | `NfcSurfaceBrush` | **1.48** | **1.71** | `Theme/ExpanderStyles.axaml:13` | adv |
| Expander container border | static | `NfcBorderMutedBrush` | `NfcSurfaceSubtleBrush` | **1.42** | **1.52** | `Theme/ExpanderStyles.axaml:13` | adv |

#### Progress / Divider / Scroll

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| ProgressBar indicator vs track | determinate | `Nvt.Progress.IndicatorBrush` | `Nvt.Progress.TrackBrush` | 3.96 | 4.07 | `Theme/ProgressStyles.axaml:17` |  |
| ProgressBar indicator vs page | determinate | `Nvt.Progress.IndicatorBrush` | `NfcAppBackgroundBrush` | 5.37 | 7.35 | `Theme/ProgressStyles.axaml:17` |  |
| ProgressBar indicator vs page | determinate | `Nvt.Progress.IndicatorBrush` | `NfcSurfaceBrush` | 5.88 | 6.97 | `Theme/ProgressStyles.axaml:17` |  |
| ProgressBar indicator vs page | determinate | `Nvt.Progress.IndicatorBrush` | `NfcSurfaceSubtleBrush` | 5.62 | 6.18 | `Theme/ProgressStyles.axaml:17` |  |
| ProgressBar track vs page | empty track | `Nvt.Progress.TrackBrush` | `NfcAppBackgroundBrush` | **1.36** | **1.81** | `Theme/ProgressStyles.axaml:16` | adv |
| ProgressBar track vs page | empty track | `Nvt.Progress.TrackBrush` | `NfcSurfaceBrush` | **1.48** | **1.71** | `Theme/ProgressStyles.axaml:16` | adv |
| ProgressBar track vs page | empty track | `Nvt.Progress.TrackBrush` | `NfcSurfaceSubtleBrush` | **1.42** | **1.52** | `Theme/ProgressStyles.axaml:16` | adv |
| ProgressBar indicator vs track | disabled | `Nvt.Progress.DisabledIndicatorBrush` | `Nvt.Progress.TrackBrush` | 3.07 | 3.03 | `Theme/ProgressStyles.axaml:50` | ex |
| GridSplitter 1px line | rest | `NfcBorderSoftBrush` | `NfcAppBackgroundBrush` | **2.34** | **2.47** | `Theme/DividerStyles.axaml:42` | adv |
| GridSplitter 1px line | rest | `NfcBorderSoftBrush` | `NfcSurfaceBrush` | **2.56** | **2.34** | `Theme/DividerStyles.axaml:42` | adv |
| GridSplitter 1px line | rest | `NfcBorderSoftBrush` | `NfcSurfaceSubtleBrush` | **2.45** | **2.08** | `Theme/DividerStyles.axaml:42` | adv |
| GridSplitter grip | rest | `NfcBorderBrush` | `NfcAppBackgroundBrush` | 3.67 | 4.71 | `Theme/DividerStyles.axaml:58` |  |
| GridSplitter grip | rest | `NfcBorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/DividerStyles.axaml:58` |  |
| GridSplitter grip | rest | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/DividerStyles.axaml:58` |  |
| GridSplitter line + grip | hover | `NfcAccentBrush` | `NfcAppBackgroundBrush` | 5.37 | 7.35 | `Theme/DividerStyles.axaml:87` |  |
| GridSplitter line + grip | hover | `NfcAccentBrush` | `NfcSurfaceBrush` | 5.88 | 6.97 | `Theme/DividerStyles.axaml:87` |  |
| GridSplitter line + grip | hover | `NfcAccentBrush` | `NfcSurfaceSubtleBrush` | 5.62 | 6.18 | `Theme/DividerStyles.axaml:87` |  |
| GridSplitter line + grip | pressed | `NfcAccentStrongBrush` | `NfcAppBackgroundBrush` | 7.11 | 9.82 | `Theme/DividerStyles.axaml:87` |  |
| GridSplitter line + grip | pressed | `NfcAccentStrongBrush` | `NfcSurfaceBrush` | 7.79 | 9.30 | `Theme/DividerStyles.axaml:87` |  |
| GridSplitter line + grip | pressed | `NfcAccentStrongBrush` | `NfcSurfaceSubtleBrush` | 7.45 | 8.25 | `Theme/DividerStyles.axaml:87` |  |
| GridSplitter | disabled | `NfcBorderMutedBrush` | `NfcAppBackgroundBrush` | **1.36** | **1.81** | `Theme/DividerStyles.axaml:106` | ex |
| GridSplitter | disabled | `NfcBorderMutedBrush` | `NfcSurfaceBrush` | **1.48** | **1.71** | `Theme/DividerStyles.axaml:106` | ex |
| GridSplitter | disabled | `NfcBorderMutedBrush` | `NfcSurfaceSubtleBrush` | **1.42** | **1.52** | `Theme/DividerStyles.axaml:106` | ex |
| Divider / Separator | static | `NfcDividerBrush` | `NfcAppBackgroundBrush` | **1.13** | **1.49** | `Theme/DividerStyles.axaml:12` | ex |
| Divider / Separator | static | `NfcDividerBrush` | `NfcSurfaceBrush` | **1.23** | **1.41** | `Theme/DividerStyles.axaml:12` | ex |
| Divider / Separator | static | `NfcDividerBrush` | `NfcSurfaceSubtleBrush` | **1.18** | **1.25** | `Theme/DividerStyles.axaml:12` | ex |
| Divider.strong / Separator.strong | static | `NfcBorderBrush` | `NfcAppBackgroundBrush` | 3.67 | 4.71 | `Theme/DividerStyles.axaml:29` | adv |
| Divider.strong / Separator.strong | static | `NfcBorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/DividerStyles.axaml:29` | adv |
| Divider.strong / Separator.strong | static | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Theme/DividerStyles.axaml:29` | adv |
| ScrollBar thumb | rest | `NfcTextDisabledBrush` | `NfcAppBackgroundBrush` | 4.16 | 5.47 | `Theme/ScrollStyles.axaml:22` |  |
| ScrollBar thumb | rest | `NfcTextDisabledBrush` | `NfcSurfaceBrush` | 4.56 | 5.18 | `Theme/ScrollStyles.axaml:22` |  |
| ScrollBar thumb | rest | `NfcTextDisabledBrush` | `NfcSurfaceSubtleBrush` | 4.36 | 4.60 | `Theme/ScrollStyles.axaml:22` |  |
| ScrollBar thumb | hover | `NfcTextMutedBrush` | `NfcAppBackgroundBrush` | 5.76 | 8.34 | `Theme/ScrollStyles.axaml:36` |  |
| ScrollBar thumb | hover | `NfcTextMutedBrush` | `NfcSurfaceBrush` | 6.31 | 7.90 | `Theme/ScrollStyles.axaml:36` |  |
| ScrollBar thumb | hover | `NfcTextMutedBrush` | `NfcSurfaceSubtleBrush` | 6.03 | 7.01 | `Theme/ScrollStyles.axaml:36` |  |

#### Other

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| NumberScrubber border | rest | `NfcBorderMutedBrush` | `NfcAppBackgroundBrush` | **1.36** | **1.81** | `Inputs/InputsStyles.axaml:10` |  |
| NumberScrubber border | rest | `NfcBorderMutedBrush` | `NfcSurfaceBrush` | **1.48** | **1.71** | `Inputs/InputsStyles.axaml:10` |  |
| NumberScrubber border | rest | `NfcBorderMutedBrush` | `NfcSurfaceSubtleBrush` | **1.42** | **1.52** | `Inputs/InputsStyles.axaml:10` |  |
| NumberScrubber border | focused | `NfcAccentBrush` | `NfcSelectionSurfaceBrush` | 4.62 | 4.80 | `Inputs/InputsStyles.axaml:17` |  |
| NumberScrubber border | focused | `NfcAccentBrush` | `NfcAppBackgroundBrush` | 5.37 | 7.35 | `Inputs/InputsStyles.axaml:17` |  |
| NumberScrubber border | focused | `NfcAccentBrush` | `NfcSurfaceBrush` | 5.88 | 6.97 | `Inputs/InputsStyles.axaml:17` |  |
| NumberScrubber border | focused | `NfcAccentBrush` | `NfcSurfaceSubtleBrush` | 5.62 | 6.18 | `Inputs/InputsStyles.axaml:17` |  |
| NumberScrubber spin handle | rest | `NfcBorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Inputs/InputsStyles.axaml:34` |  |
| ToolTip border | static | `Nvt.ToolTip.BorderBrush` | `NfcAppBackgroundBrush` | **2.34** | **2.47** | `Focus/ToolTipStyles.axaml:9` | adv |
| ToolTip border | static | `Nvt.ToolTip.BorderBrush` | `NfcSurfaceBrush` | **2.56** | **2.34** | `Focus/ToolTipStyles.axaml:9` | adv |
| ToolTip border | static | `Nvt.ToolTip.BorderBrush` | `NfcSurfaceSubtleBrush` | **2.45** | **2.08** | `Focus/ToolTipStyles.axaml:9` | adv |
| WarningDialog icon | static | `NfcWarningAccentStrongBrush` | `NfcSurfaceBrush` | 6.37 | 11.89 | `Dialogs/DialogsStyles.axaml:22` |  |
| PanelBlock / WorkspaceShell border | static | `NfcBorderBrush` | `NfcAppBackgroundBrush` | 3.67 | 4.71 | `Panels/PanelsStyles.axaml:42` | adv |
| PanelBlock / WorkspaceShell border | static | `NfcBorderBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Panels/PanelsStyles.axaml:42` | adv |
| PanelBlock / WorkspaceShell border | static | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `Panels/PanelsStyles.axaml:42` | adv |

#### Focus rings

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| Button / Toggle / Splitter / Expander / Console ring (Nvt.Focus.RingBrush) | focus-visible, vs page | `Nvt.Focus.RingBrush` | `NfcAppBackgroundBrush` | 4.51 | 7.13 | `Theme/ButtonStyles.axaml:316` |  |
| Button / Toggle / Splitter / Expander / Console ring (Nvt.Focus.RingBrush) | focus-visible, vs page | `Nvt.Focus.RingBrush` | `NfcSurfaceBrush` | 4.94 | 6.76 | `Theme/ButtonStyles.axaml:316` |  |
| Button / Toggle / Splitter / Expander / Console ring (Nvt.Focus.RingBrush) | focus-visible, vs page | `Nvt.Focus.RingBrush` | `NfcSurfaceSubtleBrush` | 4.72 | 5.99 | `Theme/ButtonStyles.axaml:316` |  |
| Button / Toggle / Splitter / Expander / Console ring (Nvt.Focus.RingBrush) | focus-visible, vs page | `Nvt.Focus.RingBrush` | `NfcSelectionSurfaceBrush` | 3.88 | 4.66 | `Theme/ButtonStyles.axaml:316` |  |
| Nvt.Focus.DangerRingBrush (actionDanger) | focus-visible, vs page | `Nvt.Focus.DangerRingBrush` | `NfcAppBackgroundBrush` | 5.13 | 6.75 | `Theme/ButtonStyles.axaml:326` |  |
| Nvt.Focus.DangerRingBrush (actionDanger) | focus-visible, vs page | `Nvt.Focus.DangerRingBrush` | `NfcSurfaceBrush` | 5.62 | 6.39 | `Theme/ButtonStyles.axaml:326` |  |
| Nvt.Focus.DangerRingBrush (actionDanger) | focus-visible, vs page | `Nvt.Focus.DangerRingBrush` | `NfcSurfaceSubtleBrush` | 5.37 | 5.67 | `Theme/ButtonStyles.axaml:326` |  |
| Nvt.Focus.DangerRingBrush (actionDanger) | focus-visible, vs page | `Nvt.Focus.DangerRingBrush` | `NfcDangerSurfaceBrush` | 5.12 | 6.08 | `Theme/ButtonStyles.axaml:326` |  |
| Nvt.Controls.FocusBrush (Choice, Tab, toggleSoft, Form via Nvt.Form.FocusBrush) | focus-visible, vs page | `Nvt.Controls.FocusBrush` | `NfcAppBackgroundBrush` | 6.07 | 10.34 | `Theme/ChoiceStyles.axaml:63` |  |
| Nvt.Controls.FocusBrush (Choice, Tab, toggleSoft, Form via Nvt.Form.FocusBrush) | focus-visible, vs page | `Nvt.Controls.FocusBrush` | `NfcSurfaceBrush` | 6.65 | 9.80 | `Theme/ChoiceStyles.axaml:63` |  |
| Nvt.Controls.FocusBrush (Choice, Tab, toggleSoft, Form via Nvt.Form.FocusBrush) | focus-visible, vs page | `Nvt.Controls.FocusBrush` | `NfcSurfaceSubtleBrush` | 6.36 | 8.69 | `Theme/ChoiceStyles.axaml:63` |  |
| Nvt.Controls.FocusBrush (Choice, Tab, toggleSoft, Form via Nvt.Form.FocusBrush) | focus-visible, vs page | `Nvt.Controls.FocusBrush` | `NfcSelectionSurfaceBrush` | 5.23 | 6.75 | `Theme/ChoiceStyles.axaml:63` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 4.12 | 6.03 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 4.48 | 5.58 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 4.29 | 4.93 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | 3.80 | 5.33 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 4.13 | 4.92 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | 3.96 | 4.36 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.List.SelectedBrush` | 5.04 | 6.79 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.List.SelectedPointerOverBrush` | 5.60 | 7.04 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Controls.FocusBrush inset ring (List, Menu: InsetFocusRingMargin = +2) | focus-visible inside hover / pressed layer | `Nvt.Controls.FocusBrush` | `Nvt.List.SelectedPressedBrush` | 5.34 | 6.19 | `Theme/ListStyles.axaml:46` |  |
| Nvt.Focus.RingBrush on pressed layer (PR #269 "known limitation") | focus-visible inside pressed layer | `Nvt.Focus.RingBrush` | `Nvt.Interaction.PressedBrush over NfcAppBackgroundBrush` | **2.82** | 3.67 | `Theme/ButtonStyles.axaml:316` | adv |
| Nvt.Focus.RingBrush on pressed layer (PR #269 "known limitation") | focus-visible inside pressed layer | `Nvt.Focus.RingBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceBrush` | 3.06 | 3.40 | `Theme/ButtonStyles.axaml:316` | adv |
| Nvt.Focus.RingBrush on pressed layer (PR #269 "known limitation") | focus-visible inside pressed layer | `Nvt.Focus.RingBrush` | `Nvt.Interaction.PressedBrush over NfcSurfaceSubtleBrush` | **2.94** | 3.00 | `Theme/ButtonStyles.axaml:316` | adv |
| Nvt.Focus.RingBrush on hover layer | focus-visible inside hover layer | `Nvt.Focus.RingBrush` | `Nvt.Interaction.HoverBrush over NfcAppBackgroundBrush` | 3.06 | 4.16 | `Theme/ButtonStyles.axaml:316` | adv |
| Nvt.Focus.RingBrush on hover layer | focus-visible inside hover layer | `Nvt.Focus.RingBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceBrush` | 3.33 | 3.85 | `Theme/ButtonStyles.axaml:316` | adv |
| Nvt.Focus.RingBrush on hover layer | focus-visible inside hover layer | `Nvt.Focus.RingBrush` | `Nvt.Interaction.HoverBrush over NfcSurfaceSubtleBrush` | 3.19 | 3.40 | `Theme/ButtonStyles.axaml:316` | adv |

#### Console

| Control | State | Foreground | Background | Light | Dark | Source | Flag |
|---|---|---|---|---|---|---|---|
| Link underline (idle) | idle | `Nvt.Console.LinkUnderlineBrush` | `NfcSurfaceBrush` | 4.02 | 4.46 | `Theme/ControlTokens.axaml:11` |  |
| Link underline (idle) | idle | `Nvt.Console.LinkUnderlineBrush` | `NfcSelectionSurfaceBrush` | 3.15 | 3.08 | `Theme/ControlTokens.axaml:11` |  |
| Link underline (idle) | idle | `Nvt.Console.LinkUnderlineBrush` | `NfcDangerSurfaceBrush` | 3.66 | 4.25 | `Theme/ControlTokens.axaml:11` |  |
| Row selection fill vs row surface | selected row | `NfcSelectionSurfaceBrush` | `NfcSurfaceBrush` | **1.27** | **1.45** | `LogConsole/ConsoleRowPresenter.cs:275` | adv |
| Row selection fill vs row surface | selected row | `NfcSelectionSurfaceBrush` | `NfcDangerSurfaceBrush` | **1.16** | **1.38** | `LogConsole/ConsoleRowPresenter.cs:275` | adv |
| consoleSearch border | rest | `NfcBorderBrush` | `NfcSurfaceSubtleBrush` | 3.84 | 3.96 | `LogConsole/ConsoleControlStyles.axaml:100` |  |
