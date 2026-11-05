# Core Theme docs sync: analysis notes

Analysis only. Nothing in the repository was changed apart from this file.

- Base: `origin/main` @ `0286a279e20882fed75efde8af4077438dba2a6e` (clone is shallow, 211 commits, so `git log` dates and blame are not reliable).
- Compared: `docs/core/modules/Theme.md` (EN) and `Theme.zh-TW.md` (ZH), `CHANGELOG.md`, `docs/core/post-adoption-tuning{,.zh-TW}.md`, and Theme-related lines in `docs/components.md`, `docs/core/api-inventory.md`, `ROADMAP.md`.
- Against: `src/Nvt.Core.Avalonia/Theme/*.axaml` (tokens and styles), `*.cs` converters, `src/Nvt.Core.Fonts/FontRoles.axaml`, `PublicAPI.Shipped.txt`, and test names in `tests/Nvt.Core.Avalonia.Tests`.
- Line numbers: `ZH` lines equal `EN` lines up to `Theme.md:1117`; after that ZH is one line earlier (EN has one extra line at 1066 that ZH folds into 1065).

## How to read this

Each finding gives the doc side and the code side, with `file:line`. Severity is a judgement:

- **High**: a reader acting on the doc gets a wrong result.
- **Medium**: wrong or misleading, but low blast radius.
- **Low**: wording, completeness or tidiness.

Nothing in the token value tables turned out to be wrong, so there is no "stale value" finding. The coverage section at the end lists what was checked and found clean.

---

## 1. Documented state or claim that the selectors contradict

### 1.1 (Medium) Expander `Left` / `Right` are described as unstyled, but they have rules

| Side | Location | Text or code |
|---|---|---|
| Doc EN | `Theme.md:520` | "The styles support `ExpandDirection` Down and Up. Left and Right are not styled." |
| Doc ZH | `Theme.zh-TW.md:520` | 「樣式支援 `ExpandDirection` 的 Down 與 Up，不處理 Left 與 Right。」 |
| Code | `ExpanderStyles.axaml:127`, `:130` | `Expander:left` / `Expander:right` set `DockPanel.Dock` on the header |
| Code | `ExpanderStyles.axaml:140`, `:144` | `Expander:left` / `Expander:right` set content `Margin` and divider `BorderThickness` |
| Tokens | `DividerTokens.axaml:24-25`, `:29-30` | `ContentLeftMargin`, `ContentRightMargin`, `ContentDividerLeftThickness`, `ContentDividerRightThickness` |
| Doc tokens | `Theme.md:1310-1311`, `:1315-1316` (ZH `1309-1310`, `1314-1315`) | The same four tokens are listed in the geometry table |

The doc therefore contradicts itself. The code styles docking, margin and divider side for Left and Right. The chevron has no Left/Right-specific rotation (`ExpanderStyles.axaml:118-123` only handles `:expanded` and `:up`). Either the sentence should say "Left and Right dock and space correctly but the chevron orientation is not designed or tested", or the rules and four tokens should be removed.

### 1.2 (Low) "Lowest fill separation" excludes the selected hover and pressed fills

| Side | Location | Text or code |
|---|---|---|
| Doc EN | `Theme.md:1737` | "The lowest fill separation from the surface is 1.217:1 in Light and 1.280:1 in Dark. These are practical floors for a hover, pressed or checked fill." |
| Doc ZH | `Theme.zh-TW.md:1736` | Same figures |
| Test | `InteractionContrastTests.cs:21-28` | Checks only `NfcSelectionSurfaceBrush`, `Nvt.Interaction.PressedBrush`, `Nvt.Controls.SelectedBrush` against `NfcSurfaceBrush` / `NfcSurfaceSubtleBrush`, with floors 1.2 (Light) and 1.25 (Dark) |

The figures reproduce for those three fills (Light hover on subtle 1.217; Dark selected on subtle 1.280). The checked-and-hovered and checked-and-pressed fills (`Nvt.Controls.SelectedPointerOverBrush` `#E3ECFC` / `#20334F`, `SelectedPressedBrush` `#DCE7FA` / `#243C5B`) are lighter or closer to the surface:

| Fill on `NfcSurfaceSubtleBrush` | Light | Dark |
|---|---|---|
| `SelectedPointerOverBrush` | 1.136 | 1.235 |
| `SelectedPressedBrush` | 1.191 | 1.403 |

So the sentence is not a floor for "a checked fill" in the sense the doc states, and the Light value falls below the 1.2 floor the test uses. Either narrow the sentence to the three fills or add the two selected-state fills to the test and the doc.

### 1.3 (Low) Text-class "pressed" rows describe a different pressed fill than the one the interaction section defines

| Side | Location | Text |
|---|---|---|
| Doc EN | `Theme.md:1693-1694` | "All text classes / pressed 11.866 / 10.501; Muted class / pressed 5.115 / 5.763" |
| Doc EN | `Theme.md:1724` | "`Nvt.Interaction.PressedBrush` ... Pressed fill of buttons, toggles, list and menu items." |
| Code | `ThemeTokens.axaml:38-39`, `:96-97` | `NfcSecondaryActionPressedBrush` `#E2E8F0` / `#243247`; `Nvt.Interaction.PressedBrush` `#D2DDEA` / `#2C3E58` |

Recomputed: `NfcTextBrush` on `NfcSecondaryActionPressedBrush` is 11.866 / 10.501, which matches the table. The same text on `Nvt.Interaction.PressedBrush` is 10.638 / 8.794 (muted: 4.586 / 4.826). The same applies to the Form/Tab "pressed" rows at `Theme.md:1607-1611` and `:1631-1632`. The table is correct for the input/tab pressed fill (`Theme.md:1730`), but its label "pressed" is ambiguous, and no text-class row covers the more common `Nvt.Interaction.PressedBrush`. Only the 4.586 figure in `Theme.md:1737` reflects it.

---

## 2. English and Chinese docs disagree

### 2.1 (Medium) ScrollStyles adoption sentence: EN says "loads it", ZH says "loads Fluent"

| Side | Location | Text |
|---|---|---|
| EN | `Theme.md:675` | "NFC loads it in `src/NvtFwCombiner.Presentation.Avalonia/App.axaml` at `085f71c`. NFH loads it in `src/FreeformHelper.UI/App.axaml` at `847cc45`." ("it" = `ScrollStyles.axaml`) |
| ZH | `Theme.zh-TW.md:675` | 「NFC 在 `085f71c` 的 …/App.axaml **載入 Fluent**。NFH 在 `847cc45` 的 …/App.axaml **載入 Fluent**。」 |

The two sentences state different facts (which file the tool loads). The next line in both docs (`:676`) says "include the file at the position where the tool's own scroll styles were", which fits the EN reading. The ZH sentence appears to be a mistranslation.

### 2.2 (Medium) `post-adoption-tuning.zh-TW.md` is missing the rest-fill instruction

| Side | Location | Text |
|---|---|---|
| EN | `docs/core/post-adoption-tuning.md:46` | "Use `ThemeRestFills.SetRestFill(resources, ThemeRestFill.None)` to hide Choice row and Expander header rest fills while preserving interaction feedback." |
| ZH | `docs/core/post-adoption-tuning.zh-TW.md:43-47` | No equivalent sentence (the paragraph at 43-44 goes straight to the blank line and the switch-geometry paragraph at 47) |

The ZH file is 83 lines against 84 for EN, and this is the only line with no counterpart.

### 2.3 (Low) Mixed-language labels inside the English doc

`Theme.md:92`, `:120-129` carry Chinese text in an English table (`Tool / 工具`, `Control height / 控制項高度`, `Compact corners / 緊湊圓角`, and the same pattern on every row of that table), and the header at `:120` is `項目 | Token／值`. ZH has identical lines (`Theme.zh-TW.md:92`, `:120-129`). The docs agree with each other, but the English doc reads as partially untranslated. This looks like a leftover from a bilingual draft.

### 2.4 Checked and found aligned

A line-by-line comparison (code spans, hex values and numbers per line, aligned with `difflib`) found no other difference between `Theme.md` and `Theme.zh-TW.md`: same headings, same 59 / 105 / 22 role-migration rows (`:242-430`), same destination column, same token tables, same contrast numbers. The remaining diff lines were only quoting or backtick style.

---

## 3. Docs that name tests or counts that do not exist

### 3.1 (Low) `RenderRestFillSoftReferences` does not exist

| Side | Location | Text or code |
|---|---|---|
| EN | `Theme.md:1863` | "`RenderRestFillSoftReferences` reuses the existing renderers for all 45 redesign and 20 forms, tabs, and text sheets." |
| ZH | `Theme.zh-TW.md:1862` | Same name |
| Code | `tests/Nvt.Core.Avalonia.Tests/Choice/ChoiceRestFillRenderer.cs:14` | Only `RenderRestFillNoneChoices` |
| Code | `tests/Nvt.Core.Avalonia.Tests/Dividers/DividerRestFillRenderer.cs:13` | Only `RenderRestFillNoneExpanders` |

`grep -rn RenderRestFill tests` finds the two `None` methods only. The sibling names on the next line (`:1865`) do exist. The frozen-baseline note at `:1796` says the Soft sheets were compared once, which may explain why the method was removed.

All other test, renderer and environment-variable names in `Theme.md` and `post-adoption-tuning.md` were found in `tests/` (`ButtonThemeTests`, `PaletteContrastTests`, `ThemeContractTests`, `NfcLegacyFontTests`, `ScrollStylesTests`, `UiResourceResolverTests` with 7 cases, `FormsContrastTests`, `PaintedSurfaceContrastPreservesDocumentedLevels` with `Assert.Equal(8232, ...)` at `RestFillTests.cs:487`, all `NVT_*_IMAGES_DIR` variables, all `Render*` methods in the renderer table).

---

## 4. Tokens: defined in `.axaml` but missing or only partly documented

### 4.1 (Low) `Nvt.Interaction.RestBrush` and `Nvt.Interaction.PressedBrush` are absent from the palette tables

| Side | Location |
|---|---|
| Code | `ThemeTokens.axaml:21`, `:39`, `:79`, `:97` (both themes, in the same dictionary as the other `Nfc*` neutral colors) |
| Doc | Not in "Neutral colors" `Theme.md:25-40` or "Semantic colors" `:46-77` |
| Doc | First used at `Theme.md:146` (`checked_pressed`), defined only at `Theme.md:1723-1724` |

A reader looking for "all colors in `ThemeTokens.axaml`" in the "Shared palette" section will miss two of the most-used state brushes. A cross-reference to the "Interaction states" section would be enough.

### 4.2 (Low) Icon geometry tokens are not in the Theme docs

| Code | `ThemeTokens.axaml:138-141` |
|---|---|
| `NfcCloseIconGeometry` | referenced only from `Dialogs.md:45`, `Dialogs.zh-TW.md:45`, `:73` |
| `NfcEditIconGeometry`, `NfcClockIconGeometry` | not referenced in any doc under `docs/` |
| `NfcDoneIconGeometry` | one mention at `Theme.md:1197` (ZH `:1196`), used by `ChoiceStyles.axaml:44` and `MenuStyles.axaml:98` |

None of the four appears in a Theme token table, although they live in the same file as the documented palette and sizes.

### 4.3 (Low) Doc Menu-token table omits `Nvt.Menu.PopupMaxHeight`

`Theme.md:800-808` (ZH `:800-808`) lists seven menu tokens. `Nvt.Menu.PopupMaxHeight` (`ListTokens.axaml:22`, value 368) appears only in the "Interaction states" table at `Theme.md:1728`. The post-adoption geometry table (`:1425-1437`) also does not list it. The value is documented, just in a different section from its siblings.

### 4.4 (Low) Palette keys that are documented as retained but nothing consumes

`Theme.md:79` says "Compatible success-emphasis, warning-muted, caution and critical names remain." A reference scan of `src/**/*.axaml` and `src/**/*.cs` shows these keys have no consumer inside Core:
`NfcAccentBorderLightBrush`, `NfcAppBackgroundBrush`, `NfcCautionBorderBrush`, `NfcCautionBorderSoftBrush`, `NfcCautionSurfaceBrush`, `NfcCautionTextBrush`, `NfcInfoSurfaceStrongBrush`, `NfcSuccessAccentBrush`, `NfcSuccessEmphasisBrush`, `NfcSuccessTextStrongBrush`, `NfcWarningAccentBrush`, `NfcWarningBorderStrongBrush`, `NfcWarningTextMutedBrush`, `NfcWarningTextSubtleBrush`, `NfcFieldSpacing`, `NfcSpace24`, `NfcFontSize10/11/12/14`, `NfcTechnicalFontFamily`, `NfcClockIconGeometry`, `NfcEditIconGeometry`.
This is consistent with "kept for compatibility" and is listed here only so a reviewer knows which table rows are contract-only.

---

## 5. Comments and prose in the code that no longer match

### 5.1 (Low) Dangling comment in `ControlTokens.axaml`

`ControlTokens.axaml:9`:

```xml
<!-- The rest fill of a control that gains a hover fill. It has the hover colour at zero alpha, so a BrushTransition fades only alpha and never passes through white. -->
<StaticResource x:Key="Nvt.Console.LinkUnderlineBrush" ResourceKey="NfcBorderBrush" />
```

The comment describes `Nvt.Interaction.RestBrush`, which now lives at `ThemeTokens.axaml:21` / `:79`. It sits above an unrelated token and appears only in the Light dictionary. It reads as a leftover from a move. The Dark dictionary (`ControlTokens.axaml:18-29`) has no equivalent.

### 5.2 (Low) `Nvt.Menu.PopupMaxHeight` comment arithmetic does not add up

`ListTokens.axaml:21`: "Ten 32 DIP rows, the popup padding and border, and the 16 DIP shadow margin on both sides." The value on `:22` is 368.
Sum from tokens: `10 × 32` (rows, `NfcControlHeight`) + `2 × 4` (`Nvt.Menu.PopupPadding`, `ListTokens.axaml:37`) + `2 × 1` (`Nvt.Menu.PopupBorderThickness`, `:36`) + `2 × 16` (`Nvt.Menu.PopupShadowMargin`, `:28`) = 362, not 368.
The same 368 is stated in `CHANGELOG.md:11` and `Theme.md:1728`. Either the sum includes something not named (6 DIP) or the number is a rounded allowance. A one-line clarification in the comment would settle it.

### 5.3 (Low) `post-adoption-tuning` focus-margin scope list is incomplete

| Side | Location | Text |
|---|---|---|
| Doc EN | `post-adoption-tuning.md:32` | "Change `Nvt.Controls.FocusRingMargin` for exterior rings on Choice controls, Expander, GridSplitter and non-switch ToggleButton roles." |
| Doc ZH | `post-adoption-tuning.zh-TW.md:32` | Same list (「Choice 控制項、Expander、GridSplitter 與非開關 ToggleButton 角色」) |
| Code | `FormStyles.axaml:123`, `:129`, `:202` | `FormFocusRing`, `SpinnerFocusRing` (TextBox, NumericUpDown, ComboBox) read the token |
| Code | `TabStyles.axaml:67` | `TabFocusRing` reads the token |
| Code | `ToggleStyles.axaml:131` | The **switch** template's `ToggleFocusRing` also reads the token |

So the list omits form fields and tabs, and "non-switch" is wrong: the switch ring reads `Nvt.Controls.FocusRingMargin` too (and then adds `SwitchFocusWidth` / `SwitchFocusHeight` for size, as `:34` of the same doc says). The statement that Button-role rings are not affected is correct (`ButtonStyles.axaml:312`, `:322` use the literal `-4`).

The next line (`post-adoption-tuning.md:35`) says `Nvt.Controls.InsetFocusRingMargin` covers "list and menu items"; it is also read by `LogConsole/ConsoleListStyles.axaml:48`.

---

## 6. CHANGELOG

### 6.1 (Medium) Shipped Theme public API and style families have no release notes

`CHANGELOG.md` mentions Theme only at: `:11` (Unreleased, interaction states), `:42-49` (0.9.4, `actionGhost.flat`), `:241-242` (0.5.0, toggle roles, shapes, `toggleSoft`), `:307`, `:321`, `:329` (0.2.0), `:341` (0.1.0).

There is no entry for any of the following, although each is documented in `Theme.md` and present in `src/`:

| Item | Doc | Code |
|---|---|---|
| `ThemeRestFill`, `ThemeRestFills.SetRestFill` (public API) | `Theme.md:1752-1790` | `PublicAPI.Shipped.txt:336-339`, `:447`; `Theme/ThemeRestFills.cs`; `RestFillSoft.axaml`, `RestFillNone.axaml` |
| `ThemeShape`, `ThemeShapes.SetShape` (public API) | `Theme.md:990-1021` | `PublicAPI.Shipped.txt:340-343`, `:448` (0.5.0 mentions only "shared runtime Pill/Square shapes", `CHANGELOG.md:241`) |
| `ExpanderStyles`, `ProgressStyles`, `DividerStyles` (Separator, GridSplitter) | `Theme.md:506-669` | `Theme/ExpanderStyles.axaml`, `ProgressStyles.axaml`, `DividerStyles.axaml` |
| `ListStyles`, `MenuStyles` | `Theme.md:734-851` | `Theme/ListStyles.axaml`, `MenuStyles.axaml` |
| `ChoiceStyles` (CheckBox, RadioButton) | `Theme.md:1118-1270` | `Theme/ChoiceStyles.axaml` |
| `FormStyles`, `TabStyles`, `TextStyles` | `Theme.md:1458-1714` | `Theme/FormStyles.axaml`, `TabStyles.axaml`, `TextStyles.axaml` |
| `reducedMotion` class on all of the above | throughout | `ExpanderStyles.axaml:133`, `ProgressStyles.axaml`, `DividerStyles.axaml`, … |

`PublicAPI.Shipped.txt` already lists `ThemeRestFill` and `ThemeRestFills`, so the API shipped in a tagged release, but no `core-v*` section records it. The clone is shallow, so the release that introduced them cannot be named from git here. `CHANGELOG.md:3` says the GitHub Release of each tag "carries the same notes", so the gap would also be in the Release notes.

### 6.2 What matches

The Unreleased bullet (`CHANGELOG.md:11`) matches the code on every checkable claim: hover fill `#DCE5EF` / `#26364D` (`ThemeTokens.axaml:20`, `:78`), zero-alpha `Nvt.Interaction.RestBrush` (`:21`, `:79`), fixed `Nvt.Controls.SelectedBrush` (`ControlTokens.axaml:6`, `:19`), checked menu rows without a fill (`MenuStyles.axaml`, no `:checked` background rule), `Nvt.Menu.PopupMaxHeight` 368 (`ListTokens.axaml:22`) and `Nvt.Console.LinkUnderlineBrush` = `NfcBorderBrush` (`ControlTokens.axaml:10`, `:22`). The 0.9.4 `actionGhost.flat` entry (`CHANGELOG.md:42-49`) matches `ButtonStyles.axaml:266-294` and `Theme.md:168-172`, `:180`. The "Breaking changes since core-v0.1.0" table (`Theme.md:199-225`) matches the 0.2.0 entry (`CHANGELOG.md:321`) and no removed class survives in `src/` (searched for `semanticAction`, `railAction`, `iconButton`, `summaryChip`, `browseAction`, `inlineEdit`, `NfcSemanticButtonTheme`, `confirmDialogActionButton`).

---

## 7. Other docs that mention Theme

### 7.1 (Low) `docs/components.md:64` still says the NFC legacy font resources are in an open pull request

`docs/components.md:64`: "Role table decided; NFC's legacy font resources in an open pull request."
`CHANGELOG.md:341` (0.1.0) lists "NFC's eight legacy font resources (#17)" as shipped, `NfcLegacyFontTokens.axaml` exists in `src/`, and `Theme.md:435-476` documents it. The status text is stale.

### 7.2 Checked and consistent

`docs/core/api-inventory.md:72` says `Nvt.Core.Avalonia.Theme` has 5 public types and 17 members. `PublicAPI.Shipped.txt` has 5 types (`ThemeRestFill`, `ThemeRestFills`, `ThemeShape`, `ThemeShapes`, `UiResourceResolver`) and 17 entries (`:336-344`, `:447-454`). `Theme.md:855-863` lists all six resolver methods that exist in `UiResourceResolver.cs:27-130`.

---

## 8. Minor completeness note

### 8.1 (Low) The top-of-file include example omits most style files

`Theme.md:7-17` (ZH same) shows `StyleInclude` for `ButtonStyles`, `ScrollStyles`, `ToggleStyles`, `FormStyles`, `ListStyles`, `TabStyles`, `TextStyles`. It does not list `ChoiceStyles`, `ExpanderStyles`, `DividerStyles`, `ProgressStyles` or `MenuStyles`. Each is introduced in its own section with its own `Load ... after Fluent` sentence, so this is a completeness point, not an error. The example also loads `ListStyles` without `MenuStyles` although `ListStyles.axaml` and `MenuStyles.axaml` are presented together at `Theme.md:825-826`.

---

## 9. Coverage: what was checked and found consistent

Method: parse every `x:Key` in `src/**/*.axaml`, resolve `StaticResource` aliases per theme variant, then compare against each table row in both Theme docs by header position (`Light` / `Dark` / `Pill` / `Square`).

- **Palette (`Theme.md:25-77`)**: 14 neutral and 31 semantic rows, Light and Dark, all equal `ThemeTokens.axaml:17-72` and `:75-130`. Includes `NfcModalScrimBrush` `#660F172A` / `#B30B1220`, the focus brushes and `Nvt.Button.PrimaryLabelBrush`.
- **Accents (`Theme.md:94-114`)**: NFC rows equal `ThemeTokens.axaml:31-37`, `:89-95`. NFH and NFU rows are not defined in this repo; they equal the `InlineData` in `PaletteContrastTests.cs:32-37` and `ButtonThemeTests.cs:510-511`.
- **Sizes and radii (`Theme.md:120-130`)**: `NfcControlHeight` 32, 6 / 8 / 999, 14,0 padding, `NfcSpace*`, `NfcFieldSpacing` 4: equal `ThemeTokens.axaml:133-148` and `ButtonStyles.axaml:12-26`.
- **Legacy fonts (`Theme.md:441-450`)**: eight rows equal `NfcLegacyFontTokens.axaml:4-11`.
- **Button roles table (`Theme.md:138-176`)**: each role/state/colour triple matches `ButtonStyles.axaml:108-294`, including disabled priority (`:258-263`), `flat` (`:266-294`), primary pressed overlay (`:296-298`), 120 ms transitions (`:48-50`), focus ring `Margin=-4` and red ring selector `:not(.chipAction)` (`:308-326`), and the `FocusRingRadiusConverter` rule "999 stays, otherwise +4".
- **Toggle (`Theme.md:909-1116`)**: state table, danger, switch geometry (`Nvt.Toggle.*` equal `ToggleTokens.axaml`), pressed `scale(0.98)`, focus ring colours (solid roles `Nvt.Focus.RingBrush`, `toggleSoft` `Nvt.Controls.FocusBrush`), `toggleSoft` outline 1 DIP / 2 DIP on hover (`ToggleStyles.axaml:251-257`).
- **Choice (`Theme.md:1148-1196`)**: all nine states match `ChoiceStyles.axaml`; `ChoiceTokens.axaml` has exactly the 14 geometry tokens the doc states; rest-fill `Tag=None` rules at `ChoiceStyles.axaml:124` and `:136`.
- **Expander, ProgressBar, Separator, GridSplitter (`Theme.md:506-669`)**: heights 32 / 44, padding, chevron 12 × 6 / 20 / 10, 150 ms, progress 6 / 3 / 10, grip 4 × 24, hit 6, line 1 / 2 / 3, preview 0.5, disabled colours: all match `ExpanderStyles.axaml`, `DividerStyles.axaml`, `ProgressStyles.axaml` and their token files, apart from 1.1 above.
- **List, Menu (`Theme.md:734-851`)**: row 32 / compact 24, padding, marker 2 × 12 at 4 DIP, inset ring 2 DIP, disabled colours, check 12 in a 20 slot, 8 gap, separator 1 / `10,4`, shadows `0 4 12 0 #26000000` / `#66000000`, `MenuItem Header="-"` via `MenuItem:separator`: match.
- **Forms, Tabs, Text (`Theme.md:1458-1714`)**: 32 palette rows and 33 geometry rows equal `FormTokens.axaml` and `TabTokens.axaml`; error precedence, `formEmbedded`, `readOnly`, pressed/open, multiline 6 / 10 corners, tab placements, text-class table equals `FontRoles.axaml:5-44` (sizes 24 / 16 / 13 / 11, weights SemiBold / Normal).
- **Shape tables (`Theme.md:1012-1017`, `:1295-1301`)** equal `ShapePill.axaml` and `ShapeSquare.axaml`. `ThemeTokens.axaml` merges the Pill file by default (`:10`).
- **Interaction, rest-fill (`Theme.md:1720-1728`, `:1784-1794`)**: equal `ThemeTokens.axaml`, `ControlTokens.axaml:32-34`, `RestFillNone.axaml:5`, `RestFillSoft.axaml`.
- **Contrast ratios**: 35 pair-groups were recomputed with WCAG relative luminance from the axaml colours (hover text, focus ring on hover / pressed / selected, selected text rest / hover / pressed, placeholder, disabled text, text-class rows, toggle white-on-fill, form border / error, progress, splitter grip, divider, choice labels, `#0B1220` / `#FFFFFF` on Expander pressed). All matched to three decimals except the label question in 1.3 and the scope question in 1.2. The 8,232-pair count matches `RestFillTests.cs:487`; the full set of rest-fill and form tables was not recomputed.
- **Role migration (`Theme.md:242-430`)**: 59 / 105 / 22 rows as stated; EN and ZH rows identical in all three columns (only the "product role, kept in the tool" phrase is localised).
- **Dialogs and Panels consumers (`Theme.md:433`)**: `ConfirmDialog.axaml:31,46` and `.axaml.cs:64-65` (cancel switches `actionNeutral` → `actionDanger`), `WarningDialog.axaml:36` (`actionPrimary`), `PanelsStyles.axaml:11` (`actionGhost`), `DialogsStyles.axaml:12` (`Button.actionDanger Path.confirmDialogDangerIcon`) match.

## 10. Not verified

- Anything that depends on another repository or an old commit: the `085f71c`, `847cc45`, `60e3f28e`, `0f291437`, `c6c50c1c`, `98f4e3a5` references, NFC `ThemeTokens.axaml` lines 218-225, NFC `MainWindowControlStyles.axaml` lines 117-185, NFH `Controls.Scroll.axaml` lines 8-94.
- The "Before" values in `Theme.md:722-730` (NFH scroll bar), `:1745-1750` ("Before" contrast column) and `:1722` ("It was `#E8EEF5` / `#1E293B`").
- Rendered-image claims (sheet sizes, "below 1 MB", pixel comparisons, "zero differing pixels") and anything that needs a running Avalonia host.
- Contrast rows that were not recomputed: most of `Theme.md:1816-1861` (rest-fill table) and the full `:1595-1640` form/tab set beyond the sampled pairs.
- Which release first shipped each Theme style family or `ThemeRestFill` (shallow clone; no tags fetched).
